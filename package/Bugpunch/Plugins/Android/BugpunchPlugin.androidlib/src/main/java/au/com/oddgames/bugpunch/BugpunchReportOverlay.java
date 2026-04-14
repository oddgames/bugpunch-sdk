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
import android.widget.LinearLayout;
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
    private static final String TAG = "BugpunchReport";
    private static final String CALLBACK_OBJECT = "BugpunchReportCallback";

    private static View sWelcomeView;
    private static View sRecordingView;
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
            backdrop.setBackgroundColor(Color.parseColor("#99000000"));
            backdrop.setClickable(true); // consume touches

            // Card container — centered
            LinearLayout card = new LinearLayout(ctx);
            card.setOrientation(LinearLayout.VERTICAL);
            card.setBackgroundColor(Color.parseColor("#F0222222"));
            card.setPadding(pad, pad, pad, pad);
            // Rounded corners via GradientDrawable
            android.graphics.drawable.GradientDrawable cardBg = new android.graphics.drawable.GradientDrawable();
            cardBg.setColor(Color.parseColor("#F0222222"));
            cardBg.setCornerRadius(dp(ctx, 16));
            card.setBackground(cardBg);

            int cardWidth = dp(ctx, 320);
            FrameLayout.LayoutParams cardLp = new FrameLayout.LayoutParams(cardWidth, ViewGroup.LayoutParams.WRAP_CONTENT);
            cardLp.gravity = Gravity.CENTER;
            backdrop.addView(card, cardLp);

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
            title.setText("Report a Bug");
            title.setTextColor(Color.WHITE);
            title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 20);
            title.setTypeface(Typeface.DEFAULT_BOLD);
            title.setGravity(Gravity.CENTER);
            card.addView(title);

            addSpacer(card, dp(ctx, 8));

            // Body text
            TextView body = new TextView(ctx);
            body.setText("We'll record your screen while you reproduce the issue.\n\nWhen you're ready, tap the report button to send us the details.");
            body.setTextColor(Color.parseColor("#BBBBBB"));
            body.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
            body.setGravity(Gravity.CENTER);
            body.setLineSpacing(dp(ctx, 2), 1f);
            card.addView(body);

            addSpacer(card, pad);

            // "Got it" button
            Button gotItBtn = new Button(ctx);
            gotItBtn.setText("Got it");
            gotItBtn.setTextColor(Color.WHITE);
            gotItBtn.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16);
            gotItBtn.setTypeface(Typeface.DEFAULT_BOLD);
            gotItBtn.setAllCaps(false);
            android.graphics.drawable.GradientDrawable btnBg = new android.graphics.drawable.GradientDrawable();
            btnBg.setColor(Color.parseColor("#2E7D32")); // green
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
            cancel.setText("Cancel");
            cancel.setTextColor(Color.parseColor("#888888"));
            cancel.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
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

    // ─── Recording Overlay (floating button) ───────────────────────

    /**
     * Show the floating recording button. Stays visible until hideRecordingOverlay
     * is called or the app is destroyed.
     * Tap → UnitySendMessage(CALLBACK_OBJECT, "OnReportTapped", "")
     */
    public static void showRecordingOverlay(final Activity activity) {
        activity.runOnUiThread(() -> {
            if (sRecordingView != null) return;

            Context ctx = activity;
            int btnSize = dp(ctx, 56);
            int margin = dp(ctx, 16);

            // Container for button + timer
            LinearLayout container = new LinearLayout(ctx);
            container.setOrientation(LinearLayout.VERTICAL);
            container.setGravity(Gravity.CENTER_HORIZONTAL);

            // Red circle button with white square stop icon
            View button = new RecordButton(ctx, btnSize);
            container.addView(button, new LinearLayout.LayoutParams(btnSize, btnSize));

            // Timer label
            sTimerLabel = new TextView(ctx);
            sTimerLabel.setText("0:00");
            sTimerLabel.setTextColor(Color.WHITE);
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

            // Drag + tap handling
            container.setOnTouchListener(new DragTouchListener(wm, wlp, () -> {
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
            // Red circle
            paint.setStyle(Paint.Style.FILL);
            paint.setColor(Color.parseColor("#D32F2F"));
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
        private final Runnable onTap;
        private int startX, startY;
        private float touchStartX, touchStartY;
        private boolean isDragging;

        DragTouchListener(WindowManager wm, WindowManager.LayoutParams lp, Runnable onTap) {
            this.wm = wm;
            this.lp = lp;
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
                        try { wm.updateViewLayout(v, lp); } catch (Exception ignored) {}
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
