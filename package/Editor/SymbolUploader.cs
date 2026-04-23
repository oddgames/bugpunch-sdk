using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Networking;

namespace ODDGames.Bugpunch.Editor
{
    /// <summary>
    /// Post-build hook: for Android IL2CPP builds, finds the unstripped .so
    /// files Unity emits (when Player Settings → Publishing Settings →
    /// Symbols is enabled), reads each library's GNU build-ID, asks the
    /// server which it's missing, uploads only those.
    ///
    /// Symbol bytes are kept on disk throughout — the ELF parser reads only
    /// the header + note segment, and the upload streams via UploadHandlerFile
    /// with a multipart body constructed on disk. Peak managed RAM is bounded
    /// regardless of how large libil2cpp.sym.so gets (can be 400MB+).
    ///
    /// Every long-running stage (zip extraction, /check, each /upload) polls
    /// a shared CancellationTokenSource and renders an EditorUtility
    /// cancelable progress bar, so nothing can silently hang the editor.
    ///
    /// iOS symbols (dSYM) are produced by Xcode, not Unity, so this hook
    /// doesn't cover them — see Tools~/upload-ios-symbols.sh.
    /// </summary>
    public class BugpunchSymbolUploader : IPostprocessBuildWithReport
    {
        // Runs after BugpunchPostBuildHook (callbackOrder = 0) so the build
        // artifact + type DB upload happen first. Symbols are optional polish
        // on top — if they fail, the build itself is still usable.
        public int callbackOrder => 10;

        const string PROGRESS_TITLE = "Bugpunch: uploading symbols";

        public void OnPostprocessBuild(BuildReport report)
        {
            // Unity's BuildAndRun path leaves result==Unknown during post-build hooks;
            // only bail on explicit failure. The symbols.zip presence is the real signal.
            if (report.summary.result == BuildResult.Failed ||
                report.summary.result == BuildResult.Cancelled) return;
            if (report.summary.platform != BuildTarget.Android) return;

            var config = ODDGames.Bugpunch.DeviceConnect.BugpunchConfig.Load();
            if (config == null || string.IsNullOrEmpty(config.apiKey))
            {
                Debug.Log("[Bugpunch.SymbolUploader] Skipping symbol upload — no config / API key.");
                return;
            }
            if (!config.symbolUploadEnabled)
            {
                Debug.Log("[Bugpunch.SymbolUploader] symbolUploadEnabled=false — skipping symbol upload.");
                return;
            }
            if (string.IsNullOrWhiteSpace(config.serverUrl) ||
                !Uri.TryCreate(NormalizeBaseUrl(config.serverUrl), UriKind.Absolute, out _))
            {
                Debug.LogWarning($"[Bugpunch.SymbolUploader] Invalid serverUrl '{config.serverUrl}' — skipping symbol upload.");
                return;
            }

            // Accept SymbolTable ("Public" in the UI) or Debugging ("Full"
            // — DWARF-bearing). Server-side multer is configured for 3 GB
            // per-file uploads and the streaming UnityWebRequest path here
            // doesn't load the .so into managed memory, so the multi-GB
            // libil2cpp.dbg.so at Debugging level is fine. Trade-off:
            //   SymbolTable: ~120 MB libil2cpp.sym.so. Function names via
            //                .symtab + IL2CPP method-map gives method
            //                start-line. Cheap.
            //   Debugging:   ~1-2 GB libil2cpp.dbg.so per ABI. Full DWARF
            //                gives exact crash-line resolution via
            //                llvm-symbolizer. Slow first build (cache
            //                invalidation) and big uploads — opt in when
            //                you need it.
            // None is the only level we can't work with — it produces no
            // symbols.zip at all.
            var symLevel = UnityEditor.Android.UserBuildSettings.DebugSymbols.level;
            if (symLevel != Unity.Android.Types.DebugSymbolLevel.SymbolTable &&
                symLevel != Unity.Android.Types.DebugSymbolLevel.Full)
            {
                Debug.Log($"[Bugpunch.SymbolUploader] Android Symbols level is '{symLevel}' — skipping upload. " +
                          "Set Player Settings → Publishing Settings → Symbols to 'Public' (SymbolTable) " +
                          "or 'Debugging' (Full) to enable symbol upload.");
                return;
            }
            if (symLevel == Unity.Android.Types.DebugSymbolLevel.Full)
            {
                Debug.Log("[Bugpunch.SymbolUploader] Android Symbols level is 'Debugging' (Full DWARF) — uploading. " +
                          "Expect 1-2 GB per ABI for libil2cpp; resolution will be per-instruction.");
            }

            // Batch mode: post-build hook blocks until this returns (OK, no UI
            // thread to free up). Extraction + upload run synchronously on the
            // caller thread; the cancel token exists only so shutdown propagates.
            // Capture main-thread-only state up front so background work can use it.
            //  - BuildReport.summary is main-thread-only.
            //  - Application.dataPath is main-thread-only.
            var outputPath = report.summary.outputPath;
            var projectRoot = Path.GetDirectoryName(Application.dataPath);

            if (Application.isBatchMode)
            {
                var found = new List<SymbolFile>();
                using var cts = new CancellationTokenSource();
                try
                {
                    FindAndroidSymbols(outputPath, projectRoot, found, cts.Token, null);
                    if (found.Count == 0)
                    {
                        Debug.Log("[Bugpunch.SymbolUploader] No Android symbol files found.");
                        return;
                    }
                    UploadSymbols(config, found, projectRoot, interactive: false, cts);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Bugpunch.SymbolUploader] Symbol upload failed: {ex.Message}\n{ex.StackTrace}");
                }
                finally { CleanupTempFiles(found); }
                return;
            }

            // Interactive mode: kick off the extract + upload on a background
            // Task and drive the progress bar via EditorApplication.update.
            // The delayCall callback returns immediately so Unity's "Hold on"
            // dialog never fires — the editor stays fully responsive, Cancel
            // is instant, and the main thread never blocks on I/O.
            KickOffInteractive(config, outputPath, projectRoot);
        }

        // ── Interactive flow state (main-thread only) ────────────────────
        //
        // One pending operation at a time — Unity serialises post-build hooks
        // anyway so there's no concurrency here. Fields are reset by Finalize().

        enum Phase { Extracting, Uploading }

        static Task s_pendingTask;
        static CancellationTokenSource s_pendingCts;
        static List<SymbolFile> s_pendingFound;
        static ExtractState s_pendingState;
        static volatile Phase s_pendingPhase;

        static void KickOffInteractive(
            ODDGames.Bugpunch.DeviceConnect.BugpunchConfig config,
            string outputPath,
            string projectRoot)
        {
            if (s_pendingTask != null)
            {
                Debug.LogWarning("[Bugpunch.SymbolUploader] Symbol upload already in progress — skipping second kickoff.");
                return;
            }
            s_pendingCts = new CancellationTokenSource();
            s_pendingFound = new List<SymbolFile>();
            s_pendingState = new ExtractState();
            s_pendingPhase = Phase.Extracting;

            var cts = s_pendingCts;
            var found = s_pendingFound;
            var state = s_pendingState;

            s_pendingTask = Task.Run(() =>
            {
                FindAndroidSymbols(outputPath, projectRoot, found, cts.Token, state);
                if (cts.IsCancellationRequested) return;
                if (found.Count == 0) return;
                s_pendingPhase = Phase.Uploading;
                // Non-interactive so UploadSymbols skips EditorUtility calls —
                // it's running on a background thread and those APIs are main-
                // thread-only. Main thread draws our own progress bar via Pump.
                UploadSymbols(config, found, projectRoot, interactive: false, cts);
            }, s_pendingCts.Token);

            EditorApplication.update += PumpInteractive;
        }

        static void PumpInteractive()
        {
            var task = s_pendingTask;
            var cts = s_pendingCts;
            var state = s_pendingState;
            var found = s_pendingFound;
            if (task == null)
            {
                EditorApplication.update -= PumpInteractive;
                return;
            }

            if (task.IsCompleted)
            {
                EditorApplication.update -= PumpInteractive;
                EditorUtility.ClearProgressBar();
                try { task.GetAwaiter().GetResult(); }
                catch (OperationCanceledException)
                {
                    Debug.LogWarning("[Bugpunch.SymbolUploader] Symbol upload cancelled.");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Bugpunch.SymbolUploader] Symbol upload failed: {ex.Message}\n{ex.StackTrace}");
                }
                finally
                {
                    CleanupTempFiles(found);
                    try { cts?.Dispose(); } catch { /* already disposed */ }
                    if (found.Count == 0 && !cts.IsCancellationRequested)
                    {
                        Debug.Log("[Bugpunch.SymbolUploader] No Android symbol files found. " +
                            "Enable Player Settings → Publishing Settings → Symbols " +
                            "(Public/Debugging) to produce symbols.zip alongside builds.");
                    }
                    s_pendingTask = null;
                    s_pendingCts = null;
                    s_pendingFound = null;
                    s_pendingState = null;
                }
                return;
            }

            string msg;
            float progress;
            if (s_pendingPhase == Phase.Extracting)
            {
                msg = string.IsNullOrEmpty(state.Current)
                    ? "Scanning output folder for symbols.zip…"
                    : $"Extracting {state.Current} — {state.Bytes / 1048576.0:F1} MB";
                progress = 0f;
            }
            else
            {
                msg = $"Uploading symbols to server ({found.Count} file{(found.Count == 1 ? "" : "s")})…";
                progress = 0.5f;
            }
            if (EditorUtility.DisplayCancelableProgressBar(PROGRESS_TITLE, msg, progress))
            {
                try { cts.Cancel(); } catch { /* already cancelled */ }
            }
        }

        static void CleanupTempFiles(List<SymbolFile> found)
        {
            foreach (var f in found)
            {
                if (!string.IsNullOrEmpty(f.TempPath))
                {
                    try { if (File.Exists(f.TempPath)) File.Delete(f.TempPath); } catch { }
                }
            }
        }

        /// <summary>
        /// Metadata for one extracted symbol file. The .so contents live at
        /// TempPath on disk — never loaded into the managed heap.
        /// </summary>
        struct SymbolFile
        {
            public string BuildId;   // hex, lowercase
            public string Abi;       // "arm64-v8a", "armeabi-v7a", ...
            public string Filename;  // "libil2cpp.sym.so" etc.
            public string TempPath;  // extracted .so on local disk
            public long Size;
        }

        /// <summary>
        /// Progress shared between the background extract Task and the main
        /// thread's progress-bar poll. Plain fields — updates are monotonic so
        /// torn reads just show a slightly stale number.
        /// </summary>
        class ExtractState
        {
            public string Current = "";
            public long Bytes;
        }

        enum UploadResult { Success, Failed, Cancelled }

        // ── Locate the symbols emitted by Unity for the Android build ──

        /// <summary>
        /// Two-pass discovery:
        ///   1. Unity's symbols.zip next to the build output — unstripped Unity
        ///      .so files (libunity, libil2cpp, libmain). Best-quality symbols.
        ///   2. Gradle's merged_native_libs folder under Library/Bee — picks up
        ///      every other .so that ships in the APK (third-party AAR deps
        ///      like libgma.so / libwebrtc.so / etc.) so their crash frames
        ///      resolve to function names. Stripped binaries but the dynamic
        ///      symbol table survives, which is what llvm-symbolizer needs.
        ///   Dedupe across passes by GNU build-id.
        /// </summary>
        static void FindAndroidSymbols(
            string outputPath, string projectRoot,
            List<SymbolFile> into, CancellationToken ct, ExtractState state)
        {
            // Shared across both passes so a build-id that appears in
            // multiple symbols.zip files, under multiple ABI folders of
            // merged_native_libs, or in both sources only gets uploaded once.
            var seen = new HashSet<string>();

            // ── Pass 1: Unity's symbols.zip (unstripped Unity .so files) ──
            if (!string.IsNullOrEmpty(outputPath))
            {
                string searchDir = File.Exists(outputPath)
                    ? Path.GetDirectoryName(outputPath)
                    : Directory.Exists(outputPath) ? outputPath : null;
                if (searchDir != null && Directory.Exists(searchDir))
                {
                    foreach (var zip in Directory.GetFiles(searchDir, "*symbols*.zip", SearchOption.TopDirectoryOnly))
                    {
                        ct.ThrowIfCancellationRequested();
                        ExtractSymbolZip(zip, into, seen, ct, state);
                    }
                }
            }

            // ── Pass 2: Gradle merged_native_libs (third-party .so files) ──
            if (!string.IsNullOrEmpty(projectRoot))
            {
                ScanBuildOutputNativeLibs(projectRoot, into, seen, ct, state);
            }
        }

        /// <summary>
        /// Walks Library/Bee/Android/Prj/.../merged_native_libs/.../lib/&lt;abi&gt;/*.so.
        /// Reads each ELF's GNU build-id straight from disk (no extraction step
        /// needed — these aren't inside a zip), copies to temp so the upload
        /// pipeline owns the file lifetime even if the next build rewrites the
        /// folder. Skips any build-id already collected from symbols.zip — the
        /// symbols.zip versions are unstripped and strictly better.
        /// </summary>
        static void ScanBuildOutputNativeLibs(
            string projectRoot, List<SymbolFile> into, HashSet<string> seen,
            CancellationToken ct, ExtractState state)
        {
            var beeRoot = Path.Combine(projectRoot, "Library", "Bee", "Android", "Prj");
            if (!Directory.Exists(beeRoot)) return;

            foreach (var soPath in Directory.EnumerateFiles(beeRoot, "*.so", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                // Restrict to the merged_native_libs output — there are several
                // intermediate copies (stripped/unstripped/per-task) under Bee
                // and we want the one Gradle hands to the packaging task.
                var norm = soPath.Replace('\\', '/');
                if (!norm.Contains("/merged_native_libs/")) continue;

                var fname = Path.GetFileName(soPath);
                if (state != null) { state.Current = fname; state.Bytes = 0; }

                string buildId;
                try { buildId = ElfBuildId.ReadFromFile(soPath); }
                catch { continue; }
                if (string.IsNullOrEmpty(buildId)) continue;
                if (!seen.Add(buildId)) continue; // already covered by symbols.zip

                var tempPath = Path.Combine(Path.GetTempPath(), $"bp_sym_{Guid.NewGuid():N}.so");
                try { File.Copy(soPath, tempPath, overwrite: false); }
                catch { seen.Remove(buildId); continue; }

                into.Add(new SymbolFile
                {
                    BuildId = buildId,
                    Abi = GuessAbiFromPath(soPath),
                    Filename = fname,
                    TempPath = tempPath,
                    Size = new FileInfo(tempPath).Length,
                });
            }
        }

        static string GuessAbiFromPath(string fullPath)
        {
            // Path ends like ".../merged_native_libs/<variant>/.../out/lib/<abi>/<file>.so"
            var parts = fullPath.Replace('\\', '/').Split('/');
            for (int i = parts.Length - 2; i > 0; i--)
            {
                if (parts[i - 1] == "lib") return parts[i];
            }
            return "";
        }

        static void ExtractSymbolZip(
            string zipPath, List<SymbolFile> into, HashSet<string> seen,
            CancellationToken ct, ExtractState state)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                // Entries look like: <abi>/libunity.sym.so, <abi>/libil2cpp.sym.so, <abi>/libmain.sym.so
                // Some Unity versions omit the .sym prefix. Accept any .so we find.
                if (!entry.FullName.EndsWith(".so", StringComparison.OrdinalIgnoreCase)) continue;
                var abi = GuessAbi(entry.FullName);
                if (state != null) { state.Current = entry.FullName; state.Bytes = 0; }

                // Stream-copy the zip entry straight to a temp file — never
                // materialises the full .so in managed memory. Chunked so
                // cancellation is observed within ~1MB of work.
                var tempPath = Path.Combine(Path.GetTempPath(), $"bp_sym_{Guid.NewGuid():N}.so");
                try
                {
                    using (var es = entry.Open())
                    using (var fs = File.Create(tempPath))
                    {
                        var buf = new byte[1 << 20];
                        int n;
                        while ((n = es.Read(buf, 0, buf.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            fs.Write(buf, 0, n);
                            if (state != null) state.Bytes += n;
                        }
                    }
                }
                catch
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                    throw;
                }

                string buildId = null;
                try { buildId = ElfBuildId.ReadFromFile(tempPath); }
                catch { /* treat as missing build-id below */ }

                if (string.IsNullOrEmpty(buildId))
                {
                    // Lib was linked without --build-id (or had its .note.gnu.build-id
                    // section stripped). Not necessarily a bug — some third-party
                    // prebuilts ship this way. Either way the server can't index it,
                    // because crash reports identify modules by build-id. Frames in
                    // this lib will stay unresolved on the dashboard.
                    Debug.Log($"[Bugpunch.SymbolUploader] {entry.FullName} has no GNU build-id — can't be symbolicated, skipping. " +
                        "Common for vendor-prebuilt libs (e.g. libwebrtc, libjingle); harmless unless you crash inside it.");
                    try { File.Delete(tempPath); } catch { }
                    continue;
                }

                // Same build-id already collected (same zip re-scanned, or
                // duplicate entry across variant zips Unity sometimes emits).
                if (!seen.Add(buildId))
                {
                    try { File.Delete(tempPath); } catch { }
                    continue;
                }

                into.Add(new SymbolFile {
                    BuildId = buildId,
                    Abi = abi,
                    Filename = Path.GetFileName(entry.FullName),
                    TempPath = tempPath,
                    Size = new FileInfo(tempPath).Length,
                });
            }
        }

        static string GuessAbi(string fullName)
        {
            // Entry path like "arm64-v8a/libil2cpp.sym.so". First segment is ABI.
            var i = fullName.IndexOfAny(new[] { '/', '\\' });
            if (i <= 0) return "";
            return fullName.Substring(0, i);
        }

        // ── Server round-trip: check then upload missing ──

        static void UploadSymbols(
            ODDGames.Bugpunch.DeviceConnect.BugpunchConfig config,
            List<SymbolFile> files,
            string projectRoot,
            bool interactive,
            CancellationTokenSource cts)
        {
            // Large uploads bypass any CDN body-size cap via uploadServerUrl if set
            // (e.g. DNS-only api.bugpunch.com straight to origin). /check is tiny —
            // keep it on the regular serverUrl so CF caching/TLS still apply.
            var baseUrl = NormalizeBaseUrl(config.serverUrl);
            var uploadBaseUrl = string.IsNullOrWhiteSpace(config.uploadServerUrl)
                ? baseUrl
                : NormalizeBaseUrl(config.uploadServerUrl);

            HashSet<string> missing;
            try { missing = QueryMissing(baseUrl, config.apiKey, files, cts, interactive); }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("[Bugpunch.SymbolUploader] Symbol check cancelled.");
                return;
            }
            if (missing == null) // user cancelled during poll
            {
                Debug.LogWarning("[Bugpunch.SymbolUploader] Symbol check cancelled.");
                return;
            }
            if (missing.Count == 0)
            {
                Debug.Log($"[Bugpunch.SymbolUploader] Symbol store already has all {files.Count} files. Nothing to upload.");
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

            var summary = $"[Bugpunch.SymbolUploader] Uploaded {uploaded}/{missing.Count} missing symbol files " +
                $"({totalBytes / 1024 / 1024}MB). {files.Count - missing.Count} already on server.";
            if (failed > 0) summary += $" {failed} failed.";
            if (cts.IsCancellationRequested) summary += " Batch cancelled.";
            Debug.Log(summary);

            // Phase 2 — IL2CPP source-line enrichment.
            // The mapping is derived from the cpp source tree (ABI-independent),
            // so we build it once and POST it under each libil2cpp.so build-id
            // we just queued. Cheap (file is ~100-300 KB gzipped) and matches
            // the lookup key the symbolicator will use at crash time. Skipped
            // silently if there's no libil2cpp in this batch (other platforms,
            // partial uploads) or no cpp source markers (first build after
            // enabling --emit-source-mapping triggers a full IL2CPP rebuild).
            if (cts.IsCancellationRequested) return;
            UploadIl2cppMappingIfPresent(uploadBaseUrl, config.apiKey, files, projectRoot, interactive, cts);
        }

        static void UploadIl2cppMappingIfPresent(
            string uploadBaseUrl, string apiKey,
            List<SymbolFile> files, string projectRoot,
            bool interactive, CancellationTokenSource cts)
        {
            var il2cppBuildIds = new List<string>();
            foreach (var f in files)
            {
                if (string.IsNullOrEmpty(f.Filename) || string.IsNullOrEmpty(f.BuildId)) continue;
                if (f.Filename.IndexOf("libil2cpp", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    il2cppBuildIds.Add(f.BuildId);
                }
            }
            if (il2cppBuildIds.Count == 0) return;

            string mappingPath;
            try
            {
                mappingPath = IL2CppMethodMapBuilder.BuildAndStage(projectRoot);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch.SymbolUploader] IL2CPP method map build failed: {ex.Message}");
                return;
            }
            if (string.IsNullOrEmpty(mappingPath)) return;

            try
            {
                int mUploaded = 0;
                int mFailed = 0;
                int idx = 0;
                foreach (var bid in il2cppBuildIds)
                {
                    if (cts.IsCancellationRequested) break;
                    idx++;
                    if (interactive)
                    {
                        EditorUtility.DisplayCancelableProgressBar(
                            PROGRESS_TITLE,
                            $"IL2CPP method map → {bid.Substring(0, Math.Min(12, bid.Length))}… ({idx}/{il2cppBuildIds.Count})",
                            0f);
                    }
                    if (UploadIl2cppMappingOne(uploadBaseUrl, apiKey, bid, mappingPath, cts))
                        mUploaded++;
                    else
                        mFailed++;
                }
                Debug.Log($"[Bugpunch.SymbolUploader] IL2CPP method map uploaded for {mUploaded}/{il2cppBuildIds.Count} libil2cpp build-id(s)" +
                          (mFailed > 0 ? $" ({mFailed} failed)" : "") + ".");
            }
            finally
            {
                try { if (File.Exists(mappingPath)) File.Delete(mappingPath); } catch { }
                // Always clear the IL2CPP mapping progress bar — without
                // this, an exception inside UploadIl2cppMappingOne would
                // leave the bar stuck on screen until the editor restarts.
                if (interactive) EditorUtility.ClearProgressBar();
            }
        }

        static bool UploadIl2cppMappingOne(
            string baseUrl, string apiKey, string buildId, string filePath,
            CancellationTokenSource cts)
        {
            // Same disk-staged multipart pattern as the .so upload — no
            // managed allocation, libcurl streams from disk.
            var bodyPath = StageMappingMultipart(out var boundary, buildId, filePath);
            if (bodyPath == null) return false;

            UnityWebRequest req = null;
            try
            {
                req = new UnityWebRequest($"{baseUrl}/api/symbols/il2cpp-mapping/upload", "POST");
                req.uploadHandler = new UploadHandlerFile(bodyPath);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", $"multipart/form-data; boundary={boundary}");
                req.SetRequestHeader("X-Api-Key", apiKey);
                req.SendWebRequest();

                while (!req.isDone)
                {
                    Thread.Sleep(50);
                    if (cts.IsCancellationRequested) { try { req.Abort(); } catch { } return false; }
                }

                if (req.result == UnityWebRequest.Result.Success) return true;
                Debug.LogWarning($"[Bugpunch.SymbolUploader] IL2CPP map upload failed for {buildId}: " +
                    $"{req.error} — {req.downloadHandler?.text}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch.SymbolUploader] IL2CPP map upload threw for {buildId}: {ex.Message}");
                return false;
            }
            finally
            {
                try { req?.Dispose(); } catch { }
                try { if (File.Exists(bodyPath)) File.Delete(bodyPath); } catch { }
            }
        }

        static string StageMappingMultipart(out string boundary, string buildId, string filePath)
        {
            boundary = "----BugpunchBoundary" + Guid.NewGuid().ToString("N");
            var bodyPath = Path.Combine(Path.GetTempPath(), $"bp_il2map_{Guid.NewGuid():N}.bin");
            try
            {
                using var body = new FileStream(
                    bodyPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    SymStagingBuffer, FileOptions.SequentialScan);
                WriteMpField(body, boundary, "buildId", buildId);
                WriteMpFileHeader(body, boundary, "file", "il2cpp_mapping.json.gz", "application/gzip");
                using (var fs = new FileStream(
                    filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    SymStagingBuffer, FileOptions.SequentialScan))
                {
                    fs.CopyTo(body, SymStagingBuffer);
                }
                WriteMpAscii(body, $"\r\n--{boundary}--\r\n");
                return bodyPath;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Bugpunch.SymbolUploader] Failed to stage IL2CPP mapping body: {ex.Message}");
                try { if (File.Exists(bodyPath)) File.Delete(bodyPath); } catch { }
                return null;
            }
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
                        if (EditorUtility.DisplayCancelableProgressBar(PROGRESS_TITLE, msg, pctBytes))
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

        static UploadResult UploadOne(
            string baseUrl, string apiKey, SymbolFile f,
            int index, int total, bool interactive,
            CancellationTokenSource cts)
        {
            if (!File.Exists(f.TempPath))
            {
                Debug.LogError($"[Bugpunch.SymbolUploader] Symbol temp file missing before upload: {f.TempPath}");
                return UploadResult.Failed;
            }

            // Compute SHA-256 once and reuse across retry attempts. Server
            // re-hashes the assembled S3 object and rejects on mismatch,
            // catching truncation / wrong-file uploads.
            string sha256;
            try { sha256 = ComputeSha256Hex(f.TempPath); }
            catch (Exception ex)
            {
                Debug.LogError($"[Bugpunch.SymbolUploader] SHA-256 failed for {f.Filename}: {ex.Message}");
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
                Debug.LogWarning($"[Bugpunch.SymbolUploader] Direct upload attempt {attempt}/{MaxUploadAttempts} failed for {f.Filename}; " +
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
                    Debug.LogError($"[Bugpunch.SymbolUploader] /upload-direct/init failed for {f.Filename}: {ex.Message}");
                    return (UploadResult.Failed, true);
                }
                if (init == null) return (UploadResult.Cancelled, false);

                if (interactive)
                {
                    EditorUtility.DisplayCancelableProgressBar(
                        PROGRESS_TITLE,
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
                    Debug.LogError($"[Bugpunch.SymbolUploader] {f.Filename}: {failedParts.Count} of {init.Parts.Count} parts failed to upload to S3.");
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
                    Debug.LogError($"[Bugpunch.SymbolUploader] /upload-direct/complete failed for {f.Filename}: {ex.Message}");
                    return (UploadResult.Failed, true);
                }
            }
            catch (Exception ex)
            {
                // Defensive — anything unexpected (OOM staging part bytes,
                // file gone mid-read, threadpool starvation throwing inside
                // Task.WaitAll) lands here so the finally still runs.
                Debug.LogError($"[Bugpunch.SymbolUploader] Direct upload threw for {f.Filename}: {ex.Message}");
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
                Debug.LogWarning($"[Bugpunch.SymbolUploader] failed to read part bytes: {ex.Message}");
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
                Debug.LogWarning($"[Bugpunch.SymbolUploader] S3 part timed out after {S3PartTimeoutSeconds:F0}s.");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch.SymbolUploader] S3 PUT threw: {ex.Message}");
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
            Debug.LogError($"[Bugpunch.SymbolUploader] /complete HTTP {(int)resp.StatusCode} — {text}");
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
                Debug.LogWarning($"[Bugpunch.SymbolUploader] /abort failed (non-fatal): {ex.Message}");
            }
        }

        // Single upload attempt. Returns (result, retryable). retryable is
        // true only for transient errors — connection drops, 5xx server
        // (Legacy multipart-to-server upload path was removed when we moved
        // .so uploads to direct-S3 via presigned URLs. Multipart helpers
        // below are still used by the small IL2CPP method-map upload, which
        // stays server-proxied because it's only ~300 KB gzipped.)

        const int SymStagingBuffer = 4 * 1024 * 1024;

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

        static void WriteMpField(Stream s, string boundary, string name, string value)
        {
            WriteMpAscii(s, $"--{boundary}\r\n");
            WriteMpAscii(s, $"Content-Disposition: form-data; name=\"{name}\"\r\n\r\n");
            WriteMpAscii(s, value ?? "");
            WriteMpAscii(s, "\r\n");
        }

        static void WriteMpFileHeader(Stream s, string boundary, string name, string filename, string contentType)
        {
            WriteMpAscii(s, $"--{boundary}\r\n");
            WriteMpAscii(s, $"Content-Disposition: form-data; name=\"{name}\"; filename=\"{filename}\"\r\n");
            WriteMpAscii(s, $"Content-Type: {contentType}\r\n\r\n");
        }

        static void WriteMpAscii(Stream s, string text)
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
                EditorUtility.DisplayCancelableProgressBar(PROGRESS_TITLE, message(), Math.Max(0f, progress01()));
            }
            while (!task.IsCompleted)
            {
                Thread.Sleep(100);
                if (cts.IsCancellationRequested) return false;
                if (!interactive) continue;
                if (EditorUtility.DisplayCancelableProgressBar(PROGRESS_TITLE, message(), Math.Max(0f, progress01())))
                {
                    cts.Cancel();
                    try { task.Wait(TimeSpan.FromSeconds(5)); } catch { /* cancelled/faulted */ }
                    return false;
                }
            }
            return true;
        }

        static string NormalizeBaseUrl(string url)
        {
            url = (url ?? "").TrimEnd('/');
            if (url.StartsWith("ws://"))  url = "http://" + url.Substring(5);
            if (url.StartsWith("wss://")) url = "https://" + url.Substring(6);
            return url;
        }

        static string JsonEscape(string s)
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

    /// <summary>
    /// Minimal ELF parser for extracting the GNU build-ID from a 64-bit or
    /// 32-bit little-endian Android .so on disk. Supports ELF64 (arm64-v8a,
    /// x86_64) and ELF32 (armeabi-v7a, x86) — the four ABIs Unity targets.
    ///
    /// Reads only the ELF header, program header table, and PT_NOTE segments
    /// (usually &lt;1KB total). The rest of the .so is never touched.
    /// </summary>
    static class ElfBuildId
    {
        const int NT_GNU_BUILD_ID = 3;

        public static string ReadFromFile(string path)
        {
            using var fs = File.OpenRead(path);

            var ehdr = new byte[64];
            if (ReadFull(fs, ehdr, 0, 64) < 64) return null;
            if (ehdr[0] != 0x7F || ehdr[1] != 'E' || ehdr[2] != 'L' || ehdr[3] != 'F') return null;
            bool is64 = ehdr[4] == 2;
            bool isLE = ehdr[5] == 1;
            if (!isLE) return null;

            long phoff = is64 ? (long)ReadU64(ehdr, 32) : ReadU32(ehdr, 28);
            int phentsize = is64 ? ReadU16(ehdr, 54) : ReadU16(ehdr, 42);
            int phnum     = is64 ? ReadU16(ehdr, 56) : ReadU16(ehdr, 44);

            if (phoff <= 0 || phentsize <= 0 || phnum <= 0) return null;
            if (phentsize * (long)phnum > 64 * 1024) return null; // sanity

            fs.Seek(phoff, SeekOrigin.Begin);
            var ph = new byte[phentsize * phnum];
            if (ReadFull(fs, ph, 0, ph.Length) < ph.Length) return null;

            for (int i = 0; i < phnum; i++)
            {
                int off = i * phentsize;
                uint pType = ReadU32(ph, off);
                if (pType != 4 /* PT_NOTE */) continue;

                long pOffset = is64 ? (long)ReadU64(ph, off + 8)  : ReadU32(ph, off + 4);
                long pFilesz = is64 ? (long)ReadU64(ph, off + 32) : ReadU32(ph, off + 16);
                if (pOffset <= 0 || pFilesz <= 0 || pFilesz > 1024 * 1024) continue;

                fs.Seek(pOffset, SeekOrigin.Begin);
                var notes = new byte[pFilesz];
                if (ReadFull(fs, notes, 0, notes.Length) < notes.Length) continue;

                var id = ScanNotesForBuildId(notes);
                if (!string.IsNullOrEmpty(id)) return id;
            }
            return null;
        }

        static int ReadFull(Stream s, byte[] buf, int offset, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buf, offset + read, count - read);
                if (n <= 0) break;
                read += n;
            }
            return read;
        }

        static string ScanNotesForBuildId(byte[] notes)
        {
            int p = 0;
            int end = notes.Length;
            while (p + 12 <= end)
            {
                int namesz = (int)ReadU32(notes, p);
                int descsz = (int)ReadU32(notes, p + 4);
                int type   = (int)ReadU32(notes, p + 8);
                int namePad = (namesz + 3) & ~3;
                int descPad = (descsz + 3) & ~3;
                int descStart = p + 12 + namePad;
                if (type == NT_GNU_BUILD_ID && descsz > 0 && descStart + descsz <= end)
                {
                    var sb = new StringBuilder(descsz * 2);
                    for (int i = 0; i < descsz; i++)
                        sb.Append(notes[descStart + i].ToString("x2"));
                    return sb.ToString();
                }
                p = descStart + descPad;
            }
            return null;
        }

        static ushort ReadU16(byte[] b, int o) => (ushort)(b[o] | (b[o + 1] << 8));
        static uint ReadU32(byte[] b, int o) =>
            (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
        static ulong ReadU64(byte[] b, int o) =>
            ReadU32(b, o) | ((ulong)ReadU32(b, o + 4) << 32);
    }
}
