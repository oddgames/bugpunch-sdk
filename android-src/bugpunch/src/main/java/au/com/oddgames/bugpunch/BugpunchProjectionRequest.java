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
 * result back to {@link BugpunchRecorder}. Launched from Unity via
 * {@code new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchProjectionRequest").CallStatic("request", ...)}.
 *
 * This exists because MediaProjection requires an Activity for the consent
 * dialog — we can't ask for it from a background service or JNI context.
 */
public class BugpunchProjectionRequest extends Activity {
    private static final String TAG = "[Bugpunch.ProjectionRequest]";
    private static final int REQ_CODE = 0xB06C; // "bugc"

    /** Callback from Unity — use UnitySendMessage to deliver results. */
    private static String sCallbackObject;
    private static String sCallbackMethod;

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
        sWidth = width; sHeight = height; sBitrate = bitrate; sFps = fps;
        sWindowSeconds = windowSeconds; sDpi = dpi;

        Intent i = new Intent(unityActivity, BugpunchProjectionRequest.class);
        i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        unityActivity.startActivity(i);
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
            sendResultToUnity(false);
            finish();
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQ_CODE) { finish(); return; }

        if (resultCode != Activity.RESULT_OK || data == null) {
            Log.w(TAG, "user denied MediaProjection consent");
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
