using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Networking;

namespace ODDGames.Bugpunch.Editor
{
    /// <summary>
    /// Post-build hook: for Android R8/ProGuard-minified builds, locates the
    /// emitted <c>mapping.txt</c> (Unity → Player Settings → Publishing →
    /// Minify) and uploads it to the server keyed by
    /// <c>(bundleId, version, buildCode)</c>. The server retraces obfuscated
    /// frames in ANR thread dumps, exception stacks, and the main-thread
    /// sample ring back to their original names at ingest time.
    ///
    /// Piggybacks on <c>symbolUploadEnabled</c> — mapping.txt is the Java
    /// companion to the native symbol uploads; same bandwidth tier, same
    /// "when the build matters" usage. Bundling the flag keeps config simple.
    ///
    /// If R8/ProGuard minification isn't enabled, no mapping.txt is produced
    /// and this hook silently no-ops — which is the right behaviour for the
    /// common case of unminified Unity builds.
    /// </summary>
    public class BugpunchMappingUploader : IPostprocessBuildWithReport
    {
        // Runs after BugpunchSymbolUploader (callbackOrder = 10) so the
        // heavier native symbol uploads finish first.
        public int callbackOrder => 20;

        const string PROGRESS_TITLE = "Bugpunch: uploading mapping.txt";
        const long MaxMappingBytes = 50L * 1024 * 1024; // server rejects >50 MB

        public void OnPostprocessBuild(BuildReport report)
        {
            // Unity's BuildAndRun leaves result==Unknown in post-build; only
            // bail on explicit failure.
            if (report.summary.result == BuildResult.Failed ||
                report.summary.result == BuildResult.Cancelled) return;
            if (report.summary.platform != BuildTarget.Android) return;

            var config = ODDGames.Bugpunch.DeviceConnect.BugpunchConfig.Load();
            if (config == null || string.IsNullOrEmpty(config.apiKey)) return;
            if (!config.symbolUploadEnabled) return; // same gate as symbol upload
            if (string.IsNullOrWhiteSpace(config.serverUrl)) return;

            // Capture main-thread-only state up front — background work below
            // only uses the captured locals.
            var outputPath = report.summary.outputPath;
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var bundleId = Application.identifier;
            var version = Application.version;
            var buildCode = PlayerSettings.Android.bundleVersionCode.ToString();
            var gitSha = GetPlasticChangeset();

            // Batch mode: post-build blocks until we return, so run sync.
            if (Application.isBatchMode)
            {
                try { FindAndUpload(config, outputPath, projectRoot, bundleId, version, buildCode, gitSha, interactive: false); }
                catch (Exception ex) { BugpunchLog.Error("MappingUploader", $"Mapping upload failed: {ex.Message}"); }
                return;
            }

            // Interactive: return immediately so Unity's build dialog unfreezes.
            EditorApplication.delayCall += () =>
            {
                try { FindAndUpload(config, outputPath, projectRoot, bundleId, version, buildCode, gitSha, interactive: true); }
                catch (Exception ex) { BugpunchLog.Error("MappingUploader", $"Mapping upload failed: {ex.Message}"); }
                finally { EditorUtility.ClearProgressBar(); }
            };
        }

        static void FindAndUpload(
            ODDGames.Bugpunch.DeviceConnect.BugpunchConfig config,
            string outputPath, string projectRoot,
            string bundleId, string version, string buildCode, string gitSha,
            bool interactive)
        {
            string mappingPath = null;
            bool mappingFromZip = false;
            string tempExtractedPath = null;

            try
            {
                // Pass 1: symbols.zip next to the build output. R8 writes
                // mapping.txt into the same zip in some Unity versions; check
                // before falling back to the Gradle output folder.
                if (!string.IsNullOrEmpty(outputPath))
                {
                    var searchDir = File.Exists(outputPath)
                        ? Path.GetDirectoryName(outputPath)
                        : Directory.Exists(outputPath) ? outputPath : null;
                    if (searchDir != null && Directory.Exists(searchDir))
                    {
                        foreach (var zip in Directory.GetFiles(searchDir, "*symbols*.zip", SearchOption.TopDirectoryOnly))
                        {
                            tempExtractedPath = TryExtractMappingFromZip(zip);
                            if (tempExtractedPath != null) { mappingPath = tempExtractedPath; mappingFromZip = true; break; }
                        }
                    }
                }

                // Pass 2: Library/Bee/.../launcher/build/outputs/mapping/release/mapping.txt
                if (mappingPath == null && !string.IsNullOrEmpty(projectRoot))
                {
                    mappingPath = FindMappingInGradleOutput(projectRoot);
                }

                if (mappingPath == null || !File.Exists(mappingPath))
                {
                    // Silent no-op: minification probably isn't enabled, which
                    // is the common case for Unity builds. Don't nag.
                    return;
                }

                var size = new FileInfo(mappingPath).Length;
                if (size <= 0)
                {
                    BugpunchLog.Warn("MappingUploader", $"mapping.txt at {mappingPath} is empty — skipping.");
                    return;
                }
                if (size > MaxMappingBytes)
                {
                    BugpunchLog.Warn("MappingUploader", $"mapping.txt is {size / 1048576.0:F1} MB — exceeds server limit; skipping.");
                    return;
                }

                if (interactive)
                {
                    EditorUtility.DisplayCancelableProgressBar(
                        PROGRESS_TITLE,
                        $"Uploading mapping.txt ({size / 1024.0:F0} KB) — v{version} ({buildCode})",
                        0f);
                }

                UploadOne(config, mappingPath, bundleId, version, buildCode, gitSha);
            }
            finally
            {
                if (tempExtractedPath != null)
                {
                    try { File.Delete(tempExtractedPath); } catch { }
                }
                _ = mappingFromZip; // suppress unused-var
            }
        }

        static string TryExtractMappingFromZip(string zipPath)
        {
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.EndsWith("mapping.txt", StringComparison.OrdinalIgnoreCase)) continue;
                    var temp = Path.Combine(Path.GetTempPath(), $"bp_mapping_{Guid.NewGuid():N}.txt");
                    entry.ExtractToFile(temp, overwrite: true);
                    return temp;
                }
            }
            catch { }
            return null;
        }

        static string FindMappingInGradleOutput(string projectRoot)
        {
            // Preferred canonical path.
            var canonical = Path.Combine(projectRoot, "Library", "Bee", "Android", "Prj",
                "IL2CPP", "Gradle", "launcher", "build", "outputs", "mapping", "release", "mapping.txt");
            if (File.Exists(canonical)) return canonical;

            // Fallback: Unity occasionally reshuffles these paths between
            // editor versions. Recursive search — but scoped to the Bee
            // Android output so we don't walk the whole project.
            var beeRoot = Path.Combine(projectRoot, "Library", "Bee", "Android");
            if (!Directory.Exists(beeRoot)) return null;
            try
            {
                foreach (var p in Directory.EnumerateFiles(beeRoot, "mapping.txt", SearchOption.AllDirectories))
                {
                    var norm = p.Replace('\\', '/');
                    // Filter to R8/ProGuard output — Unity puts unrelated
                    // "mapping.txt" files elsewhere (IL2CPP method map, etc.).
                    if (norm.Contains("/outputs/mapping/")) return p;
                }
            }
            catch { }
            return null;
        }

        static void UploadOne(
            ODDGames.Bugpunch.DeviceConnect.BugpunchConfig config,
            string mappingPath, string bundleId, string version, string buildCode, string gitSha)
        {
            var baseUrl = NormalizeBaseUrl(
                string.IsNullOrWhiteSpace(config.uploadServerUrl) ? config.serverUrl : config.uploadServerUrl);

            var bodyPath = StageMultipart(out var boundary,
                mappingPath, bundleId, version, buildCode, gitSha);
            if (bodyPath == null) return;

            UnityWebRequest req = null;
            try
            {
                req = new UnityWebRequest($"{baseUrl}/api/v1/mappings", "POST");
                req.uploadHandler = new UploadHandlerFile(bodyPath);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = 90;
                req.SetRequestHeader("Content-Type", $"multipart/form-data; boundary={boundary}");
                req.SetRequestHeader("X-Api-Key", config.apiKey);
                req.SendWebRequest();

                while (!req.isDone) Thread.Sleep(50);

                if (req.result == UnityWebRequest.Result.Success)
                {
                    BugpunchLog.Info("MappingUploader", $"mapping.txt uploaded for {bundleId} v{version} ({buildCode}).");
                }
                else
                {
                    BugpunchLog.Warn("MappingUploader", $"mapping.txt upload failed: {req.error} — {req.downloadHandler?.text}");
                }
            }
            catch (Exception ex)
            {
                BugpunchLog.Warn("MappingUploader", $"mapping.txt upload threw: {ex.Message}");
            }
            finally
            {
                try { req?.Dispose(); } catch { }
                try { if (File.Exists(bodyPath)) File.Delete(bodyPath); } catch { }
            }
        }

        const int StagingBuffer = 256 * 1024;

        static string StageMultipart(
            out string boundary,
            string mappingPath, string bundleId, string version, string buildCode, string gitSha)
        {
            boundary = "----BugpunchBoundary" + Guid.NewGuid().ToString("N");
            var bodyPath = Path.Combine(Path.GetTempPath(), $"bp_mapping_body_{Guid.NewGuid():N}.bin");
            try
            {
                using var body = new FileStream(
                    bodyPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    StagingBuffer, FileOptions.SequentialScan);
                WriteField(body, boundary, "bundleId", bundleId ?? "");
                WriteField(body, boundary, "version", version ?? "");
                WriteField(body, boundary, "buildCode", buildCode ?? "");
                if (!string.IsNullOrEmpty(gitSha)) WriteField(body, boundary, "gitSha", gitSha);
                WriteFileHeader(body, boundary, "mapping", "mapping.txt", "text/plain");
                using (var fs = new FileStream(
                    mappingPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    StagingBuffer, FileOptions.SequentialScan))
                {
                    fs.CopyTo(body, StagingBuffer);
                }
                WriteAscii(body, $"\r\n--{boundary}--\r\n");
                return bodyPath;
            }
            catch (Exception ex)
            {
                BugpunchLog.Warn("MappingUploader", $"Failed to stage mapping upload body: {ex.Message}");
                try { if (File.Exists(bodyPath)) File.Delete(bodyPath); } catch { }
                return null;
            }
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

        static string NormalizeBaseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            url = url.TrimEnd('/');
            if (url.StartsWith("ws://")) url = "http://" + url.Substring(5);
            else if (url.StartsWith("wss://")) url = "https://" + url.Substring(6);
            return url;
        }

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
                var parts = output.Trim().Split('#');
                if (parts.Length >= 1) return parts[0];
            }
            catch { }
            return null;
        }
    }
}
