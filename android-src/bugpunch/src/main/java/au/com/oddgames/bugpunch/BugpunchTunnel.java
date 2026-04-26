package au.com.oddgames.bugpunch;

import android.os.Handler;
import android.os.HandlerThread;
import android.util.Log;

import com.neovisionaries.ws.client.WebSocket;
import com.neovisionaries.ws.client.WebSocketAdapter;
import com.neovisionaries.ws.client.WebSocketException;
import com.neovisionaries.ws.client.WebSocketFactory;
import com.neovisionaries.ws.client.WebSocketFrame;

import org.json.JSONException;
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
    private static final String TAG = "[Bugpunch.Tunnel]";

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
     */
    public static synchronized void start(JSONObject config, String stableDeviceId) {
        if (config == null) return;
        if (sInstance != null) {
            sInstance.updateConfig(config);
            return;
        }
        sInstance = new BugpunchTunnel(config, stableDeviceId);
        sInstance.connect();
    }

    /**
     * Merge richer config values into the tunnel (e.g. after Unity boots and
     * provides appVersion/useNativeTunnel). If the tunnel was previously
     * disabled or permanently rejected due to missing metadata, this triggers
     * a fresh connection attempt.
     */
    public void updateConfig(JSONObject config) {
        if (config == null) return;
        mConfig = config;
        mServerUrl = config.optString("serverUrl", mServerUrl);
        mApiKey = config.optString("apiKey", mApiKey);

        if (mConfig.optBoolean("useNativeTunnel", false) && !mConnected && mSocket == null) {
            Log.i(TAG, "config updated — re-evaluating tunnel connection");
            mStopRequested = false;
            mBackoffMs = BACKOFF_INITIAL_MS;
            connect();
        }
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

    // ── Standardized response envelope builders ──
    // Every native service that answers a server request should use these
    // instead of rolling its own JSON so the tunnel always ships the same
    // envelope shape.  The server expects:
    //   { type:"response", requestId, status, body, contentType }

    public static String buildResponse(String requestId, int status, String body, String contentType) {
        try {
            JSONObject r = new JSONObject();
            r.put("type", "response");
            r.put("requestId", requestId != null ? requestId : "");
            r.put("status", status);
            r.put("body", body != null ? body : "");
            r.put("contentType", contentType != null ? contentType : "application/json");
            return r.toString();
        } catch (JSONException e) {
            return "{\"type\":\"response\",\"requestId\":" + JSONObject.quote(requestId != null ? requestId : "") + ",\"status\":500,\"body\":\"\",\"contentType\":\"application/json\"}";
        }
    }

    public static String buildBinaryResponse(String requestId, String base64Body, String contentType) {
        try {
            JSONObject r = new JSONObject();
            r.put("type", "response");
            r.put("requestId", requestId != null ? requestId : "");
            r.put("status", 200);
            r.put("body", base64Body != null ? base64Body : "");
            r.put("contentType", contentType != null ? contentType : "application/octet-stream");
            r.put("isBase64", true);
            return r.toString();
        } catch (JSONException e) {
            return "{\"type\":\"response\",\"requestId\":" + JSONObject.quote(requestId != null ? requestId : "") + ",\"status\":500,\"body\":\"\",\"contentType\":\"application/json\"}";
        }
    }

    public static String buildErrorResponse(String requestId, int status, String message) {
        try {
            JSONObject err = new JSONObject();
            err.put("error", message != null ? message : "");
            JSONObject r = new JSONObject();
            r.put("type", "response");
            r.put("requestId", requestId != null ? requestId : "");
            r.put("status", status);
            r.put("body", err.toString());
            r.put("contentType", "application/json");
            return r.toString();
        } catch (JSONException e) {
            return "{\"type\":\"response\",\"requestId\":" + JSONObject.quote(requestId != null ? requestId : "") + ",\"status\":500,\"body\":\"{\\\"error\\\":\\\"\\\"}\",\"contentType\":\"application/json\"}";
        }
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
        if (sInstance == null || json == null) {
            Log.w(TAG, "sendResponse DROP null instance or json");
            return;
        }
        WebSocket s = sInstance.mSocket;
        if (s == null || !s.isOpen()) {
            Log.w(TAG, "sendResponse DROP tunnel not connected isOpen=" + (s != null ? s.isOpen() : false));
            return;
        }
        Log.i(TAG, "sendResponse ENQUEUE jsonLen=" + json.length() + " preview=" + json.substring(0, Math.min(200, json.length())));
        try {
            s.sendText(json);
            Log.i(TAG, "sendResponse SENT jsonLen=" + json.length());
        } catch (Exception e) {
            Log.e(TAG, "sendResponse EXCEPTION: " + e.getMessage());
            BugpunchSdkErrorOverlay.reportThrowable("BugpunchTunnel", "sendResponse", e);
        }
    }

    /**
     * Send an async event to the server (no request/response pairing).
     * Used for WebRTC ICE candidates and other realtime signals.
     */
    public static synchronized void sendEvent(String event, String dataJson) {
        if (sInstance == null) {
            Log.w(TAG, "sendEvent DROP null instance event=" + event);
            return;
        }
        WebSocket s = sInstance.mSocket;
        if (s == null || !s.isOpen()) {
            Log.w(TAG, "sendEvent DROP tunnel not connected event=" + event + " isOpen=" + (s != null ? s.isOpen() : false));
            return;
        }
        try {
            JSONObject msg = new JSONObject();
            msg.put("type", "event");
            msg.put("event", event != null ? event : "");
            // Ensure data is JSON object if possible, else send as string
            if (dataJson != null && dataJson.startsWith("{") && dataJson.endsWith("}")) {
                msg.put("data", new JSONObject(dataJson));
            } else {
                msg.put("data", dataJson != null ? dataJson : "");
            }

            String out = msg.toString();
            Log.i(TAG, "sendEvent ENQUEUE event=" + event + " len=" + out.length());
            s.sendText(out);
            Log.i(TAG, "sendEvent SENT event=" + event);
        } catch (Exception e) {
            Log.e(TAG, "sendEvent EXCEPTION event=" + event + " err=" + e.getMessage());
            BugpunchSdkErrorOverlay.reportThrowable("BugpunchTunnel", "sendEvent(" + event + ")", e);
        }
    }

    // ── Native tester-role accessors ──
    //
    // Role lives on the devices row keyed by stable_device_id (reinstall-safe)
    // and is delivered at tunnel handshake + poll response time as a signed
    // roleConfig blob. Cold-start reads come from SharedPreferences; the
    // handshake refreshes with the server's current state.

    /** "internal" | "external" | "public". Defaults to "public" if unknown. */
    public static String getTesterRole() {
        return sInstance != null ? sInstance.mTesterRole : "public";
    }

    /** Internal = ambient log + Remote IDE + startup debug prompt. */
    public static boolean isInternal() {
        return sInstance != null && "internal".equals(sInstance.mTesterRole);
    }

    /** Internal or external — both get the startup debug-mode consent prompt. */
    public static boolean isTester() {
        return sInstance != null && !"public".equals(sInstance.mTesterRole);
    }

    // Back-compat shims retained so existing callers (log reader, debug-mode
    // gates) read derived state off the role. Ambient alwaysDebug is gone —
    // the video ring now runs only when the user accepts the startup prompt.
    public static boolean isAlwaysLog()    { return isInternal(); }
    public static boolean isAlwaysRemote() { return isInternal(); }

    /**
     * Apply a signed role config received outside the WebSocket handshake
     * (currently the native HTTP poll path). The WebSocket handshake remains
     * the hot path for live updates once the report tunnel is connected.
     */
    public static synchronized void applyRoleConfig(JSONObject cfg) {
        if (cfg == null) return;
        if (sInstance != null) {
            sInstance.mLastPinConfigJson = cfg.toString();
            sInstance.applyRoleConfigInternal(cfg);
            return;
        }

        android.content.Context ctx = BugpunchRuntime.getAppContext();
        if (ctx == null) return;
        String role = normalizeRole(cfg.optString("role", "public"));
        try {
            ctx.getSharedPreferences(PREFS_FILE, android.content.Context.MODE_PRIVATE)
                .edit()
                .putString("role", role)
                .apply();
        } catch (Throwable t) {
            Log.w(TAG, "role cache write failed", t);
        }
    }

    private static String normalizeRole(String raw) {
        if (raw == null) return "public";
        switch (raw) {
            case "internal":
            case "admin":     // legacy
            case "developer": // legacy
                return "internal";
            case "external":
                return "external";
            default:
                return "public";
        }
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
        if (!"internal".equals(t.mTesterRole)) return;

        // Phase 6c: redact before the line enters the batcher so nothing
        // matching a configured pattern ever leaves the process.
        String redacted = t.redact(line);

        synchronized (t.mLogBufLock) {
            t.mLogBuf.append(redacted);
            t.mLogBuf.append('\n');
            boolean overflow = t.mLogBuf.length() >= LOG_FLUSH_BYTES;
            if (overflow) {
                // Flush immediately, but only queue one immediate task. A
                // hot logcat stream can otherwise post thousands of runnables
                // before the worker thread gets CPU.
                if (!t.mLogFlushImmediateScheduled) {
                    if (t.mLogFlushScheduled) {
                        t.mWorker.removeCallbacks(t.mFlushLogsRunnable);
                    }
                    t.mLogFlushScheduled = true;
                    t.mLogFlushImmediateScheduled = true;
                    t.mWorker.post(t.mFlushLogsRunnable);
                }
                return;
            }
            if (!t.mLogFlushScheduled) {
                t.mLogFlushScheduled = true;
                t.mLogFlushImmediateScheduled = false;
                t.mWorker.postDelayed(t.mFlushLogsRunnable, LOG_FLUSH_INTERVAL_MS);
            }
        }
    }

    private void flushLogs() {
        String text;
        synchronized (mLogBufLock) {
            mLogFlushScheduled = false;
            mLogFlushImmediateScheduled = false;
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
           .append('"');
        String parent = BugpunchRuntime.getParentSessionId();
        if (parent != null) {
            out.append(",\"parentSessionId\":\"").append(parent).append('"');
        }
        out.append(",\"text\":\"");
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

    private void loadCachedRole() {
        try {
            android.content.Context ctx = BugpunchRuntime.getAppContext();
            if (ctx == null) return;
            android.content.SharedPreferences prefs =
                ctx.getSharedPreferences(PREFS_FILE, android.content.Context.MODE_PRIVATE);
            mTesterRole = normalizeRole(prefs.getString("role", "public"));
        } catch (Throwable t) {
            Log.w(TAG, "loadCachedRole failed", t);
        }
    }

    /**
     * Parse a server-delivered role config blob ({ role, issuedAt, sig, payload })
     * into in-memory state and mirror to SharedPreferences. HMAC verification
     * of `sig` against the bundled pin_signing_secret is TODO — captured in
     * memory for a follow-up. For now we trust the tunnel (API-key
     * authenticated over TLS) the way earlier phases did.
     */
    private void applyRoleConfigInternal(JSONObject cfg) {
        String role = normalizeRole(cfg.optString("role", "public"));
        String previous = mTesterRole;
        boolean becameInternal = !"internal".equals(previous) && "internal".equals(role);
        boolean leftInternal  =  "internal".equals(previous) && !"internal".equals(role);
        mTesterRole = role;

        android.content.Context ctx = BugpunchRuntime.getAppContext();
        if (ctx != null) {
            try {
                ctx.getSharedPreferences(PREFS_FILE, android.content.Context.MODE_PRIVATE)
                    .edit()
                    .putString("role", role)
                    .apply();
            } catch (Throwable t) {
                Log.w(TAG, "role cache write failed", t);
            }
            // Mirror the role to the auto-prompt cache file (separate file +
            // key per the cache-driven launch flow spec). Pre-release: writing
            // "external"/"user" verbatim — both behave the same (no auto-prompt)
            // but readers can distinguish if needed later.
            try {
                String autoPromptValue = "internal".equals(role) ? "internal"
                    : "external".equals(role) ? "external" : "user";
                ctx.getSharedPreferences(PREFS_LAST_ROLE_FILE, android.content.Context.MODE_PRIVATE)
                    .edit()
                    .putString(PREFS_LAST_ROLE_KEY, autoPromptValue)
                    .apply();
            } catch (Throwable t) {
                Log.w(TAG, "last_tester_role cache write failed", t);
            }
        }

        // Catch up the dashboard with anything captured between app start and
        // the role arriving — without this, the first ~seconds of logs are
        // invisible in the live viewer (they're still in the on-disk crash
        // attachment, just not on the live tunnel).
        if (becameInternal) flushBufferedLogs();

        // Cache-driven auto-prompt reconciliation.
        //   - new role == internal but we didn't auto-prompt (cache stale or
        //     non-internal) → prompt now.
        //   - new role != internal and the ring buffer is running from the
        //     speculative cached prompt → tear it down. Server's authoritative
        //     answer wins.
        try {
            if (becameInternal) {
                BugpunchTesterRoleManager.onRoleBecameInternal();
            } else if (leftInternal) {
                BugpunchTesterRoleManager.onRoleLeftInternal();
            }
        } catch (Throwable t) {
            Log.w(TAG, "role transition dispatch failed", t);
        }
    }

    /**
     * Tee BugpunchLogReader's startup + recent ring through the tunnel as a
     * single batch. Called when the role flips to "internal" so the live
     * viewer doesn't miss the pre-handshake window. Each line goes through
     * {@link #enqueueLogLine} so it picks up redaction + framing for free.
     */
    private void flushBufferedLogs() {
        String snap;
        try { snap = BugpunchLogReader.snapshotText(); }
        catch (Throwable t) { Log.w(TAG, "flushBufferedLogs snapshot failed", t); return; }
        if (snap == null || snap.isEmpty()) return;
        for (String line : snap.split("\n")) {
            if (!line.isEmpty()) enqueueLogLine(line);
        }
    }

    // ── Instance state ──

    private JSONObject mConfig;
    private final String mStableDeviceId;
    private String mServerUrl;
    private String mApiKey;
    private final String mBuildChannel;
    private final WebSocketFactory mFactory;
    private final Handler mWorker;
    private WebSocket mSocket;
    private volatile boolean mConnected;
    private volatile boolean mStopRequested;
    private long mBackoffMs = BACKOFF_INITIAL_MS;
    private String mLastPinConfigJson;

    // Parsed role, mirrored to SharedPreferences so a cold start applies
    // the last-known role before the tunnel handshake completes. Defaults
    // to "public" → all interactive features off until server tells us
    // otherwise.
    private static final String PREFS_FILE = "bugpunch_role";
    private volatile String mTesterRole = "public";

    // Cache-driven debug-mode auto-prompt key. Lives in a separate
    // SharedPreferences file ("bugpunch") so a cold start of
    // BugpunchDebugAutoPrompt.maybeShowOnLaunch can read it without
    // depending on the role-state cache. Values: "internal" |
    // "external" | "user" | absent (no cache yet → wait for server).
    static final String PREFS_LAST_ROLE_FILE = "bugpunch";
    static final String PREFS_LAST_ROLE_KEY = "last_tester_role";

    /**
     * Read the last-known tester role from the cache used to drive the
     * launch-time debug-mode auto-prompt. Returns null when there's no
     * cached value (first ever launch — wait for server).
     */
    public static String readLastTesterRoleFromCache(android.content.Context ctx) {
        if (ctx == null) return null;
        try {
            android.content.SharedPreferences prefs =
                ctx.getSharedPreferences(PREFS_LAST_ROLE_FILE, android.content.Context.MODE_PRIVATE);
            if (!prefs.contains(PREFS_LAST_ROLE_KEY)) return null;
            return prefs.getString(PREFS_LAST_ROLE_KEY, null);
        } catch (Throwable t) {
            Log.w(TAG, "readLastTesterRoleFromCache failed", t);
            return null;
        }
    }

    // N5: log sink batcher. BugpunchLogReader tees every line here when the
    // alwaysLog pin is active; flush every 100 ms or when the buffer hits
    // ~32 KB, whichever first. Raw UTF-8 bytes on the wire with `\n` as
    // line separator — server writes them straight to disk with no parsing.
    private static final int LOG_FLUSH_BYTES = 32 * 1024;
    private static final long LOG_FLUSH_INTERVAL_MS = 100L;
    private final StringBuilder mLogBuf = new StringBuilder(LOG_FLUSH_BYTES);
    private final Object mLogBufLock = new Object();
    private final Runnable mFlushLogsRunnable = new Runnable() {
        @Override public void run() { flushLogs(); }
    };
    private boolean mLogFlushScheduled;
    private boolean mLogFlushImmediateScheduled;
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
        loadCachedRole();

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
        // Prevent duplicate / overlapping connections
        if (mSocket != null) {
            Log.w(TAG, "connect() skipped — socket already exists");
            return;
        }
        if (!mConfig.optBoolean("useNativeTunnel", false)) {
            Log.i(TAG, "useNativeTunnel is false — tunnel disabled");
            return;
        }
        if (mServerUrl.isEmpty()) {
            Log.w(TAG, "no serverUrl in config — aborting");
            return;
        }
        // Native tunnel is report-only (crashes / bugs / pin config / log sink /
        // device actions). Remote IDE RPC rides a separate managed WebSocket
        // that C# opens against /api/devices/ide-tunnel.
        String wsUrl = toWsUrl(mServerUrl) + "/api/devices/report-tunnel";
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
            BugpunchSdkErrorOverlay.reportThrowable("BugpunchTunnel", "connect", e);
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
            String metaModel = mConfig.optJSONObject("metadata") != null
                ? mConfig.optJSONObject("metadata").optString("deviceModel", "")
                : "";
            if (metaModel.isEmpty()) {
                metaModel = (android.os.Build.MANUFACTURER + " " + android.os.Build.MODEL).trim();
            }
            reg.put("name", metaModel);
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
                        JSONObject cfg = msg.optJSONObject("roleConfig");
                        if (cfg != null) {
                            mLastPinConfigJson = cfg.toString();
                            applyRoleConfigInternal(cfg);
                        }
                        Log.i(TAG, "registered (roleConfig=" + (cfg != null) + ")");
                        break;
                    }
                    case "roleUpdate": {
                        JSONObject cfg = msg.optJSONObject("config");
                        if (cfg != null) {
                            mLastPinConfigJson = cfg.toString();
                            applyRoleConfigInternal(cfg);
                        }
                        Log.i(TAG, "roleUpdate received");
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

        // Tiny query-string + parsing helpers used by the dispatch path. We
        // only need a handful of fields out of incoming /capture / /files
        // URLs and pulling in a real URL parser feels like overkill.

        private String queryParam(String path, String key) {
            int q = path.indexOf('?');
            if (q < 0) return "";
            String qs = path.substring(q + 1);
            for (String pair : qs.split("&")) {
                int eq = pair.indexOf('=');
                if (eq < 0) continue;
                if (pair.substring(0, eq).equals(key)) {
                    try { return java.net.URLDecoder.decode(pair.substring(eq + 1), "UTF-8"); }
                    catch (Exception e) { return pair.substring(eq + 1); }
                }
            }
            return "";
        }

        private int parseInt(String s, int fallback) {
            if (s == null || s.isEmpty()) return fallback;
            try { return Integer.parseInt(s); } catch (NumberFormatException e) { return fallback; }
        }

        private float parseFloat(String s, float fallback) {
            if (s == null || s.isEmpty()) return fallback;
            try { return Float.parseFloat(s); } catch (NumberFormatException e) { return fallback; }
        }

        @Override public void onDisconnected(WebSocket ws, WebSocketFrame serverFrame,
                                             WebSocketFrame clientFrame, boolean closedByServer) {
            mConnected = false;
            // Clear stale socket so reconnect can create a fresh one
            mSocket = null;
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
            BugpunchSdkErrorOverlay.reportThrowable("BugpunchTunnel", "websocket", cause);
            // Ensure state is reset; onDisconnected should handle scheduling reconnect
            mConnected = false;
            mSocket = null;
            // nv-websocket-client typically fires onDisconnected for failures;
            // avoid double-scheduling reconnect here.
        }
    }
}
