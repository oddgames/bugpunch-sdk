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
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

/**
 * On-demand screenshot capture via PixelCopy. Reads the Unity SurfaceView's
 * GPU buffer without any user permission prompt (we're reading our own window).
 *
 * <p>Three live entry points:
 * <ul>
 *   <li>{@link #captureForTunnel} — Remote IDE / tunnel /capture handler;
 *       sync result over the WebSocket.</li>
 *   <li>{@link #capture} — generic capture-to-disk for the bug-report form
 *       and the manual report path.</li>
 *   <li>{@link #captureSync} — blocking capture used by the ANR watchdog,
 *       which runs on a background thread (the main thread is already stuck).</li>
 * </ul>
 *
 * The 1 Hz rolling buffer that used to live here is gone — every crash that
 * follows a UI press picks up its rescue frame from the {@link
 * BugpunchStoryboard} ring instead. {@link #cacheSurfaceView} still runs at
 * boot so the ANR path can locate the SurfaceView from a background thread.
 */
public class BugpunchScreenshot {
    private static final String TAG = "[Bugpunch.Screenshot]";

    private static HandlerThread sThread;
    private static Handler sHandler;
    private static final ConcurrentHashMap<String, Boolean> sInFlight = new ConcurrentHashMap<>();
    /** Cached at boot — finding the SurfaceView requires the UI thread, which
     *  is stuck during an ANR. */
    private static volatile SurfaceView sCachedSurface;

    /** Cache the Unity SurfaceView for later use by {@link #captureSync} (ANR
     *  path). Idempotent — calling more than once just refreshes the reference. */
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
     * Blocking screenshot for the ANR path. Uses the cached SurfaceView +
     * PixelCopy from a background thread. Times out after 3 seconds. Does
     * NOT touch the main thread (which is stuck during an ANR).
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
     * Variant that fires a Java {@link Runnable} when the JPEG is on disk
     * (success only — failures log and skip). For the in-process report-form
     * launch path, where we can't wait for UnitySendMessage to round-trip.
     */
    public static void captureThen(String outputPath, int quality, final Runnable onWritten) {
        capture("th_" + System.nanoTime(), outputPath, quality, null, null, onWritten, null);
    }

    /**
     * Java callback for captureWithResult — fired once per call, on either
     * success or failure. {@code success} is true when {@code path} points
     * at a written JPEG; on failure, {@code reason} carries a short
     * human-readable description and {@code path} is null.
     */
    public interface OnCaptureFinished {
        void onResult(boolean success, String path, String reason);
    }

    /**
     * Native-only callback variant. Used by the chat module to fulfil
     * QA-issued screenshot requests without bouncing through Unity. Always
     * fires the callback exactly once (success path or any failure mode).
     */
    public static void captureWithResult(String outputPath, int quality,
                                         final OnCaptureFinished cb) {
        if (cb == null) {
            captureThen(outputPath, quality, null);
            return;
        }
        capture("cb_" + System.nanoTime(), outputPath, quality, null, null, null, cb);
    }

    public static void capture(final String requestId, final String outputPath,
                               final int quality, final String unityGameObject,
                               final String unityMethod) {
        capture(requestId, outputPath, quality, unityGameObject, unityMethod, null, null);
    }

    private static void capture(final String requestId, final String outputPath,
                                final int quality, final String unityGameObject,
                                final String unityMethod, final Runnable onWritten,
                                final OnCaptureFinished onResult) {
        final Activity activity = BugpunchUnity.currentActivity();
        if (activity == null) {
            deliver(unityGameObject, unityMethod, requestId, false, "no activity");
            fireResult(onResult, false, null, "no activity");
            return;
        }
        if (sInFlight.putIfAbsent(requestId, Boolean.TRUE) != null) {
            deliver(unityGameObject, unityMethod, requestId, false, "duplicate requestId");
            fireResult(onResult, false, null, "duplicate requestId");
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
                                        fireResult(onResult, true, outputPath, null);
                                    } else {
                                        deliver(unityGameObject, unityMethod, requestId, false, err);
                                        fireResult(onResult, false, null, err);
                                    }
                                } else {
                                    bitmap.recycle();
                                    String msg = "PixelCopy failed: " + copyResult;
                                    deliver(unityGameObject, unityMethod, requestId, false, msg);
                                    fireResult(onResult, false, null, msg);
                                }
                            }
                        }, getHandler());
                    } else {
                        // SurfaceView not ready — fall back to View-hierarchy draw
                        // (won't include the Unity GPU surface but preserves native
                        // UI overlays).
                        sInFlight.remove(requestId);
                        if (root.getWidth() <= 0 || root.getHeight() <= 0) {
                            deliver(unityGameObject, unityMethod, requestId, false, "zero-size view");
                            fireResult(onResult, false, null, "zero-size view");
                            return;
                        }
                        Bitmap bitmap = Bitmap.createBitmap(
                            root.getWidth(), root.getHeight(), Bitmap.Config.ARGB_8888);
                        Canvas c = new Canvas(bitmap);
                        root.draw(c);
                        String err = writeJpeg(bitmap, outputPath, quality);
                        bitmap.recycle();
                        if (err == null) {
                            deliver(unityGameObject, unityMethod, requestId, true, outputPath);
                            if (onWritten != null) try { onWritten.run(); }
                                catch (Throwable t) { Log.w(TAG, "onWritten failed", t); }
                            fireResult(onResult, true, outputPath, null);
                        } else {
                            deliver(unityGameObject, unityMethod, requestId, false, err);
                            fireResult(onResult, false, null, err);
                        }
                    }
                } catch (Throwable t) {
                    sInFlight.remove(requestId);
                    Log.w(TAG, "capture failed", t);
                    String msg = t.getMessage() != null ? t.getMessage() : t.getClass().getSimpleName();
                    deliver(unityGameObject, unityMethod, requestId, false, msg);
                    fireResult(onResult, false, null, msg);
                }
            }
        });
    }

    private static void fireResult(OnCaptureFinished cb, boolean success, String path, String reason) {
        if (cb == null) return;
        try { cb.onResult(success, path, reason); }
        catch (Throwable t) { Log.w(TAG, "onResult callback failed", t); }
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
