package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.app.Dialog;
import android.content.Context;
import android.graphics.Color;
import android.graphics.drawable.ColorDrawable;
import android.graphics.drawable.GradientDrawable;
import android.util.DisplayMetrics;
import android.util.Log;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

import org.json.JSONObject;

/**
 * OPT-IN video recording coordinator.
 *
 * <p>Activated via {@code Bugpunch.EnterDebugMode()} — the user sees a
 * consent dialog before MediaProjection starts recording. Has nothing to
 * do with crash / ANR / exception reporting, which runs for all users on
 * every launch via {@link BugpunchRuntime#start(Activity, String)}.
 *
 * <p>Flow:
 * <ol>
 *   <li>Game calls {@link #enter(Activity, boolean)} from a tester-only UI
 *       (e.g. a menu in a beta build).</li>
 *   <li>We show an in-app consent sheet (Start Recording / Cancel).</li>
 *   <li>On Start the MediaProjection system dialog appears; on accept the
 *       ring buffer and touch recorder start.</li>
 *   <li>On any subsequent bug / exception / crash the runtime asks us for
 *       a video dump via {@link #dumpRingIfRunning(Activity)} — cheap
 *       no-op if recording was never consented to.</li>
 * </ol>
 */
public class BugpunchDebugMode {
    private static final String TAG = "[Bugpunch.DebugMode]";

    /**
     * Enable debug recording. With {@code skipConsent=false} (default) shows
     * a native consent sheet with Start / Cancel; with {@code true} skips it
     * and goes straight to the OS-level MediaProjection consent dialog.
     * No-op if the runtime isn't started or recording is already running.
     */
    public static void enter(final Activity activity, final boolean skipConsent) {
        if (!BugpunchRuntime.isStarted() || activity == null) return;
        if (BugpunchRecorder.getInstance().hasFootage()) return;
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                if (skipConsent) startRecordingFromConfig(activity);
                else             showConsentDialog(activity);
            }
        });
    }

    /**
     * Stop all opt-in recording and tear the runtime's subsystems down.
     * Mostly here for symmetry / test cleanup — production apps never call
     * this because the runtime is meant to live for the whole process.
     */
    public static synchronized void stop() {
        BugpunchShakeDetector.stop();
        BugpunchLogReader.stop();
        BugpunchTouchRecorder.stop();
        BugpunchScreenshot.stopRollingBuffer();
        BugpunchCrashHandler.shutdown();
    }

    /**
     * Dump the video ring buffer if recording is running, else return null.
     * Called from the runtime's silent-report path so a bug/crash report
     * picks up the last N seconds of footage when — and only when — the
     * game has previously opted into recording.
     */
    static String dumpRingIfRunning(Activity activity) {
        if (!BugpunchRecorder.getInstance().hasFootage()) {
            Log.i(TAG, "no video dump — recorder not running or no footage yet");
            return null;
        }
        String path = activity.getCacheDir().getAbsolutePath()
            + "/bp_vid_" + System.nanoTime() + ".mp4";
        try {
            boolean ok = BugpunchRecorder.getInstance().dump(path);
            java.io.File f = new java.io.File(path);
            long size = f.length();
            if (!ok || size <= 0) {
                Log.w(TAG, "video dump returned " + ok + " size=" + size + " — skipping");
                if (f.exists()) f.delete();
                return null;
            }
            Log.i(TAG, "video dump ok: " + path + " (" + size + " bytes)");
            return path;
        } catch (Throwable t) {
            Log.w(TAG, "video dump failed", t);
            return null;
        }
    }

    /**
     * Show a consent dialog for screen recording. On Accept we kick off the
     * MediaProjection flow (which brings up its own OS-level consent dialog);
     * on Cancel nothing happens.
     */
    private static void showConsentDialog(final Activity activity) {
        final Dialog dialog = new Dialog(activity, android.R.style.Theme_Translucent_NoTitleBar);
        Window window = dialog.getWindow();
        if (window != null) {
            window.setBackgroundDrawable(new ColorDrawable(0xAA000000));
            window.setLayout(ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT);
        }

        FrameInt dp = new FrameInt(activity);

        // Root — centers the card over a dimmed background.
        LinearLayout root = new LinearLayout(activity);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setGravity(Gravity.CENTER);
        root.setPadding(dp.of(20), dp.of(20), dp.of(20), dp.of(20));

        // Card
        LinearLayout card = new LinearLayout(activity);
        card.setOrientation(LinearLayout.VERTICAL);
        card.setGravity(Gravity.CENTER_HORIZONTAL);
        GradientDrawable cardBg = new GradientDrawable();
        cardBg.setColor(0xFF141820);
        cardBg.setCornerRadius(dp.of(20));
        cardBg.setStroke(dp.of(1), 0xFF2A3240);
        card.setBackground(cardBg);
        int padH = dp.of(28), padV = dp.of(32);
        card.setPadding(padH, padV, padH, padV);
        LinearLayout.LayoutParams cardLp = new LinearLayout.LayoutParams(
            dp.of(340), ViewGroup.LayoutParams.WRAP_CONTENT);
        root.addView(card, cardLp);

        // Title
        TextView title = new TextView(activity);
        title.setText("Enable debug recording");
        title.setTextColor(Color.WHITE);
        title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 20);
        title.setTypeface(title.getTypeface(), android.graphics.Typeface.BOLD);
        title.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams titleLp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        titleLp.bottomMargin = dp.of(10);
        card.addView(title, titleLp);

        // Body
        TextView body = new TextView(activity);
        body.setText("Bugpunch will record your screen so that bug reports include the "
            + "moments leading up to an issue. Recording stays on your device until you "
            + "submit a report.");
        body.setTextColor(0xFFA8B2BF);
        body.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        body.setGravity(Gravity.CENTER);
        body.setLineSpacing(dp.of(3), 1f);
        LinearLayout.LayoutParams bodyLp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        bodyLp.bottomMargin = dp.of(28);
        card.addView(body, bodyLp);

        // Primary CTA
        Button start = pillButton(activity, "Start Recording", 0xFF2A7BE0, Color.WHITE, dp);
        LinearLayout.LayoutParams startLp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, dp.of(48));
        startLp.bottomMargin = dp.of(10);
        card.addView(start, startLp);
        start.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                dialog.dismiss();
                startRecordingFromConfig(activity);
            }
        });

        // Secondary
        Button notNow = pillButton(activity, "Not now", 0x00000000, 0xFFA8B2BF, dp);
        LinearLayout.LayoutParams notNowLp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, dp.of(48));
        card.addView(notNow, notNowLp);
        notNow.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { dialog.dismiss(); }
        });

        dialog.setContentView(root);
        dialog.setCancelable(true);
        dialog.show();
    }

    private static Button pillButton(Context ctx, String text, int bg, int fg, FrameInt dp) {
        Button b = new Button(ctx);
        b.setText(text);
        b.setTextColor(fg);
        b.setAllCaps(false);
        b.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
        b.setTypeface(b.getTypeface(), android.graphics.Typeface.BOLD);
        GradientDrawable bgShape = new GradientDrawable();
        bgShape.setColor(bg);
        bgShape.setCornerRadius(dp.of(12));
        b.setBackground(bgShape);
        return b;
    }

    // Tiny helper so we can compute dp→px without repeating getResources().getDisplayMetrics().
    private static class FrameInt {
        private final android.util.DisplayMetrics dm;
        FrameInt(Context c) { dm = c.getResources().getDisplayMetrics(); }
        int of(int v) { return (int) TypedValue.applyDimension(
            TypedValue.COMPLEX_UNIT_DIP, v, dm); }
    }

    private static void startRecordingFromConfig(Activity activity) {
        JSONObject config = BugpunchRuntime.getConfig();
        JSONObject video = config != null ? config.optJSONObject("video") : null;
        int fps = video != null ? video.optInt("fps", 30) : 30;
        int bitrate = video != null ? video.optInt("bitrate", 2_000_000) : 2_000_000;
        int windowSec = video != null ? video.optInt("bufferSeconds", 30) : 30;
        DisplayMetrics dm = activity.getResources().getDisplayMetrics();
        // Touch recorder shares the same window; sized generously so a
        // multi-finger session over the buffer window never drops events.
        BugpunchTouchRecorder.configure(windowSec * 600);
        BugpunchTouchRecorder.start(activity);
        // Show the floating debug widget (recording indicator + tools).
        BugpunchDebugWidget.show();
        // If the user previously denied MediaProjection, skip the OS dialog
        // this session too and go straight to Unity-surface buffer mode. The
        // Unity side (BugpunchSurfaceRecorder) notices isBufferMode() and
        // starts feeding frames; system UI is excluded but the game surface
        // is captured without another consent prompt.
        if (BugpunchProjectionRequest.wasPreviouslyDenied(activity)) {
            Log.i(TAG, "MediaProjection previously denied — starting buffer-mode fallback");
            BugpunchRecorder.getInstance().configure(
                dm.widthPixels, dm.heightPixels, bitrate, fps, windowSec);
            BugpunchRecorder.getInstance().startBufferMode(activity);
            return;
        }
        // Fire BugpunchProjectionRequest which handles the system-level
        // MediaProjection consent dialog and kicks off the recorder service.
        // Empty callback — we don't need Unity to hear about it; state lives
        // entirely native from here.
        BugpunchProjectionRequest.request(activity,
            dm.widthPixels, dm.heightPixels, bitrate, fps, windowSec, dm.densityDpi,
            "", "");
    }
}
