package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.util.Log;

/**
 * Reconciles the speculative cache-driven debug-mode state with the
 * server's authoritative {@code roleConfig}.
 *
 * <p>Called from {@link BugpunchTunnel#applyRoleConfigInternal} on the two
 * cross-boundary transitions:
 * <ul>
 *   <li>{@link #onRoleBecameInternal()} — server flipped us TO "internal"
 *       and the launch-time {@link BugpunchDebugAutoPrompt} hasn't already
 *       prompted. Show the consent sheet now.</li>
 *   <li>{@link #onRoleLeftInternal()} — server flipped us AWAY from
 *       "internal". If a recording is running because of a speculative
 *       cached prompt (not a manual {@code Bugpunch.EnterDebugMode} call),
 *       tear it down — server wins.</li>
 * </ul>
 *
 * <p>The two shared static flags ({@code sAutoPromptShown} and
 * {@code sStartedFromCachedPrompt}) live on {@link BugpunchDebugMode}
 * because the recording-lifecycle path needs them too; we route through
 * its package-private accessors.
 */
final class BugpunchTesterRoleManager {
    private static final String TAG = "[Bugpunch.DebugMode]";

    private BugpunchTesterRoleManager() {}

    /**
     * Called when the server's roleConfig flips us TO "internal". If the
     * cached path didn't already prompt (cache was missing or
     * non-internal), prompt now.
     */
    static void onRoleBecameInternal() {
        if (BugpunchDebugMode.isAutoPromptShown()) return;
        Activity activity = BugpunchRuntime.getAttachedActivity();
        if (activity == null) return;
        if (!BugpunchRuntime.isStarted()) return;
        if (BugpunchRecorder.getInstance().isRunning()) return;
        BugpunchDebugMode.setAutoPromptShown(true);
        Log.i(TAG, "role flipped to internal — prompting consent mid-session");
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                BugpunchDebugMode.setStartedFromCachedPrompt(true);
                BugpunchDebugMode.showConsentDialog(activity);
            }
        });
    }

    /**
     * Called when the server's roleConfig flips us AWAY from "internal".
     * If a recording is running because of the speculative cached prompt,
     * tear it down — the server's authoritative answer wins. Manual
     * {@code Bugpunch.EnterDebugMode} recordings are left alone.
     */
    static void onRoleLeftInternal() {
        if (!BugpunchDebugMode.isStartedFromCachedPrompt()) return;
        final Activity activity = BugpunchRuntime.getAttachedActivity();
        if (activity == null) return;
        Log.i(TAG, "role left internal — tearing down speculative recording");
        BugpunchDebugMode.setStartedFromCachedPrompt(false);
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                try { BugpunchRecorder.getInstance().stop(); } catch (Throwable ignore) {}
                try { BugpunchTouchRecorder.stop(); } catch (Throwable ignore) {}
                try { BugpunchDebugWidget.hide(); } catch (Throwable ignore) {}
            }
        });
    }
}
