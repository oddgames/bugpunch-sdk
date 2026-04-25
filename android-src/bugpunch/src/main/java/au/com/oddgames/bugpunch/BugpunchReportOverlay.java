package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.PixelFormat;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.util.Log;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

/**
 * Native overlay views for the Bugpunch report flow.
 *
 * Two overlays:
 *   1. Welcome card — explains debug/recording mode, user taps "Got it" to proceed
 *   2. Recording button — floating draggable red button with elapsed timer,
 *      stays visible until app restart. Tap → fires callback to Unity,
 *      after report is sent the button reappears.
 */
public class BugpunchReportOverlay {
    private static final String TAG = "[Bugpunch.ReportOverlay]";
    private static final String CALLBACK_OBJECT = "BugpunchReportCallback";

    private static View sWelcomeView;
    private static View sRecordingView;
    private static View sRequestHelpView;
    private static TextView sTimerLabel;
    private static Handler sHandler;
    private static long sRecordStartTime;
    private static Runnable sTimerRunnable;

    // ─── UnitySendMessage via reflection (avoids compile-time dep on UnityPlayer) ──

    private static void sendToUnity(String method, String message) {
        try {
            Class<?> playerClass = Class.forName("com.unity3d.player.UnityPlayer");
            playerClass.getMethod("UnitySendMessage", String.class, String.class, String.class)
                    .invoke(null, CALLBACK_OBJECT, method, message);
        } catch (Exception e) {
            Log.e(TAG, "UnitySendMessage failed: " + method, e);
        }
    }

    // ─── Welcome Card ──────────────────────────────────────────────

    /**
     * Show the welcome overlay explaining debug recording mode.
     * "Got it" → UnitySendMessage(CALLBACK_OBJECT, "OnWelcomeConfirm", "")
     * "Cancel" → UnitySendMessage(CALLBACK_OBJECT, "OnWelcomeCancel", "")
     */
    public static void showWelcome(final Activity activity) {
        activity.runOnUiThread(() -> {
            if (sWelcomeView != null) return;

            Context ctx = activity;
            int pad = dp(ctx, 24);
            int padSmall = dp(ctx, 12);

            // Backdrop — full screen semi-transparent
            FrameLayout backdrop = new FrameLayout(ctx);
            backdrop.setBackgroundColor(BugpunchTheme.color("backdrop", 0x99000000));
            backdrop.setClickable(true); // consume touches

            // Scrollable card wrapper — handles landscape where card may exceed screen height
            ScrollView cardScroll = new ScrollView(ctx);
            cardScroll.setFillViewport(false);
            cardScroll.setClipToPadding(false);
            int screenMargin = dp(ctx, 24);
            FrameLayout.LayoutParams scrollLp = new FrameLayout.LayoutParams(
                    dp(ctx, 320), ViewGroup.LayoutParams.WRAP_CONTENT);
            scrollLp.gravity = Gravity.CENTER;
            scrollLp.setMargins(0, screenMargin, 0, screenMargin);
            backdrop.addView(cardScroll, scrollLp);

            // Card container
            LinearLayout card = new LinearLayout(ctx);
            card.setOrientation(LinearLayout.VERTICAL);
            card.setPadding(pad, pad, pad, pad);
            android.graphics.drawable.GradientDrawable cardBg = new android.graphics.drawable.GradientDrawable();
            cardBg.setColor(BugpunchTheme.color("cardBackground", 0xF0222222));
            cardBg.setStroke(dp(ctx, 1), BugpunchTheme.color("cardBorder", 0xFF474747));
            cardBg.setCornerRadius(BugpunchTheme.dp(ctx, "cardRadius", 16));
            card.setBackground(cardBg);
            cardScroll.addView(card, new ViewGroup.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

            // Bugpunch logo
            int logoResId = ctx.getResources().getIdentifier("bugpunch_logo", "drawable", ctx.getPackageName());
            if (logoResId != 0) {
                ImageView logoImg = new ImageView(ctx);
                logoImg.setImageResource(logoResId);
                logoImg.setScaleType(ImageView.ScaleType.FIT_CENTER);
                LinearLayout.LayoutParams logoLp = new LinearLayout.LayoutParams(dp(ctx, 44), dp(ctx, 44));
                logoLp.gravity = Gravity.CENTER_HORIZONTAL;
                card.addView(logoImg, logoLp);
                addSpacer(card, padSmall);
            }

            // Icons row
            LinearLayout iconsRow = new LinearLayout(ctx);
            iconsRow.setOrientation(LinearLayout.HORIZONTAL);
            iconsRow.setGravity(Gravity.CENTER);
            LinearLayout.LayoutParams iconRowLp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, dp(ctx, 64));
            card.addView(iconsRow, iconRowLp);

            // Camera icon
            View cameraIcon = new IconView(ctx, IconView.ICON_CAMERA);
            LinearLayout.LayoutParams iconLp = new LinearLayout.LayoutParams(dp(ctx, 56), dp(ctx, 56));
            iconLp.setMargins(0, 0, dp(ctx, 16), 0);
            iconsRow.addView(cameraIcon, iconLp);

            // Video icon
            View videoIcon = new IconView(ctx, IconView.ICON_VIDEO);
            iconsRow.addView(videoIcon, new LinearLayout.LayoutParams(dp(ctx, 56), dp(ctx, 56)));

            addSpacer(card, padSmall);

            // Title
            TextView title = new TextView(ctx);
            title.setText(BugpunchStrings.text("welcomeTitle", "Report a Bug"));
            title.setTextColor(BugpunchTheme.color("textPrimary", Color.WHITE));
            BugpunchTheme.applyTextSize(title, "fontSizeTitle", 20);
            title.setTypeface(Typeface.DEFAULT_BOLD);
            title.setGravity(Gravity.CENTER);
            card.addView(title);

            addSpacer(card, dp(ctx, 8));

            // Body text
            TextView body = new TextView(ctx);
            body.setText(BugpunchStrings.text("welcomeBody",
                "We'll record your screen while you reproduce the issue.\n\nWhen you're ready, tap the report button to send us the details."));
            body.setTextColor(BugpunchTheme.color("textSecondary", 0xFFBBBBBB));
            BugpunchTheme.applyTextSize(body, "fontSizeBody", 14);
            body.setGravity(Gravity.CENTER);
            body.setLineSpacing(dp(ctx, 2), 1f);
            card.addView(body);

            addSpacer(card, pad);

            // "Got it" button — primary accent
            Button gotItBtn = new Button(ctx);
            gotItBtn.setText(BugpunchStrings.text("welcomeConfirm", "Got it"));
            gotItBtn.setTextColor(BugpunchTheme.color("textPrimary", Color.WHITE));
            BugpunchTheme.applyTextSize(gotItBtn, "fontSizeBody", 16);
            gotItBtn.setTypeface(Typeface.DEFAULT_BOLD);
            gotItBtn.setAllCaps(false);
            android.graphics.drawable.GradientDrawable btnBg = new android.graphics.drawable.GradientDrawable();
            btnBg.setColor(BugpunchTheme.color("accentPrimary", 0xFF2E7D32));
            btnBg.setCornerRadius(dp(ctx, 8));
            gotItBtn.setBackground(btnBg);
            gotItBtn.setPadding(dp(ctx, 16), dp(ctx, 12), dp(ctx, 16), dp(ctx, 12));
            LinearLayout.LayoutParams btnLp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            card.addView(gotItBtn, btnLp);

            gotItBtn.setOnClickListener(v -> {
                hideWelcome(activity);
                sendToUnity("OnWelcomeConfirm", "");
            });

            addSpacer(card, padSmall);

            // "Cancel" link
            TextView cancel = new TextView(ctx);
            cancel.setText(BugpunchStrings.text("welcomeCancel", "Cancel"));
            cancel.setTextColor(BugpunchTheme.color("textMuted", 0xFF888888));
            BugpunchTheme.applyTextSize(cancel, "fontSizeBody", 14);
            cancel.setGravity(Gravity.CENTER);
            cancel.setClickable(true);
            cancel.setOnClickListener(v -> {
                hideWelcome(activity);
                sendToUnity("OnWelcomeCancel", "");
            });
            card.addView(cancel);

            // Add to window
            WindowManager wm = (WindowManager) ctx.getSystemService(Context.WINDOW_SERVICE);
            WindowManager.LayoutParams wlp = new WindowManager.LayoutParams(
                    WindowManager.LayoutParams.MATCH_PARENT,
                    WindowManager.LayoutParams.MATCH_PARENT,
                    WindowManager.LayoutParams.TYPE_APPLICATION,
                    WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                            | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                    PixelFormat.TRANSLUCENT);
            // Make focusable so buttons work
            wlp.flags &= ~WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE;
            inheritImmersiveFlags(backdrop, activity);
            wm.addView(backdrop, wlp);
            sWelcomeView = backdrop;

            Log.d(TAG, "Welcome overlay shown");
        });
    }

    public static void hideWelcome(final Activity activity) {
        activity.runOnUiThread(() -> {
            if (sWelcomeView == null) return;
            try {
                WindowManager wm = (WindowManager) activity.getSystemService(Context.WINDOW_SERVICE);
                wm.removeView(sWelcomeView);
            } catch (Exception e) {
                Log.w(TAG, "Error removing welcome view", e);
            }
            sWelcomeView = null;
        });
    }

    // ─── Request Help Picker ──────────────────────────────────────
    //
    // Three-choice card shown by `Bugpunch.RequestHelp()`:
    //   0 → Ask for help       (short question to the dev team)
    //   1 → Record a bug       (capture video + report a problem)
    //   2 → Request a feature  (suggest / vote on improvements)
    //
    // Backdrop tap, Cancel button, and the Android system back button all
    // dismiss with OnRequestHelpCancel. Picked choice dispatches via
    // UnitySendMessage(CALLBACK_OBJECT, "OnRequestHelpChoice", "<index>").

    public static void showRequestHelp(final Activity activity) {
        activity.runOnUiThread(() -> {
            if (sRequestHelpView != null) return;

            Context ctx = activity;
            int pad = dp(ctx, 24);
            int padSmall = dp(ctx, 12);

            // Backdrop — full screen semi-transparent, swallows touches
            FrameLayout backdrop = new FrameLayout(ctx) {
                @Override
                public boolean dispatchKeyEvent(android.view.KeyEvent event) {
                    if (event.getKeyCode() == android.view.KeyEvent.KEYCODE_BACK
                            && event.getAction() == android.view.KeyEvent.ACTION_UP) {
                        hideRequestHelp(activity);
                        sendToUnity("OnRequestHelpCancel", "");
                        return true;
                    }
                    return super.dispatchKeyEvent(event);
                }
            };
            backdrop.setBackgroundColor(BugpunchTheme.color("backdrop", 0x99000000));
            backdrop.setClickable(true); // consume touches that miss the card
            backdrop.setFocusable(true);
            backdrop.setFocusableInTouchMode(true);
            backdrop.setOnClickListener(v -> {
                hideRequestHelp(activity);
                sendToUnity("OnRequestHelpCancel", "");
            });

            // Horizontal (row) layout kicks in at ≥ 540dp of available screen
            // width. This is the usual tablet / landscape phone breakpoint —
            // narrow phones fall back to the original vertical stack.
            boolean horizontal = ctx.getResources().getConfiguration().screenWidthDp >= 540;

            // Scroll wrapper so the card fits landscape orientation.
            // Horizontal picker needs a wider card (3 × option ≈ 160dp + gaps)
            // than the vertical stack (single column at 340dp).
            ScrollView cardScroll = new ScrollView(ctx);
            cardScroll.setFillViewport(false);
            cardScroll.setClipToPadding(false);
            int screenMargin = dp(ctx, 24);
            int cardWidthDp = horizontal ? 560 : 340;
            FrameLayout.LayoutParams scrollLp = new FrameLayout.LayoutParams(
                    dp(ctx, cardWidthDp), ViewGroup.LayoutParams.WRAP_CONTENT);
            scrollLp.gravity = Gravity.CENTER;
            scrollLp.setMargins(0, screenMargin, 0, screenMargin);
            backdrop.addView(cardScroll, scrollLp);

            // Card container
            LinearLayout card = new LinearLayout(ctx);
            card.setOrientation(LinearLayout.VERTICAL);
            card.setPadding(pad, pad, pad, pad);
            card.setClickable(true); // stop clicks propagating to backdrop
            android.graphics.drawable.GradientDrawable cardBg = new android.graphics.drawable.GradientDrawable();
            cardBg.setColor(BugpunchTheme.color("cardBackground", 0xF0222222));
            cardBg.setStroke(dp(ctx, 1), BugpunchTheme.color("cardBorder", 0xFF474747));
            cardBg.setCornerRadius(BugpunchTheme.dp(ctx, "cardRadius", 16));
            card.setBackground(cardBg);
            cardScroll.addView(card, new ViewGroup.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

            // Title
            TextView title = new TextView(ctx);
            title.setText(BugpunchStrings.text("pickerTitle", "What would you like to do?"));
            title.setTextColor(BugpunchTheme.color("textPrimary", Color.WHITE));
            BugpunchTheme.applyTextSize(title, "fontSizeTitle", 20);
            title.setTypeface(Typeface.DEFAULT_BOLD);
            if (horizontal) title.setGravity(Gravity.CENTER);
            card.addView(title);

            addSpacer(card, dp(ctx, 4));

            // Subtitle
            TextView subtitle = new TextView(ctx);
            subtitle.setText(BugpunchStrings.text("pickerSubtitle",
                "Pick what fits — we'll only bother the dev team with what you send."));
            subtitle.setTextColor(BugpunchTheme.color("textSecondary", 0xFFB8B8B8));
            BugpunchTheme.applyTextSize(subtitle, "fontSizeCaption", 13);
            if (horizontal) subtitle.setGravity(Gravity.CENTER);
            card.addView(subtitle);

            addSpacer(card, padSmall);

            // Three option panels. Horizontal row on wide screens, vertical
            // column on narrow phones — shared builder so we don't duplicate
            // icon / label / underline wiring.
            LinearLayout options = new LinearLayout(ctx);
            options.setOrientation(horizontal ? LinearLayout.HORIZONTAL : LinearLayout.VERTICAL);
            if (horizontal) options.setWeightSum(3f);
            card.addView(options, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

            int gap = dp(ctx, horizontal ? 12 : 8);
            addOption(options, ctx, activity, 0,
                    BugpunchStrings.text("pickerAskTitle", "Ask for help"),
                    BugpunchStrings.text("pickerAskCaption", "Short question to the dev team"),
                    BugpunchTheme.color("accentChat", 0xFF336199), "bugpunch_help_ask",
                    horizontal, 0, gap);
            addOption(options, ctx, activity, 1,
                    BugpunchStrings.text("pickerBugTitle", "Record a bug"),
                    BugpunchStrings.text("pickerBugCaption", "Capture a video + report a problem"),
                    BugpunchTheme.color("accentBug", 0xFF943838), "bugpunch_help_bug",
                    horizontal, 1, gap);
            addOption(options, ctx, activity, 2,
                    BugpunchStrings.text("pickerFeatureTitle", "Request a feature"),
                    BugpunchStrings.text("pickerFeatureCaption", "Suggest / vote on improvements"),
                    BugpunchTheme.color("accentFeedback", 0xFF407D4C), "bugpunch_help_feedback",
                    horizontal, 2, gap);

            addSpacer(card, padSmall);

            // Cancel text button
            TextView cancel = new TextView(ctx);
            cancel.setText(BugpunchStrings.text("pickerCancel", "Cancel"));
            cancel.setTextColor(BugpunchTheme.color("textMuted", 0xFF999999));
            BugpunchTheme.applyTextSize(cancel, "fontSizeBody", 14);
            cancel.setGravity(Gravity.CENTER);
            cancel.setClickable(true);
            cancel.setPadding(0, dp(ctx, 8), 0, dp(ctx, 4));
            cancel.setOnClickListener(v -> {
                hideRequestHelp(activity);
                sendToUnity("OnRequestHelpCancel", "");
            });
            card.addView(cancel);

            // Add to window — focusable so back-button handler fires
            WindowManager wm = (WindowManager) ctx.getSystemService(Context.WINDOW_SERVICE);
            WindowManager.LayoutParams wlp = new WindowManager.LayoutParams(
                    WindowManager.LayoutParams.MATCH_PARENT,
                    WindowManager.LayoutParams.MATCH_PARENT,
                    WindowManager.LayoutParams.TYPE_APPLICATION,
                    WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                    PixelFormat.TRANSLUCENT);
            inheritImmersiveFlags(backdrop, activity);
            wm.addView(backdrop, wlp);
            backdrop.requestFocus();
            sRequestHelpView = backdrop;

            Log.d(TAG, "Request-help picker shown (layout=" + (horizontal ? "horizontal" : "vertical") + ")");
        });
    }

    /**
     * Build one picker option and add it to {@code parent}. Horizontal row
     * picker uses a stacked icon/title/caption with an accent underline bar;
     * vertical stack keeps the legacy icon-leading row with chevron. Both
     * share the same accent colour + click callback.
     */
    private static void addOption(LinearLayout parent, Context ctx, Activity activity, int choice,
                                  String titleText, String captionText, int accent, String iconResName,
                                  boolean horizontal, int indexInRow, int gap) {
        View option = horizontal
                ? buildPickerOptionStacked(ctx, activity, choice, titleText, captionText, accent, iconResName)
                : buildPickerOption(ctx, activity, choice, titleText, captionText, accent, iconResName);

        LinearLayout.LayoutParams lp;
        if (horizontal) {
            lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
            if (indexInRow > 0) lp.leftMargin = gap;
        } else {
            lp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            if (indexInRow > 0) lp.topMargin = gap;
        }
        parent.addView(option, lp);
    }

    /**
     * Stacked-layout option panel used by the horizontal picker.
     *
     * <pre>
     *   ┌─────────────┐
     *   │   [icon]    │    64dp centered
     *   │    Title    │    18sp bold
     *   │   caption   │    13sp, 2 lines
     *   │ ═══════════ │    2dp accent underline
     *   └─────────────┘
     * </pre>
     */
    private static View buildPickerOptionStacked(Context ctx, final Activity activity, final int choice,
                                                 String titleText, String captionText, int accent,
                                                 String iconResName) {
        // Outer panel = clickable card with an accent bottom-underline drawn
        // as a 2dp child view. Kept as two views rather than a LayerDrawable
        // so the underline always sits flush with the card's rounded corners.
        LinearLayout panel = new LinearLayout(ctx);
        panel.setOrientation(LinearLayout.VERTICAL);
        panel.setGravity(Gravity.CENTER_HORIZONTAL);
        panel.setClickable(true);
        panel.setFocusable(true);

        int ipV = dp(ctx, 14);
        int ipH = dp(ctx, 10);
        panel.setPadding(ipH, ipV, ipH, 0);

        android.graphics.drawable.GradientDrawable bg = new android.graphics.drawable.GradientDrawable();
        bg.setColor(BugpunchTheme.color("cardBackground", 0xFF2B2B2B));
        bg.setStroke(dp(ctx, 1), BugpunchTheme.color("cardBorder", 0xFF474747));
        bg.setCornerRadius(dp(ctx, 10));
        panel.setBackground(bg);

        // Icon (64dp square, centered). Falls back to the accent dot only if
        // the bundled drawable went missing from the AAR.
        int iconRes = ctx.getResources().getIdentifier(
                iconResName, "drawable", ctx.getPackageName());
        if (iconRes != 0) {
            ImageView iv = new ImageView(ctx);
            iv.setImageResource(iconRes);
            iv.setScaleType(ImageView.ScaleType.FIT_CENTER);
            LinearLayout.LayoutParams ivLp = new LinearLayout.LayoutParams(dp(ctx, 64), dp(ctx, 64));
            ivLp.gravity = Gravity.CENTER_HORIZONTAL;
            panel.addView(iv, ivLp);
        } else {
            View dot = new View(ctx);
            android.graphics.drawable.GradientDrawable dotBg = new android.graphics.drawable.GradientDrawable();
            dotBg.setShape(android.graphics.drawable.GradientDrawable.OVAL);
            dotBg.setColor(accent);
            dot.setBackground(dotBg);
            LinearLayout.LayoutParams dotLp = new LinearLayout.LayoutParams(dp(ctx, 16), dp(ctx, 16));
            dotLp.gravity = Gravity.CENTER_HORIZONTAL;
            dotLp.topMargin = dp(ctx, 24);
            dotLp.bottomMargin = dp(ctx, 24);
            panel.addView(dot, dotLp);
        }

        addSpacer(panel, dp(ctx, 8));

        TextView titleLabel = new TextView(ctx);
        titleLabel.setText(titleText);
        titleLabel.setTextColor(BugpunchTheme.color("textPrimary", Color.WHITE));
        titleLabel.setTextSize(TypedValue.COMPLEX_UNIT_SP, 18);
        titleLabel.setTypeface(Typeface.DEFAULT_BOLD);
        titleLabel.setGravity(Gravity.CENTER);
        panel.addView(titleLabel, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        addSpacer(panel, dp(ctx, 4));

        TextView captionLabel = new TextView(ctx);
        captionLabel.setText(captionText);
        captionLabel.setTextColor(BugpunchTheme.color("textSecondary", 0xFFB2B2B2));
        captionLabel.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        captionLabel.setGravity(Gravity.CENTER);
        captionLabel.setMaxLines(2);
        captionLabel.setEllipsize(android.text.TextUtils.TruncateAt.END);
        LinearLayout.LayoutParams capLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        capLp.leftMargin = capLp.rightMargin = dp(ctx, 4);
        panel.addView(captionLabel, capLp);

        addSpacer(panel, dp(ctx, 12));

        // Accent underline — 2dp bar flush with the bottom of the card
        View underline = new View(ctx);
        underline.setBackgroundColor(accent);
        LinearLayout.LayoutParams ulp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(ctx, 2));
        panel.addView(underline, ulp);

        panel.setOnClickListener(v -> {
            hideRequestHelp(activity);
            sendToUnity("OnRequestHelpChoice", String.valueOf(choice));
        });

        return panel;
    }

    private static View buildPickerOption(Context ctx, final Activity activity, final int choice,
                                          String titleText, String captionText, int accent,
                                          String iconResName) {
        LinearLayout btn = new LinearLayout(ctx);
        btn.setOrientation(LinearLayout.HORIZONTAL);
        btn.setGravity(Gravity.CENTER_VERTICAL);
        int ip = dp(ctx, 14);
        btn.setPadding(ip, dp(ctx, 12), ip, dp(ctx, 12));
        btn.setClickable(true);
        btn.setFocusable(true);

        android.graphics.drawable.GradientDrawable bg = new android.graphics.drawable.GradientDrawable();
        bg.setColor(BugpunchTheme.color("cardBackground", 0xFF2B2B2B));
        bg.setStroke(dp(ctx, 1), BugpunchTheme.color("cardBorder", 0xFF474747));
        bg.setCornerRadius(dp(ctx, 8));
        btn.setBackground(bg);

        // Leading icon — falls back to accent dot if drawable isn't bundled.
        int iconRes = ctx.getResources().getIdentifier(
                iconResName, "drawable", ctx.getPackageName());
        if (iconRes != 0) {
            ImageView iv = new ImageView(ctx);
            iv.setImageResource(iconRes);
            iv.setScaleType(ImageView.ScaleType.FIT_CENTER);
            LinearLayout.LayoutParams ivLp = new LinearLayout.LayoutParams(dp(ctx, 48), dp(ctx, 48));
            ivLp.rightMargin = dp(ctx, 12);
            btn.addView(iv, ivLp);
        } else {
            View dot = new View(ctx);
            android.graphics.drawable.GradientDrawable dotBg = new android.graphics.drawable.GradientDrawable();
            dotBg.setShape(android.graphics.drawable.GradientDrawable.OVAL);
            dotBg.setColor(accent);
            dot.setBackground(dotBg);
            LinearLayout.LayoutParams dotLp = new LinearLayout.LayoutParams(dp(ctx, 10), dp(ctx, 10));
            dotLp.rightMargin = dp(ctx, 12);
            btn.addView(dot, dotLp);
        }

        // Text column
        LinearLayout textCol = new LinearLayout(ctx);
        textCol.setOrientation(LinearLayout.VERTICAL);
        LinearLayout.LayoutParams textLp = new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        btn.addView(textCol, textLp);

        TextView titleLabel = new TextView(ctx);
        titleLabel.setText(titleText);
        titleLabel.setTextColor(BugpunchTheme.color("textPrimary", Color.WHITE));
        titleLabel.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
        titleLabel.setTypeface(Typeface.DEFAULT_BOLD);
        textCol.addView(titleLabel);

        TextView captionLabel = new TextView(ctx);
        captionLabel.setText(captionText);
        captionLabel.setTextColor(BugpunchTheme.color("textSecondary", 0xFFB2B2B2));
        BugpunchTheme.applyTextSize(captionLabel, "fontSizeCaption", 12);
        LinearLayout.LayoutParams capLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        capLp.topMargin = dp(ctx, 2);
        textCol.addView(captionLabel, capLp);

        // Chevron
        TextView chev = new TextView(ctx);
        chev.setText("›");
        chev.setTextColor(BugpunchTheme.color("textMuted", 0xFF8C8C8C));
        chev.setTextSize(TypedValue.COMPLEX_UNIT_SP, 20);
        chev.setTypeface(Typeface.DEFAULT_BOLD);
        LinearLayout.LayoutParams chevLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        chevLp.leftMargin = dp(ctx, 8);
        btn.addView(chev, chevLp);

        btn.setOnClickListener(v -> {
            hideRequestHelp(activity);
            sendToUnity("OnRequestHelpChoice", String.valueOf(choice));
        });

        return btn;
    }

    public static void hideRequestHelp(final Activity activity) {
        activity.runOnUiThread(() -> {
            if (sRequestHelpView == null) return;
            try {
                WindowManager wm = (WindowManager) activity.getSystemService(Context.WINDOW_SERVICE);
                wm.removeView(sRequestHelpView);
            } catch (Exception e) {
                Log.w(TAG, "Error removing request-help view", e);
            }
            sRequestHelpView = null;
        });
    }

    // ─── Chat Board (native shell) ────────────────────────────────
    //
    // The full chat UI lives in C# (BugpunchChatBoard.cs) because it's a
    // multi-view master-detail widget with polling and we don't want to
    // reinvent a full chat client natively. This entry point exists so the
    // SDK's public surface stays native-first: any caller that wants the
    // chat board goes through INativeDialog.ShowChatBoard, which on Android
    // reaches here, which bounces the request back into C#.
    //
    // Callback chain:
    //   AndroidDialog.ShowChatBoard()
    //     → BugpunchReportOverlay.showChatBoard(activity)
    //       → UnitySendMessage("BugpunchClient", "OnShowChatBoardRequested", "")
    //         → BugpunchClient.OnShowChatBoardRequested(...)
    //           → BugpunchChatBoard.Show()
    public static void showChatBoard(final Activity activity) {
        // No native UI to draw — just forward the request. Running on the
        // UI thread keeps this consistent with the other overlay methods
        // and gives us a single place to add a native pre-check later
        // (e.g. offline snackbar) without callers needing to care.
        activity.runOnUiThread(() -> sendToUnity("OnShowChatBoardRequested", ""));
    }

    // ─── Recording Overlay (floating button) ───────────────────────

    /**
     * Show the floating recording button. Stays visible until hideRecordingOverlay
     * is called or the app is destroyed.
     * Tap → UnitySendMessage(CALLBACK_OBJECT, "OnReportTapped", "")
     *
     * The chat + feedback icons stack above the red record circle so that
     * any visible recording session also exposes the "Ask the team" / "Send
     * feedback" shortcuts without needing a second overlay. Piggyback per
     * the `feedback_indicator_piggyback.md` policy — the recording pill is
     * already on screen while debug mode is active, so we reuse it.
     */
    public static void showRecordingOverlay(final Activity activity) {
        activity.runOnUiThread(() -> {
            if (sRecordingView != null) return;

            Context ctx = activity;
            int btnSize = dp(ctx, 56);
            int shortcutSize = dp(ctx, 40);
            int margin = dp(ctx, 16);

            // Container for shortcuts + record button + timer
            LinearLayout container = new LinearLayout(ctx);
            container.setOrientation(LinearLayout.VERTICAL);
            container.setGravity(Gravity.CENTER_HORIZONTAL);

            // Chat shortcut (topmost — least destructive action goes first)
            ShortcutButton chatBtn = new ShortcutButton(ctx, ShortcutButton.ICON_CHAT);
            LinearLayout.LayoutParams chatLp = new LinearLayout.LayoutParams(shortcutSize, shortcutSize);
            chatLp.bottomMargin = dp(ctx, 8);
            container.addView(chatBtn, chatLp);
            chatBtn.setOnClickListener(v -> sendToUnity("OnRecordingBarChatTapped", ""));

            // Feedback shortcut
            ShortcutButton feedbackBtn = new ShortcutButton(ctx, ShortcutButton.ICON_FEEDBACK);
            LinearLayout.LayoutParams feedbackLp = new LinearLayout.LayoutParams(shortcutSize, shortcutSize);
            feedbackLp.bottomMargin = dp(ctx, 8);
            container.addView(feedbackBtn, feedbackLp);
            feedbackBtn.setOnClickListener(v -> sendToUnity("OnRecordingBarFeedbackTapped", ""));

            // Red circle button with white square stop icon
            RecordButton button = new RecordButton(ctx, btnSize);
            button.setClickable(true);
            container.addView(button, new LinearLayout.LayoutParams(btnSize, btnSize));

            // Timer label
            sTimerLabel = new TextView(ctx);
            sTimerLabel.setText("0:00");
            sTimerLabel.setTextColor(BugpunchTheme.color("textPrimary", Color.WHITE));
            sTimerLabel.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11);
            sTimerLabel.setTypeface(Typeface.DEFAULT_BOLD);
            sTimerLabel.setGravity(Gravity.CENTER);
            sTimerLabel.setShadowLayer(2, 1, 1, Color.BLACK);
            LinearLayout.LayoutParams timerLp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            timerLp.topMargin = dp(ctx, 4);
            container.addView(sTimerLabel, timerLp);

            // Window params — positioned bottom-right
            WindowManager wm = (WindowManager) ctx.getSystemService(Context.WINDOW_SERVICE);
            WindowManager.LayoutParams wlp = new WindowManager.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT,
                    ViewGroup.LayoutParams.WRAP_CONTENT,
                    WindowManager.LayoutParams.TYPE_APPLICATION,
                    WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                            | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                    PixelFormat.TRANSLUCENT);
            wlp.gravity = Gravity.BOTTOM | Gravity.END;
            wlp.x = margin;
            wlp.y = margin + dp(ctx, 40); // above nav bar

            // Drag + tap handling — attach to the button itself so taps on
            // the red circle are captured reliably. Pass the container as the
            // view to reposition (the window manages the whole container).
            // Shortcut buttons above have their own onClick listeners and
            // absorb their own events, so dragging is only active on the
            // record button itself.
            button.setOnTouchListener(new DragTouchListener(wm, wlp, container, () -> {
                sendToUnity("OnReportTapped", "");
            }));

            wm.addView(container, wlp);
            sRecordingView = container;

            // Start timer
            sRecordStartTime = SystemClock.elapsedRealtime();
            sHandler = new Handler(Looper.getMainLooper());
            sTimerRunnable = new Runnable() {
                @Override
                public void run() {
                    if (sTimerLabel == null || sRecordingView == null) return;
                    long elapsed = (SystemClock.elapsedRealtime() - sRecordStartTime) / 1000;
                    long m = elapsed / 60;
                    long s = elapsed % 60;
                    sTimerLabel.setText(String.format("%d:%02d", m, s));
                    sHandler.postDelayed(this, 1000);
                }
            };
            sHandler.postDelayed(sTimerRunnable, 1000);

            Log.d(TAG, "Recording overlay shown");
        });
    }

    public static void hideRecordingOverlay(final Activity activity) {
        activity.runOnUiThread(() -> {
            if (sRecordingView == null) return;
            if (sHandler != null && sTimerRunnable != null) {
                sHandler.removeCallbacks(sTimerRunnable);
            }
            try {
                WindowManager wm = (WindowManager) activity.getSystemService(Context.WINDOW_SERVICE);
                wm.removeView(sRecordingView);
            } catch (Exception e) {
                Log.w(TAG, "Error removing recording overlay", e);
            }
            sRecordingView = null;
            sTimerLabel = null;
        });
    }

    /**
     * Reset the timer and reshow if needed (after a report is submitted,
     * the button stays but timer resets).
     */
    public static void resetTimer() {
        sRecordStartTime = SystemClock.elapsedRealtime();
        if (sTimerLabel != null) sTimerLabel.setText("0:00");
    }

    // ─── Helpers ───────────────────────────────────────────────────

    /**
     * Mirror the host activity's decor-view system-UI flags onto a
     * {@link WindowManager}-managed overlay so adding it doesn't pull the
     * app out of immersive / fullscreen mode (which would surface the
     * status and nav bars). Unity's activity sets
     * {@code SYSTEM_UI_FLAG_IMMERSIVE_STICKY} et al. — without this copy a
     * new focusable {@code TYPE_APPLICATION} window resets to default
     * visibility and the user sees the status bar pop in.
     */
    private static void inheritImmersiveFlags(View overlay, Activity activity) {
        if (overlay == null || activity == null) return;
        try {
            overlay.setSystemUiVisibility(
                    activity.getWindow().getDecorView().getSystemUiVisibility());
        } catch (Throwable ignore) { /* best-effort */ }
    }

    private static int dp(Context ctx, int dp) {
        return (int) (dp * ctx.getResources().getDisplayMetrics().density + 0.5f);
    }

    private static void addSpacer(LinearLayout parent, int height) {
        View spacer = new View(parent.getContext());
        parent.addView(spacer, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, height));
    }

    // ─── Custom Views ──────────────────────────────────────────────

    /** Draws vector icons (camera or video camera) */
    static class IconView extends View {
        static final int ICON_CAMERA = 0;
        static final int ICON_VIDEO = 1;
        private final int iconType;
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);

        IconView(Context ctx, int type) {
            super(ctx);
            iconType = type;
        }

        @Override
        protected void onDraw(Canvas canvas) {
            float w = getWidth(), h = getHeight();
            float cx = w / 2f, cy = h / 2f;

            if (iconType == ICON_CAMERA) {
                drawCamera(canvas, cx, cy, w * 0.4f);
            } else {
                drawVideoCamera(canvas, cx, cy, w * 0.4f);
            }
        }

        private void drawCamera(Canvas c, float cx, float cy, float size) {
            paint.setStyle(Paint.Style.STROKE);
            paint.setStrokeWidth(size * 0.12f);
            paint.setColor(Color.parseColor("#64B5F6")); // light blue

            // Body rectangle
            float bw = size * 1.4f, bh = size;
            RectF body = new RectF(cx - bw / 2, cy - bh / 2 + size * 0.15f,
                    cx + bw / 2, cy + bh / 2 + size * 0.15f);
            c.drawRoundRect(body, size * 0.15f, size * 0.15f, paint);

            // Lens circle
            paint.setStyle(Paint.Style.STROKE);
            c.drawCircle(cx, cy + size * 0.15f, size * 0.3f, paint);

            // Viewfinder bump
            Path bump = new Path();
            float bumpW = size * 0.5f;
            bump.moveTo(cx - bumpW / 2, body.top);
            bump.lineTo(cx - bumpW * 0.35f, body.top - size * 0.25f);
            bump.lineTo(cx + bumpW * 0.35f, body.top - size * 0.25f);
            bump.lineTo(cx + bumpW / 2, body.top);
            c.drawPath(bump, paint);
        }

        private void drawVideoCamera(Canvas c, float cx, float cy, float size) {
            paint.setStyle(Paint.Style.STROKE);
            paint.setStrokeWidth(size * 0.12f);
            paint.setColor(Color.parseColor("#EF5350")); // red

            // Body rectangle (shifted left)
            float bw = size * 1.2f, bh = size * 0.9f;
            float bodyLeft = cx - size * 0.5f;
            RectF body = new RectF(bodyLeft - bw / 2, cy - bh / 2,
                    bodyLeft + bw / 2, cy + bh / 2);
            c.drawRoundRect(body, size * 0.12f, size * 0.12f, paint);

            // Viewfinder triangle (right side)
            Path tri = new Path();
            float triLeft = body.right + size * 0.08f;
            tri.moveTo(triLeft, cy - bh * 0.3f);
            tri.lineTo(triLeft + size * 0.55f, cy);
            tri.lineTo(triLeft, cy + bh * 0.3f);
            tri.close();
            c.drawPath(tri, paint);

            // Record dot
            paint.setStyle(Paint.Style.FILL);
            c.drawCircle(body.left + size * 0.25f, body.top + size * 0.2f, size * 0.1f, paint);
        }
    }

    /**
     * Circular shortcut button for the recording overlay — draws a speech
     * bubble (chat) or lightbulb (feedback) on a muted accent fill. Handles
     * its own click dispatch via {@link View#setOnClickListener}; taps on
     * this button don't drag the main overlay (it sits above the record
     * button's DragTouchListener so events land here first).
     */
    static class ShortcutButton extends View {
        static final int ICON_CHAT = 0;
        static final int ICON_FEEDBACK = 1;
        private final int iconType;
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);

        ShortcutButton(Context ctx, int type) {
            super(ctx);
            iconType = type;
            setElevation(dp(ctx, 4));
            setClickable(true);
        }

        @Override
        protected void onDraw(Canvas canvas) {
            float w = getWidth(), h = getHeight();
            float r = Math.min(w, h) / 2f;
            float cx = w / 2f, cy = h / 2f;

            // Circular fill — accent colour by icon type, matches the palette
            // used on the Ask for help / Send feedback picker rows.
            paint.setStyle(Paint.Style.FILL);
            paint.setColor(iconType == ICON_CHAT
                    ? BugpunchTheme.color("accentChat", 0xFF336199)
                    : BugpunchTheme.color("accentFeedback", 0xFF407D4C));
            canvas.drawCircle(cx, cy, r * 0.92f, paint);

            // White glyph
            paint.setStyle(Paint.Style.STROKE);
            paint.setColor(Color.WHITE);
            paint.setStrokeWidth(r * 0.12f);
            paint.setStrokeCap(Paint.Cap.ROUND);
            paint.setStrokeJoin(Paint.Join.ROUND);

            if (iconType == ICON_CHAT) {
                drawChat(canvas, cx, cy, r * 0.5f);
            } else {
                drawLightbulb(canvas, cx, cy, r * 0.5f);
            }
        }

        private void drawChat(Canvas c, float cx, float cy, float s) {
            // Rounded-rect speech bubble with a little tail on the bottom-left.
            float bw = s * 1.7f, bh = s * 1.2f;
            RectF body = new RectF(cx - bw / 2, cy - bh / 2 - s * 0.1f,
                    cx + bw / 2, cy + bh / 2 - s * 0.1f);
            paint.setStyle(Paint.Style.STROKE);
            c.drawRoundRect(body, s * 0.25f, s * 0.25f, paint);

            // Tail (two short strokes from bottom-left of bubble down-left).
            Path tail = new Path();
            tail.moveTo(body.left + s * 0.3f, body.bottom);
            tail.lineTo(body.left + s * 0.1f, body.bottom + s * 0.4f);
            tail.lineTo(body.left + s * 0.7f, body.bottom - s * 0.05f);
            c.drawPath(tail, paint);

            // Three dots inside — signals "talking".
            paint.setStyle(Paint.Style.FILL);
            float dotR = s * 0.1f;
            float dotY = cy - s * 0.1f;
            c.drawCircle(cx - s * 0.45f, dotY, dotR, paint);
            c.drawCircle(cx, dotY, dotR, paint);
            c.drawCircle(cx + s * 0.45f, dotY, dotR, paint);
        }

        private void drawLightbulb(Canvas c, float cx, float cy, float s) {
            // Bulb = circle on top of a short neck + base.
            paint.setStyle(Paint.Style.STROKE);
            c.drawCircle(cx, cy - s * 0.2f, s * 0.7f, paint);

            // Base (trapezoid-ish: two short horizontals)
            Path base = new Path();
            base.moveTo(cx - s * 0.35f, cy + s * 0.55f);
            base.lineTo(cx + s * 0.35f, cy + s * 0.55f);
            base.moveTo(cx - s * 0.25f, cy + s * 0.8f);
            base.lineTo(cx + s * 0.25f, cy + s * 0.8f);
            c.drawPath(base, paint);

            // Filament hint inside the bulb (two short lines)
            paint.setStyle(Paint.Style.FILL);
            float fR = s * 0.08f;
            c.drawCircle(cx - s * 0.18f, cy - s * 0.15f, fR, paint);
            c.drawCircle(cx + s * 0.18f, cy - s * 0.15f, fR, paint);
        }
    }

    /** Red circle button with white square stop icon */
    static class RecordButton extends View {
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final int size;

        RecordButton(Context ctx, int size) {
            super(ctx);
            this.size = size;
            setElevation(dp(ctx, 6));
        }

        @Override
        protected void onDraw(Canvas canvas) {
            float r = size / 2f;
            // Accent-record circle
            paint.setStyle(Paint.Style.FILL);
            paint.setColor(BugpunchTheme.color("accentRecord", 0xFFD32F2F));
            canvas.drawCircle(r, r, r * 0.9f, paint);

            // White rounded square (stop icon)
            paint.setColor(Color.WHITE);
            float sq = r * 0.45f;
            RectF rect = new RectF(r - sq, r - sq, r + sq, r + sq);
            canvas.drawRoundRect(rect, sq * 0.2f, sq * 0.2f, paint);
        }
    }

    /** Handles drag + tap on the floating button */
    static class DragTouchListener implements View.OnTouchListener {
        private final WindowManager wm;
        private final WindowManager.LayoutParams lp;
        private final View windowView;
        private final Runnable onTap;
        private int startX, startY;
        private float touchStartX, touchStartY;
        private boolean isDragging;

        DragTouchListener(WindowManager wm, WindowManager.LayoutParams lp, View windowView, Runnable onTap) {
            this.wm = wm;
            this.lp = lp;
            this.windowView = windowView;
            this.onTap = onTap;
        }

        @Override
        public boolean onTouch(View v, MotionEvent event) {
            switch (event.getAction()) {
                case MotionEvent.ACTION_DOWN:
                    startX = lp.x;
                    startY = lp.y;
                    touchStartX = event.getRawX();
                    touchStartY = event.getRawY();
                    isDragging = false;
                    return true;

                case MotionEvent.ACTION_MOVE:
                    float dx = event.getRawX() - touchStartX;
                    float dy = event.getRawY() - touchStartY;
                    if (Math.abs(dx) > 10 || Math.abs(dy) > 10) isDragging = true;
                    if (isDragging) {
                        // Gravity is BOTTOM|END so x/y are inverted
                        lp.x = startX - (int) dx;
                        lp.y = startY - (int) dy;
                        try { wm.updateViewLayout(windowView, lp); } catch (Exception ignored) {}
                    }
                    return true;

                case MotionEvent.ACTION_UP:
                    if (!isDragging) {
                        onTap.run();
                    }
                    return true;
            }
            return false;
        }
    }
}
