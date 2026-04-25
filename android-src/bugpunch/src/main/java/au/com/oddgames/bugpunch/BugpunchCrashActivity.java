package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.media.MediaPlayer;
import android.net.Uri;
import android.os.Bundle;
import android.text.TextUtils;
import android.util.Log;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.SurfaceHolder;
import android.view.SurfaceView;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;

import android.widget.ImageView;

import java.io.File;

/**
 * Full-screen native crash report overlay Activity.
 *
 * Launched from Unity via {@link #launch(Activity, String, String, String)}.
 * Displays the ring-buffer video (if available), exception details, and
 * input fields for the user to describe the issue. Results are sent back
 * to Unity via UnitySendMessage to the "BugpunchCrashCallback" GameObject.
 *
 * This Activity uses FLAG_ACTIVITY_NEW_TASK so it can be launched even
 * when the app is in a crashing state. It runs in the app's main process
 * (not a separate process) because it needs to communicate with Unity.
 */
public class BugpunchCrashActivity extends Activity {
    private static final String TAG = "[Bugpunch.CrashActivity]";

    private static final String EXTRA_EXCEPTION = "exception";
    private static final String EXTRA_STACK_TRACE = "stackTrace";
    private static final String EXTRA_VIDEO_PATH = "videoPath";

    private MediaPlayer mediaPlayer;
    private SurfaceView surfaceView;

    /**
     * Launch this Activity from Unity C#. Called via AndroidJavaClass.CallStatic.
     */
    public static void launch(Activity unityActivity, String exception, String stackTrace, String videoPath) {
        Intent intent = new Intent(unityActivity, BugpunchCrashActivity.class);
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        intent.putExtra(EXTRA_EXCEPTION, exception != null ? exception : "");
        intent.putExtra(EXTRA_STACK_TRACE, stackTrace != null ? stackTrace : "");
        intent.putExtra(EXTRA_VIDEO_PATH, videoPath != null ? videoPath : "");
        unityActivity.startActivity(intent);
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        // Keep screen on and make full-screen
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);

        String exceptionMsg = getIntent().getStringExtra(EXTRA_EXCEPTION);
        String stackTrace = getIntent().getStringExtra(EXTRA_STACK_TRACE);
        String videoPath = getIntent().getStringExtra(EXTRA_VIDEO_PATH);

        // Build the UI programmatically (no XML layouts needed in Unity plugin)
        setContentView(buildLayout(exceptionMsg, stackTrace, videoPath));
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        releasePlayer();
    }

    @Override
    public void onBackPressed() {
        sendDismissToUnity();
        finish();
    }

    // ─── Layout builder ─────────────────────────────────────────────

    private View buildLayout(String exceptionMsg, String stackTrace, String videoPath) {
        int pad = dp(16);
        int padSmall = dp(8);

        // Root: dark background ScrollView (uses the shared theme backdrop
        // alpha so a customised palette stays consistent across overlays).
        ScrollView scrollView = new ScrollView(this);
        scrollView.setBackgroundColor(BugpunchTheme.color("backdrop", 0xE6000000));
        scrollView.setFillViewport(true);

        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(pad, dp(24), pad, pad);
        scrollView.addView(root, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        // ── Optional brand row ──
        // SDK ships unbranded; if the host game drops a `bugpunch_logo`
        // drawable into their app's res/drawable to brand the overlay,
        // it'll be picked up here.
        int logoResId = getResources().getIdentifier("bugpunch_logo", "drawable", getPackageName());
        if (logoResId != 0) {
            ImageView logoImg = new ImageView(this);
            logoImg.setImageResource(logoResId);
            logoImg.setScaleType(ImageView.ScaleType.FIT_CENTER);
            LinearLayout.LayoutParams logoParams = new LinearLayout.LayoutParams(dp(24), dp(24));
            logoParams.gravity = Gravity.START;
            root.addView(logoImg, logoParams);
            addSpacer(root, dp(8));
        }

        // ── Header ──
        TextView header = new TextView(this);
        header.setText(BugpunchStrings.text("crashHeader", "Crash Report"));
        header.setTextColor(BugpunchTheme.color("accentBug", 0xFFFF5555));
        header.setTextSize(TypedValue.COMPLEX_UNIT_SP,
            BugpunchTheme.sp("fontSizeTitle", 20) + 2);
        header.setTypeface(Typeface.DEFAULT_BOLD);
        root.addView(header);
        addSpacer(root, dp(8));

        // ── Video player ──
        boolean hasVideo = !TextUtils.isEmpty(videoPath) && new File(videoPath).exists();
        if (hasVideo) {
            FrameLayout videoContainer = new FrameLayout(this);
            videoContainer.setBackgroundColor(Color.BLACK);

            surfaceView = new SurfaceView(this);
            videoContainer.addView(surfaceView, new FrameLayout.LayoutParams(
                    FrameLayout.LayoutParams.MATCH_PARENT, dp(200)));

            surfaceView.getHolder().addCallback(new SurfaceHolder.Callback() {
                @Override public void surfaceCreated(SurfaceHolder holder) { startVideoPlayback(videoPath, holder); }
                @Override public void surfaceChanged(SurfaceHolder h, int f, int w, int ht) {}
                @Override public void surfaceDestroyed(SurfaceHolder holder) { releasePlayer(); }
            });

            root.addView(videoContainer, new LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT, dp(200)));
            addSpacer(root, padSmall);
        }

        int colTextPrimary   = BugpunchTheme.color("textPrimary",   Color.WHITE);
        int colTextSecondary = BugpunchTheme.color("textSecondary", 0xFFAAAAAA);
        int colTextMuted     = BugpunchTheme.color("textMuted",     0xFF666666);
        int colCardBorder    = BugpunchTheme.color("cardBorder",    0xFF2A2A2A);
        int colAccentBug     = BugpunchTheme.color("accentBug",     0xFFFF8888);
        int colAccentPrimary = BugpunchTheme.color("accentPrimary", 0xFF2E7D32);

        // ── Exception message ──
        if (!TextUtils.isEmpty(exceptionMsg)) {
            TextView exLabel = new TextView(this);
            exLabel.setText(exceptionMsg);
            exLabel.setTextColor(colAccentBug);
            BugpunchTheme.applyTextSize(exLabel, "fontSizeBody", 14);
            exLabel.setTypeface(Typeface.DEFAULT_BOLD);
            exLabel.setMaxLines(3);
            exLabel.setEllipsize(TextUtils.TruncateAt.END);
            root.addView(exLabel);
            addSpacer(root, padSmall);
        }

        // ── Stack trace (scrollable) ──
        if (!TextUtils.isEmpty(stackTrace)) {
            TextView stackLabel = new TextView(this);
            stackLabel.setText(BugpunchStrings.text("crashStackTrace", "Stack Trace:"));
            stackLabel.setTextColor(colTextSecondary);
            BugpunchTheme.applyTextSize(stackLabel, "fontSizeCaption", 12);
            root.addView(stackLabel);

            ScrollView stackScroll = new ScrollView(this);
            stackScroll.setBackgroundColor(colCardBorder);
            int sp = dp(8);
            stackScroll.setPadding(sp, sp, sp, sp);

            TextView stackText = new TextView(this);
            stackText.setText(stackTrace);
            stackText.setTextColor(colTextSecondary);
            stackText.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11);
            stackText.setTypeface(Typeface.MONOSPACE);
            stackScroll.addView(stackText);

            LinearLayout.LayoutParams stackParams = new LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT, dp(140));
            root.addView(stackScroll, stackParams);
            addSpacer(root, padSmall);
        }

        // ── Title input ──
        TextView titleLabel = new TextView(this);
        titleLabel.setText(BugpunchStrings.text("crashTitleField", "Title"));
        titleLabel.setTextColor(colTextPrimary);
        root.addView(titleLabel);

        EditText titleInput = new EditText(this);
        titleInput.setHint(BugpunchStrings.text("crashTitleHint", "Brief description of the issue"));
        titleInput.setTextColor(colTextPrimary);
        titleInput.setHintTextColor(colTextMuted);
        titleInput.setBackgroundColor(colCardBorder);
        titleInput.setSingleLine(true);
        titleInput.setPadding(dp(8), dp(8), dp(8), dp(8));
        // Pre-fill with exception message truncated
        if (!TextUtils.isEmpty(exceptionMsg)) {
            String prefill = exceptionMsg.length() > 120 ? exceptionMsg.substring(0, 120) : exceptionMsg;
            titleInput.setText(prefill);
        }
        root.addView(titleInput, new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));
        addSpacer(root, padSmall);

        // ── Description input ──
        TextView descLabel = new TextView(this);
        descLabel.setText(BugpunchStrings.text("crashDescField", "Description (what were you doing?)"));
        descLabel.setTextColor(colTextPrimary);
        root.addView(descLabel);

        EditText descInput = new EditText(this);
        descInput.setHint(BugpunchStrings.text("crashDescHint", "Steps to reproduce, additional context..."));
        descInput.setTextColor(colTextPrimary);
        descInput.setHintTextColor(colTextMuted);
        descInput.setBackgroundColor(colCardBorder);
        descInput.setMinLines(3);
        descInput.setGravity(Gravity.TOP);
        descInput.setPadding(dp(8), dp(8), dp(8), dp(8));
        root.addView(descInput, new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));
        addSpacer(root, padSmall);

        // ── Severity spinner ──
        TextView sevLabel = new TextView(this);
        sevLabel.setText(BugpunchStrings.text("crashSeverity", "Severity"));
        sevLabel.setTextColor(colTextPrimary);
        root.addView(sevLabel);

        Spinner sevSpinner = new Spinner(this);
        String[] severities = {"Low", "Medium", "High", "Critical"};
        ArrayAdapter<String> adapter = new ArrayAdapter<>(this,
                android.R.layout.simple_spinner_dropdown_item, severities);
        sevSpinner.setAdapter(adapter);
        sevSpinner.setSelection(2); // Default to "High" for crashes
        sevSpinner.setBackgroundColor(colCardBorder);
        root.addView(sevSpinner);
        addSpacer(root, padSmall);

        // ── Checkboxes ──
        LinearLayout checkRow = new LinearLayout(this);
        checkRow.setOrientation(LinearLayout.HORIZONTAL);

        CheckBox videoCheck = new CheckBox(this);
        videoCheck.setText(BugpunchStrings.text("crashIncludeVideo", "Include video"));
        videoCheck.setTextColor(colTextPrimary);
        videoCheck.setChecked(hasVideo);
        videoCheck.setEnabled(hasVideo);
        checkRow.addView(videoCheck);

        CheckBox logsCheck = new CheckBox(this);
        logsCheck.setText(BugpunchStrings.text("crashIncludeLogs", "Include logs"));
        logsCheck.setTextColor(colTextPrimary);
        logsCheck.setChecked(true);
        LinearLayout.LayoutParams logParams = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        logParams.setMarginStart(dp(24));
        checkRow.addView(logsCheck, logParams);

        root.addView(checkRow);
        addSpacer(root, dp(16));

        // ── Buttons ──
        LinearLayout btnRow = new LinearLayout(this);
        btnRow.setOrientation(LinearLayout.HORIZONTAL);
        btnRow.setGravity(Gravity.END);

        Button dismissBtn = new Button(this);
        dismissBtn.setText(BugpunchStrings.text("crashDismiss", "Dismiss"));
        dismissBtn.setTextColor(colTextPrimary);
        dismissBtn.setBackgroundColor(BugpunchTheme.color("cardBorder", 0xFF444444));
        dismissBtn.setOnClickListener(v -> {
            sendDismissToUnity();
            finish();
        });
        btnRow.addView(dismissBtn);

        View spacer = new View(this);
        btnRow.addView(spacer, new LinearLayout.LayoutParams(dp(12), 1));

        Button submitBtn = new Button(this);
        submitBtn.setText(BugpunchStrings.text("crashSubmit", "Submit Report"));
        submitBtn.setTextColor(colTextPrimary);
        submitBtn.setBackgroundColor(colAccentPrimary);
        submitBtn.setOnClickListener(v -> {
            String title = titleInput.getText().toString();
            String desc = descInput.getText().toString();
            String sevVal = severities[sevSpinner.getSelectedItemPosition()].toLowerCase();
            boolean inclVideo = videoCheck.isChecked();
            boolean inclLogs = logsCheck.isChecked();

            sendSubmitToUnity(title, desc, sevVal, inclVideo, inclLogs);
            finish();
        });
        btnRow.addView(submitBtn);

        root.addView(btnRow, new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));

        addSpacer(root, dp(12));
        root.addView(buildDeviceIdFooter());

        return scrollView;
    }

    /** Compact tap-to-copy device ID label. Identical shape used across
     *  BugpunchReportActivity so QA can read the ID out of any SDK dialog. */
    private TextView buildDeviceIdFooter() {
        final String id = BugpunchIdentity.getStableDeviceId(this);
        TextView tv = new TextView(this);
        tv.setTextColor(Color.parseColor("#7A7A7A"));
        tv.setTextSize(TypedValue.COMPLEX_UNIT_SP, 10);
        tv.setTypeface(Typeface.MONOSPACE);
        tv.setGravity(Gravity.CENTER);
        tv.setText("ID: " + id);
        tv.setOnClickListener(v -> {
            android.content.ClipboardManager cm =
                (android.content.ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
            if (cm != null) {
                cm.setPrimaryClip(android.content.ClipData.newPlainText("Bugpunch device ID", id));
                android.widget.Toast.makeText(this, "Device ID copied", android.widget.Toast.LENGTH_SHORT).show();
            }
        });
        return tv;
    }

    // ─── Video playback ─────────────────────────────────────────────

    private void startVideoPlayback(String videoPath, SurfaceHolder holder) {
        try {
            releasePlayer();
            mediaPlayer = new MediaPlayer();
            mediaPlayer.setDataSource(this, Uri.fromFile(new File(videoPath)));
            mediaPlayer.setDisplay(holder);
            mediaPlayer.setLooping(true);
            mediaPlayer.setOnPreparedListener(mp -> {
                mp.start();
                // Scale video to fit within the SurfaceView while maintaining aspect ratio
                int vw = mp.getVideoWidth();
                int vh = mp.getVideoHeight();
                if (vw > 0 && vh > 0 && surfaceView != null) {
                    ViewGroup.LayoutParams lp = surfaceView.getLayoutParams();
                    float containerW = surfaceView.getWidth();
                    if (containerW > 0) {
                        float scale = containerW / vw;
                        lp.height = (int)(vh * scale);
                        surfaceView.setLayoutParams(lp);
                    }
                }
            });
            mediaPlayer.prepareAsync();
        } catch (Exception e) {
            Log.e(TAG, "Video playback failed", e);
        }
    }

    private void releasePlayer() {
        if (mediaPlayer != null) {
            try {
                if (mediaPlayer.isPlaying()) mediaPlayer.stop();
                mediaPlayer.release();
            } catch (Exception ignored) {}
            mediaPlayer = null;
        }
    }

    // ─── Unity communication ────────────────────────────────────────

    private void sendSubmitToUnity(String title, String desc, String severity, boolean inclVideo, boolean inclLogs) {
        // Pipe-delimited: title|description|severity|includeVideo|includeLogs
        // Pipes in user text are replaced with dashes to avoid parsing issues.
        String msg = sanitize(title) + "|" + sanitize(desc) + "|" + severity
                + "|" + (inclVideo ? "1" : "0") + "|" + (inclLogs ? "1" : "0");
        sendMessageToUnity("OnSubmit", msg);
    }

    private void sendDismissToUnity() {
        sendMessageToUnity("OnDismiss", "");
    }

    private void sendMessageToUnity(String method, String message) {
        try {
            Class<?> playerClass = Class.forName("com.unity3d.player.UnityPlayer");
            playerClass.getMethod("UnitySendMessage", String.class, String.class, String.class)
                    .invoke(null, "BugpunchCrashCallback", method, message);
        } catch (Exception e) {
            Log.e(TAG, "UnitySendMessage failed", e);
        }
    }

    private static String sanitize(String s) {
        return s != null ? s.replace("|", "-").replace("\n", "\\n") : "";
    }

    // ─── Helpers ────────────────────────────────────────────────────

    private int dp(int dp) {
        return (int)(dp * getResources().getDisplayMetrics().density + 0.5f);
    }

    private void addSpacer(LinearLayout parent, int height) {
        View spacer = new View(this);
        parent.addView(spacer, new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, height));
    }
}
