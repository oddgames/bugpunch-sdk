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
 * Shows a blinking recording indicator plus Report / Screenshot / Tools
 * buttons. Can be dragged anywhere on screen.
 *
 * Added to the Unity Activity's content view as a window overlay — no extra
 * permissions needed (it's inside our own Activity, not a system overlay).
 */
public class BugpunchDebugWidget {
    private static final String TAG = "[Bugpunch.DebugWidget]";
    private static final int COL_BG = 0xE0141820;
    private static final int COL_REC = 0xFFE03030;
    private static final int COL_TEXT = 0xFFE6E8EE;
    private static final int COL_DIM = 0xFF8C90A0;
    private static final int COL_TOOLS = 0xFF333849;

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

            // Inner row
            LinearLayout row = new LinearLayout(activity);
            row.setOrientation(LinearLayout.HORIZONTAL);
            row.setGravity(Gravity.CENTER_VERTICAL);
            row.setPadding(dp12, dp8, dp12, dp8);
            GradientDrawable bg = new GradientDrawable();
            bg.setColor(COL_BG);
            bg.setCornerRadius(dp(activity, 20));
            bg.setStroke(dp(activity, 1), 0xFF2A3240);
            row.setBackground(bg);
            row.setElevation(dp(activity, 6));

            // Recording dot
            sRecDot = new View(activity);
            GradientDrawable dotBg = new GradientDrawable();
            dotBg.setShape(GradientDrawable.OVAL);
            dotBg.setColor(COL_REC);
            sRecDot.setBackground(dotBg);
            int dotSize = dp(activity, 10);
            LinearLayout.LayoutParams dotLp = new LinearLayout.LayoutParams(dotSize, dotSize);
            dotLp.rightMargin = dp8;
            row.addView(sRecDot, dotLp);

            // Report button
            TextView reportBtn = new TextView(activity);
            reportBtn.setText("Report");
            reportBtn.setTextColor(Color.WHITE);
            reportBtn.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
            reportBtn.setPadding(dp12, dp(activity, 4), dp12, dp(activity, 4));
            GradientDrawable reportBg = new GradientDrawable();
            reportBg.setColor(0xFFDA3838);
            reportBg.setCornerRadius(dp(activity, 12));
            reportBtn.setBackground(reportBg);
            reportBtn.setOnClickListener(v -> {
                BugpunchReportingService.reportBug("bug", "Bug report", "Triggered from debug widget", null);
            });
            LinearLayout.LayoutParams reportLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            reportLp.rightMargin = dp(activity, 6);
            row.addView(reportBtn, reportLp);

            // Screenshot button — camera icon drawn via BugpunchToolsActivity.FeatherIcon
            ImageView shotBtn = new ImageView(activity);
            shotBtn.setImageDrawable(new BugpunchToolsActivity.FeatherIcon(activity, "camera", COL_DIM));
            shotBtn.setPadding(dp(activity, 6), dp(activity, 4), dp(activity, 6), dp(activity, 4));
            shotBtn.setScaleType(ImageView.ScaleType.CENTER_INSIDE);
            GradientDrawable shotBg = new GradientDrawable();
            shotBg.setColor(COL_TOOLS);
            shotBg.setCornerRadius(dp(activity, 12));
            shotBtn.setBackground(shotBg);
            shotBtn.setOnClickListener(v -> {
                // Capture from rolling buffer and notify Unity
                String path = activity.getCacheDir().getAbsolutePath()
                    + "/bp_manual_" + System.nanoTime() + ".jpg";
                boolean ok = BugpunchScreenshot.writeLastFrame(path, 85);
                if (!ok) BugpunchScreenshot.captureSync(path, 85);
                long ts = System.currentTimeMillis();
                BugpunchUnity.sendMessage("BugpunchToolsBridge", "OnManualScreenshot",
                    path + "|" + ts);
                // Flash feedback
                v.setAlpha(0.4f);
                v.animate().alpha(1f).setDuration(300).start();
            });
            LinearLayout.LayoutParams shotLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            shotLp.rightMargin = dp(activity, 6);
            row.addView(shotBtn, shotLp);

            // Tools button
            TextView toolsBtn = new TextView(activity);
            toolsBtn.setText("Tools");
            toolsBtn.setTextColor(COL_DIM);
            toolsBtn.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
            toolsBtn.setPadding(dp12, dp(activity, 4), dp12, dp(activity, 4));
            GradientDrawable toolsBg = new GradientDrawable();
            toolsBg.setColor(COL_TOOLS);
            toolsBg.setCornerRadius(dp(activity, 12));
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
