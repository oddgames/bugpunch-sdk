package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.os.Build;
import android.os.Handler;
import android.os.HandlerThread;
import android.util.Base64;
import android.util.Log;
import android.view.PixelCopy;
import android.view.SurfaceView;
import android.view.View;
import android.view.ViewGroup;

import java.io.File;
import java.io.FileOutputStream;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

/**
 * Native screenshot capture via PixelCopy. Reads the Unity SurfaceView's GPU
 * buffer without any user permission prompt (we're reading our own window).
 *
 * Called from Unity C# via AndroidJavaClass. Result delivered back via
 * UnitySendMessage so Unity doesn't block waiting for PixelCopy.
 */
public class BugpunchScreenshot {
    private static final String TAG = "[Bugpunch.Screenshot]";

    private static HandlerThread sThread;
    private static Handler sHandler;
    private static final ConcurrentHashMap<String, Boolean> sInFlight = new ConcurrentHashMap<>();
    // Cached for ANR screenshots — finding the SurfaceView requires the UI
    // thread, which is stuck during an ANR.
    private static volatile SurfaceView sCachedSurface;

    // ── Shared rolling screenshot buffer ──
    // PixelCopies the GPU surface every ROLLING_INTERVAL_MS into one of two
    // native-memory slots. No disk I/O during normal operation — the only
    // reason we'd touch disk is the signal handler for a SIGSEGV/SIGABRT,
    // which dumps the native bytes so the next process launch can upload them.
    //
    // Keeping the pixels in native memory (direct ByteBuffer) rather than a
    // Java Bitmap means they survive Mono/IL2CPP meltdowns: when managed code
    // explodes we can still read the pre-crash frame out of native memory and
    // attach it to the crash report.
    //
    // Two slots rotate so we always have:
    //   - "at crash"  = the most recent completed slot (≤ROLLING_INTERVAL_MS old)
    //   - "before"    = the other slot (1-2× interval old)
    // Only active on devices with enough RAM.
    private static volatile Bitmap sLastFrame;
    private static volatile long sLastFrameTs;
    private static volatile boolean sRollingActive;
    private static final long ROLLING_INTERVAL_MS = 1000;
    /** Fast retry until the first successful PixelCopy lands, so a crash in
     *  the first second of play still has a frame to attach. */
    private static final long STARTUP_RETRY_MS = 150;
    /** True after the first successful capture — flips the scheduler out of
     *  fast-retry mode and gates failure logging so we don't spam the console. */
    private static volatile boolean sHadFirstCapture;

    /** Longest-side target for the downscaled capture — GPU scales on PixelCopy
     *  so there's no CPU cost to going smaller. 960 balances detail with the
     *  16-bit-wide native ring (~3.6MB total) on commodity phones. */
    private static final int CAPTURE_LONG_SIDE = 960;

    /** Native-memory ring slots. DirectByteBuffer addresses are handed to bp.c
     *  so the signal handler can copy bytes out even after Mono is dead. */
    private static volatile ByteBuffer sSlotA;
    private static volatile ByteBuffer sSlotB;
    private static volatile int sCaptureW;
    private static volatile int sCaptureH;
    /** Reused target bitmap so we don't churn the heap each second. */
    private static volatile Bitmap sCaptureBitmap;

    /** Index of the most-recently-written slot: 0 = A, 1 = B. */
    private static volatile int sNewestSlot = -1;
    /** Next slot to write into. Alternates after each capture. */
    private static int sNextSlot = 0;
    /** Minimum gap between captures to throttle touch-triggered kicks
     *  without starving normal 1 Hz. */
    private static final long MIN_CAPTURE_GAP_MS = 200;
    private static volatile long sLastCaptureAttemptMs;

    /** Legacy no-op retained for existing callers — disk flush has moved into
     *  the native signal handler, so Java-side paths are no longer used. */
    public static void setRollingDiskPaths(String pathA, String pathB) { /* no-op */ }
    public static void setRollingDiskPath(String path) { /* no-op */ }
    public static String getFramePathA() { return null; }
    public static String getFramePathB() { return null; }
    public static int getNewestSlot() { return sNewestSlot; }
    public static String getRollingDiskPath() { return null; }

    /**
     * Start the rolling screenshot buffer. Call once at startup.
     * Only activates on high-end devices (>= 256MB heap).
     *
     * Capture is fully asynchronous (no latch.await). Each capture step
     * issues PixelCopy.request and returns; the callback writes the slot
     * AND schedules the next step — fast retry (STARTUP_RETRY_MS) while
     * anything is still failing, normal cadence (ROLLING_INTERVAL_MS)
     * after the first successful capture. Blocking here deadlocks with
     * the callback handler, which is exactly what `rc=-1 (latch timeout)`
     * used to mean.
     */
    public static void startRollingBuffer() {
        long maxMb = Runtime.getRuntime().maxMemory() / (1024 * 1024);
        if (maxMb < 256) {
            Log.i(TAG, "rolling buffer skipped — low memory device (" + maxMb + "MB heap)");
            return;
        }
        if (sRollingActive) return;
        sRollingActive = true;
        getHandler().post(sCaptureStep);
        Log.i(TAG, "rolling buffer started (" + maxMb + "MB heap)");
    }

    /** The one and only entry to kick off a capture step. All scheduling
     *  goes through this Runnable — both the periodic tick and the kick()
     *  path post or remove it from the handler queue. */
    private static final Runnable sCaptureStep = new Runnable() {
        @Override public void run() {
            if (!sRollingActive) return;
            captureStep();
        }
    };

    private static void rescheduleFast() {
        if (!sRollingActive) return;
        Handler h = getHandler();
        h.removeCallbacks(sCaptureStep);
        h.postDelayed(sCaptureStep, STARTUP_RETRY_MS);
    }

    private static void rescheduleSlow() {
        if (!sRollingActive) return;
        Handler h = getHandler();
        h.removeCallbacks(sCaptureStep);
        h.postDelayed(sCaptureStep, ROLLING_INTERVAL_MS);
    }

    /** Stop the rolling buffer. */
    public static void stopRollingBuffer() {
        sRollingActive = false;
        sLastFrame = null;
    }

    /**
     * Allocate the downscaled capture bitmap + native slot buffers once we
     * know the SurfaceView dimensions. Slots live for the life of the
     * process; bp.c holds their native addresses via GetDirectBufferAddress.
     */
    private static void ensureSlots(SurfaceView sv) {
        if (sSlotA != null && sSlotB != null && sCaptureBitmap != null) return;
        int sw = sv.getWidth(), sh = sv.getHeight();
        if (sw <= 0 || sh <= 0) return;
        int w, h;
        if (sw >= sh) {
            w = Math.min(sw, CAPTURE_LONG_SIDE);
            h = (int) ((long) sh * w / sw);
        } else {
            h = Math.min(sh, CAPTURE_LONG_SIDE);
            w = (int) ((long) sw * h / sh);
        }
        // Round to even so stride is well-behaved for downstream encoders.
        w = (w + 1) & ~1;
        h = (h + 1) & ~1;
        int bytes = w * h * 4;
        sCaptureW = w;
        sCaptureH = h;
        sSlotA = ByteBuffer.allocateDirect(bytes).order(ByteOrder.nativeOrder());
        sSlotB = ByteBuffer.allocateDirect(bytes).order(ByteOrder.nativeOrder());
        sCaptureBitmap = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888);
        // Hand the native addresses + dimensions to the signal handler so it
        // can dump the ring contents when SIGSEGV fires.
        try { BugpunchCrashHandler.setScreenshotBuffers(sSlotA, sSlotB, w, h); }
        catch (Throwable ignored) {}
    }

    /**
     * Request a rolling-buffer capture now, bypassing the normal 1 Hz tick.
     * Called from touch / scene-change hooks so the ring is fresh at the
     * moments most correlated with crashes. Throttled to avoid starving
     * the capture thread during rapid tapping.
     */
    public static void kickCapture() {
        if (!sRollingActive) return;
        long now = System.currentTimeMillis();
        if (now - sLastCaptureAttemptMs < MIN_CAPTURE_GAP_MS) return;
        sLastCaptureAttemptMs = now;
        Handler h = getHandler();
        h.removeCallbacks(sCaptureStep);
        h.post(sCaptureStep);
    }

    /**
     * One asynchronous capture step. Returns immediately after issuing
     * PixelCopy.request; the callback handles the memcpy and schedules
     * the next step via rescheduleSlow()/rescheduleFast(). Never blocks.
     *
     * The callback handler is the same BugpunchScreenshot HandlerThread
     * we're running on — but because we've already returned by the time
     * PixelCopy fires the callback, the handler is free and there's no
     * deadlock. (The previous version used latch.await() on this thread,
     * which starved the callback and caused every capture to time out.)
     */
    private static void captureStep() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.N) {
            rescheduleSlow();
            return;
        }
        SurfaceView sv = sCachedSurface;
        if (sv == null) {
            // Re-drive the cache lookup — Unity's SurfaceView may not have
            // been attached to the window yet on the first couple of ticks.
            cacheSurfaceView();
            if (!sHadFirstCapture) {
                Log.d(TAG, "capture skipped: SurfaceView not yet cached");
            }
            rescheduleFast();
            return;
        }
        ensureSlots(sv);
        final Bitmap bmp = sCaptureBitmap;
        if (bmp == null) {
            if (!sHadFirstCapture) {
                Log.d(TAG, "capture skipped: surface dims not ready (w="
                    + sv.getWidth() + " h=" + sv.getHeight() + ")");
            }
            rescheduleFast();
            return;
        }
        if (!sv.getHolder().getSurface().isValid()) {
            if (!sHadFirstCapture) Log.d(TAG, "capture skipped: surface not yet valid");
            rescheduleFast();
            return;
        }
        try {
            // GPU downscales source → bmp dimensions on its own, so there's
            // no CPU cost to capturing smaller than the actual display.
            PixelCopy.request(sv, bmp, new PixelCopy.OnPixelCopyFinishedListener() {
                @Override public void onPixelCopyFinished(int rc) {
                    onCaptureComplete(bmp, rc);
                }
            }, getHandler());
        } catch (Throwable t) {
            if (!sHadFirstCapture) Log.d(TAG, "capture request threw: " + t.getMessage());
            rescheduleFast();
        }
    }

    /**
     * PixelCopy callback. Runs on the same HandlerThread we scheduled on,
     * but captureStep has already returned so the handler is free.
     */
    private static void onCaptureComplete(Bitmap bmp, int rc) {
        if (rc != PixelCopy.SUCCESS) {
            if (!sHadFirstCapture) {
                // PixelCopy codes: 0=SUCCESS, 1=UNKNOWN, 2=TIMEOUT,
                // 3=SOURCE_NO_DATA, 4=SOURCE_INVALID, 5=DESTINATION_INVALID.
                Log.d(TAG, "capture failed: PixelCopy rc=" + rc);
            }
            rescheduleFast();
            return;
        }
        try {
            // Memcpy pixels from the Java Bitmap into the next native slot.
            // Update sNewestSlot only AFTER the copy completes so a crash
            // mid-copy still leaves the previous slot pointing at a valid
            // frame (this slot becomes garbage, other slot stays intact).
            int slot = sNextSlot;
            ByteBuffer dst = (slot == 0) ? sSlotA : sSlotB;
            if (dst == null) { rescheduleFast(); return; }
            dst.clear();
            bmp.copyPixelsToBuffer(dst);
            sNewestSlot = slot;
            sNextSlot = 1 - slot;
            sLastFrameTs = System.currentTimeMillis();
            // Preserve the Java-side reference so the ANR / exception / bug
            // report paths can still encode JPEG at report time from Java
            // heap — those paths run while Mono is alive and don't need the
            // native-memory rescue.
            sLastFrame = bmp;
            // Tell the native signal handler which slot is "at crash" now.
            try { BugpunchCrashHandler.setScreenshotNewestSlot(slot); }
            catch (Throwable ignored) {}
            if (!sHadFirstCapture) {
                sHadFirstCapture = true;
                Log.i(TAG, "first capture OK (" + sCaptureW + "x" + sCaptureH + ")");
            }
        } catch (Throwable ignored) {
            // Never crash the app for a background screenshot.
        }
        rescheduleSlow();
    }

    /** Epoch ms timestamp of the last captured frame, or 0 if none. */
    public static long getLastFrameTimestamp() { return sLastFrameTs; }

    /** Whether the rolling buffer is active and has at least one frame. */
    public static boolean hasLastFrame() { return sLastFrame != null; }

    /**
     * Write the last buffered frame to a JPEG file. Compression happens here,
     * not in the rolling capture loop. Thread-safe.
     * @return true if written successfully.
     */
    public static boolean writeLastFrame(String outputPath, int quality) {
        Bitmap frame = sLastFrame;
        if (frame == null) return false;
        try {
            return writeJpeg(frame, outputPath, quality) == null;
        } catch (Throwable t) {
            Log.w(TAG, "writeLastFrame failed", t);
            return false;
        }
    }

    /** Cache the Unity SurfaceView for later use by captureSync (ANR path). */
    public static void cacheSurfaceView() {
        Activity activity = BugpunchUnity.currentActivity();
        if (activity == null) return;
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                try {
                    View root = activity.getWindow().getDecorView();
                    sCachedSurface = findSurfaceView(root);
                } catch (Throwable t) {
                    Log.w(TAG, "cacheSurfaceView failed", t);
                }
            }
        });
    }

    /**
     * Blocking screenshot for ANR path. Uses the cached SurfaceView + PixelCopy
     * from a background thread. Times out after 3 seconds. Does NOT touch the
     * main thread (which is stuck during ANR).
     */
    public static void captureSync(String outputPath, int quality) {
        SurfaceView sv = sCachedSurface;
        if (sv == null || Build.VERSION.SDK_INT < Build.VERSION_CODES.N) return;
        if (!sv.getHolder().getSurface().isValid()) return;
        try {
            int w = sv.getWidth(), h = sv.getHeight();
            if (w <= 0 || h <= 0) return;
            final Bitmap bitmap = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888);
            final CountDownLatch latch = new CountDownLatch(1);
            final AtomicReference<Boolean> success = new AtomicReference<>(false);
            PixelCopy.request(sv, bitmap, new PixelCopy.OnPixelCopyFinishedListener() {
                @Override public void onPixelCopyFinished(int result) {
                    success.set(result == PixelCopy.SUCCESS);
                    latch.countDown();
                }
            }, getHandler());
            if (latch.await(3, TimeUnit.SECONDS) && success.get()) {
                writeJpeg(bitmap, outputPath, quality);
            }
            bitmap.recycle();
        } catch (Throwable t) {
            Log.w(TAG, "captureSync failed", t);
        }
    }

    private static synchronized Handler getHandler() {
        if (sThread == null) {
            sThread = new HandlerThread("BugpunchScreenshot");
            sThread.start();
            sHandler = new Handler(sThread.getLooper());
        }
        return sHandler;
    }

    /**
     * Tunnel-side /capture handler. Captures the Unity window via PixelCopy
     * (or a View-hierarchy fallback), optionally scales the bitmap, encodes
     * to JPEG in memory, base64-wraps it, and ships it through
     * {@link BugpunchTunnel#sendResponse(String)}. No disk hop, no
     * UnitySendMessage round-trip — the full /capture request stays native.
     *
     * <p>Camera-specific capture (<c>/capture?id=N</c>) still bounces to C#
     * because per-Unity-Camera readback needs a managed Camera reference.
     *
     * @param requestId tunnel request id to echo back in the response envelope
     * @param scale     1.0 = full screen size, 0.5 = half each axis, etc.
     * @param quality   JPEG quality 1..100
     */
    public static void captureForTunnel(final String requestId, final float scale, final int quality) {
        final Activity activity = BugpunchUnity.currentActivity();
        if (activity == null) {
            BugpunchTunnel.sendResponse(BugpunchTunnel.buildErrorResponse(requestId, 503, "no activity"));
            return;
        }
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                try {
                    final View root = activity.getWindow().getDecorView();
                    final SurfaceView sv = findSurfaceView(root);

                    if (sv != null && Build.VERSION.SDK_INT >= Build.VERSION_CODES.N
                            && sv.getWidth() > 0 && sv.getHeight() > 0
                            && sv.getHolder().getSurface().isValid()) {
                        final Bitmap bitmap = Bitmap.createBitmap(
                            sv.getWidth(), sv.getHeight(), Bitmap.Config.ARGB_8888);
                        PixelCopy.request(sv, bitmap, new PixelCopy.OnPixelCopyFinishedListener() {
                            @Override public void onPixelCopyFinished(int copyResult) {
                                if (copyResult != PixelCopy.SUCCESS) {
                                    bitmap.recycle();
                                    BugpunchTunnel.sendResponse(BugpunchTunnel.buildErrorResponse(
                                        requestId, 500, "PixelCopy failed: " + copyResult));
                                    return;
                                }
                                deliverBitmap(requestId, bitmap, scale, quality);
                            }
                        }, getHandler());
                        return;
                    }

                    // Fallback: View-hierarchy draw (Unity GPU surface will be
                    // black, but native UI overlays are captured).
                    if (root.getWidth() <= 0 || root.getHeight() <= 0) {
                        BugpunchTunnel.sendResponse(BugpunchTunnel.buildErrorResponse(requestId, 500, "zero-size view"));
                        return;
                    }
                    Bitmap bitmap = Bitmap.createBitmap(
                        root.getWidth(), root.getHeight(), Bitmap.Config.ARGB_8888);
                    Canvas c = new Canvas(bitmap);
                    root.draw(c);
                    deliverBitmap(requestId, bitmap, scale, quality);
                } catch (Throwable t) {
                    Log.w(TAG, "captureForTunnel failed", t);
                    BugpunchTunnel.sendResponse(BugpunchTunnel.buildErrorResponse(
                        requestId, 500, t.getMessage() != null ? t.getMessage() : t.getClass().getSimpleName()));
                }
            }
        });
    }

    private static void deliverBitmap(String requestId, Bitmap bitmap, float scale, int quality) {
        try {
            Bitmap toEncode = bitmap;
            if (scale > 0f && scale < 1f) {
                int targetW = Math.max(1, Math.round(bitmap.getWidth() * scale));
                int targetH = Math.max(1, Math.round(bitmap.getHeight() * scale));
                toEncode = Bitmap.createScaledBitmap(bitmap, targetW, targetH, true);
                if (toEncode != bitmap) bitmap.recycle();
            }
            int q = Math.max(1, Math.min(100, quality));
            java.io.ByteArrayOutputStream baos = new java.io.ByteArrayOutputStream();
            toEncode.compress(Bitmap.CompressFormat.JPEG, q, baos);
            toEncode.recycle();
            byte[] jpeg = baos.toByteArray();
            String base64 = Base64.encodeToString(jpeg, Base64.NO_WRAP);
            BugpunchTunnel.sendResponse(BugpunchTunnel.buildBinaryResponse(requestId, base64, "image/jpeg"));
        } catch (Throwable t) {
            Log.w(TAG, "deliverBitmap failed", t);
            BugpunchTunnel.sendResponse(BugpunchTunnel.buildErrorResponse(
                requestId, 500, t.getMessage() != null ? t.getMessage() : t.getClass().getSimpleName()));
        }
    }

    /**
     * Capture the Unity window to a JPEG at outputPath. Async.
     *
     * @param requestId      opaque token echoed back to Unity
     * @param outputPath     absolute file path to write the JPEG
     * @param quality        1..100
     * @param unityGameObject name of the GameObject to receive the result
     * @param unityMethod    method on that GameObject — called with "requestId|1|outputPath" or "requestId|0|error"
     */
    /**
     * Variant that fires a Java {@link Runnable} when the JPEG is on disk
     * (success only — failures log and skip). For the in-process report-form
     * launch path, where we can't wait for UnitySendMessage to round-trip.
     */
    public static void captureThen(String outputPath, int quality, final Runnable onWritten) {
        capture("th_" + System.nanoTime(), outputPath, quality, null, null, onWritten);
    }

    public static void capture(final String requestId, final String outputPath,
                               final int quality, final String unityGameObject,
                               final String unityMethod) {
        capture(requestId, outputPath, quality, unityGameObject, unityMethod, null);
    }

    private static void capture(final String requestId, final String outputPath,
                                final int quality, final String unityGameObject,
                                final String unityMethod, final Runnable onWritten) {
        final Activity activity = BugpunchUnity.currentActivity();
        if (activity == null) {
            deliver(unityGameObject, unityMethod, requestId, false, "no activity");
            return;
        }
        if (sInFlight.putIfAbsent(requestId, Boolean.TRUE) != null) {
            deliver(unityGameObject, unityMethod, requestId, false, "duplicate requestId");
            return;
        }

        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                try {
                    View root = activity.getWindow().getDecorView();
                    SurfaceView sv = findSurfaceView(root);

                    if (sv != null && Build.VERSION.SDK_INT >= Build.VERSION_CODES.N
                            && sv.getWidth() > 0 && sv.getHeight() > 0
                            && sv.getHolder().getSurface().isValid()) {
                        final Bitmap bitmap = Bitmap.createBitmap(
                            sv.getWidth(), sv.getHeight(), Bitmap.Config.ARGB_8888);
                        PixelCopy.request(sv, bitmap, new PixelCopy.OnPixelCopyFinishedListener() {
                            @Override public void onPixelCopyFinished(int copyResult) {
                                sInFlight.remove(requestId);
                                if (copyResult == PixelCopy.SUCCESS) {
                                    String err = writeJpeg(bitmap, outputPath, quality);
                                    bitmap.recycle();
                                    if (err == null) {
                                        deliver(unityGameObject, unityMethod, requestId, true, outputPath);
                                        if (onWritten != null) try { onWritten.run(); }
                                            catch (Throwable t) { Log.w(TAG, "onWritten failed", t); }
                                    } else {
                                        deliver(unityGameObject, unityMethod, requestId, false, err);
                                    }
                                } else {
                                    bitmap.recycle();
                                    deliver(unityGameObject, unityMethod, requestId, false,
                                        "PixelCopy failed: " + copyResult);
                                }
                            }
                        }, getHandler());
                    } else {
                        // Fallback: draw the View hierarchy. Won't include the Unity GPU surface
                        // (that'll be black), but still captures native UI overlays.
                        if (root.getWidth() <= 0 || root.getHeight() <= 0) {
                            sInFlight.remove(requestId);
                            deliver(unityGameObject, unityMethod, requestId, false, "zero-size view");
                            return;
                        }
                        Bitmap bitmap = Bitmap.createBitmap(
                            root.getWidth(), root.getHeight(), Bitmap.Config.ARGB_8888);
                        Canvas c = new Canvas(bitmap);
                        root.draw(c);
                        String err = writeJpeg(bitmap, outputPath, quality);
                        bitmap.recycle();
                        sInFlight.remove(requestId);
                        if (err == null) {
                            deliver(unityGameObject, unityMethod, requestId, true, outputPath);
                            if (onWritten != null) try { onWritten.run(); }
                                catch (Throwable t) { Log.w(TAG, "onWritten failed", t); }
                        } else {
                            deliver(unityGameObject, unityMethod, requestId, false, err);
                        }
                    }
                } catch (Throwable t) {
                    sInFlight.remove(requestId);
                    Log.w(TAG, "capture failed", t);
                    deliver(unityGameObject, unityMethod, requestId, false, t.getMessage());
                }
            }
        });
    }

    private static SurfaceView findSurfaceView(View v) {
        if (v instanceof SurfaceView) return (SurfaceView) v;
        if (v instanceof ViewGroup) {
            ViewGroup g = (ViewGroup) v;
            for (int i = 0; i < g.getChildCount(); i++) {
                SurfaceView sv = findSurfaceView(g.getChildAt(i));
                if (sv != null) return sv;
            }
        }
        return null;
    }

    private static String writeJpeg(Bitmap bitmap, String path, int quality) {
        FileOutputStream out = null;
        try {
            File f = new File(path);
            File parent = f.getParentFile();
            if (parent != null && !parent.exists()) parent.mkdirs();
            out = new FileOutputStream(f);
            bitmap.compress(Bitmap.CompressFormat.JPEG,
                Math.max(1, Math.min(100, quality)), out);
            out.flush();
            return null;
        } catch (Throwable t) {
            Log.w(TAG, "writeJpeg failed", t);
            return t.getMessage();
        } finally {
            if (out != null) try { out.close(); } catch (Throwable ignored) {}
        }
    }

    private static void deliver(String go, String method, String requestId,
                                boolean ok, String payload) {
        if (go == null || method == null) return;
        String msg = requestId + "|" + (ok ? "1" : "0") + "|" + (payload == null ? "" : payload);
        BugpunchUnity.sendMessage(go, method, msg);
    }
}
