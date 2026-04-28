package au.com.oddgames.bugpunch;

import android.util.Log;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.util.ArrayDeque;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.Deque;
import java.util.List;
import java.util.Locale;
import java.util.TimeZone;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Tails our own process's logcat into a bounded ring buffer of <b>raw lines</b>.
 * Android 7+ restricts logcat to the calling app's own lines, which is exactly
 * what we want — Unity's Debug.Log output comes through with tag "Unity".
 *
 * <p>Zero device-side parsing: lines land in the ring as-is and travel to the
 * server as plain text. The server's logcat parser is the single source of
 * truth for splitting time / level / tag / body. This keeps the SDK small,
 * makes fixes to parsing deployable server-only, and eliminates the legacy
 * Entry-based structured path that used to build JSON on the way out.</p>
 *
 * <p>Keeps the first {@code STARTUP_CAPTURE} lines verbatim (never evicted —
 * init-time logs are disproportionately valuable for reproducing crashes) and
 * a rolling ring of the most recent {@code RECENT_CAPTURE} lines.</p>
 */
public class BugpunchLogReader {
    private static final String TAG = "[Bugpunch.LogReader]";
    /** First N log lines — captured once at startup and never dropped. */
    private static final int STARTUP_CAPTURE = 2000;
    /** Rolling ring size — last N lines before the current moment. */
    private static final int RECENT_CAPTURE = 2000;

    private static Thread sThread;
    private static final AtomicBoolean sRunning = new AtomicBoolean();
    private static List<String> sStartupBuffer = new ArrayList<>();
    private static Deque<String> sBuffer = new ArrayDeque<>();
    private static int sMaxEntries = RECENT_CAPTURE;
    private static long sSkippedCount = 0;
    private static boolean sStartupFull = false;
    private static final Object sLock = new Object();

    // Rolling mirror in NATIVE MEMORY — never touches disk during normal play.
    // During gameplay we only move bytes around in RAM: serialize the Java
    // ring to a UTF-8 text blob and memcpy into a pre-allocated direct
    // ByteBuffer whose address bp.c holds. The signal handler is the ONLY
    // path that writes it to disk (from the async-signal-safe context of a
    // dying process), and only then so the next launch's drain can attach it.
    private static final int NATIVE_BUF_CAPACITY = 1024 * 1024;
    private static volatile ByteBuffer sNativeBuffer;
    private static volatile long sLastFlushMs;
    /** Floor between native flushes (ms). Log chatter can reach hundreds of
     *  lines per second; regenerating the blob on every line would be wasteful. */
    private static final long FLUSH_INTERVAL_MS = 1000;
    /** Max unwritten entries before forcing a flush ahead of the interval. */
    private static final int FLUSH_BATCH_SIZE = 64;
    private static int sPendingSinceFlush;

    public static synchronized void start(int maxEntries) {
        if (sRunning.get()) return;
        // Spec: keep first STARTUP_CAPTURE + last RECENT_CAPTURE lines.
        // The maxEntries parameter is preserved for backwards compat but
        // ignored — config-driven tier scaling used to set it to 500/2000/5000,
        // and we no longer want tier-dependent log coverage.
        sMaxEntries = RECENT_CAPTURE;
        sStartupBuffer = new ArrayList<>();
        sStartupFull = false;
        sSkippedCount = 0;
        ensureNativeBuffer();
        sRunning.set(true);
        sThread = new Thread(new Runnable() {
            @Override public void run() { runLoop(); }
        }, "BugpunchLogReader");
        sThread.setDaemon(true);
        sThread.start();
    }

    /**
     * Allocate the native snapshot buffer and hand its address to bp.c.
     * Called once at startup — the address stays fixed for the process
     * lifetime so the signal handler can dereference it safely.
     */
    static synchronized void ensureNativeBuffer() {
        if (sNativeBuffer != null) return;
        ByteBuffer buf = ByteBuffer.allocateDirect(NATIVE_BUF_CAPACITY)
            .order(ByteOrder.nativeOrder());
        sNativeBuffer = buf;
        try { BugpunchCrashHandler.setLogsBuffer(buf); }
        catch (Throwable ignored) {}
    }

    /** Legacy no-op — the rolling mirror has moved into native memory. */
    public static void setRollingDiskPath(String path) { /* no-op */ }
    public static String getRollingDiskPath() { return null; }

    /**
     * Serialize the current ring into the native snapshot buffer. Pure
     * memory copy — no disk, no JNI per log line. Signal handler reads the
     * buffer + length when it fires.
     *
     * <p>If the text exceeds the buffer capacity the <i>oldest</i> ring
     * entries are dropped (startup lines are always kept first; recent-ring
     * tail lines are dropped in chunks) so the blob fits.</p>
     */
    public static void flushToNative() {
        ByteBuffer buf = sNativeBuffer;
        if (buf == null) return;
        String text;
        try { text = snapshotText(); } catch (Throwable t) { return; }
        byte[] bytes = text.getBytes(StandardCharsets.UTF_8);
        if (bytes.length > NATIVE_BUF_CAPACITY) {
            bytes = truncateTextToFit(text, NATIVE_BUF_CAPACITY);
        }
        try {
            buf.clear();
            buf.put(bytes);
        } catch (Throwable t) {
            return;
        }
        try { BugpunchCrashHandler.setLogsLength(bytes.length); }
        catch (Throwable ignored) {}
        sLastFlushMs = System.currentTimeMillis();
        sPendingSinceFlush = 0;
    }

    /**
     * Shrink a newline-joined text blob to fit within {@code capacity} bytes,
     * dropping oldest recent-ring lines first. Startup lines are preserved
     * because they're the most valuable debug context.
     */
    private static byte[] truncateTextToFit(String text, int capacity) {
        byte[] full = text.getBytes(StandardCharsets.UTF_8);
        if (full.length <= capacity) return full;
        final String marker = "--- log entries truncated to fit buffer ---\n";
        byte[] markerBytes = marker.getBytes(StandardCharsets.UTF_8);
        // Keep the tail of the blob (most recent context) after the marker.
        int tailSize = Math.max(0, capacity - markerBytes.length);
        // Align to a newline so we don't chop a line in half.
        int offset = full.length - tailSize;
        while (offset < full.length && full[offset] != '\n') offset++;
        if (offset < full.length) offset++; // skip past the newline
        int kept = full.length - offset;
        byte[] out = new byte[markerBytes.length + kept];
        System.arraycopy(markerBytes, 0, out, 0, markerBytes.length);
        System.arraycopy(full, offset, out, markerBytes.length, kept);
        return out;
    }

    /**
     * Inject a synthetic boundary line into the ring after a report has been
     * snapshotted for upload. The next report's log buffer will include this
     * line, and the dashboard's log viewer collapses everything above the
     * most recent boundary into a click-to-expand band so testers can see
     * "since last report" at a glance.
     *
     * <p>Format mirrors logcat threadtime so the server parser routes it
     * through the same path as a real log entry; the dedicated tag
     * {@code Bugpunch.Boundary} is what the parser keys off.</p>
     */
    public static void markBoundary(String reportType, String reportTitle) {
        if (reportType == null) reportType = "report";
        String safeTitle = reportTitle == null ? "" : reportTitle.replace('\n', ' ').replace('\r', ' ');
        SimpleDateFormat fmt = new SimpleDateFormat("MM-dd HH:mm:ss.SSS", Locale.US);
        fmt.setTimeZone(TimeZone.getTimeZone("UTC"));
        String line = fmt.format(new Date()) + "     0     0 I Bugpunch.Boundary: type="
            + reportType + " title=" + safeTitle;
        synchronized (sLock) {
            if (!sStartupFull) {
                sStartupBuffer.add(line);
                if (sStartupBuffer.size() >= STARTUP_CAPTURE) sStartupFull = true;
            } else {
                sBuffer.addLast(line);
                while (sBuffer.size() > sMaxEntries) {
                    sBuffer.removeFirst();
                    sSkippedCount++;
                }
                sPendingSinceFlush++;
            }
        }
        flushToNative();
    }

    public static synchronized void stop() {
        sRunning.set(false);
        if (sThread != null) sThread.interrupt();
        sThread = null;
    }

    /**
     * Snapshot the current ring as newline-separated raw logcat text. The
     * server's log parser splits this into structured entries on ingest.
     */
    public static String snapshotText() {
        StringBuilder sb = new StringBuilder(8192);
        synchronized (sLock) {
            for (String line : sStartupBuffer) {
                sb.append(line).append('\n');
            }
            if (sSkippedCount > 0) {
                sb.append("--- ").append(sSkippedCount)
                    .append(" log entries omitted ---\n");
            }
            for (String line : sBuffer) {
                sb.append(line).append('\n');
            }
        }
        return sb.toString();
    }

    /**
     * Extract the level char (V/D/I/W/E/F/A) from a logcat -v UTC,threadtime
     * line. Format is "MM-DD HH:MM:SS.mmm [+0000] PID TID L Tag: msg" with
     * variable-width whitespace between fields. We walk past 4 fields (date,
     * time, optional tz, pid) plus tid, and the next non-space char is the
     * level. Returns ' ' if the line doesn't match.
     */
    private static char extractLevelChar(String line) {
        int len = line.length();
        // Walk fields: date, time, ?tz, pid, tid → then level char.
        // Skip the first ws-delimited field cluster up to the level.
        int i = 0;
        // Skip date
        while (i < len && line.charAt(i) != ' ') i++;
        i = skipSpaces(line, i);
        // Skip time
        while (i < len && line.charAt(i) != ' ') i++;
        i = skipSpaces(line, i);
        // Optional timezone (+0000 / -0500). Detect by leading +/-.
        if (i < len && (line.charAt(i) == '+' || line.charAt(i) == '-')) {
            while (i < len && line.charAt(i) != ' ') i++;
            i = skipSpaces(line, i);
        }
        // Skip PID
        while (i < len && line.charAt(i) != ' ') i++;
        i = skipSpaces(line, i);
        // Skip TID
        while (i < len && line.charAt(i) != ' ') i++;
        i = skipSpaces(line, i);
        return i < len ? line.charAt(i) : ' ';
    }

    private static int skipSpaces(String s, int i) {
        while (i < s.length() && s.charAt(i) == ' ') i++;
        return i;
    }

    // ── Reader loop ──

    private static void runLoop() {
        Process proc = null;
        try {
            // -v threadtime:  "MM-DD HH:MM:SS.mmm PID TID LEVEL TAG: MSG"
            // -v UTC modifier forces timestamps in UTC instead of device-
            // local time. Without it, the server's log normalizer reads
            // device-local wall-clock and stamps it as UTC (`…Z`), which
            // shifts every parsed log entry forward by the device's TZ
            // offset — the REL view then displayed logs as hours AFTER
            // the crash instead of the few-seconds-before-crash they
            // actually were.
            // The own-app restriction is automatic on API 25+.
            proc = Runtime.getRuntime().exec(new String[] {
                "logcat", "-v", "UTC,threadtime"
            });
            BufferedReader br = new BufferedReader(new InputStreamReader(proc.getInputStream()));
            String line;
            while (sRunning.get() && (line = br.readLine()) != null) {
                if (line.isEmpty()) continue;
                // N5: tee to the native log sink. BugpunchTunnel.enqueueLogLine
                // no-ops unless the device is tagged Internal, so this is
                // effectively free when the device isn't QA-enrolled.
                //
                // Drop Verbose/Debug from the LIVE feed only — these are
                // platform/driver chatter (vulkan, Choreographer, OpenGLRenderer,
                // BufferQueueProducer, …) with no diagnostic value for the IDE
                // console. Unity's own Debug.Log surfaces at Info+, so we don't
                // lose any user logs. The crash ring buffer below stays
                // unfiltered so exception/ANR bundles retain full context.
                char level = extractLevelChar(line);
                if (level != 'V' && level != 'D') {
                    BugpunchTunnel.enqueueLogLine(line);
                }
                synchronized (sLock) {
                    if (!sStartupFull) {
                        sStartupBuffer.add(line);
                        if (sStartupBuffer.size() >= STARTUP_CAPTURE) sStartupFull = true;
                        continue;
                    }
                    sBuffer.addLast(line);
                    while (sBuffer.size() > sMaxEntries) {
                        sBuffer.removeFirst();
                        sSkippedCount++;
                    }
                    sPendingSinceFlush++;
                }
                long nowMs = System.currentTimeMillis();
                if (sNativeBuffer != null &&
                    (sPendingSinceFlush >= FLUSH_BATCH_SIZE ||
                     nowMs - sLastFlushMs >= FLUSH_INTERVAL_MS)) {
                    flushToNative();
                }
            }
        } catch (IOException e) {
            Log.w(TAG, "logcat loop ended", e);
        } finally {
            if (proc != null) proc.destroy();
        }
    }
}
