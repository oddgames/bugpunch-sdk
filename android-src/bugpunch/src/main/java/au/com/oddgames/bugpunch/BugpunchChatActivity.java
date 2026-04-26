package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.Editable;
import android.text.InputType;
import android.text.TextWatcher;
import android.util.Log;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.view.WindowManager;
import android.view.inputmethod.EditorInfo;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.net.URLEncoder;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;
import java.util.TimeZone;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Native chat board — the "Ask for help" surface. Replaces the v1 C# UI Toolkit
 * {@code BugpunchChatBoard} which rendered inside the Unity surface and didn't
 * look like a chat app. Full-screen Messenger-style layout: header bar, bubble
 * list, composer pinned at the bottom. HTTP and 5s polling live in this class —
 * no UnitySendMessage round-trip.
 *
 * Endpoints (all under {@code BugpunchRuntime.getServerUrl()}):
 *   GET  /api/v1/chat/hours                        → enabled / business hours
 *   GET  /api/v1/chat/thread                       → existing thread (or 404)
 *   GET  /api/v1/chat/messages?since=ISO           → poll loop
 *   POST /api/v1/chat/message    { body }          → post a player message
 *   POST /api/v1/chat/read       { }               → mark thread read
 *
 * Auth: {@code Authorization: Bearer <apiKey>} + {@code X-Device-Id: <stable>}
 * — server resolves projectId from the API key.
 */
public class BugpunchChatActivity extends Activity {
    private static final String TAG = "[Bugpunch.ChatActivity]";
    private static final long POLL_MS = 5000L;

    // ── Palette (resolved from theme on create) ─────────────────────
    private int COLOR_BG, COLOR_HEADER, COLOR_BORDER;
    private int COLOR_TEXT, COLOR_TEXT_DIM, COLOR_TEXT_MUTED;
    private int COLOR_ACCENT, COLOR_BUBBLE_OTHER;

    private void applyTheme() {
        COLOR_BG           = BugpunchTheme.color("backdrop",       0xFF101216);
        COLOR_HEADER       = BugpunchTheme.color("cardBackground", 0xFF1B1F25);
        COLOR_BORDER       = BugpunchTheme.color("cardBorder",     0xFF2A3139);
        COLOR_TEXT         = BugpunchTheme.color("textPrimary",    0xFFF1F4F7);
        COLOR_TEXT_DIM     = BugpunchTheme.color("textSecondary",  0xFFB8C2CF);
        COLOR_TEXT_MUTED   = BugpunchTheme.color("textMuted",      0xFF8B95A2);
        COLOR_ACCENT       = BugpunchTheme.color("accentChat",     0xFF336199);
        COLOR_BUBBLE_OTHER = BugpunchTheme.color("cardBorder",     0xFF2A3139);
    }

    // ── Views ──────────────────────────────────────────────────────
    private LinearLayout mMessageList;
    private ScrollView mScroll;
    private TextView mEmpty;
    private TextView mHoursBanner;
    private EditText mComposer;
    private View mSendBtn;
    private View mAttachBtn;
    private LinearLayout mAttachPill;       // floating Screenshot/Video chooser
    private LinearLayout mAttachmentStrip;  // pending attachment chips above composer
    private View mDisabledOverlay;

    /**
     * One pending attachment in the composer. Captured locally; uploaded
     * by the server-side multipart endpoint when the message is sent.
     * For v1 we hold the path and a lightweight ref; full upload pipeline
     * to the chat endpoint is wired in {@link #sendCurrent()}.
     */
    private static class PendingAttachment {
        final String type;     // "image" | "video"
        final String path;     // absolute file path on disk
        PendingAttachment(String type, String path) { this.type = type; this.path = path; }
    }
    private final List<PendingAttachment> mPendingAttachments = new ArrayList<>();

    // ── State ──────────────────────────────────────────────────────
    private final List<JSONObject> mMessages = new ArrayList<>();
    private String mThreadId;             // null until first send / load
    private String mLastMessageAt;        // ISO timestamp; drives "since" param
    private boolean mDisabled;            // chat globally disabled
    private boolean mOffHours;
    private String mHoursMessage;

    private final Handler mUi = new Handler(Looper.getMainLooper());
    private final ExecutorService mNet = Executors.newSingleThreadExecutor();
    private final Runnable mPoll = new Runnable() {
        @Override public void run() {
            fetchMessages(false);
            mUi.postDelayed(this, POLL_MS);
        }
    };

    // ── Launch ─────────────────────────────────────────────────────

    public static void launch() {
        Activity host = BugpunchUnity.currentActivity();
        if (host == null) { Log.w(TAG, "no activity to launch from"); return; }
        Intent i = new Intent(host, BugpunchChatActivity.class);
        i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        host.startActivity(i);
    }

    // ── Lifecycle ──────────────────────────────────────────────────

    @Override protected void onCreate(Bundle b) {
        super.onCreate(b);
        applyTheme();

        // Telemetry (#32) — note whether the player tapped through with the
        // unread badge visible. Just a log line for v1; full analytics is
        // out of scope.
        Log.d(TAG, "chat opened (badge was: "
                + BugpunchReportOverlay.getLastUnreadCount() + ")");

        // Full-bleed dark window — no system title bar.
        getWindow().setBackgroundDrawable(new android.graphics.drawable.ColorDrawable(COLOR_BG));
        getWindow().setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE);

        FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(COLOR_BG);
        root.setLayoutParams(new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        root.setFitsSystemWindows(true);
        setContentView(root);

        LinearLayout column = new LinearLayout(this);
        column.setOrientation(LinearLayout.VERTICAL);
        column.setLayoutParams(new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        root.addView(column);

        column.addView(buildHeader());
        column.addView(buildHoursBanner());
        column.addView(buildMessagesArea(), new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        column.addView(buildComposer());

        // "Disabled" overlay sits on top of everything when chat is off.
        mDisabledOverlay = buildDisabledOverlay();
        mDisabledOverlay.setVisibility(View.GONE);
        root.addView(mDisabledOverlay);

        // Bootstrap.
        fetchHours();
        fetchThread();
    }

    @Override protected void onResume() {
        super.onResume();
        mUi.postDelayed(mPoll, POLL_MS);
    }

    @Override protected void onPause() {
        mUi.removeCallbacks(mPoll);
        super.onPause();
    }

    @Override protected void onDestroy() {
        mUi.removeCallbacks(mPoll);
        mNet.shutdownNow();
        super.onDestroy();
    }

    // ── Header ─────────────────────────────────────────────────────

    private View buildHeader() {
        LinearLayout bar = new LinearLayout(this);
        bar.setOrientation(LinearLayout.HORIZONTAL);
        bar.setBackgroundColor(COLOR_HEADER);
        bar.setGravity(Gravity.CENTER_VERTICAL);
        int pad = dp(12);
        bar.setPadding(pad, pad, pad, pad);

        // Bottom hairline.
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(COLOR_HEADER);
        // Use a layer drawable so we get a 1dp bottom line without an
        // extra wrapper view.
        android.graphics.drawable.LayerDrawable layered = new android.graphics.drawable.LayerDrawable(
                new android.graphics.drawable.Drawable[] { bg, hairline(COLOR_BORDER) });
        layered.setLayerInset(1, 0, dp(48), 0, 0);  // push hairline to the bottom
        // The bottom-inset trick above isn't strictly needed since we set the
        // bg solid; simpler: just keep the solid header bg and add a child
        // separator. Drop the layered drawable.
        bar.setBackground(bg);

        // Avatar circle
        View avatar = new View(this);
        GradientDrawable av = new GradientDrawable();
        av.setShape(GradientDrawable.OVAL);
        av.setColor(COLOR_ACCENT);
        avatar.setBackground(av);
        LinearLayout.LayoutParams aLp = new LinearLayout.LayoutParams(dp(36), dp(36));
        aLp.rightMargin = dp(10);
        bar.addView(avatar, aLp);

        // Title + subtitle stack
        LinearLayout titleCol = new LinearLayout(this);
        titleCol.setOrientation(LinearLayout.VERTICAL);
        LinearLayout.LayoutParams tcLp = new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        bar.addView(titleCol, tcLp);

        TextView title = new TextView(this);
        title.setText(BugpunchStrings.text("chatTitle", "Chat with the team"));
        title.setTextColor(COLOR_TEXT);
        title.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeTitle", 17));
        title.setTypeface(Typeface.DEFAULT_BOLD);
        titleCol.addView(title);

        TextView subtitle = new TextView(this);
        subtitle.setText(BugpunchStrings.text("chatSubtitle", "Usually replies within a day"));
        subtitle.setTextColor(COLOR_TEXT_MUTED);
        subtitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12));
        titleCol.addView(subtitle);

        // Close (X)
        TextView close = new TextView(this);
        close.setText("✕");
        close.setTextColor(COLOR_TEXT_DIM);
        close.setTextSize(TypedValue.COMPLEX_UNIT_SP, 22);
        close.setGravity(Gravity.CENTER);
        close.setClickable(true);
        close.setFocusable(true);
        int cp = dp(8);
        close.setPadding(cp, cp, cp, cp);
        close.setOnClickListener(v -> finish());
        LinearLayout.LayoutParams cLp = new LinearLayout.LayoutParams(dp(40), dp(40));
        bar.addView(close, cLp);

        return bar;
    }

    private GradientDrawable hairline(int color) {
        GradientDrawable d = new GradientDrawable();
        d.setColor(color);
        return d;
    }

    // ── Hours banner (visible when off-hours but chat is enabled) ──

    private View buildHoursBanner() {
        TextView t = new TextView(this);
        t.setBackgroundColor(0xFF332B17);
        t.setTextColor(0xFFF6CF7C);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12));
        int pad = dp(10);
        t.setPadding(pad, pad, pad, pad);
        t.setVisibility(View.GONE);
        mHoursBanner = t;
        return t;
    }

    // ── Messages area ──────────────────────────────────────────────

    private View buildMessagesArea() {
        FrameLayout wrap = new FrameLayout(this);
        wrap.setBackgroundColor(COLOR_BG);

        mScroll = new ScrollView(this);
        mScroll.setFillViewport(true);
        wrap.addView(mScroll, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        mMessageList = new LinearLayout(this);
        mMessageList.setOrientation(LinearLayout.VERTICAL);
        int pad = dp(12);
        mMessageList.setPadding(pad, pad, pad, pad);
        mScroll.addView(mMessageList, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        mEmpty = new TextView(this);
        mEmpty.setText(BugpunchStrings.text("chatEmpty",
                "Say hi 👋 — the dev team will reply here"));
        mEmpty.setTextColor(COLOR_TEXT_MUTED);
        mEmpty.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeBody", 14));
        mEmpty.setGravity(Gravity.CENTER);
        FrameLayout.LayoutParams eLp = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        eLp.gravity = Gravity.CENTER;
        eLp.leftMargin = eLp.rightMargin = dp(32);
        wrap.addView(mEmpty, eLp);

        return wrap;
    }

    // ── Composer ───────────────────────────────────────────────────

    private View buildComposer() {
        // Wrap composer in a vertical column so we can stack the attachment
        // strip + the optional attachments pill on top of the input row.
        LinearLayout column = new LinearLayout(this);
        column.setOrientation(LinearLayout.VERTICAL);
        column.setBackgroundColor(COLOR_HEADER);

        // Attachments pill (Screenshot / Record video) — shown when the
        // attach button is tapped. Sits above the input row so the choice
        // appears "close to the bubble" per the design brief.
        mAttachPill = buildAttachPill();
        mAttachPill.setVisibility(View.GONE);
        column.addView(mAttachPill);

        // Pending-attachment chip strip — shows captured screenshots / videos
        // before they're sent. Tap an X on a chip to discard it.
        mAttachmentStrip = new LinearLayout(this);
        mAttachmentStrip.setOrientation(LinearLayout.HORIZONTAL);
        int sPad = dp(6);
        mAttachmentStrip.setPadding(sPad, sPad, sPad, 0);
        mAttachmentStrip.setVisibility(View.GONE);
        column.addView(mAttachmentStrip);

        LinearLayout bar = new LinearLayout(this);
        bar.setOrientation(LinearLayout.HORIZONTAL);
        bar.setGravity(Gravity.CENTER_VERTICAL);
        int pad = dp(8);
        bar.setPadding(pad, pad, pad, pad);
        column.addView(bar);

        // Attach (paperclip) button — toggles the attach pill above.
        FrameLayout attach = new FrameLayout(this);
        GradientDrawable ab = new GradientDrawable();
        ab.setShape(GradientDrawable.OVAL);
        ab.setColor(COLOR_BORDER);
        attach.setBackground(ab);
        attach.setClickable(true);
        attach.setFocusable(true);
        attach.setOnClickListener(v -> toggleAttachPill());
        TextView clip = new TextView(this);
        clip.setText("+");
        clip.setTextColor(COLOR_TEXT);
        clip.setTextSize(TypedValue.COMPLEX_UNIT_SP, 22);
        clip.setTypeface(Typeface.DEFAULT_BOLD);
        clip.setGravity(Gravity.CENTER);
        attach.addView(clip, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT,
                Gravity.CENTER));
        LinearLayout.LayoutParams attLp = new LinearLayout.LayoutParams(dp(40), dp(40));
        attLp.rightMargin = dp(8);
        bar.addView(attach, attLp);
        mAttachBtn = attach;

        mComposer = new EditText(this);
        mComposer.setHint(BugpunchStrings.text("chatComposerHint", "Type a message…"));
        mComposer.setHintTextColor(COLOR_TEXT_MUTED);
        mComposer.setTextColor(COLOR_TEXT);
        mComposer.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeBody", 14));
        mComposer.setBackground(pillBg(COLOR_BG, dp(20), dp(1), COLOR_BORDER));
        int cpv = dp(10), cph = dp(14);
        mComposer.setPadding(cph, cpv, cph, cpv);
        mComposer.setMinHeight(dp(40));
        mComposer.setMaxLines(4);
        mComposer.setInputType(InputType.TYPE_CLASS_TEXT
                | InputType.TYPE_TEXT_FLAG_MULTI_LINE
                | InputType.TYPE_TEXT_FLAG_CAP_SENTENCES);
        mComposer.setImeOptions(EditorInfo.IME_ACTION_SEND);
        mComposer.setOnEditorActionListener((v, actionId, event) -> {
            if (actionId == EditorInfo.IME_ACTION_SEND) { sendCurrent(); return true; }
            return false;
        });
        mComposer.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void onTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void afterTextChanged(Editable s) { updateSendEnabled(); }
        });
        LinearLayout.LayoutParams cLp = new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        cLp.rightMargin = dp(8);
        bar.addView(mComposer, cLp);

        // Send button — circular accent.
        FrameLayout send = new FrameLayout(this);
        GradientDrawable sb = new GradientDrawable();
        sb.setShape(GradientDrawable.OVAL);
        sb.setColor(COLOR_ACCENT);
        send.setBackground(sb);
        send.setClickable(true);
        send.setFocusable(true);
        send.setOnClickListener(v -> sendCurrent());
        TextView arrow = new TextView(this);
        arrow.setText("➤");
        arrow.setTextColor(Color.WHITE);
        arrow.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16);
        arrow.setGravity(Gravity.CENTER);
        send.addView(arrow, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT,
                Gravity.CENTER));
        LinearLayout.LayoutParams sLp = new LinearLayout.LayoutParams(dp(40), dp(40));
        bar.addView(send, sLp);
        mSendBtn = send;
        updateSendEnabled();

        return column;
    }

    private void updateSendEnabled() {
        boolean ok = (mComposer.getText().toString().trim().length() > 0
                || !mPendingAttachments.isEmpty())
                && !mDisabled;
        mSendBtn.setAlpha(ok ? 1f : 0.4f);
        mSendBtn.setClickable(ok);
    }

    // ── Attach pill (Screenshot / Record video chooser) ────────────

    private LinearLayout buildAttachPill() {
        LinearLayout pill = new LinearLayout(this);
        pill.setOrientation(LinearLayout.HORIZONTAL);
        pill.setGravity(Gravity.CENTER);
        int outer = dp(8);
        pill.setPadding(outer, outer, outer, 0);

        // Inner pill capsule with the two options. Sits flush-left so it
        // visually anchors to the attach button below it (the "+" lives on
        // the left of the composer).
        LinearLayout caps = new LinearLayout(this);
        caps.setOrientation(LinearLayout.HORIZONTAL);
        caps.setGravity(Gravity.CENTER_VERTICAL);
        GradientDrawable cb = new GradientDrawable();
        cb.setColor(COLOR_BG);
        cb.setStroke(dp(1), COLOR_BORDER);
        cb.setCornerRadius(dp(22));
        caps.setBackground(cb);
        int ip = dp(6);
        caps.setPadding(ip, ip, ip, ip);
        pill.addView(caps, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        caps.addView(buildPillOption("📷  Screenshot", v -> takeScreenshotAttachment()));
        caps.addView(buildPillSeparator());
        caps.addView(buildPillOption("🎥  Record video", v -> startVideoAttachment()));

        return pill;
    }

    private View buildPillOption(String label, View.OnClickListener onClick) {
        TextView t = new TextView(this);
        t.setText(label);
        t.setTextColor(COLOR_TEXT);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeBody", 14));
        t.setTypeface(Typeface.DEFAULT_BOLD);
        t.setClickable(true);
        t.setFocusable(true);
        int hp = dp(14), vp = dp(8);
        t.setPadding(hp, vp, hp, vp);
        t.setOnClickListener(onClick);
        return t;
    }

    private View buildPillSeparator() {
        View v = new View(this);
        v.setBackgroundColor(COLOR_BORDER);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(dp(1), dp(20));
        int m = dp(2);
        lp.leftMargin = m; lp.rightMargin = m;
        v.setLayoutParams(lp);
        return v;
    }

    private void toggleAttachPill() {
        if (mDisabled) return;
        mAttachPill.setVisibility(mAttachPill.getVisibility() == View.VISIBLE
                ? View.GONE : View.VISIBLE);
    }

    private void hideAttachPill() {
        mAttachPill.setVisibility(View.GONE);
    }

    // ── Capture: screenshot ────────────────────────────────────────

    private void takeScreenshotAttachment() {
        hideAttachPill();
        // Capture using existing native screenshot helper. We minimize the
        // chat first so the captured frame is the live game view, not our
        // own UI; then re-show it once the file is on disk.
        moveTaskToBack(true);
        final String outPath = new java.io.File(
                getCacheDir(), "bp_chat_shot_" + System.currentTimeMillis() + ".png").getAbsolutePath();
        // Tiny delay so the chat window has actually animated out before we
        // grab the frame. 250ms matches the default activity transition.
        mUi.postDelayed(() -> {
            BugpunchScreenshot.captureThen(outPath, 90, () -> mUi.post(() -> {
                addPendingAttachment(new PendingAttachment("image", outPath));
                bringChatBack();
            }));
        }, 250L);
    }

    // ── Capture: video (issue #30 — real flow) ─────────────────────
    //
    // 1. Hide chat pill, request MediaProjection consent via the new Java-
    //    callback overload of BugpunchProjectionRequest (no UnitySendMessage
    //    bounce — chat is fully native).
    // 2. On approval: minimise chat (so the captured frames are the live
    //    game view, not our own UI), kick off a foreground-service-backed
    //    BugpunchRecorder writing a single segment to a known path in
    //    cache dir, and float a Stop pill on top of the host activity.
    // 3. On Stop tap: stop the recorder (which finalises the muxer), remove
    //    the pill, bring chat back to the front, and add the resulting MP4
    //    as a pending attachment chip. uploadAttachment() picks it up on
    //    send and POSTs it to /api/v1/chat/upload as video/mp4.
    // 4. On consent denial: just bring chat back — no error toast (the
    //    system dialog already provided the user feedback).

    /** Path of the in-progress chat-video MP4 — null when not recording. */
    private String mPendingVideoPath;
    /** Stop pill view added to the host activity's WindowManager — null when
     *  not visible. Held in static field so the overlay survives this
     *  Activity moving to the back. */
    private static View sChatStopPill;
    private static WindowManager sChatStopPillWm;

    private void startVideoAttachment() {
        hideAttachPill();

        final Activity host = BugpunchUnity.currentActivity();
        final Activity self = this;
        if (host == null) {
            Log.w(TAG, "startVideoAttachment: no host activity for projection consent");
            return;
        }

        // Output path lives in the app cache dir so we don't need any
        // runtime storage permission. Cleaned up when the cache is cleared.
        final String outPath = new java.io.File(getCacheDir(),
                "bp_chat_video_" + System.currentTimeMillis() + ".mp4")
                .getAbsolutePath();

        // Minimise the chat so the captured frames are the live game view.
        // We do this BEFORE asking for projection consent so the system
        // dialog appears over the game (matching the bug-report flow which
        // is normally already minimised when triggered from a pin / shake).
        moveTaskToBack(true);

        BugpunchProjectionRequest.requestForJavaCallback(host,
                new BugpunchProjectionRequest.JavaProjectionCallback() {
            @Override
            public void onApproved(int resultCode, Intent resultData) {
                mPendingVideoPath = outPath;
                // Configure + start the recorder via the FG service. We
                // don't drive the recorder here directly because Android
                // 14+ requires the FG service to be alive BEFORE
                // getMediaProjection() runs. Service reads the segment
                // path extra and routes to startSegmentToPath() instead
                // of the ring-buffer path.
                int dpi = getResources().getDisplayMetrics().densityDpi;
                int width = getResources().getDisplayMetrics().widthPixels;
                int height = getResources().getDisplayMetrics().heightPixels;
                // Match the bug-report defaults — readable mobile cap that
                // keeps the file small enough for the 30 MB chat upload
                // limit even at the upper end of recording length.
                int bitrate = 2_000_000;
                int fps = 30;
                int windowSeconds = 30; // ignored in segment mode; passed for parity

                Intent svc = new Intent(host, BugpunchProjectionService.class)
                        .putExtra(BugpunchProjectionService.EXTRA_RESULT_CODE, resultCode)
                        .putExtra(BugpunchProjectionService.EXTRA_RESULT_DATA, resultData)
                        .putExtra(BugpunchProjectionService.EXTRA_WIDTH, width)
                        .putExtra(BugpunchProjectionService.EXTRA_HEIGHT, height)
                        .putExtra(BugpunchProjectionService.EXTRA_BITRATE, bitrate)
                        .putExtra(BugpunchProjectionService.EXTRA_FPS, fps)
                        .putExtra(BugpunchProjectionService.EXTRA_WINDOW_SECONDS, windowSeconds)
                        .putExtra(BugpunchProjectionService.EXTRA_DPI, dpi)
                        .putExtra(BugpunchProjectionService.EXTRA_SEGMENT_OUTPUT_PATH, outPath);
                try {
                    if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) {
                        host.startForegroundService(svc);
                    } else {
                        host.startService(svc);
                    }
                } catch (Exception e) {
                    Log.e(TAG, "failed to start projection service", e);
                    mPendingVideoPath = null;
                    bringChatBack();
                    return;
                }
                showStopPill(host);
            }

            @Override
            public void onDenied() {
                // System dialog already informed the user; just rehydrate
                // the chat. No toast / no error chip.
                bringChatBack();
            }
        });
    }

    /**
     * Floating Stop pill (red, white dot, "Stop ⏺") anchored top-center on
     * the host activity. Patterned after BugpunchReportOverlay.showRecording-
     * Overlay — same WindowManager / TYPE_APPLICATION trick, just a much
     * simpler layout. Static so we can remove it from another callback even
     * if this Activity instance is destroyed (chat may be re-launched in
     * onApproved before the pill is dismissed).
     */
    private void showStopPill(final Activity host) {
        host.runOnUiThread(() -> {
            if (sChatStopPill != null) return;

            int accent = BugpunchTheme.color("accentRecord", 0xFFD22E2E);

            // Pill capsule: red background, padding, "● Stop" label.
            LinearLayout pill = new LinearLayout(host);
            pill.setOrientation(LinearLayout.HORIZONTAL);
            pill.setGravity(Gravity.CENTER_VERTICAL);
            int pad = (int) (host.getResources().getDisplayMetrics().density * 14);
            int padV = (int) (host.getResources().getDisplayMetrics().density * 10);
            pill.setPadding(pad, padV, pad, padV);

            GradientDrawable bg = new GradientDrawable();
            bg.setColor(accent);
            bg.setCornerRadius(host.getResources().getDisplayMetrics().density * 24);
            pill.setBackground(bg);
            pill.setClickable(true);
            pill.setFocusable(true);

            // White dot — drawn as a circular drawable view.
            View dot = new View(host);
            GradientDrawable dotBg = new GradientDrawable();
            dotBg.setShape(GradientDrawable.OVAL);
            dotBg.setColor(Color.WHITE);
            dot.setBackground(dotBg);
            int dotSize = (int) (host.getResources().getDisplayMetrics().density * 10);
            LinearLayout.LayoutParams dotLp = new LinearLayout.LayoutParams(dotSize, dotSize);
            dotLp.rightMargin = (int) (host.getResources().getDisplayMetrics().density * 8);
            pill.addView(dot, dotLp);

            TextView label = new TextView(host);
            label.setText(BugpunchStrings.text("chatStopRecording", "Stop ⏺"));
            label.setTextColor(Color.WHITE);
            label.setTypeface(Typeface.DEFAULT_BOLD);
            label.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeBody", 14));
            pill.addView(label);

            pill.setOnClickListener(v -> stopVideoAttachment());

            WindowManager wm = (WindowManager) host.getSystemService(Context.WINDOW_SERVICE);
            WindowManager.LayoutParams wlp = new WindowManager.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT,
                    ViewGroup.LayoutParams.WRAP_CONTENT,
                    WindowManager.LayoutParams.TYPE_APPLICATION,
                    WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                            | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                    PixelFormat.TRANSLUCENT);
            wlp.gravity = Gravity.TOP | Gravity.CENTER_HORIZONTAL;
            // ~48dp from top so it clears the status bar / notch.
            wlp.y = (int) (host.getResources().getDisplayMetrics().density * 48);

            try {
                wm.addView(pill, wlp);
                sChatStopPill = pill;
                sChatStopPillWm = wm;
            } catch (Exception e) {
                Log.w(TAG, "showStopPill addView failed", e);
            }
        });
    }

    private static void hideStopPill() {
        try {
            if (sChatStopPill != null && sChatStopPillWm != null) {
                sChatStopPillWm.removeView(sChatStopPill);
            }
        } catch (Exception ignored) {}
        sChatStopPill = null;
        sChatStopPillWm = null;
    }

    /**
     * Stop the in-progress chat video and surface the resulting MP4 as a
     * pending attachment. Triggered by tapping the Stop pill — runs on the
     * UI thread (the pill click listener is fired by the Activity's looper).
     */
    private void stopVideoAttachment() {
        hideStopPill();
        final String path = mPendingVideoPath;
        mPendingVideoPath = null;
        if (path == null) { bringChatBack(); return; }

        // BugpunchRecorder.stop() finalises the segment muxer and stops the
        // FG service. It's synchronous so we can safely peek at the file
        // immediately after.
        try {
            BugpunchRecorder.getInstance().stop();
        } catch (Exception e) {
            Log.w(TAG, "recorder.stop failed", e);
        }

        // Bring chat back regardless — even if recording produced no usable
        // file we want the player back at the composer.
        bringChatBack();

        boolean valid = BugpunchRecorder.getInstance().isLastSegmentValid();
        java.io.File f = new java.io.File(path);
        if (valid && f.exists() && f.length() > 0) {
            mUi.post(() -> {
                addPendingAttachment(new PendingAttachment("video", path));
            });
        } else {
            Log.w(TAG, "chat video segment invalid or empty — discarded: " + path);
            try { f.delete(); } catch (Exception ignored) {}
        }
    }

    private void bringChatBack() {
        Intent i = new Intent(this, BugpunchChatActivity.class);
        i.addFlags(Intent.FLAG_ACTIVITY_REORDER_TO_FRONT
                | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        startActivity(i);
    }

    // ── Pending attachment chips ───────────────────────────────────

    private void addPendingAttachment(PendingAttachment a) {
        mPendingAttachments.add(a);
        rebuildAttachmentStrip();
        updateSendEnabled();
    }

    private void rebuildAttachmentStrip() {
        mAttachmentStrip.removeAllViews();
        if (mPendingAttachments.isEmpty()) {
            mAttachmentStrip.setVisibility(View.GONE);
            return;
        }
        mAttachmentStrip.setVisibility(View.VISIBLE);
        for (final PendingAttachment a : mPendingAttachments) {
            FrameLayout chip = new FrameLayout(this);
            GradientDrawable cb = new GradientDrawable();
            cb.setColor(COLOR_BG);
            cb.setStroke(dp(1), COLOR_BORDER);
            cb.setCornerRadius(dp(8));
            chip.setBackground(cb);

            TextView label = new TextView(this);
            label.setText(("image".equals(a.type) ? "📷 " : "🎥 ")
                    + new java.io.File(a.path).getName());
            label.setTextColor(COLOR_TEXT_DIM);
            label.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12));
            int lp = dp(8);
            label.setPadding(lp, lp, dp(28), lp);
            chip.addView(label);

            TextView x = new TextView(this);
            x.setText("×");
            x.setTextColor(COLOR_TEXT_MUTED);
            x.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16);
            x.setTypeface(Typeface.DEFAULT_BOLD);
            x.setClickable(true);
            x.setOnClickListener(v -> {
                mPendingAttachments.remove(a);
                rebuildAttachmentStrip();
                updateSendEnabled();
            });
            FrameLayout.LayoutParams xLp = new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            xLp.gravity = Gravity.CENTER_VERTICAL | Gravity.END;
            xLp.rightMargin = dp(6);
            chip.addView(x, xLp);

            LinearLayout.LayoutParams chipLp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            chipLp.rightMargin = dp(6);
            mAttachmentStrip.addView(chip, chipLp);
        }
    }

    // ── Disabled overlay ───────────────────────────────────────────

    private View buildDisabledOverlay() {
        LinearLayout v = new LinearLayout(this);
        v.setOrientation(LinearLayout.VERTICAL);
        v.setGravity(Gravity.CENTER);
        v.setBackgroundColor(0xCC000000);
        v.setClickable(true);  // swallow taps

        TextView t = new TextView(this);
        t.setText(BugpunchStrings.text("chatDisabled",
                "Chat is unavailable right now."));
        t.setTextColor(COLOR_TEXT);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeBody", 14));
        t.setGravity(Gravity.CENTER);
        int pad = dp(24);
        t.setPadding(pad, pad, pad, pad);
        v.addView(t);

        TextView close = new TextView(this);
        close.setText(BugpunchStrings.text("close", "Close"));
        close.setTextColor(COLOR_ACCENT);
        close.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeBody", 14));
        close.setGravity(Gravity.CENTER);
        close.setTypeface(Typeface.DEFAULT_BOLD);
        close.setClickable(true);
        close.setOnClickListener(x -> finish());
        close.setPadding(pad, pad / 2, pad, pad);
        v.addView(close);

        FrameLayout.LayoutParams lp = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT);
        v.setLayoutParams(lp);
        return v;
    }

    // ── Bubbles ────────────────────────────────────────────────────

    private void renderMessages() {
        mMessageList.removeAllViews();
        if (mMessages.isEmpty()) {
            mEmpty.setVisibility(View.VISIBLE);
            return;
        }
        mEmpty.setVisibility(View.GONE);
        for (JSONObject m : mMessages) {
            mMessageList.addView(buildBubble(m));
        }
        mUi.post(() -> mScroll.fullScroll(View.FOCUS_DOWN));
    }

    private View buildBubble(JSONObject m) {
        String sender = m.optString("Sender", m.optString("sender", "qa"));
        boolean mine = "sdk".equalsIgnoreCase(sender);
        String body = m.optString("Body", m.optString("body", ""));
        String createdAt = m.optString("CreatedAt", m.optString("createdAt", ""));
        String type = m.optString("Type", m.optString("type", "text"));

        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.VERTICAL);
        row.setGravity(mine ? Gravity.END : Gravity.START);
        int rowPad = dp(2);
        LinearLayout.LayoutParams rowLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        rowLp.topMargin = rowPad;
        rowLp.bottomMargin = rowPad;
        row.setLayoutParams(rowLp);

        // Attachments first (above the body bubble) — rendered as small
        // image thumbnails when the local path or a server URL is known.
        JSONArray atts = m.optJSONArray("Attachments");
        if (atts == null) atts = m.optJSONArray("attachments");
        if (atts != null) {
            for (int i = 0; i < atts.length(); i++) {
                JSONObject ao = atts.optJSONObject(i);
                if (ao == null) continue;
                View thumb = buildAttachmentThumb(ao, mine);
                if (thumb != null) row.addView(thumb);
            }
        }

        // Special bubble: dashboard-issued data / script request with inline
        // Approve / Decline buttons. Only render the special variant for
        // *incoming* requests still pending an answer.
        boolean isRequest = ("scriptRequest".equalsIgnoreCase(type)
                          || "dataRequest".equalsIgnoreCase(type)) && !mine;
        String requestState = m.optString("RequestState", m.optString("requestState", "pending"));
        if (isRequest && "pending".equalsIgnoreCase(requestState)) {
            row.addView(buildRequestBubble(m, type, body));
        } else {
            // Plain text bubble (shown only if there's text — pure-attachment
            // messages skip this).
            if (body.length() > 0 || atts == null || atts.length() == 0) {
                TextView bubble = new TextView(this);
                String shown = body.length() > 0 ? body
                        : (atts != null && atts.length() > 0
                            ? ("image".equalsIgnoreCase(atts.optJSONObject(0).optString("type")) ? "📷" : "🎥")
                            : "");
                bubble.setText(shown);
                bubble.setTextColor(mine ? Color.WHITE : COLOR_TEXT);
                bubble.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeBody", 14));
                bubble.setAutoLinkMask(android.text.util.Linkify.WEB_URLS);
                bubble.setLinksClickable(true);
                int bp = dp(12), bpv = dp(8);
                bubble.setPadding(bp, bpv, bp, bpv);

                GradientDrawable bg = new GradientDrawable();
                bg.setColor(mine ? COLOR_ACCENT : COLOR_BUBBLE_OTHER);
                float r = dp(16), tail = dp(4);
                bg.setCornerRadii(mine
                        ? new float[] { r, r, r, r, tail, tail, r, r }
                        : new float[] { r, r, r, r, r, r, tail, tail });
                bubble.setBackground(bg);
                bubble.setMaxWidth((int)(getResources().getDisplayMetrics().widthPixels * 0.8f));

                LinearLayout.LayoutParams bLp = new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
                bLp.gravity = mine ? Gravity.END : Gravity.START;
                row.addView(bubble, bLp);
            }

            // Final-state badge for finished requests (under the bubble).
            if (isRequest || "scriptRequest".equalsIgnoreCase(type) || "dataRequest".equalsIgnoreCase(type)) {
                if (!"pending".equalsIgnoreCase(requestState)) {
                    TextView badge = new TextView(this);
                    badge.setText("approved".equalsIgnoreCase(requestState)
                            ? "✓ Approved" : "✗ Declined");
                    badge.setTextColor("approved".equalsIgnoreCase(requestState)
                            ? COLOR_ACCENT : COLOR_TEXT_MUTED);
                    badge.setTextSize(TypedValue.COMPLEX_UNIT_SP,
                            BugpunchTheme.sp("fontSizeCaption", 12));
                    badge.setTypeface(Typeface.DEFAULT_BOLD);
                    LinearLayout.LayoutParams blp = new LinearLayout.LayoutParams(
                            ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
                    blp.gravity = mine ? Gravity.END : Gravity.START;
                    blp.topMargin = dp(2);
                    blp.leftMargin = blp.rightMargin = dp(6);
                    row.addView(badge, blp);
                }
            }
        }

        // Time caption underneath, aligned to the bubble side.
        if (createdAt.length() > 0) {
            TextView time = new TextView(this);
            time.setText(formatTime(createdAt));
            time.setTextColor(COLOR_TEXT_MUTED);
            time.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 11) - 1);
            LinearLayout.LayoutParams tLp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            tLp.gravity = mine ? Gravity.END : Gravity.START;
            tLp.topMargin = dp(2);
            tLp.leftMargin = dp(6); tLp.rightMargin = dp(6);
            time.setLayoutParams(tLp);
            row.addView(time);
        }
        return row;
    }

    /**
     * One image / video attachment rendered as a thumb above the bubble body.
     * Image: actual bitmap loaded from disk (local path) — server-fetched
     * thumbs come in a follow-up pass since we don't have a network image
     * loader yet. Video: 🎥 placeholder block.
     */
    private View buildAttachmentThumb(JSONObject ao, boolean mine) {
        String type = ao.optString("type", "image");
        String localPath = ao.optString("localPath", "");
        String url = ao.optString("url", ao.optString("Url", ""));

        ImageView iv = new ImageView(this);
        iv.setScaleType(ImageView.ScaleType.CENTER_CROP);
        int side = dp(160);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(side, side);
        lp.gravity = mine ? Gravity.END : Gravity.START;
        lp.bottomMargin = dp(4);
        iv.setLayoutParams(lp);

        // Rounded card behind the thumb so the colour edges match the bubble.
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(COLOR_BUBBLE_OTHER);
        bg.setCornerRadius(dp(12));
        iv.setBackground(bg);
        iv.setClipToOutline(true);

        if ("image".equalsIgnoreCase(type) && localPath.length() > 0) {
            try {
                android.graphics.Bitmap bmp = android.graphics.BitmapFactory.decodeFile(localPath);
                if (bmp != null) iv.setImageBitmap(bmp);
            } catch (Throwable ignored) {}
        } else {
            // Server URL or video — placeholder glyph for v1.
            iv.setImageDrawable(null);
            TextView wrap = new TextView(this);
            wrap.setText("video".equalsIgnoreCase(type) ? "🎥" : "📷");
            wrap.setTextSize(TypedValue.COMPLEX_UNIT_SP, 36);
            wrap.setGravity(Gravity.CENTER);
            wrap.setLayoutParams(lp);
            wrap.setBackground(bg);
            return wrap;
        }
        return iv;
    }

    /**
     * Inline approve / decline bubble for dashboard-driven dataRequest /
     * scriptRequest messages. Renders the payload preview (truncated) plus
     * two action buttons. On tap, POSTs to the server and refreshes — server
     * stamps the message with the new RequestState.
     */
    private View buildRequestBubble(final JSONObject m, String type, String body) {
        LinearLayout container = new LinearLayout(this);
        container.setOrientation(LinearLayout.VERTICAL);
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(COLOR_BUBBLE_OTHER);
        bg.setStroke(dp(1), COLOR_ACCENT);
        bg.setCornerRadius(dp(12));
        container.setBackground(bg);
        int p = dp(12);
        container.setPadding(p, p, p, p);

        TextView label = new TextView(this);
        label.setText("scriptRequest".equalsIgnoreCase(type)
                ? BugpunchStrings.text("chatScriptRequest", "The dev team wants to run a script:")
                : BugpunchStrings.text("chatDataRequest", "The dev team is asking for data:"));
        label.setTextColor(COLOR_TEXT_DIM);
        label.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12));
        label.setTypeface(Typeface.DEFAULT_BOLD);
        container.addView(label);

        TextView preview = new TextView(this);
        // Truncate long script bodies — full source is still shown when
        // tapped to expand (TODO).
        String shown = body == null ? "" : body;
        if (shown.length() > 600) shown = shown.substring(0, 600) + "\n…";
        preview.setText(shown);
        preview.setTextColor(COLOR_TEXT);
        preview.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12));
        if ("scriptRequest".equalsIgnoreCase(type)) {
            preview.setTypeface(Typeface.MONOSPACE);
        }
        int pp = dp(8);
        preview.setPadding(0, pp, 0, pp);
        container.addView(preview);

        // Two-button row.
        LinearLayout actions = new LinearLayout(this);
        actions.setOrientation(LinearLayout.HORIZONTAL);
        actions.setGravity(Gravity.END);
        container.addView(actions);

        TextView decline = makeActionButton(
                BugpunchStrings.text("chatDecline", "Decline"), COLOR_BORDER, COLOR_TEXT_DIM);
        decline.setOnClickListener(v -> answerRequest(m, false));
        actions.addView(decline);

        View spacer = new View(this);
        actions.addView(spacer, new LinearLayout.LayoutParams(dp(8), 0));

        TextView approve = makeActionButton(
                BugpunchStrings.text("chatApprove", "Approve"), COLOR_ACCENT, Color.WHITE);
        approve.setOnClickListener(v -> answerRequest(m, true));
        actions.addView(approve);

        // Constrain width to ~85% so it never bleeds across the screen.
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                (int)(getResources().getDisplayMetrics().widthPixels * 0.85f),
                ViewGroup.LayoutParams.WRAP_CONTENT);
        lp.gravity = Gravity.START;
        container.setLayoutParams(lp);
        return container;
    }

    private TextView makeActionButton(String text, int bg, int fg) {
        TextView b = new TextView(this);
        b.setText(text);
        b.setTextColor(fg);
        b.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeBody", 14));
        b.setTypeface(Typeface.DEFAULT_BOLD);
        b.setClickable(true);
        b.setFocusable(true);
        int hp = dp(16), vp = dp(10);
        b.setPadding(hp, vp, hp, vp);
        GradientDrawable d = new GradientDrawable();
        d.setColor(bg);
        d.setCornerRadius(dp(8));
        b.setBackground(d);
        return b;
    }

    private void answerRequest(final JSONObject m, final boolean approved) {
        final String id = m.optString("Id", m.optString("id", ""));
        if (id.length() == 0) return;
        // Optimistic state flip so the buttons disappear immediately.
        try {
            m.put("RequestState", approved ? "approved" : "declined");
            renderMessages();
        } catch (Throwable ignored) {}

        mNet.execute(() -> {
            try {
                JSONObject payload = new JSONObject();
                payload.put("messageId", id);
                payload.put("approved", approved);
                http("POST", "/api/v1/chat/request/answer", payload.toString());

                // For approved scriptRequest / dataRequest: bounce the body
                // up to C# (BugpunchClient.OnApprovedScriptRequest /
                // OnApprovedDataRequest) where ODDGames.Scripting actually
                // lives. C# runs it, captures the result, and POSTs the
                // reply chat message itself — native just kicks the next
                // poll so the bubble appears.
                String type = m.optString("Type", m.optString("type", ""));
                if (approved) {
                    String body = m.optString("Body", m.optString("body", ""));
                    String b64 = android.util.Base64.encodeToString(
                            body.getBytes("UTF-8"),
                            android.util.Base64.NO_WRAP);
                    String unityPayload = id + "|" + b64;
                    if ("scriptRequest".equalsIgnoreCase(type)) {
                        BugpunchUnity.sendMessage(
                                "BugpunchClient", "OnApprovedScriptRequest", unityPayload);
                    } else if ("dataRequest".equalsIgnoreCase(type)) {
                        BugpunchUnity.sendMessage(
                                "BugpunchClient", "OnApprovedDataRequest", unityPayload);
                    }
                }
                mUi.post(() -> fetchMessages(true));
            } catch (Throwable t) {
                Log.w(TAG, "answerRequest failed", t);
            }
        });
    }

    // ── Sending ────────────────────────────────────────────────────

    private void sendCurrent() {
        if (mDisabled) return;
        String body = mComposer.getText().toString().trim();
        if (body.length() == 0 && mPendingAttachments.isEmpty()) return;
        mComposer.setText("");
        final List<PendingAttachment> attachments = new ArrayList<>(mPendingAttachments);
        mPendingAttachments.clear();
        rebuildAttachmentStrip();

        // Optimistic add — render right away. Reconcile on next poll.
        try {
            JSONObject pending = new JSONObject();
            pending.put("Id", "_local_" + System.currentTimeMillis());
            pending.put("Sender", "sdk");
            pending.put("Body", body);
            pending.put("CreatedAt", iso(System.currentTimeMillis()));
            if (!attachments.isEmpty()) {
                JSONArray arr = new JSONArray();
                for (PendingAttachment a : attachments) {
                    JSONObject ao = new JSONObject();
                    ao.put("type", a.type);
                    ao.put("localPath", a.path);
                    arr.put(ao);
                }
                pending.put("attachments", arr);
            }
            mMessages.add(pending);
            renderMessages();
        } catch (Throwable t) { Log.w(TAG, "optimistic add failed", t); }

        mNet.execute(() -> {
            try {
                // Upload attachments first via the existing multipart uploader.
                // Server returns an attachment ref we can include in the chat
                // message payload. If upload fails we still post the body so
                // the player isn't blocked; the chip is gone either way.
                JSONArray uploaded = new JSONArray();
                for (PendingAttachment a : attachments) {
                    String ref = uploadAttachment(a);
                    if (ref == null) continue;
                    JSONObject obj = new JSONObject();
                    obj.put("type", a.type);
                    obj.put("ref", ref);
                    uploaded.put(obj);
                }

                JSONObject payload = new JSONObject();
                payload.put("body", body);
                if (uploaded.length() > 0) payload.put("attachments", uploaded);
                String resp = http("POST", "/api/v1/chat/message", payload.toString());
                if (resp == null) return;
                JSONObject obj = new JSONObject(resp);
                String tid = obj.optString("ThreadId", obj.optString("threadId", null));
                if (tid != null) mThreadId = tid;
                // Refresh from server so the optimistic stub is replaced
                // with the canonical one (with real Id + timestamp).
                mUi.post(() -> fetchMessages(true));
            } catch (Throwable t) {
                Log.w(TAG, "send failed", t);
            }
        });
    }

    /**
     * Multipart-upload an attachment file to the chat upload endpoint.
     * Returns the server-assigned ref string (used in the message payload),
     * or null on failure. Bypasses {@link BugpunchUploader}'s queue because
     * chat attachments should fail-fast rather than retry across launches.
     */
    private String uploadAttachment(PendingAttachment a) {
        java.io.File f = new java.io.File(a.path);
        if (!f.exists() || f.length() == 0) return null;
        String base = BugpunchRuntime.getServerUrl();
        String key = BugpunchRuntime.getApiKey();
        if (base == null || base.isEmpty() || key == null || key.isEmpty()) return null;
        HttpURLConnection conn = null;
        try {
            String boundary = "----bp" + System.currentTimeMillis();
            URL url = new URL(base + "/api/v1/chat/upload");
            conn = (HttpURLConnection) url.openConnection();
            conn.setConnectTimeout(15000);
            conn.setReadTimeout(60000);
            conn.setRequestMethod("POST");
            conn.setDoOutput(true);
            conn.setRequestProperty("Authorization", "Bearer " + key);
            conn.setRequestProperty("X-Device-Id", BugpunchIdentity.getStableDeviceId(this));
            conn.setRequestProperty("Content-Type", "multipart/form-data; boundary=" + boundary);

            String mime = "image".equals(a.type) ? "image/png" : "video/mp4";
            try (OutputStream os = conn.getOutputStream()) {
                String pre = "--" + boundary + "\r\n"
                        + "Content-Disposition: form-data; name=\"file\"; filename=\""
                        + f.getName() + "\"\r\n"
                        + "Content-Type: " + mime + "\r\n\r\n";
                os.write(pre.getBytes("UTF-8"));
                byte[] buf = new byte[8192];
                try (java.io.FileInputStream in = new java.io.FileInputStream(f)) {
                    int n; while ((n = in.read(buf)) > 0) os.write(buf, 0, n);
                }
                os.write(("\r\n--" + boundary + "--\r\n").getBytes("UTF-8"));
            }

            int code = conn.getResponseCode();
            if (code < 200 || code >= 300) return null;
            String resp = readAll(conn);
            JSONObject o = new JSONObject(resp);
            return o.optString("ref", o.optString("Ref", null));
        } catch (Throwable t) {
            Log.w(TAG, "attachment upload failed: " + t.getMessage());
            return null;
        } finally {
            if (conn != null) conn.disconnect();
        }
    }

    // ── HTTP fetches ───────────────────────────────────────────────

    private void fetchHours() {
        mNet.execute(() -> {
            String resp = http("GET", "/api/v1/chat/hours", null);
            if (resp == null) return;
            try {
                JSONObject o = new JSONObject(resp);
                boolean disabled = o.optBoolean("disabled", false)
                        || !o.optBoolean("enabled", true);
                boolean offHours = o.optBoolean("isOffHours", false);
                String msg = o.optString("offHoursMessage",
                        "Outside support hours — we'll get back to you.");
                mUi.post(() -> {
                    mDisabled = disabled;
                    mOffHours = offHours;
                    mHoursMessage = msg;
                    if (mDisabled) {
                        mDisabledOverlay.setVisibility(View.VISIBLE);
                    } else if (mOffHours) {
                        mHoursBanner.setText(mHoursMessage);
                        mHoursBanner.setVisibility(View.VISIBLE);
                    } else {
                        mHoursBanner.setVisibility(View.GONE);
                    }
                    updateSendEnabled();
                });
            } catch (Throwable t) { Log.w(TAG, "hours parse failed", t); }
        });
    }

    private void fetchThread() {
        mNet.execute(() -> {
            String resp = http("GET", "/api/v1/chat/thread", null);
            if (resp == null) return;
            try {
                JSONObject o = new JSONObject(resp);
                JSONObject thread = o.optJSONObject("thread");
                if (thread == null) thread = o;
                String tid = thread.optString("Id", thread.optString("id", null));
                if (tid != null && !tid.isEmpty()) mThreadId = tid;
                mUi.post(() -> fetchMessages(true));
            } catch (Throwable t) {
                // 404 = no thread yet, that's fine — empty state stays.
                mUi.post(this::renderMessages);
            }
        });
    }

    private void fetchMessages(boolean fullReplace) {
        if (mDisabled) return;
        mNet.execute(() -> {
            String path = "/api/v1/chat/messages";
            if (mLastMessageAt != null && !fullReplace) {
                try {
                    path += "?since=" + URLEncoder.encode(mLastMessageAt, "UTF-8");
                } catch (Exception ignored) {}
            }
            String resp = http("GET", path, null);
            if (resp == null) return;
            try {
                JSONObject o = new JSONObject(resp);
                JSONArray arr = o.optJSONArray("messages");
                if (arr == null) arr = o.optJSONArray("Messages");
                if (arr == null) return;
                List<JSONObject> incoming = new ArrayList<>(arr.length());
                for (int i = 0; i < arr.length(); i++) incoming.add(arr.getJSONObject(i));
                mUi.post(() -> mergeMessages(incoming, fullReplace));
            } catch (Throwable t) { Log.w(TAG, "messages parse failed", t); }
        });
    }

    private void mergeMessages(List<JSONObject> incoming, boolean fullReplace) {
        if (fullReplace) {
            mMessages.clear();
        } else {
            // Drop any optimistic local stubs once a real version arrives.
            mMessages.removeIf(m -> m.optString("Id", "").startsWith("_local_"));
        }
        for (JSONObject m : incoming) {
            String id = m.optString("Id", m.optString("id", ""));
            boolean dup = false;
            for (JSONObject existing : mMessages) {
                if (id.equals(existing.optString("Id", existing.optString("id", "")))) {
                    dup = true; break;
                }
            }
            if (!dup) mMessages.add(m);
            String createdAt = m.optString("CreatedAt", m.optString("createdAt", ""));
            if (createdAt.length() > 0
                    && (mLastMessageAt == null || createdAt.compareTo(mLastMessageAt) > 0)) {
                mLastMessageAt = createdAt;
            }
        }
        renderMessages();
        markRead();
    }

    private void markRead() {
        if (mThreadId == null) return;
        mNet.execute(() -> http("POST", "/api/v1/chat/read", "{}"));
        // Clear the floating-button badge immediately rather than waiting
        // for the next BugpunchPoller tick to round-trip /chat/unread (#32).
        BugpunchReportOverlay.setUnreadCount(0);
    }

    // ── HTTP helper ────────────────────────────────────────────────

    private String http(String method, String path, String jsonBody) {
        String base = BugpunchRuntime.getServerUrl();
        String key = BugpunchRuntime.getApiKey();
        if (base == null || base.isEmpty() || key == null || key.isEmpty()) {
            Log.w(TAG, "no server config — skipping " + method + " " + path);
            return null;
        }
        HttpURLConnection conn = null;
        try {
            URL url = new URL(base + path);
            conn = (HttpURLConnection) url.openConnection();
            conn.setConnectTimeout(15000);
            conn.setReadTimeout(15000);
            conn.setRequestMethod(method);
            conn.setRequestProperty("Authorization", "Bearer " + key);
            conn.setRequestProperty("X-Device-Id",
                    BugpunchIdentity.getStableDeviceId(this));
            conn.setRequestProperty("Accept", "application/json");

            if (jsonBody != null) {
                conn.setDoOutput(true);
                conn.setRequestProperty("Content-Type", "application/json");
                byte[] bytes = jsonBody.getBytes("UTF-8");
                try (OutputStream os = conn.getOutputStream()) { os.write(bytes); }
            }

            int code = conn.getResponseCode();
            if (code < 200 || code >= 300) return null;
            return readAll(conn);
        } catch (Throwable t) {
            Log.w(TAG, method + " " + path + " failed: " + t.getMessage());
            return null;
        } finally {
            if (conn != null) conn.disconnect();
        }
    }

    private String readAll(HttpURLConnection conn) throws Exception {
        StringBuilder sb = new StringBuilder();
        try (BufferedReader r = new BufferedReader(new InputStreamReader(
                conn.getInputStream(), "UTF-8"))) {
            String line;
            while ((line = r.readLine()) != null) sb.append(line);
        }
        return sb.toString();
    }

    // ── Utility ────────────────────────────────────────────────────

    private int dp(int v) {
        return Math.round(v * getResources().getDisplayMetrics().density);
    }

    private GradientDrawable pillBg(int fill, int radius, int strokePx, int strokeColor) {
        GradientDrawable d = new GradientDrawable();
        d.setColor(fill);
        d.setCornerRadius(radius);
        if (strokePx > 0) d.setStroke(strokePx, strokeColor);
        return d;
    }

    private static String iso(long epochMs) {
        SimpleDateFormat f = new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.US);
        f.setTimeZone(TimeZone.getTimeZone("UTC"));
        return f.format(new Date(epochMs));
    }

    private static String formatTime(String iso) {
        try {
            SimpleDateFormat in = new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss", Locale.US);
            in.setTimeZone(TimeZone.getTimeZone("UTC"));
            String trimmed = iso;
            int dot = trimmed.indexOf('.');
            if (dot > 0) trimmed = trimmed.substring(0, dot);
            if (trimmed.endsWith("Z")) trimmed = trimmed.substring(0, trimmed.length() - 1);
            Date d = in.parse(trimmed);
            SimpleDateFormat out = new SimpleDateFormat("MMM d, h:mm a", Locale.getDefault());
            return out.format(d);
        } catch (Throwable t) { return iso; }
    }
}
