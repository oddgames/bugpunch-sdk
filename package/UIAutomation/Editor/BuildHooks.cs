using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.Networking;

namespace ODDGames.UIAutomation.Editor
{
    /// <summary>
    /// Automatic hooks that fire during Unity's build pipeline:
    /// 1. After compilation → export + upload TypeDB
    /// 2. After build → upload build artifact
    /// </summary>
    [InitializeOnLoad]
    public static class BuildHooks
    {
        static bool _typeDbUploaded;

        static BuildHooks()
        {
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        /// <summary>
        /// Called after every script compilation in the editor.
        /// Exports and uploads the type database automatically.
        /// </summary>
        static void OnCompilationFinished(object context)
        {
            var config = ODDGames.UIAutomation.DeviceConnect.OddDevConfig.Load();
            if (config == null || string.IsNullOrEmpty(config.apiKey)) return;

            // Don't upload on every recompile — only when explicitly building
            // or when the user has opted in via config
            // For now, just mark that types are stale
            _typeDbUploaded = false;
        }

        /// <summary>
        /// Force upload the type database. Called before builds or on-demand.
        /// </summary>
        public static void UploadTypeDatabase()
        {
            if (_typeDbUploaded) return;

            try
            {
                TypeDatabaseExporter.ExportAndUpload();
                _typeDbUploaded = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OddDev] TypeDB upload failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Pre-build hook: uploads TypeDB before the build starts.
    /// This ensures the type database matches the code being built.
    /// </summary>
    public class OddDevPreBuildHook : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var config = ODDGames.UIAutomation.DeviceConnect.OddDevConfig.Load();
            if (config == null || string.IsNullOrEmpty(config.apiKey)) return;

            Debug.Log("[OddDev] Pre-build: uploading type database...");
            BuildHooks.UploadTypeDatabase();
        }
    }

    /// <summary>
    /// Post-build hook: uploads the build artifact to the server.
    /// Tags it with the current Plastic SCM changeset/branch if available.
    /// </summary>
    public class OddDevPostBuildHook : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            var config = ODDGames.UIAutomation.DeviceConnect.OddDevConfig.Load();
            if (config == null || string.IsNullOrEmpty(config.apiKey)) return;

            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.Log("[OddDev] Build failed — skipping artifact upload");
                return;
            }

            var outputPath = report.summary.outputPath;
            if (!File.Exists(outputPath))
            {
                Debug.LogWarning($"[OddDev] Build output not found: {outputPath}");
                return;
            }

            Debug.Log($"[OddDev] Post-build: uploading artifact {Path.GetFileName(outputPath)}...");

            try
            {
                var platform = GetPlatformString(report.summary.platform);
                var changeset = GetPlasticChangeset();
                var branch = GetPlasticBranch();

                UploadBuildArtifact(config, outputPath, platform, changeset, branch);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OddDev] Artifact upload failed: {ex.Message}");
            }
        }

        static void UploadBuildArtifact(
            ODDGames.UIAutomation.DeviceConnect.OddDevConfig config,
            string filePath, string platform, string changeset, string branch)
        {
            var url = config.serverUrl.TrimEnd('/');
            if (url.StartsWith("ws://")) url = "http://" + url.Substring(5);
            else if (url.StartsWith("wss://")) url = "https://" + url.Substring(6);

            var uploadUrl = $"{url}/api/builds/upload";

            var form = new WWWForm();
            form.AddBinaryData("file", File.ReadAllBytes(filePath), Path.GetFileName(filePath));
            form.AddField("platform", platform);
            form.AddField("appVersion", Application.version);
            form.AddField("branch", branch ?? "");
            form.AddField("commit", changeset ?? "");
            form.AddField("notes", $"Auto-uploaded from Unity build at {DateTime.Now:yyyy-MM-dd HH:mm}");

            var request = UnityWebRequest.Post(uploadUrl, form);
            request.SetRequestHeader("X-Api-Key", config.apiKey);
            if (!string.IsNullOrEmpty(config.projectId))
                request.SetRequestHeader("X-Project-Id", config.projectId);

            var op = request.SendWebRequest();
            while (!op.isDone) { }

            if (request.result == UnityWebRequest.Result.Success)
            {
                var fileSize = new FileInfo(filePath).Length;
                Debug.Log($"[OddDev] Build artifact uploaded: {Path.GetFileName(filePath)} ({fileSize / 1024 / 1024}MB) [{platform}]");
            }
            else
            {
                Debug.LogError($"[OddDev] Artifact upload failed: {request.error} — {request.downloadHandler?.text}");
            }

            request.Dispose();
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
