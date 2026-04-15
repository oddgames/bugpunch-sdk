package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Context;
import android.content.SharedPreferences;
import android.util.Log;

import com.unity3d.player.UnityPlayer;

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
 * Flow: {@link BugpunchUploader} POSTs a crash to /api/crashes. The server
 * response carries {@code eventId} + {@code matchedDirectives[]}. This class
 * iterates the directives and applies each action:
 * <ul>
 *   <li>{@code attach_files}  - native glob inside the game-declared
 *       attachment allow-list (from BugpunchConfig). Posts matched file
 *       bytes (base64) to {@code /api/crashes/events/:id/enrich}.</li>
 *   <li>{@code run_paxscript} - UnitySendMessage into managed code; result
 *       comes back via {@link BugpunchDebugMode#postPaxScriptResult}.</li>
 *   <li>{@code ask_user_for_help} - native AlertDialog. On accept, silently
 *       enters debug mode and tags the session. On decline, persists
 *       "never ask again for this fingerprint" in SharedPreferences.</li>
 * </ul>
 *
 * All HTTP posting reuses {@link BugpunchUploader}'s multipart queue so
 * retries and app-kill survival come for free.
 */
public class BugpunchDirectives {
    private static final String TAG = "BugpunchDirectives";
    private static final String PREFS = "bugpunch_directives";
    private static final String DENIED_PREFIX = "denied:";

    // Pending paxscript callbacks, keyed by directiveId.
    // Populated when we dispatch to Unity; drained by postPaxScriptResult.
    private static final Map<String, Pending> sPendingPaxScript = new ConcurrentHashMap<>();

    static class Pending {
        final String eventId;
        final String fingerprint;
        Pending(String eId, String fp) { eventId = eId; fingerprint = fp; }
    }

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

        for (int i = 0; i < directives.length(); i++) {
            JSONObject d = directives.optJSONObject(i);
            if (d == null) continue;
            String directiveId = d.optString("id", "");
            JSONArray actions = d.optJSONArray("actions");
            if (directiveId.isEmpty() || actions == null) continue;

            for (int j = 0; j < actions.length(); j++) {
                JSONObject a = actions.optJSONObject(j);
                if (a == null) continue;
                try {
                    applyAction(activity, eventId, fingerprint, directiveId, a);
                } catch (Throwable t) {
                    Log.w(TAG, "apply action failed", t);
                }
            }
        }
    }

    private static void applyAction(final Activity activity, final String eventId,
                                    final String fingerprint, final String directiveId,
                                    JSONObject action) throws JSONException {
        String type = action.optString("type", "");
        if ("attach_files".equals(type)) {
            handleAttachFiles(activity, eventId, directiveId, action);
        } else if ("run_paxscript".equals(type)) {
            handleRunPaxScript(eventId, fingerprint, directiveId, action);
        } else if ("ask_user_for_help".equals(type)) {
            handleAskUser(activity, eventId, fingerprint, directiveId, action);
        }
    }

    // ── attach_files ──────────────────────────────────────────────────────

    private static void handleAttachFiles(Activity activity, String eventId,
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
        postEnrich(activity, eventId, body);
    }

    /**
     * Read BugpunchConfig.attachmentRules from the startup config the game
     * handed us (native already has them in BugpunchDebugMode.sConfig).
     * Each rule: { name, rawPath, path, pattern, maxBytes }.
     */
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

    /**
     * For a server-supplied pattern like "[PersistentDataPath]/saves/*.json",
     * find the allow-list rule whose rawPath is a prefix of the pattern, then
     * glob within the rule's directory. Patterns with no matching rule are
     * silently dropped \u2014 the game-declared allow-list is authoritative.
     */
    private static File[] resolveAndGlob(String pattern, List<JSONObject> allow) {
        for (JSONObject rule : allow) {
            String rawPath = rule.optString("rawPath", "");
            String resolvedRoot = rule.optString("path", "");
            String globPattern = rule.optString("pattern", "*");
            if (rawPath.isEmpty() || resolvedRoot.isEmpty()) continue;
            if (!pattern.startsWith(rawPath)) continue;
            // Pattern is inside this rule's scope. Use the rule's own pattern
            // as a secondary filter \u2014 server globs are refined by the allow-list.
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

    // ── run_paxscript ─────────────────────────────────────────────────────

    private static void handleRunPaxScript(String eventId, String fingerprint,
                                           String directiveId, JSONObject action) {
        String code = action.optString("code", "");
        int timeoutMs = action.optInt("timeoutMs", 2000);
        if (code.isEmpty()) return;
        sPendingPaxScript.put(directiveId, new Pending(eventId, fingerprint));
        JSONObject payload = new JSONObject();
        try {
            payload.put("directiveId", directiveId);
            payload.put("code", code);
            payload.put("timeoutMs", timeoutMs);
        } catch (JSONException e) { return; }
        try {
            UnityPlayer.UnitySendMessage("BugpunchClient", "DirectiveRunPaxScript", payload.toString());
        } catch (Throwable t) {
            Log.w(TAG, "UnitySendMessage failed", t);
            sPendingPaxScript.remove(directiveId);
        }
    }

    /**
     * Called from {@link BugpunchDebugMode#postPaxScriptResult} when C#
     * finishes running the script (success or failure).
     */
    public static void onPaxScriptResult(String directiveId, String resultJson) {
        Pending p = sPendingPaxScript.remove(directiveId);
        if (p == null) return;
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
        postEnrich(activity, p.eventId, body);
    }

    // ── ask_user_for_help ─────────────────────────────────────────────────

    private static void handleAskUser(final Activity activity, final String eventId,
                                      final String fingerprint, final String directiveId,
                                      JSONObject action) {
        if (fingerprint == null || fingerprint.isEmpty()) return;
        if (isDenied(activity, fingerprint)) {
            Log.i(TAG, "ask_user skipped \u2014 fingerprint previously denied");
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
                            postAskResult(activity, eventId, directiveId, "accepted");
                        })
                        .setNegativeButton(decline, (dlg, which) -> {
                            setDenied(activity, fingerprint);
                            postAskResult(activity, eventId, directiveId, "declined");
                        })
                        .setOnCancelListener(d ->
                            postAskResult(activity, eventId, directiveId, "dismissed"))
                        .show();
                } catch (Throwable t) {
                    Log.w(TAG, "ask dialog failed", t);
                }
            }
        });
    }

    private static void postAskResult(Activity activity, String eventId,
                                      String directiveId, String result) {
        JSONObject body = new JSONObject();
        try {
            body.put("directiveId", directiveId);
            body.put("userPromptResult", result);
        } catch (JSONException e) { return; }
        postEnrich(activity, eventId, body);
    }

    private static boolean isDenied(Context ctx, String fingerprint) {
        return ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .getBoolean(DENIED_PREFIX + fingerprint, false);
    }

    private static void setDenied(Context ctx, String fingerprint) {
        SharedPreferences sp = ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        sp.edit().putBoolean(DENIED_PREFIX + fingerprint, true).apply();
    }

    // ── Enrich POST ───────────────────────────────────────────────────────

    /**
     * Enqueue a POST to {@code /api/crashes/events/:id/enrich} via the
     * existing uploader so retries + app-kill survival come for free.
     */
    private static void postEnrich(Context ctx, String eventId, JSONObject body) {
        String serverUrl = BugpunchDebugMode.getServerUrl();
        String apiKey = BugpunchDebugMode.getApiKey();
        if (serverUrl == null || serverUrl.isEmpty() || apiKey == null || apiKey.isEmpty()) return;
        String url = serverUrl.replaceAll("/+$", "") + "/api/crashes/events/" + eventId + "/enrich";
        BugpunchUploader.enqueueJson(ctx, url, apiKey, body.toString());
    }
}
