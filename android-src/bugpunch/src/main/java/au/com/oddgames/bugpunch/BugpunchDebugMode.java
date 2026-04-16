package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.app.Dialog;
import android.content.Context;
import android.graphics.Color;
import android.graphics.drawable.ColorDrawable;
import android.graphics.drawable.GradientDrawable;
import android.util.DisplayMetrics;
import android.util.Log;
import android.util.TypedValue;
import android.view.Choreographer;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.widget.Button;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.zip.GZIPOutputStream;

/**
 * Master coordinator for Bugpunch debug mode. Unity C# calls startDebugMode()
 * once with the full config JSON and thereafter just fires reportBug() when
 * something happens. Everything else (video buffer, log capture, shake
 * detection, screenshot, upload queue, retry) lives native.
 */
public class BugpunchDebugMode {
    private static final String TAG = "BugpunchDebugMode";

    private static boolean sStarted;
    private static JSONObject sConfig;

    // Live metadata — seeded from config. scene is pushed by Unity; fps is
    // measured natively via Choreographer.
    private static final Map<String, String> sMetadata = new ConcurrentHashMap<>();
    private static final Map<String, String> sCustomData = new ConcurrentHashMap<>();

    private static long sLastAutoReportMs;
    // Guards against a second report being filed while the form/consent UI
    // is already open. Set when reportBug launches the form, cleared when
    // the BugpunchReportActivity finishes (whether Send or Cancel).
    private static volatile boolean sReportInProgress;
    static void clearReportInProgress() { sReportInProgress = false; }

    // FPS tracking via Choreographer (main-thread frame callback).
    private static final AtomicInteger sFrameCount = new AtomicInteger();
    private static volatile int sFps;
    private static long sFpsWindowStartNs;

    /**
     * Initialize everything. Safe to call multiple times (no-ops after first).
     * Schema: see JSON comment in source.
     */
    public static synchronized boolean startDebugMode(Activity activity, String configJson) {
        if (sStarted) return true;
        if (activity == null) { Log.w(TAG, "null activity"); return false; }

        try {
            sConfig = new JSONObject(configJson != null ? configJson : "{}");
        } catch (JSONException e) {
            Log.w(TAG, "bad config json, using defaults", e);
            sConfig = new JSONObject();
        }

        // Seed metadata from config.
        JSONObject meta = sConfig.optJSONObject("metadata");
        if (meta != null) {
            for (java.util.Iterator<String> it = meta.keys(); it.hasNext(); ) {
                String k = it.next();
                sMetadata.put(k, meta.optString(k));
            }
        }

        // 1) Crash handlers + ANR watchdog (already native; also drains uploader).
        int anrMs = sConfig.optInt("anrTimeoutMs", 5000);
        BugpunchCrashHandler.initialize(activity, anrMs);
        BugpunchCrashHandler.setMetadata(
            sMetadata.getOrDefault("appVersion", ""),
            sMetadata.getOrDefault("bundleId", ""),
            sMetadata.getOrDefault("unityVersion", ""),
            sMetadata.getOrDefault("deviceModel", ""),
            sMetadata.getOrDefault("osVersion", ""),
            sMetadata.getOrDefault("gpu", ""));

        // 2) Log reader — our own logcat ring buffer.
        int logSize = sConfig.optInt("logBufferSize", 2000);
        BugpunchLogReader.start(logSize);

        // 3) Shake detector (opt-in).
        JSONObject shake = sConfig.optJSONObject("shake");
        if (shake != null && shake.optBoolean("enabled", false)) {
            float threshold = (float) shake.optDouble("threshold", 2.5);
            BugpunchShakeDetector.start(activity, threshold, new Runnable() {
                @Override public void run() { onShake(); }
            });
        }

        // 4) Ring buffer: not auto-started here. MediaProjection needs a user
        //    consent activity; game invokes BugpunchProjectionRequest when it
        //    wants recording. That stays as-is.

        // 5) Frame tick for native FPS — Choreographer fires once per vsync.
        activity.runOnUiThread(new Runnable() {
            @Override public void run() { startFrameTick(); }
        });

        sStarted = true;
        Log.i(TAG, "debug mode started");

        // 6) Drain any native crash reports written by the signal handler /
        //    ANR watchdog on a previous launch. We do this AFTER everything
        //    else is wired so metadata is available and the uploader has a
        //    valid URL + API key.
        try { drainPendingCrashFiles(activity); } catch (Throwable t) {
            Log.w(TAG, "crash drain failed", t);
        }
        return true;
    }

    /**
     * Pick up every .crash file the native handler / ANR watchdog wrote on
     * a previous launch, wrap the raw text as a /api/crashes JSON payload,
     * enqueue via the durable uploader (so a failed POST resumes on the
     * next launch), and delete the local file.
     *
     * Intentionally sends the **raw** crash text as the `stackTrace` field —
     * server's crashSymbolicator parses it to find the ---STACK--- and
     * ---BUILD_IDS--- sections. This means no format divergence between
     * what we wrote and what we upload.
     */
    private static void drainPendingCrashFiles(Activity activity) {
        String[] paths = BugpunchCrashHandler.getPendingCrashFiles(activity);
        if (paths == null || paths.length == 0) return;

        String serverUrl = sConfig != null ? sConfig.optString("serverUrl", "") : "";
        String apiKey    = sConfig != null ? sConfig.optString("apiKey", "") : "";
        if (serverUrl.isEmpty() || apiKey.isEmpty()) {
            Log.w(TAG, "native crashes pending but serverUrl/apiKey not configured");
            return;
        }
        String url = serverUrl.replaceAll("/+$", "") + "/api/crashes";

        for (String path : paths) {
            String raw = BugpunchCrashHandler.readCrashFile(path);
            if (raw == null || raw.isEmpty()) {
                BugpunchCrashHandler.deleteCrashFile(path);
                continue;
            }
            try {
                JSONObject body = buildCrashPayload(raw);
                BugpunchUploader.enqueueJson(activity, url, apiKey, body.toString());
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
                default: /* fall through — preserved in stackTrace */ break;
            }
        }

        // errorMessage is what the dashboard lists as the one-line summary.
        if ("NATIVE_SIGNAL".equals(type)) {
            body.put("errorMessage",
                (signal != null ? signal : "NATIVE") +
                (faultAddr != null ? " at " + faultAddr : ""));
        } else if ("ANR".equals(type)) {
            body.put("errorMessage", "ANR — main thread unresponsive");
        } else {
            body.put("errorMessage", type != null ? type : "Native crash");
        }
        return body;
    }

    private static void startFrameTick() {
        sFpsWindowStartNs = System.nanoTime();
        Choreographer.getInstance().postFrameCallback(new Choreographer.FrameCallback() {
            @Override public void doFrame(long frameTimeNanos) {
                if (!sStarted) return;
                int frames = sFrameCount.incrementAndGet();
                long elapsed = frameTimeNanos - sFpsWindowStartNs;
                if (elapsed >= 1_000_000_000L) {
                    sFps = (int) (frames * 1_000_000_000L / elapsed);
                    sFrameCount.set(0);
                    sFpsWindowStartNs = frameTimeNanos;
                }
                Choreographer.getInstance().postFrameCallback(this);
            }
        });
    }

    /**
     * Show a consent dialog for screen recording. On Accept we kick off the
     * MediaProjection flow (which brings up its own OS-level consent dialog);
     * on Cancel nothing happens. No-op if debug mode isn't started yet or if
     * recording is already running.
     */
    /**
     * Enable debug recording. With {@code skipConsent=false} (default) shows
     * a native consent sheet with Start / Cancel; with {@code true} skips it
     * and goes straight to the OS-level MediaProjection consent dialog.
     */
    public static void enterDebugMode(final Activity activity, final boolean skipConsent) {
        if (!sStarted || activity == null) return;
        if (BugpunchRecorder.getInstance().hasFootage()) return;
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                if (skipConsent) startRecordingFromConfig(activity);
                else             showConsentDialog(activity);
            }
        });
    }

    private static void showConsentDialog(final Activity activity) {
        final Dialog dialog = new Dialog(activity, android.R.style.Theme_Translucent_NoTitleBar);
        Window window = dialog.getWindow();
        if (window != null) {
            window.setBackgroundDrawable(new ColorDrawable(0xAA000000));
            window.setLayout(ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT);
        }

        FrameInt dp = new FrameInt(activity);

        // Root — centers the card over a dimmed background.
        LinearLayout root = new LinearLayout(activity);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setGravity(Gravity.CENTER);
        root.setPadding(dp.of(20), dp.of(20), dp.of(20), dp.of(20));

        // Card
        LinearLayout card = new LinearLayout(activity);
        card.setOrientation(LinearLayout.VERTICAL);
        card.setGravity(Gravity.CENTER_HORIZONTAL);
        GradientDrawable cardBg = new GradientDrawable();
        cardBg.setColor(0xFF141820);
        cardBg.setCornerRadius(dp.of(20));
        cardBg.setStroke(dp.of(1), 0xFF2A3240);
        card.setBackground(cardBg);
        int padH = dp.of(28), padV = dp.of(32);
        card.setPadding(padH, padV, padH, padV);
        LinearLayout.LayoutParams cardLp = new LinearLayout.LayoutParams(
            dp.of(340), ViewGroup.LayoutParams.WRAP_CONTENT);
        root.addView(card, cardLp);

        // Title
        TextView title = new TextView(activity);
        title.setText("Enable debug recording");
        title.setTextColor(Color.WHITE);
        title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 20);
        title.setTypeface(title.getTypeface(), android.graphics.Typeface.BOLD);
        title.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams titleLp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        titleLp.bottomMargin = dp.of(10);
        card.addView(title, titleLp);

        // Body
        TextView body = new TextView(activity);
        body.setText("Bugpunch will record your screen so that bug reports include the "
            + "moments leading up to an issue. Recording stays on your device until you "
            + "submit a report.");
        body.setTextColor(0xFFA8B2BF);
        body.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        body.setGravity(Gravity.CENTER);
        body.setLineSpacing(dp.of(3), 1f);
        LinearLayout.LayoutParams bodyLp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        bodyLp.bottomMargin = dp.of(28);
        card.addView(body, bodyLp);

        // Primary CTA
        Button start = pillButton(activity, "Start Recording", 0xFF2A7BE0, Color.WHITE, dp);
        LinearLayout.LayoutParams startLp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, dp.of(48));
        startLp.bottomMargin = dp.of(10);
        card.addView(start, startLp);
        start.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                dialog.dismiss();
                startRecordingFromConfig(activity);
            }
        });

        // Secondary
        Button notNow = pillButton(activity, "Not now", 0x00000000, 0xFFA8B2BF, dp);
        LinearLayout.LayoutParams notNowLp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, dp.of(48));
        card.addView(notNow, notNowLp);
        notNow.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { dialog.dismiss(); }
        });

        dialog.setContentView(root);
        dialog.setCancelable(true);
        dialog.show();
    }

    private static Button pillButton(Context ctx, String text, int bg, int fg, FrameInt dp) {
        Button b = new Button(ctx);
        b.setText(text);
        b.setTextColor(fg);
        b.setAllCaps(false);
        b.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
        b.setTypeface(b.getTypeface(), android.graphics.Typeface.BOLD);
        GradientDrawable bgShape = new GradientDrawable();
        bgShape.setColor(bg);
        bgShape.setCornerRadius(dp.of(12));
        b.setBackground(bgShape);
        return b;
    }

    // Tiny helper so we can compute dp→px without repeating getResources().getDisplayMetrics().
    private static class FrameInt {
        private final android.util.DisplayMetrics dm;
        FrameInt(Context c) { dm = c.getResources().getDisplayMetrics(); }
        int of(int v) { return (int) TypedValue.applyDimension(
            TypedValue.COMPLEX_UNIT_DIP, v, dm); }
    }

    private static void startRecordingFromConfig(Activity activity) {
        JSONObject video = sConfig.optJSONObject("video");
        int fps = video != null ? video.optInt("fps", 30) : 30;
        int bitrate = video != null ? video.optInt("bitrate", 2_000_000) : 2_000_000;
        int windowSec = video != null ? video.optInt("bufferSeconds", 30) : 30;
        DisplayMetrics dm = activity.getResources().getDisplayMetrics();
        // Touch recorder shares the same window; sized generously so a
        // multi-finger session over the buffer window never drops events.
        BugpunchTouchRecorder.configure(windowSec * 600);
        BugpunchTouchRecorder.start(activity);
        // Fire BugpunchProjectionRequest which handles the system-level
        // MediaProjection consent dialog and kicks off the recorder service.
        // Empty callback — we don't need Unity to hear about it; state lives
        // entirely native from here.
        BugpunchProjectionRequest.request(activity,
            dm.widthPixels, dm.heightPixels, bitrate, fps, windowSec, dm.densityDpi,
            "", "");
    }

    public static synchronized void stopDebugMode() {
        if (!sStarted) return;
        BugpunchShakeDetector.stop();
        BugpunchLogReader.stop();
        BugpunchTouchRecorder.stop();
        BugpunchCrashHandler.shutdown();
        sStarted = false;
    }

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
            return null;
        }
    }

    // ── Metadata + custom data (called from C# as game state changes) ──

    public static void setCustomData(String key, String value) {
        if (key == null) return;
        if (value == null) sCustomData.remove(key);
        else sCustomData.put(key, value);
    }

    /** Update the current Unity scene name (the one thing we can't derive natively). */
    public static void updateScene(String scene) {
        if (scene != null) sMetadata.put("scene", scene);
    }

    // ── Accessors for BugpunchDirectives ─────────────────────────────────

    /** Game-declared attachment allow-list, straight from the startup config. */
    public static JSONArray getAttachmentRules() {
        return sConfig != null ? sConfig.optJSONArray("attachmentRules") : null;
    }

    public static String getServerUrl() {
        return sConfig != null ? sConfig.optString("serverUrl", "") : "";
    }

    public static String getApiKey() {
        return sConfig != null ? sConfig.optString("apiKey", "") : "";
    }

    /**
     * Called from C# via <c>BugpunchNative.PostPaxScriptResult</c> when a
     * directive-triggered PaxScript run finishes. Hands off to
     * {@link BugpunchDirectives} which POSTs to the /enrich endpoint.
     */
    public static void postPaxScriptResult(String directiveId, String resultJson) {
        if (!sStarted) return;
        BugpunchDirectives.onPaxScriptResult(directiveId, resultJson);
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
        if (!sStarted) { Log.w(TAG, "reportBug before startDebugMode"); return; }
        final Activity activity = BugpunchUnity.currentActivity();
        if (activity == null) return;

        // Cooldown for auto exception reports.
        if ("exception".equals(type)) {
            long cooldownMs = (long)(sConfig.optDouble("autoReportCooldownSeconds", 30.0) * 1000);
            long now = System.currentTimeMillis();
            if (now - sLastAutoReportMs < cooldownMs) return;
            sLastAutoReportMs = now;
        }

        // User-initiated bug reports show the form; silent reports
        // (exceptions, crashes, feedback) skip it and enqueue directly.
        final boolean showForm = "bug".equals(type);
        final String shotPath = activity.getCacheDir().getAbsolutePath()
            + "/bp_shot_" + System.nanoTime() + ".jpg";

        if (showForm) {
            // Guard: if a report form is already open, ignore the second trigger.
            if (sReportInProgress) { Log.i(TAG, "report already in progress, ignoring"); return; }
            sReportInProgress = true;
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
        BugpunchScreenshot.capture("rb_" + System.nanoTime(), shotPath, 85, "", "");

        JSONObject extra = parseOr(extraJson);
        String logsJson = BugpunchLogReader.snapshotJson();
        String logsGzPath = writeGzipLogs(activity, logsJson);
        String metadataJson = buildMetadataJson(type, title, description,
            extra, /* reporterEmail */ null, /* severity */ null, dump);
        Object[] traceAttach = prepareTraceAttachments(activity);
        String tracesJsonPath = traceAttach != null ? (String) traceAttach[0] : null;
        String[] traceShots = traceAttach != null ? (String[]) traceAttach[1] : null;
        enqueueManifest(activity, endpointFor(type), metadataJson, shotPath, dump.videoPath, null,
            tracesJsonPath, traceShots, logsGzPath);
    }

    /**
     * Called from {@link BugpunchReportActivity} when the user taps Send.
     * Builds the manifest including the annotations layer (if any) and hands
     * to the uploader.
     */
    public static void submitReport(String title, String description, String email,
                                    String severity, String screenshotPath,
                                    String annotationsPath) {
        if (!sStarted) return;
        Activity activity = BugpunchUnity.currentActivity();
        if (activity == null) return;

        DumpResult dump = dumpRingAndTouches(activity);
        String logsJson = BugpunchLogReader.snapshotJson();
        String logsGzPath = writeGzipLogs(activity, logsJson);
        String metadataJson = buildMetadataJson("bug", title, description,
            null, email, severity, dump);
        Object[] traceAttach = prepareTraceAttachments(activity);
        String tracesJsonPath = traceAttach != null ? (String) traceAttach[0] : null;
        String[] traceShots = traceAttach != null ? (String[]) traceAttach[1] : null;
        enqueueManifest(activity, "/api/reports/bug", metadataJson,
            screenshotPath, dump.videoPath, annotationsPath, tracesJsonPath, traceShots, logsGzPath);
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
        r.videoPath = dumpRingIfRunning(activity);
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

    private static String dumpRingIfRunning(Activity activity) {
        if (!BugpunchRecorder.getInstance().hasFootage()) {
            Log.i(TAG, "no video dump — recorder not running or no footage yet");
            return null;
        }
        String path = activity.getCacheDir().getAbsolutePath()
            + "/bp_vid_" + System.nanoTime() + ".mp4";
        try {
            boolean ok = BugpunchRecorder.getInstance().dump(path);
            java.io.File f = new java.io.File(path);
            long size = f.length();
            if (!ok || size <= 0) {
                Log.w(TAG, "video dump returned " + ok + " size=" + size + " — skipping");
                if (f.exists()) f.delete();
                return null;
            }
            Log.i(TAG, "video dump ok: " + path + " (" + size + " bytes)");
            return path;
        } catch (Throwable t) {
            Log.w(TAG, "video dump failed", t);
            return null;
        }
    }

    private static void enqueueManifest(Activity activity, String endpointPath,
                                        String metadataJson, String shotPath,
                                        String videoPath, String annotationsPath,
                                        String tracesJsonPath, String[] traceScreenshotPaths,
                                        String logsGzPath) {
        String serverUrl = sConfig.optString("serverUrl", "");
        String apiKey = sConfig.optString("apiKey", "");
        if (serverUrl.isEmpty() || apiKey.isEmpty()) {
            Log.w(TAG, "serverUrl/apiKey not configured — skipping");
            return;
        }
        String url = serverUrl.replaceAll("/+$", "") + endpointPath;
        BugpunchUploader.enqueue(activity, url, apiKey, metadataJson,
            shotPath, videoPath, annotationsPath, tracesJsonPath, traceScreenshotPaths,
            logsGzPath);
    }

    private static String endpointFor(String type) {
        if ("feedback".equals(type)) return "/api/reports/feedback";
        if ("crash".equals(type))    return "/api/crashes";
        return "/api/reports/bug";
    }

    // ── Internals ──

    private static void onShake() {
        reportBug("bug", "Shake report", "Triggered by shake gesture", null);
    }

    private static JSONObject parseOr(String json) {
        if (json == null || json.isEmpty()) return new JSONObject();
        try { return new JSONObject(json); } catch (JSONException e) { return new JSONObject(); }
    }

    /** Gzip a JSON string to a temp file. Returns the path, or null on error. */
    private static String writeGzipLogs(Activity activity, String logsJson) {
        if (logsJson == null || logsJson.isEmpty() || logsJson.equals("[]")) return null;
        try {
            File f = new File(activity.getCacheDir(), "bp_logs_" + System.nanoTime() + ".json.gz");
            FileOutputStream fos = new FileOutputStream(f);
            GZIPOutputStream gz = new GZIPOutputStream(fos);
            gz.write(logsJson.getBytes("UTF-8"));
            gz.close();
            return f.getAbsolutePath();
        } catch (IOException e) {
            Log.w(TAG, "writeGzipLogs failed", e);
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
            device.put("model", sMetadata.getOrDefault("deviceModel", ""));
            device.put("os", sMetadata.getOrDefault("osVersion", ""));
            device.put("platform", "Android");
            device.put("gpu", sMetadata.getOrDefault("gpu", ""));
            m.put("device", device);

            JSONObject app = new JSONObject();
            app.put("version", sMetadata.getOrDefault("appVersion", ""));
            app.put("bundleId", sMetadata.getOrDefault("bundleId", ""));
            app.put("unityVersion", sMetadata.getOrDefault("unityVersion", ""));
            app.put("scene", sMetadata.getOrDefault("scene", ""));
            app.put("fps", sFps);
            m.put("app", app);

            JSONObject custom = new JSONObject(sCustomData);
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
            return "{}";
        }
    }
}
