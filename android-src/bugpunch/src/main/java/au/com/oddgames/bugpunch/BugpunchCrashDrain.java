package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.File;
import java.util.ArrayList;
import java.util.List;

/**
 * Previous-session crash file drain. Runs once at startup from
 * {@link BugpunchRuntime#start(Activity, String)} — no other entry points.
 *
 * <p>Picks up every {@code .crash} file the native signal handler / ANR
 * watchdog wrote on a previous launch, wraps the raw text as a
 * {@code /api/issues/ingest} JSON payload, snapshots any attachments (screenshots,
 * logs, breadcrumbs, ANR screenshot burst) into the uploader's cache dir,
 * enqueues through {@link BugpunchUploader} (so a failed POST resumes on the
 * next launch), and deletes the local file.
 *
 * <p>Intentionally sends the <b>raw</b> crash text as the {@code stackTrace}
 * field — server's crashSymbolicator parses it to find the
 * {@code ---STACK---} and {@code ---BUILD_IDS---} sections. No format
 * divergence between what we wrote and what we upload.
 */
final class BugpunchCrashDrain {
    private static final String TAG = "[Bugpunch.CrashDrain]";

    private BugpunchCrashDrain() {}

    /**
     * Pick up every .crash file the native handler / ANR watchdog wrote on
     * a previous launch, wrap the raw text as a /api/issues/ingest JSON payload,
     * enqueue via the durable uploader (so a failed POST resumes on the
     * next launch), and delete the local file.
     */
    static void drain(Activity activity) {
        String[] paths = BugpunchCrashHandler.getPendingCrashFiles(activity);
        if (paths == null || paths.length == 0) return;

        JSONObject config = BugpunchRuntime.getConfig();
        String serverUrl = config != null ? config.optString("serverUrl", "") : "";
        String apiKey    = config != null ? config.optString("apiKey", "") : "";
        if (serverUrl.isEmpty() || apiKey.isEmpty()) {
            Log.w(TAG, "native crashes pending but serverUrl/apiKey not configured");
            return;
        }
        String base = serverUrl.replaceAll("/+$", "");
        String preflightUrl = base + "/api/issues/ingest";
        String enrichTemplate = base + "/api/issues/events/{id}/enrich";

        for (String path : paths) {
            String raw = BugpunchCrashHandler.readCrashFile(path);
            if (raw == null || raw.isEmpty()) {
                BugpunchCrashHandler.deleteCrashFile(path);
                continue;
            }
            try {
                JSONObject body = buildCrashPayload(raw);
                // Attachment paths embedded in the crash file by the native
                // signal handler (bp.c) and the ANR writer:
                //   screenshot:         — "at crash" shot (raw ARGB for SIGSEGV, JPEG for ANR)
                //   context_screenshot: — "1s before" shot (raw ARGB for SIGSEGV)
                //   logs:               — rolling log mirror JSON
                //   frame_w/frame_h/frame_format: set alongside raw dumps
                String shotPath    = extractField(raw, "screenshot");
                String ctxPath     = extractField(raw, "context_screenshot");
                String logsPath    = extractField(raw, "logs");
                String crumbsPath  = extractField(raw, "breadcrumbs");
                int    crumbsCount = 0;
                int    crumbsStride = 0;
                try {
                    String c = extractField(raw, "breadcrumbs_count");
                    if (c != null) crumbsCount = Integer.parseInt(c);
                    String s = extractField(raw, "breadcrumbs_stride");
                    if (s != null) crumbsStride = Integer.parseInt(s);
                } catch (NumberFormatException ignored) {}
                // Parse the breadcrumb ring into the JSON body so the server
                // stores it in crash_events.breadcrumbs (dedicated column).
                if (crumbsPath != null && crumbsCount > 0 && crumbsStride > 0) {
                    JSONArray arr = parseBreadcrumbsFile(crumbsPath, crumbsCount, crumbsStride);
                    if (arr != null && arr.length() > 0) {
                        body.put("breadcrumbs", arr);
                    }
                }
                // If the signal handler wrote raw pixels (native crash path),
                // encode them to JPEG before queuing — the server expects
                // image/jpeg, and upload bandwidth matters far more than the
                // one-shot encode cost at next-launch drain time.
                String fmt = extractField(raw, "frame_format");
                if ("rgba8888".equals(fmt)) {
                    int fw = 0, fh = 0;
                    try {
                        String wStr = extractField(raw, "frame_w");
                        String hStr = extractField(raw, "frame_h");
                        if (wStr != null) fw = Integer.parseInt(wStr);
                        if (hStr != null) fh = Integer.parseInt(hStr);
                    } catch (NumberFormatException ignored) {}
                    if (fw > 0 && fh > 0) {
                        shotPath = encodeRawFrame(shotPath, fw, fh);
                        ctxPath  = encodeRawFrame(ctxPath,  fw, fh);
                    }
                }
                // Collect ANR screenshot burst (anr_screenshot_0, _1, ...) with timestamps.
                int shotCount = 0;
                try {
                    String countStr = extractField(raw, "screenshot_count");
                    if (countStr != null) shotCount = Integer.parseInt(countStr);
                } catch (NumberFormatException ignored) {}
                String[] anrShotPaths = null;
                if (shotCount > 0) {
                    java.util.ArrayList<String> valid = new java.util.ArrayList<>();
                    JSONArray tsArray = new JSONArray();
                    for (int i = 0; i < shotCount; i++) {
                        String p = extractField(raw, "anr_screenshot_" + i);
                        String ts = extractField(raw, "anr_screenshot_ts_" + i);
                        if (p != null && new java.io.File(p).exists()) {
                            valid.add(p);
                            tsArray.put(ts != null ? Long.parseLong(ts) : 0);
                        }
                    }
                    if (!valid.isEmpty()) {
                        anrShotPaths = valid.toArray(new String[0]);
                        body.put("anrScreenshotTimestamps", tsArray);
                    }
                }
                // Build attachment list for the crash upload. Snapshot each
                // referenced file into the uploader's cache dir so the
                // rolling buffers in THIS session can't overwrite them
                // between now and when the worker thread actually POSTs.
                // cleanupPaths on the manifest deletes the snapshot on
                // successful upload.
                File uploadCache = new File(activity.getCacheDir(), "bugpunch_uploads");
                if (!uploadCache.exists()) uploadCache.mkdirs();
                String snapPrefix = "crash_" + System.currentTimeMillis() + "_";
                List<BugpunchUploader.FileAttachment> files = new ArrayList<>();
                String snapShot = snapshotAttachment(shotPath, uploadCache, snapPrefix + "at.jpg");
                String snapCtx  = snapshotAttachment(ctxPath,  uploadCache, snapPrefix + "before.jpg");
                String snapLogs = snapshotAttachment(logsPath, uploadCache, snapPrefix + "logs.log");
                if (snapShot != null) {
                    files.add(BugpunchUploader.FileAttachment.jpegGated(
                        "screenshot", snapShot, "screenshot"));
                }
                if (snapCtx != null) {
                    files.add(BugpunchUploader.FileAttachment.jpegGated(
                        "context_screenshot", snapCtx, "context_screenshot"));
                }
                // Logs mirror — raw logcat text (not JSON). Multipart field
                // name `logs` matches /api/issues/events/:id/enrich server-side
                // expectation; middleware detects text vs JSON from the
                // payload's first char.
                if (snapLogs != null) {
                    files.add(new BugpunchUploader.FileAttachment(
                        "logs", "logs.log", "text/plain", snapLogs, "logs"));
                }
                if (anrShotPaths != null) {
                    for (int i = 0; i < anrShotPaths.length; i++) {
                        files.add(BugpunchUploader.FileAttachment.jpegGated(
                            "anr_screenshot", i, anrShotPaths[i], "anr_screenshots"));
                    }
                }
                // Native crashes always go through preflight — the server
                // decides whether logs/screenshots/video are worth uploading
                // based on per-fingerprint budget. If budget is spent,
                // phase 2 is skipped entirely and attachments are cleaned up.
                BugpunchUploader.enqueuePreflight(activity, preflightUrl,
                    enrichTemplate, apiKey, body.toString(), files);
                BugpunchCrashHandler.deleteCrashFile(path);
                Log.i(TAG, "queued pending crash: " + path);
            } catch (Throwable t) {
                Log.w(TAG, "failed to queue crash " + path, t);
                // Leave the file on disk so next launch retries.
            }
        }
    }

    /**
     * Map the flat <code>key:value</code> header lines bp.c writes into the
     * JSON fields crashService.ingestCrash expects. Anything unrecognized
     * still lives inside the full `stackTrace` string for the server to
     * parse (build-id table, maps, etc.), so this doesn't need to be
     * exhaustive.
     */
    private static JSONObject buildCrashPayload(String raw) throws JSONException {
        JSONObject body = new JSONObject();
        body.put("stackTrace", raw);
        body.put("platform", "android");

        // Pull the header fields bp.c writes before ---STACK---. Each line is
        // "key:value" — stop at the first section marker.
        String signal = null, faultAddr = null, type = null;
        for (String line : raw.split("\\r?\\n")) {
            if (line.startsWith("---")) break;
            int c = line.indexOf(':');
            if (c < 0) continue;
            String k = line.substring(0, c);
            String v = line.substring(c + 1);
            switch (k) {
                case "signal":       signal = v; break;
                case "fault_addr":   faultAddr = v; break;
                case "type":         type = v; break;
                case "app_version":  body.put("buildVersion", v); break;
                case "bundle_id":    body.put("bundleId", v); break;
                case "device_model": body.put("deviceName", v); break;
                case "timestamp": {
                    // Device-clock crash time, same reference as breadcrumb `t`.
                    // bp.c writes CLOCK_REALTIME epoch seconds; the ANR watchdog
                    // writes System.currentTimeMillis() — detect by magnitude
                    // so both paths feed one clientTimestampMs field the server
                    // can use to anchor the storyboard.
                    try {
                        long n = Long.parseLong(v.trim());
                        // 1e11 is year 5138 in seconds / year 1973 in ms — clean split.
                        long ms = n < 100_000_000_000L ? n * 1000L : n;
                        body.put("clientTimestampMs", ms);
                    } catch (NumberFormatException ignored) { }
                    break;
                }
                default: /* fall through — preserved in stackTrace */ break;
            }
        }

        // errorMessage is what the dashboard lists as the one-line summary.
        // category controls the Issues page tab (crash / exception / anr).
        // type is the /api/issues/ingest discriminator — must be one of
        // "crash", "exception", or "anr".
        if ("NATIVE_SIGNAL".equals(type)) {
            body.put("errorMessage",
                (signal != null ? signal : "NATIVE") +
                (faultAddr != null ? " at " + faultAddr : ""));
            body.put("category", "crash");
            body.put("type", "crash");
        } else if ("ANR".equals(type)) {
            body.put("errorMessage", "ANR — main thread unresponsive");
            body.put("category", "anr");
            body.put("type", "anr");
        } else {
            body.put("errorMessage", type != null ? type : "Native crash");
            body.put("category", "crash");
            body.put("type", "crash");
        }

        // branch / changeset / buildFingerprint come from current runtime
        // metadata rather than the crash file. These values don't change
        // within a build so reading them at drain time (next launch) is
        // equivalent to reading at crash time. Avoids a schema change in
        // the NDK signal writer.
        String branch = BugpunchRuntime.getMetadata("branch");
        if (branch != null && !branch.isEmpty()) body.put("branch", branch);
        String changeset = BugpunchRuntime.getMetadata("changeset");
        if (changeset != null && !changeset.isEmpty()) body.put("changeset", changeset);
        String fingerprint = BugpunchRuntime.getMetadata("buildFingerprint");
        if (fingerprint != null && !fingerprint.isEmpty()) body.put("buildFingerprint", fingerprint);
        return body;
    }

    /** Extract a header field from a crash file's key:value lines (before ---STACK---). */
    private static String extractField(String raw, String key) {
        for (String line : raw.split("\\r?\\n")) {
            if (line.startsWith("---")) break;
            if (line.startsWith(key + ":")) return line.substring(key.length() + 1);
        }
        return null;
    }

    /** Read a null-padded UTF-8 string of max `max` bytes from `buf` at `offset`. */
    private static String readCString(byte[] buf, int offset, int max) {
        int end = offset;
        int limit = offset + max;
        if (limit > buf.length) limit = buf.length;
        while (end < limit && buf[end] != 0) end++;
        if (end == offset) return "";
        return new String(buf, offset, end - offset, java.nio.charset.StandardCharsets.UTF_8);
    }

    /** Map the int type to the string the dashboard expects. */
    private static String eventTypeName(int type) {
        switch (type) {
            case BugpunchInput.TYPE_TOUCH_DOWN:   return "touch_down";
            case BugpunchInput.TYPE_TOUCH_UP:     return "touch_up";
            case BugpunchInput.TYPE_TOUCH_MOVE:   return "touch_move";
            case BugpunchInput.TYPE_KEY_DOWN:     return "key_down";
            case BugpunchInput.TYPE_KEY_UP:       return "key_up";
            case BugpunchInput.TYPE_SCENE_CHANGE: return "scene_change";
            case BugpunchInput.TYPE_CUSTOM:       return "custom";
            default: return "unknown";
        }
    }

    /**
     * Encode a raw ARGB8888 dump produced by the native signal handler into
     * a JPEG sitting next to it ({same name}.jpg). Returns the JPEG path on
     * success, or null if the source is missing / truncated / encoding
     * fails (caller then skips that attachment rather than uploading a raw
     * file labelled as JPEG). Deletes the raw on success to reclaim disk.
     */
    private static String encodeRawFrame(String rawPath, int w, int h) {
        if (rawPath == null || rawPath.isEmpty()) return null;
        java.io.File raw = new java.io.File(rawPath);
        if (!raw.exists() || raw.length() == 0) return null;
        String jpgPath = rawPath.replaceAll("\\.raw$", "") + ".jpg";
        java.io.FileInputStream fis = null;
        java.io.FileOutputStream fos = null;
        android.graphics.Bitmap bmp = null;
        try {
            int bytes = w * h * 4;
            if (raw.length() < bytes) return null;  // truncated dump
            byte[] buf = new byte[bytes];
            fis = new java.io.FileInputStream(raw);
            int total = 0;
            while (total < bytes) {
                int n = fis.read(buf, total, bytes - total);
                if (n < 0) break;
                total += n;
            }
            if (total < bytes) return null;
            bmp = android.graphics.Bitmap.createBitmap(w, h, android.graphics.Bitmap.Config.ARGB_8888);
            bmp.copyPixelsFromBuffer(java.nio.ByteBuffer.wrap(buf));
            fos = new java.io.FileOutputStream(jpgPath);
            bmp.compress(android.graphics.Bitmap.CompressFormat.JPEG, 80, fos);
            fos.flush();
            raw.delete();
            return jpgPath;
        } catch (Throwable t) {
            Log.w(TAG, "encodeRawFrame failed for " + rawPath, t);
            return null;
        } finally {
            if (bmp != null) try { bmp.recycle(); } catch (Throwable ignored) {}
            if (fis != null) try { fis.close(); } catch (Throwable ignored) {}
            if (fos != null) try { fos.close(); } catch (Throwable ignored) {}
        }
    }

    /**
     * Copy a crash-time attachment into the uploader's cache dir so the
     * rolling buffers in the current session can't overwrite it before the
     * multipart POST reads it. Returns the snapshot path, or null if the
     * source is missing / copy fails (caller skips attaching).
     */
    private static String snapshotAttachment(String src, File destDir, String destName) {
        if (src == null || src.isEmpty()) return null;
        File in = new File(src);
        if (!in.exists() || in.length() == 0) return null;
        File out = new File(destDir, destName);
        java.io.FileInputStream fis = null;
        java.io.FileOutputStream fos = null;
        try {
            fis = new java.io.FileInputStream(in);
            fos = new java.io.FileOutputStream(out);
            byte[] buf = new byte[8192];
            int n;
            while ((n = fis.read(buf)) > 0) fos.write(buf, 0, n);
            fos.flush();
            return out.getAbsolutePath();
        } catch (Throwable t) {
            Log.w(TAG, "snapshotAttachment failed for " + src, t);
            try { out.delete(); } catch (Throwable ignored) {}
            return null;
        } finally {
            if (fis != null) try { fis.close(); } catch (Throwable ignored) {}
            if (fos != null) try { fos.close(); } catch (Throwable ignored) {}
        }
    }

    /**
     * Parse the native breadcrumb ring dump (fixed-size binary records)
     * into a JSONArray suitable for crash_events.breadcrumbs. Returns null
     * on any I/O / format error — caller skips attaching.
     *
     * Must stay in lock-step with {@link BugpunchInput#ENTRY_SIZE} and the
     * C struct in bp.c::s_input_buf.
     */
    private static JSONArray parseBreadcrumbsFile(String path, int count, int stride) {
        if (path == null || stride <= 0 || count <= 0) return null;
        java.io.File f = new java.io.File(path);
        if (!f.exists() || f.length() < (long) count * stride) return null;
        java.io.FileInputStream fis = null;
        try {
            byte[] bytes = new byte[count * stride];
            fis = new java.io.FileInputStream(f);
            int total = 0;
            while (total < bytes.length) {
                int n = fis.read(bytes, total, bytes.length - total);
                if (n < 0) break;
                total += n;
            }
            if (total < bytes.length) return null;
            java.nio.ByteBuffer bb = java.nio.ByteBuffer.wrap(bytes)
                .order(java.nio.ByteOrder.LITTLE_ENDIAN);
            JSONArray out = new JSONArray();
            for (int i = 0; i < count; i++) {
                int base = i * stride;
                bb.position(base);
                long t = bb.getLong();
                int type = bb.getInt();
                float x = bb.getFloat();
                float y = bb.getFloat();
                int keyCode = bb.getInt();
                String hpath = readCString(bytes, base + 24, BugpunchInput.PATH_LEN);
                String scene = readCString(bytes, base + 24 + BugpunchInput.PATH_LEN,
                    BugpunchInput.SCENE_LEN);
                String label = readCString(bytes,
                    base + 24 + BugpunchInput.PATH_LEN + BugpunchInput.SCENE_LEN,
                    BugpunchInput.LABEL_LEN);
                JSONObject o = new JSONObject();
                o.put("t", t);
                o.put("type", eventTypeName(type));
                if (type <= BugpunchInput.TYPE_TOUCH_MOVE) {
                    o.put("x", x);
                    o.put("y", y);
                }
                if (type == BugpunchInput.TYPE_KEY_DOWN || type == BugpunchInput.TYPE_KEY_UP) {
                    o.put("keyCode", keyCode);
                }
                if (type == BugpunchInput.TYPE_CUSTOM) {
                    // Custom breadcrumb — path carries the caller's category,
                    // label carries the message. Promote them to dedicated
                    // fields so the dashboard doesn't have to reinterpret
                    // a touch-shaped payload.
                    if (!hpath.isEmpty()) o.put("category", hpath);
                    if (!label.isEmpty()) o.put("message", label);
                } else {
                    if (!hpath.isEmpty()) o.put("path", hpath);
                    if (!label.isEmpty()) o.put("label", label);
                }
                if (!scene.isEmpty()) o.put("scene", scene);
                out.put(o);
            }
            return out;
        } catch (Throwable t) {
            Log.w(TAG, "parseBreadcrumbsFile failed for " + path, t);
            return null;
        } finally {
            if (fis != null) try { fis.close(); } catch (Throwable ignored) {}
        }
    }
}
