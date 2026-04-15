package au.com.oddgames.bugpunch;

import android.util.Log;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.util.ArrayDeque;
import java.util.Deque;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Tails our own process's logcat into a bounded ring buffer. Android 7+
 * restricts logcat to the calling app's own lines which is exactly what we
 * want — Unity's Debug.Log output comes through with tag "Unity".
 */
public class BugpunchLogReader {
    private static final String TAG = "BugpunchLogReader";

    private static Thread sThread;
    private static final AtomicBoolean sRunning = new AtomicBoolean();
    private static Deque<Entry> sBuffer = new ArrayDeque<>();
    private static int sMaxEntries = 500;
    private static final Object sLock = new Object();

    private static class Entry {
        final long time;
        final String level;
        final String tag;
        final String message;
        Entry(long t, String l, String tg, String m) { time = t; level = l; tag = tg; message = m; }
    }

    public static synchronized void start(int maxEntries) {
        if (sRunning.get()) return;
        sMaxEntries = Math.max(50, maxEntries);
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
        StringBuilder sb = new StringBuilder(4096);
        sb.append('[');
        synchronized (sLock) {
            boolean first = true;
            for (Entry e : sBuffer) {
                if (!first) sb.append(',');
                first = false;
                sb.append('{');
                sb.append("\"time\":").append(e.time).append(',');
                sb.append("\"type\":\"").append(jsonEsc(mapType(e.level))).append("\",");
                sb.append("\"message\":\"").append(jsonEsc(e.tag + ": " + e.message)).append("\",");
                sb.append("\"stackTrace\":\"\"");
                sb.append('}');
            }
        }
        sb.append(']');
        return sb.toString();
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
                    sBuffer.addLast(e);
                    while (sBuffer.size() > sMaxEntries) sBuffer.removeFirst();
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
