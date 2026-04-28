package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.content.Context;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Handler;
import android.os.Looper;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

/**
 * Persistent top pill that tells the player they have unread chat messages
 * from a dev. Same compose-by-code style as {@link BugpunchUploadStatusBanner};
 * differences from that one:
 *
 * <ul>
 *  <li>Persistent — stays mounted across activity changes until the player
 *      taps it (opens the chat) or taps the X (snoozes the current set).</li>
 *  <li>Clickable — tap anywhere except the X opens the chat board.</li>
 *  <li>Dismiss X — hides the banner. C# tracks the snooze high-watermark so a
 *      newer QA message will re-show it.</li>
 *  <li>Sits 56dp below the top so it doesn't overlap the upload banner if
 *      both are visible at once.</li>
 * </ul>
 *
 * Driven from C# {@code BugpunchClient.ChatReplyHeartbeat} via JNI on
 * {@code BugpunchRuntime.showChatBanner(int)} / {@code hideChatBanner()}.
 */
public final class BugpunchChatBanner {
    private static final String TAG = "[Bugpunch.ChatBanner]";

    private BugpunchChatBanner() {}

    private static volatile Handler sMain;
    private static volatile FrameLayout sBanner;
    private static volatile TextView sLabel;
    private static volatile Activity sAttachedTo;
    private static volatile int sUnread;

    private static Handler main() {
        if (sMain == null) sMain = new Handler(Looper.getMainLooper());
        return sMain;
    }

    public static void showOrUpdate(final int unread) {
        main().post(new Runnable() {
            @Override public void run() { showOrUpdateOnMain(unread); }
        });
    }

    public static void hide() {
        main().post(new Runnable() {
            @Override public void run() { hideOnMain(); }
        });
    }

    private static void showOrUpdateOnMain(int unread) {
        try {
            sUnread = unread;
            Activity activity = BugpunchRuntime.getResumedActivity();
            if (activity == null || activity.isFinishing()) return;

            ensureAttached(activity);
            if (sLabel != null) sLabel.setText(buildLabel(unread));
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
            sBanner.setAlpha(1f);
            sBanner.setTranslationY(0f);
            return;
        }
        if (sBanner != null) detach();

        // Drop-down notification card. Wider + taller than the old slim
        // pill, with a primary heading + sub-line. Has *no* dismiss X — the
        // only way to clear it is to tap, which opens the chat sheet and
        // marks the messages read. Matches the request that the
        // notification "needs to be interacted with to get rid of".
        Context ctx = activity;
        FrameLayout banner = new FrameLayout(ctx);
        banner.setBackground(buildBackground());
        int padH = dp(ctx, 16);
        int padV = dp(ctx, 12);
        banner.setPadding(padH, padV, padH, padV);
        banner.setClickable(true);

        LinearLayout row = new LinearLayout(ctx);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);

        TextView icon = new TextView(ctx);
        icon.setText("💬");
        icon.setTextSize(TypedValue.COMPLEX_UNIT_SP, 22);
        LinearLayout.LayoutParams iconLp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT);
        iconLp.rightMargin = dp(ctx, 12);
        row.addView(icon, iconLp);

        LinearLayout textCol = new LinearLayout(ctx);
        textCol.setOrientation(LinearLayout.VERTICAL);

        TextView label = new TextView(ctx);
        label.setText(buildLabel(sUnread));
        label.setTextColor(Color.WHITE);
        label.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
        label.setSingleLine(true);
        label.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        textCol.addView(label, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT));

        TextView sub = new TextView(ctx);
        sub.setText("Tap to read");
        sub.setTextColor(Color.argb(0xC0, 0xFF, 0xFF, 0xFF));
        sub.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
        textCol.addView(sub, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT));

        row.addView(textCol, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT));

        banner.addView(row, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT,
            Gravity.CENTER));

        // Tap anywhere on the card opens the chat board.
        banner.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Activity a = BugpunchRuntime.getResumedActivity();
                if (a != null) BugpunchReportOverlay.showChatBoard(a);
                BugpunchRuntime.onChatBannerOpened();
                hide();
            }
        });

        ViewGroup content = activity.findViewById(android.R.id.content);
        if (content == null) return;
        // Card spans most of the screen width. Capped at 420 dp (tablet)
        // and indented 16 dp from the edges on phones.
        int screenW = ctx.getResources().getDisplayMetrics().widthPixels;
        int cardW = Math.min(dp(ctx, 420), screenW - dp(ctx, 32));
        FrameLayout.LayoutParams lp = new FrameLayout.LayoutParams(
            cardW,
            ViewGroup.LayoutParams.WRAP_CONTENT,
            Gravity.TOP | Gravity.CENTER_HORIZONTAL);
        lp.topMargin = dp(ctx, 56);
        banner.setLayoutParams(lp);
        banner.setAlpha(0f);
        banner.setTranslationY(-dp(ctx, 24));
        content.addView(banner);
        banner.animate()
            .alpha(1f)
            .translationY(0f)
            .setDuration(220)
            .start();

        sBanner = banner;
        sLabel = label;
        sAttachedTo = activity;
    }

    private static String buildLabel(int unread) {
        if (unread <= 1) return "New message from a dev";
        return unread + " new messages from a dev";
    }

    private static GradientDrawable buildBackground() {
        GradientDrawable bg = new GradientDrawable();
        // Tinted blue/teal so it reads differently from the dark crash-upload pill.
        bg.setColor(Color.argb(0xF0, 0x1F, 0x4E, 0x79));
        bg.setStroke(1, Color.argb(0x55, 0xFF, 0xFF, 0xFF));
        bg.setCornerRadius(28f);
        return bg;
    }

    private static int dp(Context ctx, int dp) {
        return (int) (dp * ctx.getResources().getDisplayMetrics().density + 0.5f);
    }
}
