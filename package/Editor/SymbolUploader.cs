using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

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

        internal const string PROGRESS_TITLE = "Bugpunch: uploading symbols";

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
                BugpunchLog.Info("SymbolUploader", "Skipping symbol upload — no config / API key.");
                return;
            }
            if (!config.symbolUploadEnabled)
            {
                BugpunchLog.Info("SymbolUploader", "symbolUploadEnabled=false — skipping symbol upload.");
                return;
            }
            if (string.IsNullOrWhiteSpace(config.serverUrl) ||
                !Uri.TryCreate(NormalizeBaseUrl(config.serverUrl), UriKind.Absolute, out _))
            {
                BugpunchLog.Warn("SymbolUploader", $"Invalid serverUrl '{config.serverUrl}' — skipping symbol upload.");
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
                BugpunchLog.Info("SymbolUploader", $"Android Symbols level is '{symLevel}' — skipping upload. " +
                          "Set Player Settings → Publishing Settings → Symbols to 'Public' (SymbolTable) " +
                          "or 'Debugging' (Full) to enable symbol upload.");
                return;
            }
            if (symLevel == Unity.Android.Types.DebugSymbolLevel.Full)
            {
                BugpunchLog.Info("SymbolUploader", "Android Symbols level is 'Debugging' (Full DWARF) — uploading. " +
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
                    SymbolDiscovery.FindAndroidSymbols(outputPath, projectRoot, found, cts.Token, null);
                    if (found.Count == 0)
                    {
                        BugpunchLog.Info("SymbolUploader", "No Android symbol files found.");
                        return;
                    }
                    SymbolUploadClient.UploadSymbols(config, found, projectRoot, interactive: false, cts);
                }
                catch (Exception ex)
                {
                    BugpunchLog.Error("SymbolUploader", $"Symbol upload failed: {ex.Message}\n{ex.StackTrace}");
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
                BugpunchLog.Warn("SymbolUploader", "Symbol upload already in progress — skipping second kickoff.");
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
                SymbolDiscovery.FindAndroidSymbols(outputPath, projectRoot, found, cts.Token, state);
                if (cts.IsCancellationRequested) return;
                if (found.Count == 0) return;
                s_pendingPhase = Phase.Uploading;
                // Non-interactive so UploadSymbols skips EditorUtility calls —
                // it's running on a background thread and those APIs are main-
                // thread-only. Main thread draws our own progress bar via Pump.
                SymbolUploadClient.UploadSymbols(config, found, projectRoot, interactive: false, cts);
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
                    BugpunchLog.Warn("SymbolUploader", "Symbol upload cancelled.");
                }
                catch (Exception ex)
                {
                    BugpunchLog.Error("SymbolUploader", $"Symbol upload failed: {ex.Message}\n{ex.StackTrace}");
                }
                finally
                {
                    CleanupTempFiles(found);
                    try { cts?.Dispose(); } catch { /* already disposed */ }
                    if (found.Count == 0 && !cts.IsCancellationRequested)
                    {
                        BugpunchLog.Info("SymbolUploader", "No Android symbol files found. " +
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

        internal static string NormalizeBaseUrl(string url)
        {
            url = (url ?? "").TrimEnd('/');
            if (url.StartsWith("ws://"))  url = "http://" + url.Substring(5);
            if (url.StartsWith("wss://")) url = "https://" + url.Substring(6);
            return url;
        }
    }
}
