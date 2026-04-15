package au.com.oddgames.bugpunch;

import android.content.Context;
import android.hardware.Sensor;
import android.hardware.SensorEvent;
import android.hardware.SensorEventListener;
import android.hardware.SensorManager;
import android.util.Log;

/**
 * Accelerometer-based shake detector. When magnitude-gravity exceeds the
 * configured threshold two times within 500ms, fires the onShake callback
 * (with a 2-second cooldown to prevent repeats).
 */
public class BugpunchShakeDetector {
    private static final String TAG = "BugpunchShake";

    private static SensorManager sManager;
    private static Sensor sSensor;
    private static SensorEventListener sListener;
    private static Runnable sCallback;

    public static synchronized void start(Context ctx, final float threshold, Runnable onShake) {
        if (sManager != null) return;
        sManager = (SensorManager) ctx.getSystemService(Context.SENSOR_SERVICE);
        if (sManager == null) return;
        sSensor = sManager.getDefaultSensor(Sensor.TYPE_ACCELEROMETER);
        if (sSensor == null) { Log.w(TAG, "no accelerometer"); return; }
        sCallback = onShake;

        sListener = new SensorEventListener() {
            long lastShakeMs = 0;
            long lastSpikeMs = 0;
            int spikeCount = 0;

            @Override public void onSensorChanged(SensorEvent e) {
                float x = e.values[0], y = e.values[1], z = e.values[2];
                double mag = Math.sqrt(x * x + y * y + z * z) - SensorManager.GRAVITY_EARTH;
                if (mag < threshold) return;
                long now = System.currentTimeMillis();
                if (now - lastSpikeMs > 500) spikeCount = 0;
                spikeCount++;
                lastSpikeMs = now;
                if (spikeCount >= 2 && now - lastShakeMs > 2000) {
                    lastShakeMs = now;
                    spikeCount = 0;
                    try { if (sCallback != null) sCallback.run(); }
                    catch (Throwable t) { Log.w(TAG, "shake callback failed", t); }
                }
            }
            @Override public void onAccuracyChanged(Sensor s, int a) { }
        };
        sManager.registerListener(sListener, sSensor, SensorManager.SENSOR_DELAY_UI);
    }

    public static synchronized void stop() {
        if (sManager != null && sListener != null) sManager.unregisterListener(sListener);
        sManager = null; sSensor = null; sListener = null; sCallback = null;
    }
}
