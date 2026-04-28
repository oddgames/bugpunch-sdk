package au.com.oddgames.bugpunch;

import android.util.Log;

import java.io.File;
import java.io.FileOutputStream;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;

/**
 * Storyboard ring — captures the last {@link #CAPACITY} UI press frames as raw
 * RGBA byte slabs in native memory plus per-press metadata (path / label /
 * scene / x,y / screen dims / timestamp).
 *
 * Pushed by C# {@code BugpunchInputCapture} after AsyncGPUReadback completes.
 * The newest slot is the rescue path for {@code screenshot_at_crash} when a
 * native signal handler fires — bp.c reads the slot pointer and dumps it as
 * a {@code .rgba} raw blob, since JPEG encoding isn't async-signal-safe.
 *
 * On non-signal-handler paths (bug report, exception, ANR) Java is alive and
 * we can encode JPEG inline via {@link #dumpToDisk}.
 *
 * <p>Memory budget: at the documented 540 long-side cap with a typical 16:9
 * phone aspect ratio, each pixel slot is &lt; 660 KB; full ring at 10 slots
 * caps at ~6.6 MB native. Square aspect (rare) tops out at ~1.17 MB / slot,
 * so worst-case is ~11.7 MB. Pixel buffers are lazily allocated on first
 * push, so a session with no UI presses pays zero pixel memory.
 *
 * <p>Header layout per slot (must stay in sync with bp.c parser):
 * <pre>
 *   offset  size  field
 *   0       8     long   tsMs
 *   8       4     float  x
 *   12      4     float  y
 *   16      4     int    screenW
 *   20      4     int    screenH
 *   24      4     int    w           pixel-buffer width
 *   28      4     int    h           pixel-buffer height
 *   32      4     int    pixelsLen   bytes valid in pixel slot (= w*h*4)
 *   36    192     char   path[192]   UTF-8 NUL-padded; UI hierarchy
 *  228     96     char   label[96]   visible button text + type prefix
 *  324     32     char   scene[32]
 *   = 356 bytes header.
 * </pre>
 */
public class BugpunchStoryboard {
    private static final String TAG = "[Bugpunch.Storyboard]";

    public static final int PATH_LEN  = 192;
    public static final int LABEL_LEN = 96;
    public static final int SCENE_LEN = 32;
    public static final int HEADER_BYTES = 8 + 4*7 + PATH_LEN + LABEL_LEN + SCENE_LEN;

    /** Ring capacity. 10 frames matches the storyboard rail's typical visible
     *  density without inflating native-memory pressure on low-tier devices. */
    public static final int CAPACITY = 10;

    private static volatile ByteBuffer[] sHeaderSlots;
    private static volatile ByteBuffer[] sPixelSlots;
    /** Width all slots are locked to — first push wins; orientation change
     *  resets the ring. */
    private static volatile int sLockedW;
    private static volatile int sLockedH;
    /** Index of the most-recently-written slot (0..CAPACITY-1) or -1 if empty. */
    private static volatile int sNewestSlot = -1;
    /** How many slots contain valid frames (capped at CAPACITY). */
    private static volatile int sCount;
    /** Next slot to overwrite. Single-writer (C# main thread via JNI). */
    private static int sNextSlot;
    private static volatile boolean sInitialised;

    /** Allocate the header ring + register pointers with the signal handler.
     *  Called from {@link BugpunchRuntime#start}. Idempotent. Pixel slots are
     *  allocated lazily on first push so a session with no presses pays zero
     *  pixel memory. */
    public static synchronized void initialise() {
        if (sInitialised) return;
        sHeaderSlots = new ByteBuffer[CAPACITY];
        sPixelSlots  = new ByteBuffer[CAPACITY];
        for (int i = 0; i < CAPACITY; i++) {
            sHeaderSlots[i] = ByteBuffer.allocateDirect(HEADER_BYTES)
                .order(ByteOrder.LITTLE_ENDIAN);
            try { BugpunchCrashHandler.setStoryboardSlotHeader(i, sHeaderSlots[i], HEADER_BYTES); }
            catch (Throwable t) { Log.w(TAG, "register storyboard header slot " + i + " failed", t); }
        }
        try { BugpunchCrashHandler.setStoryboardCapacity(CAPACITY); } catch (Throwable ignored) {}
        sInitialised = true;
    }

    /**
     * Push one frame into the ring. Called via JNI from {@code BugpunchNative.PushButtonPressFrame}
     * after the AsyncGPUReadback callback delivers the bytes. <code>bytes</code> is RGBA8 row-major
     * with no padding ({@code bytes.length >= w * h * 4}).
     */
    public static synchronized void pushFrame(long tsMs, String path, String label, String scene,
                                              float x, float y, int screenW, int screenH,
                                              int w, int h, byte[] bytes) {
        if (bytes == null || bytes.length == 0 || w <= 0 || h <= 0) return;
        if (!sInitialised) initialise();
        int expected = w * h * 4;
        if (bytes.length < expected) {
            Log.w(TAG, "pushFrame: bytes too short — got " + bytes.length + " want " + expected);
            return;
        }

        // Aspect / size lock. First push wins. Orientation change drops the
        // entire ring rather than mixing slot dimensions — bp.c needs each
        // slot's pixel size to be predictable from its header w/h alone.
        if (sLockedW == 0 && sLockedH == 0) {
            sLockedW = w; sLockedH = h;
        } else if (sLockedW != w || sLockedH != h) {
            for (int i = 0; i < CAPACITY; i++) sPixelSlots[i] = null;
            sNewestSlot = -1;
            sCount = 0;
            sNextSlot = 0;
            sLockedW = w; sLockedH = h;
            try { BugpunchCrashHandler.setStoryboardNewest(-1, 0); } catch (Throwable ignored) {}
        }

        int slot = sNextSlot;
        ByteBuffer pix = sPixelSlots[slot];
        if (pix == null || pix.capacity() < expected) {
            pix = ByteBuffer.allocateDirect(expected).order(ByteOrder.nativeOrder());
            sPixelSlots[slot] = pix;
            try { BugpunchCrashHandler.setStoryboardSlotPixels(slot, pix, expected); }
            catch (Throwable ignored) {}
        }
        pix.clear();
        pix.put(bytes, 0, expected);

        ByteBuffer hdr = sHeaderSlots[slot];
        hdr.clear();
        hdr.putLong(tsMs);
        hdr.putFloat(x);
        hdr.putFloat(y);
        hdr.putInt(screenW);
        hdr.putInt(screenH);
        hdr.putInt(w);
        hdr.putInt(h);
        hdr.putInt(expected);
        writeFixedUtf8(hdr, path,  PATH_LEN);
        writeFixedUtf8(hdr, label, LABEL_LEN);
        writeFixedUtf8(hdr, scene, SCENE_LEN);

        sNewestSlot = slot;
        sNextSlot = (slot + 1) % CAPACITY;
        if (sCount < CAPACITY) sCount++;
        try { BugpunchCrashHandler.setStoryboardNewest(slot, sCount); } catch (Throwable ignored) {}
    }

    public static int getNewestSlot() { return sNewestSlot; }
    public static int getCount() { return sCount; }
    public static int getLockedWidth() { return sLockedW; }
    public static int getLockedHeight() { return sLockedH; }

    /** True when the ring has at least one frame the live-Java helpers can
     *  hand out as a "what was on screen just before the event" picture. */
    public static boolean hasNewestFrame() {
        return sNewestSlot >= 0 && sCount > 0
            && sHeaderSlots != null && sPixelSlots != null
            && sPixelSlots[sNewestSlot] != null;
    }

    /** Epoch-ms timestamp of the newest pushed frame, or 0 if the ring is
     *  empty. Reads {@code tsMs} from the header at offset 0. */
    public static long getNewestTimestampMs() {
        int slot = sNewestSlot;
        if (slot < 0 || sCount == 0) return 0;
        ByteBuffer hdr = sHeaderSlots != null ? sHeaderSlots[slot] : null;
        return hdr != null ? hdr.getLong(0) : 0;
    }

    /** Encode the newest frame to a JPEG at {@code outputPath}. Used by the
     *  ANR watchdog (before-frame) and the bug-report path (context shot)
     *  in lieu of the old 1 Hz rolling buffer. Returns true on success.
     *  Live-Java only — the signal-handler path uses bp.c's raw dump. */
    public static synchronized boolean writeNewestJpegTo(File outputPath, int quality) {
        int slot = sNewestSlot;
        if (slot < 0 || sCount == 0) return false;
        if (sPixelSlots == null || sHeaderSlots == null) return false;
        ByteBuffer pix = sPixelSlots[slot];
        ByteBuffer hdr = sHeaderSlots[slot];
        if (pix == null || hdr == null) return false;
        FrameMeta m = readHeader(hdr);
        if (m.w <= 0 || m.h <= 0) return false;
        return encodeJpeg(pix, m.w, m.h, outputPath, quality);
    }

    /**
     * Dump the ring to disk for non-signal-handler upload paths (bug reports,
     * managed exceptions, ANR). Java is alive on these paths so we encode
     * JPEG inline. Returns the absolute path of the JSON sidecar that lists
     * the JPEG files + per-frame metadata, or null if the ring is empty.
     *
     * <p>Layout written to {@code dir}:
     * <ul>
     *   <li>{@code storyboard_<i>.jpg} — one per valid slot, oldest-to-newest order</li>
     *   <li>{@code storyboard.json} — array of frame entries with metadata
     *       (label, path, scene, x, y, screenW, screenH, w, h, tsMs, file)</li>
     * </ul>
     * The signal-handler dump (bp.c) writes parallel <code>.rgba</code> files
     * + the same JSON shape, so the next-launch crash drain doesn't need to
     * know which path produced the dump.
     */
    public static synchronized String dumpToDisk(File dir) {
        if (sCount == 0 || sNewestSlot < 0) return null;
        if (!dir.exists() && !dir.mkdirs()) {
            Log.w(TAG, "dumpToDisk: cannot create " + dir);
            return null;
        }

        // Walk in oldest-to-newest order. With sCount entries and sNewestSlot as
        // the last, oldest is (sNewestSlot - sCount + 1) mod CAPACITY.
        int newest = sNewestSlot;
        int count = sCount;
        int start = (newest - count + 1 + CAPACITY) % CAPACITY;

        StringBuilder json = new StringBuilder(512);
        json.append('[');
        boolean firstEntry = true;
        for (int i = 0; i < count; i++) {
            int slot = (start + i) % CAPACITY;
            ByteBuffer hdr = sHeaderSlots[slot];
            ByteBuffer pix = sPixelSlots[slot];
            if (hdr == null || pix == null) continue;

            FrameMeta m = readHeader(hdr);
            String fileName = "storyboard_" + i + ".jpg";
            File outFile = new File(dir, fileName);
            if (!encodeJpeg(pix, m.w, m.h, outFile, 85)) continue;

            if (!firstEntry) json.append(',');
            firstEntry = false;
            appendFrameJson(json, m, fileName);
        }
        json.append(']');

        File sidecar = new File(dir, "storyboard.json");
        try (FileOutputStream out = new FileOutputStream(sidecar)) {
            out.write(json.toString().getBytes(StandardCharsets.UTF_8));
        } catch (Throwable t) {
            Log.w(TAG, "dumpToDisk: sidecar write failed", t);
            return null;
        }
        return sidecar.getAbsolutePath();
    }

    /** Read the fixed-layout header into a struct. Used by the live-Java dump
     *  path. The signal-handler path reads the same layout from C in bp.c. */
    private static FrameMeta readHeader(ByteBuffer hdr) {
        FrameMeta m = new FrameMeta();
        hdr.position(0);
        m.tsMs    = hdr.getLong();
        m.x       = hdr.getFloat();
        m.y       = hdr.getFloat();
        m.screenW = hdr.getInt();
        m.screenH = hdr.getInt();
        m.w       = hdr.getInt();
        m.h       = hdr.getInt();
        m.pixelsLen = hdr.getInt();
        m.path  = readFixedUtf8(hdr, PATH_LEN);
        m.label = readFixedUtf8(hdr, LABEL_LEN);
        m.scene = readFixedUtf8(hdr, SCENE_LEN);
        return m;
    }

    private static boolean encodeJpeg(ByteBuffer pix, int w, int h, File out, int quality) {
        try {
            // copyPixelsFromBuffer expects a Bitmap of matching dimensions and
            // ARGB_8888 config. The byte order in the buffer is RGBA from the
            // AsyncGPUReadback path, which Bitmap maps as ABGR on little-endian
            // platforms — visually negligible (channels swap), but acceptable
            // for storyboard thumbnails. Color-correct decode happens server-side.
            android.graphics.Bitmap bmp = android.graphics.Bitmap.createBitmap(
                w, h, android.graphics.Bitmap.Config.ARGB_8888);
            pix.position(0);
            bmp.copyPixelsFromBuffer(pix);
            try (FileOutputStream fos = new FileOutputStream(out)) {
                bmp.compress(android.graphics.Bitmap.CompressFormat.JPEG,
                    Math.max(1, Math.min(100, quality)), fos);
            }
            bmp.recycle();
            return true;
        } catch (Throwable t) {
            Log.w(TAG, "encodeJpeg failed", t);
            return false;
        }
    }

    private static void appendFrameJson(StringBuilder sb, FrameMeta m, String fileName) {
        sb.append('{');
        appendKv(sb, "tsMs", m.tsMs); sb.append(',');
        appendKv(sb, "x",    m.x);    sb.append(',');
        appendKv(sb, "y",    m.y);    sb.append(',');
        appendKv(sb, "screenW", m.screenW); sb.append(',');
        appendKv(sb, "screenH", m.screenH); sb.append(',');
        appendKv(sb, "w",    m.w);    sb.append(',');
        appendKv(sb, "h",    m.h);    sb.append(',');
        appendKvStr(sb, "path",  m.path);  sb.append(',');
        appendKvStr(sb, "label", m.label); sb.append(',');
        appendKvStr(sb, "scene", m.scene); sb.append(',');
        appendKvStr(sb, "file",  fileName);
        sb.append('}');
    }

    private static void appendKv(StringBuilder sb, String k, long v) {
        sb.append('"').append(k).append("\":").append(v);
    }
    private static void appendKv(StringBuilder sb, String k, int v) {
        sb.append('"').append(k).append("\":").append(v);
    }
    private static void appendKv(StringBuilder sb, String k, float v) {
        sb.append('"').append(k).append("\":").append(v);
    }
    private static void appendKvStr(StringBuilder sb, String k, String v) {
        sb.append('"').append(k).append("\":\"");
        if (v != null) {
            for (int i = 0; i < v.length(); i++) {
                char c = v.charAt(i);
                switch (c) {
                    case '"':  sb.append("\\\""); break;
                    case '\\': sb.append("\\\\"); break;
                    case '\n': sb.append("\\n"); break;
                    case '\r': sb.append("\\r"); break;
                    case '\t': sb.append("\\t"); break;
                    default:
                        if (c < 0x20) sb.append(String.format("\\u%04x", (int)c));
                        else sb.append(c);
                }
            }
        }
        sb.append('"');
    }

    private static void writeFixedUtf8(ByteBuffer dst, String s, int maxBytes) {
        byte[] utf8 = s == null ? new byte[0] : s.getBytes(StandardCharsets.UTF_8);
        int n = Math.min(utf8.length, maxBytes);
        dst.put(utf8, 0, n);
        for (int i = n; i < maxBytes; i++) dst.put((byte)0);
    }

    private static String readFixedUtf8(ByteBuffer src, int maxBytes) {
        byte[] tmp = new byte[maxBytes];
        src.get(tmp);
        int n = 0;
        while (n < maxBytes && tmp[n] != 0) n++;
        return new String(tmp, 0, n, StandardCharsets.UTF_8);
    }

    private static class FrameMeta {
        long tsMs;
        float x, y;
        int screenW, screenH;
        int w, h;
        int pixelsLen;
        String path, label, scene;
    }
}
