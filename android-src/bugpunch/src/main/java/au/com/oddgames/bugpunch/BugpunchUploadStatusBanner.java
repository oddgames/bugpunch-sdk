package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.content.Context;
import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.os.Handler;
import android.os.Looper;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;

/**
 * Slim translucent banner pinned to the top of whatever activity is currently
 * resumed, rendered while {@link BugpunchUploader} has pending work. Mirrors
 * how {@link BugpunchSdkErrorOverlay} attaches to an activity's content
 * frame — same compose-by-code style so we don't add an XML layout the
 * AAR packager doesn't already handle.
 *
 * <p>Lifecycle is purely status-driven: when the uploader's queue goes
 * non-empty, {@link #showOrUpdate} is called from the uploader's status
 * observer; when the queue drains, {@link #hide} runs and the banner
 * fades out. There's no "click to dismiss" affordance — uploads happen
 * fast enough that swatting the banner away creates more friction than
 * it saves.
 */
public final class BugpunchUploadStatusBanner {
    private static final String TAG = "[Bugpunch.UploadBanner]";

    private BugpunchUploadStatusBanner() {}

    private static volatile Handler sMain;
    private static volatile FrameLayout sBanner;
    private static volatile TextView sLabel;
    private static volatile Activity sAttachedTo;

    private static Handler main() {
        if (sMain == null) sMain = new Handler(Looper.getMainLooper());
        return sMain;
    }

    /**
     * Show or update the banner with a count + phase. No-op if there's no
     * resumed activity to mount on (we'll catch it on the next status
     * publish from the uploader).
     */
    public static void showOrUpdate(final int pending, final String phaseHint) {
        main().post(new Runnable() {
            @Override public void run() { showOrUpdateOnMain(pending, phaseHint); }
        });
    }

    public static void hide() {
        main().post(new Runnable() {
            @Override public void run() { hideOnMain(); }
        });
    }

    private static void showOrUpdateOnMain(int pending, String phaseHint) {
        try {
            Activity activity = BugpunchRuntime.getResumedActivity();
            if (activity == null || activity.isFinishing()) return;

            ensureAttached(activity);
            if (sLabel != null) sLabel.setText(buildLabel(pending, phaseHint));
        } catch (Throwable t) {
            android.util.Log.w(TAG, "showOrUpdate failed", t);
        }
    }

    private static void hideOnMain() {
        FrameLayout banner = sBanner;
        if (banner == null) return;
        banner.animate()
            .alpha(0f)
            .translationY(-banner.getHeight())
            .setDuration(180)
            .withEndAction(new Runnable() {
                @Override public void run() { detach(); }
            })
            .start();
    }

    private static void detach() {
        try {
            FrameLayout banner = sBanner;
            if (banner != null && banner.getParent() instanceof ViewGroup) {
                ((ViewGroup) banner.getParent()).removeView(banner);
            }
        } catch (Throwable t) {
            android.util.Log.w(TAG, "detach failed", t);
        }
        sBanner = null;
        sLabel = null;
        sAttachedTo = null;
    }

    private static void ensureAttached(Activity activity) {
        if (sBanner != null && sAttachedTo == activity && sBanner.getParent() != null) {
            // Already mounted on this activity; just make sure it's visible.
            sBanner.setAlpha(1f);
            sBanner.setTranslationY(0f);
            return;
        }
        // Either we have no banner yet, or it's still mounted on a stopped
        // activity — rebuild from scratch on the new activity's content view.
        if (sBanner != null) detach();

        Context ctx = activity;
        FrameLayout banner = new FrameLayout(ctx);
        banner.setBackground(buildBackground());
        int pad = dp(ctx, 12);
        banner.setPadding(pad, dp(ctx, 10), pad, dp(ctx, 10));

        LinearLayout row = new LinearLayout(ctx);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);

        ProgressBar spinner = new ProgressBar(ctx, null,
            android.R.attr.progressBarStyleSmall);
        int sz = dp(ctx, 16);
        LinearLayout.LayoutParams spinnerLp = new LinearLayout.LayoutParams(sz, sz);
        spinnerLp.rightMargin = dp(ctx, 10);
        row.addView(spinner, spinnerLp);

        TextView label = new TextView(ctx);
        label.setText(buildLabel(1, "preflight"));
        label.setTextColor(Color.WHITE);
        label.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        label.setSingleLine(true);
        row.addView(label, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT));

        banner.addView(row, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT,
            Gravity.CENTER));

        // Attach to the activity's content frame at the top, ignoring window
        // insets — the banner is informational, not interactive, so a small
        // overlap with the status bar is fine and safer than a WindowInsets
        // dance that breaks across Android versions.
        ViewGroup content = activity.findViewById(android.R.id.content);
        if (content == null) return;
        FrameLayout.LayoutParams lp = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT,
            Gravity.TOP | Gravity.CENTER_HORIZONTAL);
        lp.topMargin = dp(ctx, 24);
        banner.setLayoutParams(lp);
        banner.setAlpha(0f);
        banner.setTranslationY(-dp(ctx, 24));
        content.addView(banner);
        banner.animate()
            .alpha(1f)
            .translationY(0f)
            .setDuration(180)
            .start();

        sBanner = banner;
        sLabel = label;
        sAttachedTo = activity;
    }

    private static String buildLabel(int pending, String phaseHint) {
        if (pending <= 0) return "Crash report sent";
        if (pending == 1) return "Sending crash report…";
        return "Sending crash reports (" + pending + ")…";
    }

    private static GradientDrawable buildBackground() {
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(Color.argb(0xE6, 0x18, 0x18, 0x1B));   // near-black 90%
        bg.setStroke(1, Color.argb(0x40, 0xFF, 0xFF, 0xFF));
        bg.setCornerRadius(28f);
        return bg;
    }

    private static int dp(Context ctx, int dp) {
        return (int) (dp * ctx.getResources().getDisplayMetrics().density + 0.5f);
    }
}
