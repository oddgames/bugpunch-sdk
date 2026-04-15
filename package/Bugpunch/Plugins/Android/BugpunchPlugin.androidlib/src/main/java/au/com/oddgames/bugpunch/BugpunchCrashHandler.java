package au.com.oddgames.bugpunch;

import android.content.Context;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import java.io.BufferedReader;
import java.io.File;
import java.io.FileReader;
import java.io.IOException;
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
    private static final String TAG = "BugpunchCrash";
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

        AnrWatchdog(int timeoutMs, String crashDir) {
            super("BugpunchANR");
            setDaemon(true);
            mTimeoutMs = timeoutMs;
            mCrashDir = crashDir;
            mLastTickMs = System.currentTimeMillis();
        }

        /** Called from the main thread to signal liveness. */
        void tick() {
            mLastTickMs = System.currentTimeMillis();
        }

        void shutdown() {
            mRunning = false;
            interrupt();
        }

        @Override
        public void run() {
            while (mRunning) {
                try {
                    Thread.sleep(mTimeoutMs / 2);
                } catch (InterruptedException e) {
                    if (!mRunning) return;
                    continue;
                }

                long elapsed = System.currentTimeMillis() - mLastTickMs;
                if (elapsed > mTimeoutMs) {
                    Log.e(TAG, "ANR detected! Main thread unresponsive for " + elapsed + "ms");
                    writeAnrReport(elapsed);
                    // Reset tick so we don't fire again immediately
                    mLastTickMs = System.currentTimeMillis();
                }
            }
        }

        private void writeAnrReport(long elapsedMs) {
            try {
                // Capture the main thread's stack trace
                Thread mainThread = Looper.getMainLooper().getThread();
                StackTraceElement[] stack = mainThread.getStackTrace();

                StringBuilder sb = new StringBuilder();
                sb.append("BUGPUNCH_CRASH_V1\n");
                sb.append("type:ANR\n");
                sb.append("timestamp:").append(System.currentTimeMillis()).append('\n');
                sb.append("elapsed_ms:").append(elapsedMs).append('\n');
                sb.append("thread:main\n");
                sb.append("---STACK---\n");
                for (StackTraceElement frame : stack) {
                    sb.append(frame.getClassName()).append('.')
                      .append(frame.getMethodName()).append('(')
                      .append(frame.getFileName()).append(':')
                      .append(frame.getLineNumber()).append(")\n");
                }
                sb.append("---END---\n");

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

                Log.i(TAG, "ANR report written: " + file.getAbsolutePath());
            } catch (Exception e) {
                Log.e(TAG, "Failed to write ANR report", e);
            }
        }
    }
}
