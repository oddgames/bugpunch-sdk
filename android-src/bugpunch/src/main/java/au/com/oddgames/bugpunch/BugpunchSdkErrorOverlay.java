package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.content.Context;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.util.Log;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Deque;
import java.util.List;

/**
 * On-screen banner that surfaces internal Bugpunch SDK errors so the dev/QA
 * isn't in the dark when the SDK itself fails (JNI failures, JSON parse
 * failures, swallowed exceptions, etc.).
 *
 * <p>Behaviour:
 * <ul>
 *   <li>A small red pill appears at the top of the screen with the latest
 *       error message + a count badge if there are more.</li>
 *   <li>Auto-hides after {@link #AUTO_HIDE_MS} ms of no new errors.</li>
 *   <li>Tap to expand into a card showing the full ring buffer of recent
 *       errors with their stack traces.</li>
 *   <li>Errors are deduplicated in the ring (same source+message increments
 *       a counter rather than adding a new entry).</li>
 *   <li>Always-on collection — the ring buffer fills regardless of the
 *       overlay-visible flag, so disabling the visual just hides the banner
 *       but keeps the recent-errors list available for crash reports.</li>
 * </ul>
 *
 * Safe to call from any thread — every UI mutation hops to the main thread.
 */
public class BugpunchSdkErrorOverlay {
    private static final String TAG = "[Bugpunch.SdkError]";

    private static final int RING_CAPACITY = 50;
    private static final long AUTO_HIDE_MS = 6_000L;

    public static class Entry {
        public final String source;
        public final String message;
        public final String stackTrace;
        public final long timestampMs;
        public int count;

        Entry(String source, String message, String stackTrace) {
            this.source = source;
            this.message = message;
            this.stackTrace = stackTrace;
            this.timestampMs = System.currentTimeMillis();
            this.count = 1;
        }
    }

    // Always-on ring of recent errors. Bounded; dedupes consecutive
    // source+message entries by incrementing the count on the head.
    private static final Deque<Entry> sRing = new ArrayDeque<>(RING_CAPACITY);

    private static volatile boolean sOverlayEnabled = true;
    private static volatile boolean sExpanded;

    // UI state — touch only on the main thread.
    private static View sBannerView;
    private static View sExpandedView;
    private static TextView sBannerText;
    private static TextView sBannerCount;
    private static Handler sHandler;
    private static Runnable sHideRunnable;

    /** Toggle the banner visibility flag. Errors continue to be collected. */
    public static synchronized void setOverlayEnabled(boolean enabled) {
        sOverlayEnabled = enabled;
        if (!enabled) {
            // Hide on the main thread; the toggle itself can come from any thread.
            ensureHandler();
            sHandler.post(BugpunchSdkErrorOverlay::hideAll);
        }
    }

    public static boolean isOverlayEnabled() { return sOverlayEnabled; }

    /**
     * Snapshot of the recent-errors ring. Used by the bug-report payload so
     * SDK self-diagnostics are attached to user-filed reports too.
     */
    public static synchronized List<Entry> getRecentErrors() {
        return new ArrayList<>(sRing);
    }

    /** Drop the ring (e.g. after a successful upload). */
    public static synchronized void clear() {
        sRing.clear();
        ensureHandler();
        sHandler.post(BugpunchSdkErrorOverlay::hideAll);
    }

    /**
     * Record an SDK error and (if the overlay is enabled and we have a host
     * activity) flash it on screen. Always returns immediately — UI work is
     * scheduled on the main thread.
     */
    /**
     * Convenience for native catch blocks: <pre>catch (Throwable t) { ... }</pre>.
     * Captures the exception class name + message + stack trace.
     */
    public static void reportThrowable(String source, String op, Throwable t) {
        if (t == null) { report(source, op, null); return; }
        String msg = (op == null ? "" : op + ": ")
            + t.getClass().getSimpleName()
            + (t.getMessage() != null ? ": " + t.getMessage() : "");
        java.io.StringWriter sw = new java.io.StringWriter();
        try { t.printStackTrace(new java.io.PrintWriter(sw)); }
        catch (Throwable ignore) {}
        report(source, msg, sw.toString());
    }

    public static void report(String source, String message, String stackTrace) {
        Entry pinned;
        synchronized (BugpunchSdkErrorOverlay.class) {
            // Dedupe consecutive same-source+message entries so a tight loop
            // doesn't blow the ring; older distinct entries are kept.
            Entry head = sRing.peekFirst();
            if (head != null && eq(head.source, source) && eq(head.message, message)) {
                head.count++;
                pinned = head;
            } else {
                Entry e = new Entry(
                    source == null ? "Bugpunch" : source,
                    message == null ? "" : message,
                    stackTrace == null ? "" : stackTrace);
                sRing.addFirst(e);
                while (sRing.size() > RING_CAPACITY) sRing.pollLast();
                pinned = e;
            }
        }
        if (!sOverlayEnabled) return;
        Activity activity = BugpunchRuntime.getAttachedActivity();
        if (activity == null) return;

        final Entry latest = pinned;
        ensureHandler();
        sHandler.post(() -> showOrUpdateBanner(activity, latest));
    }

    private static boolean eq(String a, String b) {
        return a == null ? b == null : a.equals(b);
    }

    private static void ensureHandler() {
        if (sHandler == null) sHandler = new Handler(Looper.getMainLooper());
    }

    // ─── UI ─────────────────────────────────────────────────────────

    private static void showOrUpdateBanner(Activity activity, Entry latest) {
        if (!sOverlayEnabled) return;
        if (sExpanded) {
            // Expanded card already visible — refresh it to include the new entry.
            rebuildExpanded(activity);
            return;
        }
        Context ctx = activity;
        try {
            if (sBannerView == null) {
                sBannerView = buildBanner(activity);
                WindowManager wm = (WindowManager) ctx.getSystemService(Context.WINDOW_SERVICE);
                WindowManager.LayoutParams wlp = new WindowManager.LayoutParams(
                        WindowManager.LayoutParams.WRAP_CONTENT,
                        WindowManager.LayoutParams.WRAP_CONTENT,
                        WindowManager.LayoutParams.TYPE_APPLICATION,
                        WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                                | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                        PixelFormat.TRANSLUCENT);
                wlp.gravity = Gravity.TOP | Gravity.CENTER_HORIZONTAL;
                wlp.y = dp(ctx, 32);
                inheritImmersiveFlags(sBannerView, activity);
                wm.addView(sBannerView, wlp);
            }
            updateBannerText(latest);
            scheduleAutoHide(activity);
        } catch (Throwable t) {
            // Don't loop back into the sink — just log to the system logger.
            Log.w(TAG, "showOrUpdateBanner failed", t);
        }
    }

    private static View buildBanner(final Activity activity) {
        Context ctx = activity;
        LinearLayout pill = new LinearLayout(ctx);
        pill.setOrientation(LinearLayout.HORIZONTAL);
        pill.setGravity(Gravity.CENTER_VERTICAL);
        pill.setPadding(dp(ctx, 14), dp(ctx, 8), dp(ctx, 14), dp(ctx, 8));
        pill.setClickable(true);
        pill.setFocusable(false);
        pill.setElevation(dp(ctx, 6));

        GradientDrawable bg = new GradientDrawable();
        bg.setColor(0xEEB00020);
        bg.setStroke(dp(ctx, 1), 0xFFFFFFFF & 0x33FFFFFF);
        bg.setCornerRadius(dp(ctx, 18));
        pill.setBackground(bg);

        TextView icon = new TextView(ctx);
        icon.setText("!");
        icon.setTypeface(Typeface.DEFAULT_BOLD);
        icon.setTextColor(Color.WHITE);
        icon.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        LinearLayout.LayoutParams iconLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        iconLp.rightMargin = dp(ctx, 8);
        pill.addView(icon, iconLp);

        sBannerText = new TextView(ctx);
        sBannerText.setTextColor(Color.WHITE);
        sBannerText.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
        sBannerText.setMaxLines(1);
        sBannerText.setEllipsize(android.text.TextUtils.TruncateAt.END);
        LinearLayout.LayoutParams textLp = new LinearLayout.LayoutParams(
                dp(ctx, 240), ViewGroup.LayoutParams.WRAP_CONTENT);
        pill.addView(sBannerText, textLp);

        sBannerCount = new TextView(ctx);
        sBannerCount.setTextColor(Color.WHITE);
        sBannerCount.setTypeface(Typeface.DEFAULT_BOLD);
        sBannerCount.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11);
        sBannerCount.setVisibility(View.GONE);
        LinearLayout.LayoutParams countLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        countLp.leftMargin = dp(ctx, 8);
        pill.addView(sBannerCount, countLp);

        pill.setOnClickListener(v -> showExpanded(activity));
        return pill;
    }

    private static void updateBannerText(Entry latest) {
        if (sBannerText == null || latest == null) return;
        String label = "[" + (latest.source == null ? "Bugpunch" : latest.source) + "] "
                + (latest.message == null ? "" : latest.message);
        sBannerText.setText(label);

        int total;
        synchronized (BugpunchSdkErrorOverlay.class) {
            total = sRing.size();
        }
        if (total > 1) {
            sBannerCount.setText("+" + (total - 1));
            sBannerCount.setVisibility(View.VISIBLE);
        } else if (latest.count > 1) {
            sBannerCount.setText("×" + latest.count);
            sBannerCount.setVisibility(View.VISIBLE);
        } else {
            sBannerCount.setVisibility(View.GONE);
        }
    }

    private static void scheduleAutoHide(final Activity activity) {
        ensureHandler();
        if (sHideRunnable != null) sHandler.removeCallbacks(sHideRunnable);
        sHideRunnable = () -> {
            if (sExpanded) return; // expanded card stays until user dismisses
            hideBanner(activity);
        };
        sHandler.postDelayed(sHideRunnable, AUTO_HIDE_MS);
    }

    private static void hideBanner(Activity activity) {
        if (sBannerView == null) return;
        try {
            WindowManager wm = (WindowManager) activity.getSystemService(Context.WINDOW_SERVICE);
            wm.removeView(sBannerView);
        } catch (Throwable t) { Log.w(TAG, "hideBanner failed", t); }
        sBannerView = null;
        sBannerText = null;
        sBannerCount = null;
    }

    private static void hideAll() {
        Activity activity = BugpunchRuntime.getAttachedActivity();
        if (activity == null) return;
        if (sExpanded) hideExpanded(activity);
        hideBanner(activity);
    }

    // ─── Expanded card — list of recent errors ─────────────────────

    private static void showExpanded(final Activity activity) {
        if (sExpanded) return;
        sExpanded = true;
        Context ctx = activity;

        try {
            if (sHideRunnable != null && sHandler != null) sHandler.removeCallbacks(sHideRunnable);
            // Hide the pill — the expanded card supersedes it.
            hideBanner(activity);

            android.widget.FrameLayout backdrop = new android.widget.FrameLayout(ctx);
            backdrop.setBackgroundColor(0xCC000000);
            backdrop.setClickable(true);
            backdrop.setOnClickListener(v -> hideExpanded(activity));

            ScrollView scroll = new ScrollView(ctx);
            scroll.setFillViewport(false);
            android.widget.FrameLayout.LayoutParams scrollLp = new android.widget.FrameLayout.LayoutParams(
                    dp(ctx, 360), ViewGroup.LayoutParams.WRAP_CONTENT);
            scrollLp.gravity = Gravity.CENTER;
            scrollLp.setMargins(0, dp(ctx, 32), 0, dp(ctx, 32));
            backdrop.addView(scroll, scrollLp);

            LinearLayout card = new LinearLayout(ctx);
            card.setOrientation(LinearLayout.VERTICAL);
            card.setPadding(dp(ctx, 16), dp(ctx, 16), dp(ctx, 16), dp(ctx, 16));
            card.setClickable(true); // stop clicks reaching backdrop
            GradientDrawable cardBg = new GradientDrawable();
            cardBg.setColor(0xF0181818);
            cardBg.setStroke(dp(ctx, 1), 0xFF444444);
            cardBg.setCornerRadius(dp(ctx, 12));
            card.setBackground(cardBg);
            scroll.addView(card, new ViewGroup.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

            TextView title = new TextView(ctx);
            title.setText("Bugpunch SDK errors");
            title.setTypeface(Typeface.DEFAULT_BOLD);
            title.setTextColor(Color.WHITE);
            title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16);
            card.addView(title);

            TextView subtitle = new TextView(ctx);
            subtitle.setText("Internal SDK problems. Tap dismiss to hide; toggle off via Bugpunch.SetSdkErrorOverlay(false).");
            subtitle.setTextColor(0xFFB8B8B8);
            subtitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11);
            LinearLayout.LayoutParams subtLp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            subtLp.topMargin = dp(ctx, 4);
            subtLp.bottomMargin = dp(ctx, 12);
            card.addView(subtitle, subtLp);

            // Tag the card so rebuildExpanded() can locate the entries column
            // without keeping another static reference around.
            LinearLayout entriesCol = new LinearLayout(ctx);
            entriesCol.setOrientation(LinearLayout.VERTICAL);
            entriesCol.setTag("entries");
            card.addView(entriesCol, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

            populateEntries(ctx, entriesCol);

            // Footer row: Clear + Dismiss
            LinearLayout footer = new LinearLayout(ctx);
            footer.setOrientation(LinearLayout.HORIZONTAL);
            footer.setGravity(Gravity.END);
            LinearLayout.LayoutParams footerLp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            footerLp.topMargin = dp(ctx, 12);
            card.addView(footer, footerLp);

            TextView clearBtn = new TextView(ctx);
            clearBtn.setText("Clear");
            clearBtn.setTextColor(0xFF999999);
            clearBtn.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
            clearBtn.setPadding(dp(ctx, 12), dp(ctx, 6), dp(ctx, 12), dp(ctx, 6));
            clearBtn.setClickable(true);
            clearBtn.setOnClickListener(v -> {
                synchronized (BugpunchSdkErrorOverlay.class) { sRing.clear(); }
                hideExpanded(activity);
            });
            footer.addView(clearBtn);

            TextView dismissBtn = new TextView(ctx);
            dismissBtn.setText("Dismiss");
            dismissBtn.setTextColor(0xFFFFFFFF);
            dismissBtn.setTypeface(Typeface.DEFAULT_BOLD);
            dismissBtn.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
            dismissBtn.setPadding(dp(ctx, 12), dp(ctx, 6), dp(ctx, 12), dp(ctx, 6));
            dismissBtn.setClickable(true);
            dismissBtn.setOnClickListener(v -> hideExpanded(activity));
            footer.addView(dismissBtn);

            WindowManager wm = (WindowManager) ctx.getSystemService(Context.WINDOW_SERVICE);
            WindowManager.LayoutParams wlp = new WindowManager.LayoutParams(
                    WindowManager.LayoutParams.MATCH_PARENT,
                    WindowManager.LayoutParams.MATCH_PARENT,
                    WindowManager.LayoutParams.TYPE_APPLICATION,
                    WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                    PixelFormat.TRANSLUCENT);
            inheritImmersiveFlags(backdrop, activity);
            wm.addView(backdrop, wlp);
            sExpandedView = backdrop;
        } catch (Throwable t) {
            sExpanded = false;
            Log.w(TAG, "showExpanded failed", t);
        }
    }

    private static void rebuildExpanded(Activity activity) {
        if (sExpandedView == null) return;
        try {
            LinearLayout entriesCol = (LinearLayout) sExpandedView.findViewWithTag("entries");
            if (entriesCol == null) return;
            entriesCol.removeAllViews();
            populateEntries(activity, entriesCol);
        } catch (Throwable t) { Log.w(TAG, "rebuildExpanded failed", t); }
    }

    private static void populateEntries(Context ctx, LinearLayout entriesCol) {
        List<Entry> snapshot;
        synchronized (BugpunchSdkErrorOverlay.class) { snapshot = new ArrayList<>(sRing); }

        if (snapshot.isEmpty()) {
            TextView empty = new TextView(ctx);
            empty.setText("No SDK errors recorded.");
            empty.setTextColor(0xFFB8B8B8);
            empty.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
            entriesCol.addView(empty);
            return;
        }

        for (Entry e : snapshot) {
            LinearLayout row = new LinearLayout(ctx);
            row.setOrientation(LinearLayout.VERTICAL);
            row.setPadding(0, dp(ctx, 6), 0, dp(ctx, 6));

            TextView head = new TextView(ctx);
            String countSuffix = e.count > 1 ? "  ×" + e.count : "";
            head.setText("[" + e.source + "] " + e.message + countSuffix);
            head.setTypeface(Typeface.DEFAULT_BOLD);
            head.setTextColor(0xFFFF6B6B);
            head.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
            row.addView(head);

            if (e.stackTrace != null && !e.stackTrace.isEmpty()) {
                TextView trace = new TextView(ctx);
                trace.setText(e.stackTrace);
                trace.setTextColor(0xFFD0D0D0);
                trace.setTextSize(TypedValue.COMPLEX_UNIT_SP, 10);
                trace.setTypeface(Typeface.MONOSPACE);
                trace.setMaxLines(8);
                trace.setEllipsize(android.text.TextUtils.TruncateAt.END);
                LinearLayout.LayoutParams traceLp = new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
                traceLp.topMargin = dp(ctx, 2);
                row.addView(trace, traceLp);
            }

            entriesCol.addView(row, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

            View div = new View(ctx);
            div.setBackgroundColor(0xFF333333);
            entriesCol.addView(div, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, dp(ctx, 1)));
        }
    }

    private static void hideExpanded(Activity activity) {
        sExpanded = false;
        if (sExpandedView == null) return;
        try {
            WindowManager wm = (WindowManager) activity.getSystemService(Context.WINDOW_SERVICE);
            wm.removeView(sExpandedView);
        } catch (Throwable t) { Log.w(TAG, "hideExpanded failed", t); }
        sExpandedView = null;
    }

    // ─── helpers ────────────────────────────────────────────────────

    private static int dp(Context ctx, int dp) {
        return (int) (dp * ctx.getResources().getDisplayMetrics().density + 0.5f);
    }

    private static void inheritImmersiveFlags(View overlay, Activity activity) {
        if (overlay == null || activity == null) return;
        try {
            overlay.setSystemUiVisibility(
                    activity.getWindow().getDecorView().getSystemUiVisibility());
        } catch (Throwable ignore) { /* best-effort */ }
    }
}
