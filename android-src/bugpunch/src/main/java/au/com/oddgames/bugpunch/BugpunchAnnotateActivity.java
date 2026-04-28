package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.drawable.BitmapDrawable;
import android.os.Bundle;
import android.util.Log;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.ViewGroup.LayoutParams;
import android.view.WindowInsets;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;

import java.io.File;
import java.io.FileOutputStream;
import java.util.ArrayList;
import java.util.List;

/**
 * Fullscreen annotation canvas. Draws over the screenshot; on Done, writes a
 * transparent PNG with just the strokes (matches screenshot dimensions) so
 * the dashboard can overlay it as a toggleable layer.
 */
public class BugpunchAnnotateActivity extends Activity {
    private static final String TAG = "[Bugpunch.AnnotateActivity]";
    public static final String EX_SHOT = "bp_shot";

    private AnnotateView mView;
    private Bitmap mBaseShot;

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        // Pause the rolling video ring while we're annotating so the time
        // spent drawing on the screenshot doesn't push out pre-incident
        // gameplay. Released in onDestroy.
        try { BugpunchRecorder.getInstance().pauseRing(); } catch (Throwable ignore) {}
        String shotPath = getIntent().getStringExtra(EX_SHOT);
        mBaseShot = shotPath != null ? BitmapFactory.decodeFile(shotPath) : null;
        if (mBaseShot == null) { finish(); return; }

        int colSecondaryBtn = BugpunchTheme.color("cardBorder",   0xFF394048);
        int colPrimaryBtn   = BugpunchTheme.color("accentPrimary", 0xFF2A7BE0);
        int colBarBg        = BugpunchTheme.color("cardBackground", 0xCC101418);

        FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(BugpunchTheme.color("backdrop", 0xFF000000));

        mView = new AnnotateView(this, mBaseShot);
        root.addView(mView, new FrameLayout.LayoutParams(
            LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT));

        LinearLayout bar = new LinearLayout(this);
        bar.setOrientation(LinearLayout.HORIZONTAL);
        bar.setGravity(Gravity.CENTER_VERTICAL);
        int p = dp(12);
        bar.setPadding(p, p, p, p);
        bar.setBackgroundColor(colBarBg);

        Button undo = button(BugpunchStrings.text("annotateUndo", "Undo"), colSecondaryBtn);
        undo.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { mView.undo(); }
        });
        Button clear = button(BugpunchStrings.text("annotateClear", "Clear"), colSecondaryBtn);
        clear.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { mView.clearStrokes(); }
        });
        Button cancel = button(BugpunchStrings.text("annotateCancel", "Cancel"), colSecondaryBtn);
        cancel.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { setResult(RESULT_CANCELED); finish(); }
        });
        Button done = button(BugpunchStrings.text("annotateDone", "Done"), colPrimaryBtn);
        done.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { onDone(); }
        });
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
            0, LayoutParams.WRAP_CONTENT, 1);
        lp.setMarginStart(dp(4));
        lp.setMarginEnd(dp(4));
        bar.addView(undo, lp);
        bar.addView(clear, lp);
        bar.addView(cancel, lp);
        bar.addView(done, lp);

        FrameLayout.LayoutParams barLp = new FrameLayout.LayoutParams(
            LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT);
        barLp.gravity = Gravity.BOTTOM;
        root.addView(bar, barLp);

        // Pad the bottom bar above the system nav bar (Android 15+ edge-to-edge
        // ignores the .NoTitleBar theme; fitsSystemWindows alone won't help on a
        // FrameLayout). Apply system-bars insets as bottom padding once.
        final int basePadding = bar.getPaddingBottom();
        bar.setOnApplyWindowInsetsListener(new View.OnApplyWindowInsetsListener() {
            @Override public WindowInsets onApplyWindowInsets(View v, WindowInsets ins) {
                int navInset = 0;
                if (android.os.Build.VERSION.SDK_INT >= 30) {
                    navInset = ins.getInsets(WindowInsets.Type.systemBars()).bottom;
                } else {
                    navInset = ins.getSystemWindowInsetBottom();
                }
                v.setPadding(v.getPaddingLeft(), v.getPaddingTop(),
                    v.getPaddingRight(), basePadding + navInset);
                return ins;
            }
        });
        bar.requestApplyInsets();

        setContentView(root);
    }

    private void onDone() {
        try {
            Bitmap overlay = mView.exportOverlayPng(mBaseShot.getWidth(), mBaseShot.getHeight());
            File out = new File(getCacheDir(),
                "bp_annotations_" + System.nanoTime() + ".png");
            FileOutputStream fos = new FileOutputStream(out);
            overlay.compress(Bitmap.CompressFormat.PNG, 100, fos);
            fos.close();
            overlay.recycle();
            BugpunchReportActivity.sAnnotationsPath = out.getAbsolutePath();
            setResult(RESULT_OK);
        } catch (Throwable t) {
            Log.w(TAG, "export failed", t);
            setResult(RESULT_CANCELED);
        }
        finish();
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        if (mBaseShot != null && !mBaseShot.isRecycled()) mBaseShot.recycle();
        try { BugpunchRecorder.getInstance().resumeRing(); } catch (Throwable ignore) {}
    }

    private Button button(String text, int bg) {
        Button b = new Button(this);
        b.setText(text);
        b.setTextColor(BugpunchTheme.color("textPrimary", Color.WHITE));
        b.setBackgroundColor(bg);
        b.setAllCaps(false);
        return b;
    }

    private int dp(int v) {
        return (int) TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v,
            getResources().getDisplayMetrics());
    }

    // ── Drawing surface ──

    static class AnnotateView extends View {
        private final Bitmap mShot;
        private final Paint mStrokePaint;
        private final List<Path> mStrokes = new ArrayList<>();
        private Path mCurrent;
        private float mShotScale; // display → source bitmap
        private float mShotOffsetX, mShotOffsetY;

        AnnotateView(Context ctx, Bitmap shot) {
            super(ctx);
            mShot = shot;
            mStrokePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
            // Annotation ink uses the bug accent so the customer's brand
            // palette extends to the mark-up tool too. 0xFFFF2B5C is the
            // hot-pink fallback that matches the SDK's default theme.
            mStrokePaint.setColor(BugpunchTheme.color("accentBug", 0xFFFF2B5C));
            mStrokePaint.setStyle(Paint.Style.STROKE);
            mStrokePaint.setStrokeCap(Paint.Cap.ROUND);
            mStrokePaint.setStrokeJoin(Paint.Join.ROUND);
            mStrokePaint.setStrokeWidth(10f);
        }

        void undo() {
            if (!mStrokes.isEmpty()) {
                mStrokes.remove(mStrokes.size() - 1);
                invalidate();
            }
        }

        void clearStrokes() { mStrokes.clear(); invalidate(); }

        @Override
        protected void onDraw(Canvas c) {
            super.onDraw(c);
            // Fit screenshot into view.
            float vw = getWidth(), vh = getHeight();
            float iw = mShot.getWidth(), ih = mShot.getHeight();
            float scale = Math.min(vw / iw, vh / ih);
            mShotScale = scale;
            float dw = iw * scale, dh = ih * scale;
            mShotOffsetX = (vw - dw) / 2f;
            mShotOffsetY = (vh - dh) / 2f;
            android.graphics.Rect dst = new android.graphics.Rect(
                (int) mShotOffsetX, (int) mShotOffsetY,
                (int) (mShotOffsetX + dw), (int) (mShotOffsetY + dh));
            c.drawBitmap(mShot, null, dst, null);
            for (Path p : mStrokes) c.drawPath(p, mStrokePaint);
            if (mCurrent != null) c.drawPath(mCurrent, mStrokePaint);
        }

        @Override
        public boolean onTouchEvent(MotionEvent e) {
            switch (e.getAction()) {
                case MotionEvent.ACTION_DOWN:
                    mCurrent = new Path();
                    mCurrent.moveTo(e.getX(), e.getY());
                    return true;
                case MotionEvent.ACTION_MOVE:
                    if (mCurrent != null) { mCurrent.lineTo(e.getX(), e.getY()); invalidate(); }
                    return true;
                case MotionEvent.ACTION_UP:
                case MotionEvent.ACTION_CANCEL:
                    if (mCurrent != null) { mStrokes.add(mCurrent); mCurrent = null; invalidate(); }
                    return true;
            }
            return false;
        }

        /**
         * Export strokes only (no screenshot) onto a transparent bitmap of the
         * same dimensions as the source screenshot — so the dashboard can
         * overlay it 1:1.
         */
        Bitmap exportOverlayPng(int w, int h) {
            Bitmap out = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888);
            Canvas c = new Canvas(out);
            // Translate view-space strokes → bitmap-space.
            c.save();
            c.translate(-mShotOffsetX, -mShotOffsetY);
            c.scale(1f / mShotScale, 1f / mShotScale);
            Paint p = new Paint(mStrokePaint);
            // Keep line thickness visually consistent at bitmap resolution.
            p.setStrokeWidth(mStrokePaint.getStrokeWidth() / mShotScale);
            for (Path path : mStrokes) c.drawPath(path, p);
            c.restore();
            return out;
        }
    }
}
