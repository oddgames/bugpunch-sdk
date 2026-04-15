package au.com.oddgames.bugpunch;

import android.content.Context;
import android.os.Handler;
import android.os.HandlerThread;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.DataOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.Iterator;
import java.util.UUID;

/**
 * Native uploader. Owns the upload queue; takes manifests from Unity/native
 * code, does multipart POSTs with retry, cleans up on success.
 *
 * Queue lives at {cacheDir}/bugpunch_uploads/*.upload.json. Each manifest
 * describes one request end-to-end (URL, headers, multipart body, files to
 * clean up on success).
 *
 * Manifest JSON schema:
 *   {
 *     "url":      "https://.../api/reports/crash",
 *     "headers":  { "X-Api-Key": "..." },
 *     "fields":   { "metadata": "<json string>" },
 *     "files":    [ { "field": "screenshot", "filename": "...", "contentType": "image/jpeg", "path": "/..." } ],
 *     "cleanupPaths": [ "/tmp/shot.jpg", "/..." ],
 *     "attempts": 0
 *   }
 */
public class BugpunchUploader {
    private static final String TAG = "BugpunchUploader";
    private static final String QUEUE_DIR = "bugpunch_uploads";
    private static final int MAX_ATTEMPTS = 10;
    private static final int CONNECT_TIMEOUT_MS = 15_000;
    private static final int READ_TIMEOUT_MS = 60_000;

    private static HandlerThread sThread;
    private static Handler sHandler;
    private static File sQueueDir;

    private static synchronized void ensureStarted(Context ctx) {
        if (sThread == null) {
            sThread = new HandlerThread("BugpunchUploader");
            sThread.start();
            sHandler = new Handler(sThread.getLooper());
        }
        if (sQueueDir == null) {
            sQueueDir = new File(ctx.getCacheDir(), QUEUE_DIR);
            if (!sQueueDir.exists()) sQueueDir.mkdirs();
        }
    }

    /**
     * Enqueue a report. Writes a manifest describing the request and kicks
     * the worker. All args passed as primitives — no JSON glue in C#.
     * Empty-string paths are treated as absent.
     */
    public static void enqueue(Context ctx, final String url, final String apiKey,
                               final String metadataJson, final String screenshotPath,
                               final String videoPath, final String annotationsPath) {
        enqueue(ctx, url, apiKey, metadataJson, screenshotPath, videoPath,
            annotationsPath, null, null);
    }

    /**
     * Full overload that accepts trace attachments. {@code tracesJsonPath} is
     * a multipart field {@code traces} (application/json). {@code traceScreenshotPaths}
     * each become multipart fields {@code trace_0}, {@code trace_1}, ...
     */
    public static void enqueue(Context ctx, final String url, final String apiKey,
                               final String metadataJson, final String screenshotPath,
                               final String videoPath, final String annotationsPath,
                               final String tracesJsonPath,
                               final String[] traceScreenshotPaths) {
        ensureStarted(ctx);
        sHandler.post(new Runnable() {
            @Override public void run() {
                try {
                    JSONObject m = new JSONObject();
                    m.put("url", url);
                    JSONObject headers = new JSONObject();
                    headers.put("X-Api-Key", apiKey);
                    m.put("headers", headers);
                    JSONObject fields = new JSONObject();
                    fields.put("metadata", metadataJson != null ? metadataJson : "");
                    m.put("fields", fields);
                    JSONArray files = new JSONArray();
                    JSONArray cleanup = new JSONArray();
                    if (screenshotPath != null && !screenshotPath.isEmpty()) {
                        JSONObject f = new JSONObject();
                        f.put("field", "screenshot");
                        f.put("filename", "screenshot.jpg");
                        f.put("contentType", "image/jpeg");
                        f.put("path", screenshotPath);
                        files.put(f);
                        cleanup.put(screenshotPath);
                    }
                    if (videoPath != null && !videoPath.isEmpty()) {
                        JSONObject f = new JSONObject();
                        f.put("field", "video");
                        f.put("filename", "video.mp4");
                        f.put("contentType", "video/mp4");
                        f.put("path", videoPath);
                        files.put(f);
                        cleanup.put(videoPath);
                    }
                    if (annotationsPath != null && !annotationsPath.isEmpty()) {
                        JSONObject f = new JSONObject();
                        f.put("field", "annotations");
                        f.put("filename", "annotations.png");
                        f.put("contentType", "image/png");
                        f.put("path", annotationsPath);
                        files.put(f);
                        cleanup.put(annotationsPath);
                    }
                    if (tracesJsonPath != null && !tracesJsonPath.isEmpty()) {
                        JSONObject f = new JSONObject();
                        f.put("field", "traces");
                        f.put("filename", "traces.json");
                        f.put("contentType", "application/json");
                        f.put("path", tracesJsonPath);
                        files.put(f);
                        cleanup.put(tracesJsonPath);
                    }
                    if (traceScreenshotPaths != null) {
                        for (int i = 0; i < traceScreenshotPaths.length; i++) {
                            String p = traceScreenshotPaths[i];
                            if (p == null || p.isEmpty()) continue;
                            JSONObject f = new JSONObject();
                            f.put("field", "trace_" + i);
                            f.put("filename", "trace_" + i + ".jpg");
                            f.put("contentType", "image/jpeg");
                            f.put("path", p);
                            files.put(f);
                            cleanup.put(p);
                        }
                    }
                    m.put("files", files);
                    m.put("cleanupPaths", cleanup);
                    m.put("attempts", 0);

                    File out = new File(sQueueDir, UUID.randomUUID().toString() + ".upload.json");
                    FileOutputStream fos = new FileOutputStream(out);
                    fos.write(m.toString().getBytes(StandardCharsets.UTF_8));
                    fos.close();
                    drainInternal();
                } catch (Throwable t) {
                    Log.w(TAG, "enqueue failed", t);
                }
            }
        });
    }

    /**
     * Kick the worker to scan and process the queue. Safe to call at any time
     * (app launch, after connectivity change, etc.).
     */
    public static void drain(Context ctx) {
        ensureStarted(ctx);
        sHandler.post(new Runnable() {
            @Override public void run() { drainInternal(); }
        });
    }

    // ── Worker (runs on sThread) ──

    private static void drainInternal() {
        File[] files = sQueueDir.listFiles();
        if (files == null) return;
        for (File f : files) {
            if (!f.getName().endsWith(".upload.json")) continue;
            try {
                processOne(f);
            } catch (Throwable t) {
                Log.w(TAG, "processOne failed: " + f.getName(), t);
            }
        }
    }

    private static void processOne(File manifestFile) throws IOException, JSONException {
        String body = readText(manifestFile);
        JSONObject manifest = new JSONObject(body);
        int attempts = manifest.optInt("attempts", 0);
        boolean isJson = manifest.has("rawJsonBody");

        boolean ok;
        int status = 0;
        String err = null;
        String responseBody = null;
        try {
            Result r = isJson ? sendJson(manifest) : sendMultipart(manifest);
            status = r.status;
            responseBody = r.body;
            ok = status >= 200 && status < 300;
            if (!ok) err = "HTTP " + status;
        } catch (Throwable t) {
            ok = false;
            err = t.getClass().getSimpleName() + ": " + t.getMessage();
        }

        if (ok) {
            Log.i(TAG, "uploaded " + manifest.optString("url"));
            cleanup(manifest);
            if (!manifestFile.delete()) {
                Log.w(TAG, "could not delete manifest " + manifestFile.getName());
            }
            // Crash ingest responses carry matchedDirectives[] \u2014 apply them.
            if (responseBody != null
                && manifest.optString("url").endsWith("/api/crashes")) {
                try {
                    BugpunchDirectives.onUploadResponse(manifest, responseBody);
                } catch (Throwable t) {
                    Log.w(TAG, "directive dispatch failed", t);
                }
            }
            return;
        }

        attempts++;
        if (attempts >= MAX_ATTEMPTS || status == 400 || status == 401 || status == 403) {
            // Give up permanently on terminal errors or too many attempts.
            Log.w(TAG, "dropping after " + attempts + " attempts (" + err + "): "
                + manifest.optString("url"));
            cleanup(manifest);
            manifestFile.delete();
            return;
        }

        manifest.put("attempts", attempts);
        writeText(manifestFile, manifest.toString());
        Log.w(TAG, "retry " + attempts + "/" + MAX_ATTEMPTS + " (" + err + "): "
            + manifest.optString("url"));
        // No in-process backoff timer — next drain() (app launch / enqueue) retries.
    }

    /**
     * Enqueue a JSON POST. Used by directive enrichment \u2014 same retry +
     * app-kill survival as crash/bug multipart uploads.
     */
    public static void enqueueJson(final Context ctx, final String url,
                                   final String apiKey, final String jsonBody) {
        ensureStarted(ctx);
        sHandler.post(new Runnable() {
            @Override public void run() {
                try {
                    JSONObject m = new JSONObject();
                    m.put("url", url);
                    JSONObject headers = new JSONObject();
                    headers.put("X-Api-Key", apiKey);
                    headers.put("Content-Type", "application/json");
                    m.put("headers", headers);
                    m.put("rawJsonBody", jsonBody != null ? jsonBody : "{}");
                    m.put("attempts", 0);
                    File out = new File(sQueueDir, UUID.randomUUID().toString() + ".upload.json");
                    FileOutputStream fos = new FileOutputStream(out);
                    fos.write(m.toString().getBytes(StandardCharsets.UTF_8));
                    fos.close();
                    drainInternal();
                } catch (Throwable t) {
                    Log.w(TAG, "enqueueJson failed", t);
                }
            }
        });
    }

    private static Result sendJson(JSONObject manifest) throws IOException, JSONException {
        String urlStr = manifest.getString("url");
        JSONObject headers = manifest.optJSONObject("headers");
        String rawBody = manifest.getString("rawJsonBody");
        URL url = new URL(urlStr);
        HttpURLConnection conn = (HttpURLConnection) url.openConnection();
        try {
            conn.setRequestMethod("POST");
            conn.setDoOutput(true);
            conn.setConnectTimeout(CONNECT_TIMEOUT_MS);
            conn.setReadTimeout(READ_TIMEOUT_MS);
            if (headers != null) {
                Iterator<String> keys = headers.keys();
                while (keys.hasNext()) {
                    String k = keys.next();
                    conn.setRequestProperty(k, headers.optString(k));
                }
            }
            byte[] bytes = rawBody.getBytes(StandardCharsets.UTF_8);
            conn.setFixedLengthStreamingMode(bytes.length);
            java.io.OutputStream os = conn.getOutputStream();
            try { os.write(bytes); os.flush(); } finally { os.close(); }

            int code = conn.getResponseCode();
            InputStream is = code >= 400 ? conn.getErrorStream() : conn.getInputStream();
            java.io.ByteArrayOutputStream bos = new java.io.ByteArrayOutputStream();
            if (is != null) {
                byte[] buf = new byte[4096];
                int n;
                while ((n = is.read(buf)) > 0) {
                    if (bos.size() < 64 * 1024) bos.write(buf, 0, n);
                }
                is.close();
            }
            return new Result(code, bos.toString("UTF-8"));
        } finally {
            conn.disconnect();
        }
    }

    static class Result {
        int status;
        String body;
        Result(int s, String b) { status = s; body = b; }
    }

    private static Result sendMultipart(JSONObject manifest) throws IOException, JSONException {
        String urlStr = manifest.getString("url");
        JSONObject fields = manifest.optJSONObject("fields");
        JSONArray filesArr = manifest.optJSONArray("files");
        JSONObject headers = manifest.optJSONObject("headers");

        String boundary = "----BugpunchBoundary" + UUID.randomUUID().toString().replace("-", "");
        URL url = new URL(urlStr);
        HttpURLConnection conn = (HttpURLConnection) url.openConnection();
        try {
            conn.setRequestMethod("POST");
            conn.setDoOutput(true);
            conn.setConnectTimeout(CONNECT_TIMEOUT_MS);
            conn.setReadTimeout(READ_TIMEOUT_MS);
            conn.setRequestProperty("Content-Type", "multipart/form-data; boundary=" + boundary);
            if (headers != null) {
                Iterator<String> keys = headers.keys();
                while (keys.hasNext()) {
                    String k = keys.next();
                    conn.setRequestProperty(k, headers.optString(k));
                }
            }
            conn.setChunkedStreamingMode(0);

            DataOutputStream out = new DataOutputStream(conn.getOutputStream());
            try {
                if (fields != null) {
                    Iterator<String> keys = fields.keys();
                    while (keys.hasNext()) {
                        String k = keys.next();
                        String v = fields.optString(k);
                        writeField(out, boundary, k, v);
                    }
                }
                if (filesArr != null) {
                    for (int i = 0; i < filesArr.length(); i++) {
                        JSONObject f = filesArr.getJSONObject(i);
                        writeFilePart(out, boundary,
                            f.getString("field"),
                            f.optString("filename", "file"),
                            f.optString("contentType", "application/octet-stream"),
                            f.getString("path"));
                    }
                }
                out.writeBytes("--" + boundary + "--\r\n");
                out.flush();
            } finally {
                out.close();
            }

            int code = conn.getResponseCode();
            // Read response so the caller can inspect matchedDirectives, etc.
            InputStream is = code >= 400 ? conn.getErrorStream() : conn.getInputStream();
            java.io.ByteArrayOutputStream bos = new java.io.ByteArrayOutputStream();
            if (is != null) {
                byte[] buf = new byte[4096];
                int n;
                while ((n = is.read(buf)) > 0) {
                    if (bos.size() < 128 * 1024) bos.write(buf, 0, n);
                }
                is.close();
            }
            return new Result(code, bos.toString("UTF-8"));
        } finally {
            conn.disconnect();
        }
    }

    private static void writeField(DataOutputStream out, String boundary, String name, String value)
            throws IOException {
        out.writeBytes("--" + boundary + "\r\n");
        out.writeBytes("Content-Disposition: form-data; name=\"" + name + "\"\r\n");
        out.writeBytes("Content-Type: text/plain; charset=utf-8\r\n\r\n");
        out.write(value.getBytes(StandardCharsets.UTF_8));
        out.writeBytes("\r\n");
    }

    private static void writeFilePart(DataOutputStream out, String boundary, String field,
                                      String filename, String contentType, String path)
            throws IOException {
        File f = new File(path);
        if (!f.exists()) {
            Log.w(TAG, "file missing, skipping: " + path);
            return;
        }
        out.writeBytes("--" + boundary + "\r\n");
        out.writeBytes("Content-Disposition: form-data; name=\"" + field
            + "\"; filename=\"" + filename + "\"\r\n");
        out.writeBytes("Content-Type: " + contentType + "\r\n\r\n");
        FileInputStream in = new FileInputStream(f);
        try {
            byte[] buf = new byte[8192];
            int n;
            while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
        } finally {
            in.close();
        }
        out.writeBytes("\r\n");
    }

    private static void cleanup(JSONObject manifest) {
        JSONArray paths = manifest.optJSONArray("cleanupPaths");
        if (paths == null) return;
        for (int i = 0; i < paths.length(); i++) {
            String p = paths.optString(i);
            if (p == null || p.isEmpty()) continue;
            try { new File(p).delete(); } catch (Throwable ignored) {}
        }
    }

    private static String readText(File f) throws IOException {
        FileInputStream in = new FileInputStream(f);
        try {
            byte[] buf = new byte[(int) f.length()];
            int total = 0;
            while (total < buf.length) {
                int n = in.read(buf, total, buf.length - total);
                if (n < 0) break;
                total += n;
            }
            return new String(buf, 0, total, StandardCharsets.UTF_8);
        } finally {
            in.close();
        }
    }

    private static void writeText(File f, String s) throws IOException {
        FileOutputStream out = new FileOutputStream(f);
        try {
            out.write(s.getBytes(StandardCharsets.UTF_8));
        } finally {
            out.close();
        }
    }
}
