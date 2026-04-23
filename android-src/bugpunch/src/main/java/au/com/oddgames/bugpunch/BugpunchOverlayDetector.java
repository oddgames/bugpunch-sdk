package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.app.Application;
import android.os.Bundle;
import android.util.Log;

/**
 * Detects in-process overlay Activities (ad SDKs, IAP billing proxy, Firebase
 * Auth UI, Sign-in flows, …) and records their FQCN as breadcrumbs so crash
 * triage can see what was on top at crash time.
 *
 * Only fires for Activities in our own process — remote overlays (direct
 * Play Store launches, notification shade) are invisible without extra
 * permissions and aren't covered.
 *
 * Wiring: {@link BugpunchRuntime#start} calls {@link #start} once
 * at init. We register an {@link Application.ActivityLifecycleCallbacks}
 * against the process-wide Application and watch for a non-game Activity
 * resuming while the game is still considered foreground.
 */
public class BugpunchOverlayDetector {
    private static final String TAG = "[Bugpunch.OverlayDetector]";

    private static volatile boolean sStarted;
    private static String sGameActivityClass;
    private static String sCurrentOverlay;

    public static synchronized void start(Activity gameActivity) {
        if (sStarted) return;
        Application app = gameActivity.getApplication();
        if (app == null) { Log.w(TAG, "no application; overlay detection disabled"); return; }

        sGameActivityClass = gameActivity.getClass().getName();

        app.registerActivityLifecycleCallbacks(new Application.ActivityLifecycleCallbacks() {
            @Override public void onActivityCreated(Activity a, Bundle b) {}
            @Override public void onActivityStarted(Activity a) {}
            @Override public void onActivityStopped(Activity a) {}
            @Override public void onActivityDestroyed(Activity a) {}
            @Override public void onActivitySaveInstanceState(Activity a, Bundle b) {}

            @Override public void onActivityResumed(Activity a) {
                String cls = a.getClass().getName();
                if (cls.equals(sGameActivityClass)) {
                    // Game is back on top — whatever overlay was showing is gone.
                    if (sCurrentOverlay != null) {
                        pushCrumb("overlay_dismissed", sCurrentOverlay);
                        sCurrentOverlay = null;
                    }
                    return;
                }
                // Non-game Activity resumed in our process = overlay.
                sCurrentOverlay = cls;
                pushCrumb("overlay_appeared", cls);
            }

            @Override public void onActivityPaused(Activity a) {}
        });

        sStarted = true;
        Log.i(TAG, "overlay detector started (game=" + sGameActivityClass + ")");
    }

    private static void pushCrumb(String category, String message) {
        try {
            BugpunchInput.pushCustom(System.currentTimeMillis(), category, message, "");
        } catch (Throwable t) {
            // Never crash the game for a breadcrumb.
        }
    }
}
