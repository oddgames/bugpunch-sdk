package au.com.oddgames.bugpunch;

import android.app.ActivityManager;
import android.content.ComponentCallbacks2;
import android.content.Context;
import android.content.SharedPreferences;
import android.content.res.Configuration;
import android.os.Build;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.PowerManager;
import android.util.DisplayMetrics;
import android.util.Log;
import android.view.WindowManager;

import org.json.JSONObject;

import java.util.Map;

/**
 * Lightweight native performance monitor. Runs on a dedicated background
 * HandlerThread — never touches the main/render thread. Reads FPS from
 * BugpunchRuntime.sFps (volatile, zero cost), memory from Runtime +
 * ActivityManager, thermal from PowerManager.
 *
 * Fires full snapshots (screenshot + metadata) only on:
 *   1. Memory pressure (OS signal via ComponentCallbacks2)
 *   2. Consistently bad FPS (rolling window average below threshold)
 *
 * Lightweight scene-change summaries and periodic heartbeats have no
 * screenshot and are batched.
 *
 * OOM detection: on startup, checks if previous session exited cleanly.
 * If not, and memory was high, posts a post-mortem OOM event.
 */
public class BugpunchPerfMonitor {
    private static final String TAG = "[Bugpunch.PerfMonitor]";
    private static final String PREFS_NAME = "bugpunch_perf";

    private static boolean sStarted;
    private static HandlerThread sThread;
    private static Handler sHandler;
    private static Context sAppContext;

    // Config from server
    private static int sFpsThreshold = 30;
    private static int sReportInterval = 60;
    private static int sSampleBudget = 10;

    // Rolling window (60 entries = 60 seconds)
    private static final int WINDOW_SIZE = 60;
    private static final float[] sFpsWindow = new float[WINDOW_SIZE];
    private static final float[] sMemWindow = new float[WINDOW_SIZE];
    private static int sWindowIndex = 0;
    private static int sWindowFilled = 0;

    // Aggregation state
    private static float sFpsMin, sFpsMax, sFpsSum;
    private static float sMemPeak;
    private static int sSampleCount;
    private static long sSceneStartMs;
    private static String sCurrentScene = "unknown";

    // Throttle: only fire one FPS-low report per reporting window
    private static long sLastFpsReportMs;
    private static long sLastPeriodicMs;

    // OOM detection
    private static final String KEY_CLEAN_EXIT = "clean_exit";
    private static final String KEY_LAST_MEM_MB = "last_mem_mb";
    private static final String KEY_LAST_SCENE = "last_scene";

    /**
     * Start the performance monitor with server-provided config.
     * Called from C# via JNI after the game config fetch.
     */
    public static synchronized void start(String configJson) {
        if (sStarted) return;
        sAppContext = BugpunchRuntime.getAppContext();
        if (sAppContext == null) {
            Log.w(TAG, "Cannot start — no app context");
            return;
        }

        // Parse config
        try {
            JSONObject cfg = new JSONObject(configJson);
            sFpsThreshold = cfg.optInt("fpsThreshold", 30);
            sReportInterval = cfg.optInt("reportInterval", 60);
            sSampleBudget = cfg.optInt("sampleBudget", 10);
        } catch (Exception e) {
            Log.w(TAG, "Config parse error, using defaults: " + e.getMessage());
        }

        // Check for OOM from previous session
        checkPreviousSessionOom();

        // Start background thread
        sThread = new HandlerThread("BugpunchPerfMon");
        sThread.start();
        sHandler = new Handler(sThread.getLooper());

        // Register memory pressure callback
        sAppContext.registerComponentCallbacks(new ComponentCallbacks2() {
            @Override
            public void onTrimMemory(int level) {
                if (level >= ComponentCallbacks2.TRIM_MEMORY_RUNNING_LOW) {
                    onMemoryPressure(level);
                }
            }
            @Override public void onConfigurationChanged(Configuration cfg) {}
            @Override public void onLowMemory() { onMemoryPressure(15); }
        });

        // Schedule periodic sampling (every 1 second)
        sSceneStartMs = System.currentTimeMillis();
        sLastPeriodicMs = System.currentTimeMillis();
        resetAggregation();
        scheduleSample();

        // Mark session as not cleanly exited (will be set on onDestroy)
        SharedPreferences prefs = sAppContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        prefs.edit()
            .putBoolean(KEY_CLEAN_EXIT, false)
            .putFloat(KEY_LAST_MEM_MB, 0)
            .apply();

        sStarted = true;
        Log.i(TAG, "Started — fpsThreshold=" + sFpsThreshold + " reportInterval=" + sReportInterval);
    }

    /** Mark a clean exit — call from BugpunchRuntime.onDestroy or similar. */
    public static void markCleanExit() {
        if (sAppContext == null) return;
        SharedPreferences prefs = sAppContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        prefs.edit().putBoolean(KEY_CLEAN_EXIT, true).apply();
    }

    /** Scene changed — flush summary for the departing scene. */
    public static void onSceneChange(String newScene) {
        if (!sStarted || sHandler == null) return;
        final String oldScene = sCurrentScene;
        sCurrentScene = newScene != null ? newScene : "unknown";

        sHandler.post(() -> {
            flushSceneSummary(oldScene);
            sSceneStartMs = System.currentTimeMillis();
            resetAggregation();
        });
    }

    // ── Sampling ──

    private static void scheduleSample() {
        if (sHandler == null) return;
        sHandler.postDelayed(BugpunchPerfMonitor::doSample, 1000);
    }

    private static void doSample() {
        if (!sStarted) return;

        // Read FPS (volatile field from BugpunchRuntime — zero cost)
        float fps = BugpunchRuntime.getFps();
        float memMB = getUsedMemoryMB();

        // Update rolling window
        sFpsWindow[sWindowIndex] = fps;
        sMemWindow[sWindowIndex] = memMB;
        sWindowIndex = (sWindowIndex + 1) % WINDOW_SIZE;
        if (sWindowFilled < WINDOW_SIZE) sWindowFilled++;

        // Update aggregation
        if (sSampleCount == 0) {
            sFpsMin = fps;
            sFpsMax = fps;
        } else {
            if (fps < sFpsMin) sFpsMin = fps;
            if (fps > sFpsMax) sFpsMax = fps;
        }
        sFpsSum += fps;
        if (memMB > sMemPeak) sMemPeak = memMB;
        sSampleCount++;

        // Save memory watermark for OOM detection
        if (sAppContext != null && sSampleCount % 10 == 0) {
            SharedPreferences prefs = sAppContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
            prefs.edit()
                .putFloat(KEY_LAST_MEM_MB, memMB)
                .putString(KEY_LAST_SCENE, sCurrentScene)
                .apply();
        }

        // Check for consistently bad FPS
        if (sWindowFilled >= 10) { // need at least 10 seconds of data
            float windowAvg = computeWindowAvg(sFpsWindow, sWindowFilled);
            long nowMs = System.currentTimeMillis();
            if (windowAvg < sFpsThreshold && windowAvg > 0 &&
                nowMs - sLastFpsReportMs > sReportInterval * 1000L) {
                sLastFpsReportMs = nowMs;
                firePerfEvent("fps_low", true);
            }
        }

        // Periodic summary (no screenshot)
        long nowMs = System.currentTimeMillis();
        if (nowMs - sLastPeriodicMs >= sReportInterval * 1000L) {
            sLastPeriodicMs = nowMs;
            firePerfEvent("periodic", false);
            resetAggregation();
        }

        scheduleSample();
    }

    // ── Memory pressure handler ──

    private static void onMemoryPressure(int level) {
        if (!sStarted) return;
        Log.w(TAG, "Memory pressure: level=" + level);
        if (sHandler != null) {
            sHandler.post(() -> firePerfEvent("memory_pressure", true));
        }
    }

    // ── OOM detection ──

    private static void checkPreviousSessionOom() {
        SharedPreferences prefs = sAppContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        boolean cleanExit = prefs.getBoolean(KEY_CLEAN_EXIT, true);
        float lastMemMB = prefs.getFloat(KEY_LAST_MEM_MB, 0);
        String lastScene = prefs.getString(KEY_LAST_SCENE, "unknown");

        if (!cleanExit && lastMemMB > 100) {
            // Previous session didn't exit cleanly and had high memory — likely OOM
            Log.w(TAG, "Previous session may have OOM'd (memMB=" + lastMemMB + ", scene=" + lastScene + ")");

            // Post a post-mortem OOM event (no screenshot — we're in a new session)
            try {
                JSONObject event = buildEventJson("oom", false);
                event.put("scene", lastScene);
                event.put("memoryPeakMB", lastMemMB);
                uploadEvent(event);
            } catch (Exception e) {
                Log.e(TAG, "Failed to report OOM: " + e.getMessage());
            }
        }

        // Reset for this session
        prefs.edit().putBoolean(KEY_CLEAN_EXIT, false).apply();
    }

    // ── Event building & upload ──

    private static void firePerfEvent(String trigger, boolean withScreenshot) {
        try {
            JSONObject event = buildEventJson(trigger, withScreenshot);

            if (withScreenshot) {
                // Generate output path, capture screenshot, upload when done
                String dir = sAppContext.getCacheDir().getAbsolutePath();
                String screenshotPath = dir + "/bp_perf_" + System.nanoTime() + ".jpg";
                BugpunchScreenshot.captureThen(screenshotPath, 80, () -> {
                    try {
                        event.put("screenshotPath", screenshotPath);
                        uploadEvent(event);
                    } catch (Exception e) {
                        Log.e(TAG, "Screenshot upload failed: " + e.getMessage());
                    }
                });
            } else {
                uploadEvent(event);
            }
        } catch (Exception e) {
            Log.e(TAG, "firePerfEvent failed: " + e.getMessage());
        }
    }

    private static JSONObject buildEventJson(String trigger, boolean isFull) {
        JSONObject event = new JSONObject();
        try {
            event.put("trigger", trigger);
            event.put("scene", sCurrentScene);
            event.put("buildVersion", BugpunchRuntime.getMetadata("appVersion"));
            event.put("branch", BugpunchRuntime.getMetadata("branch"));
            event.put("changeset", BugpunchRuntime.getMetadata("changeset"));
            event.put("platform", "Android");
            event.put("deviceId", BugpunchRuntime.getMetadata("deviceId"));
            event.put("deviceName", Build.MODEL);
            event.put("deviceModel", Build.MANUFACTURER + " " + Build.MODEL);
            event.put("deviceTier", computeDeviceTier());
            event.put("gpu", BugpunchRuntime.getMetadata("gpu"));

            // Screen size
            if (sAppContext != null) {
                WindowManager wm = (WindowManager) sAppContext.getSystemService(Context.WINDOW_SERVICE);
                if (wm != null) {
                    DisplayMetrics dm = new DisplayMetrics();
                    wm.getDefaultDisplay().getMetrics(dm);
                    event.put("screenSize", dm.widthPixels + "x" + dm.heightPixels);
                }
            }

            // System memory
            int totalMemMB = getTotalMemoryMB();
            event.put("systemMemoryMB", totalMemMB);

            // FPS aggregates
            if (sSampleCount > 0) {
                event.put("fpsAvg", Math.round(sFpsSum / sSampleCount * 10f) / 10f);
                event.put("fpsMin", sFpsMin);
                event.put("fpsMax", sFpsMax);
                event.put("fpsP5", computePercentile(sFpsWindow, sWindowFilled, 5));
            }

            // Memory
            float usedMB = getUsedMemoryMB();
            event.put("memoryTotalMB", Math.round(usedMB * 10f) / 10f);
            event.put("memoryPeakMB", Math.round(sMemPeak * 10f) / 10f);
            event.put("memoryAvailableMB", getAvailableMemoryMB());

            // Battery
            event.put("batteryLevel", getBatteryLevel());

            // Thermal state (API 29+)
            if (Build.VERSION.SDK_INT >= 29) {
                PowerManager pm = (PowerManager) sAppContext.getSystemService(Context.POWER_SERVICE);
                if (pm != null) {
                    int thermal = pm.getCurrentThermalStatus();
                    String[] states = {"nominal", "light", "moderate", "severe", "critical", "emergency", "shutdown"};
                    event.put("thermalState", thermal < states.length ? states[thermal] : "unknown");
                }
            }

            // Tags from custom data
            JSONObject tags = new JSONObject();
            for (Map.Entry<String, String> entry : BugpunchRuntime.getCustomDataSnapshot().entrySet()) {
                tags.put(entry.getKey(), entry.getValue());
            }
            event.put("tags", tags);

        } catch (Exception e) {
            Log.e(TAG, "buildEventJson error: " + e.getMessage());
        }
        return event;
    }

    private static void uploadEvent(JSONObject event) {
        String serverUrl = BugpunchRuntime.getServerUrl();
        String apiKey = BugpunchRuntime.getApiKey();
        if (serverUrl == null || apiKey == null) return;

        String url = serverUrl + "/api/v1/perf/events";
        String screenshotPath = event.optString("screenshotPath", null);

        if (screenshotPath != null) {
            // Multipart upload with screenshot
            event.remove("screenshotPath");
            BugpunchUploader.enqueue(sAppContext, url, apiKey,
                event.toString(), screenshotPath, null, null);
        } else {
            // JSON-only upload
            BugpunchUploader.enqueueJson(sAppContext, url, apiKey, event.toString());
        }
    }

    private static void flushSceneSummary(String scene) {
        if (sSampleCount == 0) return;
        try {
            JSONObject event = buildEventJson("scene_change", false);
            event.put("scene", scene);
            uploadEvent(event);
        } catch (Exception e) {
            Log.e(TAG, "flushSceneSummary failed: " + e.getMessage());
        }
        resetAggregation();
    }

    // ── Helpers ──

    private static void resetAggregation() {
        sFpsMin = 0;
        sFpsMax = 0;
        sFpsSum = 0;
        sMemPeak = 0;
        sSampleCount = 0;
    }

    private static float computeWindowAvg(float[] window, int filled) {
        float sum = 0;
        for (int i = 0; i < filled; i++) sum += window[i];
        return filled > 0 ? sum / filled : 0;
    }

    private static float computePercentile(float[] window, int filled, int percentile) {
        if (filled == 0) return 0;
        float[] sorted = new float[filled];
        System.arraycopy(window, 0, sorted, 0, filled);
        java.util.Arrays.sort(sorted);
        int idx = (int) Math.ceil(percentile / 100.0 * filled) - 1;
        if (idx < 0) idx = 0;
        return sorted[idx];
    }

    private static float getUsedMemoryMB() {
        Runtime rt = Runtime.getRuntime();
        return (rt.totalMemory() - rt.freeMemory()) / (1024f * 1024f);
    }

    private static int getTotalMemoryMB() {
        if (sAppContext == null) return 0;
        ActivityManager am = (ActivityManager) sAppContext.getSystemService(Context.ACTIVITY_SERVICE);
        if (am == null) return 0;
        ActivityManager.MemoryInfo mi = new ActivityManager.MemoryInfo();
        am.getMemoryInfo(mi);
        return (int) (mi.totalMem / (1024 * 1024));
    }

    private static float getAvailableMemoryMB() {
        if (sAppContext == null) return 0;
        ActivityManager am = (ActivityManager) sAppContext.getSystemService(Context.ACTIVITY_SERVICE);
        if (am == null) return 0;
        ActivityManager.MemoryInfo mi = new ActivityManager.MemoryInfo();
        am.getMemoryInfo(mi);
        return mi.availMem / (1024f * 1024f);
    }

    private static float getBatteryLevel() {
        try {
            android.content.Intent batteryStatus = sAppContext.registerReceiver(null,
                new android.content.IntentFilter(android.content.Intent.ACTION_BATTERY_CHANGED));
            if (batteryStatus == null) return -1;
            int level = batteryStatus.getIntExtra(android.os.BatteryManager.EXTRA_LEVEL, -1);
            int scale = batteryStatus.getIntExtra(android.os.BatteryManager.EXTRA_SCALE, -1);
            return level >= 0 && scale > 0 ? level / (float) scale : -1;
        } catch (Exception e) {
            return -1;
        }
    }

    private static String computeDeviceTier() {
        int memMB = getTotalMemoryMB();
        int cpuCount = Runtime.getRuntime().availableProcessors();
        if (memMB >= 6144 && cpuCount >= 6) return "high";
        if (memMB >= 3072 && cpuCount >= 4) return "mid";
        return "low";
    }
}
