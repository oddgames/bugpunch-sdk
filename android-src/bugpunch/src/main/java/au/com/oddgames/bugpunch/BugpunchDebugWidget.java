package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.PixelFormat;
import android.graphics.drawable.GradientDrawable;
import android.os.Handler;
import android.os.Looper;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

/**
 * Small draggable floating widget that appears when debug mode is active.
 * Shows a blinking recording indicator plus Report and Tools buttons. Can be
 * dragged anywhere on screen.
 *
 * Added to the Unity Activity's content view as a window overlay — no extra
 * permissions needed (it's inside our own Activity, not a system overlay).
 */
public class BugpunchDebugWidget {
    private static final String TAG = "[Bugpunch.DebugWidget]";
    // Hardcoded fallbacks live here so the widget still renders if the theme
    // dictionary somehow didn't apply at startup. Live values come from
    // BugpunchTheme — game-customisable via BugpunchConfig.Theme.
    private static final int COL_BG_FALLBACK     = 0xE0141820;
    private static final int COL_REC_FALLBACK    = 0xFFE03030;
    private static final int COL_DIM_FALLBACK    = 0xFF8C90A0;
    private static final int COL_TOOLS_FALLBACK  = 0xFF333849;
    private static final int COL_BORDER_FALLBACK = 0xFF2A3240;

    private static FrameLayout sWidget;
    private static View sRecDot;
    private static Handler sHandler;
    private static boolean sShowing;

    /**
     * Show the floating debug widget. Call after recording consent is granted.
     */
    public static void show() {
        Activity activity = BugpunchUnity.currentActivity();
        if (activity == null || sShowing) return;
        sShowing = true;
        sHandler = new Handler(Looper.getMainLooper());

        activity.runOnUiThread(() -> {
            ViewGroup root = (ViewGroup) activity.getWindow().getDecorView();

            int dp8 = dp(activity, 8);
            int dp12 = dp(activity, 12);
            int dp16 = dp(activity, 16);

            // Container
            sWidget = new FrameLayout(activity);
            FrameLayout.LayoutParams widgetLp = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            widgetLp.gravity = Gravity.TOP | Gravity.START;
            widgetLp.leftMargin = dp(activity, 16);
            widgetLp.topMargin = dp(activity, 80);

            int colBg     = BugpunchTheme.color("cardBackground", COL_BG_FALLBACK);
            int colRec    = BugpunchTheme.color("accentRecord",  COL_REC_FALLBACK);
            int colDim    = BugpunchTheme.color("textMuted",     COL_DIM_FALLBACK);
            int colTools  = BugpunchTheme.color("cardBorder",    COL_TOOLS_FALLBACK);
            int colBorder = BugpunchTheme.color("cardBorder",    COL_BORDER_FALLBACK);
            int colText   = BugpunchTheme.color("textPrimary",   Color.WHITE);
            int colBug    = BugpunchTheme.color("accentBug",     0xFFDA3838);
            int radius    = BugpunchTheme.dp(activity, "cardRadius", 12);

            // Inner row
            LinearLayout row = new LinearLayout(activity);
            row.setOrientation(LinearLayout.HORIZONTAL);
            row.setGravity(Gravity.CENTER_VERTICAL);
            row.setPadding(dp12, dp8, dp12, dp8);
            GradientDrawable bg = new GradientDrawable();
            bg.setColor(colBg);
            bg.setCornerRadius(dp(activity, 20));
            bg.setStroke(dp(activity, 1), colBorder);
            row.setBackground(bg);
            row.setElevation(dp(activity, 6));

            // Recording dot
            sRecDot = new View(activity);
            GradientDrawable dotBg = new GradientDrawable();
            dotBg.setShape(GradientDrawable.OVAL);
            dotBg.setColor(colRec);
            sRecDot.setBackground(dotBg);
            int dotSize = dp(activity, 10);
            LinearLayout.LayoutParams dotLp = new LinearLayout.LayoutParams(dotSize, dotSize);
            dotLp.rightMargin = dp8;
            row.addView(sRecDot, dotLp);

            // Report button
            TextView reportBtn = new TextView(activity);
            reportBtn.setText(BugpunchStrings.text("widgetReport", "Report"));
            reportBtn.setTextColor(colText);
            BugpunchTheme.applyTextSize(reportBtn, "fontSizeBody", 13);
            reportBtn.setPadding(dp12, dp(activity, 4), dp12, dp(activity, 4));
            GradientDrawable reportBg = new GradientDrawable();
            reportBg.setColor(colBug);
            reportBg.setCornerRadius(radius);
            reportBtn.setBackground(reportBg);
            reportBtn.setOnClickListener(v -> {
                BugpunchReportingService.reportBug("bug", "Bug report", "Triggered from debug widget", null);
            });
            LinearLayout.LayoutParams reportLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            reportLp.rightMargin = dp(activity, 6);
            row.addView(reportBtn, reportLp);

            // Tools button — toolbox icon drawn via BugpunchToolsActivity.FeatherIcon
            ImageView toolsBtn = new ImageView(activity);
            toolsBtn.setImageDrawable(new BugpunchToolsActivity.FeatherIcon(activity, "toolbox", colDim));
            toolsBtn.setPadding(dp(activity, 6), dp(activity, 4), dp(activity, 6), dp(activity, 4));
            toolsBtn.setScaleType(ImageView.ScaleType.CENTER_INSIDE);
            GradientDrawable toolsBg = new GradientDrawable();
            toolsBg.setColor(colTools);
            toolsBg.setCornerRadius(radius);
            toolsBtn.setBackground(toolsBg);
            toolsBtn.setOnClickListener(v -> {
                BugpunchUnity.sendMessage("BugpunchToolsBridge", "OnShowTools", "");
            });
            row.addView(toolsBtn);

            sWidget.addView(row);

            // Drag handling
            row.setOnTouchListener(new DragTouchListener(widgetLp, root));

            root.addView(sWidget, widgetLp);

            // Blink the recording dot
            startRecDotBlink();
        });
    }

    /** Hide and remove the widget. */
    public static void hide() {
        if (!sShowing) return;
        sShowing = false;
        if (sHandler != null) sHandler.removeCallbacksAndMessages(null);
        Activity activity = BugpunchUnity.currentActivity();
        if (activity != null && sWidget != null) {
            activity.runOnUiThread(() -> {
                ViewGroup parent = (ViewGroup) sWidget.getParent();
                if (parent != null) parent.removeView(sWidget);
                sWidget = null;
                sRecDot = null;
            });
        }
    }

    public static boolean isShowing() { return sShowing; }

    private static void startRecDotBlink() {
        if (!sShowing || sHandler == null) return;
        sHandler.postDelayed(new Runnable() {
            @Override public void run() {
                if (!sShowing || sRecDot == null) return;
                sRecDot.setAlpha(sRecDot.getAlpha() > 0.5f ? 0.2f : 1.0f);
                sHandler.postDelayed(this, 800);
            }
        }, 800);
    }

    private static int dp(Activity a, int v) {
        return (int) TypedValue.applyDimension(
            TypedValue.COMPLEX_UNIT_DIP, v, a.getResources().getDisplayMetrics());
    }

    /**
     * Touch listener that drags the widget around the screen.
     */
    static class DragTouchListener implements View.OnTouchListener {
        private final FrameLayout.LayoutParams lp;
        private final ViewGroup root;
        private float startX, startY;
        private int origLeft, origTop;
        private boolean dragging;

        DragTouchListener(FrameLayout.LayoutParams lp, ViewGroup root) {
            this.lp = lp;
            this.root = root;
        }

        @Override
        public boolean onTouch(View v, MotionEvent e) {
            switch (e.getActionMasked()) {
                case MotionEvent.ACTION_DOWN:
                    startX = e.getRawX();
                    startY = e.getRawY();
                    origLeft = lp.leftMargin;
                    origTop = lp.topMargin;
                    dragging = false;
                    return true;
                case MotionEvent.ACTION_MOVE:
                    float dx = e.getRawX() - startX;
                    float dy = e.getRawY() - startY;
                    if (!dragging && (Math.abs(dx) > 8 || Math.abs(dy) > 8)) dragging = true;
                    if (dragging) {
                        lp.leftMargin = (int) (origLeft + dx);
                        lp.topMargin = (int) (origTop + dy);
                        // Clamp to screen
                        lp.leftMargin = Math.max(0, Math.min(lp.leftMargin, root.getWidth() - v.getWidth()));
                        lp.topMargin = Math.max(0, Math.min(lp.topMargin, root.getHeight() - v.getHeight()));
                        if (sWidget != null) sWidget.setLayoutParams(lp);
                    }
                    return true;
                case MotionEvent.ACTION_UP:
                    if (!dragging) {
                        // It was a tap, not a drag — let child click handlers fire
                        v.performClick();
                    }
                    return true;
            }
            return false;
        }
    }
}
