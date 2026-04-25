package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.content.res.Configuration;
import android.graphics.BitmapFactory;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.BitmapDrawable;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.RippleDrawable;
import android.content.res.ColorStateList;
import android.os.Bundle;
import android.text.InputType;
import android.util.Log;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup.LayoutParams;
import android.view.WindowInsets;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;
import android.widget.Toast;

/**
 * Bug report form. Shows a screenshot preview (tap to annotate), email +
 * description + severity inputs, Send/Cancel. On Send, calls
 * {@link BugpunchReportingService#submitReport} which builds the manifest and hands
 * to the uploader.
 *
 * Launched via {@link #launch(String, String)} with the pre-captured
 * screenshot path; lives entirely native (no Unity involvement).
 */
public class BugpunchReportActivity extends Activity {
    private static final String TAG = "[Bugpunch.ReportActivity]";
    private static final int REQ_ANNOTATE = 4201;

    // ── Palette (resolved from BugpunchTheme at form-build time). ──
    // Hardcoded fallbacks match the original SDK look so the form still
    // renders if the theme dictionary somehow wasn't applied at startup.
    private int COLOR_BG;
    private int COLOR_SURFACE;
    private int COLOR_SURFACE_ALT;
    private int COLOR_BORDER;
    private int COLOR_TEXT;
    private int COLOR_TEXT_DIM;
    private int COLOR_TEXT_LABEL;
    private int COLOR_ACCENT;
    private int COLOR_ACCENT_DARK;

    private void applyTheme() {
        COLOR_BG          = BugpunchTheme.color("backdrop",       0xFF0B0D10);
        COLOR_SURFACE     = BugpunchTheme.color("cardBackground", 0xFF171B20);
        COLOR_SURFACE_ALT = BugpunchTheme.color("cardBorder",     0xFF1E242B);
        COLOR_BORDER      = BugpunchTheme.color("cardBorder",     0xFF2A3139);
        COLOR_TEXT        = BugpunchTheme.color("textPrimary",    0xFFF1F4F7);
        COLOR_TEXT_DIM    = BugpunchTheme.color("textMuted",      0xFF8B95A2);
        COLOR_TEXT_LABEL  = BugpunchTheme.color("textSecondary",  0xFFB8C2CF);
        COLOR_ACCENT      = BugpunchTheme.color("accentPrimary",  0xFF3B82F6);
        COLOR_ACCENT_DARK = BugpunchTheme.color("accentChat",     0xFF2563EB);
    }

    private static final String EX_SHOT = "bp_shot";
    private static final String EX_TITLE = "bp_title";
    private static final String EX_DESC = "bp_desc";

    static String sAnnotationsPath;        // filled by annotate activity on return
    private static final String EX_EXTRA_SHOTS = "bp_extra_shots";

    private String mShotPath;
    private String mAnnotationsPath;
    private ImageView mPreview;
    private EditText mEmail, mDescription;
    private Spinner mSeverity;
    private String mInitialDescription;
    private String mInitialTitle;
    private java.util.ArrayList<String> mExtraShots = new java.util.ArrayList<>();
    private LinearLayout mThumbStrip;
    private TextView mThumbCountLabel;
    private boolean mThumbExpanded = false;

    public static void launch(String screenshotPath, String title, String description) {
        launch(screenshotPath, title, description, null);
    }

    public static void launch(String screenshotPath, String title, String description,
                              String[] extraScreenshots) {
        Activity host = BugpunchUnity.currentActivity();
        if (host == null) { Log.w(TAG, "no activity"); return; }
        Intent i = new Intent(host, BugpunchReportActivity.class);
        i.putExtra(EX_SHOT, screenshotPath);
        i.putExtra(EX_TITLE, title == null ? "" : title);
        i.putExtra(EX_DESC, description == null ? "" : description);
        if (extraScreenshots != null && extraScreenshots.length > 0) {
            i.putExtra(EX_EXTRA_SHOTS, extraScreenshots);
        }
        i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        host.startActivity(i);
    }

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        mShotPath = getIntent().getStringExtra(EX_SHOT);
        mInitialTitle = getIntent().getStringExtra(EX_TITLE);
        mInitialDescription = getIntent().getStringExtra(EX_DESC);
        String[] extras = getIntent().getStringArrayExtra(EX_EXTRA_SHOTS);
        if (extras != null) {
            for (String p : extras) {
                if (p != null && !p.isEmpty() && new java.io.File(p).exists())
                    mExtraShots.add(p);
            }
        }

        buildUi();
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == REQ_ANNOTATE && resultCode == RESULT_OK) {
            mAnnotationsPath = sAnnotationsPath;
            sAnnotationsPath = null;
            refreshPreview();
        }
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        // Release the "report in progress" guard so the next Send Report works.
        BugpunchRuntime.clearReportInProgress();
    }

    @Override
    public void onConfigurationChanged(Configuration newConfig) {
        super.onConfigurationChanged(newConfig);
        // Activity has android:configChanges="orientation|screenSize" so the OS
        // won't recreate us. Snapshot the form values, rebuild for the new
        // orientation, restore them.
        String email = mEmail != null ? mEmail.getText().toString() : null;
        String desc = mDescription != null ? mDescription.getText().toString() : null;
        int sev = mSeverity != null ? mSeverity.getSelectedItemPosition() : 1;
        buildUi();
        if (email != null) mEmail.setText(email);
        if (desc != null) mDescription.setText(desc);
        mSeverity.setSelection(sev);
    }

    private boolean isLandscape() {
        return getResources().getConfiguration().orientation
            == Configuration.ORIENTATION_LANDSCAPE;
    }

    private void buildUi() {
        applyTheme();

        final boolean landscape = isLandscape();
        final int pad = dp(20);

        // Header: title + accent underline so the form has a visual anchor.
        LinearLayout headerBlock = new LinearLayout(this);
        headerBlock.setOrientation(LinearLayout.VERTICAL);
        TextView eyebrow = new TextView(this);
        eyebrow.setText(BugpunchStrings.text("reportFormEyebrow", "BUG REPORT"));
        eyebrow.setTextColor(COLOR_ACCENT);
        eyebrow.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11);
        eyebrow.setLetterSpacing(0.2f);
        eyebrow.setTypeface(Typeface.DEFAULT_BOLD);
        headerBlock.addView(eyebrow);
        TextView header = new TextView(this);
        header.setText(BugpunchStrings.text("reportFormHeader", "Tell us what happened"));
        header.setTextColor(COLOR_TEXT);
        header.setTextSize(TypedValue.COMPLEX_UNIT_SP, 22);
        header.setTypeface(Typeface.DEFAULT_BOLD);
        header.setPadding(0, dp(4), 0, dp(6));
        headerBlock.addView(header);
        View accentBar = new View(this);
        GradientDrawable bar = new GradientDrawable(
            GradientDrawable.Orientation.LEFT_RIGHT,
            new int[] { COLOR_ACCENT, COLOR_ACCENT_DARK });
        bar.setCornerRadius(dp(2));
        accentBar.setBackground(bar);
        LinearLayout.LayoutParams lpBar = new LinearLayout.LayoutParams(dp(32), dp(3));
        lpBar.bottomMargin = dp(18);
        headerBlock.addView(accentBar, lpBar);

        // Screenshot preview — tap to annotate. Framed in a rounded card.
        FrameLayout previewCard = new FrameLayout(this);
        previewCard.setBackground(surface(COLOR_SURFACE_ALT, dp(12), COLOR_BORDER));
        previewCard.setElevation(dp(4));
        previewCard.setClipToPadding(false);

        mPreview = new ImageView(this);
        mPreview.setAdjustViewBounds(true);
        mPreview.setScaleType(ImageView.ScaleType.FIT_CENTER);
        mPreview.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { openAnnotate(); }
        });
        FrameLayout.LayoutParams lpPrevInner = new FrameLayout.LayoutParams(
            LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT);
        int previewPad = dp(4);
        lpPrevInner.setMargins(previewPad, previewPad, previewPad, previewPad);
        previewCard.addView(mPreview, lpPrevInner);

        // Tap-to-annotate pill floats bottom-right on top of the screenshot.
        TextView pill = new TextView(this);
        pill.setText("\u270E  " + BugpunchStrings.text("reportFormTapToAnnotate", "Tap to annotate"));
        pill.setTextColor(COLOR_TEXT);
        pill.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11);
        pill.setTypeface(Typeface.DEFAULT_BOLD);
        pill.setPadding(dp(10), dp(5), dp(10), dp(5));
        GradientDrawable pillBg = new GradientDrawable();
        pillBg.setShape(GradientDrawable.RECTANGLE);
        pillBg.setCornerRadius(dp(999));
        // Pill backdrop reuses the app backdrop hue at high alpha so it
        // reads against any screenshot regardless of palette tweaks.
        pillBg.setColor((COLOR_BG & 0x00FFFFFF) | 0xCC000000);
        pillBg.setStroke(dp(1), (COLOR_BORDER & 0x00FFFFFF) | 0x33000000);
        pill.setBackground(pillBg);
        FrameLayout.LayoutParams lpPill = new FrameLayout.LayoutParams(
            LayoutParams.WRAP_CONTENT, LayoutParams.WRAP_CONTENT);
        lpPill.gravity = Gravity.END | Gravity.BOTTOM;
        lpPill.setMargins(0, 0, dp(10), dp(10));
        previewCard.addView(pill, lpPill);

        // ── Extra screenshots thumbnail strip ──
        // Horizontal scroll of dismissable thumbnails for manual screenshots
        // captured via the debug widget. Shown only when there are extras.
        View thumbSection = buildThumbStrip();

        TextView emailLabel = label(BugpunchStrings.text("reportFormEmailLabel", "Your email"));
        mEmail = input(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS);
        mEmail.setHint(BugpunchStrings.text("reportFormEmailHint", "you@studio.com"));
        mEmail.setHintTextColor(COLOR_TEXT_DIM);

        TextView descLabel = label(BugpunchStrings.text("reportFormDescField", "Description"));
        mDescription = input(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_MULTI_LINE);
        mDescription.setMinLines(4);
        mDescription.setGravity(Gravity.TOP | Gravity.START);
        mDescription.setHint(BugpunchStrings.text("reportFormDescPlaceholder",
            "What went wrong? Steps to reproduce?"));
        mDescription.setHintTextColor(COLOR_TEXT_DIM);
        if (mInitialDescription != null) mDescription.setText(mInitialDescription);

        TextView sevLabel = label(BugpunchStrings.text("reportFormSeverityLabel", "Severity"));
        mSeverity = new Spinner(this);
        ArrayAdapter<String> adapter = new ArrayAdapter<>(this,
            android.R.layout.simple_spinner_item,
            new String[] {
                BugpunchStrings.text("reportFormSeverityLow",      "Low"),
                BugpunchStrings.text("reportFormSeverityMedium",   "Medium"),
                BugpunchStrings.text("reportFormSeverityHigh",     "High"),
                BugpunchStrings.text("reportFormSeverityCritical", "Critical"),
            }) {
            @Override public View getView(int pos, View convert, android.view.ViewGroup p) {
                TextView v = (TextView) super.getView(pos, convert, p);
                v.setTextColor(COLOR_TEXT);
                v.setPadding(dp(12), dp(10), dp(12), dp(10));
                return v;
            }
        };
        adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        mSeverity.setAdapter(adapter);
        mSeverity.setBackground(surface(COLOR_SURFACE, dp(10), COLOR_BORDER));
        mSeverity.setSelection(1);

        // Sticky footer — sits outside the ScrollView so Cancel/Send stay
        // visible no matter how tall the form grows or whether the IME is up
        // (Theme.Translucent disables adjustResize, so we can't rely on it).
        LinearLayout buttons = new LinearLayout(this);
        buttons.setOrientation(LinearLayout.HORIZONTAL);
        buttons.setGravity(Gravity.END | Gravity.CENTER_VERTICAL);
        buttons.setPadding(pad, dp(12), pad, dp(12));
        buttons.setBackgroundColor(COLOR_BG);
        // Top hairline so the footer reads as a separate band from the form.
        View footerDivider = new View(this);
        footerDivider.setBackgroundColor(COLOR_BORDER);
        Button cancel = secondaryButton(BugpunchStrings.text("reportFormCancel", "Cancel"));
        cancel.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { finish(); }
        });
        LinearLayout.LayoutParams lpBtn = new LinearLayout.LayoutParams(
            0, dp(46), 1f);
        lpBtn.rightMargin = dp(12);
        buttons.addView(cancel, lpBtn);
        Button send = primaryButton(BugpunchStrings.text("reportFormSendButton", "Send report"));
        send.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { onSend(); }
        });
        LinearLayout.LayoutParams lpSend = new LinearLayout.LayoutParams(
            0, dp(46), 1f);
        buttons.addView(send, lpSend);

        // Outer container: scrolling form on top, sticky button footer on
        // bottom. The footer is a sibling of the scroll view so it never
        // scrolls off-screen and stays visible with the IME up.
        LinearLayout shell = new LinearLayout(this);
        shell.setOrientation(LinearLayout.VERTICAL);
        shell.setBackgroundColor(COLOR_BG);

        final View content = shell;
        final View insetsTarget = shell;

        if (landscape) {
            // Two columns: screenshot preview on the left, scrollable form on
            // the right. In landscape the screen is short, so the preview
            // flexes with the viewport instead of eating a fixed 260dp.
            LinearLayout row = new LinearLayout(this);
            row.setOrientation(LinearLayout.HORIZONTAL);
            row.setBackgroundColor(COLOR_BG);
            row.setPadding(pad, pad, pad, 0);

            LinearLayout left = new LinearLayout(this);
            left.setOrientation(LinearLayout.VERTICAL);
            LinearLayout.LayoutParams lpPrevRow = new LinearLayout.LayoutParams(
                LayoutParams.MATCH_PARENT, 0, 1f);
            left.addView(previewCard, lpPrevRow);
            LinearLayout.LayoutParams lpLeft = new LinearLayout.LayoutParams(
                0, LayoutParams.MATCH_PARENT, 1f);
            lpLeft.rightMargin = dp(20);
            row.addView(left, lpLeft);

            ScrollView formScroll = new ScrollView(this);
            formScroll.setClipToPadding(false);
            LinearLayout form = new LinearLayout(this);
            form.setOrientation(LinearLayout.VERTICAL);
            form.addView(headerBlock);
            if (thumbSection != null) form.addView(thumbSection);
            form.addView(emailLabel);
            form.addView(mEmail);
            form.addView(descLabel);
            form.addView(mDescription);
            form.addView(sevLabel);
            form.addView(mSeverity);
            formScroll.addView(form);
            LinearLayout.LayoutParams lpRight = new LinearLayout.LayoutParams(
                0, LayoutParams.MATCH_PARENT, 1f);
            row.addView(formScroll, lpRight);

            shell.addView(row, new LinearLayout.LayoutParams(
                LayoutParams.MATCH_PARENT, 0, 1f));
        } else {
            final ScrollView scroll = new ScrollView(this);
            scroll.setBackgroundColor(COLOR_BG);
            scroll.setClipToPadding(false);
            LinearLayout root = new LinearLayout(this);
            root.setOrientation(LinearLayout.VERTICAL);
            root.setPadding(pad, pad, pad, dp(12));
            root.setBackgroundColor(COLOR_BG);
            root.addView(headerBlock);
            LinearLayout.LayoutParams lpShot = new LinearLayout.LayoutParams(
                LayoutParams.MATCH_PARENT, dp(240));
            lpShot.bottomMargin = dp(20);
            root.addView(previewCard, lpShot);
            if (thumbSection != null) root.addView(thumbSection);
            root.addView(emailLabel);
            root.addView(mEmail);
            root.addView(descLabel);
            root.addView(mDescription);
            root.addView(sevLabel);
            root.addView(mSeverity);
            scroll.addView(root);
            shell.addView(scroll, new LinearLayout.LayoutParams(
                LayoutParams.MATCH_PARENT, 0, 1f));
        }

        shell.addView(footerDivider, new LinearLayout.LayoutParams(
            LayoutParams.MATCH_PARENT, Math.max(1, dp(1) / 2)));
        shell.addView(buttons, new LinearLayout.LayoutParams(
            LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT));
        shell.addView(buildDeviceIdFooter(), new LinearLayout.LayoutParams(
            LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT));

        // Absorb status/nav bar insets on the shell so content and the sticky
        // footer both clear the system bars. Inner views supply their own
        // content padding, so we only add the inset amount here.
        insetsTarget.setOnApplyWindowInsetsListener(new View.OnApplyWindowInsetsListener() {
            @Override public WindowInsets onApplyWindowInsets(View v, WindowInsets ins) {
                int top = 0, bottom = 0, left = 0, right = 0;
                if (android.os.Build.VERSION.SDK_INT >= 30) {
                    android.graphics.Insets sb = ins.getInsets(WindowInsets.Type.systemBars());
                    top = sb.top; bottom = sb.bottom; left = sb.left; right = sb.right;
                } else {
                    top = ins.getSystemWindowInsetTop();
                    bottom = ins.getSystemWindowInsetBottom();
                    left = ins.getSystemWindowInsetLeft();
                    right = ins.getSystemWindowInsetRight();
                }
                v.setPadding(left, top, right, bottom);
                return ins;
            }
        });
        content.requestApplyInsets();

        setContentView(content);
        refreshPreview();
    }

    private void refreshPreview() {
        try {
            if (mShotPath == null) return;
            android.graphics.Bitmap shot = BitmapFactory.decodeFile(mShotPath);
            if (shot == null) return;
            if (mAnnotationsPath != null) {
                // Composite annotation overlay on top of screenshot for preview.
                android.graphics.Bitmap overlay = BitmapFactory.decodeFile(mAnnotationsPath);
                if (overlay != null) {
                    android.graphics.Bitmap combined = shot.copy(
                        android.graphics.Bitmap.Config.ARGB_8888, true);
                    android.graphics.Canvas c = new android.graphics.Canvas(combined);
                    android.graphics.Rect dst = new android.graphics.Rect(
                        0, 0, combined.getWidth(), combined.getHeight());
                    c.drawBitmap(overlay, null, dst, null);
                    overlay.recycle();
                    mPreview.setImageBitmap(combined);
                    return;
                }
            }
            mPreview.setImageBitmap(shot);
        } catch (Throwable t) {
            Log.w(TAG, "preview failed", t);
        }
    }

    private void openAnnotate() {
        if (mShotPath == null) return;
        Intent i = new Intent(this, BugpunchAnnotateActivity.class);
        i.putExtra(BugpunchAnnotateActivity.EX_SHOT, mShotPath);
        startActivityForResult(i, REQ_ANNOTATE);
    }

    private void onSend() {
        String email = mEmail.getText().toString().trim();
        String desc = mDescription.getText().toString().trim();
        // Map the localised severity label back to the canonical English
        // severity string the server expects. Default to "medium".
        String severityCanonical = severityCanonicalForIndex(mSeverity.getSelectedItemPosition());
        if (desc.isEmpty()) {
            Toast.makeText(this,
                BugpunchStrings.text("reportFormDescRequired", "Please add a description"),
                Toast.LENGTH_SHORT).show();
            return;
        }
        try {
            String[] extras = mExtraShots.isEmpty() ? null : mExtraShots.toArray(new String[0]);
            BugpunchReportingService.submitReport(
                mInitialTitle != null ? mInitialTitle : "Bug report",
                desc, email, severityCanonical, mShotPath, mAnnotationsPath, extras);
            Toast.makeText(this,
                BugpunchStrings.text("reportFormSent", "Report sent"),
                Toast.LENGTH_SHORT).show();
        } catch (Throwable t) {
            Log.w(TAG, "submit failed", t);
            Toast.makeText(this,
                BugpunchStrings.text("reportFormFailed", "Failed to send report"),
                Toast.LENGTH_SHORT).show();
        }
        finish();
    }

    private static String severityCanonicalForIndex(int idx) {
        switch (idx) {
            case 0:  return "low";
            case 2:  return "high";
            case 3:  return "critical";
            default: return "medium";
        }
    }

    // ── Screenshot thumbnail strip ──

    private View buildThumbStrip() {
        if (mExtraShots.isEmpty()) return null;

        LinearLayout wrapper = new LinearLayout(this);
        wrapper.setOrientation(LinearLayout.VERTICAL);
        LinearLayout.LayoutParams wrapLp = new LinearLayout.LayoutParams(
            LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT);
        wrapLp.topMargin = dp(8);
        wrapLp.bottomMargin = dp(4);
        wrapper.setLayoutParams(wrapLp);

        // Header row: label + count
        mThumbCountLabel = new TextView(this);
        mThumbCountLabel.setTextColor(COLOR_TEXT_LABEL);
        mThumbCountLabel.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11);
        mThumbCountLabel.setLetterSpacing(0.12f);
        mThumbCountLabel.setTypeface(Typeface.DEFAULT_BOLD);
        mThumbCountLabel.setPadding(0, 0, 0, dp(6));
        wrapper.addView(mThumbCountLabel);

        // Scrollable thumbnail row
        android.widget.HorizontalScrollView scroll = new android.widget.HorizontalScrollView(this);
        scroll.setHorizontalScrollBarEnabled(false);
        mThumbStrip = new LinearLayout(this);
        mThumbStrip.setOrientation(LinearLayout.HORIZONTAL);
        scroll.addView(mThumbStrip);
        wrapper.addView(scroll);

        rebuildThumbs();
        return wrapper;
    }

    private void rebuildThumbs() {
        if (mThumbStrip == null) return;
        mThumbStrip.removeAllViews();
        mThumbCountLabel.setText(BugpunchStrings.text("reportFormScreenshotsLabel", "SCREENSHOTS")
            + " (" + mExtraShots.size() + ")");

        int thumbH = dp(64);
        for (int idx = 0; idx < mExtraShots.size(); idx++) {
            String path = mExtraShots.get(idx);
            final int capturedIdx = idx;

            FrameLayout frame = new FrameLayout(this);
            LinearLayout.LayoutParams frameLp = new LinearLayout.LayoutParams(
                LayoutParams.WRAP_CONTENT, thumbH);
            frameLp.rightMargin = dp(6);
            frame.setLayoutParams(frameLp);

            // Thumbnail image
            ImageView thumb = new ImageView(this);
            thumb.setScaleType(ImageView.ScaleType.CENTER_CROP);
            thumb.setAdjustViewBounds(false);
            try {
                android.graphics.BitmapFactory.Options opts = new android.graphics.BitmapFactory.Options();
                opts.inSampleSize = 4; // quarter resolution for thumbnails
                android.graphics.Bitmap bmp = android.graphics.BitmapFactory.decodeFile(path, opts);
                if (bmp != null) thumb.setImageBitmap(bmp);
            } catch (Throwable ignored) {}
            FrameLayout.LayoutParams thumbLp = new FrameLayout.LayoutParams(
                LayoutParams.WRAP_CONTENT, LayoutParams.MATCH_PARENT);
            thumb.setBackground(surface(COLOR_SURFACE_ALT, dp(8), COLOR_BORDER));
            thumb.setClipToOutline(true);
            frame.addView(thumb, thumbLp);

            // Dismiss X button
            TextView dismiss = new TextView(this);
            dismiss.setText("✕");
            dismiss.setTextColor(Color.WHITE);
            dismiss.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11);
            dismiss.setGravity(Gravity.CENTER);
            GradientDrawable xBg = new GradientDrawable();
            xBg.setShape(GradientDrawable.OVAL);
            xBg.setColor(0xCC000000);
            dismiss.setBackground(xBg);
            FrameLayout.LayoutParams xLp = new FrameLayout.LayoutParams(dp(20), dp(20));
            xLp.gravity = Gravity.TOP | Gravity.END;
            xLp.setMargins(0, dp(2), dp(2), 0);
            dismiss.setOnClickListener(v -> {
                mExtraShots.remove(capturedIdx);
                rebuildThumbs();
            });
            frame.addView(dismiss, xLp);

            mThumbStrip.addView(frame);
        }
    }

    // ── Small builders to keep this file terse ──

    /** Compact tap-to-copy device ID row shown below the sticky button bar
     *  so QA can read the ID without diving into settings. Same shape as the
     *  footer on BugpunchCrashActivity. */
    private TextView buildDeviceIdFooter() {
        final String id = BugpunchIdentity.getStableDeviceId(this);
        TextView tv = new TextView(this);
        tv.setText("ID: " + id);
        tv.setTextColor(COLOR_TEXT_LABEL);
        tv.setTextSize(TypedValue.COMPLEX_UNIT_SP, 10);
        tv.setTypeface(Typeface.MONOSPACE);
        tv.setGravity(Gravity.CENTER);
        tv.setPadding(dp(12), dp(8), dp(12), dp(10));
        tv.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                android.content.ClipboardManager cm =
                    (android.content.ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
                if (cm != null) {
                    cm.setPrimaryClip(android.content.ClipData.newPlainText("device ID", id));
                    android.widget.Toast.makeText(BugpunchReportActivity.this,
                        BugpunchStrings.text("reportFormDeviceIdCopied", "Device ID copied"),
                        android.widget.Toast.LENGTH_SHORT).show();
                }
            }
        });
        return tv;
    }

    private TextView label(String text) {
        TextView t = new TextView(this);
        t.setText(text.toUpperCase());
        t.setTextColor(COLOR_TEXT_LABEL);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11);
        t.setLetterSpacing(0.12f);
        t.setTypeface(Typeface.DEFAULT_BOLD);
        t.setPadding(0, dp(16), 0, dp(6));
        return t;
    }

    private EditText input(int inputType) {
        EditText e = new EditText(this);
        e.setInputType(inputType);
        e.setBackground(surface(COLOR_SURFACE, dp(10), COLOR_BORDER));
        e.setTextColor(COLOR_TEXT);
        e.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
        e.setPadding(dp(14), dp(12), dp(14), dp(12));
        e.setElevation(dp(1));
        return e;
    }

    /// Rounded, bordered background used for inputs, cards, and the spinner.
    private GradientDrawable surface(int fill, int radius, int border) {
        GradientDrawable g = new GradientDrawable();
        g.setShape(GradientDrawable.RECTANGLE);
        g.setColor(fill);
        g.setCornerRadius(radius);
        g.setStroke(dp(1), border);
        return g;
    }

    /// Primary action — filled accent, subtle shadow, ripple on press.
    private Button primaryButton(String text) {
        Button b = new Button(this);
        b.setText(text);
        b.setTextColor(Color.WHITE);
        b.setAllCaps(false);
        b.setTypeface(Typeface.DEFAULT_BOLD);
        b.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        b.setPadding(dp(22), 0, dp(22), 0);
        GradientDrawable fill = new GradientDrawable(
            GradientDrawable.Orientation.TOP_BOTTOM,
            new int[] { COLOR_ACCENT, COLOR_ACCENT_DARK });
        fill.setCornerRadius(dp(10));
        b.setBackground(new RippleDrawable(
            ColorStateList.valueOf(0x33FFFFFF), fill, null));
        b.setElevation(dp(6));
        return b;
    }

    /// Secondary action — bordered, transparent fill.
    private Button secondaryButton(String text) {
        Button b = new Button(this);
        b.setText(text);
        b.setTextColor(COLOR_TEXT);
        b.setAllCaps(false);
        b.setTypeface(Typeface.DEFAULT_BOLD);
        b.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        b.setPadding(dp(22), 0, dp(22), 0);
        GradientDrawable fill = surface(COLOR_SURFACE, dp(10), COLOR_BORDER);
        b.setBackground(new RippleDrawable(
            ColorStateList.valueOf(0x22FFFFFF), fill, null));
        b.setElevation(dp(2));
        return b;
    }

    private int dp(int v) {
        return (int) TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v,
            getResources().getDisplayMetrics());
    }
}
