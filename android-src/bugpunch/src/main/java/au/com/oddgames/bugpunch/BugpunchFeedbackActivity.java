package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.Editable;
import android.text.InputType;
import android.text.TextWatcher;
import android.text.util.Linkify;
import android.util.Log;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
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
 * Native feedback board — the "Request a feature" surface (with voting).
 * Replaces the v1 C# UI Toolkit {@code BugpunchFeedbackBoard} which rendered
 * inside the Unity surface. Three views in one Activity (List / Detail /
 * Submit), switched by swapping the body container's children — same shape
 * as {@link BugpunchChatActivity}.
 *
 * Endpoints (all under {@code BugpunchRuntime.getServerUrl()}):
 *   GET  /api/feedback?sort=votes              → list
 *   POST /api/issues/ingest (type=feedback_item) → create (with bypassSimilarity)
 *   POST /api/feedback/similarity              → similarity check before submit
 *   POST /api/feedback/&lt;id&gt;/vote         → toggle vote
 *   GET  /api/feedback/&lt;id&gt;/comments     → comments under a feedback item
 *   POST /api/feedback/&lt;id&gt;/comments     → post a comment
 *   POST /api/feedback/attachments             → multipart upload, returns {url,...}
 *
 * Auth: {@code Authorization: Bearer <apiKey>} + {@code X-Device-Id: <stable>}
 * — server resolves projectId from the API key.
 */
public class BugpunchFeedbackActivity extends Activity {
    private static final String TAG = "[Bugpunch.FeedbackActivity]";

    // ── Palette (resolved from theme on create) ─────────────────────
    private int COLOR_BG, COLOR_HEADER, COLOR_BORDER;
    private int COLOR_TEXT, COLOR_TEXT_DIM, COLOR_TEXT_MUTED;
    private int COLOR_ACCENT, COLOR_ACCENT_FEEDBACK, COLOR_CARD;

    private void applyTheme() {
        COLOR_BG              = BugpunchTheme.color("backdrop",       0xFF101216);
        COLOR_HEADER          = BugpunchTheme.color("cardBackground", 0xFF1B1F25);
        COLOR_CARD            = BugpunchTheme.color("cardBackground", 0xFF1B1F25);
        COLOR_BORDER          = BugpunchTheme.color("cardBorder",     0xFF2A3139);
        COLOR_TEXT            = BugpunchTheme.color("textPrimary",    0xFFF1F4F7);
        COLOR_TEXT_DIM        = BugpunchTheme.color("textSecondary",  0xFFB8C2CF);
        COLOR_TEXT_MUTED      = BugpunchTheme.color("textMuted",      0xFF8B95A2);
        COLOR_ACCENT          = BugpunchTheme.color("accentPrimary",  0xFF336199);
        COLOR_ACCENT_FEEDBACK = BugpunchTheme.color("accentFeedback", 0xFF407D4C);
    }

    // ── Views ──────────────────────────────────────────────────────
    private FrameLayout mRoot;
    private LinearLayout mBody;            // current view's content lives in here
    private View mHeader;                  // pinned header, swapped when view changes

    // Submit-view state — kept across the similarity prompt so "Post mine
    // anyway" can resubmit with the same payload.
    private String mPendingTitle;
    private String mPendingDescription;
    private final List<PendingAttachment> mSubmitDraftAttachments = new ArrayList<>();

    // Detail-view state
    private JSONObject mDetailItem;
    private final List<PendingAttachment> mCommentDraftAttachments = new ArrayList<>();

    // List state
    private final List<JSONObject> mItems = new ArrayList<>();
    private String mListSearchFilter = "";

    private final Handler mUi = new Handler(Looper.getMainLooper());
    private final ExecutorService mNet = Executors.newSingleThreadExecutor();

    /**
     * One pending attachment in either composer (submit form / comment).
     * Captured locally then uploaded multipart to /api/feedback/attachments
     * which returns a server-side URL we embed in the create / comment POST.
     */
    private static class PendingAttachment {
        final String localPath;   // local path before upload (preview only)
        String url;               // server URL after successful upload
        String mime;
        int width;
        int height;
        PendingAttachment(String localPath) { this.localPath = localPath; }
    }

    // ── Launch ─────────────────────────────────────────────────────

    public static void launch() {
        Activity host = BugpunchUnity.currentActivity();
        if (host == null) { Log.w(TAG, "no activity to launch from"); return; }
        Intent i = new Intent(host, BugpunchFeedbackActivity.class);
        i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        host.startActivity(i);
    }

    // ── Lifecycle ──────────────────────────────────────────────────

    @Override protected void onCreate(Bundle b) {
        super.onCreate(b);
        applyTheme();

        getWindow().setBackgroundDrawable(new android.graphics.drawable.ColorDrawable(COLOR_BG));
        getWindow().setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE);

        mRoot = new FrameLayout(this);
        mRoot.setBackgroundColor(COLOR_BG);
        mRoot.setLayoutParams(new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        mRoot.setFitsSystemWindows(true);
        setContentView(mRoot);

        LinearLayout column = new LinearLayout(this);
        column.setOrientation(LinearLayout.VERTICAL);
        column.setLayoutParams(new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        mRoot.addView(column);

        // Header swaps per-view (List / Detail / Submit) so back / title text
        // matches the active screen. Body container just gets its children
        // replaced — no re-layout of the activity root each switch.
        mHeader = buildListHeader();
        column.addView(mHeader);

        mBody = new LinearLayout(this);
        mBody.setOrientation(LinearLayout.VERTICAL);
        column.addView(mBody, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        showListView();
    }

    @Override protected void onDestroy() {
        mNet.shutdownNow();
        super.onDestroy();
    }

    // ── View switching ─────────────────────────────────────────────

    private void replaceHeader(View newHeader) {
        ViewGroup parent = (ViewGroup) mHeader.getParent();
        int idx = parent.indexOfChild(mHeader);
        parent.removeView(mHeader);
        parent.addView(newHeader, idx);
        mHeader = newHeader;
    }

    private void showListView() {
        replaceHeader(buildListHeader());
        mBody.removeAllViews();
        mBody.addView(buildListBody(), new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        fetchList();
    }

    private void showDetailView(JSONObject item) {
        mDetailItem = item;
        mCommentDraftAttachments.clear();
        replaceHeader(buildDetailHeader());
        mBody.removeAllViews();
        mBody.addView(buildDetailBody(), new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        fetchComments(item.optString("id", item.optString("Id", "")));
    }

    private void showSubmitView() {
        mPendingTitle = "";
        mPendingDescription = "";
        mSubmitDraftAttachments.clear();
        replaceHeader(buildSubmitHeader());
        mBody.removeAllViews();
        mBody.addView(buildSubmitBody(), new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
    }

    // ── Headers ────────────────────────────────────────────────────

    private View buildHeaderShell(String title, String leadingText, View.OnClickListener leadingClick) {
        LinearLayout bar = new LinearLayout(this);
        bar.setOrientation(LinearLayout.HORIZONTAL);
        bar.setBackgroundColor(COLOR_HEADER);
        bar.setGravity(Gravity.CENTER_VERTICAL);
        int pad = dp(12);
        bar.setPadding(pad, pad, pad, pad);

        // Leading control — close X for list, back chevron for detail/submit.
        TextView leading = new TextView(this);
        leading.setText(leadingText);
        leading.setTextColor(COLOR_TEXT_DIM);
        leading.setTextSize(TypedValue.COMPLEX_UNIT_SP, 22);
        leading.setGravity(Gravity.CENTER);
        leading.setClickable(true);
        leading.setFocusable(true);
        int cp = dp(8);
        leading.setPadding(cp, cp, cp, cp);
        leading.setOnClickListener(leadingClick);
        LinearLayout.LayoutParams lLp = new LinearLayout.LayoutParams(dp(40), dp(40));
        lLp.rightMargin = dp(8);
        bar.addView(leading, lLp);

        TextView titleView = new TextView(this);
        titleView.setText(title);
        titleView.setTextColor(COLOR_TEXT);
        titleView.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeTitle", 17));
        titleView.setTypeface(Typeface.DEFAULT_BOLD);
        LinearLayout.LayoutParams tLp = new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        bar.addView(titleView, tLp);

        return bar;
    }

    private View buildListHeader() {
        return buildHeaderShell(
                BugpunchStrings.text("feedbackTitle", "Feedback"),
                "✕",
                v -> finish());
    }

    private View buildDetailHeader() {
        return buildHeaderShell(
                BugpunchStrings.text("feedbackDetailTitle", "Feedback"),
                "‹",
                v -> showListView());
    }

    private View buildSubmitHeader() {
        return buildHeaderShell(
                BugpunchStrings.text("feedbackNewTitle", "New feedback"),
                "‹",
                v -> showListView());
    }

    // ── List view body ─────────────────────────────────────────────

    private View buildListBody() {
        LinearLayout col = new LinearLayout(this);
        col.setOrientation(LinearLayout.VERTICAL);
        col.setBackgroundColor(COLOR_BG);
        int pad = dp(12);
        col.setPadding(pad, pad, pad, pad);

        TextView subtitle = new TextView(this);
        subtitle.setText(BugpunchStrings.text("feedbackSubtitle",
                "Suggest what to build next, or vote on what others have asked for."));
        subtitle.setTextColor(COLOR_TEXT_DIM);
        subtitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12) + 1);
        subtitle.setPadding(0, 0, 0, dp(10));
        col.addView(subtitle);

        // Search field
        final EditText search = new EditText(this);
        search.setHint(BugpunchStrings.text("feedbackSearchHint", "Search feedback…"));
        search.setHintTextColor(COLOR_TEXT_MUTED);
        search.setTextColor(COLOR_TEXT);
        search.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeBody", 14));
        search.setBackground(pillBg(COLOR_HEADER, dp(8), dp(1), COLOR_BORDER));
        int sph = dp(12), spv = dp(8);
        search.setPadding(sph, spv, sph, spv);
        search.setSingleLine(true);
        search.setImeOptions(EditorInfo.IME_ACTION_SEARCH);
        LinearLayout.LayoutParams sLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        sLp.bottomMargin = dp(8);
        col.addView(search, sLp);

        // Scroll list — empty placeholder until the fetch returns.
        final ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        LinearLayout.LayoutParams scLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f);
        col.addView(scroll, scLp);

        final LinearLayout listWrap = new LinearLayout(this);
        listWrap.setOrientation(LinearLayout.VERTICAL);
        scroll.addView(listWrap, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        listWrap.setTag("listWrap");

        // Live search rebuild — same pattern as the C# board.
        search.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void onTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void afterTextChanged(Editable s) {
                mListSearchFilter = s.toString();
                rebuildListInto(listWrap);
            }
        });

        // Initial render uses the cached items if we've fetched before;
        // fetchList() will refresh asynchronously in showListView().
        rebuildListInto(listWrap);

        // "+ New feedback" pinned at the bottom.
        TextView newBtn = new TextView(this);
        newBtn.setText(BugpunchStrings.text("feedbackNewButton", "+ New feedback"));
        newBtn.setTextColor(Color.WHITE);
        newBtn.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeBody", 14));
        newBtn.setTypeface(Typeface.DEFAULT_BOLD);
        newBtn.setGravity(Gravity.CENTER);
        newBtn.setClickable(true);
        newBtn.setFocusable(true);
        int npv = dp(12), nph = dp(16);
        newBtn.setPadding(nph, npv, nph, npv);
        GradientDrawable nb = new GradientDrawable();
        nb.setColor(COLOR_ACCENT_FEEDBACK);
        nb.setCornerRadius(dp(8));
        newBtn.setBackground(nb);
        newBtn.setOnClickListener(v -> showSubmitView());
        LinearLayout.LayoutParams nLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        nLp.topMargin = dp(8);
        col.addView(newBtn, nLp);

        return col;
    }

    private void rebuildListInto(LinearLayout listWrap) {
        listWrap.removeAllViews();

        String filter = mListSearchFilter == null ? "" : mListSearchFilter.trim().toLowerCase(Locale.ROOT);
        List<JSONObject> visible = new ArrayList<>();
        for (JSONObject it : mItems) {
            if (filter.length() > 0) {
                String title = it.optString("title", it.optString("Title", "")).toLowerCase(Locale.ROOT);
                if (title.indexOf(filter) < 0) continue;
            }
            visible.add(it);
        }

        if (visible.isEmpty()) {
            TextView empty = new TextView(this);
            empty.setText(mItems.isEmpty()
                    ? BugpunchStrings.text("feedbackEmpty",
                            "No feedback yet. Be the first to suggest something!")
                    : BugpunchStrings.text("feedbackNoMatches", "No feedback matches your search."));
            empty.setTextColor(COLOR_TEXT_MUTED);
            empty.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12) + 1);
            empty.setGravity(Gravity.CENTER);
            int ep = dp(40);
            empty.setPadding(ep, ep, ep, ep);
            listWrap.addView(empty);
            return;
        }

        for (JSONObject it : visible) {
            listWrap.addView(buildListRow(it));
        }
    }

    private View buildListRow(final JSONObject item) {
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);
        GradientDrawable rb = new GradientDrawable();
        rb.setColor(COLOR_CARD);
        rb.setStroke(dp(1), COLOR_BORDER);
        rb.setCornerRadius(dp(8));
        row.setBackground(rb);
        int rp = dp(10);
        row.setPadding(dp(12), rp, dp(8), rp);
        row.setClickable(true);
        row.setFocusable(true);
        row.setOnClickListener(v -> showDetailView(item));
        LinearLayout.LayoutParams rowLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        rowLp.bottomMargin = dp(6);
        row.setLayoutParams(rowLp);

        // Text column: title + body preview + optional attachments badge
        LinearLayout textCol = new LinearLayout(this);
        textCol.setOrientation(LinearLayout.VERTICAL);
        LinearLayout.LayoutParams textLp = new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        row.addView(textCol, textLp);

        TextView titleLabel = new TextView(this);
        titleLabel.setText(item.optString("title", item.optString("Title", "(untitled)")));
        titleLabel.setTextColor(COLOR_TEXT);
        titleLabel.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeBody", 14));
        titleLabel.setTypeface(Typeface.DEFAULT_BOLD);
        textCol.addView(titleLabel);

        String body = item.optString("body", item.optString("Body", ""));
        if (body != null && body.length() > 0) {
            String preview = stripMarkdownForPreview(body);
            if (preview.length() > 160) preview = preview.substring(0, 157) + "…";
            TextView bodyLabel = new TextView(this);
            bodyLabel.setText(preview);
            bodyLabel.setTextColor(COLOR_TEXT_DIM);
            bodyLabel.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12));
            bodyLabel.setMaxLines(3);
            LinearLayout.LayoutParams bLp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            bLp.topMargin = dp(2);
            textCol.addView(bodyLabel, bLp);
        }

        JSONArray atts = item.optJSONArray("attachments");
        if (atts == null) atts = item.optJSONArray("Attachments");
        if (atts != null && atts.length() > 0) {
            TextView badge = new TextView(this);
            int n = atts.length();
            badge.setText("📎 " + n + (n == 1 ? " image" : " images"));
            badge.setTextColor(COLOR_TEXT_DIM);
            badge.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12));
            LinearLayout.LayoutParams aLp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            aLp.topMargin = dp(2);
            textCol.addView(badge, aLp);
        }

        // Vote pill — 48dp+ tap target, accent fill when voted.
        boolean hasMyVote = item.optBoolean("hasMyVote", item.optBoolean("HasMyVote", false));
        int voteCount = item.optInt("voteCount", item.optInt("VoteCount", 0));
        View votePill = buildVotePill(hasMyVote, voteCount, /*wide*/ false, () -> toggleVote(item));
        LinearLayout.LayoutParams vLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        vLp.leftMargin = dp(12);
        row.addView(votePill, vLp);

        return row;
    }

    /**
     * Vertical (▲ on top, count below) pill used in the list rows; wide=false.
     * Horizontal (▲ then "N votes") used in the detail view; wide=true.
     */
    private View buildVotePill(boolean voted, int count, boolean wide, final Runnable onTap) {
        LinearLayout pill = new LinearLayout(this);
        pill.setOrientation(wide ? LinearLayout.HORIZONTAL : LinearLayout.VERTICAL);
        pill.setGravity(Gravity.CENTER);
        pill.setClickable(true);
        pill.setFocusable(true);
        int hp = wide ? dp(12) : dp(10);
        int vp = dp(8);
        pill.setPadding(hp, vp, hp, vp);
        // 48dp+ tap target — minWidth covers both orientations.
        pill.setMinimumWidth(dp(56));
        pill.setMinimumHeight(dp(48));
        applyVoteFill(pill, voted);

        // Stop the row's click from also opening the detail when the player
        // just wants to upvote.
        pill.setOnClickListener(v -> {
            if (onTap != null) onTap.run();
        });

        TextView arrow = new TextView(this);
        arrow.setText("▲");
        arrow.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12));
        arrow.setTypeface(Typeface.DEFAULT_BOLD);
        arrow.setTextColor(voted ? COLOR_TEXT : COLOR_TEXT_DIM);
        if (wide) {
            LinearLayout.LayoutParams aLp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            aLp.rightMargin = dp(6);
            pill.addView(arrow, aLp);
        } else {
            pill.addView(arrow);
        }

        TextView countView = new TextView(this);
        if (wide) {
            countView.setText(count + (count == 1 ? " vote" : " votes"));
            countView.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12) + 1);
        } else {
            countView.setText(String.valueOf(count));
            countView.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12));
        }
        countView.setTypeface(Typeface.DEFAULT_BOLD);
        countView.setTextColor(voted ? COLOR_TEXT : COLOR_TEXT_DIM);
        pill.addView(countView);

        return pill;
    }

    private void applyVoteFill(View pill, boolean voted) {
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(voted ? COLOR_ACCENT_FEEDBACK : COLOR_BORDER);
        bg.setStroke(dp(1), voted ? COLOR_ACCENT_FEEDBACK : COLOR_BORDER);
        bg.setCornerRadius(dp(8));
        pill.setBackground(bg);
    }

    // ── Detail view body ───────────────────────────────────────────

    private View buildDetailBody() {
        // Whole detail body is scrollable so a long thread + composer can
        // co-exist without a nested scroll problem.
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(COLOR_BG);

        LinearLayout col = new LinearLayout(this);
        col.setOrientation(LinearLayout.VERTICAL);
        int pad = dp(12);
        col.setPadding(pad, pad, pad, pad);
        scroll.addView(col, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        if (mDetailItem == null) {
            TextView err = new TextView(this);
            err.setText(BugpunchStrings.text("feedbackDetailMissing", "Could not load feedback item."));
            err.setTextColor(COLOR_TEXT_MUTED);
            col.addView(err);
            return scroll;
        }

        final JSONObject item = mDetailItem;

        TextView titleLabel = new TextView(this);
        titleLabel.setText(item.optString("title", item.optString("Title", "(untitled)")));
        titleLabel.setTextColor(COLOR_TEXT);
        titleLabel.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeTitle", 17) - 1);
        titleLabel.setTypeface(Typeface.DEFAULT_BOLD);
        col.addView(titleLabel);

        // Vote row directly under the title.
        boolean hasMyVote = item.optBoolean("hasMyVote", item.optBoolean("HasMyVote", false));
        int voteCount = item.optInt("voteCount", item.optInt("VoteCount", 0));
        View pill = buildVotePill(hasMyVote, voteCount, /*wide*/ true, () -> toggleVote(item));
        LinearLayout.LayoutParams pillLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        pillLp.topMargin = dp(8); pillLp.bottomMargin = dp(10);
        col.addView(pill, pillLp);

        // Body — plain text + autolink. Per #29 we ship without nested
        // markdown rendering; URLs are made tappable via Linkify.
        String body = item.optString("body", item.optString("Body", ""));
        if (body != null && body.length() > 0) {
            TextView bodyView = new TextView(this);
            bodyView.setText(body);
            bodyView.setTextColor(COLOR_TEXT_DIM);
            bodyView.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12) + 1);
            bodyView.setLinkTextColor(0xFF8FBFFF);
            Linkify.addLinks(bodyView, Linkify.WEB_URLS);
            LinearLayout.LayoutParams bLp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            bLp.bottomMargin = dp(10);
            col.addView(bodyView, bLp);
        }

        // Body attachments — image thumbnails only (mirrors the chat).
        JSONArray atts = item.optJSONArray("attachments");
        if (atts == null) atts = item.optJSONArray("Attachments");
        if (atts != null) {
            for (int i = 0; i < atts.length(); i++) {
                JSONObject ao = atts.optJSONObject(i);
                if (ao == null) continue;
                View thumb = buildAttachmentThumb(ao);
                if (thumb != null) col.addView(thumb);
            }
        }

        // Comments header.
        TextView commentsHeader = new TextView(this);
        commentsHeader.setText(BugpunchStrings.text("feedbackComments", "Comments"));
        commentsHeader.setTextColor(COLOR_TEXT);
        commentsHeader.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeBody", 14));
        commentsHeader.setTypeface(Typeface.DEFAULT_BOLD);
        LinearLayout.LayoutParams chLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        chLp.topMargin = dp(6); chLp.bottomMargin = dp(6);
        col.addView(commentsHeader, chLp);

        final LinearLayout commentsList = new LinearLayout(this);
        commentsList.setOrientation(LinearLayout.VERTICAL);
        commentsList.setTag("commentsList");
        col.addView(commentsList);

        TextView empty = new TextView(this);
        empty.setText(BugpunchStrings.text("feedbackCommentsEmpty",
                "No comments yet. Be the first to reply."));
        empty.setTextColor(COLOR_TEXT_MUTED);
        empty.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12));
        commentsList.addView(empty);

        // Composer (matches chat — dense single-line EditText + Send circle).
        col.addView(buildCommentComposer(item));

        return scroll;
    }

    private View buildCommentComposer(final JSONObject item) {
        LinearLayout column = new LinearLayout(this);
        column.setOrientation(LinearLayout.VERTICAL);
        column.setBackgroundColor(COLOR_HEADER);
        LinearLayout.LayoutParams cLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        cLp.topMargin = dp(12);
        column.setLayoutParams(cLp);

        // Pending-attachment chip strip above the input row.
        final LinearLayout strip = new LinearLayout(this);
        strip.setOrientation(LinearLayout.HORIZONTAL);
        int sPad = dp(6);
        strip.setPadding(sPad, sPad, sPad, 0);
        strip.setVisibility(View.GONE);
        strip.setTag("commentStrip");
        column.addView(strip);

        LinearLayout bar = new LinearLayout(this);
        bar.setOrientation(LinearLayout.HORIZONTAL);
        bar.setGravity(Gravity.CENTER_VERTICAL);
        int pad = dp(8);
        bar.setPadding(pad, pad, pad, pad);
        column.addView(bar);

        // Attach (📷 screenshot only) button — keeps parity with chat composer
        // styling, but we skip the multi-option pill since v1 supports image
        // attachments only on the feedback surface.
        FrameLayout attach = new FrameLayout(this);
        GradientDrawable ab = new GradientDrawable();
        ab.setShape(GradientDrawable.OVAL);
        ab.setColor(COLOR_BORDER);
        attach.setBackground(ab);
        attach.setClickable(true);
        attach.setFocusable(true);
        attach.setOnClickListener(v -> takeScreenshotAttachment(/*isComment*/ true, strip, null));
        TextView clip = new TextView(this);
        clip.setText("📷");
        clip.setTextColor(COLOR_TEXT);
        clip.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16);
        clip.setGravity(Gravity.CENTER);
        attach.addView(clip, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT,
                Gravity.CENTER));
        LinearLayout.LayoutParams attLp = new LinearLayout.LayoutParams(dp(40), dp(40));
        attLp.rightMargin = dp(8);
        bar.addView(attach, attLp);

        final EditText composer = new EditText(this);
        composer.setHint(BugpunchStrings.text("feedbackCommentHint", "Reply to this thread…"));
        composer.setHintTextColor(COLOR_TEXT_MUTED);
        composer.setTextColor(COLOR_TEXT);
        composer.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeBody", 14));
        composer.setBackground(pillBg(COLOR_BG, dp(20), dp(1), COLOR_BORDER));
        int cpv = dp(10), cph = dp(14);
        composer.setPadding(cph, cpv, cph, cpv);
        composer.setMinHeight(dp(40));
        composer.setMaxLines(4);
        composer.setInputType(InputType.TYPE_CLASS_TEXT
                | InputType.TYPE_TEXT_FLAG_MULTI_LINE
                | InputType.TYPE_TEXT_FLAG_CAP_SENTENCES);
        composer.setImeOptions(EditorInfo.IME_ACTION_SEND);
        LinearLayout.LayoutParams compLp = new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        compLp.rightMargin = dp(8);
        bar.addView(composer, compLp);

        // Circular Send button — accent fill, matches chat.
        final FrameLayout send = new FrameLayout(this);
        GradientDrawable sb = new GradientDrawable();
        sb.setShape(GradientDrawable.OVAL);
        sb.setColor(COLOR_ACCENT_FEEDBACK);
        send.setBackground(sb);
        send.setClickable(true);
        send.setFocusable(true);
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

        Runnable updateEnabled = () -> {
            boolean ok = composer.getText().toString().trim().length() > 0
                    || !mCommentDraftAttachments.isEmpty();
            send.setAlpha(ok ? 1f : 0.4f);
            send.setClickable(ok);
        };
        updateEnabled.run();

        composer.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void onTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void afterTextChanged(Editable s) { updateEnabled.run(); }
        });

        send.setOnClickListener(v -> {
            String draft = composer.getText().toString().trim();
            if (draft.length() == 0 && mCommentDraftAttachments.isEmpty()) return;
            postComment(item, draft, () -> {
                composer.setText("");
                mCommentDraftAttachments.clear();
                rebuildCommentStrip(strip);
                updateEnabled.run();
            });
        });

        return column;
    }

    private void rebuildCommentStrip(final LinearLayout strip) {
        strip.removeAllViews();
        if (mCommentDraftAttachments.isEmpty()) {
            strip.setVisibility(View.GONE);
            return;
        }
        strip.setVisibility(View.VISIBLE);
        for (final PendingAttachment a : mCommentDraftAttachments) {
            strip.addView(buildAttachmentChip(a, () -> {
                mCommentDraftAttachments.remove(a);
                rebuildCommentStrip(strip);
            }));
        }
    }

    // ── Submit view body ───────────────────────────────────────────

    private View buildSubmitBody() {
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(COLOR_BG);

        LinearLayout col = new LinearLayout(this);
        col.setOrientation(LinearLayout.VERTICAL);
        int pad = dp(12);
        col.setPadding(pad, pad, pad, pad);
        scroll.addView(col, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        TextView intro = new TextView(this);
        intro.setText(BugpunchStrings.text("feedbackNewIntro",
                "Write a short title and (optionally) describe what you'd like."));
        intro.setTextColor(COLOR_TEXT_DIM);
        intro.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12) + 1);
        intro.setPadding(0, 0, 0, dp(12));
        col.addView(intro);

        col.addView(buildFieldLabel(BugpunchStrings.text("feedbackTitleLabel", "Title")));
        final EditText titleField = new EditText(this);
        titleField.setHint(BugpunchStrings.text("feedbackTitleHint", "One-line summary"));
        styleTextInput(titleField, /*multiline*/ false);
        col.addView(titleField);

        LinearLayout.LayoutParams gap = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, dp(8));
        col.addView(new View(this), gap);

        col.addView(buildFieldLabel(BugpunchStrings.text("feedbackDescLabel", "Description (optional)")));
        final EditText descField = new EditText(this);
        descField.setHint(BugpunchStrings.text("feedbackDescHint",
                "More detail — how it would work, why it matters…"));
        styleTextInput(descField, /*multiline*/ true);
        col.addView(descField);

        // Attachment chip strip + attach button.
        final LinearLayout strip = new LinearLayout(this);
        strip.setOrientation(LinearLayout.HORIZONTAL);
        strip.setVisibility(View.GONE);
        LinearLayout.LayoutParams stripLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        stripLp.topMargin = dp(8);
        col.addView(strip, stripLp);

        // Action row — Cancel / Attach / Submit.
        LinearLayout actions = new LinearLayout(this);
        actions.setOrientation(LinearLayout.HORIZONTAL);
        actions.setGravity(Gravity.CENTER_VERTICAL);
        LinearLayout.LayoutParams aLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        aLp.topMargin = dp(12);
        col.addView(actions, aLp);

        // Attach button (screenshot-only on feedback v1, like the chat
        // composer's v1 single-screenshot path).
        TextView attachBtn = makeOutlineButton("📷  " + BugpunchStrings.text("feedbackAttach", "Attach screenshot"));
        attachBtn.setOnClickListener(v ->
                takeScreenshotAttachment(/*isComment*/ false, null, strip));
        actions.addView(attachBtn);

        actions.addView(new View(this), new LinearLayout.LayoutParams(0, 0, 1f));

        TextView cancelBtn = makeOutlineButton(BugpunchStrings.text("feedbackCancel", "Cancel"));
        cancelBtn.setOnClickListener(v -> showListView());
        LinearLayout.LayoutParams cLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        cLp.rightMargin = dp(8);
        actions.addView(cancelBtn, cLp);

        final TextView submitBtn = makeFilledButton(BugpunchStrings.text("feedbackSubmit", "Submit"));
        submitBtn.setEnabled(false);
        submitBtn.setAlpha(0.4f);
        actions.addView(submitBtn);

        titleField.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void onTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void afterTextChanged(Editable s) {
                boolean ok = s.toString().trim().length() > 0;
                submitBtn.setEnabled(ok);
                submitBtn.setAlpha(ok ? 1f : 0.4f);
            }
        });

        submitBtn.setOnClickListener(v -> {
            String t = titleField.getText().toString().trim();
            String d = descField.getText().toString().trim();
            if (t.length() == 0) return;
            mPendingTitle = t;
            mPendingDescription = d;
            submitBtn.setEnabled(false);
            submitBtn.setAlpha(0.4f);
            submitBtn.setText(BugpunchStrings.text("feedbackChecking", "Checking…"));

            checkSimilarityThenSubmit(t, d, () -> {
                submitBtn.setEnabled(true);
                submitBtn.setAlpha(1f);
                submitBtn.setText(BugpunchStrings.text("feedbackSubmit", "Submit"));
            });
        });

        // Tag the strip so the screenshot-capture flow can re-render it.
        strip.setTag("submitStrip");
        return scroll;
    }

    private void rebuildSubmitStrip(final LinearLayout strip) {
        strip.removeAllViews();
        if (mSubmitDraftAttachments.isEmpty()) {
            strip.setVisibility(View.GONE);
            return;
        }
        strip.setVisibility(View.VISIBLE);
        for (final PendingAttachment a : mSubmitDraftAttachments) {
            strip.addView(buildAttachmentChip(a, () -> {
                mSubmitDraftAttachments.remove(a);
                rebuildSubmitStrip(strip);
            }));
        }
    }

    // ── Similarity prompt (overlay over submit view) ───────────────

    private void showSimilarityPrompt(JSONObject match, Runnable resetSubmitButton) {
        // Render as an inline replacement of the submit body so the user
        // doesn't lose context. "Vote for that" / "Post mine anyway" lead
        // back into the appropriate flow.
        replaceHeader(buildSubmitHeader());
        mBody.removeAllViews();

        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(COLOR_BG);
        mBody.addView(scroll, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        LinearLayout col = new LinearLayout(this);
        col.setOrientation(LinearLayout.VERTICAL);
        int pad = dp(16);
        col.setPadding(pad, pad, pad, pad);
        scroll.addView(col);

        TextView heading = new TextView(this);
        heading.setText(BugpunchStrings.text("feedbackSimilarTitle",
                "Similar feedback already exists"));
        heading.setTextColor(COLOR_TEXT);
        heading.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeTitle", 17) - 1);
        heading.setTypeface(Typeface.DEFAULT_BOLD);
        col.addView(heading);

        TextView intro = new TextView(this);
        intro.setText(BugpunchStrings.text("feedbackSimilarIntro", "Sounds similar to:"));
        intro.setTextColor(COLOR_TEXT_DIM);
        intro.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12) + 1);
        LinearLayout.LayoutParams iLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        iLp.topMargin = dp(8); iLp.bottomMargin = dp(6);
        col.addView(intro, iLp);

        // Match summary card.
        LinearLayout card = new LinearLayout(this);
        card.setOrientation(LinearLayout.VERTICAL);
        GradientDrawable cb = new GradientDrawable();
        cb.setColor(COLOR_CARD);
        cb.setStroke(dp(1), COLOR_BORDER);
        cb.setCornerRadius(dp(8));
        card.setBackground(cb);
        int cp = dp(12);
        card.setPadding(cp, cp, cp, cp);
        LinearLayout.LayoutParams cardLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        cardLp.bottomMargin = dp(10);
        col.addView(card, cardLp);

        TextView matchTitle = new TextView(this);
        matchTitle.setText(match.optString("title", "(untitled)"));
        matchTitle.setTextColor(COLOR_TEXT);
        matchTitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeBody", 14) + 1);
        matchTitle.setTypeface(Typeface.DEFAULT_BOLD);
        card.addView(matchTitle);

        int votes = match.optInt("voteCount", 0);
        TextView votesLabel = new TextView(this);
        votesLabel.setText(votes + (votes == 1 ? " vote" : " votes"));
        votesLabel.setTextColor(COLOR_TEXT_MUTED);
        votesLabel.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12));
        card.addView(votesLabel);

        String mbody = match.optString("body", "");
        if (mbody.length() > 0) {
            if (mbody.length() > 240) mbody = mbody.substring(0, 237) + "…";
            TextView mbView = new TextView(this);
            mbView.setText(mbody);
            mbView.setTextColor(COLOR_TEXT_DIM);
            mbView.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12));
            LinearLayout.LayoutParams mbLp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            mbLp.topMargin = dp(6);
            card.addView(mbView, mbLp);
        }

        TextView question = new TextView(this);
        question.setText(BugpunchStrings.text("feedbackSimilarQuestion",
                "Want to vote for that instead?"));
        question.setTextColor(COLOR_TEXT_DIM);
        question.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12) + 1);
        col.addView(question);

        LinearLayout actions = new LinearLayout(this);
        actions.setOrientation(LinearLayout.HORIZONTAL);
        actions.setGravity(Gravity.END);
        LinearLayout.LayoutParams aLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        aLp.topMargin = dp(12);
        col.addView(actions, aLp);

        TextView postMineBtn = makeOutlineButton(
                BugpunchStrings.text("feedbackPostAnyway", "Post mine anyway"));
        postMineBtn.setOnClickListener(v -> {
            // Bypass similarity, send a fresh create POST.
            createFeedback(mPendingTitle, mPendingDescription, /*bypass*/ true, () -> {
                if (resetSubmitButton != null) resetSubmitButton.run();
                showListView();
            });
        });
        LinearLayout.LayoutParams pmLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        pmLp.rightMargin = dp(8);
        actions.addView(postMineBtn, pmLp);

        TextView voteBtn = makeFilledButton(
                BugpunchStrings.text("feedbackVoteForThat", "Vote for that"));
        voteBtn.setOnClickListener(v -> {
            voteFor(match.optString("id", match.optString("Id", "")), () -> showListView());
        });
        actions.addView(voteBtn);
    }

    // ── Field / button helpers ─────────────────────────────────────

    private TextView buildFieldLabel(String text) {
        TextView l = new TextView(this);
        l.setText(text);
        l.setTextColor(COLOR_TEXT_DIM);
        l.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12) + 1);
        l.setPadding(0, 0, 0, dp(4));
        return l;
    }

    private void styleTextInput(EditText f, boolean multiline) {
        f.setTextColor(COLOR_TEXT);
        f.setHintTextColor(COLOR_TEXT_MUTED);
        f.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeBody", 14));
        f.setBackground(pillBg(COLOR_HEADER, dp(8), dp(1), COLOR_BORDER));
        int hp = dp(12), vp = dp(8);
        f.setPadding(hp, vp, hp, vp);
        if (multiline) {
            f.setInputType(InputType.TYPE_CLASS_TEXT
                    | InputType.TYPE_TEXT_FLAG_MULTI_LINE
                    | InputType.TYPE_TEXT_FLAG_CAP_SENTENCES);
            f.setMinLines(4);
            f.setGravity(Gravity.TOP | Gravity.START);
        } else {
            f.setSingleLine(true);
            f.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_CAP_SENTENCES);
        }
    }

    private TextView makeOutlineButton(String text) {
        TextView b = new TextView(this);
        b.setText(text);
        b.setTextColor(COLOR_TEXT_DIM);
        b.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeBody", 14));
        b.setTypeface(Typeface.DEFAULT_BOLD);
        b.setClickable(true);
        b.setFocusable(true);
        int hp = dp(14), vp = dp(10);
        b.setPadding(hp, vp, hp, vp);
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(COLOR_BORDER);
        bg.setCornerRadius(dp(8));
        b.setBackground(bg);
        return b;
    }

    private TextView makeFilledButton(String text) {
        TextView b = new TextView(this);
        b.setText(text);
        b.setTextColor(Color.WHITE);
        b.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeBody", 14));
        b.setTypeface(Typeface.DEFAULT_BOLD);
        b.setClickable(true);
        b.setFocusable(true);
        int hp = dp(16), vp = dp(10);
        b.setPadding(hp, vp, hp, vp);
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(COLOR_ACCENT_FEEDBACK);
        bg.setCornerRadius(dp(8));
        b.setBackground(bg);
        return b;
    }

    /**
     * One image attachment chip in a composer's pending-strip — thumbnail
     * with a small × in the top-right that calls back to remove it.
     */
    private View buildAttachmentChip(final PendingAttachment a, final Runnable onRemove) {
        FrameLayout chip = new FrameLayout(this);
        GradientDrawable cb = new GradientDrawable();
        cb.setColor(COLOR_BG);
        cb.setStroke(dp(1), COLOR_BORDER);
        cb.setCornerRadius(dp(8));
        chip.setBackground(cb);

        // 48dp thumbnail.
        ImageView iv = new ImageView(this);
        iv.setScaleType(ImageView.ScaleType.CENTER_CROP);
        try {
            Bitmap bmp = BitmapFactory.decodeFile(a.localPath);
            if (bmp != null) iv.setImageBitmap(bmp);
        } catch (Throwable ignored) {}
        FrameLayout.LayoutParams ivLp = new FrameLayout.LayoutParams(dp(48), dp(48));
        chip.addView(iv, ivLp);

        TextView x = new TextView(this);
        x.setText("×");
        x.setTextColor(COLOR_TEXT);
        x.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        x.setTypeface(Typeface.DEFAULT_BOLD);
        x.setClickable(true);
        x.setOnClickListener(v -> { if (onRemove != null) onRemove.run(); });
        FrameLayout.LayoutParams xLp = new FrameLayout.LayoutParams(dp(20), dp(20));
        xLp.gravity = Gravity.TOP | Gravity.END;
        xLp.rightMargin = dp(2); xLp.topMargin = dp(2);
        GradientDrawable xb = new GradientDrawable();
        xb.setShape(GradientDrawable.OVAL);
        xb.setColor(COLOR_HEADER);
        x.setBackground(xb);
        x.setGravity(Gravity.CENTER);
        chip.addView(x, xLp);

        LinearLayout.LayoutParams chipLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        chipLp.rightMargin = dp(6);
        chip.setLayoutParams(chipLp);
        return chip;
    }

    /**
     * Image attachment thumb under a feedback body / comment — same shape
     * as the chat's {@code buildAttachmentThumb}. Server-fetched URLs are
     * shown as a 📷 placeholder for v1; local-path captures (e.g. a freshly
     * snapped screenshot we haven't navigated away from yet) render the bitmap.
     */
    private View buildAttachmentThumb(JSONObject ao) {
        String localPath = ao.optString("localPath", "");
        String url = ao.optString("url", ao.optString("Url", ""));

        ImageView iv = new ImageView(this);
        iv.setScaleType(ImageView.ScaleType.CENTER_CROP);
        int side = dp(160);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(side, side);
        lp.bottomMargin = dp(6);
        iv.setLayoutParams(lp);

        GradientDrawable bg = new GradientDrawable();
        bg.setColor(COLOR_CARD);
        bg.setCornerRadius(dp(8));
        iv.setBackground(bg);
        iv.setClipToOutline(true);

        if (localPath.length() > 0) {
            try {
                Bitmap bmp = BitmapFactory.decodeFile(localPath);
                if (bmp != null) iv.setImageBitmap(bmp);
            } catch (Throwable ignored) {}
            return iv;
        }
        // Tap to open the URL in the system browser / photos app.
        if (url.length() > 0) {
            iv.setClickable(true);
            iv.setFocusable(true);
            final String openUrl = url;
            iv.setOnClickListener(v -> {
                try {
                    Intent i = new Intent(Intent.ACTION_VIEW, android.net.Uri.parse(openUrl));
                    i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                    startActivity(i);
                } catch (Throwable ignored) {}
            });
        }
        // No async network image loader on the chat side either — show a
        // simple placeholder. v1 simplification per #29.
        TextView placeholder = new TextView(this);
        placeholder.setText("📷");
        placeholder.setTextSize(TypedValue.COMPLEX_UNIT_SP, 36);
        placeholder.setGravity(Gravity.CENTER);
        placeholder.setLayoutParams(lp);
        placeholder.setBackground(bg);
        if (url.length() > 0) {
            placeholder.setClickable(true);
            final String openUrl2 = url;
            placeholder.setOnClickListener(v -> {
                try {
                    Intent i = new Intent(Intent.ACTION_VIEW, android.net.Uri.parse(openUrl2));
                    i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                    startActivity(i);
                } catch (Throwable ignored) {}
            });
        }
        return placeholder;
    }

    // ── Capture: screenshot ────────────────────────────────────────

    /**
     * Capture a screenshot and queue it as a pending attachment.
     * Mirrors {@link BugpunchChatActivity#takeScreenshotAttachment} — we
     * minimize the activity first so the captured frame is the live game
     * view, then re-show the activity once the file is on disk and the
     * matching upload returns a server URL.
     *
     * Either {@code commentStrip} or {@code submitStrip} should be non-null
     * depending on which composer requested the capture.
     */
    private void takeScreenshotAttachment(final boolean isComment,
                                          final LinearLayout commentStrip,
                                          final LinearLayout submitStrip) {
        moveTaskToBack(true);
        final String outPath = new java.io.File(
                getCacheDir(), "bp_feedback_shot_" + System.currentTimeMillis() + ".png").getAbsolutePath();
        mUi.postDelayed(() -> {
            BugpunchScreenshot.captureThen(outPath, 90, () -> mUi.post(() -> {
                final PendingAttachment pa = new PendingAttachment(outPath);
                if (isComment) {
                    mCommentDraftAttachments.add(pa);
                    if (commentStrip != null) rebuildCommentStrip(commentStrip);
                } else {
                    mSubmitDraftAttachments.add(pa);
                    if (submitStrip != null) rebuildSubmitStrip(submitStrip);
                }
                bringFeedbackBack();

                // Upload in background — we'll attach the resulting URL when
                // the user actually submits / posts, matching the C# board's
                // upload-on-attach pattern (so submit-time is fast).
                mNet.execute(() -> uploadAttachment(pa));
            }));
        }, 250L);
    }

    private void bringFeedbackBack() {
        Intent i = new Intent(this, BugpunchFeedbackActivity.class);
        i.addFlags(Intent.FLAG_ACTIVITY_REORDER_TO_FRONT
                | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        startActivity(i);
    }

    /**
     * Multipart-upload an attachment file to /api/feedback/attachments and
     * fill in the {@code url} / {@code mime} / dims on the attachment object.
     * Returns silently on failure — the chip stays in the strip and the
     * server-side create / comment will just not include this attachment.
     */
    private void uploadAttachment(PendingAttachment a) {
        java.io.File f = new java.io.File(a.localPath);
        if (!f.exists() || f.length() == 0) return;
        String base = BugpunchRuntime.getServerUrl();
        String key = BugpunchRuntime.getApiKey();
        if (base == null || base.isEmpty() || key == null || key.isEmpty()) return;
        if (f.length() > 5L * 1024L * 1024L) {
            mUi.post(() -> android.widget.Toast.makeText(this,
                    BugpunchStrings.text("feedbackImageTooLarge",
                            "Image is over 5MB — pick a smaller one."),
                    android.widget.Toast.LENGTH_SHORT).show());
            return;
        }
        HttpURLConnection conn = null;
        try {
            String boundary = "----bp" + System.currentTimeMillis();
            URL url = new URL(base + "/api/feedback/attachments");
            conn = (HttpURLConnection) url.openConnection();
            conn.setConnectTimeout(15000);
            conn.setReadTimeout(60000);
            conn.setRequestMethod("POST");
            conn.setDoOutput(true);
            conn.setRequestProperty("Authorization", "Bearer " + key);
            conn.setRequestProperty("X-Device-Id", BugpunchIdentity.getStableDeviceId(this));
            conn.setRequestProperty("Content-Type", "multipart/form-data; boundary=" + boundary);

            try (OutputStream os = conn.getOutputStream()) {
                String pre = "--" + boundary + "\r\n"
                        + "Content-Disposition: form-data; name=\"file\"; filename=\""
                        + f.getName() + "\"\r\n"
                        + "Content-Type: image/png\r\n\r\n";
                os.write(pre.getBytes("UTF-8"));
                byte[] buf = new byte[8192];
                try (java.io.FileInputStream in = new java.io.FileInputStream(f)) {
                    int n; while ((n = in.read(buf)) > 0) os.write(buf, 0, n);
                }
                os.write(("\r\n--" + boundary + "--\r\n").getBytes("UTF-8"));
            }

            int code = conn.getResponseCode();
            if (code < 200 || code >= 300) return;
            String resp = readAll(conn);
            JSONObject o = new JSONObject(resp);
            a.url = o.optString("url", o.optString("Url", null));
            a.mime = o.optString("mime", "image/png");
            a.width = o.optInt("width", 0);
            a.height = o.optInt("height", 0);
        } catch (Throwable t) {
            Log.w(TAG, "attachment upload failed: " + t.getMessage());
        } finally {
            if (conn != null) conn.disconnect();
        }
    }

    // ── HTTP fetches ───────────────────────────────────────────────

    private void fetchList() {
        mNet.execute(() -> {
            String resp = http("GET", "/api/feedback?sort=votes", null);
            if (resp == null) return;
            try {
                JSONArray arr = new JSONArray(resp);
                List<JSONObject> incoming = new ArrayList<>();
                for (int i = 0; i < arr.length(); i++) incoming.add(arr.getJSONObject(i));
                mUi.post(() -> {
                    mItems.clear();
                    mItems.addAll(incoming);
                    LinearLayout listWrap = (LinearLayout) mBody.findViewWithTag("listWrap");
                    if (listWrap != null) rebuildListInto(listWrap);
                });
            } catch (Throwable t) { Log.w(TAG, "list parse failed", t); }
        });
    }

    private void fetchComments(final String itemId) {
        if (itemId == null || itemId.length() == 0) return;
        mNet.execute(() -> {
            String resp = http("GET", "/api/feedback/" + urlEnc(itemId) + "/comments", null);
            if (resp == null) return;
            try {
                JSONObject obj = new JSONObject(resp);
                JSONArray arr = obj.optJSONArray("comments");
                if (arr == null) arr = obj.optJSONArray("Comments");
                final List<JSONObject> incoming = new ArrayList<>();
                if (arr != null) {
                    for (int i = 0; i < arr.length(); i++) incoming.add(arr.getJSONObject(i));
                }
                mUi.post(() -> renderComments(incoming));
            } catch (Throwable t) { Log.w(TAG, "comments parse failed", t); }
        });
    }

    private void renderComments(List<JSONObject> comments) {
        LinearLayout list = (LinearLayout) mBody.findViewWithTag("commentsList");
        if (list == null) return;
        list.removeAllViews();
        if (comments.isEmpty()) {
            TextView empty = new TextView(this);
            empty.setText(BugpunchStrings.text("feedbackCommentsEmpty",
                    "No comments yet. Be the first to reply."));
            empty.setTextColor(COLOR_TEXT_MUTED);
            empty.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12));
            list.addView(empty);
            return;
        }
        for (JSONObject c : comments) {
            list.addView(buildCommentRow(c));
        }
    }

    private View buildCommentRow(JSONObject c) {
        LinearLayout card = new LinearLayout(this);
        card.setOrientation(LinearLayout.VERTICAL);
        GradientDrawable cb = new GradientDrawable();
        cb.setColor(COLOR_CARD);
        cb.setStroke(dp(1), COLOR_BORDER);
        cb.setCornerRadius(dp(8));
        card.setBackground(cb);
        int p = dp(10);
        card.setPadding(p, p, p, p);
        LinearLayout.LayoutParams cardLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        cardLp.bottomMargin = dp(6);
        card.setLayoutParams(cardLp);

        boolean staff = c.optBoolean("authorIsStaff", c.optBoolean("AuthorIsStaff", false));
        String name = c.optString("authorName", c.optString("AuthorName", ""));
        if (name.length() == 0) name = "Anonymous";

        LinearLayout header = new LinearLayout(this);
        header.setOrientation(LinearLayout.HORIZONTAL);
        header.setGravity(Gravity.CENTER_VERTICAL);
        card.addView(header);

        TextView nameView = new TextView(this);
        nameView.setText(name);
        nameView.setTextColor(COLOR_TEXT);
        nameView.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12));
        nameView.setTypeface(Typeface.DEFAULT_BOLD);
        header.addView(nameView);

        if (staff) {
            TextView badge = new TextView(this);
            badge.setText("staff");
            badge.setTextColor(Color.WHITE);
            badge.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12) - 2);
            badge.setTypeface(Typeface.DEFAULT_BOLD);
            int bp = dp(4);
            badge.setPadding(bp, dp(1), bp, dp(1));
            GradientDrawable bb = new GradientDrawable();
            bb.setColor(COLOR_ACCENT);
            bb.setCornerRadius(dp(3));
            badge.setBackground(bb);
            LinearLayout.LayoutParams bLp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            bLp.leftMargin = dp(6);
            header.addView(badge, bLp);
        }

        boolean deleted = !c.isNull("deletedAt") && c.optString("deletedAt", "").length() > 0;
        if (deleted) {
            TextView del = new TextView(this);
            del.setText("[deleted]");
            del.setTextColor(COLOR_TEXT_MUTED);
            del.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12));
            del.setTypeface(Typeface.DEFAULT, Typeface.ITALIC);
            card.addView(del);
            return card;
        }

        String body = c.optString("body", c.optString("Body", ""));
        if (body.length() > 0) {
            TextView b = new TextView(this);
            b.setText(body);
            b.setTextColor(COLOR_TEXT_DIM);
            b.setTextSize(TypedValue.COMPLEX_UNIT_SP, BugpunchTheme.sp("fontSizeCaption", 12));
            b.setLinkTextColor(0xFF8FBFFF);
            Linkify.addLinks(b, Linkify.WEB_URLS);
            LinearLayout.LayoutParams blp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            blp.topMargin = dp(2);
            card.addView(b, blp);
        }

        JSONArray atts = c.optJSONArray("attachments");
        if (atts == null) atts = c.optJSONArray("Attachments");
        if (atts != null) {
            for (int i = 0; i < atts.length(); i++) {
                JSONObject ao = atts.optJSONObject(i);
                if (ao == null) continue;
                View thumb = buildAttachmentThumb(ao);
                if (thumb != null) card.addView(thumb);
            }
        }
        return card;
    }

    // ── Mutations ──────────────────────────────────────────────────

    /**
     * Toggle the player's vote for an item. Optimistic UI flip first — the
     * server reply reconciles with whatever the canonical state ends up being.
     */
    private void toggleVote(final JSONObject item) {
        // Optimistic flip.
        boolean before = item.optBoolean("hasMyVote", item.optBoolean("HasMyVote", false));
        int prev = item.optInt("voteCount", item.optInt("VoteCount", 0));
        try {
            item.put("hasMyVote", !before);
            item.put("voteCount", prev + (before ? -1 : 1));
        } catch (Throwable ignored) {}
        // Re-render whichever surface is open.
        LinearLayout listWrap = (LinearLayout) mBody.findViewWithTag("listWrap");
        if (listWrap != null) rebuildListInto(listWrap);
        if (mDetailItem == item) {
            // Re-render the detail body to refresh the wide pill.
            mBody.removeAllViews();
            mBody.addView(buildDetailBody(), new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        }

        final String id = item.optString("id", item.optString("Id", ""));
        if (id.length() == 0) return;

        mNet.execute(() -> {
            String resp = http("POST", "/api/feedback/" + urlEnc(id) + "/vote", "{}");
            if (resp == null) return;
            try {
                JSONObject o = new JSONObject(resp);
                final int voteCount = o.optInt("voteCount", item.optInt("voteCount", 0));
                final boolean hasMyVote = o.optBoolean("hasMyVote",
                        item.optBoolean("hasMyVote", false));
                mUi.post(() -> {
                    try {
                        item.put("voteCount", voteCount);
                        item.put("hasMyVote", hasMyVote);
                    } catch (Throwable ignored) {}
                    LinearLayout lw = (LinearLayout) mBody.findViewWithTag("listWrap");
                    if (lw != null) rebuildListInto(lw);
                    if (mDetailItem == item) {
                        mBody.removeAllViews();
                        mBody.addView(buildDetailBody(), new LinearLayout.LayoutParams(
                                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
                    }
                });
            } catch (Throwable t) { Log.w(TAG, "vote parse failed", t); }
        });
    }

    /**
     * Vote for the similarity-match item without ever creating a new
     * feedback item. Routed from the similarity-prompt CTA.
     */
    private void voteFor(final String id, final Runnable onDone) {
        if (id == null || id.length() == 0) {
            if (onDone != null) onDone.run();
            return;
        }
        mNet.execute(() -> {
            http("POST", "/api/feedback/" + urlEnc(id) + "/vote", "{}");
            mUi.post(() -> {
                if (onDone != null) onDone.run();
            });
        });
    }

    /**
     * Run the similarity check, then either show the similarity prompt or
     * fall through to a direct create. {@code onDone} fires either way so
     * the submit button state can be reset.
     */
    private void checkSimilarityThenSubmit(final String title, final String description,
                                           final Runnable onDone) {
        mNet.execute(() -> {
            try {
                JSONObject payload = new JSONObject();
                payload.put("title", title);
                payload.put("description", description);
                String resp = http("POST", "/api/feedback/similarity", payload.toString());
                JSONObject top = null;
                float topScore = 0f;
                if (resp != null) {
                    try {
                        JSONObject obj = new JSONObject(resp);
                        JSONArray matches = obj.optJSONArray("matches");
                        if (matches != null) {
                            for (int i = 0; i < matches.length(); i++) {
                                JSONObject m = matches.optJSONObject(i);
                                if (m == null) continue;
                                float score = (float) m.optDouble("score", 0.0);
                                if (score > 0.85f && score > topScore) {
                                    top = m;
                                    topScore = score;
                                }
                            }
                        }
                    } catch (Throwable t) {
                        Log.w(TAG, "similarity parse failed", t);
                    }
                }
                final JSONObject finalTop = top;
                mUi.post(() -> {
                    if (finalTop != null) {
                        showSimilarityPrompt(finalTop, onDone);
                    } else {
                        // No similarity hit — direct create.
                        createFeedback(title, description, /*bypass*/ false, () -> {
                            if (onDone != null) onDone.run();
                            showListView();
                        });
                    }
                });
            } catch (Throwable t) {
                Log.w(TAG, "similarity flow failed", t);
                mUi.post(() -> {
                    if (onDone != null) onDone.run();
                });
            }
        });
    }

    private void createFeedback(final String title, final String description,
                                final boolean bypassSimilarity, final Runnable onDone) {
        mNet.execute(() -> {
            try {
                JSONObject payload = new JSONObject();
                payload.put("type", "feedback_item");
                payload.put("title", title);
                payload.put("description", description);
                if (bypassSimilarity) payload.put("bypassSimilarity", true);
                payload.put("attachments", attachmentsArray(mSubmitDraftAttachments));
                String resp = http("POST", "/api/issues/ingest", payload.toString());
                final boolean ok = resp != null;
                mUi.post(() -> {
                    if (!ok) {
                        android.widget.Toast.makeText(BugpunchFeedbackActivity.this,
                                BugpunchStrings.text("feedbackSubmitFailed",
                                        "Could not submit feedback. Please try again."),
                                android.widget.Toast.LENGTH_SHORT).show();
                        if (onDone != null) onDone.run();
                        return;
                    }
                    mSubmitDraftAttachments.clear();
                    if (onDone != null) onDone.run();
                });
            } catch (Throwable t) {
                Log.w(TAG, "create failed", t);
                mUi.post(() -> { if (onDone != null) onDone.run(); });
            }
        });
    }

    private void postComment(final JSONObject item, final String body, final Runnable onSent) {
        final String id = item.optString("id", item.optString("Id", ""));
        if (id.length() == 0) return;
        // Wait for in-flight uploads — if the user attached a screenshot a
        // moment ago and hits Send before the upload completes, the comment
        // payload would lose the URL. Block the network thread (not UI) on
        // each attachment until it has a URL, with a short timeout.
        mNet.execute(() -> {
            try {
                final List<PendingAttachment> snapshot = new ArrayList<>(mCommentDraftAttachments);
                long deadline = System.currentTimeMillis() + 8000L;
                for (PendingAttachment a : snapshot) {
                    while (a.url == null && System.currentTimeMillis() < deadline) {
                        try { Thread.sleep(150L); } catch (InterruptedException ignored) { break; }
                    }
                }
                JSONObject payload = new JSONObject();
                payload.put("body", body);
                payload.put("playerName", "");
                payload.put("attachments", attachmentsArray(snapshot));
                String resp = http("POST", "/api/feedback/" + urlEnc(id) + "/comments", payload.toString());
                final boolean ok = resp != null;
                mUi.post(() -> {
                    if (ok) {
                        if (onSent != null) onSent.run();
                        fetchComments(id);
                    } else {
                        android.widget.Toast.makeText(BugpunchFeedbackActivity.this,
                                BugpunchStrings.text("feedbackCommentFailed",
                                        "Could not post comment. Please try again."),
                                android.widget.Toast.LENGTH_SHORT).show();
                    }
                });
            } catch (Throwable t) {
                Log.w(TAG, "comment failed", t);
            }
        });
    }

    /**
     * Build the attachment JSON array the server expects on POST /api/issues/ingest
     * and POST /api/feedback/&lt;id&gt;/comments. Skips any attachment whose
     * upload didn't complete in time — better to post fewer thumbs than to
     * lose the message body.
     */
    private JSONArray attachmentsArray(List<PendingAttachment> list) {
        JSONArray arr = new JSONArray();
        if (list == null) return arr;
        for (PendingAttachment a : list) {
            if (a.url == null || a.url.length() == 0) continue;
            try {
                JSONObject ao = new JSONObject();
                ao.put("type", "image");
                ao.put("url", a.url);
                ao.put("mime", a.mime != null ? a.mime : "image/png");
                if (a.width > 0) ao.put("width", a.width);
                if (a.height > 0) ao.put("height", a.height);
                arr.put(ao);
            } catch (Throwable ignored) {}
        }
        return arr;
    }

    // ── Markdown preview stripping ─────────────────────────────────

    /**
     * Mirrors the C# {@code BugpunchMarkdownRenderer.StripForPreview} so the
     * list previews look the same as on the dashboard / Editor fallback —
     * markers go away but content stays.
     */
    private static String stripMarkdownForPreview(String s) {
        if (s == null || s.length() == 0) return "";
        s = s.replaceAll("`([^`]+)`", "$1");
        s = s.replaceAll("\\*\\*([^*]+)\\*\\*", "$1");
        s = s.replaceAll("\\*([^*]+)\\*", "$1");
        s = s.replaceAll("\\[([^\\]]+)\\]\\((https?://[^)]+)\\)", "$1");
        s = s.replaceAll("(?m)^\\s*[-*]\\s+", "• ");
        s = s.replaceAll("(?m)^\\s*\\d+\\.\\s+", "");
        return s;
    }

    // ── HTTP helper (mirrors chat) ─────────────────────────────────

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

    private static String urlEnc(String s) {
        try { return URLEncoder.encode(s, "UTF-8"); }
        catch (Throwable t) { return s; }
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

    @SuppressWarnings("unused")
    private static String iso(long epochMs) {
        SimpleDateFormat f = new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.US);
        f.setTimeZone(TimeZone.getTimeZone("UTC"));
        return f.format(new Date(epochMs));
    }
}
