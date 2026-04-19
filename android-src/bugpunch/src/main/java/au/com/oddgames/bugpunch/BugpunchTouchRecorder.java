package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.app.Instrumentation;
import android.os.Build;
import android.os.SystemClock;
import android.util.Log;
import android.view.ActionMode;
import android.view.KeyEvent;
import android.view.Menu;
import android.view.MenuItem;
import android.view.MotionEvent;
import android.view.SearchEvent;
import android.view.View;
import android.view.Window;
import android.view.WindowManager;
import android.view.accessibility.AccessibilityEvent;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.util.ArrayDeque;
import java.util.Deque;

/**
 * Always-on ring buffer of MotionEvent records for Bugpunch SDK (Android).
 *
 * Mirrors the iOS BugpunchTouchRecorder. Installs a Window.Callback proxy on
 * the Unity Activity's window — dispatchTouchEvent is observed then delegated
 * back to the original callback so Unity's input path is untouched.
 *
 * Clock alignment: MotionEvent.getEventTimeNanos() (API 34+) and its pre-34
 * fallback getEventTime()*1e6 return SystemClock.uptimeMillis-derived nanos,
 * the same monotonic source MediaCodec uses for Surface input PTS. So a
 * touch's nanos is directly comparable to the recorder's last-dump nanos
 * window, and (touchNs - dumpStartNs) / 1e6 is the ms offset into the MP4.
 */
public final class BugpunchTouchRecorder {
    private static final String TAG = "BugpunchTouch";

    private static final int PHASE_BEGAN     = 0;
    private static final int PHASE_MOVED     = 1;
    private static final int PHASE_ENDED     = 3;
    private static final int PHASE_CANCELLED = 4;

    private static final class Record {
        final long tNanos;
        final int  id;
        final int  phase;
        final float x;
        final float y;
        Record(long t, int id, int phase, float x, float y) {
            this.tNanos = t; this.id = id; this.phase = phase; this.x = x; this.y = y;
        }
    }

    private static int sMaxEvents = 10000;
    private static final Object sLock = new Object();
    private static final Deque<Record> sRing = new ArrayDeque<>();

    private static volatile boolean sRunning;
    private static volatile Activity sHostActivity;
    private static volatile Window.Callback sOriginalCallback;
    private static volatile Window.Callback sProxyCallback;

    // Capture size populated on first event.
    private static volatile int sCaptureW;
    private static volatile int sCaptureH;

    public static void configure(int maxEvents) {
        if (maxEvents < 100) maxEvents = 100;
        if (maxEvents > 100000) maxEvents = 100000;
        sMaxEvents = maxEvents;
    }

    /** Install proxy on the given Activity's window. Safe to call again — no-op if already running. */
    public static synchronized boolean start(Activity activity) {
        if (sRunning) return true;
        if (activity == null) { Log.w(TAG, "null activity"); return false; }
        final Activity act = activity;
        try {
            act.runOnUiThread(new Runnable() {
                @Override public void run() {
                    try {
                        Window w = act.getWindow();
                        if (w == null) { Log.w(TAG, "no window"); return; }
                        Window.Callback original = w.getCallback();
                        sOriginalCallback = original;
                        sProxyCallback = new ProxyCallback(original);
                        w.setCallback(sProxyCallback);
                        sHostActivity = act;
                        sRunning = true;
                        Log.i(TAG, "started (max " + sMaxEvents + " events)");
                    } catch (Throwable t) {
                        Log.w(TAG, "start failed on UI thread", t);
                    }
                }
            });
            return true;
        } catch (Throwable t) {
            Log.w(TAG, "start failed", t);
            return false;
        }
    }

    /** Remove proxy. Ring contents are preserved until next start. */
    public static synchronized void stop() {
        if (!sRunning) return;
        sRunning = false;
        final Activity act = sHostActivity;
        final Window.Callback original = sOriginalCallback;
        final Window.Callback proxy = sProxyCallback;
        sHostActivity = null;
        sOriginalCallback = null;
        sProxyCallback = null;
        if (act == null) return;
        try {
            act.runOnUiThread(new Runnable() {
                @Override public void run() {
                    try {
                        Window w = act.getWindow();
                        // Only restore if our proxy is still the installed callback.
                        // If something else wrapped us since, leave it alone.
                        if (w != null && w.getCallback() == proxy) {
                            w.setCallback(original);
                        }
                    } catch (Throwable t) {
                        Log.w(TAG, "stop restore failed", t);
                    }
                }
            });
        } catch (Throwable ignored) {}
        Log.i(TAG, "stopped");
    }

    public static boolean isRunning() { return sRunning; }

    public static int getCaptureWidth()  { return sCaptureW; }
    public static int getCaptureHeight() { return sCaptureH; }

    /**
     * Snapshot events whose timestamp nanos fall in [startNanos, endNanos],
     * rebased to video t=0. Returns JSON array string or null if empty.
     */
    public static String snapshotJson(long startNanos, long endNanos) {
        if (endNanos <= startNanos) return null;
        Record[] snap;
        synchronized (sLock) {
            if (sRing.isEmpty()) return null;
            snap = sRing.toArray(new Record[0]);
        }
        try {
            JSONArray arr = new JSONArray();
            for (Record r : snap) {
                if (r.tNanos < startNanos || r.tNanos > endNanos) continue;
                int tMs = (int) ((r.tNanos - startNanos) / 1_000_000L);
                if (tMs < 0) tMs = 0;
                JSONObject o = new JSONObject();
                o.put("t", tMs);
                o.put("id", r.id);
                o.put("phase", phaseName(r.phase));
                o.put("x", (double) r.x);
                o.put("y", (double) r.y);
                arr.put(o);
            }
            if (arr.length() == 0) return null;
            return arr.toString();
        } catch (JSONException e) {
            Log.w(TAG, "snapshotJson failed", e);
            return null;
        }
    }

    private static String phaseName(int p) {
        switch (p) {
            case PHASE_BEGAN:     return "began";
            case PHASE_MOVED:     return "moved";
            case PHASE_ENDED:     return "ended";
            case PHASE_CANCELLED: return "cancelled";
            default:              return "moved";
        }
    }

    private static int mapAction(int actionMasked) {
        switch (actionMasked) {
            case MotionEvent.ACTION_DOWN:
            case MotionEvent.ACTION_POINTER_DOWN:
                return PHASE_BEGAN;
            case MotionEvent.ACTION_MOVE:
                return PHASE_MOVED;
            case MotionEvent.ACTION_UP:
            case MotionEvent.ACTION_POINTER_UP:
                return PHASE_ENDED;
            case MotionEvent.ACTION_CANCEL:
                return PHASE_CANCELLED;
            default:
                return -1;
        }
    }

    private static long eventTimeNanos(MotionEvent ev) {
        if (Build.VERSION.SDK_INT >= 34) {
            try { return ev.getEventTimeNanos(); }
            catch (Throwable ignored) {}
        }
        return ev.getEventTime() * 1_000_000L;
    }

    private static void record(MotionEvent ev) {
        if (!sRunning || ev == null) return;
        try {
            if (sCaptureW == 0 || sCaptureH == 0) {
                Activity act = sHostActivity;
                if (act != null) {
                    android.util.DisplayMetrics dm = act.getResources().getDisplayMetrics();
                    sCaptureW = dm.widthPixels;
                    sCaptureH = dm.heightPixels;
                }
            }
            int action = ev.getActionMasked();
            int mapped = mapAction(action);
            if (mapped < 0) return;

            long tNs = eventTimeNanos(ev);
            int pointerCount = ev.getPointerCount();

            // For POINTER_DOWN/UP only the pointer at getActionIndex() changes;
            // everything else is effectively stationary. Android has no
            // stationary phase in our schema, so emit a single record for the
            // action pointer and one "moved" per other pointer (cheap and
            // keeps dashboard playback smooth).
            synchronized (sLock) {
                if (action == MotionEvent.ACTION_POINTER_DOWN
                    || action == MotionEvent.ACTION_POINTER_UP) {
                    int idx = ev.getActionIndex();
                    if (idx >= 0 && idx < pointerCount) {
                        sRing.addLast(new Record(tNs,
                            ev.getPointerId(idx), mapped,
                            ev.getX(idx), ev.getY(idx)));
                    }
                    for (int i = 0; i < pointerCount; i++) {
                        if (i == idx) continue;
                        sRing.addLast(new Record(tNs,
                            ev.getPointerId(i), PHASE_MOVED,
                            ev.getX(i), ev.getY(i)));
                    }
                } else {
                    for (int i = 0; i < pointerCount; i++) {
                        sRing.addLast(new Record(tNs,
                            ev.getPointerId(i), mapped,
                            ev.getX(i), ev.getY(i)));
                    }
                }
                while (sRing.size() > sMaxEvents) sRing.pollFirst();
            }
        } catch (Throwable t) {
            // Never let instrumentation break the host input path.
            Log.w(TAG, "record failed", t);
        }
    }

    /**
     * Return recent touch events (last trailMs) wrapped with screen dimensions.
     * JSON: {"events":[...], "w":1080, "h":1920}
     */
    public static String getLiveTouches(int trailMs) {
        long nowNanos = SystemClock.uptimeMillis() * 1_000_000L;
        long startNanos = nowNanos - ((long) trailMs * 1_000_000L);
        String events = snapshotJson(startNanos, nowNanos);
        return "{\"events\":" + (events != null ? events : "[]") +
                ",\"w\":" + sCaptureW + ",\"h\":" + sCaptureH + "}";
    }

    // Persistent single-pointer injection state. Used by injectPointerDown /
    // Move / Up so the dashboard can drive press-and-hold + drag from the
    // Remote IDE without synthesising whole gestures client-side.
    private static final Object sInjectLock = new Object();
    private static volatile boolean sPointerDown;
    private static volatile long sPointerDownTime;
    private static volatile float sLastPointerX;
    private static volatile float sLastPointerY;

    public static void injectPointerDown(final float x, final float y) {
        new Thread(new Runnable() {
            @Override public void run() {
                synchronized (sInjectLock) {
                    try {
                        if (sPointerDown) {
                            // Cancel any stale state first — defensive.
                            long t = SystemClock.uptimeMillis();
                            new Instrumentation().sendPointerSync(MotionEvent.obtain(
                                sPointerDownTime, t, MotionEvent.ACTION_CANCEL,
                                sLastPointerX, sLastPointerY, 0));
                        }
                        long down = SystemClock.uptimeMillis();
                        sPointerDownTime = down;
                        sLastPointerX = x;
                        sLastPointerY = y;
                        sPointerDown = true;
                        new Instrumentation().sendPointerSync(MotionEvent.obtain(
                            down, down, MotionEvent.ACTION_DOWN, x, y, 0));
                    } catch (Throwable t) {
                        Log.w(TAG, "injectPointerDown failed", t);
                    }
                }
            }
        }, "BugpunchPointerDown").start();
    }

    public static void injectPointerMove(final float x, final float y) {
        new Thread(new Runnable() {
            @Override public void run() {
                synchronized (sInjectLock) {
                    if (!sPointerDown) return;
                    try {
                        sLastPointerX = x;
                        sLastPointerY = y;
                        long now = SystemClock.uptimeMillis();
                        new Instrumentation().sendPointerSync(MotionEvent.obtain(
                            sPointerDownTime, now, MotionEvent.ACTION_MOVE, x, y, 0));
                    } catch (Throwable t) {
                        Log.w(TAG, "injectPointerMove failed", t);
                    }
                }
            }
        }, "BugpunchPointerMove").start();
    }

    public static void injectPointerUp(final float x, final float y) {
        new Thread(new Runnable() {
            @Override public void run() {
                synchronized (sInjectLock) {
                    if (!sPointerDown) return;
                    try {
                        long now = SystemClock.uptimeMillis();
                        new Instrumentation().sendPointerSync(MotionEvent.obtain(
                            sPointerDownTime, now, MotionEvent.ACTION_UP, x, y, 0));
                    } catch (Throwable t) {
                        Log.w(TAG, "injectPointerUp failed", t);
                    } finally {
                        sPointerDown = false;
                    }
                }
            }
        }, "BugpunchPointerUp").start();
    }

    public static void injectPointerCancel() {
        new Thread(new Runnable() {
            @Override public void run() {
                synchronized (sInjectLock) {
                    if (!sPointerDown) return;
                    try {
                        long now = SystemClock.uptimeMillis();
                        new Instrumentation().sendPointerSync(MotionEvent.obtain(
                            sPointerDownTime, now, MotionEvent.ACTION_CANCEL,
                            sLastPointerX, sLastPointerY, 0));
                    } catch (Throwable t) {
                        Log.w(TAG, "injectPointerCancel failed", t);
                    } finally {
                        sPointerDown = false;
                    }
                }
            }
        }, "BugpunchPointerCancel").start();
    }

    /**
     * Inject a tap at pixel coordinates (x, y) via Instrumentation.
     * Runs on a background thread — sendPointerSync blocks until the event
     * is consumed, so it must NOT be called from the UI thread.
     */
    public static void injectTap(final float x, final float y) {
        new Thread(new Runnable() {
            @Override public void run() {
                try {
                    Instrumentation inst = new Instrumentation();
                    long down = SystemClock.uptimeMillis();
                    inst.sendPointerSync(MotionEvent.obtain(down, down,
                            MotionEvent.ACTION_DOWN, x, y, 0));
                    inst.sendPointerSync(MotionEvent.obtain(down, down + 50,
                            MotionEvent.ACTION_UP, x, y, 0));
                } catch (Throwable t) {
                    Log.w(TAG, "injectTap failed", t);
                }
            }
        }, "BugpunchInjectTap").start();
    }

    /**
     * Inject a swipe from (x1,y1) to (x2,y2) over durationMs milliseconds.
     * Runs on a background thread.
     */
    public static void injectSwipe(final float x1, final float y1,
                                    final float x2, final float y2,
                                    final int durationMs) {
        new Thread(new Runnable() {
            @Override public void run() {
                try {
                    Instrumentation inst = new Instrumentation();
                    int steps = Math.max(durationMs / 16, 2);
                    long down = SystemClock.uptimeMillis();
                    inst.sendPointerSync(MotionEvent.obtain(down, down,
                            MotionEvent.ACTION_DOWN, x1, y1, 0));
                    for (int i = 1; i <= steps; i++) {
                        float f = (float) i / steps;
                        float cx = x1 + (x2 - x1) * f;
                        float cy = y1 + (y2 - y1) * f;
                        long t = down + (long) ((float) durationMs * f);
                        inst.sendPointerSync(MotionEvent.obtain(down, t,
                                MotionEvent.ACTION_MOVE, cx, cy, 0));
                    }
                    long up = down + durationMs;
                    inst.sendPointerSync(MotionEvent.obtain(down, up,
                            MotionEvent.ACTION_UP, x2, y2, 0));
                } catch (Throwable t) {
                    Log.w(TAG, "injectSwipe failed", t);
                }
            }
        }, "BugpunchInjectSwipe").start();
    }

    // ─── Window.Callback proxy ──────────────────────────────────────────
    //
    // Delegates every method to the original callback. Only dispatchTouchEvent
    // is observed — recorded before delegating, so Unity still receives the
    // event unchanged.

    private static final class ProxyCallback implements Window.Callback {
        private final Window.Callback base;
        ProxyCallback(Window.Callback base) { this.base = base; }

        @Override public boolean dispatchTouchEvent(MotionEvent event) {
            record(event);
            return base != null && base.dispatchTouchEvent(event);
        }

        @Override public boolean dispatchKeyEvent(KeyEvent event) {
            return base != null && base.dispatchKeyEvent(event);
        }
        @Override public boolean dispatchKeyShortcutEvent(KeyEvent event) {
            return base != null && base.dispatchKeyShortcutEvent(event);
        }
        @Override public boolean dispatchTrackballEvent(MotionEvent event) {
            return base != null && base.dispatchTrackballEvent(event);
        }
        @Override public boolean dispatchGenericMotionEvent(MotionEvent event) {
            return base != null && base.dispatchGenericMotionEvent(event);
        }
        @Override public boolean dispatchPopulateAccessibilityEvent(AccessibilityEvent event) {
            return base != null && base.dispatchPopulateAccessibilityEvent(event);
        }
        @Override public View onCreatePanelView(int featureId) {
            return base != null ? base.onCreatePanelView(featureId) : null;
        }
        @Override public boolean onCreatePanelMenu(int featureId, Menu menu) {
            return base != null && base.onCreatePanelMenu(featureId, menu);
        }
        @Override public boolean onPreparePanel(int featureId, View view, Menu menu) {
            return base != null && base.onPreparePanel(featureId, view, menu);
        }
        @Override public boolean onMenuOpened(int featureId, Menu menu) {
            return base != null && base.onMenuOpened(featureId, menu);
        }
        @Override public boolean onMenuItemSelected(int featureId, MenuItem item) {
            return base != null && base.onMenuItemSelected(featureId, item);
        }
        @Override public void onWindowAttributesChanged(WindowManager.LayoutParams attrs) {
            if (base != null) base.onWindowAttributesChanged(attrs);
        }
        @Override public void onContentChanged() {
            if (base != null) base.onContentChanged();
        }
        @Override public void onWindowFocusChanged(boolean hasFocus) {
            if (base != null) base.onWindowFocusChanged(hasFocus);
        }
        @Override public void onAttachedToWindow() {
            if (base != null) base.onAttachedToWindow();
        }
        @Override public void onDetachedFromWindow() {
            if (base != null) base.onDetachedFromWindow();
        }
        @Override public void onPanelClosed(int featureId, Menu menu) {
            if (base != null) base.onPanelClosed(featureId, menu);
        }
        @Override public boolean onSearchRequested() {
            return base != null && base.onSearchRequested();
        }
        @Override public boolean onSearchRequested(SearchEvent searchEvent) {
            return base != null && base.onSearchRequested(searchEvent);
        }
        @Override public ActionMode onWindowStartingActionMode(ActionMode.Callback callback) {
            return base != null ? base.onWindowStartingActionMode(callback) : null;
        }
        @Override public ActionMode onWindowStartingActionMode(ActionMode.Callback callback, int type) {
            return base != null ? base.onWindowStartingActionMode(callback, type) : null;
        }
        @Override public void onActionModeStarted(ActionMode mode) {
            if (base != null) base.onActionModeStarted(mode);
        }
        @Override public void onActionModeFinished(ActionMode mode) {
            if (base != null) base.onActionModeFinished(mode);
        }
    }

    private BugpunchTouchRecorder() {}
}
