package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.zip.GZIPOutputStream;

/**
 * Builds and enqueues in-process reports (bug, feedback, exception). Entry
 * points are {@link #reportBug}, {@link #submitReport}, {@link #addTrace}
 * and {@link #addTraceScreenshot}, all called via JNI from the Unity C# SDK
 * (or from other Java classes like {@code BugpunchDebugWidget} /
 * {@code BugpunchReportActivity}).
 *
 * <p>Gathers attachments (screenshots, traces, logs, video dumps), builds
 * the metadata JSON, and hands the manifest to {@link BugpunchUploader} —
 * which owns the retry/persistence state machine.  Video dumps come from
 * {@link BugpunchDebugMode#dumpRingIfRunning(Activity)} which is a cheap
 * no-op unless the user opted into recording this session.
 *
 * <p>Depends on {@link BugpunchRuntime} only for shared state accessors
 * (config, metadata, custom data, fps, session id) — never called back
 * from Runtime.
 */
public final class BugpunchReportingService {
    private static final String TAG = "[Bugpunch.ReportingService]";

    private BugpunchReportingService() {}

    // ── Trace events (ring buffer, bundled into next report) ──

    /** A single trace event. */
    private static final class TraceEvent {
        final long timestampMs;
        final String label;
        final String tagsJson;    // nullable, raw JSON object string
        volatile String screenshotPath; // nullable, set async after capture
        TraceEvent(long ts, String label, String tagsJson, String shot) {
            this.timestampMs = ts;
            this.label = label;
            this.tagsJson = tagsJson;
            this.screenshotPath = shot;
        }
    }

    private static final int TRACE_MAX = 50;
    private static final Object sTraceLock = new Object();
    private static final ArrayList<TraceEvent> sTraces = new ArrayList<>();

    public static void addTrace(String label, String tagsJson) {
        if (label == null) return;
        TraceEvent ev = new TraceEvent(System.currentTimeMillis(), label, tagsJson, null);
        synchronized (sTraceLock) {
            sTraces.add(ev);
            while (sTraces.size() > TRACE_MAX) sTraces.remove(0);
        }
    }

    public static void addTraceScreenshot(final String label, final String tagsJson) {
        if (label == null) return;
        Activity activity = BugpunchUnity.currentActivity();
        if (activity == null) { addTrace(label, tagsJson); return; }
        final String shotPath = activity.getCacheDir().getAbsolutePath()
            + "/bp_trace_" + System.nanoTime() + ".jpg";
        // Reserve the slot immediately so ordering is deterministic; fill in
        // the path once capture succeeds.
        final TraceEvent ev = new TraceEvent(System.currentTimeMillis(), label, tagsJson, null);
        synchronized (sTraceLock) {
            sTraces.add(ev);
            while (sTraces.size() > TRACE_MAX) sTraces.remove(0);
        }
        BugpunchScreenshot.captureThen(shotPath, 80, new Runnable() {
            @Override public void run() { ev.screenshotPath = shotPath; }
        });
    }

    /**
     * Drain the trace buffer. Returns the drained events (oldest first) and
     * clears the buffer. Called from submitReport paths so the next report
     * starts fresh.
     */
    private static List<TraceEvent> drainTraces() {
        synchronized (sTraceLock) {
            if (sTraces.isEmpty()) return null;
            ArrayList<TraceEvent> copy = new ArrayList<>(sTraces);
            sTraces.clear();
            return copy;
        }
    }

    /**
     * Serialize traces to a temp JSON file and collect screenshot paths.
     * Returns {tracesJsonPath, screenshotPaths[]} or null if no traces.
     */
    private static Object[] prepareTraceAttachments(Activity activity) {
        List<TraceEvent> evs = drainTraces();
        if (evs == null || evs.isEmpty()) return null;
        try {
            JSONArray arr = new JSONArray();
            ArrayList<String> shots = new ArrayList<>();
            for (TraceEvent ev : evs) {
                JSONObject o = new JSONObject();
                o.put("ts", ev.timestampMs);
                o.put("label", ev.label);
                if (ev.tagsJson != null && !ev.tagsJson.isEmpty()) {
                    try { o.put("tags", new JSONObject(ev.tagsJson)); }
                    catch (JSONException ignore) {}
                }
                if (ev.screenshotPath != null) {
                    java.io.File f = new java.io.File(ev.screenshotPath);
                    if (f.exists() && f.length() > 0) {
                        o.put("screenshotIndex", shots.size());
                        shots.add(ev.screenshotPath);
                    }
                }
                arr.put(o);
            }
            String path = activity.getCacheDir().getAbsolutePath()
                + "/bp_traces_" + System.nanoTime() + ".json";
            java.io.FileOutputStream fos = new java.io.FileOutputStream(path);
            try { fos.write(arr.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8)); }
            finally { fos.close(); }
            return new Object[] { path, shots.toArray(new String[0]) };
        } catch (Throwable t) {
            Log.w(TAG, "prepareTraceAttachments failed", t);
            BugpunchSdkErrorOverlay.reportThrowable("BugpunchReportingService", "prepareTraceAttachments", t);
            return null;
        }
    }

    // ── Trigger (from shake, from C# exception handler, from game code) ──

    /**
     * Fire a bug report. Captures screenshot + dumps video + builds payload +
     * enqueues upload. Returns immediately.
     *
     * @param type        "bug" | "feedback" | "exception" | "crash"
     * @param title       short title
     * @param description long-form description (may include stack trace)
     * @param extraJson   extra key/values (e.g. exception-specific fields) merged into customData
     */
    public static void reportBug(final String type, final String title,
                                 final String description, final String extraJson) {
        if (!BugpunchRuntime.isStarted()) { Log.w(TAG, "reportBug before start"); return; }
        final Activity activity = BugpunchUnity.currentActivity();
        if (activity == null) return;

        JSONObject config = BugpunchRuntime.getConfig();

        // Cooldown for auto exception reports.
        if ("exception".equals(type)) {
            long cooldownMs = (long)(config.optDouble("autoReportCooldownSeconds", 30.0) * 1000);
            long now = System.currentTimeMillis();
            if (now - BugpunchRuntime.getLastAutoReport() < cooldownMs) return;
            BugpunchRuntime.setLastAutoReport(now);
        }

        // User-initiated bug reports show the form; silent reports
        // (exceptions, crashes, feedback) skip it and enqueue directly.
        final boolean showForm = "bug".equals(type);
        final String shotPath = activity.getCacheDir().getAbsolutePath()
            + "/bp_shot_" + System.nanoTime() + ".jpg";

        if (showForm) {
            // Guard: if a report form is already open, ignore the second trigger.
            if (BugpunchRuntime.isReportInProgress()) { Log.i(TAG, "report already in progress, ignoring"); return; }
            BugpunchRuntime.setReportInProgress(true);
            // Capture the GAME state first, THEN launch the form. If we fired
            // capture and launch in the same instant the form would already be
            // covering Unity by the time PixelCopy ran, and the screenshot
            // would show the form (or a transitional black frame).
            BugpunchScreenshot.captureThen(shotPath, 85, new Runnable() {
                @Override public void run() {
                    BugpunchReportActivity.launch(shotPath, title, description);
                }
            });
            return;
        }

        // Silent path — assemble manifest directly.
        final DumpResult dump = dumpRingAndTouches(activity);

        // Context screenshot — last frame from the rolling buffer (~1s before
        // the event). Additional to the event screenshot taken below.
        String contextShotPath = null;
        long contextShotTs = 0;
        if (BugpunchScreenshot.hasLastFrame()) {
            contextShotPath = activity.getCacheDir().getAbsolutePath()
                + "/bp_ctx_" + System.nanoTime() + ".jpg";
            contextShotTs = BugpunchScreenshot.getLastFrameTimestamp();
            if (!BugpunchScreenshot.writeLastFrame(contextShotPath, 85)) {
                contextShotPath = null;
            }
        }

        // Event screenshot — captured right now at the moment of the event.
        BugpunchScreenshot.capture("rb_" + System.nanoTime(), shotPath, 85, "", "");

        JSONObject extra = parseOr(extraJson);
        long eventShotTs = System.currentTimeMillis();

        // Build screenshots manifest — every screenshot gets a timestamp.
        JSONArray screenshotsMeta = new JSONArray();
        try {
            if (contextShotPath != null && contextShotTs > 0) {
                JSONObject ctx = new JSONObject();
                ctx.put("type", "context");
                ctx.put("timestampMs", contextShotTs);
                ctx.put("field", "context_screenshot");
                screenshotsMeta.put(ctx);
            }
            JSONObject evt = new JSONObject();
            evt.put("type", "event");
            evt.put("timestampMs", eventShotTs);
            evt.put("field", "screenshot");
            screenshotsMeta.put(evt);
        } catch (JSONException ignored) {}
        try { extra.put("screenshots", screenshotsMeta); }
        catch (JSONException ignored) {}

        String logsText = BugpunchLogReader.snapshotText();
        String logsGzPath = writeGzipLogs(activity, logsText);
        String metadataJson = buildMetadataJson(type, title, description,
            extra, /* reporterEmail */ null, /* severity */ null, dump);
        Object[] traceAttach = prepareTraceAttachments(activity);
        String tracesJsonPath = traceAttach != null ? (String) traceAttach[0] : null;
        String[] traceShots = traceAttach != null ? (String[]) traceAttach[1] : null;

        // Route exception/crash/anr through the two-phase preflight so the
        // server can reject heavy attachments (logs/screenshot/video) when
        // the per-fingerprint budget is spent — saving bandwidth on spammy
        // crashes. bug/feedback keep the single-phase multipart path since
        // they're user-initiated and rare.
        if ("exception".equals(type) || "crash".equals(type) || "anr".equals(type)) {
            enqueuePreflight(activity, metadataJson, shotPath, contextShotPath,
                dump.videoPath, logsGzPath, tracesJsonPath, traceShots);
        } else {
            enqueueManifest(activity, endpointFor(type), metadataJson, shotPath, dump.videoPath, null,
                tracesJsonPath, traceShots, logsGzPath, contextShotPath);
        }
    }

    /**
     * Two-phase ingest for auto-reported exceptions / ANRs / native crashes.
     * Phase 1 posts the lightweight metadata JSON to /api/crashes; server
     * responds with eventId + collect[] naming which heavy fields it wants.
     * Phase 2 posts those fields as multipart to /api/crashes/events/:id/enrich.
     *
     * The uploader owns the full state machine — this method just builds the
     * attachment list with `requires` hints so the uploader can filter for
     * phase 2 automatically.
     */
    private static void enqueuePreflight(Activity activity, String metadataJson,
                                         String shotPath, String contextShotPath,
                                         String videoPath, String logsGzPath,
                                         String tracesJsonPath, String[] traceScreenshotPaths) {
        JSONObject config = BugpunchRuntime.getConfig();
        String serverUrl = config.optString("serverUrl", "");
        String apiKey = config.optString("apiKey", "");
        if (serverUrl.isEmpty() || apiKey.isEmpty()) {
            Log.w(TAG, "serverUrl/apiKey not configured — skipping");
            return;
        }
        String base = serverUrl.replaceAll("/+$", "");
        String preflightUrl = base + "/api/crashes";
        String enrichTemplate = base + "/api/crashes/events/{id}/enrich";

        List<BugpunchUploader.FileAttachment> files = new ArrayList<>();
        if (contextShotPath != null && !contextShotPath.isEmpty())
            files.add(BugpunchUploader.FileAttachment.jpegGated(
                "context_screenshot", contextShotPath, "context_screenshot"));
        if (shotPath != null && !shotPath.isEmpty())
            files.add(BugpunchUploader.FileAttachment.jpegGated(
                "screenshot", shotPath, "screenshot"));
        if (videoPath != null && !videoPath.isEmpty())
            files.add(new BugpunchUploader.FileAttachment(
                "video", "video.mp4", "video/mp4", videoPath, "video"));
        if (logsGzPath != null && !logsGzPath.isEmpty())
            files.add(new BugpunchUploader.FileAttachment(
                "logs", "logs.log.gz", "application/gzip", logsGzPath, "logs"));
        if (tracesJsonPath != null && !tracesJsonPath.isEmpty())
            files.add(new BugpunchUploader.FileAttachment(
                "traces", "traces.json", "application/json", tracesJsonPath, "traces"));
        if (traceScreenshotPaths != null) {
            for (int i = 0; i < traceScreenshotPaths.length; i++) {
                String p = traceScreenshotPaths[i];
                if (p != null && !p.isEmpty())
                    files.add(BugpunchUploader.FileAttachment.jpegGated(
                        "trace", i, p, "traces"));
            }
        }

        BugpunchUploader.enqueuePreflight(activity, preflightUrl, enrichTemplate,
            apiKey, metadataJson, files);
    }

    /**
     * Called from {@link BugpunchReportActivity} when the user taps Send.
     * Builds the manifest including the annotations layer (if any) and hands
     * to the uploader.
     */
    public static void submitReport(String title, String description, String email,
                                    String severity, String screenshotPath,
                                    String annotationsPath) {
        submitReport(title, description, email, severity, screenshotPath, annotationsPath, null);
    }

    public static void submitReport(String title, String description, String email,
                                    String severity, String screenshotPath,
                                    String annotationsPath, String[] extraScreenshots) {
        if (!BugpunchRuntime.isStarted()) return;
        Activity activity = BugpunchUnity.currentActivity();
        if (activity == null) return;

        DumpResult dump = dumpRingAndTouches(activity);

        // Build screenshots metadata with timestamps
        JSONObject extra = new JSONObject();
        JSONArray screenshotsMeta = new JSONArray();
        try {
            // Context screenshot from rolling buffer
            String contextPath = null;
            long contextTs = 0;
            if (BugpunchScreenshot.hasLastFrame()) {
                contextPath = activity.getCacheDir().getAbsolutePath()
                    + "/bp_ctx_" + System.nanoTime() + ".jpg";
                contextTs = BugpunchScreenshot.getLastFrameTimestamp();
                if (!BugpunchScreenshot.writeLastFrame(contextPath, 85)) contextPath = null;
            }
            if (contextPath != null) {
                JSONObject ctx = new JSONObject();
                ctx.put("type", "context");
                ctx.put("timestampMs", contextTs);
                ctx.put("field", "context_screenshot");
                screenshotsMeta.put(ctx);
            }
            // Event screenshot
            JSONObject evt = new JSONObject();
            evt.put("type", "event");
            evt.put("timestampMs", System.currentTimeMillis());
            evt.put("field", "screenshot");
            screenshotsMeta.put(evt);
            // Extra manual screenshots
            if (extraScreenshots != null) {
                for (int i = 0; i < extraScreenshots.length; i++) {
                    JSONObject es = new JSONObject();
                    es.put("type", "manual");
                    es.put("timestampMs", 0);
                    es.put("field", "extra_screenshot_" + i);
                    screenshotsMeta.put(es);
                }
            }
            extra.put("screenshots", screenshotsMeta);
        } catch (JSONException ignored) {}

        String logsText = BugpunchLogReader.snapshotText();
        String logsGzPath = writeGzipLogs(activity, logsText);
        String metadataJson = buildMetadataJson("bug", title, description,
            extra, email, severity, dump);
        Object[] traceAttach = prepareTraceAttachments(activity);
        String tracesJsonPath = traceAttach != null ? (String) traceAttach[0] : null;
        String[] traceShots = traceAttach != null ? (String[]) traceAttach[1] : null;

        // Build file attachment list
        JSONObject config = BugpunchRuntime.getConfig();
        String serverUrl = config.optString("serverUrl", "");
        String apiKey = config.optString("apiKey", "");
        if (serverUrl.isEmpty() || apiKey.isEmpty()) return;
        String url = serverUrl.replaceAll("/+$", "") + "/api/reports/bug";
        List<BugpunchUploader.FileAttachment> files = new ArrayList<>();
        if (screenshotPath != null && !screenshotPath.isEmpty())
            files.add(BugpunchUploader.FileAttachment.jpeg("screenshot", screenshotPath));
        if (dump.videoPath != null && !dump.videoPath.isEmpty())
            files.add(new BugpunchUploader.FileAttachment("video", "video.mp4", "video/mp4", dump.videoPath));
        if (annotationsPath != null && !annotationsPath.isEmpty())
            files.add(new BugpunchUploader.FileAttachment("annotations", "annotations.png", "image/png", annotationsPath));
        if (tracesJsonPath != null && !tracesJsonPath.isEmpty())
            files.add(new BugpunchUploader.FileAttachment("traces", "traces.json", "application/json", tracesJsonPath));
        if (logsGzPath != null && !logsGzPath.isEmpty())
            files.add(new BugpunchUploader.FileAttachment("logs", "logs.log.gz", "application/gzip", logsGzPath));
        if (traceShots != null) {
            for (int i = 0; i < traceShots.length; i++) {
                if (traceShots[i] != null && !traceShots[i].isEmpty())
                    files.add(BugpunchUploader.FileAttachment.jpeg("trace", i, traceShots[i]));
            }
        }
        if (extraScreenshots != null) {
            for (int i = 0; i < extraScreenshots.length; i++) {
                if (extraScreenshots[i] != null && !extraScreenshots[i].isEmpty())
                    files.add(BugpunchUploader.FileAttachment.jpeg("extra_screenshot", i, extraScreenshots[i]));
            }
        }
        BugpunchUploader.enqueue(activity, url, apiKey, metadataJson, files);
    }

    /**
     * Holds the result of dumping the video ring: the MP4 path (or null) and,
     * if any touches landed in the video's PTS window, a JSON array of touch
     * records (rebased to video t=0) and a video descriptor for the report
     * metadata. Mirrors iOS BPDumpRingAndTouches.
     */
    private static final class DumpResult {
        String videoPath;
        JSONArray touches;
        JSONObject videoMeta;
    }

    private static DumpResult dumpRingAndTouches(Activity activity) {
        DumpResult r = new DumpResult();
        // Video ring lives in BugpunchDebugMode (opt-in). If a user enabled
        // recording earlier this session the ring has footage; otherwise
        // dumpRingIfRunning is a cheap no-op and r.videoPath stays null.
        r.videoPath = BugpunchDebugMode.dumpRingIfRunning(activity);
        if (r.videoPath == null) return r;

        BugpunchRecorder rec = BugpunchRecorder.getInstance();
        long startNs = rec.getLastDumpStartNanos();
        long endNs   = rec.getLastDumpEndNanos();
        if (endNs <= startNs) return r;

        try {
            JSONObject v = new JSONObject();
            v.put("durationMs", (int) ((endNs - startNs) / 1_000_000L));
            int w = rec.getWidth(), h = rec.getHeight();
            if (w > 0 && h > 0) {
                v.put("width", w);
                v.put("height", h);
            }
            r.videoMeta = v;
        } catch (JSONException ignored) {}

        String touchesJson = BugpunchTouchRecorder.snapshotJson(startNs, endNs);
        if (touchesJson != null) {
            try { r.touches = new JSONArray(touchesJson); }
            catch (JSONException ignored) {}
        }
        return r;
    }

    /**
     * Build the file attachment list and enqueue via the uploader.
     * All screenshot/video/trace/log paths are assembled into a single
     * {@link BugpunchUploader.FileAttachment} list — no parameter explosion.
     */
    private static void enqueueManifest(Activity activity, String endpointPath,
                                        String metadataJson, String shotPath,
                                        String videoPath, String annotationsPath,
                                        String tracesJsonPath, String[] traceScreenshotPaths,
                                        String logsGzPath) {
        enqueueManifest(activity, endpointPath, metadataJson, shotPath, videoPath,
            annotationsPath, tracesJsonPath, traceScreenshotPaths, logsGzPath, null);
    }

    private static void enqueueManifest(Activity activity, String endpointPath,
                                        String metadataJson, String shotPath,
                                        String videoPath, String annotationsPath,
                                        String tracesJsonPath, String[] traceScreenshotPaths,
                                        String logsGzPath, String contextShotPath) {
        JSONObject config = BugpunchRuntime.getConfig();
        String serverUrl = config.optString("serverUrl", "");
        String apiKey = config.optString("apiKey", "");
        if (serverUrl.isEmpty() || apiKey.isEmpty()) {
            Log.w(TAG, "serverUrl/apiKey not configured — skipping");
            return;
        }
        String url = serverUrl.replaceAll("/+$", "") + endpointPath;

        List<BugpunchUploader.FileAttachment> files = new ArrayList<>();
        if (contextShotPath != null && !contextShotPath.isEmpty())
            files.add(BugpunchUploader.FileAttachment.jpeg("context_screenshot", contextShotPath));
        if (shotPath != null && !shotPath.isEmpty())
            files.add(BugpunchUploader.FileAttachment.jpeg("screenshot", shotPath));
        if (videoPath != null && !videoPath.isEmpty())
            files.add(new BugpunchUploader.FileAttachment("video", "video.mp4", "video/mp4", videoPath));
        if (annotationsPath != null && !annotationsPath.isEmpty())
            files.add(new BugpunchUploader.FileAttachment("annotations", "annotations.png", "image/png", annotationsPath));
        if (tracesJsonPath != null && !tracesJsonPath.isEmpty())
            files.add(new BugpunchUploader.FileAttachment("traces", "traces.json", "application/json", tracesJsonPath));
        if (logsGzPath != null && !logsGzPath.isEmpty())
            files.add(new BugpunchUploader.FileAttachment("logs", "logs.log.gz", "application/gzip", logsGzPath));
        if (traceScreenshotPaths != null) {
            for (int i = 0; i < traceScreenshotPaths.length; i++) {
                String p = traceScreenshotPaths[i];
                if (p != null && !p.isEmpty())
                    files.add(BugpunchUploader.FileAttachment.jpeg("trace", i, p));
            }
        }
        BugpunchUploader.enqueue(activity, url, apiKey, metadataJson, files);
    }

    private static String endpointFor(String type) {
        // exception/crash/anr go through the two-phase preflight path and
        // never hit this function. Only user-initiated bug reports and
        // feedback land here now.
        if ("feedback".equals(type)) return "/api/reports/feedback";
        return "/api/reports/bug";
    }

    private static JSONObject parseOr(String json) {
        if (json == null || json.isEmpty()) return new JSONObject();
        try { return new JSONObject(json); } catch (JSONException e) { return new JSONObject(); }
    }

    /**
     * Gzip the raw logcat ring snapshot to a temp file. Returns the path, or
     * null on I/O failure. Empty snapshots still round-trip — the server
     * synthesizes a stack-trace fallback when the parsed entry list is empty
     * so the Logs tab is never blank when there's something useful to show.
     */
    private static String writeGzipLogs(Activity activity, String logsText) {
        if (logsText == null) logsText = "";
        try {
            File f = new File(activity.getCacheDir(), "bp_logs_" + System.nanoTime() + ".log.gz");
            FileOutputStream fos = new FileOutputStream(f);
            GZIPOutputStream gz = new GZIPOutputStream(fos);
            gz.write(logsText.getBytes("UTF-8"));
            gz.close();
            return f.getAbsolutePath();
        } catch (IOException e) {
            Log.w(TAG, "writeGzipLogs failed", e);
            BugpunchSdkErrorOverlay.reportThrowable("BugpunchReportingService", "writeGzipLogs", e);
            return null;
        }
    }

    private static String buildMetadataJson(String type, String title, String description,
                                            JSONObject extra,
                                            String reporterEmail, String severity,
                                            DumpResult dump) {
        try {
            JSONObject m = new JSONObject();
            m.put("type", type != null ? type : "bug");
            if (title != null) m.put("title", title);
            if (description != null) m.put("description", description);
            if (reporterEmail != null && !reporterEmail.isEmpty())
                m.put("reporterEmail", reporterEmail);
            if (severity != null && !severity.isEmpty())
                m.put("severity", severity);
            m.put("timestamp", java.text.DateFormat.getDateTimeInstance().format(new java.util.Date()));

            JSONObject device = new JSONObject();
            device.put("model", BugpunchRuntime.getMetadata("deviceModel"));
            device.put("os", BugpunchRuntime.getMetadata("osVersion"));
            device.put("platform", "Android");
            device.put("gpu", BugpunchRuntime.getMetadata("gpu"));
            device.put("deviceId", BugpunchRuntime.getMetadata("deviceId"));
            m.put("device", device);

            JSONObject app = new JSONObject();
            app.put("version", BugpunchRuntime.getMetadata("appVersion"));
            app.put("bundleId", BugpunchRuntime.getMetadata("bundleId"));
            app.put("buildCode", BugpunchRuntime.getMetadata("buildCode"));
            app.put("buildFingerprint", BugpunchRuntime.getMetadata("buildFingerprint"));
            app.put("unityVersion", BugpunchRuntime.getMetadata("unityVersion"));
            app.put("branch", BugpunchRuntime.getMetadata("branch"));
            app.put("changeset", BugpunchRuntime.getMetadata("changeset"));
            app.put("scene", BugpunchRuntime.getMetadata("scene"));
            app.put("fps", BugpunchRuntime.getFps());
            app.put("installerMode", BugpunchRuntime.getMetadata("installerMode"));
            m.put("app", app);

            JSONObject custom = new JSONObject();
            for (Map.Entry<String, String> e : BugpunchRuntime.getCustomDataSnapshot().entrySet()) {
                custom.put(e.getKey(), e.getValue());
            }
            if (extra != null) {
                for (java.util.Iterator<String> it = extra.keys(); it.hasNext(); ) {
                    String k = it.next();
                    custom.put(k, extra.get(k));
                }
            }
            m.put("customData", custom);

            // Top-level `touches` + `videoMeta` matching iOS. Dashboard uses
            // these to overlay animated finger indicators on the video clip.
            // Key is `videoMeta` (not `video`) — the multipart uploader uses
            // `video` for the MP4 binary; the server middleware would clobber
            // this object.
            if (dump != null) {
                if (dump.videoMeta != null) m.put("videoMeta", dump.videoMeta);
                if (dump.touches != null)   m.put("touches", dump.touches);
            }
            return m.toString();
        } catch (JSONException e) {
            Log.w(TAG, "buildMetadataJson failed", e);
            BugpunchSdkErrorOverlay.reportThrowable("BugpunchReportingService", "buildMetadataJson", e);
            return "{}";
        }
    }
}
