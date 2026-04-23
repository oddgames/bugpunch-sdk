package au.com.oddgames.bugpunch;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.os.Build;
import android.os.IBinder;
import android.util.Log;

/**
 * Foreground service that holds the MediaProjection token while the recorder is running.
 *
 * Android 10+ requires a foreground service to use MediaProjection, and Android 14+
 * requires the service type to be {@code mediaProjection} and the service to already
 * be started in the foreground BEFORE {@code getMediaProjection()} is called.
 *
 * Lifecycle:
 * 1. {@link BugpunchProjectionRequest} gets consent, then starts this service with
 *    the resultCode + data Intent as extras.
 * 2. {@link #onStartCommand} calls {@link #startForeground} immediately, then hands
 *    the extras to {@link BugpunchRecorder#start} which calls getMediaProjection().
 * 3. When the recorder is stopped, {@link #stopService} is called which tears down
 *    the MediaProjection and removes the notification.
 */
public class BugpunchProjectionService extends Service {
    private static final String TAG = "[Bugpunch.ProjectionService]";
    private static final String CHANNEL_ID = "bugpunch_recorder";
    private static final int NOTIFICATION_ID = 0xB06C;

    public static final String EXTRA_RESULT_CODE = "resultCode";
    public static final String EXTRA_RESULT_DATA = "resultData";
    public static final String EXTRA_WIDTH = "width";
    public static final String EXTRA_HEIGHT = "height";
    public static final String EXTRA_BITRATE = "bitrate";
    public static final String EXTRA_FPS = "fps";
    public static final String EXTRA_WINDOW_SECONDS = "windowSeconds";
    public static final String EXTRA_DPI = "dpi";

    /** Started flag so Unity can query whether the FG service is alive. */
    private static volatile boolean sRunning;

    public static boolean isRunning() { return sRunning; }

    /** Stop the service if running. Called from BugpunchRecorder.stop(). */
    public static void stopIfRunning(Context ctx) {
        if (!sRunning) return;
        try {
            ctx.stopService(new Intent(ctx, BugpunchProjectionService.class));
        } catch (Exception e) {
            Log.w(TAG, "stopService failed", e);
        }
    }

    @Override
    public IBinder onBind(Intent intent) { return null; }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent == null) {
            stopSelf();
            return START_NOT_STICKY;
        }

        // Must go foreground BEFORE calling getMediaProjection on Android 14+.
        createChannelIfNeeded();
        Notification notification = buildNotification();
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(NOTIFICATION_ID, notification,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION);
        } else {
            startForeground(NOTIFICATION_ID, notification);
        }
        sRunning = true;

        // Now that we're a foreground service, start the recorder which will
        // call MediaProjectionManager.getMediaProjection(resultCode, data).
        int resultCode = intent.getIntExtra(EXTRA_RESULT_CODE, 0);
        Intent resultData = intent.getParcelableExtra(EXTRA_RESULT_DATA);
        int width = intent.getIntExtra(EXTRA_WIDTH, 1080);
        int height = intent.getIntExtra(EXTRA_HEIGHT, 1920);
        int bitrate = intent.getIntExtra(EXTRA_BITRATE, 2_000_000);
        int fps = intent.getIntExtra(EXTRA_FPS, 30);
        int windowSeconds = intent.getIntExtra(EXTRA_WINDOW_SECONDS, 30);
        int dpi = intent.getIntExtra(EXTRA_DPI, 320);

        BugpunchRecorder recorder = BugpunchRecorder.getInstance();
        recorder.configure(width, height, bitrate, fps, windowSeconds);
        boolean ok = recorder.startFromService(this, resultCode, resultData, dpi);
        if (!ok) {
            Log.e(TAG, "recorder.startFromService failed, stopping service");
            stopSelf();
        }
        return START_NOT_STICKY;
    }

    @Override
    public void onDestroy() {
        sRunning = false;
        try { BugpunchRecorder.getInstance().stop(); } catch (Exception ignored) {}
        super.onDestroy();
    }

    // ─── Notification plumbing ────────────────────────────────────────

    private void createChannelIfNeeded() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return;
        NotificationManager nm = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
        if (nm == null) return;
        NotificationChannel channel = new NotificationChannel(
            CHANNEL_ID, "Bug report recorder", NotificationManager.IMPORTANCE_LOW);
        channel.setDescription("Records the screen so we can attach video to crash reports");
        channel.setShowBadge(false);
        nm.createNotificationChannel(channel);
    }

    private Notification buildNotification() {
        Notification.Builder b;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            b = new Notification.Builder(this, CHANNEL_ID);
        } else {
            b = new Notification.Builder(this);
        }
        b.setContentTitle("Bug recorder active")
         .setContentText("Recording the last 30s in case something breaks")
         .setSmallIcon(android.R.drawable.ic_menu_camera)
         .setOngoing(true);
        return b.build();
    }
}
