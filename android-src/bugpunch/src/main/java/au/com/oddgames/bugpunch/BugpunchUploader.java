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
import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;
import java.util.UUID;

/**
 * Native uploader. Owns the upload queue; takes manifests from Unity/native
 * code, does multipart POSTs with retry, cleans up on success.
 *
 * Queue lives at {cacheDir}/bugpunch_uploads/*.upload.json. Each manifest
 * describes one request end-to-end (URL, headers, multipart body, files to
 * clean up on success).
 *
 * Canonical manifest schema, two-stage preflight flow, and pinned constants:
 * see sdk/docs/upload-manifest.md. The iOS BugpunchUploader.mm follows the
 * same contract — change them together.
 */
public class BugpunchUploader {
    private static final String TAG = "[Bugpunch.Uploader]";
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
     * A named file to include in a multipart upload.
     * Callers build a list of these and pass to {@link #enqueue}.
     *
     * {@code requires} is the two-phase preflight gate: after the server
     * response lists which heavy fields it wants (e.g. {@code ["logs",
     * "screenshot"]}), only attachments whose {@code requires} appears in
     * that list advance to phase 2. null/empty means unconditional (existing
     * single-phase flow).
     */
    public static class FileAttachment {
        public final String field;       // multipart field name
        public final String filename;    // filename in the multipart part
        public final String contentType; // MIME type
        public final String path;        // absolute path on disk
        public final String requires;    // preflight-gate key, or null for unconditional

        public FileAttachment(String field, String filename, String contentType, String path) {
            this(field, filename, contentType, path, null);
        }

        public FileAttachment(String field, String filename, String contentType, String path,
                              String requires) {
            this.field = field;
            this.filename = filename;
            this.contentType = contentType;
            this.path = path;
            this.requires = requires;
        }

        /** Convenience: JPEG image attachment. */
        public static FileAttachment jpeg(String field, String path) {
            return new FileAttachment(field, field + ".jpg", "image/jpeg", path);
        }

        /** Preflight-gated JPEG. */
        public static FileAttachment jpegGated(String field, String path, String requires) {
            return new FileAttachment(field, field + ".jpg", "image/jpeg", path, requires);
        }

        /** Convenience: numbered JPEG (trace_0, anr_screenshot_1, etc.). */
        public static FileAttachment jpeg(String fieldPrefix, int index, String path) {
            String name = fieldPrefix + "_" + index;
            return new FileAttachment(name, name + ".jpg", "image/jpeg", path);
        }

        /** Preflight-gated numbered JPEG — shares one collect key across the group. */
        public static FileAttachment jpegGated(String fieldPrefix, int index, String path,
                                               String requires) {
            String name = fieldPrefix + "_" + index;
            return new FileAttachment(name, name + ".jpg", "image/jpeg", path, requires);
        }
    }

    /**
     * Enqueue a multipart upload. The metadata JSON becomes the {@code metadata}
     * text field; each {@link FileAttachment} becomes a binary part. All temp
     * files in the list are cleaned up after successful upload.
     */
    public static void enqueue(Context ctx, final String url, final String apiKey,
                               final String metadataJson,
                               final List<FileAttachment> attachments) {
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
                    if (attachments != null) {
                        for (FileAttachment a : attachments) {
                            if (a.path == null || a.path.isEmpty()) continue;
                            JSONObject f = new JSONObject();
                            f.put("field", a.field);
                            f.put("filename", a.filename);
                            f.put("contentType", a.contentType);
                            f.put("path", a.path);
                            if (a.requires != null) f.put("requires", a.requires);
                            files.put(f);
                            cleanup.put(a.path);
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
                    BugpunchSdkErrorOverlay.reportThrowable("BugpunchUploader", "enqueue", t);
                }
            }
        });
    }

    /**
     * Lazily-produced attachment. Kept out of the multipart upload until
     * the server's phase-1 {@code collect} list confirms it wants this kind
     * of artifact; only then does {@link #produce} run (typically an
     * expensive encode/remux step).
     *
     * <p>Concrete implementations are wired into {@link #produceDeferred}
     * by {@code kind} string. New kinds need a switch arm there + a
     * matching {@code DeferredAttachment} subclass.
     */
    public static class DeferredAttachment {
        public final String field;
        public final String filename;
        public final String contentType;
        public final String requires;
        public final String kind;
        public final String sourcePath;
        public DeferredAttachment(String field, String filename, String contentType,
                                  String requires, String kind, String sourcePath) {
            this.field = field;
            this.filename = filename;
            this.contentType = contentType;
            this.requires = requires;
            this.kind = kind;
            this.sourcePath = sourcePath;
        }
    }

    /**
     * Enqueue a two-phase upload. Phase 1 POSTs {@code jsonBody} to
     * {@code preflightUrl}; server response carries {@code eventId} +
     * {@code collect[]}. On success we rewrite the manifest to phase 2:
     * multipart POST to {@code enrichUrlTemplate} (with {id} substituted)
     * carrying only files whose {@link FileAttachment#requires} appears in
     * {@code collect}, plus any {@link DeferredAttachment} that's
     * accepted (produced on demand). An empty {@code collect} skips phase
     * 2 entirely and cleans up all attachments immediately.
     *
     * Either phase can fail + retry independently; the worker pages through
     * attempts without losing the other phase's state.
     */
    public static void enqueuePreflight(Context ctx, final String preflightUrl,
                                        final String enrichUrlTemplate, final String apiKey,
                                        final String jsonBody,
                                        final List<FileAttachment> attachments) {
        enqueuePreflight(ctx, preflightUrl, enrichUrlTemplate, apiKey, jsonBody,
            attachments, null);
    }

    public static void enqueuePreflight(Context ctx, final String preflightUrl,
                                        final String enrichUrlTemplate, final String apiKey,
                                        final String jsonBody,
                                        final List<FileAttachment> attachments,
                                        final List<DeferredAttachment> deferred) {
        ensureStarted(ctx);
        sHandler.post(new Runnable() {
            @Override public void run() {
                try {
                    JSONObject m = new JSONObject();
                    m.put("stage", "preflight");
                    m.put("url", preflightUrl);
                    m.put("enrichUrlTemplate", enrichUrlTemplate);
                    JSONObject headers = new JSONObject();
                    headers.put("X-Api-Key", apiKey);
                    headers.put("Content-Type", "application/json");
                    m.put("headers", headers);
                    m.put("rawJsonBody", jsonBody != null ? jsonBody : "{}");
                    JSONArray files = new JSONArray();
                    JSONArray cleanup = new JSONArray();
                    if (attachments != null) {
                        for (FileAttachment a : attachments) {
                            if (a.path == null || a.path.isEmpty()) continue;
                            JSONObject f = new JSONObject();
                            f.put("field", a.field);
                            f.put("filename", a.filename);
                            f.put("contentType", a.contentType);
                            f.put("path", a.path);
                            if (a.requires != null) f.put("requires", a.requires);
                            files.put(f);
                            cleanup.put(a.path);
                        }
                    }
                    m.put("files", files);
                    // Serialise deferred attachments alongside the regular
                    // file list. They're resolved in transitionToEnrich
                    // (phase-2 transition) — only the ones the server's
                    // `collect` list accepts will actually produce + upload.
                    JSONArray deferredArr = new JSONArray();
                    if (deferred != null) {
                        for (DeferredAttachment d : deferred) {
                            if (d.sourcePath == null || d.sourcePath.isEmpty()) continue;
                            JSONObject o = new JSONObject();
                            o.put("field", d.field);
                            o.put("filename", d.filename);
                            o.put("contentType", d.contentType);
                            o.put("requires", d.requires);
                            o.put("kind", d.kind);
                            o.put("sourcePath", d.sourcePath);
                            deferredArr.put(o);
                            cleanup.put(d.sourcePath);
                        }
                    }
                    java.util.HashSet<String> available =
                        collectAvailableRequires(attachments, deferred);
                    if (deferredArr.length() > 0) m.put("deferredFiles", deferredArr);
                    // Inform the server which expensive-to-produce attachments
                    // are available so its budget logic can decide whether
                    // to ask for them in the phase-1 collect[] response.
                    if (!available.isEmpty()) {
                        JSONArray availArr = new JSONArray();
                        for (String s : available) availArr.put(s);
                        // Surface in the phase-1 body, not just the manifest,
                        // so the server actually sees it. rawJsonBody is the
                        // string sent verbatim — patch it if it's valid JSON.
                        try {
                            JSONObject body = new JSONObject(jsonBody != null ? jsonBody : "{}");
                            body.put("attachmentsAvailable", availArr);
                            m.put("rawJsonBody", body.toString());
                        } catch (JSONException ignored) {
                            // Body wasn't JSON — leave rawJsonBody untouched.
                        }
                    }
                    m.put("cleanupPaths", cleanup);
                    m.put("attempts", 0);

                    File out = new File(sQueueDir, UUID.randomUUID().toString() + ".upload.json");
                    FileOutputStream fos = new FileOutputStream(out);
                    fos.write(m.toString().getBytes(StandardCharsets.UTF_8));
                    fos.close();
                    drainInternal();
                } catch (Throwable t) {
                    Log.w(TAG, "enqueuePreflight failed", t);
                    BugpunchSdkErrorOverlay.reportThrowable("BugpunchUploader", "enqueuePreflight", t);
                }
            }
        });
    }

    /**
     * Legacy overload for C# bridge and crash drain callers.
     * Wraps named paths into a {@link FileAttachment} list.
     */
    public static void enqueue(Context ctx, String url, String apiKey,
                               String metadataJson, String screenshotPath,
                               String videoPath, String annotationsPath) {
        List<FileAttachment> files = new ArrayList<>();
        if (screenshotPath != null && !screenshotPath.isEmpty())
            files.add(FileAttachment.jpeg("screenshot", screenshotPath));
        if (videoPath != null && !videoPath.isEmpty())
            files.add(new FileAttachment("video", "video.mp4", "video/mp4", videoPath));
        if (annotationsPath != null && !annotationsPath.isEmpty())
            files.add(new FileAttachment("annotations", "annotations.png", "image/png", annotationsPath));
        enqueue(ctx, url, apiKey, metadataJson, files);
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

    // ── Status observer (used by the in-app upload-progress banner) ──
    //
    // Single observer slot — the banner is the only consumer. Fires after
    // every meaningful queue change: enqueue, terminal success, terminal
    // failure. Posts via a Handler the observer supplies (typically the
    // main looper) so the consumer doesn't have to thread-jump itself.
    public interface StatusObserver {
        /**
         * @param pending  Number of {@code .upload.json} manifests still in
         *                 the queue dir (i.e. work not yet finished).
         * @param phaseHint One of "preflight", "enrich", "idle" — best-effort
         *                  stage of the *most recently touched* manifest, for
         *                  status-text rendering. Always "idle" when pending
         *                  is 0.
         */
        void onUploadStatus(int pending, String phaseHint);
    }
    private static volatile StatusObserver sStatusObserver;
    public static void setStatusObserver(StatusObserver o) { sStatusObserver = o; }

    private static int countPendingManifests() {
        File[] files = sQueueDir != null ? sQueueDir.listFiles() : null;
        if (files == null) return 0;
        int n = 0;
        for (File f : files) if (f.getName().endsWith(".upload.json")) n++;
        return n;
    }

    private static void publishStatus(String phaseHint) {
        StatusObserver o = sStatusObserver;
        if (o == null) return;
        int pending = countPendingManifests();
        try { o.onUploadStatus(pending, pending == 0 ? "idle" : phaseHint); }
        catch (Throwable t) { Log.w(TAG, "status observer threw", t); }
    }

    // ── Worker (runs on sThread) ──

    private static void drainInternal() {
        File[] files = sQueueDir.listFiles();
        if (files == null) return;
        // Status: announce work pending before we start the loop so the
        // banner can pop up immediately; announce again after each one
        // finishes so the count decrements visibly.
        publishStatus("preflight");
        for (File f : files) {
            if (!f.getName().endsWith(".upload.json")) continue;
            try {
                processOne(f);
            } catch (Throwable t) {
                Log.w(TAG, "processOne failed: " + f.getName(), t);
                BugpunchSdkErrorOverlay.reportThrowable("BugpunchUploader", "processOne(" + f.getName() + ")", t);
            }
            publishStatus("preflight");
        }
        publishStatus("idle");
    }

    private static void processOne(File manifestFile) throws IOException, JSONException {
        String body = readText(manifestFile);
        JSONObject manifest = new JSONObject(body);
        int attempts = manifest.optInt("attempts", 0);
        String stage = manifest.optString("stage", "");
        boolean isPreflight = "preflight".equals(stage);
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

            if (isPreflight) {
                // Phase 1 succeeded. Decide whether phase 2 runs at all, and
                // if so, rewrite this manifest so the next drain picks up the
                // enrich request. Phase 1 is the ONLY place a crash ingest
                // response carries matchedDirectives[] / eventId — dispatch
                // directives now, while the response body is in hand.
                if (responseBody != null) {
                    try { BugpunchDirectives.onUploadResponse(manifest, responseBody); }
                    catch (Throwable t) { Log.w(TAG, "directive dispatch failed", t); }
                }
                boolean advanced = transitionToEnrich(manifest, manifestFile, responseBody);
                if (!advanced) {
                    // Either collect=[] (budget spent) or nothing to send —
                    // clean up attachments, drop manifest.
                    cleanup(manifest);
                    if (!manifestFile.delete())
                        Log.w(TAG, "could not delete manifest " + manifestFile.getName());
                }
                return;
            }

            cleanup(manifest);
            if (!manifestFile.delete()) {
                Log.w(TAG, "could not delete manifest " + manifestFile.getName());
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
     * After a successful phase-1 preflight, parse {@code responseBody} for
     * {@code eventId} + {@code collect[]} and rewrite {@code manifest} in
     * place to the phase-2 enrich shape:
     *   - url set from {@code enrichUrlTemplate} with {id} substituted
     *   - headers stripped of Content-Type (multipart sets its own)
     *   - rawJsonBody removed
     *   - files filtered to those whose {@code requires} is in collect
     *   - attempts reset so phase 2 gets its own retry budget
     *
     * Returns true if the manifest was written back and should run as phase
     * 2; false when nothing needs to be sent (empty collect, or no matching
     * files even after filtering).
     */
    private static boolean transitionToEnrich(JSONObject manifest, File manifestFile,
                                              String responseBody)
            throws IOException, JSONException {
        if (responseBody == null || responseBody.isEmpty()) return false;
        JSONObject res;
        try { res = new JSONObject(responseBody); }
        catch (JSONException e) {
            Log.w(TAG, "preflight response not JSON — skipping phase 2");
            return false;
        }
        String eventId = res.optString("eventId", "");
        JSONArray collectArr = res.optJSONArray("collect");
        if (eventId.isEmpty() || collectArr == null || collectArr.length() == 0) {
            return false;
        }
        String enrichTemplate = manifest.optString("enrichUrlTemplate", "");
        if (enrichTemplate.isEmpty()) return false;

        java.util.HashSet<String> collect = new java.util.HashSet<>();
        for (int i = 0; i < collectArr.length(); i++) collect.add(collectArr.optString(i));

        JSONArray inFiles = manifest.optJSONArray("files");
        JSONArray outFiles = new JSONArray();
        if (inFiles != null) {
            for (int i = 0; i < inFiles.length(); i++) {
                JSONObject f = inFiles.getJSONObject(i);
                String req = f.optString("requires", "");
                if (req.isEmpty() || collect.contains(req)) outFiles.put(f);
            }
        }

        // Deferred attachments: produce only the ones the server's `collect`
        // list accepts, then add the produced file to outFiles. Skipped
        // entries get cleaned up via the existing cleanupPaths machinery
        // (their sourcePath was registered there at enqueue time).
        //
        // When a deferred attachment fails with a known reason (e.g. video
        // ring has no IDR yet), write the reason to a tiny text file and
        // upload it under a non-builtin multipart field. The server's enrich
        // route (issues.routes.ts) routes any non-builtin field through to
        // gameAttachments, so the diagnostic surfaces automatically in the
        // event detail without server-side changes.
        JSONArray deferredArr = manifest.optJSONArray("deferredFiles");
        if (deferredArr != null) {
            for (int i = 0; i < deferredArr.length(); i++) {
                JSONObject d = deferredArr.getJSONObject(i);
                String req = d.optString("requires", "");
                if (!req.isEmpty() && !collect.contains(req)) continue;
                DeferredResult dr = produceDeferred(d);
                JSONArray cleanup = manifest.optJSONArray("cleanupPaths");
                if (cleanup == null) {
                    cleanup = new JSONArray();
                    manifest.put("cleanupPaths", cleanup);
                }
                if (dr.path != null) {
                    JSONObject f = new JSONObject();
                    f.put("field", d.optString("field"));
                    f.put("filename", d.optString("filename"));
                    f.put("contentType", d.optString("contentType"));
                    f.put("path", dr.path);
                    if (!req.isEmpty()) f.put("requires", req);
                    outFiles.put(f);
                    cleanup.put(dr.path);
                } else if (dr.reason != null) {
                    String diagPath = writeDiagFile(d.optString("field", "deferred"), dr.reason);
                    if (diagPath != null) {
                        JSONObject f = new JSONObject();
                        // Field name is non-builtin (camelCase prefixed with
                        // `bugpunchDiag_`) so the server treats it as a game
                        // attachment. Filename + contentType keep it readable
                        // in the dashboard's attachment viewer.
                        f.put("field", "bugpunchDiag_" + d.optString("field", "deferred"));
                        f.put("filename", d.optString("field", "deferred") + "_unavailable.txt");
                        f.put("contentType", "text/plain");
                        f.put("path", diagPath);
                        outFiles.put(f);
                        cleanup.put(diagPath);
                    }
                }
            }
            manifest.remove("deferredFiles");
        }

        if (outFiles.length() == 0) return false;

        // Strip the JSON-only bits and rewrite headers (drop Content-Type so
        // sendMultipart controls it). Keep cleanupPaths as-is — they cover
        // every attachment, including those filtered out, so orphaned files
        // get cleaned up after phase 2 succeeds.
        manifest.put("stage", "enrich");
        manifest.put("url", enrichTemplate.replace("{id}", eventId));
        manifest.remove("enrichUrlTemplate");
        manifest.remove("rawJsonBody");
        JSONObject headers = manifest.optJSONObject("headers");
        if (headers != null) headers.remove("Content-Type");
        manifest.put("files", outFiles);
        manifest.put("attempts", 0);
        writeText(manifestFile, manifest.toString());
        // Kick the worker so phase 2 runs right away — same-tick re-entry is
        // safe, drainInternal just walks the dir again.
        sHandler.post(new Runnable() { @Override public void run() { drainInternal(); } });
        return true;
    }

    /**
     * Enqueue a JSON POST. Used by directive enrichment \u2014 same retry +
     * app-kill survival as crash/bug multipart uploads.
     */
    /**
     * Build the {@code attachmentsAvailable} set advertised on the phase-1
     * preflight body. THE RULE: every attachment with a non-empty
     * {@code requires} key, regardless of whether it's regular or deferred,
     * MUST be included. The server's per-fingerprint budget filter
     * ({@code issues/ingest.ts}: {@code if (offered.has(f))}) drops any
     * field absent from this set, which is silent on the client side —
     * regressions here look like "logs / screenshots randomly missing on
     * crashes" with no error.
     *
     * <p>Centralised here so callers can't forget one of the lists; the
     * iOS uploader follows the same contract (see
     * {@code sdk/docs/upload-manifest.md}).
     */
    static java.util.HashSet<String> collectAvailableRequires(
            List<FileAttachment> attachments, List<DeferredAttachment> deferred) {
        java.util.HashSet<String> available = new java.util.HashSet<>();
        if (attachments != null) {
            for (FileAttachment a : attachments) {
                if (a.requires != null && !a.requires.isEmpty()) {
                    available.add(a.requires);
                }
            }
        }
        if (deferred != null) {
            for (DeferredAttachment d : deferred) {
                if (d.requires != null && !d.requires.isEmpty()) {
                    available.add(d.requires);
                }
            }
        }
        return available;
    }

    /** Outcome of a deferred-attachment produce step. Either {@code path} is
     *  non-null (success — file ready to upload) or {@code reason} is non-null
     *  (failure with a stable token; surfaced as a diagnostic text attachment).
     *  Both null means "skip silently" (legacy / unknown kinds). */
    private static final class DeferredResult {
        final String path;
        final String reason;
        DeferredResult(String path, String reason) { this.path = path; this.reason = reason; }
    }

    private static DeferredResult produceDeferred(JSONObject d) {
        String kind = d.optString("kind", "");
        String src  = d.optString("sourcePath", "");
        if (kind.isEmpty() || src.isEmpty()) {
            return new DeferredResult(null, "missing_kind_or_source");
        }
        try {
            switch (kind) {
                case "video_ring": {
                    BugpunchVideoRingRemuxer.Result r =
                        BugpunchVideoRingRemuxer.remux(src, sQueueDir);
                    return new DeferredResult(r.path, r.reason);
                }
                default:
                    Log.w(TAG, "produceDeferred: unknown kind=" + kind);
                    return new DeferredResult(null, "unknown_kind_" + kind);
            }
        } catch (Throwable t) {
            Log.w(TAG, "produceDeferred(" + kind + ") failed", t);
            return new DeferredResult(null, "produce_threw");
        }
    }

    /** Write a one-line diagnostic txt for a deferred attachment that failed
     *  to produce. Returns the absolute path, or null if the write failed
     *  (in which case we silently skip — diagnostics are best-effort). */
    private static String writeDiagFile(String fieldName, String reason) {
        try {
            File out = new File(sQueueDir,
                "bugpunch_diag_" + fieldName + "_" + UUID.randomUUID() + ".txt");
            FileOutputStream fos = new FileOutputStream(out);
            fos.write(reason.getBytes(StandardCharsets.UTF_8));
            fos.close();
            return out.getAbsolutePath();
        } catch (Throwable t) {
            Log.w(TAG, "writeDiagFile failed", t);
            return null;
        }
    }

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
                    BugpunchSdkErrorOverlay.reportThrowable("BugpunchUploader", "enqueueJson", t);
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
