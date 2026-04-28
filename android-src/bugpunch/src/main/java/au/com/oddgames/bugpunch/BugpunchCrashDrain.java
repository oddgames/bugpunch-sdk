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
                // Crash-survivable video. The native handler emits `video:`
                // pointing at the bp_video.dat ring; remux to mp4 here (we
                // have a live process and MediaMuxer is fine to use), then
                // attach. The remuxer returns null for empty / unrecoverable
                // rings, in which case the crash uploads without video.
                // Video ring goes in as a DeferredAttachment so the remux
                // only runs if the server's phase-1 response says it wants
                // video for this fingerprint. The ring file's source path
                // gets cleaned up automatically by the uploader after phase
                // 2 — whether or not we actually remuxed it.
                // Game-data attachment rules. Pre-resolved paths come from
                // BugpunchNative.cs (Unity tokens like [PersistentDataPath]
                // already expanded). Each match goes up gated on
                // `attach_<rule_name>` so the server can budget per-rule.
                appendAttachmentRuleFiles(uploadCache, snapPrefix, files);

                List<BugpunchUploader.DeferredAttachment> deferred = new ArrayList<>();
                String videoRing = resolveVideoRingPath(activity, extractField(raw, "video"));
                if (videoRing != null) {
                    deferred.add(new BugpunchUploader.DeferredAttachment(
                        "video", "video.mp4", "video/mp4",
                        "video", "video_ring", videoRing));
                } else {
                    // No ring on disk this session — could be: recorder never
                    // started (no consent / EnterDebugMode never called), the
                    // preserve step missed it, or the file was deleted. Push a
                    // sentinel so the uploader emits a `bugpunchDiag_video`
                    // text attachment with the reason instead of silently
                    // omitting video. The sentinel sourcePath is non-empty so
                    // the deferred validator passes; the remuxer will return
                    // `ring_missing` since the file doesn't exist.
                    deferred.add(new BugpunchUploader.DeferredAttachment(
                        "video", "video.mp4", "video/mp4",
                        "video", "video_ring",
                        new java.io.File(activity.getCacheDir(), "bp_video_absent").getAbsolutePath()));
                }
                if (anrShotPaths != null) {
                    for (int i = 0; i < anrShotPaths.length; i++) {
                        files.add(BugpunchUploader.FileAttachment.jpegGated(
                            "anr_screenshot", i, anrShotPaths[i], "anr_screenshots"));
                    }
                }

                // Storyboard ring — bp.c dumped raw RGBA per slot + a packed
                // header file. Encode each slot to JPEG, snapshot into the
                // upload cache, embed metadata in the body so the server has
                // the per-frame {label, path, scene, x, y, ...} even if the
                // budget filter strips the JPEGs themselves.
                attachStoryboardFiles(raw, uploadCache, snapPrefix, files, body);
                // Native crashes always go through preflight — the server
                // decides whether logs/screenshots/video are worth uploading
                // based on per-fingerprint budget. If budget is spent,
                // phase 2 is skipped entirely and attachments are cleaned up.
                BugpunchUploader.enqueuePreflight(activity, preflightUrl,
                    enrichTemplate, apiKey, body.toString(), files, deferred);
                BugpunchCrashHandler.deleteCrashFile(path);
                Log.i(TAG, "queued pending crash: " + path);

                // Recover the post-crash log tail into the live log_session.
                // logsPath points at the dumped in-process log ring — most of
                // it was already streamed via the WebSocket sink, but the last
                // ~100 ms of buffered-but-unflushed lines got cut off when the
                // process died. Server walks the prefix and drops overlap so
                // re-deliveries are idempotent. Best-effort: a failure here
                // never blocks the crash upload.
                String crashSessionId = extractField(raw, "session_id");
                if (crashSessionId != null && !crashSessionId.isEmpty() && logsPath != null) {
                    enqueueCrashTail(activity, base, apiKey, crashSessionId, logsPath);
                }
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

    /**
     * Walk the configured attachment rules and stage matching files into
     * the upload cache. Each match is added as a {@link
     * BugpunchUploader.FileAttachment} gated on {@code attach_<name>} so
     * the server's per-fingerprint budget can refuse the bytes when
     * already collected. Files larger than the rule's {@code maxBytes} are
     * skipped silently — no SDK-level error overlay for "your save file
     * is too big to attach".
     *
     * <p>Per-rule cap: at most {@link #MAX_FILES_PER_RULE} files attached,
     * sorted by mtime descending (newest first), to stop a rule pointing
     * at a 5,000-file directory from blowing the manifest size. Drain
     * stays best-effort — any rule throwing inside its loop is logged and
     * skipped, the rest still run.
     */
    private static final int MAX_FILES_PER_RULE = 8;
    private static void appendAttachmentRuleFiles(File uploadCache, String snapPrefix,
                                                  List<BugpunchUploader.FileAttachment> files) {
        JSONArray rules = BugpunchRuntime.getAttachmentRules();
        if (rules == null || rules.length() == 0) return;
        for (int i = 0; i < rules.length(); i++) {
            try {
                JSONObject r = rules.getJSONObject(i);
                String name = r.optString("name", "rule_" + i);
                String dirPath = r.optString("path", "");
                String pattern = r.optString("pattern", "*");
                long maxBytes = r.optLong("maxBytes", 1024 * 1024);
                if (dirPath.isEmpty()) continue;

                File dir = new File(dirPath);
                if (!dir.isDirectory()) continue;
                File[] candidates = dir.listFiles();
                if (candidates == null) continue;

                java.nio.file.PathMatcher matcher;
                try {
                    matcher = java.nio.file.FileSystems.getDefault()
                        .getPathMatcher("glob:" + pattern);
                } catch (Throwable t) {
                    Log.w(TAG, "rule " + name + ": bad pattern '" + pattern + "'", t);
                    continue;
                }

                List<File> matched = new ArrayList<>();
                for (File f : candidates) {
                    if (!f.isFile()) continue;
                    if (f.length() > maxBytes) continue;
                    if (!matcher.matches(java.nio.file.Paths.get(f.getName()))) continue;
                    matched.add(f);
                }
                // Newest-first: typical "save data" use case wants the
                // most recent autosave, not 6-month-old slot files.
                matched.sort((a, b) -> Long.compare(b.lastModified(), a.lastModified()));
                int kept = 0;
                String requires = "attach_" + name;
                for (File f : matched) {
                    if (kept >= MAX_FILES_PER_RULE) break;
                    String snapName = snapPrefix + "attach_" + name + "_" + f.getName();
                    String snap = snapshotAttachment(f.getAbsolutePath(), uploadCache, snapName);
                    if (snap == null) continue;
                    String mime = guessMimeForAttachment(f.getName());
                    files.add(new BugpunchUploader.FileAttachment(
                        kept == 0 ? name : name + "_" + kept,
                        f.getName(), mime, snap, requires));
                    kept++;
                }
            } catch (Throwable t) {
                Log.w(TAG, "attachment rule " + i + " failed", t);
            }
        }
    }

    /** Cheap mime guess from extension. The server stores these as opaque
     *  blobs anyway — getting it slightly wrong (text/plain vs application/json)
     *  doesn't affect storage, only what the dashboard's preview tries to
     *  render. */
    private static String guessMimeForAttachment(String filename) {
        String n = filename.toLowerCase();
        if (n.endsWith(".json")) return "application/json";
        if (n.endsWith(".txt") || n.endsWith(".log")) return "text/plain";
        if (n.endsWith(".xml")) return "application/xml";
        if (n.endsWith(".csv")) return "text/csv";
        if (n.endsWith(".png")) return "image/png";
        if (n.endsWith(".jpg") || n.endsWith(".jpeg")) return "image/jpeg";
        if (n.endsWith(".dat") || n.endsWith(".sav") || n.endsWith(".save")) return "application/octet-stream";
        return "application/octet-stream";
    }

    /**
     * Resolve a `video:` field from a .crash file to an actually-readable
     * ring path. bp.c always writes the live-recorder path (e.g.
     * {@code .../cache/bp_video.dat}); on next launch BugpunchRuntime renames
     * that file to {@code bp_video.prev.dat} to keep it from being clobbered
     * by the new session's recorder. So the path bp.c stamped into the crash
     * file is usually stale by the time drain reads it — fall back to the
     * parked prev file when that's the case.
     *
     * @return absolute path to a ring file that exists, or null if neither
     *         the original nor the prev path resolves.
     */
    private static String resolveVideoRingPath(android.content.Context ctx, String fromCrash) {
        if (fromCrash != null && !fromCrash.isEmpty()) {
            File f = new File(fromCrash);
            if (f.exists() && f.length() > 0) return f.getAbsolutePath();
        }
        File prev = BugpunchCrashHandler.videoRingPrevFile(ctx);
        if (prev.exists() && prev.length() > 0) return prev.getAbsolutePath();
        return null;
    }

    /** Extract a header field from a crash file's key:value lines (before ---STACK---). */
    private static String extractField(String raw, String key) {
        for (String line : raw.split("\\r?\\n")) {
            if (line.startsWith("---")) break;
            if (line.startsWith(key + ":")) return line.substring(key.length() + 1);
        }
        return null;
    }

    /**
     * POST the previously-dumped log ring to
     * {@code /api/v1/log-sessions/<id>/append-crash-tail} so the live Logs
     * page sees everything up to the moment of death (not just the last
     * successful WebSocket flush). The server walks the prefix of the body
     * line-by-line against the existing file's tail and skips overlap, so
     * re-deliveries are idempotent.
     *
     * <p>Best-effort: a network failure here doesn't roll back the crash
     * upload — the user still gets the crash event with its `logs.log`
     * attachment, just without the tail merged into the live session. Runs
     * on its own thread so a slow upload doesn't block the rest of the
     * drain loop.
     */
    private static void enqueueCrashTail(android.content.Context ctx, final String base,
                                         final String apiKey, final String sessionId,
                                         final String logsPath) {
        final java.io.File f = new java.io.File(logsPath);
        if (!f.exists() || f.length() <= 0) return;
        new Thread(new Runnable() { @Override public void run() {
            java.net.HttpURLConnection con = null;
            try {
                byte[] body;
                try (java.io.FileInputStream in = new java.io.FileInputStream(f)) {
                    java.io.ByteArrayOutputStream out = new java.io.ByteArrayOutputStream(
                        (int) Math.min(f.length(), 4 * 1024 * 1024));
                    byte[] buf = new byte[16 * 1024];
                    int n;
                    while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
                    body = out.toByteArray();
                }
                if (body.length == 0) return;

                String url = base + "/api/v1/log-sessions/"
                    + java.net.URLEncoder.encode(sessionId, "UTF-8")
                    + "/append-crash-tail";
                con = (java.net.HttpURLConnection) new java.net.URL(url).openConnection();
                con.setRequestMethod("POST");
                con.setConnectTimeout(10_000);
                con.setReadTimeout(20_000);
                con.setDoOutput(true);
                con.setFixedLengthStreamingMode(body.length);
                con.setRequestProperty("Content-Type", "text/plain; charset=utf-8");
                con.setRequestProperty("X-API-Key", apiKey);
                try (java.io.OutputStream os = con.getOutputStream()) { os.write(body); }
                int code = con.getResponseCode();
                if (code >= 200 && code < 300) {
                    Log.i(TAG, "crash tail appended (sessionId=" + sessionId + ", " + body.length + " bytes)");
                } else {
                    Log.w(TAG, "crash tail append HTTP " + code + " for sessionId=" + sessionId);
                }
            } catch (Throwable t) {
                Log.w(TAG, "crash tail append failed for sessionId=" + sessionId, t);
            } finally {
                if (con != null) try { con.disconnect(); } catch (Throwable ignored) {}
            }
        }}, "bugpunch-crash-tail").start();
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
     * Storyboard ring — re-hydrate the per-slot RGBA blobs + packed header
     * file the signal handler wrote, JPEG-encode each frame, and attach.
     * The metadata array goes into the body too so the server keeps it even
     * if its budget filter strips the JPEGs.
     */
    private static void attachStoryboardFiles(String raw, File uploadCache, String snapPrefix,
            List<BugpunchUploader.FileAttachment> files, JSONObject body) {
        String pathBase = extractField(raw, "storyboard_path_base");
        if (pathBase == null || pathBase.isEmpty()) return;

        int count = 0, headerBytes = 0;
        try {
            String c = extractField(raw, "storyboard_count");
            if (c != null) count = Integer.parseInt(c);
            String hb = extractField(raw, "storyboard_header_bytes");
            if (hb != null) headerBytes = Integer.parseInt(hb);
        } catch (NumberFormatException ignored) {}
        if (count <= 0 || headerBytes <= 0) return;

        // Read packed headers — `<base>.bin` contains `count * headerBytes` bytes.
        byte[] headerBlob;
        java.io.File binFile = new java.io.File(pathBase + ".bin");
        if (!binFile.exists() || binFile.length() < (long)count * headerBytes) return;
        try (java.io.FileInputStream fis = new java.io.FileInputStream(binFile)) {
            headerBlob = new byte[count * headerBytes];
            int total = 0;
            while (total < headerBlob.length) {
                int n = fis.read(headerBlob, total, headerBlob.length - total);
                if (n < 0) break;
                total += n;
            }
            if (total < headerBlob.length) return;
        } catch (Throwable t) {
            Log.w(TAG, "storyboard: header read failed", t);
            return;
        }

        JSONArray frames = new JSONArray();
        try {
            for (int i = 0; i < count; i++) {
                int off = i * headerBytes;
                long tsMs = readLittleLong(headerBlob, off);
                float x   = readLittleFloat(headerBlob, off + 8);
                float y   = readLittleFloat(headerBlob, off + 12);
                int screenW = readLittleInt(headerBlob, off + 16);
                int screenH = readLittleInt(headerBlob, off + 20);
                int w = readLittleInt(headerBlob, off + 24);
                int h = readLittleInt(headerBlob, off + 28);
                // pixelsLen at +32 is informational (we already have the file size).
                String path  = readFixedUtf8(headerBlob, off + 36, BugpunchStoryboard.PATH_LEN);
                String label = readFixedUtf8(headerBlob, off + 36 + BugpunchStoryboard.PATH_LEN,
                                             BugpunchStoryboard.LABEL_LEN);
                String scene = readFixedUtf8(headerBlob, off + 36 + BugpunchStoryboard.PATH_LEN
                                             + BugpunchStoryboard.LABEL_LEN,
                                             BugpunchStoryboard.SCENE_LEN);

                String rawPath = pathBase + "_" + i + ".rgba";
                String jpgPath = encodeRawFrame(rawPath, w, h);
                String fileName = null;
                if (jpgPath != null) {
                    String snapName = snapPrefix + "story_" + i + ".jpg";
                    String snap = snapshotAttachment(jpgPath, uploadCache, snapName);
                    new java.io.File(jpgPath).delete();
                    if (snap != null) {
                        files.add(BugpunchUploader.FileAttachment.jpegGated(
                            "storyboard_frame", i, snap, "storyboard_frames"));
                        fileName = "storyboard_frame_" + i + ".jpg";
                    }
                }

                JSONObject f = new JSONObject();
                f.put("tsMs",    tsMs);
                f.put("x",       x);
                f.put("y",       y);
                f.put("screenW", screenW);
                f.put("screenH", screenH);
                f.put("w",       w);
                f.put("h",       h);
                if (path  != null) f.put("path",  path);
                if (label != null) f.put("label", label);
                if (scene != null) f.put("scene", scene);
                if (fileName != null) f.put("file", fileName);
                frames.put(f);
            }
            if (frames.length() > 0) body.put("storyboardFrames", frames);
            // Cleanup the .bin file — its data is now in the body.
            try { binFile.delete(); } catch (Throwable ignored) {}
        } catch (Throwable t) {
            Log.w(TAG, "storyboard: frame parse failed", t);
        }
    }

    private static long readLittleLong(byte[] b, int o) {
        return  ((long)(b[o]   & 0xff))
             | (((long)(b[o+1] & 0xff)) << 8)
             | (((long)(b[o+2] & 0xff)) << 16)
             | (((long)(b[o+3] & 0xff)) << 24)
             | (((long)(b[o+4] & 0xff)) << 32)
             | (((long)(b[o+5] & 0xff)) << 40)
             | (((long)(b[o+6] & 0xff)) << 48)
             | (((long)(b[o+7] & 0xff)) << 56);
    }
    private static int readLittleInt(byte[] b, int o) {
        return  (b[o]   & 0xff)
             | ((b[o+1] & 0xff) << 8)
             | ((b[o+2] & 0xff) << 16)
             | ((b[o+3] & 0xff) << 24);
    }
    private static float readLittleFloat(byte[] b, int o) {
        return Float.intBitsToFloat(readLittleInt(b, o));
    }
    private static String readFixedUtf8(byte[] b, int o, int maxBytes) {
        int n = 0;
        while (n < maxBytes && b[o + n] != 0) n++;
        try { return new String(b, o, n, "UTF-8"); }
        catch (java.io.UnsupportedEncodingException e) { return ""; }
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
