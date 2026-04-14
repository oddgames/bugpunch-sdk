using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
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
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            // Subscribe to runtime client connection events so the TypeDB refreshes
            // automatically when the editor connects (play mode).
            ODDGames.Bugpunch.DeviceConnect.BugpunchClient.OnAnyConnected += OnClientConnected;
        }

        /// <summary>
        /// Called after every script compilation in the editor.
        /// Uploads the TypeDB if the content has changed since the last upload.
        /// Deferred via delayCall so it doesn't block the compilation pipeline.
        /// </summary>
        static void OnCompilationFinished(object context)
        {
            var config = ODDGames.Bugpunch.DeviceConnect.BugpunchConfig.Load();
            if (config == null || string.IsNullOrEmpty(config.apiKey)) return;
            EditorApplication.delayCall += TryUploadTypeDatabaseQuiet;
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

    /// <summary>
    /// Pre-build hook: uploads TypeDB before the build starts.
    /// This ensures the type database matches the code being built.
    /// </summary>
    public class BugpunchPreBuildHook : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var config = ODDGames.Bugpunch.DeviceConnect.BugpunchConfig.Load();
            if (config == null || string.IsNullOrEmpty(config.apiKey)) return;

            Debug.Log("[Bugpunch] Pre-build: uploading type database...");
            BuildHooks.UploadTypeDatabase();
        }
    }

    /// <summary>
    /// Post-build hook: uploads the build artifact to the server.
    /// Tags it with the current Plastic SCM changeset/branch if available.
    /// </summary>
    public class BugpunchPostBuildHook : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            var config = ODDGames.Bugpunch.DeviceConnect.BugpunchConfig.Load();
            if (config == null || string.IsNullOrEmpty(config.apiKey)) return;

            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.Log("[Bugpunch] Build failed — skipping artifact upload");
                return;
            }

            var outputPath = report.summary.outputPath;
            if (!File.Exists(outputPath))
            {
                Debug.LogWarning($"[Bugpunch] Build output not found: {outputPath}");
                return;
            }

            Debug.Log($"[Bugpunch] Post-build: uploading artifact {Path.GetFileName(outputPath)}...");

            try
            {
                var platform = GetPlatformString(report.summary.platform);
                var changeset = GetPlasticChangeset();
                var branch = GetPlasticBranch();

                UploadBuildArtifact(config, outputPath, platform, changeset, branch);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Bugpunch] Artifact upload failed: {ex.Message}");
            }
        }

        static void UploadBuildArtifact(
            ODDGames.Bugpunch.DeviceConnect.BugpunchConfig config,
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

            var op = request.SendWebRequest();
            while (!op.isDone) { }

            if (request.result == UnityWebRequest.Result.Success)
            {
                var fileSize = new FileInfo(filePath).Length;
                Debug.Log($"[Bugpunch] Build artifact uploaded: {Path.GetFileName(filePath)} ({fileSize / 1024 / 1024}MB) [{platform}]");
            }
            else
            {
                Debug.LogError($"[Bugpunch] Artifact upload failed: {request.error} — {request.downloadHandler?.text}");
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
