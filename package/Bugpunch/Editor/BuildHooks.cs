using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Networking;

namespace ODDGames.Bugpunch.Editor
{
    /// <summary>
    /// Automatic hooks that fire during Unity's build pipeline:
    /// 1. After compilation → export + upload TypeDB
    /// 2. After build → upload build artifact
    /// </summary>
    [InitializeOnLoad]
    public static class BuildHooks
    {
        static BuildHooks()
        {
            // TypeDB upload triggers:
            //   1. On client connect during play mode (OnAnyConnected below) — ensures the
            //      server has fresh types as soon as the editor connects.
            //   2. After a successful Player build (BugpunchPostBuildHook).
            // We intentionally do NOT hook compilationFinished: it fires on every
            // editor recompile including ones cascading from a failed Player build,
            // which would waste bandwidth on a DB the user can't use.
            ODDGames.Bugpunch.DeviceConnect.BugpunchClient.OnAnyConnected += OnClientConnected;
        }

        static void OnClientConnected()
        {
            // The event may fire on a background thread — hop to the main thread.
            EditorApplication.delayCall += TryUploadTypeDatabaseQuiet;
        }

        static void TryUploadTypeDatabaseQuiet()
        {
            try
            {
                TypeDatabaseExporter.ExportAndUploadIfChanged();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Bugpunch] TypeDB auto-upload failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Force upload the type database. Called before builds or on-demand.
        /// </summary>
        public static void UploadTypeDatabase()
        {
            try
            {
                TypeDatabaseExporter.ExportAndUploadIfChanged();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Bugpunch] TypeDB upload failed: {ex.Message}");
            }
        }
    }

    // No pre-build hook needed. TypeDB upload happens only after a successful
    // build (see BugpunchPostBuildHook).

    /// <summary>
    /// Post-build hook: uploads the build artifact to the server.
    /// Tags it with the current Plastic SCM changeset/branch if available.
    /// </summary>
    public class BugpunchPostBuildHook : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        static int s_invocationCounter;

        public void OnPostprocessBuild(BuildReport report)
        {
            var n = ++s_invocationCounter;
            var s = report.summary;
            Debug.Log(
                $"[Bugpunch][diag] PostprocessBuild #{n} " +
                $"result={s.result} platform={s.platform} " +
                $"outputPath='{s.outputPath}' " +
                $"totalErrors={s.totalErrors} totalWarnings={s.totalWarnings} " +
                $"totalTimeMs={(long)s.totalTime.TotalMilliseconds}");

            var config = ODDGames.Bugpunch.DeviceConnect.BugpunchConfig.Load();
            if (config == null || string.IsNullOrEmpty(config.apiKey)) return;

            // Unity's BuildAndRun path leaves report.summary.result == Unknown at this
            // point — the final 'Succeeded' marker is written after IPostprocessBuildWithReport
            // returns. Only bail on explicit failure; we verify the output file below.
            if (report.summary.result == BuildResult.Failed ||
                report.summary.result == BuildResult.Cancelled)
            {
                Debug.Log($"[Bugpunch] Build {report.summary.result} — skipping artifact + type DB upload");
                return;
            }

            // Upload type DB only on build success — saves bandwidth on iterative
            // failed Gradle builds. Type DB is small (~MB) so it's not behind the
            // same opt-in flag as the full artifact upload.
            Debug.Log("[Bugpunch] Post-build: uploading type database...");
            BuildHooks.UploadTypeDatabase();

            if (!config.buildUploadEnabled)
            {
                Debug.Log("[Bugpunch] buildUploadEnabled=false — skipping artifact upload.");
                return;
            }

            var outputPath = report.summary.outputPath;
            if (!File.Exists(outputPath))
            {
                Debug.LogWarning($"[Bugpunch] Build output not found: {outputPath}");
                return;
            }

            var fileName = Path.GetFileName(outputPath);
            var platform = GetPlatformString(report.summary.platform);
            var changeset = GetPlasticChangeset();
            var branch = GetPlasticBranch();

            // Batch mode (`-batchmode -quit`) exits as soon as the post-build
            // hook returns, so async upload would die mid-flight. Run sync
            // with console-log progress so CI captures transfer rate.
            if (Application.isBatchMode)
            {
                Debug.Log($"[Bugpunch] Post-build (batch): uploading artifact {fileName}...");
                try
                {
                    UploadArtifactSync(config, outputPath, fileName, platform, changeset, branch);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Bugpunch] Artifact upload failed: {ex.Message}");
                }
                return;
            }

            Debug.Log($"[Bugpunch] Post-build: queueing artifact upload {fileName}...");
            try
            {
                // Kick off on delayCall so the post-build hook returns
                // immediately and Unity's progress bar unfreezes.
                EditorApplication.delayCall += () => StartArtifactUpload(
                    config, outputPath, fileName, platform, changeset, branch);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Bugpunch] Artifact upload scheduling failed: {ex.Message}");
            }
        }

        // Abort any upload that makes no forward progress for this long.
        // Covers the classic "stuck at 0.00 MB/s forever" failure mode —
        // e.g. the server stopped accepting, a middlebox reset mid-stream,
        // or the local network went down.
        const double StallTimeoutSeconds = 90.0;

        // ── Sync upload (batch mode) ──────────────────────────────────────
        // Same streaming multipart, spins in the post-build hook and logs
        // progress every second so CI logs show transfer rate. Stall
        // detection aborts the request if throughput drops to zero for
        // StallTimeoutSeconds so CI jobs can't hang forever.
        static void UploadArtifactSync(
            ODDGames.Bugpunch.DeviceConnect.BugpunchConfig config,
            string filePath, string fileName, string platform, string changeset, string branch)
        {
            var bodyPath = BuildMultipartBody(
                out var boundary, filePath, fileName, platform, changeset, branch);
            if (bodyPath == null) return;

            var totalMb = new FileInfo(bodyPath).Length / 1048576.0;
            using var req = new UnityWebRequest(ResolveUploadUrl(config), "POST");
            req.uploadHandler = new UploadHandlerFile(bodyPath);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", $"multipart/form-data; boundary={boundary}");
            req.SetRequestHeader("X-Api-Key", config.apiKey);
            req.SendWebRequest();

            var start = DateTime.UtcNow;
            var lastLog = start;
            var lastProgress = start;
            ulong lastBytes = 0;
            ulong lastProgressBytes = 0;
            bool stalled = false;
            try
            {
                while (!req.isDone)
                {
                    System.Threading.Thread.Sleep(200);
                    var uploaded = req.uploadedBytes;
                    var nowUtc = DateTime.UtcNow;

                    if (uploaded > lastProgressBytes)
                    {
                        lastProgressBytes = uploaded;
                        lastProgress = nowUtc;
                    }
                    else if ((nowUtc - lastProgress).TotalSeconds > StallTimeoutSeconds)
                    {
                        Debug.LogError($"[Bugpunch] Artifact upload stalled for {StallTimeoutSeconds:F0}s — aborting.");
                        req.Abort();
                        stalled = true;
                        break;
                    }

                    var dt = (nowUtc - lastLog).TotalSeconds;
                    if (dt < 1.0) continue;
                    var mbs = (uploaded - lastBytes) / 1048576.0 / dt;
                    Debug.Log($"[Bugpunch] Upload {uploaded / 1048576.0:F1} / {totalMb:F1} MB @ {mbs:F2} MB/s");
                    lastBytes = uploaded;
                    lastLog = nowUtc;
                }

                if (stalled) return;

                var elapsed = (DateTime.UtcNow - start).TotalSeconds;
                if (req.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[Bugpunch] Build artifact uploaded: {fileName} " +
                              $"({totalMb:F1}MB in {elapsed:F1}s, avg {totalMb / Math.Max(0.01, elapsed):F2} MB/s)");
                }
                else
                {
                    Debug.LogError($"[Bugpunch] Artifact upload failed: {req.error} — {req.downloadHandler?.text}");
                }
            }
            finally
            {
                TryDelete(bodyPath);
            }
        }

        static string ResolveUploadUrl(ODDGames.Bugpunch.DeviceConnect.BugpunchConfig config)
        {
            var url = config.serverUrl.TrimEnd('/');
            if (url.StartsWith("ws://")) url = "http://" + url.Substring(5);
            else if (url.StartsWith("wss://")) url = "https://" + url.Substring(6);
            return $"{url}/api/builds/upload";
        }

        // Stage multipart body on disk so UploadHandlerFile streams straight
        // from disk — no managed allocation for the apk payload. Uses a 4MB
        // copy buffer for the big file append to keep throughput high.
        const int StagingCopyBufferBytes = 4 * 1024 * 1024;
        static string BuildMultipartBody(
            out string boundary, string filePath, string fileName,
            string platform, string changeset, string branch)
        {
            boundary = "----BugpunchBoundary" + Guid.NewGuid().ToString("N");
            var bodyPath = Path.Combine(Path.GetTempPath(), $"bp_artifact_{Guid.NewGuid():N}.bin");
            try
            {
                using var body = new FileStream(
                    bodyPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    StagingCopyBufferBytes, FileOptions.SequentialScan);
                WriteField(body, boundary, "platform", platform);
                WriteField(body, boundary, "appVersion", Application.version);
                WriteField(body, boundary, "branch", branch ?? "");
                WriteField(body, boundary, "commit", changeset ?? "");
                WriteField(body, boundary, "notes",
                    $"Auto-uploaded from Unity build at {DateTime.UtcNow:yyyy-MM-dd HH:mm}");
                WriteFileHeader(body, boundary, "file", fileName, "application/octet-stream");
                using (var fs = new FileStream(
                    filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    StagingCopyBufferBytes, FileOptions.SequentialScan))
                {
                    fs.CopyTo(body, StagingCopyBufferBytes);
                }
                WriteAscii(body, $"\r\n--{boundary}--\r\n");
                return bodyPath;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Bugpunch] Failed to stage artifact upload body: {ex.Message}");
                TryDelete(bodyPath);
                return null;
            }
        }

        // ── Background artifact upload ────────────────────────────────────
        // One-upload-at-a-time state; pumped by EditorApplication.update.
        // The progress bar is cancelable and a stall watchdog aborts the
        // request if throughput drops to zero for StallTimeoutSeconds, so
        // a dead server can never wedge the editor indefinitely.
        static UnityWebRequest s_activeReq;
        static string s_activeBodyPath;
        static string s_activeFileName;
        static long s_activeTotalBytes;
        static ulong s_lastTickBytes;
        static double s_lastTickTime;
        static double s_uploadStartTime;
        static ulong s_lastProgressBytes;
        static double s_lastProgressTime;

        static void StartArtifactUpload(
            ODDGames.Bugpunch.DeviceConnect.BugpunchConfig config,
            string filePath, string fileName, string platform, string changeset, string branch)
        {
            if (s_activeReq != null)
            {
                Debug.LogWarning("[Bugpunch] Another artifact upload is already in progress — skipping.");
                return;
            }

            if (string.IsNullOrWhiteSpace(config.serverUrl))
            {
                Debug.LogWarning("[Bugpunch] serverUrl is empty — skipping artifact upload.");
                return;
            }

            var bodyPath = BuildMultipartBody(
                out var boundary, filePath, fileName, platform, changeset, branch);
            if (bodyPath == null) return;

            var req = new UnityWebRequest(ResolveUploadUrl(config), "POST");
            req.uploadHandler = new UploadHandlerFile(bodyPath);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", $"multipart/form-data; boundary={boundary}");
            req.SetRequestHeader("X-Api-Key", config.apiKey);
            req.SendWebRequest();

            s_activeReq = req;
            s_activeBodyPath = bodyPath;
            s_activeFileName = fileName;
            s_activeTotalBytes = new FileInfo(bodyPath).Length;
            s_uploadStartTime = EditorApplication.timeSinceStartup;
            s_lastTickTime = s_uploadStartTime;
            s_lastTickBytes = 0;
            s_lastProgressBytes = 0;
            s_lastProgressTime = s_uploadStartTime;

            EditorApplication.update += PumpArtifactUpload;
            // Initial progress bar — shows 0% with a cancel button so the
            // user is never locked out while the request is still connecting.
            EditorUtility.DisplayCancelableProgressBar(
                "Bugpunch: uploading build",
                $"{fileName} — 0.0 / {s_activeTotalBytes / 1048576.0:F1} MB — connecting…",
                0f);
        }

        static void PumpArtifactUpload()
        {
            var req = s_activeReq;
            if (req == null)
            {
                EditorApplication.update -= PumpArtifactUpload;
                return;
            }

            var now = EditorApplication.timeSinceStartup;

            if (req.isDone)
            {
                EditorApplication.update -= PumpArtifactUpload;
                EditorUtility.ClearProgressBar();

                var elapsed = now - s_uploadStartTime;
                var totalMb = s_activeTotalBytes / 1048576.0;
                var avgMbs = elapsed > 0.01 ? totalMb / elapsed : 0;

                if (req.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[Bugpunch] Build artifact uploaded: {s_activeFileName} " +
                              $"({totalMb:F1}MB in {elapsed:F1}s, avg {avgMbs:F2} MB/s)");
                }
                else
                {
                    Debug.LogError($"[Bugpunch] Artifact upload failed: {req.error} — {req.downloadHandler?.text}");
                }

                req.Dispose();
                TryDelete(s_activeBodyPath);
                s_activeReq = null;
                s_activeBodyPath = null;
                s_activeFileName = null;
                return;
            }

            var uploaded = req.uploadedBytes;

            // Stall watchdog: if no bytes moved for StallTimeoutSeconds, abort.
            if (uploaded > s_lastProgressBytes)
            {
                s_lastProgressBytes = uploaded;
                s_lastProgressTime = now;
            }
            else if (now - s_lastProgressTime > StallTimeoutSeconds)
            {
                Debug.LogError($"[Bugpunch] Artifact upload stalled for {StallTimeoutSeconds:F0}s at " +
                    $"{uploaded / 1048576.0:F1}/{s_activeTotalBytes / 1048576.0:F1} MB — aborting.");
                AbortActiveUpload();
                return;
            }

            // Throttle progress bar refresh to ~1 Hz so speed readings are
            // meaningful (instant bps fluctuates wildly).
            var dt = now - s_lastTickTime;
            if (dt < 1.0) return;

            var deltaBytes = uploaded >= s_lastTickBytes ? uploaded - s_lastTickBytes : 0;
            var bps = dt > 0 ? deltaBytes / dt : 0;
            var mbs = bps / 1048576.0;
            var uploadedMb = uploaded / 1048576.0;
            var totalMb2 = s_activeTotalBytes / 1048576.0;
            var pct = s_activeTotalBytes > 0 ? (float)((double)uploaded / s_activeTotalBytes) : 0f;
            var elapsedTotal = now - s_uploadStartTime;
            var msg = uploaded > 0
                ? $"{s_activeFileName} — {uploadedMb:F1} / {totalMb2:F1} MB @ {mbs:F2} MB/s"
                : $"{s_activeFileName} — connecting… ({elapsedTotal:F0}s)";

            if (EditorUtility.DisplayCancelableProgressBar("Bugpunch: uploading build", msg, pct))
            {
                Debug.LogWarning($"[Bugpunch] Artifact upload cancelled by user: {s_activeFileName}");
                AbortActiveUpload();
                return;
            }

            s_lastTickBytes = uploaded;
            s_lastTickTime = now;
        }

        static void AbortActiveUpload()
        {
            var req = s_activeReq;
            if (req == null) return;
            EditorApplication.update -= PumpArtifactUpload;
            EditorUtility.ClearProgressBar();
            try { req.Abort(); } catch { }
            try { req.Dispose(); } catch { }
            TryDelete(s_activeBodyPath);
            s_activeReq = null;
            s_activeBodyPath = null;
            s_activeFileName = null;
        }

        static void WriteField(Stream s, string boundary, string name, string value)
        {
            WriteAscii(s, $"--{boundary}\r\n");
            WriteAscii(s, $"Content-Disposition: form-data; name=\"{name}\"\r\n\r\n");
            WriteAscii(s, value ?? "");
            WriteAscii(s, "\r\n");
        }

        static void WriteFileHeader(Stream s, string boundary, string name, string filename, string contentType)
        {
            WriteAscii(s, $"--{boundary}\r\n");
            WriteAscii(s, $"Content-Disposition: form-data; name=\"{name}\"; filename=\"{filename}\"\r\n");
            WriteAscii(s, $"Content-Type: {contentType}\r\n\r\n");
        }

        static void WriteAscii(Stream s, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            s.Write(bytes, 0, bytes.Length);
        }

        static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch { /* best-effort cleanup */ }
        }

        static string GetPlatformString(BuildTarget target)
        {
            return target switch
            {
                BuildTarget.Android => "android",
                BuildTarget.iOS => "ios",
                BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => "windows",
                BuildTarget.StandaloneOSX => "macos",
                BuildTarget.StandaloneLinux64 => "linux",
                BuildTarget.WebGL => "webgl",
                _ => target.ToString().ToLowerInvariant()
            };
        }

        /// <summary>
        /// Try to get current Plastic SCM changeset number.
        /// Falls back to null if cm is not available.
        /// </summary>
        static string GetPlasticChangeset()
        {
            try
            {
                var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "cm";
                process.StartInfo.Arguments = "wi --machinereadable";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.WorkingDirectory = Application.dataPath;
                process.Start();

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);

                // Format: changeset#branch#repository
                var parts = output.Trim().Split('#');
                if (parts.Length >= 1) return parts[0];
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Try to get current Plastic SCM branch.
        /// </summary>
        static string GetPlasticBranch()
        {
            try
            {
                var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "cm";
                process.StartInfo.Arguments = "wi --machinereadable";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.WorkingDirectory = Application.dataPath;
                process.Start();

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);

                var parts = output.Trim().Split('#');
                if (parts.Length >= 2) return parts[1];
            }
            catch { }
            return null;
        }
    }
}
