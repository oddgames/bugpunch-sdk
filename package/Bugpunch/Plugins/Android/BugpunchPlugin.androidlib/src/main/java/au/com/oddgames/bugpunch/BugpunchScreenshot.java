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
    public static void capture(final String requestId, final String outputPath,
                               final int quality, final String unityGameObject,
                               final String unityMethod) {
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
