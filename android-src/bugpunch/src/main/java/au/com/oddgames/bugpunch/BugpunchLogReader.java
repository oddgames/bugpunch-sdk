package au.com.oddgames.bugpunch;

import android.util.Log;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Deque;
import java.util.List;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Tails our own process's logcat into a bounded ring buffer. Android 7+
 * restricts logcat to the calling app's own lines which is exactly what we
 * want — Unity's Debug.Log output comes through with tag "Unity".
 *
 * Keeps the first STARTUP_SIZE entries separately so they're never evicted,
 * then a rolling ring of the most recent entries. On snapshot, emits:
 *   [startup entries] + [breaker with skipped count] + [recent entries]
 */
public class BugpunchLogReader {
    private static final String TAG = "BugpunchLogReader";
    private static final int STARTUP_SIZE = 2000;

    private static Thread sThread;
    private static final AtomicBoolean sRunning = new AtomicBoolean();
    private static List<Entry> sStartupBuffer = new ArrayList<>();
    private static Deque<Entry> sBuffer = new ArrayDeque<>();
    private static int sMaxEntries = 2000;
    private static long sSkippedCount = 0;
    private static boolean sStartupFull = false;
    private static long sTotalIngested = 0;
    private static final Object sLock = new Object();

    private static class Entry {
        final long time;
        final String level;
        final String tag;
        final String message;
        int repeat;
        Entry(long t, String l, String tg, String m) { time = t; level = l; tag = tg; message = m; repeat = 1; }
        boolean matches(Entry o) {
            return o != null && level.equals(o.level) && tag.equals(o.tag) && message.equals(o.message);
        }
    }

    public static synchronized void start(int maxEntries) {
        if (sRunning.get()) return;
        sMaxEntries = Math.max(50, maxEntries);
        sStartupBuffer = new ArrayList<>();
        sStartupFull = false;
        sSkippedCount = 0;
        sTotalIngested = 0;
        sRunning.set(true);
        sThread = new Thread(new Runnable() {
            @Override public void run() { runLoop(); }
        }, "BugpunchLogReader");
        sThread.setDaemon(true);
        sThread.start();
    }

    public static synchronized void stop() {
        sRunning.set(false);
        if (sThread != null) sThread.interrupt();
        sThread = null;
    }

    /** Snapshot the current buffer as a JSON array string. */
    public static String snapshotJson() {
        StringBuilder sb = new StringBuilder(8192);
        sb.append('[');
        synchronized (sLock) {
            boolean first = true;
            // 1) Startup entries (always preserved).
            for (Entry e : sStartupBuffer) {
                if (!first) sb.append(',');
                first = false;
                appendEntry(sb, e);
            }
            // 2) Breaker if entries were skipped between startup and ring.
            if (sSkippedCount > 0) {
                if (!first) sb.append(',');
                first = false;
                sb.append("{\"type\":\"Log\",\"message\":\"--- ")
                    .append(sSkippedCount)
                    .append(" log entries omitted ---\",\"stackTrace\":\"\"}");
            }
            // 3) Recent ring buffer entries.
            for (Entry e : sBuffer) {
                if (!first) sb.append(',');
                first = false;
                appendEntry(sb, e);
            }
        }
        sb.append(']');
        return sb.toString();
    }

    private static void appendEntry(StringBuilder sb, Entry e) {
        sb.append('{');
        sb.append("\"time\":").append(e.time).append(',');
        sb.append("\"type\":\"").append(jsonEsc(mapType(e.level))).append("\",");
        sb.append("\"message\":\"").append(jsonEsc(e.tag + ": " + e.message)).append("\",");
        sb.append("\"stackTrace\":\"\"");
        if (e.repeat > 1) sb.append(",\"repeat\":").append(e.repeat);
        sb.append('}');
    }

    // ── Reader loop ──

    private static void runLoop() {
        Process proc = null;
        try {
            // -v threadtime: "MM-DD HH:MM:SS.mmm PID TID LEVEL TAG: MSG"
            // The own-app restriction is automatic on API 25+.
            proc = Runtime.getRuntime().exec(new String[] {
                "logcat", "-v", "threadtime"
            });
            BufferedReader br = new BufferedReader(new InputStreamReader(proc.getInputStream()));
            String line;
            while (sRunning.get() && (line = br.readLine()) != null) {
                Entry e = parse(line);
                if (e == null) continue;
                synchronized (sLock) {
                    sTotalIngested++;
                    // Dedup: collapse consecutive identical messages.
                    // Check ring buffer tail first, then startup buffer tail.
                    Entry last = sBuffer.peekLast();
                    if (last == null && !sStartupBuffer.isEmpty())
                        last = sStartupBuffer.get(sStartupBuffer.size() - 1);
                    if (last != null && last.matches(e)) {
                        last.repeat++;
                        continue;
                    }
                    // Fill startup buffer first.
                    if (!sStartupFull) {
                        sStartupBuffer.add(e);
                        if (sStartupBuffer.size() >= STARTUP_SIZE) sStartupFull = true;
                        continue;
                    }
                    // Ring buffer.
                    sBuffer.addLast(e);
                    while (sBuffer.size() > sMaxEntries) {
                        sBuffer.removeFirst();
                        sSkippedCount++;
                    }
                }
            }
        } catch (IOException e) {
            Log.w(TAG, "logcat loop ended", e);
        } finally {
            if (proc != null) proc.destroy();
        }
    }

    private static Entry parse(String line) {
        // "04-15 12:34:56.789 12345 12345 I Unity   : message text"
        if (line == null || line.length() < 33) return null;
        try {
            int spacePid = line.indexOf(' ', 18);
            int spaceTid = line.indexOf(' ', spacePid + 1);
            int spaceLvl = line.indexOf(' ', spaceTid + 1);
            String level = line.substring(spaceLvl + 1, spaceLvl + 2);
            int colon = line.indexOf(':', spaceLvl + 3);
            if (colon < 0) return null;
            String tag = line.substring(spaceLvl + 3, colon).trim();
            String msg = line.substring(colon + 2);
            return new Entry(System.currentTimeMillis(), level, tag, msg);
        } catch (StringIndexOutOfBoundsException e) {
            return null;
        }
    }

    private static String mapType(String level) {
        switch (level) {
            case "E": case "F": return "Error";
            case "W":           return "Warning";
            case "I":           return "Log";
            case "D": case "V": return "Log";
            default:            return "Log";
        }
    }

    private static String jsonEsc(String s) {
        if (s == null) return "";
        StringBuilder sb = new StringBuilder(s.length() + 8);
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            switch (c) {
                case '\\': sb.append("\\\\"); break;
                case '"':  sb.append("\\\""); break;
                case '\n': sb.append("\\n"); break;
                case '\r': sb.append("\\r"); break;
                case '\t': sb.append("\\t"); break;
                default:
                    if (c < 0x20) sb.append(String.format("\\u%04x", (int)c));
                    else sb.append(c);
            }
        }
        return sb.toString();
    }
}
