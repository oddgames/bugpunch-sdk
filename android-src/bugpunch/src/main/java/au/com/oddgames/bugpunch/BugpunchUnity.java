package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.util.Log;

import java.lang.reflect.Field;
import java.lang.reflect.Method;

/**
 * Reflection-based access to UnityPlayer. The androidlib doesn't have
 * unity-classes.jar on its compile classpath (following the same pattern as
 * {@link BugpunchReportOverlay}), so we look up {@code UnityPlayer.currentActivity}
 * and {@code UnityPlayer.UnitySendMessage(...)} via reflection at runtime.
 */
final class BugpunchUnity {
    private static final String TAG = "[Bugpunch.Unity]";
    private BugpunchUnity() {}

    /** The Unity player activity, or null if not available. */
    static Activity currentActivity() {
        try {
            Class<?> cls = Class.forName("com.unity3d.player.UnityPlayer");
            Field f = cls.getField("currentActivity");
            Object a = f.get(null);
            return a instanceof Activity ? (Activity) a : null;
        } catch (Throwable t) {
            Log.w(TAG, "currentActivity reflect failed", t);
            return null;
        }
    }

    /** Safe UnitySendMessage — no-op if UnityPlayer isn't present. */
    static void sendMessage(String gameObject, String method, String message) {
        if (gameObject == null || method == null) return;
        try {
            Class<?> cls = Class.forName("com.unity3d.player.UnityPlayer");
            Method m = cls.getMethod("UnitySendMessage", String.class, String.class, String.class);
            m.invoke(null, gameObject, method, message == null ? "" : message);
        } catch (Throwable t) {
            Log.w(TAG, "UnitySendMessage reflect failed", t);
        }
    }
}
