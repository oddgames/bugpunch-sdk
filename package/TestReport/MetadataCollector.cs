using System;
using System.Diagnostics;
using System.IO;

using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// Builds session metadata: app/platform defaults, plus optional Git/Plastic
    /// VCS detection in the editor. Pure helpers — caller passes in mutable state.
    /// </summary>
    internal static class MetadataCollector
    {
        /// <summary>
        /// Builds session metadata with platform defaults and optional VCS detection.
        /// MetadataCallback (if set) is invoked last so user code can override fields.
        /// </summary>
        internal static SessionMetadata Build(
            string runId,
            bool autoDetectVCS,
            Func<SessionMetadata, SessionMetadata> metadataCallback)
        {
            var meta = new SessionMetadata
            {
                runId = runId,
                appVersion = Application.version,
                platform = Application.platform.ToString(),
                machineName = Environment.MachineName,
                project = Application.productName
            };

#if UNITY_EDITOR
            if (autoDetectVCS)
            {
                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                if (Directory.Exists(Path.Combine(projectRoot, ".git")))
                    DetectGit(meta, projectRoot);
                else if (Directory.Exists(Path.Combine(projectRoot, ".plastic")))
                    DetectPlastic(meta, projectRoot);
            }
#endif

            if (metadataCallback != null)
            {
                try { meta = metadataCallback(meta) ?? meta; }
                catch (Exception ex) { Debug.LogWarning($"[TestReport] MetadataCallback error: {ex.Message}"); }
            }

            return meta;
        }

#if UNITY_EDITOR
        static void DetectGit(SessionMetadata meta, string workingDir)
        {
            try
            {
                meta.branch = RunProcess("git", "rev-parse --abbrev-ref HEAD", workingDir)?.Trim();
                meta.commit = RunProcess("git", "rev-parse --short HEAD", workingDir)?.Trim();
            }
            catch { /* Git not available — leave fields null */ }
        }

        static void DetectPlastic(SessionMetadata meta, string workingDir)
        {
            try
            {
                // Try reading workspace selector file for branch info
                var selectorPath = Path.Combine(workingDir, ".plastic", "plastic.selector");
                if (File.Exists(selectorPath))
                {
                    var lines = File.ReadAllLines(selectorPath);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("rep \"") || trimmed.StartsWith("repository \""))
                        {
                            // Extract repo name
                            var start = trimmed.IndexOf('"') + 1;
                            var end = trimmed.IndexOf('"', start);
                            if (end > start)
                                meta.project = trimmed.Substring(start, end - start);
                        }
                        else if (trimmed.StartsWith("path \"/") || trimmed.StartsWith("smartbranch \"") || trimmed.StartsWith("branch \""))
                        {
                            var start = trimmed.IndexOf('"') + 1;
                            var end = trimmed.IndexOf('"', start);
                            if (end > start)
                                meta.branch = trimmed.Substring(start, end - start);
                        }
                    }
                }

                // Try cm for changeset
                var csInfo = RunProcess("cm", "status --head --machinereadable", workingDir);
                if (!string.IsNullOrEmpty(csInfo))
                    meta.commit = csInfo.Trim().Split('\n')[0].Trim();
            }
            catch { /* Plastic not available — leave fields as-is */ }
        }

        static string RunProcess(string fileName, string arguments, string workingDir)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return null;

                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);
                return proc.ExitCode == 0 ? output : null;
            }
            catch
            {
                return null;
            }
        }
#endif
    }
}
