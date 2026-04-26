package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.util.Log;

/**
 * Cache-driven launch-time consent auto-prompt.
 *
 * <p>Decoupled from {@link BugpunchDebugMode} (which owns the recording
 * lifecycle) so the launch-time speculative prompt can evolve
 * independently. The shared static flags it touches
 * ({@code sAutoPromptShown} and {@code sStartedFromCachedPrompt}) live on
 * {@link BugpunchDebugMode} because the recording lifecycle and the role
 * reconciliation in {@link BugpunchTesterRoleManager} also read them; we
 * route through package-private accessors there.
 */
final class BugpunchDebugAutoPrompt {
    private static final String TAG = "[Bugpunch.DebugMode]";

    private BugpunchDebugAutoPrompt() {}

    /**
     * Cache-driven launch flow. If the last-known tester role was
     * "internal", show the consent sheet immediately so the video ring
     * buffer can come up before the tunnel handshake returns. Other cached
     * values (and an empty cache) wait for the server.
     *
     * <p>Called once from {@link BugpunchRuntime#attachActivity(Activity)}
     * after Activity-bound init completes.
     */
    public static void maybeShowOnLaunch(final Activity activity) {
        if (activity == null) return;
        if (BugpunchDebugMode.isAutoPromptShown()) return;
        if (!BugpunchRuntime.isStarted()) return;
        if (BugpunchRecorder.getInstance().isRunning()) return;

        String cached = BugpunchTunnel.readLastTesterRoleFromCache(
            activity.getApplicationContext());
        if (!"internal".equals(cached)) {
            // First-ever launch (null) or last-known non-internal: no
            // speculative prompt. Wait for the server's roleConfig.
            return;
        }
        BugpunchDebugMode.setAutoPromptShown(true);
        Log.i(TAG, "cached role=internal — auto-prompting consent on launch");
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                BugpunchDebugMode.setStartedFromCachedPrompt(true);
                BugpunchDebugMode.showConsentDialog(activity);
            }
        });
    }
}
