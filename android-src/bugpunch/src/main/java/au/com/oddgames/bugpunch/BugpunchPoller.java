package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.content.Context;
import android.content.SharedPreferences;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;

/**
 * Native poll client. Replaces the old DeviceRegistration.cs C# client.
 *
 * Owns:
 *   - POST /api/devices/register at startup (cached device token in prefs)
 *   - POST /api/device-poll every pollInterval seconds
 *   - Parse response:
 *       * pendingDirectives -> {@link BugpunchDirectives#onPollDirectives}
 *       * upgradeToWebSocket -> UnitySendMessage("BugpunchClient", "OnPollUpgradeRequested", "")
 *       * scripts -> UnitySendMessage("BugpunchClient", "OnPollScripts", scriptsJson)
 *
 * Config (serverUrl, apiKey, deviceId, platform, appVersion, installerMode)
 * is read from {@link BugpunchDebugMode#getServerUrl()}, {@code getApiKey()},
 * and the metadata that was seeded from the startup config.
 */
public final class BugpunchPoller {
    private static final String TAG = "[Bugpunch.Poller]";
    private static final String PREFS = "bugpunch_poll";
    private static final String TOKEN_KEY = "device_token";
    private static final int CONNECT_TIMEOUT_MS = 10_000;
    private static final int READ_TIMEOUT_MS = 15_000;

    private BugpunchPoller() {}

    private static volatile boolean sStarted;
    private static volatile boolean sStopped;
    private static ScheduledExecutorService sExecutor;
    private static String sDeviceToken;
    private static boolean sRegistrationRefreshed;
    private static int sPollIntervalSeconds = 30;
    private static String sScriptPermission = "ask";

    /**
     * Start registering and polling. Safe to call multiple times — subsequent
     * calls are no-ops. Must be called AFTER BugpunchDebugMode.startDebugMode
     * so getServerUrl / getApiKey / metadata are populated.
     */
    public static synchronized void start(Activity activity, String scriptPermission,
                                          int pollIntervalSeconds) {
        if (sStarted) return;
        if (activity == null) { Log.w(TAG, "null activity, not starting"); return; }
        String serverUrl = BugpunchRuntime.getServerUrl();
        String apiKey = BugpunchRuntime.getApiKey();
        if (serverUrl == null || serverUrl.isEmpty() || apiKey == null || apiKey.isEmpty()) {
            Log.w(TAG, "missing serverUrl or apiKey — poller not started");
            return;
        }

        sScriptPermission = scriptPermission == null || scriptPermission.isEmpty() ? "ask" : scriptPermission;
        sPollIntervalSeconds = Math.max(5, pollIntervalSeconds);
        sDeviceToken = loadToken(activity);
        sRegistrationRefreshed = false;

        sExecutor = Executors.newSingleThreadScheduledExecutor(r -> {
            Thread t = new Thread(r, "bugpunch-poller");
            t.setDaemon(true);
            return t;
        });
        sStarted = true;
        sStopped = false;

        // Kick off one register+poll immediately, then schedule recurring poll.
        sExecutor.execute(() -> {
            ensureRegistered(activity);
            poll(activity);
        });
        sExecutor.scheduleWithFixedDelay(() -> poll(activity),
            sPollIntervalSeconds, sPollIntervalSeconds, TimeUnit.SECONDS);

        Log.i(TAG, "started (interval=" + sPollIntervalSeconds + "s)");
    }

    public static synchronized void stop() {
        sStopped = true;
        if (sExecutor != null) {
            sExecutor.shutdownNow();
            sExecutor = null;
        }
        sStarted = false;
    }

    /**
     * Called from C# when it wants to force an immediate poll (e.g. after the
     * game config fetch lands). Safe no-op if not started.
     */
    public static void pollNow(Activity activity) {
        if (!sStarted || sStopped || sExecutor == null) return;
        sExecutor.execute(() -> poll(activity));
    }

    // -- Register -----------------------------------------------------------

    private static void ensureRegistered(Activity activity) {
        if (sRegistrationRefreshed && sDeviceToken != null && !sDeviceToken.isEmpty()) return;

        try {
            String deviceId = BugpunchRuntime.getMetadata("deviceId");
            String tunnelDeviceId = BugpunchTunnel.getDeviceId();
            if (tunnelDeviceId != null && !tunnelDeviceId.isEmpty()) deviceId = tunnelDeviceId;
            String appVersion = BugpunchRuntime.getMetadata("appVersion");
            String installerMode = BugpunchRuntime.getMetadata("installerMode");
            String deviceModel = BugpunchRuntime.getMetadata("deviceModel");
            JSONObject cfg = BugpunchRuntime.getConfig();
            String buildChannel = cfg != null ? cfg.optString("buildChannel", "unknown") : "unknown";

            JSONObject body = new JSONObject();
            body.put("deviceId", nullToEmpty(deviceId));
            body.put("name", nullToEmpty(deviceModel));
            body.put("platform", "Android");
            body.put("appVersion", nullToEmpty(appVersion));
            body.put("scriptPermission", sScriptPermission);
            body.put("installerMode", nullToEmpty(installerMode));
            body.put("stableDeviceId", nullToEmpty(BugpunchIdentity.getStableDeviceId(activity)));
            body.put("buildChannel", nullToEmpty(buildChannel));

            // Send the cached token if we have one — lets the server keep our
            // existing token instead of rotating (#226 proof-of-possession).
            HttpResult res = postJson(
                BugpunchRuntime.getServerUrl().replaceAll("/+$", "") + "/api/devices/register",
                "X-Api-Key", BugpunchRuntime.getApiKey(),
                "X-Device-Token", sDeviceToken,
                body.toString());

            if (!res.ok) {
                Log.w(TAG, "register failed: " + res.status + " " + res.body);
                return;
            }

            JSONObject parsed = new JSONObject(res.body);
            String token = parsed.optString("token", "");
            if (!token.isEmpty()) {
                sDeviceToken = token;
                saveToken(activity, token);
                sRegistrationRefreshed = true;
                Log.i(TAG, "registered");
            }
        } catch (Throwable t) {
            Log.w(TAG, "register error", t);
        }
    }

    // -- Poll ---------------------------------------------------------------

    private static void poll(Activity activity) {
        if (sStopped) return;
        if (sDeviceToken == null || sDeviceToken.isEmpty()) {
            ensureRegistered(activity);
            if (sDeviceToken == null || sDeviceToken.isEmpty()) return;
        }

        try {
            HttpResult res = postJson(
                BugpunchRuntime.getServerUrl().replaceAll("/+$", "") + "/api/device-poll",
                "X-Device-Token", sDeviceToken,
                "{}");

            if (res.status == 401) {
                // Token invalidated server-side (device deleted?). Drop and
                // re-register on the next tick.
                Log.w(TAG, "poll 401 — clearing token");
                sDeviceToken = "";
                sRegistrationRefreshed = false;
                saveToken(activity, "");
                return;
            }
            if (!res.ok) {
                Log.w(TAG, "poll failed: " + res.status);
                return;
            }

            JSONObject resp = new JSONObject(res.body);

            // Role config also travels over the poll path so internal devices
            // can pick up role enrollment before the report tunnel is live.
            JSONObject roleConfig = resp.optJSONObject("roleConfig");
            if (roleConfig != null) {
                BugpunchTunnel.applyRoleConfig(roleConfig);
                ensureReportTunnelIfInternal(activity, roleConfig);
            }

            // 1) Device-targeted directives — fire into the existing handler.
            JSONArray pendingDirectives = resp.optJSONArray("pendingDirectives");
            if (pendingDirectives != null && pendingDirectives.length() > 0) {
                BugpunchDirectives.onPollDirectives(pendingDirectives.toString());
            }

            // 2) Upgrade-to-WebSocket — signal C# to start the tunnel. The
            //    tunnel client is still C# (separate native-conversion work).
            if (resp.optBoolean("upgradeToWebSocket", false)) {
                BugpunchUnity.sendMessage("BugpunchClient", "OnPollUpgradeRequested", "");
            }

            // 3) Scheduled scripts — run in C# (script runner executes
            //    against managed code). Hand the raw array across.
            JSONArray scripts = resp.optJSONArray("scripts");
            if (scripts != null && scripts.length() > 0) {
                BugpunchUnity.sendMessage("BugpunchClient", "OnPollScripts", scripts.toString());
            }
        } catch (Throwable t) {
            Log.w(TAG, "poll error", t);
        }
    }

    private static void ensureReportTunnelIfInternal(Activity activity, JSONObject roleConfig) {
        if (activity == null || roleConfig == null) return;
        // Only Internal devices bring up the report tunnel from the poll path —
        // External/Public don't need ambient log streaming or Remote IDE accept.
        if (!"internal".equals(roleConfig.optString("role", "public"))) return;
        if (BugpunchTunnel.isConnected()) return;

        try {
            JSONObject cfg = BugpunchRuntime.getConfig();
            if (cfg == null) return;
            cfg.put("useNativeTunnel", true);
            BugpunchTunnel.start(cfg, BugpunchIdentity.getStableDeviceId(activity));
        } catch (Throwable t) {
            Log.w(TAG, "failed to start report tunnel for pinned device", t);
        }
    }

    /**
     * Post a scheduled script's execution result back to the server. Called
     * from C# via {@link BugpunchRuntime#postScriptResult} after the script
     * runner finishes.
     */
    public static void postScriptResult(String scheduledScriptId, String output,
                                        String errors, boolean success, int durationMs) {
        if (!sStarted || sStopped) return;
        if (sDeviceToken == null || sDeviceToken.isEmpty()) return;
        if (sExecutor == null) return;

        sExecutor.execute(() -> {
            try {
                JSONObject body = new JSONObject();
                body.put("scheduledScriptId", nullToEmpty(scheduledScriptId));
                body.put("output", nullToEmpty(output));
                body.put("errors", nullToEmpty(errors));
                body.put("success", success);
                body.put("durationMs", durationMs);

                postJson(
                    BugpunchRuntime.getServerUrl().replaceAll("/+$", "") + "/api/device-poll/script-result",
                    "X-Device-Token", sDeviceToken,
                    body.toString());
            } catch (Throwable t) {
                Log.w(TAG, "postScriptResult failed", t);
                BugpunchSdkErrorOverlay.reportThrowable("BugpunchPoller", "postScriptResult", t);
            }
        });
    }

    // -- Token persistence --------------------------------------------------

    private static String loadToken(Context ctx) {
        return ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getString(TOKEN_KEY, "");
    }

    private static void saveToken(Context ctx, String token) {
        SharedPreferences sp = ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        sp.edit().putString(TOKEN_KEY, token == null ? "" : token).apply();
    }

    // -- HTTP helper --------------------------------------------------------

    private static final class HttpResult {
        final int status;
        final String body;
        final boolean ok;
        HttpResult(int s, String b) { this.status = s; this.body = b == null ? "" : b; this.ok = s >= 200 && s < 300; }
    }

    private static HttpResult postJson(String url, String headerName, String headerValue,
                                       String body) {
        return postJson(url, headerName, headerValue, null, null, body);
    }

    private static HttpResult postJson(String url, String headerName, String headerValue,
                                       String header2Name, String header2Value,
                                       String body) {
        HttpURLConnection con = null;
        try {
            con = (HttpURLConnection) new URL(url).openConnection();
            con.setRequestMethod("POST");
            con.setConnectTimeout(CONNECT_TIMEOUT_MS);
            con.setReadTimeout(READ_TIMEOUT_MS);
            con.setDoOutput(true);
            con.setRequestProperty("Content-Type", "application/json");
            con.setRequestProperty(headerName, headerValue);
            if (header2Name != null && header2Value != null && !header2Value.isEmpty()) {
                con.setRequestProperty(header2Name, header2Value);
            }
            byte[] payload = body.getBytes(StandardCharsets.UTF_8);
            con.setFixedLengthStreamingMode(payload.length);
            try (OutputStream out = con.getOutputStream()) { out.write(payload); }

            int status = con.getResponseCode();
            InputStream is = status >= 200 && status < 300 ? con.getInputStream() : con.getErrorStream();
            StringBuilder sb = new StringBuilder();
            if (is != null) {
                try (BufferedReader br = new BufferedReader(new InputStreamReader(is, StandardCharsets.UTF_8))) {
                    String line;
                    while ((line = br.readLine()) != null) { sb.append(line); }
                }
            }
            return new HttpResult(status, sb.toString());
        } catch (Throwable t) {
            Log.w(TAG, "postJson failed: " + url, t);
            BugpunchSdkErrorOverlay.reportThrowable("BugpunchPoller", "postJson(" + url + ")", t);
            return new HttpResult(-1, "");
        } finally {
            if (con != null) con.disconnect();
        }
    }

    private static String nullToEmpty(String s) { return s == null ? "" : s; }
}
