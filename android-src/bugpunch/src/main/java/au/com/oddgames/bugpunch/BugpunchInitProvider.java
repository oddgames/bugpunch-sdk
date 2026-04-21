package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.app.Application;
import android.content.ContentProvider;
import android.content.ContentValues;
import android.content.Context;
import android.content.res.AssetManager;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.util.Log;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;

/**
 * Early-boot hook for the Bugpunch SDK — runs before Application.onCreate()
 * returns because Android initialises manifest-declared ContentProviders as
 * part of Application startup, ahead of any dev-defined code (same pattern
 * Firebase / Sentry / Crashlytics use).
 *
 * <p>On create we:
 * <ol>
 *   <li>Read <c>bugpunch_config.json</c> from the APK's assets directory.</li>
 *   <li>Call {@link BugpunchRuntime#startEarly(Context, String)} to install
 *       the NDK signal handlers, set crash paths, start the log ring, and
 *       drain any pending uploads from a previous launch.</li>
 *   <li>Register an {@link Application.ActivityLifecycleCallbacks} so that
 *       when Unity's activity is created, we call
 *       {@link BugpunchRuntime#attachActivity(Activity)} to wire up the
 *       Activity-bound pieces (overlay detector, surface view cache,
 *       Choreographer frame tick).</li>
 * </ol>
 *
 * <p>If the config file is missing (host app didn't include the Bugpunch
 * editor post-processor or skipped this build step), the provider quietly
 * does nothing — the legacy C# path still drives init via
 * {@link BugpunchRuntime#start(Activity, String)} after Unity boots.
 */
public class BugpunchInitProvider extends ContentProvider {
    private static final String TAG = "BugpunchInit";
    private static final String CONFIG_ASSET_NAME = "bugpunch_config.json";

    @Override
    public boolean onCreate() {
        Context ctx = getContext();
        if (ctx == null) {
            Log.w(TAG, "no context");
            return false;
        }
        Context appCtx = ctx.getApplicationContext();

        String configJson = readConfigJson(appCtx);
        if (configJson == null) {
            // No bundled config — fall back to legacy C# start path. Not an
            // error: games built without the post-processor still work.
            return true;
        }

        try {
            BugpunchRuntime.startEarly(appCtx, configJson);
        } catch (Throwable t) {
            Log.e(TAG, "early init failed", t);
            return true; // Don't break app start even if we fail
        }

        if (appCtx instanceof Application) {
            ((Application) appCtx).registerActivityLifecycleCallbacks(new LifecycleBridge());
        }

        return true;
    }

    private static String readConfigJson(Context ctx) {
        try {
            AssetManager am = ctx.getAssets();
            try (InputStream is = am.open(CONFIG_ASSET_NAME)) {
                ByteArrayOutputStream bos = new ByteArrayOutputStream(1024);
                byte[] buf = new byte[2048];
                int n;
                while ((n = is.read(buf)) > 0) bos.write(buf, 0, n);
                return bos.toString("UTF-8");
            }
        } catch (Exception e) {
            // Asset not present — this is the expected path when the host app
            // doesn't ship the bundled config. Log at debug level only.
            Log.d(TAG, "no " + CONFIG_ASSET_NAME + " in assets: " + e.getMessage());
            return null;
        }
    }

    /**
     * Captures the first non-Bugpunch Activity created by the host and hands
     * it off to the runtime so Activity-bound init can complete. The Bugpunch
     * SDK's own activities (crash overlay, report form, tools, projection
     * request) are skipped — we want Unity's player activity.
     */
    private static class LifecycleBridge implements Application.ActivityLifecycleCallbacks {
        private boolean handed;

        @Override
        public void onActivityCreated(Activity activity, Bundle savedInstanceState) {
            if (handed || activity == null) return;
            String cls = activity.getClass().getName();
            if (cls.startsWith("au.com.oddgames.bugpunch.")) return;
            handed = true;
            try {
                BugpunchRuntime.attachActivity(activity);
            } catch (Throwable t) {
                Log.w(TAG, "attachActivity failed", t);
            }
        }

        @Override public void onActivityStarted(Activity activity) {}
        @Override public void onActivityResumed(Activity activity) {}
        @Override public void onActivityPaused(Activity activity) {}
        @Override public void onActivityStopped(Activity activity) {}
        @Override public void onActivitySaveInstanceState(Activity activity, Bundle outState) {}
        @Override public void onActivityDestroyed(Activity activity) {}
    }

    // ── ContentProvider stubs — we never serve content, just use onCreate() as an init hook ──

    @Override public Cursor query(Uri u, String[] p, String s, String[] a, String o) { return null; }
    @Override public String getType(Uri u) { return null; }
    @Override public Uri insert(Uri u, ContentValues v) { return null; }
    @Override public int delete(Uri u, String s, String[] a) { return 0; }
    @Override public int update(Uri u, ContentValues v, String s, String[] a) { return 0; }
}
