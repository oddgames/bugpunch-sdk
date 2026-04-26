using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace ODDGames.Bugpunch.Editor
{
    internal enum UploadResult { Success, Failed, Cancelled }

    // ── Server round-trip: check then upload missing ──
    internal static class SymbolUploadClient
    {
        // Up to MaxUploadAttempts tries per file with exponential backoff.
        // Only network/server-side transient errors are retried — 4xx
        // responses (auth fail, bad request) come back as terminal.
        const int MaxUploadAttempts = 3;

        // How many of a single file's parts to PUT to S3 concurrently.
        // Each part = its own TCP flow; 4 streams is enough to saturate
        // a typical residential pipe over a same-region S3 endpoint
        // without thrashing the editor's threadpool.
        const int PartsPerFileConcurrency = 4;

        // Per-part PUT deadline — a 64 MB part on a slow residential link
        // can take a minute or two, so give plenty of headroom before
        // concluding the transfer is dead. CancellationTokenSource
        // linked-with-timeout is the HttpClient equivalent of the old
        // UnityWebRequest stall watchdog.
        const double S3PartTimeoutSeconds = 300.0;

        internal const int SymStagingBuffer = 4 * 1024 * 1024;

        public static void UploadSymbols(
            ODDGames.Bugpunch.DeviceConnect.BugpunchConfig config,
            List<SymbolFile> files,
            string projectRoot,
            bool interactive,
            CancellationTokenSource cts)
        {
            // Large uploads bypass any CDN body-size cap via uploadServerUrl if set
            // (e.g. DNS-only api.bugpunch.com straight to origin). /check is tiny —
            // keep it on the regular serverUrl so CF caching/TLS still apply.
            var baseUrl = BugpunchSymbolUploader.NormalizeBaseUrl(config.serverUrl);
            var uploadBaseUrl = string.IsNullOrWhiteSpace(config.uploadServerUrl)
                ? baseUrl
                : BugpunchSymbolUploader.NormalizeBaseUrl(config.uploadServerUrl);

            HashSet<string> missing;
            try { missing = QueryMissing(baseUrl, config.apiKey, files, cts, interactive); }
            catch (OperationCanceledException)
            {
                BugpunchLog.Warn("SymbolUploader", "Symbol check cancelled.");
                return;
            }
            if (missing == null) // user cancelled during poll
            {
                BugpunchLog.Warn("SymbolUploader", "Symbol check cancelled.");
                return;
            }
            if (missing.Count == 0)
            {
                BugpunchLog.Info("SymbolUploader", $"Symbol store already has all {files.Count} files. Nothing to upload.");
                return;
            }

            // Filter to only the files the server doesn't already have.
            var toUpload = new List<SymbolFile>(missing.Count);
            foreach (var f in files)
            {
                if (missing.Contains(f.BuildId)) toUpload.Add(f);
            }

            // Run uploads in parallel. All per-file I/O (init, S3 part PUTs,
            // complete, abort) goes through HttpClient — UnityWebRequest's
            // constructor requires the main thread even though libcurl does
            // the transfer elsewhere, and using it from threadpool workers
            // would blow up every upload with "Create can only be called from
            // the main thread." UploadOne only touches main-thread Unity APIs
            // when interactive=true — which we suppress here in favour of an
            // aggregate progress bar driven from the dispatch loop.
            // Concurrency is configurable via BugpunchConfig.symbolUploadConcurrency
            // (default 6, range clamped 1-16 to avoid silly values).
            var concurrency = Math.Max(1, Math.Min(16, config.symbolUploadConcurrency));
            var (uploaded, failed, totalBytes) = UploadFilesParallel(
                uploadBaseUrl, config.apiKey, toUpload, concurrency, interactive, cts);

            var summary = $"Uploaded {uploaded}/{missing.Count} missing symbol files " +
                $"({totalBytes / 1024 / 1024}MB). {files.Count - missing.Count} already on server.";
            if (failed > 0) summary += $" {failed} failed.";
            if (cts.IsCancellationRequested) summary += " Batch cancelled.";
            BugpunchLog.Info("SymbolUploader", summary);

            // Phase 2 — IL2CPP source-line enrichment.
            // The mapping is derived from the cpp source tree (ABI-independent),
            // so we build it once and POST it under each libil2cpp.so build-id
            // we just queued. Cheap (file is ~100-300 KB gzipped) and matches
            // the lookup key the symbolicator will use at crash time. Skipped
            // silently if there's no libil2cpp in this batch (other platforms,
            // partial uploads) or no cpp source markers (first build after
            // enabling --emit-source-mapping triggers a full IL2CPP rebuild).
            if (cts.IsCancellationRequested) return;
            IL2CppMappingUploader.UploadIl2cppMappingIfPresent(uploadBaseUrl, config.apiKey, files, projectRoot, interactive, cts);
        }

        static HashSet<string> QueryMissing(
            string baseUrl, string apiKey, List<SymbolFile> files,
            CancellationTokenSource cts, bool interactive)
        {
            // Build { items: [{ buildId, abi, filename }, ...] } manually to
            // avoid a JSON dependency — fields are all simple strings.
            var sb = new StringBuilder();
            sb.Append("{\"items\":[");
            for (int i = 0; i < files.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"buildId\":\"").Append(files[i].BuildId).Append('"')
                  .Append(",\"abi\":\"").Append(JsonEscape(files[i].Abi)).Append('"')
                  .Append(",\"filename\":\"").Append(JsonEscape(files[i].Filename)).Append('"')
                  .Append(",\"platform\":\"android\"}");
            }
            sb.Append("]}");

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/symbols/check")
            {
                Content = new StringContent(sb.ToString(), Encoding.UTF8, "application/json"),
            };
            req.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);

            var sendTask = GetHttp().SendAsync(req, cts.Token);
            var start = DateTime.UtcNow;
            if (!PollForCompletion(sendTask, cts, interactive,
                () => $"Checking server for missing symbols… ({(DateTime.UtcNow - start).TotalSeconds:F0}s)",
                () => 0f))
            {
                return null;
            }

            HttpResponseMessage resp;
            string body;
            try
            {
                resp = sendTask.GetAwaiter().GetResult();
                body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                throw new Exception($"/api/symbols/check failed ({baseUrl}): {ex.Message}");
            }

            if (!resp.IsSuccessStatusCode)
            {
                throw new Exception($"/api/symbols/check failed: HTTP {(int)resp.StatusCode} — {body}");
            }
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int idx = body.IndexOf("\"missing\"", StringComparison.Ordinal);
            if (idx < 0) return result;
            int lb = body.IndexOf('[', idx);
            int rb = body.IndexOf(']', lb + 1);
            if (lb < 0 || rb < 0) return result;
            foreach (var piece in body.Substring(lb + 1, rb - lb - 1).Split(','))
            {
                var clean = piece.Trim().Trim('"');
                if (clean.Length > 0) result.Add(clean.ToLowerInvariant());
            }
            return result;
        }

        // Shared HttpClient. HttpClient is designed to be reused for many
        // requests — creating per-upload wastes sockets. 60min timeout covers
        // the worst-case libil2cpp symbol upload over a slow link; shorter
        // stalls surface via the cancelable progress bar instead.
        static HttpClient s_http;
        static HttpClient GetHttp()
        {
            if (s_http == null)
            {
                s_http = new HttpClient(new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                });
                s_http.Timeout = TimeSpan.FromMinutes(60);
            }
            return s_http;
        }

        // Run uploads in parallel up to `concurrency` at a time. Each file
        // goes through the full retry-with-backoff path. Progress is shown
        // as a single aggregate bar on the main thread; per-file progress
        // is suppressed (interactive=false) because EditorUtility calls
        // can't safely originate from threadpool workers. SemaphoreSlim
        // bounds concurrent HttpClient requests in flight; observed cancel
        // bubbles into running uploads via the shared CTS.
        static (int uploaded, int failed, long totalBytes) UploadFilesParallel(
            string baseUrl, string apiKey, List<SymbolFile> toUpload,
            int concurrency, bool interactive, CancellationTokenSource cts)
        {
            int uploaded = 0;
            int failed = 0;
            long totalBytes = 0;
            int total = toUpload.Count;
            if (total == 0) return (0, 0, 0);

            // Total bytes to move across the batch — used to render byte-
            // based percentage and MB/s rate in the progress bar. Bytes are
            // more useful than file count: a 500 MB libil2cpp dominates a
            // 2 MB libmain by ~250x, so "3/5 files done" when the remaining
            // two are libil2cpp is closer to 20% than 60%.
            long expectedBytes = 0;
            foreach (var f in toUpload) expectedBytes += f.Size;
            var startUtc = DateTime.UtcNow;

            int dispatched = 0;
            int completed = 0;
            using var sem = new SemaphoreSlim(Math.Max(1, concurrency), Math.Max(1, concurrency));
            var tasks = new List<Task>(total);

            foreach (var file in toUpload)
            {
                if (cts.IsCancellationRequested) break;
                try { sem.Wait(cts.Token); }
                catch (OperationCanceledException) { break; }

                var idx = Interlocked.Increment(ref dispatched);
                var fileLocal = file;
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        var result = UploadOne(baseUrl, apiKey, fileLocal, idx, total,
                            interactive: false, cts);
                        if (result == UploadResult.Success)
                        {
                            Interlocked.Increment(ref uploaded);
                            Interlocked.Add(ref totalBytes, fileLocal.Size);
                        }
                        else if (result == UploadResult.Failed)
                        {
                            Interlocked.Increment(ref failed);
                        }
                        // Cancelled from inside UploadOne — already reflected
                        // by cts; nothing else to count here.
                    }
                    finally
                    {
                        Interlocked.Increment(ref completed);
                        sem.Release();
                    }
                }));
            }

            // Main-thread aggregate progress + cancel button. Polls the
            // shared counters and yields back to the editor every ~250ms.
            // EditorUtility.DisplayCancelableProgressBar is a no-op in
            // batch mode, so the same loop works there.
            //
            // The progress bar MUST be cleared no matter how this exits —
            // exception, cancel, success, partial failure. The outer caller
            // (BugpunchPostBuildHook) also has a finally{ClearProgressBar()},
            // but the IL2CPP-mapping upload step runs between us and that
            // outer finally, so a stale bar from this loop would shadow the
            // mapping upload's own progress messages until that step
            // finished. Clearing here keeps the UX clean per phase.
            try
            {
                var allDone = Task.WhenAll(tasks);
                while (!allDone.IsCompleted)
                {
                    Thread.Sleep(250);
                    int doneNow = Volatile.Read(ref completed);
                    int inFlight = tasks.Count - doneNow;
                    if (interactive)
                    {
                        // Bytes accounted for = fully-uploaded files only.
                        // In-flight file bytes aren't visible without a
                        // custom HttpContent; completed-file granularity
                        // is good enough for a progress bar.
                        var bytesDone = Volatile.Read(ref totalBytes);
                        var elapsed = (DateTime.UtcNow - startUtc).TotalSeconds;
                        var mbDone = bytesDone / 1048576.0;
                        var mbTotal = expectedBytes / 1048576.0;
                        var mbps = elapsed > 0.1 ? mbDone / elapsed : 0.0;
                        var pctBytes = expectedBytes > 0 ? (float)((double)bytesDone / expectedBytes) : 0f;
                        var pctDisplay = (int)(pctBytes * 100);
                        var msg = $"{mbDone:F1} / {mbTotal:F1} MB ({pctDisplay}%) — {doneNow}/{total} file(s), {inFlight} in flight — {mbps:F2} MB/s";
                        if (EditorUtility.DisplayCancelableProgressBar(BugpunchSymbolUploader.PROGRESS_TITLE, msg, pctBytes))
                        {
                            cts.Cancel();
                        }
                    }
                }
                try { allDone.Wait(); } catch { /* surfaced per-task already */ }
            }
            finally
            {
                if (interactive) EditorUtility.ClearProgressBar();
            }
            return (uploaded, failed, totalBytes);
        }

        static UploadResult UploadOne(
            string baseUrl, string apiKey, SymbolFile f,
            int index, int total, bool interactive,
            CancellationTokenSource cts)
        {
            if (!File.Exists(f.TempPath))
            {
                BugpunchLog.Error("SymbolUploader", $"Symbol temp file missing before upload: {f.TempPath}");
                return UploadResult.Failed;
            }

            // Compute SHA-256 once and reuse across retry attempts. Server
            // re-hashes the assembled S3 object and rejects on mismatch,
            // catching truncation / wrong-file uploads.
            string sha256;
            try { sha256 = ComputeSha256Hex(f.TempPath); }
            catch (Exception ex)
            {
                BugpunchLog.Error("SymbolUploader", $"SHA-256 failed for {f.Filename}: {ex.Message}");
                return UploadResult.Failed;
            }
            var totalBytes = new FileInfo(f.TempPath).Length;

            for (int attempt = 1; attempt <= MaxUploadAttempts; attempt++)
            {
                var (result, retryable) = UploadOneDirectAttempt(
                    baseUrl, apiKey, f, sha256, totalBytes,
                    index, total, attempt, interactive, cts);

                if (result == UploadResult.Success || result == UploadResult.Cancelled) return result;
                if (!retryable || attempt == MaxUploadAttempts) return result;

                var backoffMs = 1000 * (1 << (attempt - 1));
                BugpunchLog.Warn("SymbolUploader", $"Direct upload attempt {attempt}/{MaxUploadAttempts} failed for {f.Filename}; " +
                                 $"retrying in {backoffMs}ms");
                var waited = 0;
                while (waited < backoffMs)
                {
                    if (cts.IsCancellationRequested) return UploadResult.Cancelled;
                    Thread.Sleep(50);
                    waited += 50;
                }
            }
            return UploadResult.Failed;
        }

        // ── Direct S3 multipart upload (init → parallel PUTs → complete) ──
        //
        // 3-step flow that bypasses the bugpunch server for the .so bytes:
        //   1. POST /api/symbols/upload-direct/init → presigned PUT URLs per part
        //   2. PUT each part in parallel directly to S3 → ETag per part
        //   3. POST /api/symbols/upload-direct/complete → server finalises
        //      multipart in S3 + builds sidecar from the assembled object
        //
        // Each part PUT is its own TCP flow, so this sidesteps the BDP cap
        // that limits single-stream uploads on high-RTT paths. With the box
        // in Singapore the per-flow ceiling is already high so the win
        // matters most for the largest file (libil2cpp.dbg.so at Debugging).
        static (UploadResult result, bool retryable) UploadOneDirectAttempt(
            string baseUrl, string apiKey, SymbolFile f,
            string sha256, long totalBytes,
            int index, int total, int attempt,
            bool interactive, CancellationTokenSource cts)
        {
            var totalMb = totalBytes / 1048576.0;
            var attemptSuffix = attempt > 1 ? $" (attempt {attempt}/{MaxUploadAttempts})" : "";

            // Track which init we created so the outer finally can both clean
            // up on error (S3 multipart abort to avoid orphan parts costing
            // money) and clear the progress bar in interactive mode no matter
            // how we exit. Without these we've seen stuck progress bars and
            // billable orphan multipart parts after surprising exception paths.
            InitResponse init = null;
            bool succeeded = false;
            try
            {
                // Step 1 — /init
                try
                {
                    init = PostInit(baseUrl, apiKey, f, totalBytes, cts);
                }
                catch (Exception ex)
                {
                    BugpunchLog.Error("SymbolUploader", $"/upload-direct/init failed for {f.Filename}: {ex.Message}");
                    return (UploadResult.Failed, true);
                }
                if (init == null) return (UploadResult.Cancelled, false);

                if (interactive)
                {
                    EditorUtility.DisplayCancelableProgressBar(
                        BugpunchSymbolUploader.PROGRESS_TITLE,
                        $"{f.Filename} — uploading {init.Parts.Count} parts ({totalMb:F1} MB) — {index} of {total}{attemptSuffix}",
                        0f);
                }

                // Step 2 — parallel part PUTs
                var etags = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
                var failedParts = new System.Collections.Concurrent.ConcurrentBag<int>();
                var bytesUploaded = 0L;
                using (var sem = new SemaphoreSlim(PartsPerFileConcurrency, PartsPerFileConcurrency))
                {
                    var partTasks = new List<Task>(init.Parts.Count);
                    foreach (var p in init.Parts)
                    {
                        if (cts.IsCancellationRequested) break;
                        try { sem.Wait(cts.Token); }
                        catch (OperationCanceledException) { break; }

                        var partLocal = p;
                        partTasks.Add(Task.Run(() =>
                        {
                            try
                            {
                                var partOffset = (long)(partLocal.PartNumber - 1) * init.PartSize;
                                var thisPartSize = (int)Math.Min((long)init.PartSize, totalBytes - partOffset);
                                var etag = PutS3Part(partLocal.Url, f.TempPath, partOffset, thisPartSize, cts);
                                if (!string.IsNullOrEmpty(etag))
                                {
                                    etags[partLocal.PartNumber] = etag;
                                    Interlocked.Add(ref bytesUploaded, thisPartSize);
                                }
                                else
                                {
                                    failedParts.Add(partLocal.PartNumber);
                                }
                            }
                            finally { sem.Release(); }
                        }));
                    }
                    Task.WaitAll(partTasks.ToArray());
                }

                if (cts.IsCancellationRequested) return (UploadResult.Cancelled, false);
                if (etags.Count != init.Parts.Count)
                {
                    BugpunchLog.Error("SymbolUploader", $"{f.Filename}: {failedParts.Count} of {init.Parts.Count} parts failed to upload to S3.");
                    return (UploadResult.Failed, true);
                }

                // Step 3 — /complete
                try
                {
                    if (PostComplete(baseUrl, apiKey, f, sha256, init, etags, cts))
                    {
                        succeeded = true;
                        return (UploadResult.Success, false);
                    }
                    return (UploadResult.Failed, true);
                }
                catch (Exception ex)
                {
                    BugpunchLog.Error("SymbolUploader", $"/upload-direct/complete failed for {f.Filename}: {ex.Message}");
                    return (UploadResult.Failed, true);
                }
            }
            catch (Exception ex)
            {
                // Defensive — anything unexpected (OOM staging part bytes,
                // file gone mid-read, threadpool starvation throwing inside
                // Task.WaitAll) lands here so the finally still runs.
                BugpunchLog.Error("SymbolUploader", $"Direct upload threw for {f.Filename}: {ex.Message}");
                return (UploadResult.Failed, true);
            }
            finally
            {
                // Always clean up the multipart upload on any non-success
                // path so abandoned parts don't sit on S3 ticking up cost.
                // (Bucket lifecycle reaps them after 7d as backstop.)
                if (init != null && !succeeded)
                {
                    TryAbort(baseUrl, apiKey, init, cts);
                }
                // And always clear the per-file progress bar in interactive
                // mode — outer finally clears at end-of-batch but a stuck
                // bar between attempts looks broken.
                if (interactive) EditorUtility.ClearProgressBar();
            }
        }

        // ── /init request + response shape ──

        class InitPart { public int PartNumber; public string Url; }
        class InitResponse
        {
            public string UploadId;
            public string Key;
            public int PartSize;
            public List<InitPart> Parts;
        }

        static InitResponse PostInit(
            string baseUrl, string apiKey, SymbolFile f, long totalBytes,
            CancellationTokenSource cts)
        {
            // Send minimal JSON body manually — keeps zero JSON-library deps
            // in the editor assembly. Server rejects malformed input cleanly.
            var body = new StringBuilder();
            body.Append("{\"buildId\":\"").Append(JsonEscape(f.BuildId)).Append('"')
                .Append(",\"platform\":\"android\"")
                .Append(",\"abi\":\"").Append(JsonEscape(f.Abi ?? "")).Append('"')
                .Append(",\"filename\":\"").Append(JsonEscape(f.Filename ?? "symbol.so")).Append('"')
                .Append(",\"contentLength\":").Append(totalBytes)
                .Append("}");

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/symbols/upload-direct/init")
            {
                Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json"),
            };
            req.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);

            HttpResponseMessage resp;
            string text;
            try
            {
                resp = GetHttp().SendAsync(req, cts.Token).GetAwaiter().GetResult();
                text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
            }
            catch (OperationCanceledException) { return null; }

            if (!resp.IsSuccessStatusCode)
            {
                throw new Exception($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} — {text}");
            }
            return ParseInitResponse(text);
        }

        // Bare-bones JSON parser tuned to the exact shape the server returns.
        // Avoids dragging Newtonsoft into Editor assemblies that may not
        // reference it.
        static InitResponse ParseInitResponse(string json)
        {
            if (string.IsNullOrEmpty(json)) throw new Exception("empty /init response");
            var resp = new InitResponse { Parts = new List<InitPart>() };
            resp.UploadId = ExtractJsonString(json, "uploadId");
            resp.Key = ExtractJsonString(json, "key");
            resp.PartSize = (int)ExtractJsonNumber(json, "partSize");
            if (string.IsNullOrEmpty(resp.UploadId) || string.IsNullOrEmpty(resp.Key) || resp.PartSize <= 0)
            {
                throw new Exception("malformed /init response: " + json.Substring(0, Math.Min(200, json.Length)));
            }
            // Walk the parts array — tolerant to whitespace, expects each
            // entry as {"partNumber":N,"url":"..."}.
            var partsIdx = json.IndexOf("\"parts\"", StringComparison.Ordinal);
            if (partsIdx < 0) throw new Exception("no parts in /init response");
            var lb = json.IndexOf('[', partsIdx);
            var rb = json.IndexOf(']', lb + 1);
            if (lb < 0 || rb < 0) throw new Exception("malformed parts array");
            var partsBody = json.Substring(lb + 1, rb - lb - 1);
            int cur = 0;
            while (cur < partsBody.Length)
            {
                var open = partsBody.IndexOf('{', cur);
                if (open < 0) break;
                var close = partsBody.IndexOf('}', open + 1);
                if (close < 0) break;
                var entry = partsBody.Substring(open, close - open + 1);
                resp.Parts.Add(new InitPart
                {
                    PartNumber = (int)ExtractJsonNumber(entry, "partNumber"),
                    Url = ExtractJsonString(entry, "url"),
                });
                cur = close + 1;
            }
            if (resp.Parts.Count == 0) throw new Exception("no parts parsed from /init");
            // Sort by partNumber defensively — server already orders, but
            // a stray network reorder shouldn't break us.
            resp.Parts.Sort((a, b) => a.PartNumber.CompareTo(b.PartNumber));
            return resp;
        }

        static string ExtractJsonString(string json, string key)
        {
            var k = "\"" + key + "\"";
            var i = json.IndexOf(k, StringComparison.Ordinal);
            if (i < 0) return null;
            i = json.IndexOf(':', i + k.Length);
            if (i < 0) return null;
            // Skip whitespace + opening quote.
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t' || json[i] == ':')) i++;
            if (i >= json.Length || json[i] != '"') return null;
            i++;
            var sb = new StringBuilder();
            while (i < json.Length)
            {
                var c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    var next = json[i + 1];
                    sb.Append(next == 'n' ? '\n' : next == 't' ? '\t' : next == 'r' ? '\r' : next);
                    i += 2;
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        static double ExtractJsonNumber(string json, string key)
        {
            var k = "\"" + key + "\"";
            var i = json.IndexOf(k, StringComparison.Ordinal);
            if (i < 0) return 0;
            i = json.IndexOf(':', i + k.Length);
            if (i < 0) return 0;
            i++;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            var start = i;
            while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '.' || json[i] == '-' || json[i] == 'e' || json[i] == 'E' || json[i] == '+')) i++;
            if (i == start) return 0;
            double.TryParse(json.Substring(start, i - start), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n);
            return n;
        }

        // ── S3 part PUT ──
        //
        // Reads a byte range from the source .so into managed memory and PUTs
        // it to the presigned URL. 64 MB × 4 concurrent ≈ 256 MB peak heap —
        // fine for the editor process. Returns the ETag header on success
        // (S3 echoes back the part's MD5-style identifier we'll need at
        // /complete time). Internally retried for transient errors.
        static string PutS3Part(
            string presignedUrl, string sourcePath, long offset, int length,
            CancellationTokenSource cts)
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (cts.IsCancellationRequested) return null;
                var etag = PutS3PartOnce(presignedUrl, sourcePath, offset, length, cts);
                if (etag != null) return etag;
                if (cts.IsCancellationRequested) return null;
                Thread.Sleep(500 * attempt);
            }
            return null;
        }

        static string PutS3PartOnce(
            string presignedUrl, string sourcePath, long offset, int length,
            CancellationTokenSource cts)
        {
            byte[] buf;
            try
            {
                buf = new byte[length];
                using var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Seek(offset, SeekOrigin.Begin);
                int read = 0;
                while (read < length)
                {
                    var n = fs.Read(buf, read, length - read);
                    if (n <= 0) throw new Exception("unexpected EOF reading part bytes");
                    read += n;
                }
            }
            catch (Exception ex)
            {
                BugpunchLog.Warn("SymbolUploader", $"failed to read part bytes: {ex.Message}");
                return null;
            }

            // Link the outer CTS to a per-part deadline so a hung transfer
            // can't pin a worker forever. HttpClient.Timeout is global to the
            // client and we share one for concurrency, so use a per-request
            // linked token instead.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            linked.CancelAfter(TimeSpan.FromSeconds(S3PartTimeoutSeconds));

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Put, presignedUrl)
                {
                    Content = new ByteArrayContent(buf),
                };
                req.Content.Headers.ContentLength = length;

                using var resp = GetHttp().SendAsync(req, linked.Token).GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode) return null;

                // S3 returns the part's ETag in the response header. Always
                // double-quoted; we keep the quotes since that's what
                // CompleteMultipartUpload expects.
                if (resp.Headers.TryGetValues("ETag", out var etags))
                {
                    foreach (var e in etags) return e;
                }
                return null;
            }
            catch (OperationCanceledException)
            {
                if (cts.IsCancellationRequested) return null;
                BugpunchLog.Warn("SymbolUploader", $"S3 part timed out after {S3PartTimeoutSeconds:F0}s.");
                return null;
            }
            catch (Exception ex)
            {
                BugpunchLog.Warn("SymbolUploader", $"S3 PUT threw: {ex.Message}");
                return null;
            }
        }

        // ── /complete request ──

        static bool PostComplete(
            string baseUrl, string apiKey, SymbolFile f, string sha256,
            InitResponse init, System.Collections.Concurrent.ConcurrentDictionary<int, string> etags,
            CancellationTokenSource cts)
        {
            var sortedNumbers = etags.Keys.ToList();
            sortedNumbers.Sort();

            var body = new StringBuilder();
            body.Append("{\"buildId\":\"").Append(JsonEscape(f.BuildId)).Append('"')
                .Append(",\"platform\":\"android\"")
                .Append(",\"abi\":\"").Append(JsonEscape(f.Abi ?? "")).Append('"')
                .Append(",\"filename\":\"").Append(JsonEscape(f.Filename ?? "symbol.so")).Append('"')
                .Append(",\"sha256\":\"").Append(sha256).Append('"')
                .Append(",\"uploadId\":\"").Append(JsonEscape(init.UploadId)).Append('"')
                .Append(",\"key\":\"").Append(JsonEscape(init.Key)).Append('"')
                .Append(",\"parts\":[");
            for (int i = 0; i < sortedNumbers.Count; i++)
            {
                if (i > 0) body.Append(',');
                var n = sortedNumbers[i];
                body.Append("{\"partNumber\":").Append(n)
                    .Append(",\"etag\":\"").Append(JsonEscape(etags[n])).Append("\"}");
            }
            body.Append("]}");

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/symbols/upload-direct/complete")
            {
                Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json"),
            };
            req.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);

            // /complete on the server runs CompleteMultipartUpload + downloads
            // the assembled object to build the sidecar — can take up to a
            // minute for large files. No artificial timeout beyond the
            // HttpClient 60min default; cts cancel is the user's escape.
            HttpResponseMessage resp;
            string text;
            try
            {
                resp = GetHttp().SendAsync(req, cts.Token).GetAwaiter().GetResult();
                text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
            }
            catch (OperationCanceledException) { return false; }

            if (resp.IsSuccessStatusCode) return true;
            BugpunchLog.Error("SymbolUploader", $"/complete HTTP {(int)resp.StatusCode} — {text}");
            return false;
        }

        static void TryAbort(
            string baseUrl, string apiKey, InitResponse init, CancellationTokenSource cts)
        {
            try
            {
                var body = $"{{\"uploadId\":\"{JsonEscape(init.UploadId)}\",\"key\":\"{JsonEscape(init.Key)}\"}}";
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/symbols/upload-direct/abort")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
                req.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);

                // Best-effort fire and forget; bucket lifecycle reaps any
                // orphans after 7 days regardless. Bound it with a 30s linked
                // deadline so a dead /abort never pins the outer worker.
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                linked.CancelAfter(TimeSpan.FromSeconds(30));
                using var resp = GetHttp().SendAsync(req, linked.Token).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                BugpunchLog.Warn("SymbolUploader", $"/abort failed (non-fatal): {ex.Message}");
            }
        }

        // Single upload attempt. Returns (result, retryable). retryable is
        // true only for transient errors — connection drops, 5xx server
        // (Legacy multipart-to-server upload path was removed when we moved
        // .so uploads to direct-S3 via presigned URLs. Multipart helpers
        // below are still used by the small IL2CPP method-map upload, which
        // stays server-proxied because it's only ~300 KB gzipped.)

        // Streaming SHA-256 over the file — never materialises the .so in
        // managed memory. 4 MB chunks match the upload staging buffer so
        // the OS file cache is exercised the same way for both passes.
        static string ComputeSha256Hex(string filePath)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var fs = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                SymStagingBuffer, FileOptions.SequentialScan);
            var hash = sha.ComputeHash(fs);
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        internal static void WriteMpField(Stream s, string boundary, string name, string value)
        {
            WriteMpAscii(s, $"--{boundary}\r\n");
            WriteMpAscii(s, $"Content-Disposition: form-data; name=\"{name}\"\r\n\r\n");
            WriteMpAscii(s, value ?? "");
            WriteMpAscii(s, "\r\n");
        }

        internal static void WriteMpFileHeader(Stream s, string boundary, string name, string filename, string contentType)
        {
            WriteMpAscii(s, $"--{boundary}\r\n");
            WriteMpAscii(s, $"Content-Disposition: form-data; name=\"{name}\"; filename=\"{filename}\"\r\n");
            WriteMpAscii(s, $"Content-Type: {contentType}\r\n\r\n");
        }

        internal static void WriteMpAscii(Stream s, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            s.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Polls a Task to completion while showing a cancelable progress bar
        /// on the main thread. Returns false if the user pressed Cancel; the
        /// task is signalled via <paramref name="cts"/> and the caller should
        /// treat it as aborted. Exceptions aren't observed here — the caller
        /// must await/GetResult to surface faults.
        /// </summary>
        static bool PollForCompletion(
            Task task,
            CancellationTokenSource cts,
            bool interactive,
            Func<string> message,
            Func<float> progress01)
        {
            if (interactive)
            {
                EditorUtility.DisplayCancelableProgressBar(BugpunchSymbolUploader.PROGRESS_TITLE, message(), Math.Max(0f, progress01()));
            }
            while (!task.IsCompleted)
            {
                Thread.Sleep(100);
                if (cts.IsCancellationRequested) return false;
                if (!interactive) continue;
                if (EditorUtility.DisplayCancelableProgressBar(BugpunchSymbolUploader.PROGRESS_TITLE, message(), Math.Max(0f, progress01())))
                {
                    cts.Cancel();
                    try { task.Wait(TimeSpan.FromSeconds(5)); } catch { /* cancelled/faulted */ }
                    return false;
                }
            }
            return true;
        }

        internal static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20) sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
