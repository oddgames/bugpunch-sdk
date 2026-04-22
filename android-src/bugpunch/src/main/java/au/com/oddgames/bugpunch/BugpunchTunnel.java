package au.com.oddgames.bugpunch;

import android.os.Handler;
import android.os.HandlerThread;
import android.util.Log;

import com.neovisionaries.ws.client.WebSocket;
import com.neovisionaries.ws.client.WebSocketAdapter;
import com.neovisionaries.ws.client.WebSocketException;
import com.neovisionaries.ws.client.WebSocketFactory;
import com.neovisionaries.ws.client.WebSocketFrame;

import org.json.JSONObject;

/**
 * Native WebSocket tunnel — N1 of the migration plan in
 * {@code project_native_tunnel.md}.
 *
 * <p>Owns the persistent connection from the SDK to the Bugpunch server.
 * Starts from {@link BugpunchRuntime#startEarly(android.content.Context, String)}
 * — so the tunnel is alive before Unity boots — and outlives any
 * Mono/IL2CPP crash because nothing on this path touches the managed heap.
 *
 * <p>Scope for N1 is intentionally narrow: connect + register + heartbeat +
 * reconnect, capturing the signed pin config from the {@code registered}
 * ack. Incoming {@code request} frames (Remote IDE) are parked until N3
 * adds the native→C# dispatch bridge; {@code log} frames go native in N5.
 *
 * <p>Opt-in for now. The existing C# {@code TunnelClient} remains in place
 * and handles Remote IDE traffic. Until N3 lands, connecting both would
 * thrash the server's per-deviceId reconnect logic, so
 * {@link BugpunchConfig#useNativeTunnel} (field on the bundled JSON)
 * controls whether this class actually opens a socket.
 */
public final class BugpunchTunnel {
    private static final String TAG = "BugpunchTunnel";

    // Exponential backoff: 1s, 2s, 4s, 8s, 16s, then capped.
    private static final long BACKOFF_INITIAL_MS = 1_000L;
    private static final long BACKOFF_MAX_MS = 30_000L;

    // Server's handshake timeout is 10s — keep ours comfortably under that.
    private static final long HANDSHAKE_BUDGET_MS = 8_000L;

    // Application-layer heartbeat. Server replies with { type: "pong" }.
    private static final long HEARTBEAT_INTERVAL_MS = 10_000L;

    private static BugpunchTunnel sInstance;

    /**
     * Start the native tunnel with the given startup config. Called from
     * {@link BugpunchRuntime#startEarly} — fires before Unity boots.
     * Idempotent; subsequent calls no-op.
     */
    public static synchronized void start(JSONObject config, String stableDeviceId) {
        if (sInstance != null) return;
        if (config == null) return;
        String serverUrl = config.optString("serverUrl", "");
        String apiKey = config.optString("apiKey", "");
        if (serverUrl.isEmpty() || apiKey.isEmpty()) {
            Log.w(TAG, "missing serverUrl or apiKey — tunnel not started");
            return;
        }
        sInstance = new BugpunchTunnel(config, stableDeviceId);
        sInstance.connect();
    }

    /** Graceful shutdown — used by tests and process teardown. */
    public static synchronized void stop() {
        if (sInstance == null) return;
        sInstance.close();
        sInstance = null;
    }

    public static synchronized boolean isConnected() {
        return sInstance != null && sInstance.mConnected;
    }

    /** Persistent deviceId the native tunnel is registered under. Empty if unstarted. */
    public static synchronized String getDeviceId() {
        return sInstance != null ? sInstance.mDeviceId : "";
    }

    /**
     * Most recent signed pin config received from the server in a
     * {@code registered} ack or {@code pinUpdate} frame, or null if we
     * haven't successfully registered yet. Exposed so the N4 native pin
     * handler (HMAC verify + cache) can read it without threading hooks
     * through every call site.
     */
    public static synchronized String getLastPinConfigJson() {
        return sInstance != null ? sInstance.mLastPinConfigJson : null;
    }

    /**
     * Ship a response frame back to the server. Used by the N3 dispatch
     * bridge: C# answers an incoming {@code request} (routed via
     * {@code UnitySendMessage}) by calling this with a pre-built JSON
     * envelope of shape
     * {@code {type:"response", requestId, status, body, contentType, isBase64?}}.
     * Thread-safe.
     */
    public static synchronized void sendResponse(String json) {
        if (sInstance == null || json == null) return;
        WebSocket s = sInstance.mSocket;
        if (s == null || !s.isOpen()) {
            Log.w(TAG, "sendResponse dropped — tunnel not connected");
            return;
        }
        s.sendText(json);
    }

    // ── N4: native pin state accessors ──
    //
    // Pins only apply when consent == "accepted". Server-side consent_status
    // lives on the devices row keyed by stable_device_id so reinstalls pick
    // up the same decision. Cold-start reads come from SharedPreferences,
    // the tunnel handshake refreshes with the server's current state.

    public static boolean isAlwaysLog() {
        return sInstance != null && "accepted".equals(sInstance.mConsent) && sInstance.mPinAlwaysLog;
    }
    public static boolean isAlwaysRemote() {
        return sInstance != null && "accepted".equals(sInstance.mConsent) && sInstance.mPinAlwaysRemote;
    }
    public static boolean isAlwaysDebug() {
        return sInstance != null && "accepted".equals(sInstance.mConsent) && sInstance.mPinAlwaysDebug;
    }
    public static String getConsent() {
        return sInstance != null ? sInstance.mConsent : "unknown";
    }

    /**
     * N5: tee a log line into the native log sink. BugpunchLogReader calls
     * this for every captured logcat line; we buffer and flush as a single
     * WebSocket frame on a 100 ms / 32 KB cadence. Dropped silently when
     * alwaysLog is off or the tunnel isn't connected. Thread-safe.
     */
    public static void enqueueLogLine(String line) {
        BugpunchTunnel t = sInstance;
        if (t == null || line == null) return;
        if (!t.mConnected) return;
        if (!("accepted".equals(t.mConsent) && t.mPinAlwaysLog)) return;

        // Phase 6c: redact before the line enters the batcher so nothing
        // matching a configured pattern ever leaves the process.
        String redacted = t.redact(line);

        synchronized (t.mLogBufLock) {
            t.mLogBuf.append(redacted);
            t.mLogBuf.append('\n');
            boolean overflow = t.mLogBuf.length() >= LOG_FLUSH_BYTES;
            if (overflow) {
                // Flush immediately — don't wait for the scheduled tick.
                t.mWorker.post(t::flushLogs);
                return;
            }
            if (!t.mLogFlushScheduled) {
                t.mLogFlushScheduled = true;
                t.mWorker.postDelayed(t::flushLogs, LOG_FLUSH_INTERVAL_MS);
            }
        }
    }

    private void flushLogs() {
        String text;
        synchronized (mLogBufLock) {
            mLogFlushScheduled = false;
            if (mLogBuf.length() == 0) return;
            text = mLogBuf.toString();
            mLogBuf.setLength(0);
        }
        WebSocket s = mSocket;
        if (s == null || !mConnected) return;

        // Hand-built JSON envelope — the raw log text may contain quotes /
        // backslashes / newlines that we want shipped verbatim (server writes
        // raw bytes to disk). Minimum necessary escaping.
        StringBuilder out = new StringBuilder(text.length() + 128);
        out.append("{\"type\":\"log\",\"sessionId\":\"")
           .append(BugpunchRuntime.getSessionId())
           .append("\",\"text\":\"");
        for (int i = 0; i < text.length(); i++) {
            char c = text.charAt(i);
            switch (c) {
                case '\\': out.append("\\\\"); break;
                case '"':  out.append("\\\""); break;
                case '\n': out.append("\\n"); break;
                case '\r': out.append("\\r"); break;
                case '\t': out.append("\\t"); break;
                default:
                    if (c < 0x20) out.append(String.format("\\u%04x", (int) c));
                    else out.append(c);
            }
        }
        out.append("\"}");
        s.sendText(out.toString());
        mLastLogFlushMs = System.currentTimeMillis();
    }

    private void loadCachedPins() {
        try {
            android.content.Context ctx = BugpunchRuntime.getAppContext();
            if (ctx == null) return;
            android.content.SharedPreferences prefs =
                ctx.getSharedPreferences(PREFS_FILE, android.content.Context.MODE_PRIVATE);
            mPinAlwaysLog    = prefs.getBoolean("alwaysLog", false);
            mPinAlwaysRemote = prefs.getBoolean("alwaysRemote", false);
            mPinAlwaysDebug  = prefs.getBoolean("alwaysDebug", false);
            mConsent         = prefs.getString("consent", "unknown");
        } catch (Throwable t) {
            Log.w(TAG, "loadCachedPins failed", t);
        }
    }

    /**
     * Parse a server-delivered pin config blob ({ pins: { alwaysLog,
     * alwaysRemote, alwaysDebug }, consent, issuedAt, sig }) into in-memory
     * state and mirror to SharedPreferences. HMAC verification of `sig`
     * against the bundled pin_signing_secret is TODO — captured in memory
     * for the follow-up N4.2 task. For now we trust the tunnel (API-key
     * authenticated over TLS) the way Phase 3c did.
     */
    private void applyPinConfig(JSONObject pin) {
        JSONObject pins = pin.optJSONObject("pins");
        boolean log    = pins != null && pins.optBoolean("alwaysLog", false);
        boolean remote = pins != null && pins.optBoolean("alwaysRemote", false);
        boolean debug  = pins != null && pins.optBoolean("alwaysDebug", false);
        String consent = pin.optString("consent", "unknown");

        mPinAlwaysLog    = log;
        mPinAlwaysRemote = remote;
        mPinAlwaysDebug  = debug;
        mConsent         = consent;

        try {
            android.content.Context ctx = BugpunchRuntime.getAppContext();
            if (ctx == null) return;
            ctx.getSharedPreferences(PREFS_FILE, android.content.Context.MODE_PRIVATE)
                .edit()
                .putBoolean("alwaysLog", log)
                .putBoolean("alwaysRemote", remote)
                .putBoolean("alwaysDebug", debug)
                .putString("consent", consent)
                .apply();
        } catch (Throwable t) {
            Log.w(TAG, "pin cache write failed", t);
        }
    }

    // ── Instance state ──

    private final JSONObject mConfig;
    private final String mStableDeviceId;
    private final String mServerUrl;
    private final String mApiKey;
    private final String mBuildChannel;
    private final WebSocketFactory mFactory;
    private final Handler mWorker;
    private WebSocket mSocket;
    private volatile boolean mConnected;
    private volatile boolean mStopRequested;
    private long mBackoffMs = BACKOFF_INITIAL_MS;
    private String mLastPinConfigJson;

    // Parsed pin state, mirrored to SharedPreferences so a cold start
    // applies the last-known pins before the tunnel handshake completes.
    // Consent is the gate: unknown/declined → pins all read false regardless.
    private static final String PREFS_FILE = "bugpunch_pins";
    private volatile boolean mPinAlwaysLog;
    private volatile boolean mPinAlwaysRemote;
    private volatile boolean mPinAlwaysDebug;
    private volatile String mConsent = "unknown";

    // N5: log sink batcher. BugpunchLogReader tees every line here when the
    // alwaysLog pin is active; flush every 100 ms or when the buffer hits
    // ~32 KB, whichever first. Raw UTF-8 bytes on the wire with `\n` as
    // line separator — server writes them straight to disk with no parsing.
    private static final int LOG_FLUSH_BYTES = 32 * 1024;
    private static final long LOG_FLUSH_INTERVAL_MS = 100L;
    private final StringBuilder mLogBuf = new StringBuilder(LOG_FLUSH_BYTES);
    private final Object mLogBufLock = new Object();
    private boolean mLogFlushScheduled;
    private long mLastLogFlushMs;

    // Phase 6c: compiled redaction rules. Applied to every log line before
    // it enters the batcher; each match → [redacted:NAME]. Compiled once at
    // tunnel start from the bundled config.
    private static final class RedactionRule {
        final String name;
        final java.util.regex.Pattern pattern;
        RedactionRule(String n, java.util.regex.Pattern p) { name = n; pattern = p; }
    }
    private java.util.List<RedactionRule> mRedactionRules = java.util.Collections.emptyList();

    // Persistent deviceId shared with the C# TunnelClient so reconnects reuse
    // the same server-side slot — avoids deviceId churn during the migration.
    private static final String DEVICE_ID_FILE = "bugpunch_device_id";
    private final String mDeviceId;

    // Per-project HMAC secret baked into the bundled config at build time by
    // BugpunchConfigBundle (N4.2). Empty when the build-time fetch failed —
    // in that case we refuse every pin config instead of trusting the tunnel.
    private final String mPinSigningSecret;

    private BugpunchTunnel(JSONObject config, String stableDeviceId) {
        mConfig = config;
        mStableDeviceId = stableDeviceId != null ? stableDeviceId : "";
        mServerUrl = config.optString("serverUrl", "");
        mApiKey = config.optString("apiKey", "");
        mBuildChannel = config.optString("buildChannel", "unknown");
        mPinSigningSecret = config.optString("pinSigningSecret", "");
        mDeviceId = loadOrMintDeviceId();

        mFactory = new WebSocketFactory()
            .setConnectionTimeout((int) HANDSHAKE_BUDGET_MS);

        HandlerThread t = new HandlerThread("bugpunch-tunnel");
        t.start();
        mWorker = new Handler(t.getLooper());

        // Apply cached pins immediately so a cold start enforces the
        // last-known state before the handshake completes.
        loadCachedPins();

        // Compile redaction rules once from the bundled config. Bad patterns
        // are logged and skipped — a typo in one rule can't kill the others.
        compileRedactionRules();
    }

    private void compileRedactionRules() {
        org.json.JSONArray arr = mConfig.optJSONArray("logRedactionRules");
        if (arr == null || arr.length() == 0) {
            mRedactionRules = java.util.Collections.emptyList();
            return;
        }
        java.util.ArrayList<RedactionRule> compiled = new java.util.ArrayList<>(arr.length());
        for (int i = 0; i < arr.length(); i++) {
            org.json.JSONObject r = arr.optJSONObject(i);
            if (r == null) continue;
            String pat = r.optString("pattern", "");
            if (pat.isEmpty()) continue;
            String name = r.optString("name", "pii");
            try {
                compiled.add(new RedactionRule(name, java.util.regex.Pattern.compile(pat)));
            } catch (java.util.regex.PatternSyntaxException e) {
                Log.w(TAG, "skipping bad redaction pattern \"" + pat + "\": " + e.getMessage());
            }
        }
        mRedactionRules = compiled;
    }

    /** Apply every compiled redaction rule to the line. */
    private String redact(String line) {
        if (mRedactionRules.isEmpty() || line == null) return line;
        String out = line;
        for (RedactionRule r : mRedactionRules) {
            out = r.pattern.matcher(out).replaceAll("[redacted:" + r.name + "]");
        }
        return out;
    }

    private String loadOrMintDeviceId() {
        try {
            android.content.Context ctx = BugpunchRuntime.getAppContext();
            if (ctx == null) return java.util.UUID.randomUUID().toString();
            java.io.File f = new java.io.File(ctx.getFilesDir(), DEVICE_ID_FILE);
            if (f.exists()) {
                byte[] b = new byte[(int) f.length()];
                try (java.io.FileInputStream in = new java.io.FileInputStream(f)) {
                    int r = in.read(b);
                    if (r > 0) return new String(b, 0, r, java.nio.charset.StandardCharsets.UTF_8).trim();
                }
            }
            String id = java.util.UUID.randomUUID().toString();
            try (java.io.FileOutputStream out = new java.io.FileOutputStream(f)) {
                out.write(id.getBytes(java.nio.charset.StandardCharsets.UTF_8));
            }
            return id;
        } catch (Throwable t) {
            return java.util.UUID.randomUUID().toString();
        }
    }

    private void connect() {
        if (mStopRequested) return;
        if (mServerUrl.isEmpty()) {
            Log.w(TAG, "no serverUrl in config — aborting");
            return;
        }
        String wsUrl = toWsUrl(mServerUrl) + "/api/devices/tunnel";
        try {
            WebSocket ws = mFactory.createSocket(wsUrl);
            // Library-level ping every 10 s keeps the socket alive through
            // carrier-grade NAT and idle proxies. Our application-layer
            // {"type":"heartbeat"} still rides over this.
            ws.setPingInterval(HEARTBEAT_INTERVAL_MS);
            ws.addListener(new Listener());
            mSocket = ws;
            ws.connectAsynchronously();
            Log.i(TAG, "connecting to " + wsUrl);
        } catch (java.io.IOException e) {
            Log.w(TAG, "connect failed: " + e.getMessage());
            scheduleReconnect();
        }
    }

    private static String toWsUrl(String base) {
        String s = base.trim();
        if (s.endsWith("/")) s = s.substring(0, s.length() - 1);
        if (s.startsWith("https://")) return "wss://" + s.substring("https://".length());
        if (s.startsWith("http://"))  return "ws://"  + s.substring("http://".length());
        return s;
    }

    private void sendRegister() {
        JSONObject reg = new JSONObject();
        try {
            reg.put("type", "register");
            reg.put("name", mConfig.optJSONObject("metadata") != null
                ? mConfig.optJSONObject("metadata").optString("deviceModel", "Android")
                : "Android");
            reg.put("platform", "Android");
            reg.put("appVersion", mConfig.optJSONObject("metadata") != null
                ? mConfig.optJSONObject("metadata").optString("appVersion", "")
                : "");
            reg.put("remoteIdePort", 0);
            reg.put("token", mApiKey);
            reg.put("deviceId", mDeviceId);
            reg.put("stableDeviceId", mStableDeviceId);
            reg.put("buildChannel", mBuildChannel);
        } catch (org.json.JSONException e) {
            Log.w(TAG, "register payload build failed", e);
            return;
        }
        WebSocket s = mSocket;
        if (s != null) s.sendText(reg.toString());
    }

    private void scheduleReconnect() {
        if (mStopRequested) return;
        long delay = mBackoffMs;
        mBackoffMs = Math.min(mBackoffMs * 2, BACKOFF_MAX_MS);
        Log.i(TAG, "reconnecting in " + delay + "ms");
        mWorker.postDelayed(this::connect, delay);
    }

    private void close() {
        mStopRequested = true;
        WebSocket s = mSocket;
        if (s != null) {
            try { s.disconnect(1000, "shutdown"); } catch (Throwable ignore) {}
        }
        mSocket = null;
        mConnected = false;
    }

    // ── Listener (nv-websocket-client WebSocketAdapter) ──

    private final class Listener extends WebSocketAdapter {
        @Override public void onConnected(WebSocket ws, java.util.Map<String, java.util.List<String>> headers) {
            mConnected = true;
            mBackoffMs = BACKOFF_INITIAL_MS;
            Log.i(TAG, "connected");
            sendRegister();
        }

        @Override public void onTextMessage(WebSocket ws, String text) {
            try {
                JSONObject msg = new JSONObject(text);
                String type = msg.optString("type", "");
                switch (type) {
                    case "registered": {
                        JSONObject pin = msg.optJSONObject("pinConfig");
                        if (pin != null) {
                            mLastPinConfigJson = pin.toString();
                            applyPinConfig(pin);
                        }
                        Log.i(TAG, "registered (pinConfig=" + (pin != null) + ")");
                        break;
                    }
                    case "pinUpdate": {
                        JSONObject pin = msg.optJSONObject("config");
                        if (pin != null) {
                            mLastPinConfigJson = pin.toString();
                            applyPinConfig(pin);
                        }
                        Log.i(TAG, "pinUpdate received");
                        break;
                    }
                    case "request": {
                        // N3: marshal to C# so the existing RequestRouter
                        // (HierarchyService / InspectorService / ScriptRunner
                        // / SceneCameraService / etc.) answers. Response
                        // comes back via BugpunchTunnel.sendResponse.
                        try {
                            Class<?> up = Class.forName("com.unity3d.player.UnityPlayer");
                            up.getMethod("UnitySendMessage", String.class, String.class, String.class)
                                .invoke(null, "[Bugpunch Client]", "OnTunnelRequest", text);
                        } catch (Throwable t) {
                            Log.w(TAG, "UnitySendMessage dispatch failed", t);
                        }
                        break;
                    }
                    case "pong":
                    case "heartbeat":
                        break;
                    default:
                        Log.v(TAG, "unhandled frame type=" + type);
                }
            } catch (org.json.JSONException e) {
                Log.w(TAG, "malformed frame: " + text, e);
            }
        }

        @Override public void onDisconnected(WebSocket ws, WebSocketFrame serverFrame,
                                             WebSocketFrame clientFrame, boolean closedByServer) {
            mConnected = false;
            int code = serverFrame != null ? serverFrame.getCloseCode()
                     : clientFrame != null ? clientFrame.getCloseCode() : 0;
            Log.i(TAG, "disconnected (code=" + code + " byServer=" + closedByServer + ")");
            // 4000 / 4001 are permanent server-side rejections (missing register
            // fields / invalid token). Retrying just tight-loops the same
            // rejection forever. Stop and let the next app launch (with hopefully
            // fresher config) try again.
            if (code == 4000 || code == 4001) {
                Log.w(TAG, "permanent rejection (code=" + code + ") — not reconnecting. " +
                           "Check your API key + that BugpunchConfigBundle baked metadata.appVersion.");
                mStopRequested = true;
                return;
            }
            scheduleReconnect();
        }

        @Override public void onError(WebSocket ws, WebSocketException cause) {
            Log.w(TAG, "tunnel error: " + cause.getMessage());
            // nv-websocket-client fires onDisconnected for connection failures;
            // we avoid double-scheduling reconnect here.
        }
    }
}
