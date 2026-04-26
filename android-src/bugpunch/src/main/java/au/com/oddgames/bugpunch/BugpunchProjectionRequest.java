package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.media.projection.MediaProjectionConfig;
import android.media.projection.MediaProjectionManager;
import android.os.Build;
import android.os.Bundle;
import android.util.Log;

/**
 * Transparent activity that requests MediaProjection consent and hands the
 * result back to either:
 *   1) Unity (legacy bug-report flow) via UnitySendMessage, or
 *   2) A native Java callback (issue #30 — chat video attachments).
 *
 * Two entry points share the same Activity + same {@code onActivityResult}
 * handler:
 *
 *   • {@link #request(Activity, int, int, int, int, int, int, String, String)}
 *     — legacy. Stashes config + Unity callback names. On result, kicks off
 *     {@link BugpunchProjectionService} so the ring-buffer recorder starts
 *     while a foreground service holds the projection token (Android 14+
 *     requirement). Falls back to buffer-mode if consent is denied so bug
 *     reports still capture the game surface.
 *
 *   • {@link #requestForJavaCallback(Activity, JavaProjectionCallback)} —
 *     for the native chat video flow. We don't want the buffer-mode fallback
 *     (no game surface to feed it from outside Unity), and we don't want to
 *     bounce through UnitySendMessage. Just hand the consent result to the
 *     supplied callback so {@link BugpunchChatActivity} can drive the rest
 *     (start the recorder writing a single segment to a chosen path, show
 *     the Stop pill, etc.). Mutually exclusive with the legacy path: only
 *     one consent request is in flight at a time.
 */
public class BugpunchProjectionRequest extends Activity {
    private static final String TAG = "[Bugpunch.ProjectionRequest]";
    private static final int REQ_CODE = 0xB06C; // "bugc"

    /** Callback shape for the native chat path (#30). Invoked on the main
     *  thread once the system consent dialog completes. */
    public interface JavaProjectionCallback {
        void onApproved(int resultCode, android.content.Intent resultData);
        void onDenied();
    }

    /** Callback from Unity — use UnitySendMessage to deliver results. */
    private static String sCallbackObject;
    private static String sCallbackMethod;

    /** Native Java callback — set when {@link #requestForJavaCallback} is used.
     *  Mutually exclusive with the Unity-callback fields above; whichever
     *  was set last wins. Cleared in {@link #onActivityResult} after dispatch
     *  so the next request starts clean. */
    private static JavaProjectionCallback sJavaCallback;

    // Config stashed between request() and the activity onCreate
    private static int sWidth, sHeight, sBitrate, sFps, sWindowSeconds, sDpi;

    /**
     * Called from Unity. Stashes config + callback info, then launches this
     * activity on top of the Unity player.
     */
    public static void request(Activity unityActivity,
                               int width, int height, int bitrate, int fps, int windowSeconds, int dpi,
                               String callbackGameObject, String callbackMethod) {
        sCallbackObject = callbackGameObject;
        sCallbackMethod = callbackMethod;
        sJavaCallback = null;
        sWidth = width; sHeight = height; sBitrate = bitrate; sFps = fps;
        sWindowSeconds = windowSeconds; sDpi = dpi;

        Intent i = new Intent(unityActivity, BugpunchProjectionRequest.class);
        i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        unityActivity.startActivity(i);
    }

    /**
     * Native chat entry point (#30). The caller doesn't need projection
     * config (width/height/bitrate/fps/windowSeconds/dpi) — those are only
     * relevant for the legacy ring-buffer path which kicks off
     * {@link BugpunchProjectionService} on success. The chat flow takes the
     * raw resultCode + Intent and starts the recorder itself, so we just
     * forward the consent verdict to the callback.
     */
    public static void requestForJavaCallback(Activity host, JavaProjectionCallback cb) {
        sCallbackObject = null;
        sCallbackMethod = null;
        sJavaCallback = cb;
        // Config defaults — unused on the chat path but cleared so any stale
        // values from a previous Unity request don't leak into a future one.
        sWidth = 0; sHeight = 0; sBitrate = 0; sFps = 0;
        sWindowSeconds = 0; sDpi = 0;

        Intent i = new Intent(host, BugpunchProjectionRequest.class);
        i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        host.startActivity(i);
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        try {
            MediaProjectionManager mgr = (MediaProjectionManager)
                getSystemService(Context.MEDIA_PROJECTION_SERVICE);

            // Android 14+ (API 34): createScreenCaptureIntent() defaults to the
            // "Entire screen / Single app" picker. We capture the default display
            // directly to skip the picker — Android offers no API to force
            // "this single app only", so the cleanest UX is to grab the whole
            // display (bug video is cropped to the app on screen anyway).
            Intent consentIntent;
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
                consentIntent = mgr.createScreenCaptureIntent(
                    MediaProjectionConfig.createConfigForDefaultDisplay());
            } else {
                consentIntent = mgr.createScreenCaptureIntent();
            }
            startActivityForResult(consentIntent, REQ_CODE);
        } catch (Exception e) {
            Log.e(TAG, "failed to launch consent", e);
            dispatchDenied();
            finish();
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQ_CODE) { finish(); return; }

        // ── Native Java callback path (#30 — chat video) ──────────────
        // Kept entirely separate from the ring-buffer fallback so the chat
        // path doesn't inherit it. No game surface to feed buffer-mode here.
        if (sJavaCallback != null) {
            JavaProjectionCallback cb = sJavaCallback;
            sJavaCallback = null;
            try {
                if (resultCode == Activity.RESULT_OK && data != null) {
                    cb.onApproved(resultCode, data);
                } else {
                    cb.onDenied();
                }
            } catch (Throwable t) {
                Log.e(TAG, "java callback dispatch failed", t);
            }
            finish();
            return;
        }

        // ── Legacy Unity-callback path (bug-report ring buffer) ───────
        if (resultCode != Activity.RESULT_OK || data == null) {
            Log.w(TAG, "user denied MediaProjection consent — falling back to Unity-surface buffer mode");
            // Fall back to Unity-surface capture — game-only frames, no system
            // UI, but at least bug reports still include video. We intentionally
            // don't remember this denial: each EnterDebugMode call re-prompts
            // so the user can grant full projection later if they change mind.
            try {
                BugpunchRecorder.getInstance().configure(sWidth, sHeight, sBitrate, sFps, sWindowSeconds);
                BugpunchRecorder.getInstance().startBufferMode(getApplicationContext());
            } catch (Exception e) {
                Log.e(TAG, "buffer-mode fallback start failed", e);
            }
            // Reported as false to Unity since projection itself was denied;
            // Unity inspects BugpunchRecorder.isBufferMode() separately.
            sendResultToUnity(false);
            finish();
            return;
        }

        try {
            // Android 14+ requires a foreground service of type mediaProjection
            // to be running BEFORE getMediaProjection() is called. Launch the
            // service with the consent token and let it start the recorder.
            Intent svc = new Intent(this, BugpunchProjectionService.class)
                .putExtra(BugpunchProjectionService.EXTRA_RESULT_CODE, resultCode)
                .putExtra(BugpunchProjectionService.EXTRA_RESULT_DATA, data)
                .putExtra(BugpunchProjectionService.EXTRA_WIDTH, sWidth)
                .putExtra(BugpunchProjectionService.EXTRA_HEIGHT, sHeight)
                .putExtra(BugpunchProjectionService.EXTRA_BITRATE, sBitrate)
                .putExtra(BugpunchProjectionService.EXTRA_FPS, sFps)
                .putExtra(BugpunchProjectionService.EXTRA_WINDOW_SECONDS, sWindowSeconds)
                .putExtra(BugpunchProjectionService.EXTRA_DPI, sDpi);

            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                startForegroundService(svc);
            } else {
                startService(svc);
            }
            // We report success now; the recorder's actual MediaCodec setup happens
            // inside the service. If it fails, the service will stop itself and
            // BugpunchRecorder.isRunning() will return false — Unity can check.
            sendResultToUnity(true);
        } catch (Exception e) {
            Log.e(TAG, "failed to start projection service", e);
            sendResultToUnity(false);
        }
        finish();
    }

    /** Best-effort denial dispatch when the consent dialog itself fails to
     *  even launch. Routes to whichever callback is configured. */
    private void dispatchDenied() {
        if (sJavaCallback != null) {
            JavaProjectionCallback cb = sJavaCallback;
            sJavaCallback = null;
            try { cb.onDenied(); } catch (Throwable ignored) {}
            return;
        }
        sendResultToUnity(false);
    }

    private void sendResultToUnity(boolean success) {
        if (sCallbackObject == null || sCallbackMethod == null) return;
        try {
            Class<?> playerClass = Class.forName("com.unity3d.player.UnityPlayer");
            playerClass.getMethod("UnitySendMessage", String.class, String.class, String.class)
                .invoke(null, sCallbackObject, sCallbackMethod, success ? "1" : "0");
        } catch (Exception e) {
            Log.e(TAG, "UnitySendMessage failed", e);
        }
    }
}
