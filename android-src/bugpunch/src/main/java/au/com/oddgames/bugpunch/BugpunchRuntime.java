package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.content.Context;
import android.os.Build;
import android.os.Handler;
import android.os.HandlerThread;
import android.util.Log;
import android.view.Choreographer;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.File;
import java.util.ArrayList;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * Always-on Bugpunch runtime — started for every user on every launch.
 *
 * <p>Despite the word "debug" elsewhere in the SDK, the coordinator below is
 * <b>not</b> gated on a debug-mode flag. {@link #start(Activity, String)}
 * wires up crash handlers, the ANR watchdog, the AEI scan, log capture,
 * screenshot ring, analytics flush, frame tick, overlay detector, input
 * breadcrumbs, shake (if configured), and drains any pending crash files
 * from the previous process.  This happens for all users on every app
 * launch — there is no user consent step and no opt-in.
 *
 * <p>Runtime is a pure coordinator — it holds the shared process state
 * (config, metadata, custom data, session id, fps, report-in-progress
 * flag) and exposes accessors so the other native modules don't have to
 * thread these through every call. The heavy lifting is split out:
 * <ul>
 *   <li>{@link BugpunchCrashDrain} — previous-session crash file drain.</li>
 *   <li>{@link BugpunchReportingService} — runtime bug/feedback/exception
 *       reports (reportBug / submitReport / addTrace).</li>
 *   <li>{@link BugpunchDebugMode} — opt-in video recording (consent sheet
 *       + MediaProjection + ring buffer).</li>
 * </ul>
 *
 * <p>Opt-in video recording lives in {@link BugpunchDebugMode}: a game has
 * to explicitly call {@link BugpunchDebugMode#enter(Activity, boolean)} to
 * show a consent sheet and start MediaProjection.  Nothing here touches
 * video state; the silent report path asks {@code BugpunchDebugMode} to
 * dump the ring buffer only if one happens to be running.
 */
public class BugpunchRuntime {
    private static final String TAG = "[Bugpunch.Runtime]";

    private static boolean sStarted;            // full init including Activity pieces
    private static boolean sEarlyStarted;       // Context-only init ran (from ContentProvider)
    private static boolean sActivityAttached;   // Activity pieces completed
    private static JSONObject sConfig;
    private static Context sAppContext;
    private static Activity sAttachedActivity;

    // Live metadata — seeded from config. scene is pushed by Unity; fps is
    // measured natively via Choreographer.
    private static final Map<String, String> sMetadata = new ConcurrentHashMap<>();
    private static final Map<String, String> sCustomData = new ConcurrentHashMap<>();

    private static volatile long sLastAutoReportMs;
    // Guards against a second report being filed while the form/consent UI
    // is already open. Set when reportBug launches the form, cleared when
    // the BugpunchReportActivity finishes (whether Send or Cancel).
    private static volatile boolean sReportInProgress;
    public static void clearReportInProgress() { sReportInProgress = false; }
    static boolean isReportInProgress() { return sReportInProgress; }
    static void setReportInProgress(boolean v) { sReportInProgress = v; }

    // Cooldown bookkeeping for auto exception reports — written by
    // BugpunchReportingService.reportBug, read to enforce the interval.
    static long getLastAutoReport() { return sLastAutoReportMs; }
    static void setLastAutoReport(long ts) { sLastAutoReportMs = ts; }

    // FPS tracking via Choreographer (main-thread frame callback).
    private static final AtomicInteger sFrameCount = new AtomicInteger();
    private static volatile int sFps;
    private static long sFpsWindowStartNs;

    // The root session id minted at process launch — never changes for the
    // life of the process. Resume sessions reference this as their parent so
    // the dashboard can group every resume under a single launch.
    private static final String sRootSessionId = UUID.randomUUID().toString();

    // The currently active session id. Equal to sRootSessionId until the app
    // is foregrounded after being in the background longer than RESUME_THRESHOLD_MS,
    // at which point it rotates to a fresh UUID. Stamped onto every analytics
    // event + log-sink frame.
    private static volatile String sSessionId = sRootSessionId;

    // Null on the root session; equal to sRootSessionId on every resume so
    // the server can group them as one app launch. Read by the tunnel's log
    // frame builder.
    private static volatile String sParentSessionId = null;

    // Background → foreground transitions count as a "resume" only if the app
    // was backgrounded for at least this long. Below the threshold we treat it
    // as a transient interruption (notification shade, multitasking glance) and
    // keep streaming into the same session so logs remain coherent.
    private static final long RESUME_THRESHOLD_MS = 30_000L;
    private static volatile long sBackgroundedAtMs;
    private static int sStartedActivityCount;
    private static boolean sLifecycleHooked;

    // Stable per-install identifier — UUID generated on first call, persisted
    // in SharedPreferences. The dashboard uses this as the retention/cohort
    // key (vs. user_id which the game sets and may be null until login).
    private static volatile String sInstallId;
    private static final String INSTALL_PREFS = "bugpunch_install";
    private static final String INSTALL_KEY = "install_id";

    // App-supplied user id (SetUserId) and user-property bag (SetUserProperty).
    // Both are nullable — included on every typed event payload so the server
    // can roll up the user record without the SDK keeping its own copy.
    private static volatile String sUserId;
    private static final ConcurrentHashMap<String, String> sUserProps = new ConcurrentHashMap<>();

    // Open-session tracking for emitting session_end when the app backgrounds
    // (or when start() runs again after a cold launch). Wall-clock UTC string;
    // null when no session is open.
    private static volatile long sSessionStartedRealtimeMs;
    private static volatile boolean sSessionOpen;
    /** Latest resumed activity. WeakReference so we don't pin one across
     *  config changes. Used by the upload-status banner to find a window
     *  to mount on without polluting public API with an activity getter. */
    private static volatile java.lang.ref.WeakReference<Activity> sResumedActivity;

    /** Currently-resumed Activity, or null. Cleared on pause; never holds
     *  a reference past the activity's foreground lifecycle. */
    public static Activity getResumedActivity() {
        java.lang.ref.WeakReference<Activity> ref = sResumedActivity;
        return ref != null ? ref.get() : null;
    }


    /**
     * Initialize the always-on Bugpunch runtime. Safe to call multiple times
     * (no-ops after first). Does NOT start video recording — video is opt-in
     * via {@link BugpunchDebugMode#enter(Activity, boolean)}.
     *
     * <p>Legacy entry point used by the C# path from Unity. Internally this
     * calls {@link #startEarly(Context, String)} followed by
     * {@link #attachActivity(Activity)}. When {@link BugpunchInitProvider}
     * has already fired early init from the manifest-declared ContentProvider,
     * this call only completes the Activity-bound pieces.
     */
    public static synchronized boolean start(Activity activity, String configJson) {
        if (sStarted) return true;
        if (activity == null) { Log.w(TAG, "null activity"); return false; }

        if (!sEarlyStarted) {
            startEarly(activity.getApplicationContext(), configJson);
        } else {
            // Early init already ran from the ContentProvider using the bundled
            // config. The richer config from C# includes Unity runtime values
            // (Application.version, deviceUniqueIdentifier, persistentDataPath-
            // resolved attachment rules, deviceTier) — merge them in now so the
            // crash handler reports the full metadata and attachment rules
            // resolve to real paths.
            augmentConfig(configJson);
        }
        attachActivity(activity);
        return sStarted;
    }

    /**
     * Merge richer config values into the already-initialised runtime and
     * refresh crash-handler metadata. Called when C# hands us a config that
     * includes Unity runtime values after the ContentProvider has already
     * performed Context-only init with the bundled config.
     */
    private static synchronized void augmentConfig(String configJson) {
        if (configJson == null || configJson.isEmpty() || sConfig == null) return;
        try {
            JSONObject rich = new JSONObject(configJson);
            for (java.util.Iterator<String> it = rich.keys(); it.hasNext(); ) {
                String k = it.next();
                sConfig.put(k, rich.get(k));
            }
            // Theme + strings may have arrived in the richer config — reapply
            // so later overlays pick up the new values. Safe to call repeatedly.
            try { BugpunchTheme.apply(sConfig.optJSONObject("theme")); }
            catch (Throwable t) { Log.w(TAG, "theme reapply failed", t); }
            try { BugpunchStrings.apply(sConfig.optJSONObject("strings")); }
            catch (Throwable t) { Log.w(TAG, "strings reapply failed", t); }
            try { BugpunchSdkErrorOverlay.setOverlayEnabled(sConfig.optBoolean("sdkErrorOverlay", true)); }
            catch (Throwable t) { Log.w(TAG, "sdkErrorOverlay reapply failed", t); }

            JSONObject meta = sConfig.optJSONObject("metadata");
            if (meta != null) {
                for (java.util.Iterator<String> it = meta.keys(); it.hasNext(); ) {
                    String k = it.next();
                    sMetadata.put(k, meta.optString(k));
                }
            }
            BugpunchCrashHandler.setMetadata(
                sMetadata.getOrDefault("appVersion", ""),
                sMetadata.getOrDefault("bundleId", ""),
                sMetadata.getOrDefault("unityVersion", ""),
                sMetadata.getOrDefault("deviceModel", ""),
                sMetadata.getOrDefault("osVersion", ""),
                sMetadata.getOrDefault("gpu", ""));

            // C# just gave us the Unity-resolved storage paths — re-seed the
            // FileService roots so /files/* requests can resolve real paths.
            BugpunchFileService.configure(sConfig);

            // Re-eval native tunnel state with the richer config (may have flipped useNativeTunnel)
            BugpunchTunnel.start(sConfig, BugpunchIdentity.getStableDeviceId(sAppContext));
        } catch (JSONException e) {
            Log.w(TAG, "augmentConfig failed", e);
        }
    }

    /**
     * Context-only init — safe to call from a manifest-declared ContentProvider
     * before Application.onCreate() returns and before any Activity exists.
     * Installs the NDK signal handlers, sets crash paths, starts the log ring,
     * and seeds config metadata. Activity-bound pieces (overlay detector,
     * SurfaceView cache, Choreographer frame tick, AEI scan, crash drain,
     * shake detector) defer until {@link #attachActivity(Activity)} fires.
     */
    public static synchronized void startEarly(Context context, String configJson) {
        if (sEarlyStarted) return;
        if (context == null) { Log.w(TAG, "startEarly: null context"); return; }
        sAppContext = context.getApplicationContext();

        try {
            sConfig = new JSONObject(configJson != null ? configJson : "{}");
        } catch (JSONException e) {
            Log.w(TAG, "bad config json, using defaults", e);
            sConfig = new JSONObject();
        }

        // Apply the native-overlay theme from config.theme (colours, card
        // radius, font sizes). Every surface built by BugpunchReportOverlay /
        // BugpunchDebugMode consent / crash dialogs reads values through
        // BugpunchTheme so a missing/empty block falls back to the schema
        // defaults silently.
        try { BugpunchTheme.apply(sConfig.optJSONObject("theme")); }
        catch (Throwable t) { Log.w(TAG, "theme apply failed", t); }

        // Apply user-visible strings + locale overrides from config.strings.
        // BugpunchStrings.text(key, fallback) is the lookup every overlay
        // uses so a missing/empty block falls back to the caller's hardcoded
        // English fallback automatically.
        try { BugpunchStrings.apply(sConfig.optJSONObject("strings")); }
        catch (Throwable t) { Log.w(TAG, "strings apply failed", t); }

        // SDK self-diagnostic banner toggle. Default ON — the dev/QA wants
        // to see when the SDK itself fails. Production builds disable via
        // BugpunchConfig.showSdkErrorOverlay (or runtime SetSdkErrorOverlay).
        try { BugpunchSdkErrorOverlay.setOverlayEnabled(sConfig.optBoolean("sdkErrorOverlay", true)); }
        catch (Throwable t) { Log.w(TAG, "sdkErrorOverlay apply failed", t); }

        // Seed metadata from config.
        JSONObject meta = sConfig.optJSONObject("metadata");
        if (meta != null) {
            for (java.util.Iterator<String> it = meta.keys(); it.hasNext(); ) {
                String k = it.next();
                sMetadata.put(k, meta.optString(k));
            }
        }

        // Detect installer mode (store vs sideload).
        String installerPkg = null;
        try { installerPkg = sAppContext.getPackageManager().getInstallerPackageName(sAppContext.getPackageName()); }
        catch (Exception ignored) {}
        sMetadata.put("installerMode",
            "com.android.vending".equals(installerPkg) ? "store" :
            (installerPkg == null || installerPkg.isEmpty()) ? "sideload" : "unknown");

        // Android versionCode — companion to versionName. Required server-side
        // to look up the R8/ProGuard mapping.txt that deobfuscates Java
        // frames in crash / ANR / AEI trace reports. Read directly from
        // PackageInfo; no Unity/C# roundtrip needed.
        try {
            android.content.pm.PackageInfo pi = sAppContext.getPackageManager()
                .getPackageInfo(sAppContext.getPackageName(), 0);
            long code;
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                code = pi.getLongVersionCode();
            } else {
                code = pi.versionCode;
            }
            sMetadata.put("buildCode", String.valueOf(code));
        } catch (Throwable t) {
            Log.w(TAG, "failed to read versionCode for metadata", t);
        }

        // 1) Crash handlers + ANR watchdog. BugpunchCrashHandler.initialize
        // takes a Context (not Activity) — safe from ContentProvider.onCreate().
        // Before anything else, park any leftover bp_video.dat from the
        // previous session as bp_video.prev.dat so the recorder's truncating
        // re-init can't destroy footage we need for AEI/ANR synthesis.
        BugpunchCrashHandler.preserveVideoRingFromPreviousSession(sAppContext);
        int anrMs = sConfig.optInt("anrTimeoutMs", 5000);
        BugpunchCrashHandler.initialize(sAppContext, anrMs);
        BugpunchCrashHandler.setMetadata(
            sMetadata.getOrDefault("appVersion", ""),
            sMetadata.getOrDefault("bundleId", ""),
            sMetadata.getOrDefault("unityVersion", ""),
            sMetadata.getOrDefault("deviceModel", ""),
            sMetadata.getOrDefault("osVersion", ""),
            sMetadata.getOrDefault("gpu", ""));
        // Stamp the root session id so any crash before the first resume
        // still tells the next-launch drain which log_session to tail-append.
        BugpunchCrashHandler.setSessionId(sSessionId);

        // 1b) Register rescue file paths with the signal handler. Logs +
        // input breadcrumbs are mirrored into native memory (zero disk
        // during normal operation); the storyboard ring's per-slot RGBA +
        // packed-header dump files are written only after a SIGSEGV. Drain
        // on the next launch encodes JPEGs before upload.
        File crashDir = new File(sAppContext.getFilesDir(), "bugpunch_crashes");
        if (!crashDir.exists()) crashDir.mkdirs();
        File logsFile  = new File(crashDir, "logs_recent.log");
        File inputFile = new File(crashDir, "input_breadcrumbs.bin");
        BugpunchLogReader.setRollingDiskPath(logsFile.getAbsolutePath());
        BugpunchCrashHandler.setLogsPath(logsFile.getAbsolutePath());
        BugpunchCrashHandler.setInputPath(inputFile.getAbsolutePath());

        File storyboardBase = new File(crashDir, "storyboard");
        BugpunchCrashHandler.setStoryboardPathBase(storyboardBase.getAbsolutePath());
        BugpunchStoryboard.initialise();

        // Input breadcrumb ring — allocates a direct ByteBuffer and hands
        // its address to bp.c so the signal handler can dump the last ~128
        // pointer/key events at crash time.
        BugpunchInput.initialise();

        // 2) Log reader — our own logcat ring buffer. Mirrors the ring to
        // `logs_recent.json` on the reader thread (rate-limited) so the
        // native crash handler can attach it.
        int logSize = sConfig.optInt("logBufferSize", 2000);
        BugpunchLogReader.start(logSize);

        // 3) Native tunnel — comes up here, before Unity boots, and stays
        //    alive across managed crashes. The C# TunnelClient is excluded
        //    on Android/iOS now, so this is the only WebSocket on the wire.
        try {
            String stableId = BugpunchIdentity.getStableDeviceId(sAppContext);
            BugpunchTunnel.start(sConfig, stableId);
        } catch (Throwable t) {
            Log.w(TAG, "native tunnel start failed", t);
        }

        // 4) Seed the native FileService roots from the startup config. The
        //    bundled config may not have Unity-resolved paths yet — augmentConfig
        //    re-seeds when C# hands us the richer blob.
        try { BugpunchFileService.configure(sConfig); }
        catch (Throwable t) { Log.w(TAG, "FileService configure failed", t); }

        sEarlyStarted = true;
        Log.i(TAG, "runtime early init complete");
    }

    /**
     * Complete Activity-bound initialization — called either by the
     * {@link BugpunchInitProvider} lifecycle bridge when Unity's player
     * Activity is created, or by the legacy {@link #start(Activity, String)}
     * path when C# drives init. Must follow a successful
     * {@link #startEarly(Context, String)}.
     */
    public static synchronized void attachActivity(Activity activity) {
        if (sActivityAttached) return;
        if (activity == null) { Log.w(TAG, "attachActivity: null activity"); return; }
        if (!sEarlyStarted) {
            // Early init was skipped (e.g. no bundled config) — fall through
            // to let start() drive it from the legacy path. Nothing to do here.
            return;
        }
        sAttachedActivity = activity;

        // Overlay detector — logs when an in-process Activity (ad SDK,
        // IAP billing proxy, Firebase Auth UI, …) comes on top of ours so
        // crash triage can see what was covering the game.
        try { BugpunchOverlayDetector.start(activity); }
        catch (Throwable t) { Log.w(TAG, "overlay detector start failed", t); }

        // 3) Cache the SurfaceView for ANR screenshot capture.
        BugpunchScreenshot.cacheSurfaceView();

        // 3a-prep) Hook the upload-status banner so the user sees a small
        // "Sending crash report…" indicator while the drain works through
        // pending uploads. Observer fires on the uploader's worker thread;
        // the banner posts to main and renders a translucent pill on top
        // of whatever activity is currently resumed.
        BugpunchUploader.setStatusObserver(new BugpunchUploader.StatusObserver() {
            @Override public void onUploadStatus(int pending, String phaseHint) {
                if (pending > 0) {
                    BugpunchUploadStatusBanner.showOrUpdate(pending, phaseHint);
                } else {
                    BugpunchUploadStatusBanner.hide();
                }
            }
        });

        // 3a) Previous-session work — AEI scan + crash drain. Both do disk I/O
        // (file reads, JPEG encoding, system service calls) that used to
        // synchronously block the host activity's onCreate, costing hundreds
        // of ms on devices with pending crashes. Move both off the main
        // thread; their order relative to each other is preserved (AEI scan
        // first so its merges are picked up by the drain). The signal-handler
        // raw frames the drain reads aren't touched by anything in this
        // process until WE crash, so there's no race with the rolling buffer.
        final Activity drainActivity = activity;
        HandlerThread drainThread = new HandlerThread("bp-drain");
        drainThread.start();
        new Handler(drainThread.getLooper()).post(new Runnable() {
            @Override public void run() {
                try { BugpunchCrashHandler.scanApplicationExitInfo(drainActivity); }
                catch (Throwable t) { Log.w(TAG, "AEI scan failed", t); }
                try { BugpunchCrashDrain.drain(drainActivity); }
                catch (Throwable t) { Log.w(TAG, "crash drain failed", t); }
                drainThread.quitSafely();
            }
        });

        // 4) Shake detector (opt-in).
        JSONObject shake = sConfig != null ? sConfig.optJSONObject("shake") : null;
        if (shake != null && shake.optBoolean("enabled", false)) {
            float threshold = (float) shake.optDouble("threshold", 2.5);
            BugpunchShakeDetector.start(activity, threshold, new Runnable() {
                @Override public void run() { onShake(); }
            });
        }

        // 5) Frame tick for native FPS — Choreographer fires once per vsync.
        activity.runOnUiThread(new Runnable() {
            @Override public void run() { startFrameTick(); }
        });

        // Track foreground/background transitions so the log session id
        // rotates whenever the app comes back from a long enough background
        // gap — see RESUME_THRESHOLD_MS. Idempotent across re-attaches.
        try { hookLifecycle(activity); }
        catch (Throwable t) { Log.w(TAG, "hookLifecycle failed", t); }

        sActivityAttached = true;
        sStarted = true;
        Log.i(TAG, "runtime activity attached");

        // Auto-enter debug mode for debug builds (from config JSON)
        try {
            if (sConfig != null && sConfig.optBoolean("debugBuild", false)) {
                Log.i(TAG, "Debug build detected — auto-entering debug mode");
                if (!BugpunchRecorder.getInstance().isRunning()) {
                    BugpunchDebugMode.enter(activity, true); // skip consent UI, go straight to system dialog
                }
            }
        } catch (Throwable t) {
            Log.w(TAG, "auto debug mode failed", t);
        }

        // Cache-driven debug-mode auto-prompt. If the last roleConfig the
        // server delivered was "internal", fire the consent sheet now —
        // don't wait for the tunnel handshake to come back. The tunnel will
        // tear down speculatively-started recordings if the server now says
        // non-internal.
        try {
            BugpunchDebugAutoPrompt.maybeShowOnLaunch(activity);
        } catch (Throwable t) {
            Log.w(TAG, "auto-prompt on launch failed", t);
        }
    }

    /** Has {@link #start} been called successfully? */
    static boolean isStarted() { return sStarted; }

    /** Has Context-only early init run (from the ContentProvider)? */
    public static boolean isEarlyStarted() { return sEarlyStarted; }

    /**
     * Currently active session id. Rotates when the app is foregrounded after
     * being in the background longer than {@link #RESUME_THRESHOLD_MS} —
     * resume sessions stamp {@link #getParentSessionId()} so the server can
     * group them under the original launch.
     */
    public static String getSessionId() { return sSessionId; }

    /** Root (launch-time) session id. Pointed to by every resume session as its parent. */
    public static String getRootSessionId() { return sRootSessionId; }

    /** Parent of the current session, or null when the current session is the root. */
    public static String getParentSessionId() { return sParentSessionId; }

    /**
     * Wire {@link android.app.Application.ActivityLifecycleCallbacks} so we
     * notice when the app is backgrounded long enough to count as a separate
     * play session. Idempotent.
     */
    private static void hookLifecycle(Activity activity) {
        if (sLifecycleHooked) return;
        if (activity == null || activity.getApplication() == null) return;
        sLifecycleHooked = true;
        activity.getApplication().registerActivityLifecycleCallbacks(
            new android.app.Application.ActivityLifecycleCallbacks() {
                @Override public void onActivityCreated(Activity a, android.os.Bundle s) {}
                @Override public void onActivityStarted(Activity a) {
                    int prev = sStartedActivityCount++;
                    if (prev == 0) onForegrounded();
                }
                @Override public void onActivityResumed(Activity a) { sResumedActivity = new java.lang.ref.WeakReference<>(a); }
                @Override public void onActivityPaused(Activity a) {
                    if (sResumedActivity != null && sResumedActivity.get() == a) {
                        sResumedActivity = null;
                    }
                }
                @Override public void onActivityStopped(Activity a) {
                    if (sStartedActivityCount > 0) sStartedActivityCount--;
                    if (sStartedActivityCount == 0) onBackgrounded();
                }
                @Override public void onActivitySaveInstanceState(Activity a, android.os.Bundle s) {}
                @Override public void onActivityDestroyed(Activity a) {}
            });
    }

    private static void onBackgrounded() {
        sBackgroundedAtMs = android.os.SystemClock.elapsedRealtime();
        // Close the analytics session immediately. If the user comes back
        // within RESUME_THRESHOLD_MS we'll open a fresh one in onForegrounded;
        // anything shorter than that is below the noise floor for retention
        // / DAU / session-length math anyway.
        try { emitSessionEnd(); } catch (Throwable t) { Log.w(TAG, "emitSessionEnd on bg failed", t); }
    }

    private static void onForegrounded() {
        long bg = sBackgroundedAtMs;
        if (bg <= 0) {
            // First foreground after launch — kick off the cold-launch session.
            try { emitSessionStart(); } catch (Throwable t) { Log.w(TAG, "emitSessionStart cold failed", t); }
            return;
        }
        long away = android.os.SystemClock.elapsedRealtime() - bg;
        sBackgroundedAtMs = 0;
        if (away < RESUME_THRESHOLD_MS) {
            // Brief interruption — re-open the session under the same id so
            // logs and analytics stay coherent.
            try { emitSessionStart(); } catch (Throwable t) { Log.w(TAG, "emitSessionStart short bg failed", t); }
            return;
        }
        // Rotate. Every resume points at the same root so the dashboard can
        // collapse them under one launch regardless of how many times the
        // user backgrounds during a play day.
        sSessionId = UUID.randomUUID().toString();
        sParentSessionId = sRootSessionId;
        // Push the new id into the native crash handler so a SIGSEGV mid-resume
        // stamps the *current* session into the .crash header (not the prior
        // launch's). The next-launch drain reads this to know which log_session
        // to append the recovered tail to.
        try { BugpunchCrashHandler.setSessionId(sSessionId); }
        catch (Throwable t) { Log.w(TAG, "setSessionId on resume failed", t); }
        Log.i(TAG, "session resumed after " + (away / 1000) + "s background → " + sSessionId);
        try { emitSessionStart(); } catch (Throwable t) { Log.w(TAG, "emitSessionStart resume failed", t); }
    }

    /** Has the Activity-bound init completed? */
    public static boolean isActivityAttached() { return sActivityAttached; }

    /**
     * Currently attached Unity player Activity, or null if Activity-bound
     * init hasn't run yet. Exposed for the debug-mode auto-prompt flow which
     * needs to surface UI when the tunnel's roleConfig changes mid-session.
     */
    public static Activity getAttachedActivity() { return sAttachedActivity; }

    /** Raw config object (nullable until {@link #start} has run). */
    static JSONObject getConfig() { return sConfig; }

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

    // ── Metadata + custom data (called from C# as game state changes) ──

    public static void setCustomData(String key, String value) {
        if (key == null) return;
        if (value == null) sCustomData.remove(key);
        else sCustomData.put(key, value);
    }

    /** Update the current Unity scene name (the one thing we can't derive natively). */
    public static void updateScene(String scene) {
        if (scene != null) sMetadata.put("scene", scene);
        BugpunchPerfMonitor.onSceneChange(scene);
        // Timeline marker in the breadcrumb ring — drain surfaces these so
        // the storyboard can show "Tapped Buy → scene changed to Shop → …".
        BugpunchInput.pushSceneChange(System.currentTimeMillis(), scene == null ? "" : scene);
    }

    // ── Custom product-analytics events ──────────────────────────────────
    //
    // Ring buffer in memory; flushed every ANALYTICS_FLUSH_MS or when the
    // buffer hits ANALYTICS_FLUSH_SIZE, whichever comes first. Flush goes
    // through BugpunchUploader.enqueueJson so disk persistence + retry are
    // reused — same guarantees as perf/crash uploads.

    private static final int ANALYTICS_BUFFER_MAX = 500;
    private static final int ANALYTICS_FLUSH_SIZE = 50;
    private static final long ANALYTICS_FLUSH_MS = 15_000L;

    private static final Object sAnalyticsLock = new Object();
    private static final ArrayList<JSONObject> sAnalyticsBuffer = new ArrayList<>();
    private static Handler sAnalyticsHandler;
    private static final Runnable sFlushRunnable = new Runnable() {
        @Override public void run() {
            flushAnalytics();
            if (sAnalyticsHandler != null) {
                sAnalyticsHandler.postDelayed(this, ANALYTICS_FLUSH_MS);
            }
        }
    };

    private static void ensureAnalyticsHandler() {
        if (sAnalyticsHandler != null) return;
        HandlerThread t = new HandlerThread("bp-analytics");
        t.start();
        sAnalyticsHandler = new Handler(t.getLooper());
        sAnalyticsHandler.postDelayed(sFlushRunnable, ANALYTICS_FLUSH_MS);
    }

    /**
     * Stable per-install id. UUID generated lazily on first call and persisted
     * in app-private SharedPreferences. Cleared only by an app uninstall.
     * On Android we don't try to use ANDROID_ID (no longer device-stable since
     * Oreo) or build a fingerprint — install-stable is what every retention
     * model needs and it's all we ever read.
     */
    public static String getInstallId() {
        if (sInstallId != null) return sInstallId;
        if (sAppContext == null) return "";
        synchronized (BugpunchRuntime.class) {
            if (sInstallId != null) return sInstallId;
            try {
                android.content.SharedPreferences prefs =
                    sAppContext.getSharedPreferences(INSTALL_PREFS, Context.MODE_PRIVATE);
                String id = prefs.getString(INSTALL_KEY, null);
                if (id == null) {
                    id = UUID.randomUUID().toString();
                    prefs.edit().putString(INSTALL_KEY, id).apply();
                }
                sInstallId = id;
            } catch (Throwable t) {
                Log.w(TAG, "install_id init failed", t);
                sInstallId = UUID.randomUUID().toString();
            }
            return sInstallId;
        }
    }

    /** Set the app-supplied user id (e.g. after login). Pass null to clear. */
    public static void setUserId(String userId) {
        sUserId = userId;
    }

    /** Set/clear a user property in the in-memory bag. */
    public static void setUserProperty(String key, String value) {
        if (key == null || key.isEmpty()) return;
        if (value == null) sUserProps.remove(key);
        else sUserProps.put(key, value);
    }

    /** Free-form 'design' event — back-compat with the pre-rework SDK shape. */
    public static void trackEvent(String name, String propertiesJson) {
        if (name == null || name.isEmpty() || !sStarted) return;
        try {
            JSONObject ev = baseEvent("design");
            ev.put("name", name);
            if (propertiesJson != null && !propertiesJson.isEmpty()) {
                try { ev.put("properties", new JSONObject(propertiesJson)); }
                catch (JSONException ignore) {}
            }
            enqueueEvent(ev);
        } catch (Throwable t) {
            Log.w(TAG, "trackEvent failed", t);
        }
    }

    /**
     * Typed analytics event. {@code payloadJson} is the per-type field set
     * (amountMicros / currency / itemId / adAction / progPath / etc) — merged
     * onto the base event before enqueue. Identity (install_id, session_id,
     * country, locale, user_id) is filled in here so the SDK caller doesn't
     * have to thread it through every site.
     */
    public static void logTypedEvent(String type, String payloadJson) {
        if (type == null || type.isEmpty() || !sStarted) return;
        try {
            JSONObject ev = baseEvent(type);
            if (payloadJson != null && !payloadJson.isEmpty()) {
                try {
                    JSONObject payload = new JSONObject(payloadJson);
                    java.util.Iterator<String> keys = payload.keys();
                    while (keys.hasNext()) {
                        String k = keys.next();
                        ev.put(k, payload.get(k));
                    }
                } catch (JSONException ignore) {}
            }
            enqueueEvent(ev);
        } catch (Throwable t) {
            Log.w(TAG, "logTypedEvent failed", t);
        }
    }

    private static JSONObject baseEvent(String type) throws JSONException {
        JSONObject ev = new JSONObject();
        ev.put("type", type);
        ev.put("name", type);
        ev.put("timestamp", java.time.Instant.now().toString());
        ev.put("installId", getInstallId());
        ev.put("deviceId", sMetadata.getOrDefault("deviceId", ""));
        ev.put("sessionId", sSessionId);
        ev.put("platform", "android");
        ev.put("buildVersion", sMetadata.getOrDefault("appVersion", ""));
        ev.put("branch", sMetadata.getOrDefault("branch", ""));
        ev.put("changeset", sMetadata.getOrDefault("changeset", ""));
        ev.put("scene", sMetadata.getOrDefault("scene", ""));
        try {
            java.util.Locale loc = java.util.Locale.getDefault();
            ev.put("country", loc.getCountry());
            ev.put("locale", loc.toLanguageTag());
        } catch (Throwable ignore) {}
        if (sUserId != null) ev.put("userId", sUserId);
        else {
            String fallback = sCustomData.get("userId");
            if (fallback != null) ev.put("userId", fallback);
        }
        return ev;
    }

    private static void enqueueEvent(JSONObject ev) {
        boolean shouldFlush;
        synchronized (sAnalyticsLock) {
            if (sAnalyticsBuffer.size() >= ANALYTICS_BUFFER_MAX) {
                sAnalyticsBuffer.remove(0);
            }
            sAnalyticsBuffer.add(ev);
            shouldFlush = sAnalyticsBuffer.size() >= ANALYTICS_FLUSH_SIZE;
        }
        ensureAnalyticsHandler();
        if (shouldFlush) sAnalyticsHandler.post(new Runnable() {
            @Override public void run() { flushAnalytics(); }
        });
    }

    /**
     * Emit a session_start typed event. Called from the lifecycle hook on
     * cold launch and on every resume after a long-enough background.
     */
    static void emitSessionStart() {
        if (!sStarted || sSessionOpen) return;
        try {
            JSONObject ev = baseEvent("session_start");
            sSessionOpen = true;
            sSessionStartedRealtimeMs = android.os.SystemClock.elapsedRealtime();
            enqueueEvent(ev);
        } catch (Throwable t) {
            Log.w(TAG, "emitSessionStart failed", t);
        }
    }

    /** Emit a session_end typed event with the elapsed wall-time duration. */
    static void emitSessionEnd() {
        if (!sStarted || !sSessionOpen) return;
        try {
            long ms = android.os.SystemClock.elapsedRealtime() - sSessionStartedRealtimeMs;
            int durationS = (int) Math.max(0, ms / 1000);
            JSONObject ev = baseEvent("session_end");
            ev.put("durationS", durationS);
            sSessionOpen = false;
            enqueueEvent(ev);
            // Best-effort flush so the session_end lands before the OS suspends us.
            ensureAnalyticsHandler();
            sAnalyticsHandler.post(new Runnable() {
                @Override public void run() { flushAnalytics(); }
            });
        } catch (Throwable t) {
            Log.w(TAG, "emitSessionEnd failed", t);
        }
    }

    private static void flushAnalytics() {
        ArrayList<JSONObject> drained;
        synchronized (sAnalyticsLock) {
            if (sAnalyticsBuffer.isEmpty()) return;
            drained = new ArrayList<>(sAnalyticsBuffer);
            sAnalyticsBuffer.clear();
        }
        String serverUrl = getServerUrl();
        String apiKey = getApiKey();
        if (serverUrl == null || serverUrl.isEmpty() || apiKey == null || apiKey.isEmpty()) return;
        try {
            JSONArray arr = new JSONArray();
            for (JSONObject e : drained) arr.put(e);
            JSONObject body = new JSONObject();
            body.put("events", arr);
            String url = serverUrl + "/api/v1/analytics/events";
            BugpunchUploader.enqueueJson(sAppContext, url, apiKey, body.toString());
        } catch (Throwable t) {
            Log.w(TAG, "flushAnalytics failed", t);
        }
    }

    // ── Accessors for other Bugpunch modules ─────────────────────────────

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

    /** Application context, stored at start time. */
    public static Context getAppContext() { return sAppContext; }

    /** Current FPS measured by the Choreographer frame callback. */
    public static int getFps() { return sFps; }

    /** Read a single metadata field (seeded from config, scene pushed by Unity). */
    public static String getMetadata(String key) {
        return sMetadata.getOrDefault(key, "");
    }

    /** Snapshot of the current custom data map (for perf event tags). */
    public static Map<String, String> getCustomDataSnapshot() {
        return new ConcurrentHashMap<>(sCustomData);
    }

    // ── SDK self-diagnostic sink ─────────────────────────────────────────
    //
    // Bridge from C# (BugpunchNative.ReportSdkError) and from native catch
    // blocks. Banner state lives in BugpunchSdkErrorOverlay; this just keeps
    // the public entry points on Runtime so the C# side has one class to
    // bind to.

    /** Called from C# and from native catches when the SDK itself fails. */
    public static void reportSdkError(String source, String message, String stackTrace) {
        try {
            BugpunchSdkErrorOverlay.report(source, message, stackTrace);
        } catch (Throwable t) {
            // Never let the diagnostic sink itself surface an error here —
            // would loop. Plain logcat only.
            Log.w(TAG, "reportSdkError failed", t);
        }
    }

    /** Toggle the on-screen banner. Errors are still collected when off. */
    public static void setSdkErrorOverlay(boolean enabled) {
        try { BugpunchSdkErrorOverlay.setOverlayEnabled(enabled); }
        catch (Throwable t) { Log.w(TAG, "setSdkErrorOverlay failed", t); }
    }

    // ── Chat banner (unread QA-message indicator) ────────────────────────
    //
    // Driven by C# BugpunchClient.ChatReplyHeartbeat. Show is idempotent —
    // repeated calls just update the count text. Hide is fire-and-forget.

    /** Show or update the chat banner with N unread QA messages. */
    public static void showChatBanner(int unread) {
        try { BugpunchChatBanner.showOrUpdate(unread); }
        catch (Throwable t) { Log.w(TAG, "showChatBanner failed", t); }
    }

    /** Hide the chat banner. No-op if not currently shown. */
    public static void hideChatBanner() {
        try { BugpunchChatBanner.hide(); }
        catch (Throwable t) { Log.w(TAG, "hideChatBanner failed", t); }
    }

    /** Banner X tapped — tell C# so it can snooze re-show until a newer message. */
    static void onChatBannerDismissed() {
        sendUnityMessage("OnChatBannerDismissed", "");
    }

    /** Banner body tapped — tell C# the player opened the chat. */
    static void onChatBannerOpened() {
        sendUnityMessage("OnChatBannerOpened", "");
    }

    private static void sendUnityMessage(String method, String arg) {
        try {
            Class<?> playerClass = Class.forName("com.unity3d.player.UnityPlayer");
            playerClass.getMethod("UnitySendMessage", String.class, String.class, String.class)
                .invoke(null, "BugpunchClient", method, arg == null ? "" : arg);
        } catch (Throwable t) {
            Log.w(TAG, "UnitySendMessage(" + method + ") failed", t);
        }
    }

    /**
     * Called from C# via <c>BugpunchNative.PostDirectiveResult</c> when a
     * directive-triggered script run finishes. Hands off to
     * {@link BugpunchDirectives} which POSTs to the /enrich endpoint.
     */
    public static void postDirectiveResult(String directiveId, String resultJson) {
        if (!sStarted) return;
        BugpunchDirectives.onScriptResult(directiveId, resultJson);
    }

    // ── Shake trigger ────────────────────────────────────────────────────

    private static void onShake() {
        BugpunchReportingService.reportBug("bug", "Shake report", "Triggered by shake gesture", null);
    }
}
