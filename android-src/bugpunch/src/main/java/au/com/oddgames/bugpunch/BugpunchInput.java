package au.com.oddgames.bugpunch;

import android.util.Log;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;

/**
 * Input breadcrumb ring — captures what the user was pressing in the seconds
 * before a crash (taps, keypresses, scene transitions). Lives in native
 * memory (direct ByteBuffer) so the signal handler can dump it after a
 * SIGSEGV when the Mono heap is gone.
 *
 * Called from C# via {@code AndroidJavaClass} on every meaningful input
 * event. Each call serializes a fixed-size record into the ring slot at
 * {@code sHead} and advances the head + count pointers via the native
 * bridge so bp.c can dump the correct range at crash time.
 *
 * Entry layout (must stay in sync with {@code bp.c::s_input_*} and the
 * server-side parser):
 * <pre>
 *   offset  size  field
 *   0       8     long   timestampMs  (epoch millis)
 *   8       4     int    type         (0=touchDown,1=touchUp,2=touchMove,
 *                                      3=keyDown,4=keyUp,5=sceneChange)
 *   12      4     float  x            (screen px; ignored for key/scene)
 *   16      4     float  y
 *   20      4     int    keyCode      (Android KeyEvent key code for key events)
 *   24      192   char   path[PATH_LEN]  UTF-8 null-padded; UI hierarchy
 *   216     32    char   scene[SCENE_LEN] UTF-8 null-padded
 *   248     96    char   label[LABEL_LEN] UTF-8 null-padded; visible text
 *                                         on/around the button (e.g. the
 *                                         TMP_Text child's content), plus an
 *                                         optional "type:" prefix from the
 *                                         capture side ("Button", "Toggle",
 *                                         "Slider", …) so the dashboard can
 *                                         show "Tapped 'Buy Now' (Button)".
 *   = 344 bytes.
 * </pre>
 *
 * Capacity is fixed at 128 entries → ~31 KB native. Overwrites oldest when
 * full (circular). Writer is single-threaded (C# main thread), signal
 * handler is the only reader — no locks needed.
 */
public class BugpunchInput {
    private static final String TAG = "[Bugpunch.Input]";

    /** Entry field sizes. Must match bp.c and the server parser exactly. */
    public static final int PATH_LEN = 192;
    public static final int SCENE_LEN = 32;
    public static final int LABEL_LEN = 96;
    public static final int ENTRY_SIZE = 8 + 4 + 4 + 4 + 4 + PATH_LEN + SCENE_LEN + LABEL_LEN; // 344

    /** Ring capacity. Bumping this is cheap (linear native bytes). */
    public static final int CAPACITY = 128;

    /** Event type enum values — mirrored on the C# side in BugpunchInputCapture. */
    public static final int TYPE_TOUCH_DOWN = 0;
    public static final int TYPE_TOUCH_UP = 1;
    public static final int TYPE_TOUCH_MOVE = 2;
    public static final int TYPE_KEY_DOWN = 3;
    public static final int TYPE_KEY_UP = 4;
    public static final int TYPE_SCENE_CHANGE = 5;
    public static final int TYPE_CUSTOM = 6;

    private static volatile ByteBuffer sBuffer;
    private static volatile int sHead;
    private static volatile int sCount;
    private static boolean sInitialised;

    /**
     * Allocate the native ring + register it with the signal handler.
     * Called from {@link BugpunchRuntime#start} during SDK init.
     * Idempotent.
     */
    public static synchronized void initialise() {
        if (sInitialised) return;
        ByteBuffer buf = ByteBuffer.allocateDirect(CAPACITY * ENTRY_SIZE)
            .order(ByteOrder.LITTLE_ENDIAN);
        sBuffer = buf;
        sHead = 0;
        sCount = 0;
        try { BugpunchCrashHandler.setInputBuffer(buf, CAPACITY, ENTRY_SIZE); }
        catch (Throwable t) { Log.w(TAG, "setInputBuffer failed", t); }
        try { BugpunchCrashHandler.setInputHead(0, 0); }
        catch (Throwable ignored) {}
        sInitialised = true;
        Log.i(TAG, "input ring ready (" + CAPACITY + " x " + ENTRY_SIZE + " = "
            + (CAPACITY * ENTRY_SIZE) + " bytes)");
    }

    /**
     * Push one touch/pointer event. {@code path} should be the full Unity
     * hierarchy path (e.g. {@code "Canvas/Shop/Row[3]/BuyButton"}); the
     * server derives the button name from the last segment. {@code label}
     * carries the visible text that was on/around the tapped element —
     * "Button: Buy Now", "Toggle: Enable sound", a TMP string, etc. Any
     * field may be empty.
     */
    public static void pushTouch(int type, long timestampMs, float x, float y,
                                 String path, String scene, String label) {
        pushEntry(type, timestampMs, x, y, 0, path, scene, label);
    }

    /** Push one keyboard event. */
    public static void pushKey(int type, long timestampMs, int keyCode, String scene) {
        pushEntry(type, timestampMs, 0f, 0f, keyCode, "", scene, "");
    }

    /** Push a scene-change marker so the timeline shows transitions. */
    public static void pushSceneChange(long timestampMs, String scene) {
        pushEntry(TYPE_SCENE_CHANGE, timestampMs, 0f, 0f, 0, "", scene, "");
    }

    /**
     * Push a game-authored custom breadcrumb. Path field carries the
     * caller's category label ("flow", "iap", "net", …); label field
     * carries the human-readable message. Both are null-tolerant.
     */
    public static void pushCustom(long timestampMs, String category, String message, String scene) {
        pushEntry(TYPE_CUSTOM, timestampMs, 0f, 0f, 0,
            category == null ? "" : category,
            scene == null ? "" : scene,
            message == null ? "" : message);
    }

    /**
     * Serialise one record into the current slot and advance head/count.
     * Single-writer contract from C#; no synchronisation needed — at worst
     * a signal handler reads a half-written slot, which we bound to one
     * entry's-worth of garbage (the flags in the crash report let the
     * parser drop a torn record cleanly).
     */
    private static void pushEntry(int type, long t, float x, float y, int keyCode,
                                  String path, String scene, String label) {
        ByteBuffer buf = sBuffer;
        if (buf == null) return;
        try {
            int slot = sHead;
            int base = slot * ENTRY_SIZE;
            buf.position(base);
            buf.putLong(t);
            buf.putInt(type);
            buf.putFloat(x);
            buf.putFloat(y);
            buf.putInt(keyCode);
            writePaddedUtf8(buf, path, PATH_LEN);
            writePaddedUtf8(buf, scene, SCENE_LEN);
            writePaddedUtf8(buf, label, LABEL_LEN);

            int newHead = (slot + 1) % CAPACITY;
            int newCount = Math.min(sCount + 1, CAPACITY);
            sHead = newHead;
            sCount = newCount;
            try { BugpunchCrashHandler.setInputHead(newHead, newCount); }
            catch (Throwable ignored) {}
        } catch (Throwable t2) {
            // Never crash the game for a breadcrumb.
        }
    }

    /** Write {@code s} as UTF-8, truncated + null-padded to exactly {@code max} bytes. */
    private static void writePaddedUtf8(ByteBuffer buf, String s, int max) {
        byte[] bytes = s == null ? new byte[0] : s.getBytes(StandardCharsets.UTF_8);
        int write = Math.min(bytes.length, max - 1);  // leave room for null terminator
        if (write > 0) buf.put(bytes, 0, write);
        for (int i = write; i < max; i++) buf.put((byte) 0);
    }
}
