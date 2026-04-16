package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.os.Build;
import android.os.Handler;
import android.os.HandlerThread;
import android.util.Log;
import android.view.PixelCopy;
import android.view.SurfaceView;
import android.view.View;
import android.view.ViewGroup;

import java.io.File;
import java.io.FileOutputStream;
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
    private static final String TAG = "BugpunchScreenshot";

    private static HandlerThread sThread;
    private static Handler sHandler;
    private static final ConcurrentHashMap<String, Boolean> sInFlight = new ConcurrentHashMap<>();
    // Cached for ANR screenshots — finding the SurfaceView requires the UI
    // thread, which is stuck during an ANR.
    private static volatile SurfaceView sCachedSurface;

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
                            && sv.getWidth() > 0 && sv.getHeight() > 0) {
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
