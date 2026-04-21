package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Context;
import android.content.SharedPreferences;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.File;
import java.io.FileInputStream;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Android handler for server-sent "Request More Info" directives.
 *
 * Two entry points:
 * <ol>
 *   <li>{@link #onUploadResponse} - fires after a successful /api/crashes
 *       POST. Server response carries {@code eventId} + {@code matchedDirectives[]}.
 *       Action results are POSTed to {@code /api/crashes/events/{eventId}/enrich}.</li>
 *   <li>{@link #onPollDirectives} - fires from the native poll loop when
 *       /api/device-poll returns {@code pendingDirectives[]}. No crash
 *       context. Action results are POSTed to
 *       {@code /api/directives/{directiveId}/result}.</li>
 * </ol>
 *
 * Both paths run through the same handlers (attach_files / run_paxscript /
 * ask_user_for_help). The dispatcher builds the result URL once and passes it
 * down. All HTTP posting reuses {@link BugpunchUploader}'s multipart queue so
 * retries and app-kill survival come for free.
 */
public class BugpunchDirectives {
    private static final String TAG = "BugpunchDirectives";
    private static final String PREFS = "bugpunch_directives";
    private static final String DENIED_PREFIX = "denied:";

    // Pending paxscript callbacks, keyed by directiveId -> resultUrl. The URL
    // encodes both the POST target and the eventId/directiveId, so
    // onPaxScriptResult doesn't need to know which flow spawned it.
    private static final Map<String, String> sPendingPaxScript = new ConcurrentHashMap<>();

    /**
     * Called from {@link BugpunchUploader} after a successful /api/crashes
     * POST. Parses the server response and fans out the directive actions.
     */
    public static void onUploadResponse(JSONObject manifest, String responseBody) {
        if (responseBody == null || responseBody.isEmpty()) return;
        final Activity activity = BugpunchUnity.currentActivity();
        if (activity == null) return;

        JSONObject resp;
        try { resp = new JSONObject(responseBody); }
        catch (JSONException e) { Log.w(TAG, "bad response json", e); return; }

        final String eventId = resp.optString("eventId", "");
        final String fingerprint = resp.optString("fingerprint", "");
        JSONArray directives = resp.optJSONArray("matchedDirectives");
        if (eventId.isEmpty() || directives == null || directives.length() == 0) return;

        final String resultUrl = enrichUrl(eventId);
        if (resultUrl == null) return;

        for (int i = 0; i < directives.length(); i++) {
            JSONObject d = directives.optJSONObject(i);
            if (d == null) continue;
            String directiveId = d.optString("id", "");
            JSONArray actions = d.optJSONArray("actions");
            if (directiveId.isEmpty() || actions == null) continue;

            for (int j = 0; j < actions.length(); j++) {
                JSONObject a = actions.optJSONObject(j);
                if (a == null) continue;
                applyAction(activity, resultUrl, fingerprint, directiveId, a);
            }
        }
    }

    /**
     * Invoked from the native poller ({@link BugpunchPoller}) when a
     * /api/device-poll response returns pendingDirectives. Input is the
     * raw JSON array: {@code [{"id":"...","actions":[...]}, ...]}.
     */
    public static void onPollDirectives(String pendingDirectivesJson) {
        if (pendingDirectivesJson == null || pendingDirectivesJson.isEmpty()) return;
        final Activity activity = BugpunchUnity.currentActivity();
        if (activity == null) return;

        JSONArray directives;
        try { directives = new JSONArray(pendingDirectivesJson); }
        catch (JSONException e) { Log.w(TAG, "bad poll directives json", e); return; }

        for (int i = 0; i < directives.length(); i++) {
            JSONObject d = directives.optJSONObject(i);
            if (d == null) continue;
            String directiveId = d.optString("id", "");
            JSONArray actions = d.optJSONArray("actions");
            if (directiveId.isEmpty() || actions == null) continue;

            String resultUrl = directiveResultUrl(directiveId);
            if (resultUrl == null) continue;

            for (int j = 0; j < actions.length(); j++) {
                JSONObject a = actions.optJSONObject(j);
                if (a == null) continue;
                // Device-targeted directives carry no fingerprint; the denial
                // gate is a no-op in that case.
                applyAction(activity, resultUrl, "", directiveId, a);
            }
        }
    }

    private static void applyAction(final Activity activity, final String resultUrl,
                                    final String fingerprint, final String directiveId,
                                    JSONObject action) {
        String type = action.optString("type", "");
        try {
            if ("attach_files".equals(type)) {
                handleAttachFiles(activity, resultUrl, directiveId, action);
            } else if ("run_paxscript".equals(type)) {
                handleRunPaxScript(resultUrl, directiveId, action);
            } else if ("ask_user_for_help".equals(type)) {
                handleAskUser(activity, resultUrl, fingerprint, directiveId, action);
            }
            // enable_debug_tunnel is consumed server-side on the poll; no
            // client-side action required.
        } catch (Throwable t) {
            Log.w(TAG, "apply action failed", t);
        }
    }

    // -- attach_files ------------------------------------------------------

    private static void handleAttachFiles(Activity activity, String resultUrl,
                                          String directiveId, JSONObject action) {
        JSONArray paths = action.optJSONArray("paths");
        if (paths == null || paths.length() == 0) return;
        long maxBytesPerFile = action.optLong("maxBytesPerFile", 4 * 1024 * 1024L);
        long maxTotalBytes = action.optLong("maxTotalBytes", 16 * 1024 * 1024L);

        List<JSONObject> allow = getAllowList();
        if (allow.isEmpty()) {
            Log.i(TAG, "attach_files: no allow-list rules in config, skipping");
            return;
        }

        JSONArray attachments = new JSONArray();
        long total = 0;
        for (int i = 0; i < paths.length(); i++) {
            String pattern = paths.optString(i);
            if (pattern == null || pattern.isEmpty() || pattern.contains("..")) continue;
            File[] matches = resolveAndGlob(pattern, allow);
            if (matches == null) continue;
            for (File f : matches) {
                if (!f.exists() || !f.isFile()) continue;
                long len = f.length();
                if (len <= 0 || len > maxBytesPerFile) continue;
                if (total + len > maxTotalBytes) break;
                try {
                    byte[] bytes = new byte[(int) len];
                    FileInputStream in = new FileInputStream(f);
                    try { in.read(bytes); } finally { in.close(); }
                    JSONObject att = new JSONObject();
                    att.put("path", pattern);
                    att.put("bytes", len);
                    att.put("dataBase64", android.util.Base64.encodeToString(bytes, android.util.Base64.NO_WRAP));
                    attachments.put(att);
                    total += len;
                } catch (Throwable t) {
                    Log.w(TAG, "read failed: " + f, t);
                }
            }
        }

        JSONObject body = new JSONObject();
        try {
            body.put("directiveId", directiveId);
            body.put("attachments", attachments);
        } catch (JSONException e) { return; }
        postJson(activity, resultUrl, body);
    }

    private static List<JSONObject> getAllowList() {
        List<JSONObject> out = new ArrayList<>();
        JSONArray arr = BugpunchDebugMode.getAttachmentRules();
        if (arr == null) return out;
        for (int i = 0; i < arr.length(); i++) {
            JSONObject r = arr.optJSONObject(i);
            if (r != null) out.add(r);
        }
        return out;
    }

    private static File[] resolveAndGlob(String pattern, List<JSONObject> allow) {
        for (JSONObject rule : allow) {
            String rawPath = rule.optString("rawPath", "");
            String resolvedRoot = rule.optString("path", "");
            String globPattern = rule.optString("pattern", "*");
            if (rawPath.isEmpty() || resolvedRoot.isEmpty()) continue;
            if (!pattern.startsWith(rawPath)) continue;
            File dir = new File(resolvedRoot);
            if (!dir.exists() || !dir.isDirectory()) continue;
            final String suffix = pattern.substring(rawPath.length());
            final String effectiveGlob = suffix.isEmpty() || suffix.equals("/") ? globPattern : trimLeadingSlash(suffix);
            return dir.listFiles(new java.io.FilenameFilter() {
                @Override public boolean accept(File d, String name) {
                    return matchGlob(effectiveGlob, name) && matchGlob(globPattern, name);
                }
            });
        }
        return null;
    }

    private static String trimLeadingSlash(String s) {
        return s.startsWith("/") ? s.substring(1) : s;
    }

    /** Minimal glob matcher supporting only '*' and literal chars. */
    static boolean matchGlob(String pattern, String name) {
        if (pattern == null || pattern.equals("*")) return true;
        int pi = 0, ni = 0, star = -1, match = 0;
        while (ni < name.length()) {
            if (pi < pattern.length() && (pattern.charAt(pi) == name.charAt(ni)
                    || pattern.charAt(pi) == '?')) {
                pi++; ni++;
            } else if (pi < pattern.length() && pattern.charAt(pi) == '*') {
                star = pi++; match = ni;
            } else if (star != -1) {
                pi = star + 1; match++; ni = match;
            } else return false;
        }
        while (pi < pattern.length() && pattern.charAt(pi) == '*') pi++;
        return pi == pattern.length();
    }

    // -- run_paxscript -----------------------------------------------------

    private static void handleRunPaxScript(String resultUrl, String directiveId,
                                           JSONObject action) {
        String code = action.optString("code", "");
        int timeoutMs = action.optInt("timeoutMs", 2000);
        if (code.isEmpty()) return;
        sPendingPaxScript.put(directiveId, resultUrl == null ? "" : resultUrl);
        JSONObject payload = new JSONObject();
        try {
            payload.put("directiveId", directiveId);
            payload.put("code", code);
            payload.put("timeoutMs", timeoutMs);
        } catch (JSONException e) { return; }
        BugpunchUnity.sendMessage("BugpunchClient", "DirectiveRunPaxScript", payload.toString());
    }

    /**
     * Called from {@link BugpunchRuntime#postPaxScriptResult} when C#
     * finishes running the script (success or failure).
     */
    public static void onPaxScriptResult(String directiveId, String resultJson) {
        String resultUrl = sPendingPaxScript.remove(directiveId);
        if (resultUrl == null || resultUrl.isEmpty()) return;
        Activity activity = BugpunchUnity.currentActivity();
        if (activity == null) return;
        JSONObject result;
        try { result = new JSONObject(resultJson); }
        catch (JSONException e) {
            result = new JSONObject();
            try {
                result.put("ok", false);
                result.put("errors", new JSONArray().put("bad json from paxscript runner"));
            } catch (JSONException ignored) {}
        }
        JSONObject body = new JSONObject();
        try {
            body.put("directiveId", directiveId);
            body.put("paxscript", result);
        } catch (JSONException e) { return; }
        postJson(activity, resultUrl, body);
    }

    // -- ask_user_for_help -------------------------------------------------
    // fingerprint is empty for device-targeted directives; denial persistence
    // is skipped in that case.

    private static void handleAskUser(final Activity activity, final String resultUrl,
                                      final String fingerprint, final String directiveId,
                                      JSONObject action) {
        if (!fingerprint.isEmpty() && isDenied(activity, fingerprint)) {
            Log.i(TAG, "ask_user skipped - fingerprint previously denied");
            return;
        }
        final String title = action.optString("promptTitle", "Help us fix this bug");
        final String body = action.optString("promptBody", "We hit an error. Can you help us reproduce it?");
        final String accept = action.optString("acceptLabel", "Help out");
        final String decline = action.optString("declineLabel", "No thanks");

        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                try {
                    new AlertDialog.Builder(activity)
                        .setTitle(title)
                        .setMessage(body)
                        .setPositiveButton(accept, (dlg, which) -> {
                            try {
                                BugpunchDebugMode.enterDebugMode(activity, true);
                                BugpunchDebugMode.setCustomData("bugpunch.repro_attempt", "true");
                            } catch (Throwable t) { Log.w(TAG, "accept handler failed", t); }
                            postAskResult(activity, resultUrl, directiveId, "accepted");
                        })
                        .setNegativeButton(decline, (dlg, which) -> {
                            if (!fingerprint.isEmpty()) setDenied(activity, fingerprint);
                            postAskResult(activity, resultUrl, directiveId, "declined");
                        })
                        .setOnCancelListener(d ->
                            postAskResult(activity, resultUrl, directiveId, "dismissed"))
                        .show();
                } catch (Throwable t) {
                    Log.w(TAG, "ask dialog failed", t);
                }
            }
        });
    }

    private static void postAskResult(Activity activity, String resultUrl,
                                      String directiveId, String result) {
        JSONObject body = new JSONObject();
        try {
            body.put("directiveId", directiveId);
            body.put("userPromptResult", result);
        } catch (JSONException e) { return; }
        postJson(activity, resultUrl, body);
    }

    private static boolean isDenied(Context ctx, String fingerprint) {
        return ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .getBoolean(DENIED_PREFIX + fingerprint, false);
    }

    private static void setDenied(Context ctx, String fingerprint) {
        SharedPreferences sp = ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        sp.edit().putBoolean(DENIED_PREFIX + fingerprint, true).apply();
    }

    // -- URL formatting + upload --------------------------------------------

    private static String enrichUrl(String eventId) {
        String serverUrl = BugpunchDebugMode.getServerUrl();
        if (serverUrl == null || serverUrl.isEmpty() || eventId == null || eventId.isEmpty()) return null;
        return serverUrl.replaceAll("/+$", "") + "/api/crashes/events/" + eventId + "/enrich";
    }

    private static String directiveResultUrl(String directiveId) {
        String serverUrl = BugpunchDebugMode.getServerUrl();
        if (serverUrl == null || serverUrl.isEmpty() || directiveId == null || directiveId.isEmpty()) return null;
        return serverUrl.replaceAll("/+$", "") + "/api/directives/" + directiveId + "/result";
    }

    private static void postJson(Context ctx, String url, JSONObject body) {
        if (url == null || url.isEmpty()) return;
        String apiKey = BugpunchDebugMode.getApiKey();
        if (apiKey == null || apiKey.isEmpty()) return;
        BugpunchUploader.enqueueJson(ctx, url, apiKey, body.toString());
    }
}
