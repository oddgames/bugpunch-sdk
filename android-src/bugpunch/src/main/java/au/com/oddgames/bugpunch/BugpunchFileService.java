package au.com.oddgames.bugpunch;

import android.util.Base64;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.text.DateFormat;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.Enumeration;
import java.util.List;
import java.util.Locale;
import java.util.TimeZone;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;
import java.util.zip.ZipOutputStream;

/**
 * Native handler for /files/* tunnel requests. Mirrors the surface the C#
 * <c>FileService</c> used to expose, minus the PlayerPrefs paths
 * (<c>/files/prefs/*</c>) which still bounce to C# because PlayerPrefs is a
 * Unity-only API.
 *
 * <p>Roots are seeded from the startup config under <c>paths.{persistent,
 * cache, data, streamingAssets, consoleLog}</c> — values C# resolved from
 * <c>UnityEngine.Application</c> at boot. Every call gates on
 * {@link #isAllowed(String)} so the dashboard can't read or write outside the
 * Unity-known directories.
 *
 * <p>Each public handler builds a complete <c>{type:"response", …}</c>
 * envelope and ships it through {@link BugpunchTunnel#sendResponse(String)}.
 * Callers don't need to touch the wire.
 */
public final class BugpunchFileService {
    private static final String TAG = "[Bugpunch.FileService]";

    private static final List<Root> sRoots = new ArrayList<>();
    private static final DateFormat ISO_8601;
    static {
        ISO_8601 = new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.US);
        ISO_8601.setTimeZone(TimeZone.getTimeZone("UTC"));
    }

    private BugpunchFileService() {}

    /**
     * Re-seed the allowed root list from the startup config's <c>paths</c>
     * object. Safe to call again whenever the runtime gets a richer config
     * (the C# Start() call augments the bundled defaults with Unity values).
     */
    public static synchronized void configure(JSONObject config) {
        if (config == null) return;
        JSONObject paths = config.optJSONObject("paths");
        if (paths == null) return;
        sRoots.clear();
        addRoot("Persistent",      paths.optString("persistent", ""));
        addRoot("Cache",           paths.optString("cache", ""));
        addRoot("Data",            paths.optString("data", ""));
        addRoot("StreamingAssets", paths.optString("streamingAssets", ""));
        addRoot("ConsoleLog",      paths.optString("consoleLog", ""));
    }

    private static void addRoot(String name, String path) {
        if (path == null || path.isEmpty()) return;
        sRoots.add(new Root(name, normalize(path)));
    }

    /** True if path matches a /files/* request the native side handles. */
    public static boolean handles(String path) {
        if (path == null) return false;
        // PlayerPrefs paths still need Unity reflection — leave for C#.
        if (path.startsWith("/files/prefs/")) return false;
        return path.startsWith("/files/");
    }

    /**
     * Dispatch a /files/* request. Builds + ships the response envelope on
     * BugpunchTunnel. Returns true if handled (regardless of success/failure).
     */
    public static boolean dispatch(String requestId, String method, String path, String body) {
        if (!handles(path)) return false;
        try {
            String basePath = path.split("\\?")[0];
            String result;
            switch (basePath) {
                case "/files/paths":  result = getPaths(); break;
                case "/files/list":   result = listDirectory(query(path, "path")); break;
                case "/files/read":   result = readFile(query(path, "path"), parseInt(query(path, "maxBytes"), 1048576)); break;
                case "/files/write":  result = writeFile(query(path, "path"), body, "true".equals(query(path, "base64"))); break;
                case "/files/delete": result = deletePath(query(path, "path"), "true".equals(query(path, "recursive"))); break;
                case "/files/mkdir":  result = createDirectory(query(path, "path")); break;
                case "/files/info":   result = getFileInfo(query(path, "path")); break;
                case "/files/zip":    result = zipDirectory(query(path, "path")); break;
                case "/files/unzip":  result = unzipToDirectory(query(path, "path"), body, !"false".equals(query(path, "clear"))); break;
                default:
                    BugpunchTunnel.sendResponse(buildResponse(requestId, 404,
                        "{\"error\":\"" + esc("Unknown files endpoint: " + basePath) + "\"}"));
                    return true;
            }
            BugpunchTunnel.sendResponse(buildResponse(requestId, 200, result));
        } catch (Throwable t) {
            Log.w(TAG, "dispatch failed for " + path, t);
            BugpunchTunnel.sendResponse(buildResponse(requestId, 500,
                "{\"ok\":false,\"error\":\"" + esc(t.getMessage() != null ? t.getMessage() : t.getClass().getSimpleName()) + "\"}"));
        }
        return true;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Endpoints
    // ──────────────────────────────────────────────────────────────────────

    private static synchronized String getPaths() throws JSONException {
        JSONArray arr = new JSONArray();
        for (Root r : sRoots) {
            JSONObject o = new JSONObject();
            o.put("name", r.name);
            o.put("path", r.path);
            o.put("exists", new File(r.path).isDirectory());
            arr.put(o);
        }
        return arr.toString();
    }

    private static String listDirectory(String path) throws JSONException {
        String np = normalize(path);
        if (np.isEmpty()) return errorJson("Path is required");
        if (!isAllowed(np)) return errorJson("Access denied: path is outside allowed roots");
        File dir = new File(np);
        if (!dir.isDirectory()) return errorJson("Directory not found: " + np);

        File[] kids = dir.listFiles();
        if (kids == null) return "[]";

        JSONArray arr = new JSONArray();
        // Directories first, then files — matches the C# ordering.
        for (File f : kids) {
            if (!f.isDirectory()) continue;
            JSONObject o = new JSONObject();
            o.put("name", f.getName());
            o.put("isDirectory", true);
            o.put("size", 0);
            o.put("modified", iso(f.lastModified()));
            arr.put(o);
        }
        for (File f : kids) {
            if (!f.isFile()) continue;
            JSONObject o = new JSONObject();
            o.put("name", f.getName());
            o.put("isDirectory", false);
            o.put("size", f.length());
            o.put("modified", iso(f.lastModified()));
            arr.put(o);
        }
        return arr.toString();
    }

    private static String readFile(String path, int maxBytes) throws IOException, JSONException {
        String np = normalize(path);
        if (np.isEmpty()) return errorJson("Path is required");
        if (!isAllowed(np)) return errorJson("Access denied: path is outside allowed roots");
        File f = new File(np);
        if (!f.isFile()) return errorJson("File not found: " + np);

        long size = f.length();
        boolean truncated = size > maxBytes;
        int toRead = (int) Math.min(size, maxBytes);

        byte[] bytes = new byte[toRead];
        try (FileInputStream in = new FileInputStream(f)) {
            int total = 0;
            while (total < toRead) {
                int n = in.read(bytes, total, toRead - total);
                if (n < 0) break;
                total += n;
            }
        }

        boolean binary = isBinary(bytes, Math.min(toRead, 8192));
        JSONObject o = new JSONObject();
        o.put("ok", true);
        o.put("size", size);
        if (truncated) {
            o.put("truncated", true);
            o.put("readBytes", toRead);
        }
        if (binary) {
            o.put("content", Base64.encodeToString(bytes, Base64.NO_WRAP));
            o.put("encoding", "base64");
        } else {
            o.put("content", new String(bytes, java.nio.charset.StandardCharsets.UTF_8));
            o.put("encoding", "utf-8");
        }
        return o.toString();
    }

    private static String writeFile(String path, String content, boolean base64) throws IOException, JSONException {
        String np = normalize(path);
        if (np.isEmpty()) return errorJson("Path is required");
        if (!isAllowed(np)) return errorJson("Access denied: path is outside allowed roots");

        byte[] bytes = base64
            ? Base64.decode(content == null ? "" : content, Base64.DEFAULT)
            : (content == null ? new byte[0] : content.getBytes(java.nio.charset.StandardCharsets.UTF_8));

        File f = new File(np);
        File parent = f.getParentFile();
        if (parent != null && !parent.isDirectory() && !parent.mkdirs()) {
            return errorJson("Could not create parent directory");
        }
        try (FileOutputStream out = new FileOutputStream(f)) {
            out.write(bytes);
        }
        JSONObject o = new JSONObject();
        o.put("ok", true);
        o.put("size", bytes.length);
        return o.toString();
    }

    private static String deletePath(String path, boolean recursive) throws JSONException {
        String np = normalize(path);
        if (np.isEmpty()) return errorJson("Path is required");
        if (!isAllowed(np)) return errorJson("Access denied: path is outside allowed roots");
        for (Root r : sRoots) {
            if (np.equalsIgnoreCase(r.path)) return errorJson("Cannot delete a root directory");
        }
        File f = new File(np);
        if (f.isDirectory()) {
            if (!recursive) return errorJson("Path is a directory — set recursive=true to delete");
            if (!deleteRecursive(f)) return errorJson("Failed to delete directory");
        } else if (f.isFile()) {
            if (!f.delete()) return errorJson("Failed to delete file");
        } else {
            return errorJson("Path not found");
        }
        return "{\"ok\":true}";
    }

    private static String createDirectory(String path) throws JSONException {
        String np = normalize(path);
        if (np.isEmpty()) return errorJson("Path is required");
        if (!isAllowed(np)) return errorJson("Access denied: path is outside allowed roots");
        File f = new File(np);
        if (!f.exists() && !f.mkdirs()) return errorJson("Failed to create directory");
        return "{\"ok\":true}";
    }

    private static String getFileInfo(String path) throws JSONException {
        String np = normalize(path);
        if (np.isEmpty()) return errorJson("Path is required");
        if (!isAllowed(np)) return errorJson("Access denied: path is outside allowed roots");
        File f = new File(np);
        if (!f.exists()) return errorJson("Path not found");

        JSONObject o = new JSONObject();
        o.put("ok", true);
        o.put("name", f.getName());
        o.put("path", f.getAbsolutePath().replace('\\', '/'));
        o.put("isDirectory", f.isDirectory());
        o.put("size", f.isFile() ? f.length() : 0);
        if (f.isFile()) {
            String name = f.getName();
            int dot = name.lastIndexOf('.');
            o.put("extension", dot >= 0 ? name.substring(dot) : "");
        }
        // Java doesn't expose creation time on most filesystems — repeat
        // modified for both fields to match the shape the dashboard expects.
        String iso = iso(f.lastModified());
        o.put("created", iso);
        o.put("modified", iso);
        return o.toString();
    }

    private static String zipDirectory(String path) throws IOException, JSONException {
        String np = normalize(path);
        if (np.isEmpty()) return errorJson("Path is required");
        if (!isAllowed(np)) return errorJson("Access denied: path is outside allowed roots");
        File dir = new File(np);
        if (!dir.isDirectory()) return errorJson("Directory not found: " + np);

        ByteArrayOutputStream baos = new ByteArrayOutputStream();
        try (ZipOutputStream zos = new ZipOutputStream(baos)) {
            zipInto(zos, dir, "");
        }
        byte[] bytes = baos.toByteArray();
        JSONObject o = new JSONObject();
        o.put("ok", true);
        o.put("size", bytes.length);
        o.put("base64", Base64.encodeToString(bytes, Base64.NO_WRAP));
        return o.toString();
    }

    private static String unzipToDirectory(String path, String base64Zip, boolean clearFirst) throws IOException, JSONException {
        String np = normalize(path);
        if (np.isEmpty()) return errorJson("Path is required");
        if (!isAllowed(np)) return errorJson("Access denied: path is outside allowed roots");

        File targetDir = new File(np);
        if (clearFirst && targetDir.isDirectory()) {
            File[] kids = targetDir.listFiles();
            if (kids != null) for (File k : kids) deleteRecursive(k);
        }
        if (!targetDir.isDirectory() && !targetDir.mkdirs()) {
            return errorJson("Failed to create target directory");
        }

        // Stage to disk first because java.util.zip.ZipFile needs a file path.
        File tmp = File.createTempFile("bp_unzip_", ".zip");
        try {
            byte[] bytes = Base64.decode(base64Zip == null ? "" : base64Zip, Base64.DEFAULT);
            try (FileOutputStream out = new FileOutputStream(tmp)) { out.write(bytes); }
            try (ZipFile zf = new ZipFile(tmp)) {
                Enumeration<? extends ZipEntry> entries = zf.entries();
                String canonicalTarget = targetDir.getCanonicalPath();
                while (entries.hasMoreElements()) {
                    ZipEntry e = entries.nextElement();
                    File out = new File(targetDir, e.getName());
                    // Zip-slip guard.
                    if (!out.getCanonicalPath().startsWith(canonicalTarget + File.separator)
                            && !out.getCanonicalPath().equals(canonicalTarget)) {
                        return errorJson("Zip entry escapes target: " + e.getName());
                    }
                    if (e.isDirectory()) {
                        if (!out.isDirectory() && !out.mkdirs()) return errorJson("Failed to create " + e.getName());
                    } else {
                        File parent = out.getParentFile();
                        if (parent != null && !parent.isDirectory() && !parent.mkdirs()) {
                            return errorJson("Failed to create parent for " + e.getName());
                        }
                        try (FileOutputStream fos = new FileOutputStream(out);
                             java.io.InputStream in = zf.getInputStream(e)) {
                            byte[] buf = new byte[8192];
                            int n;
                            while ((n = in.read(buf)) > 0) fos.write(buf, 0, n);
                        }
                    }
                }
            }
        } finally {
            tmp.delete();
        }
        return "{\"ok\":true}";
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static synchronized boolean isAllowed(String path) {
        for (Root r : sRoots) {
            if (path.toLowerCase(Locale.ROOT).startsWith(r.path.toLowerCase(Locale.ROOT))) return true;
        }
        return false;
    }

    private static String normalize(String path) {
        if (path == null) return "";
        try {
            return new File(path).getCanonicalPath().replace('\\', '/');
        } catch (IOException e) {
            return path.replace('\\', '/');
        }
    }

    private static boolean isBinary(byte[] data, int checkLength) {
        int len = Math.min(data.length, checkLength);
        for (int i = 0; i < len; i++) if (data[i] == 0) return true;
        return false;
    }

    private static boolean deleteRecursive(File f) {
        if (f.isDirectory()) {
            File[] kids = f.listFiles();
            if (kids != null) for (File k : kids) if (!deleteRecursive(k)) return false;
        }
        return f.delete();
    }

    private static void zipInto(ZipOutputStream zos, File dir, String prefix) throws IOException {
        File[] kids = dir.listFiles();
        if (kids == null) return;
        byte[] buf = new byte[8192];
        for (File f : kids) {
            String entryName = prefix + f.getName();
            if (f.isDirectory()) {
                zos.putNextEntry(new ZipEntry(entryName + "/"));
                zos.closeEntry();
                zipInto(zos, f, entryName + "/");
            } else {
                zos.putNextEntry(new ZipEntry(entryName));
                try (FileInputStream in = new FileInputStream(f)) {
                    int n;
                    while ((n = in.read(buf)) > 0) zos.write(buf, 0, n);
                }
                zos.closeEntry();
            }
        }
    }

    private static String query(String path, String key) {
        int q = path.indexOf('?');
        if (q < 0) return "";
        String qs = path.substring(q + 1);
        for (String pair : qs.split("&")) {
            int eq = pair.indexOf('=');
            if (eq < 0) continue;
            if (pair.substring(0, eq).equals(key)) {
                try { return java.net.URLDecoder.decode(pair.substring(eq + 1), "UTF-8"); }
                catch (Exception e) { return pair.substring(eq + 1); }
            }
        }
        return "";
    }

    private static int parseInt(String s, int fallback) {
        if (s == null || s.isEmpty()) return fallback;
        try { return Integer.parseInt(s); } catch (NumberFormatException e) { return fallback; }
    }

    private static synchronized String iso(long epochMs) {
        return ISO_8601.format(new Date(epochMs));
    }

    private static String esc(String s) {
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
                    if (c < 0x20) sb.append(String.format("\\u%04X", (int) c));
                    else sb.append(c);
            }
        }
        return sb.toString();
    }

    private static String errorJson(String message) {
        return "{\"ok\":false,\"error\":\"" + esc(message) + "\"}";
    }

    private static String buildResponse(String requestId, int status, String body) {
        try {
            JSONObject o = new JSONObject();
            o.put("type", "response");
            o.put("requestId", requestId);
            o.put("status", status);
            o.put("body", body);
            o.put("contentType", "application/json");
            return o.toString();
        } catch (JSONException e) {
            return "{\"type\":\"response\",\"requestId\":\"" + esc(requestId) + "\",\"status\":500}";
        }
    }

    private static final class Root {
        final String name;
        final String path;
        Root(String n, String p) { this.name = n; this.path = p; }
    }
}
