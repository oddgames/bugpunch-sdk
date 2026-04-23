package au.com.oddgames.bugpunch;

import android.app.ActivityManager;
import android.app.ApplicationExitInfo;
import android.content.Context;
import android.content.SharedPreferences;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import java.io.BufferedReader;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileReader;
import java.io.FileWriter;
import java.io.IOException;
import java.io.InputStream;
import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

/**
 * Java-side crash handler companion.
 *
 * Responsibilities:
 * 1. Load the native .so and call into C to install signal handlers
 * 2. Start the ANR watchdog
 * 3. On next launch, scan for pending crash files and return them to C#
 *
 * The actual signal handlers are in bugpunch_crash.c (NDK). This Java class
 * provides the bridge that Unity C# calls via AndroidJavaClass.
 */
public class BugpunchCrashHandler {
    private static final String TAG = "[Bugpunch.CrashHandler]";
    private static final String CRASH_DIR_NAME = "bugpunch_crashes";

    private static boolean sNativeLoaded = false;
    private static boolean sInitialized = false;
    private static AnrWatchdog sAnrWatchdog;

    // ── Native methods (bugpunch_crash.c) ──

    /**
     * Install POSIX signal handlers for SIGABRT, SIGSEGV, SIGBUS, SIGFPE, SIGILL.
     * @param crashDir absolute path to the directory where crash files are written
     * @return true if handlers were installed successfully
     */
    private static native boolean nativeInstallSignalHandlers(String crashDir);

    /**
     * Uninstall signal handlers and restore the previous (chained) handlers.
     */
    private static native void nativeUninstallSignalHandlers();

    /**
     * Set metadata that will be embedded in crash reports.
     * Called from C# whenever app/device info changes.
     */
    private static native void nativeSetMetadata(String appVersion, String bundleId,
        String unityVersion, String deviceModel, String osVersion, String gpuName);

    /**
     * Register the two native-memory ring slots and their dimensions. The
     * signal handler reads raw ARGB bytes from these buffers and writes them
     * to the target attachment paths as `.raw` files when SIGSEGV fires —
     * the only way to get a screenshot out of a dead Mono runtime without
     * touching disk during normal operation.
     *
     * Buffers must be direct (allocateDirect) so their addresses survive
     * outside the JVM heap and are reachable from an async-signal-safe
     * context (no GC move, no copy).
     */
    private static native void nativeSetScreenshotBuffers(
        ByteBuffer slotA, ByteBuffer slotB, int width, int height);

    /** Target file paths the signal handler will write raw pixels to when it
     *  fires. `atCrashPath` receives the newer slot, `beforePath` the other.
     *  Paths stay stable for the process lifetime. */
    private static native void nativeSetScreenshotAttachmentPaths(
        String atCrashPath, String beforePath);

    /** Update which slot is currently newest: 0 = A, 1 = B. */
    private static native void nativeSetScreenshotNewestSlot(int slot);

    /** Set the absolute path the signal handler writes the log snapshot to
     *  at crash time. Stays fixed for the process lifetime. */
    private static native void nativeSetLogsPath(String path);

    /** Register the direct ByteBuffer that mirrors the log ring in native
     *  memory. The signal handler reads `nativeSetLogsLength` bytes from
     *  the buffer's address and writes them to the logs path. */
    private static native void nativeSetLogsBuffer(ByteBuffer buf);

    /** Update the valid-byte count inside the log buffer. Called from the
     *  logcat reader thread after each JSON regeneration. */
    private static native void nativeSetLogsLength(int length);

    /** Register the native-memory input event ring. Capacity = max entries,
     *  entrySize = bytes per record (must match writer + signal-handler layout). */
    private static native void nativeSetInputBuffer(ByteBuffer buf, int capacity, int entrySize);

    /** Update the ring's write head + valid count after each entry. */
    private static native void nativeSetInputHead(int head, int count);

    /** Absolute path the signal handler dumps the ring to at crash time. */
    private static native void nativeSetInputPath(String path);

    // ── Public API (called from Unity C# via AndroidJavaClass) ──

    /**
     * Initialize crash handling. Loads the native library, installs signal
     * handlers, and starts the ANR watchdog.
     *
     * @param context Android context (typically the Unity activity)
     * @param anrTimeoutMs ANR detection timeout in milliseconds (0 = disable)
     * @return true if initialization succeeded
     */
    public static synchronized boolean initialize(Context context, int anrTimeoutMs) {
        if (sInitialized) {
            Log.w(TAG, "already initialized");
            return true;
        }

        // Load the native library
        if (!sNativeLoaded) {
            try {
                System.loadLibrary("bugpunch_crash");
                sNativeLoaded = true;
            } catch (UnsatisfiedLinkError e) {
                Log.e(TAG, "Failed to load libugpunch_crash.so: " + e.getMessage());
                return false;
            }
        }

        // Create crash directory
        File crashDir = getCrashDir(context);
        if (!crashDir.exists() && !crashDir.mkdirs()) {
            Log.e(TAG, "Failed to create crash directory: " + crashDir.getAbsolutePath());
            return false;
        }

        // Install signal handlers
        boolean ok = nativeInstallSignalHandlers(crashDir.getAbsolutePath());
        if (!ok) {
            Log.e(TAG, "nativeInstallSignalHandlers failed");
            return false;
        }

        // Start ANR watchdog
        if (anrTimeoutMs > 0) {
            sAnrWatchdog = new AnrWatchdog(anrTimeoutMs, crashDir.getAbsolutePath());
            sAnrWatchdog.start();
            Log.i(TAG, "ANR watchdog started (timeout=" + anrTimeoutMs + "ms)");
        }

        sInitialized = true;
        Log.i(TAG, "Crash handler initialized. Crash dir: " + crashDir.getAbsolutePath());

        // Drain anything left in the upload queue from previous launches.
        try { BugpunchUploader.drain(context); } catch (Throwable t) {
            Log.w(TAG, "uploader drain failed", t);
        }
        return true;
    }

    /**
     * Set metadata that gets embedded in native crash reports.
     */
    public static void setMetadata(String appVersion, String bundleId,
            String unityVersion, String deviceModel, String osVersion, String gpuName) {
        if (sNativeLoaded) {
            nativeSetMetadata(appVersion, bundleId, unityVersion, deviceModel, osVersion, gpuName);
        }
    }

    /** Register the native-memory slot buffers with the signal handler. */
    public static void setScreenshotBuffers(
            ByteBuffer slotA, ByteBuffer slotB, int width, int height) {
        if (sNativeLoaded) nativeSetScreenshotBuffers(slotA, slotB, width, height);
    }

    /** Register the raw-attachment output paths (written at crash time). */
    public static void setScreenshotAttachmentPaths(String atCrashPath, String beforePath) {
        if (sNativeLoaded) nativeSetScreenshotAttachmentPaths(atCrashPath, beforePath);
    }

    /** Called by {@link BugpunchScreenshot} after each successful slot write. */
    public static void setScreenshotNewestSlot(int slot) {
        if (sNativeLoaded) nativeSetScreenshotNewestSlot(slot);
    }

    /** Register the log snapshot output path with the signal handler. */
    public static void setLogsPath(String path) {
        if (sNativeLoaded) nativeSetLogsPath(path);
    }

    /** Register the native-memory mirror buffer with the signal handler. */
    public static void setLogsBuffer(ByteBuffer buf) {
        if (sNativeLoaded) nativeSetLogsBuffer(buf);
    }

    /** Update how many bytes of the mirror buffer are currently valid. */
    public static void setLogsLength(int length) {
        if (sNativeLoaded) nativeSetLogsLength(length);
    }

    /** Register the input event ring buffer + its entry layout. */
    public static void setInputBuffer(ByteBuffer buf, int capacity, int entrySize) {
        if (sNativeLoaded) nativeSetInputBuffer(buf, capacity, entrySize);
    }

    /** Publish the ring head + count after writing a new entry. */
    public static void setInputHead(int head, int count) {
        if (sNativeLoaded) nativeSetInputHead(head, count);
    }

    /** Register the rescue file path for the input ring. */
    public static void setInputPath(String path) {
        if (sNativeLoaded) nativeSetInputPath(path);
    }

    /**
     * Shut down the crash handler and restore previous signal handlers.
     */
    public static synchronized void shutdown() {
        if (!sInitialized) return;

        if (sAnrWatchdog != null) {
            sAnrWatchdog.shutdown();
            sAnrWatchdog = null;
        }

        if (sNativeLoaded) {
            nativeUninstallSignalHandlers();
        }

        sInitialized = false;
        Log.i(TAG, "Crash handler shut down");
    }

    /**
     * Check for pending crash reports from a previous session.
     * Returns an array of crash file absolute paths, or empty array if none.
     */
    public static String[] getPendingCrashFiles(Context context) {
        File crashDir = getCrashDir(context);
        if (!crashDir.exists()) return new String[0];

        File[] files = crashDir.listFiles((dir, name) -> name.endsWith(".crash"));
        if (files == null || files.length == 0) return new String[0];

        String[] paths = new String[files.length];
        for (int i = 0; i < files.length; i++) {
            paths[i] = files[i].getAbsolutePath();
        }
        return paths;
    }

    /**
     * Read a crash file's contents and return as a string.
     */
    public static String readCrashFile(String path) {
        try {
            StringBuilder sb = new StringBuilder();
            BufferedReader reader = new BufferedReader(new FileReader(path));
            String line;
            while ((line = reader.readLine()) != null) {
                sb.append(line).append('\n');
            }
            reader.close();
            return sb.toString();
        } catch (IOException e) {
            Log.e(TAG, "Failed to read crash file: " + path, e);
            return null;
        }
    }

    /**
     * Delete a crash file after it has been uploaded.
     */
    public static boolean deleteCrashFile(String path) {
        return new File(path).delete();
    }

    /**
     * Notify the ANR watchdog that the main thread is alive.
     * Must be called from the main (UI) thread in Update().
     */
    public static void tickWatchdog() {
        if (sAnrWatchdog != null) {
            sAnrWatchdog.tick();
        }
    }

    private static File getCrashDir(Context context) {
        return new File(context.getFilesDir(), CRASH_DIR_NAME);
    }

    // ── ApplicationExitInfo scan (API 30+) ──
    // Android records authoritative exit reasons — especially the real ANR
    // trace from /data/anr/traces.txt — which our in-process handlers can't
    // see because the process is already dead by the time they're written.
    // At next launch we pull them and either merge into a matching .crash
    // file we wrote (enrich our rich in-process data with the OS trace) or
    // synthesize a new .crash file for ones we missed entirely (e.g. OS
    // killed us for low memory before the watchdog could react).
    private static final String AEI_PREFS = "bugpunch_aei";
    private static final String AEI_LAST_SEEN_KEY = "last_seen_ms";
    private static final String AEI_INIT_KEY = "init_ms";
    private static final long AEI_MERGE_WINDOW_MS = 30_000;

    /**
     * Scan Android's ApplicationExitInfo records for entries newer than the
     * last scan and either merge them into an existing on-disk .crash file
     * (matched by type + timestamp) or synthesize a new one. No-op on API
     * levels below R (30).
     *
     * Call this at startup BEFORE draining pending crash files so any merges
     * are included in the upload.
     */
    public static void scanApplicationExitInfo(Context context) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R) return;
        if (context == null) return;

        ActivityManager am = (ActivityManager) context.getSystemService(Context.ACTIVITY_SERVICE);
        if (am == null) return;

        SharedPreferences prefs = context.getSharedPreferences(AEI_PREFS, Context.MODE_PRIVATE);
        long lastSeenMs = prefs.getLong(AEI_LAST_SEEN_KEY, 0L);
        long initMs = prefs.getLong(AEI_INIT_KEY, 0L);
        if (initMs == 0L) {
            // First ever scan — clamp baseline to now so we don't import
            // stale ANRs from before the SDK was integrated.
            initMs = System.currentTimeMillis();
            prefs.edit().putLong(AEI_INIT_KEY, initMs).apply();
            if (lastSeenMs < initMs) lastSeenMs = initMs;
        }

        List<ApplicationExitInfo> infos;
        try {
            infos = am.getHistoricalProcessExitReasons(null, 0, 0);
        } catch (Throwable t) {
            Log.w(TAG, "getHistoricalProcessExitReasons failed", t);
            return;
        }
        if (infos == null || infos.isEmpty()) return;

        File crashDir = getCrashDir(context);
        if (!crashDir.exists() && !crashDir.mkdirs()) {
            Log.w(TAG, "AEI scan: crash dir missing and couldn't create");
            return;
        }

        long newLastSeen = lastSeenMs;
        int merged = 0, synthesized = 0;
        for (ApplicationExitInfo info : infos) {
            long ts = info.getTimestamp();
            if (ts <= lastSeenMs) continue;
            if (ts > newLastSeen) newLastSeen = ts;

            String type = mapReasonToType(info.getReason());
            if (type == null) continue; // uninteresting (user-initiated, etc.)

            String trace = readTrace(info);

            File existing = findMatchingCrashFile(crashDir, type, ts);
            if (existing != null) {
                appendAeiSection(existing, info, trace);
                merged++;
            } else {
                synthesizeAeiCrashFile(crashDir, info, trace, type);
                synthesized++;
            }
        }

        if (newLastSeen > lastSeenMs) {
            prefs.edit().putLong(AEI_LAST_SEEN_KEY, newLastSeen).apply();
        }
        if (merged + synthesized > 0) {
            Log.i(TAG, "AEI scan: merged=" + merged + " synthesized=" + synthesized);
        }
    }

    /** Translate an ApplicationExitInfo reason into our crash-file `type:` value,
     *  or null to skip (user-initiated exits, etc.). */
    private static String mapReasonToType(int reason) {
        switch (reason) {
            case ApplicationExitInfo.REASON_ANR:                     return "ANR";
            case ApplicationExitInfo.REASON_CRASH:                   return "CRASH";
            case ApplicationExitInfo.REASON_CRASH_NATIVE:            return "CRASH_NATIVE";
            case ApplicationExitInfo.REASON_LOW_MEMORY:              return "LOW_MEMORY";
            case ApplicationExitInfo.REASON_EXCESSIVE_RESOURCE_USAGE:return "EXCESSIVE_RESOURCE";
            case ApplicationExitInfo.REASON_SIGNALED:                return "SIGNALED";
            case ApplicationExitInfo.REASON_INITIALIZATION_FAILURE:  return "INIT_FAILURE";
            default: return null;
        }
    }

    private static String reasonName(int r) {
        switch (r) {
            case ApplicationExitInfo.REASON_ANR:                     return "ANR";
            case ApplicationExitInfo.REASON_CRASH:                   return "CRASH";
            case ApplicationExitInfo.REASON_CRASH_NATIVE:            return "CRASH_NATIVE";
            case ApplicationExitInfo.REASON_LOW_MEMORY:              return "LOW_MEMORY";
            case ApplicationExitInfo.REASON_EXCESSIVE_RESOURCE_USAGE:return "EXCESSIVE_RESOURCE_USAGE";
            case ApplicationExitInfo.REASON_SIGNALED:                return "SIGNALED";
            case ApplicationExitInfo.REASON_INITIALIZATION_FAILURE:  return "INITIALIZATION_FAILURE";
            case ApplicationExitInfo.REASON_OTHER:                   return "OTHER";
            case ApplicationExitInfo.REASON_USER_REQUESTED:          return "USER_REQUESTED";
            case ApplicationExitInfo.REASON_USER_STOPPED:            return "USER_STOPPED";
            case ApplicationExitInfo.REASON_EXIT_SELF:               return "EXIT_SELF";
            case ApplicationExitInfo.REASON_DEPENDENCY_DIED:         return "DEPENDENCY_DIED";
            case ApplicationExitInfo.REASON_PERMISSION_CHANGE:       return "PERMISSION_CHANGE";
            default: return "REASON_" + r;
        }
    }

    private static String readTrace(ApplicationExitInfo info) {
        InputStream is = null;
        try {
            is = info.getTraceInputStream();
            if (is == null) return null;
            ByteArrayOutputStream bos = new ByteArrayOutputStream();
            byte[] buf = new byte[8192];
            int n;
            while ((n = is.read(buf)) > 0) bos.write(buf, 0, n);
            return bos.toString("UTF-8");
        } catch (IOException e) {
            return null;
        } finally {
            if (is != null) { try { is.close(); } catch (IOException ignored) {} }
        }
    }

    /** Scan the crash dir for a .crash file whose header `type:` matches and
     *  whose `timestamp:` falls within {@link #AEI_MERGE_WINDOW_MS} of the
     *  AEI entry. Picks the closest match if several qualify. */
    private static File findMatchingCrashFile(File dir, String type, long tsMs) {
        File[] files = dir.listFiles((d, name) -> name.endsWith(".crash"));
        if (files == null) return null;
        File best = null;
        long bestDelta = Long.MAX_VALUE;
        for (File f : files) {
            try {
                byte[] head = new byte[512];
                int n;
                FileInputStream fis = new FileInputStream(f);
                try { n = fis.read(head); } finally { fis.close(); }
                if (n <= 0) continue;
                String h = new String(head, 0, n, StandardCharsets.UTF_8);
                String fileType = null, fileTs = null;
                for (String line : h.split("\\r?\\n")) {
                    if (line.startsWith("---")) break;
                    if (line.startsWith("type:")) fileType = line.substring(5);
                    else if (line.startsWith("timestamp:")) fileTs = line.substring(10);
                }
                if (fileType == null || !fileType.equals(type)) continue;
                if (fileTs == null) continue;
                long ft = Long.parseLong(fileTs.trim());
                long delta = Math.abs(ft - tsMs);
                if (delta > AEI_MERGE_WINDOW_MS) continue;
                if (delta < bestDelta) { bestDelta = delta; best = f; }
            } catch (Throwable ignored) {}
        }
        return best;
    }

    /** Append an ---AEI--- block to an existing .crash file. The drain
     *  parser stops at the first --- line when extracting header fields,
     *  so putting the block at the end keeps existing fields intact while
     *  still making the AEI data visible in the full stackTrace text the
     *  server stores. */
    private static void appendAeiSection(File file, ApplicationExitInfo info, String trace) {
        try {
            StringBuilder sb = new StringBuilder();
            sb.append("---AEI---\n");
            sb.append("aei_reason:").append(info.getReason()).append('\n');
            sb.append("aei_reason_name:").append(reasonName(info.getReason())).append('\n');
            sb.append("aei_description:").append(safeSingleLine(info.getDescription())).append('\n');
            sb.append("aei_importance:").append(info.getImportance()).append('\n');
            sb.append("aei_pss_kb:").append(info.getPss()).append('\n');
            sb.append("aei_rss_kb:").append(info.getRss()).append('\n');
            sb.append("aei_status:").append(info.getStatus()).append('\n');
            sb.append("aei_timestamp:").append(info.getTimestamp()).append('\n');
            if (trace != null && !trace.isEmpty()) {
                sb.append("---AEI_TRACE---\n").append(trace);
                if (!trace.endsWith("\n")) sb.append('\n');
                sb.append("---END_AEI_TRACE---\n");
            }
            sb.append("---END_AEI---\n");
            FileWriter w = new FileWriter(file, true);
            try { w.write(sb.toString()); } finally { w.close(); }
        } catch (IOException e) {
            Log.w(TAG, "appendAeiSection failed for " + file.getName(), e);
        }
    }

    /** Write a standalone .crash file for an AEI entry we had no in-process
     *  report for. Follows the same BUGPUNCH_CRASH_V1 schema the drain expects. */
    private static void synthesizeAeiCrashFile(File dir, ApplicationExitInfo info,
                                               String trace, String type) {
        try {
            StringBuilder sb = new StringBuilder();
            sb.append("BUGPUNCH_CRASH_V1\n");
            sb.append("type:").append(type).append('\n');
            sb.append("timestamp:").append(info.getTimestamp()).append('\n');
            sb.append("source:aei_only\n");
            sb.append("aei_reason:").append(info.getReason()).append('\n');
            sb.append("aei_reason_name:").append(reasonName(info.getReason())).append('\n');
            sb.append("aei_description:").append(safeSingleLine(info.getDescription())).append('\n');
            sb.append("aei_importance:").append(info.getImportance()).append('\n');
            sb.append("aei_pss_kb:").append(info.getPss()).append('\n');
            sb.append("aei_rss_kb:").append(info.getRss()).append('\n');
            sb.append("aei_status:").append(info.getStatus()).append('\n');
            sb.append("---STACK---\n");
            if (trace != null) {
                sb.append(trace);
                if (!trace.endsWith("\n")) sb.append('\n');
            }
            sb.append("---END---\n");
            File out = new File(dir, "aei_" + type.toLowerCase() + "_" + info.getTimestamp() + ".crash");
            FileWriter w = new FileWriter(out);
            try { w.write(sb.toString()); } finally { w.close(); }
        } catch (IOException e) {
            Log.w(TAG, "synthesizeAeiCrashFile failed", e);
        }
    }

    private static String safeSingleLine(String s) {
        if (s == null) return "";
        return s.replace('\n', ' ').replace('\r', ' ');
    }

    // ── ANR Watchdog ──

    /**
     * Detects main thread hangs by posting a tick from the main thread
     * and checking from a background thread that it arrives within the timeout.
     *
     * If the main thread doesn't tick within the timeout, writes an ANR crash
     * report to disk with the main thread's stack trace.
     */
    static class AnrWatchdog extends Thread {
        private final int mTimeoutMs;
        private final String mCrashDir;
        private volatile long mLastTickMs;
        private volatile boolean mRunning = true;
        // After firing an ANR, suppress further reports for this long.
        // One ANR per hang is enough — the server groups by fingerprint anyway.
        private static final long COOLDOWN_MS = 60_000;
        private volatile long mLastAnrFiredMs = 0;
        private final android.os.Handler mMainHandler =
            new android.os.Handler(Looper.getMainLooper());
        // Self-ticking Runnable. Posts to main Handler every 1s; when the main
        // thread is healthy, it runs and updates `mLastTickMs`. When the main
        // thread is stuck, this runnable stops running and the watchdog
        // thread sees the stale timestamp — that's a real ANR.
        private final Runnable mTickRunnable = new Runnable() {
            @Override public void run() {
                mLastTickMs = System.currentTimeMillis();
                if (mRunning) mMainHandler.postDelayed(this, 1000);
            }
        };

        // Adaptive main-thread stack sampler. Sampling is OFF during healthy
        // operation (Thread.getStackTrace needs a safepoint on ART and would
        // otherwise cost ~1% of main-thread time at 10 Hz — not free for an
        // always-on SDK). We only kick in once the main thread has missed a
        // tick for SAMPLE_TRIGGER_MS — at that point the user is about to
        // experience a hang anyway, so the cost is moot. If the main thread
        // recovers, sampling stops; if it escalates to a full ANR, the ring
        // already holds the history we need.
        private static final int SAMPLE_RING_SIZE = 50;
        private static final long SAMPLE_INTERVAL_MS = 100;
        private static final long SAMPLE_TRIGGER_MS = 1000;
        private final StackTraceElement[][] mSampleRing = new StackTraceElement[SAMPLE_RING_SIZE][];
        private final long[] mSampleTimestamps = new long[SAMPLE_RING_SIZE];
        private int mSampleHead = 0;
        private int mSampleCount = 0;

        AnrWatchdog(int timeoutMs, String crashDir) {
            super("BugpunchANR");
            setDaemon(true);
            mTimeoutMs = timeoutMs;
            mCrashDir = crashDir;
            mLastTickMs = System.currentTimeMillis();
            // Kick off the self-tick loop.
            mMainHandler.post(mTickRunnable);
        }

        /** Kept for backwards compat — the self-tick Runnable makes this unnecessary. */
        void tick() {
            mLastTickMs = System.currentTimeMillis();
        }

        void shutdown() {
            mRunning = false;
            mMainHandler.removeCallbacks(mTickRunnable);
            interrupt();
        }

        @Override
        public void run() {
            Thread mainThread = Looper.getMainLooper().getThread();
            long lastCheckMs = System.currentTimeMillis();
            // ANR liveness check cadence — half the timeout, matching the
            // original watchdog behaviour.
            long checkIntervalMs = Math.max(SAMPLE_INTERVAL_MS, mTimeoutMs / 2L);

            while (mRunning) {
                long now = System.currentTimeMillis();
                long elapsed = now - mLastTickMs;
                boolean sampling = elapsed >= SAMPLE_TRIGGER_MS;

                try {
                    // Sleep short while sampling (need 100ms cadence), longer
                    // otherwise to minimize wake-ups on a healthy main thread.
                    Thread.sleep(sampling ? SAMPLE_INTERVAL_MS
                                          : Math.min(checkIntervalMs, 500L));
                } catch (InterruptedException e) {
                    if (!mRunning) return;
                    continue;
                }

                now = System.currentTimeMillis();
                elapsed = now - mLastTickMs;

                // Adaptive sampling: only pays the safepoint cost while the
                // main thread is already missing ticks.
                if (elapsed >= SAMPLE_TRIGGER_MS) {
                    try {
                        StackTraceElement[] stack = mainThread.getStackTrace();
                        synchronized (mSampleRing) {
                            mSampleRing[mSampleHead] = stack;
                            mSampleTimestamps[mSampleHead] = now;
                            mSampleHead = (mSampleHead + 1) % SAMPLE_RING_SIZE;
                            if (mSampleCount < SAMPLE_RING_SIZE) mSampleCount++;
                        }
                    } catch (Throwable ignored) {}
                } else if (mSampleCount > 0) {
                    // Main thread recovered — drop old samples so the next
                    // hang's ring reflects that hang, not the previous one.
                    synchronized (mSampleRing) {
                        mSampleCount = 0;
                        mSampleHead = 0;
                    }
                }

                if (now - lastCheckMs < checkIntervalMs) continue;
                lastCheckMs = now;

                if (elapsed > mTimeoutMs) {
                    // Cooldown: only fire one ANR per 60s window.
                    if (now - mLastAnrFiredMs < COOLDOWN_MS) {
                        mLastTickMs = now;
                        continue;
                    }
                    Log.e(TAG, "ANR detected! Main thread unresponsive for " + elapsed + "ms");
                    mLastAnrFiredMs = now;
                    writeAnrReport(elapsed);
                    // Reset tick so we don't fire again immediately
                    mLastTickMs = now;
                }
            }
        }

        private void writeAnrReport(long elapsedMs) {
            try {
                long baseTs = System.currentTimeMillis();

                // 1 frame BEFORE (from the shared rolling buffer — last frame
                // captured ~1s ago while the main thread was still healthy).
                // + 2 frames AFTER (live PixelCopy captures at 500ms intervals
                // to check if the screen is still updating).
                List<String> shotPaths = new ArrayList<>();
                List<Long> shotTimestamps = new ArrayList<>();

                // Before-frame from rolling buffer.
                if (BugpunchScreenshot.hasLastFrame()) {
                    long beforeTs = BugpunchScreenshot.getLastFrameTimestamp();
                    String beforePath = mCrashDir + "/anr_" + beforeTs + "_before.jpg";
                    if (BugpunchScreenshot.writeLastFrame(beforePath, 75)) {
                        shotPaths.add(beforePath);
                        shotTimestamps.add(beforeTs);
                    }
                }

                // 2 after-frames via live PixelCopy.
                for (int i = 0; i < 2; i++) {
                    if (i > 0) {
                        try { Thread.sleep(500); } catch (InterruptedException ignored) {}
                    }
                    long shotTs = System.currentTimeMillis();
                    String path = mCrashDir + "/anr_" + shotTs + "_after" + i + ".jpg";
                    BugpunchScreenshot.captureSync(path, 75);
                    File f = new File(path);
                    if (f.exists() && f.length() > 0) {
                        shotPaths.add(path);
                        shotTimestamps.add(shotTs);
                    }
                }

                // Capture the main thread's stack trace
                Thread mainThread = Looper.getMainLooper().getThread();
                StackTraceElement[] stack = mainThread.getStackTrace();

                StringBuilder sb = new StringBuilder();
                sb.append("BUGPUNCH_CRASH_V1\n");
                sb.append("type:ANR\n");
                sb.append("timestamp:").append(baseTs).append('\n');
                sb.append("elapsed_ms:").append(elapsedMs).append('\n');
                sb.append("thread:main\n");
                // Include screenshot paths so drainPendingCrashFiles can attach them.
                // First screenshot goes in the standard field for backwards compat.
                if (!shotPaths.isEmpty()) {
                    sb.append("screenshot:").append(shotPaths.get(0)).append('\n');
                }
                // Additional ANR screenshots (the burst series) with timestamps.
                for (int i = 0; i < shotPaths.size(); i++) {
                    sb.append("anr_screenshot_").append(i).append(':')
                      .append(shotPaths.get(i)).append('\n');
                    sb.append("anr_screenshot_ts_").append(i).append(':')
                      .append(shotTimestamps.get(i)).append('\n');
                }
                sb.append("screenshot_count:").append(shotPaths.size()).append('\n');
                sb.append("---STACK---\n");
                for (StackTraceElement frame : stack) {
                    sb.append(frame.getClassName()).append('.')
                      .append(frame.getMethodName()).append('(')
                      .append(frame.getFileName()).append(':')
                      .append(frame.getLineNumber()).append(")\n");
                }
                sb.append("---END---\n");

                // Stack sample history (adaptive sampler, 10 Hz, only active
                // once the main thread has been missing ticks for ~1s). Shows
                // whether the thread was pinned in one spot the whole hang or
                // progressed through several frames before getting stuck —
                // crucial for distinguishing "bad lock" vs "bad loop".
                synchronized (mSampleRing) {
                    if (mSampleCount > 0) {
                        sb.append("---STACK_SAMPLES---\n");
                        sb.append("sample_count:").append(mSampleCount).append('\n');
                        int start = (mSampleHead - mSampleCount + SAMPLE_RING_SIZE) % SAMPLE_RING_SIZE;
                        for (int i = 0; i < mSampleCount; i++) {
                            int idx = (start + i) % SAMPLE_RING_SIZE;
                            sb.append("sample_ts:").append(mSampleTimestamps[idx]).append('\n');
                            StackTraceElement[] s = mSampleRing[idx];
                            if (s != null) {
                                for (StackTraceElement frame : s) {
                                    sb.append("  ").append(frame.toString()).append('\n');
                                }
                            }
                            sb.append('\n');
                        }
                        sb.append("---END_STACK_SAMPLES---\n");
                    }
                }

                // Also capture all thread stacks for context
                sb.append("---ALL_THREADS---\n");
                for (java.util.Map.Entry<Thread, StackTraceElement[]> entry :
                        Thread.getAllStackTraces().entrySet()) {
                    Thread t = entry.getKey();
                    sb.append("thread:").append(t.getName())
                      .append(" id=").append(t.getId())
                      .append(" state=").append(t.getState()).append('\n');
                    for (StackTraceElement frame : entry.getValue()) {
                        sb.append("  ").append(frame.toString()).append('\n');
                    }
                    sb.append('\n');
                }
                sb.append("---END_ALL_THREADS---\n");

                String filename = "anr_" + System.currentTimeMillis() + ".crash";
                File file = new File(mCrashDir, filename);
                java.io.FileWriter writer = new java.io.FileWriter(file);
                writer.write(sb.toString());
                writer.close();

                Log.i(TAG, "ANR report written: " + file.getAbsolutePath()
                    + " (" + shotPaths.size() + " screenshots)");
            } catch (Exception e) {
                Log.e(TAG, "Failed to write ANR report", e);
            }
        }
    }
}
