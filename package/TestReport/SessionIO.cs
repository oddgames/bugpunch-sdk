using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

using Newtonsoft.Json;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// File / zip / JSON I/O helpers for TestReport.
    /// Pure functions — operate on paths and data passed in by the facade.
    /// </summary>
    internal static class SessionIO
    {
        internal static readonly JsonSerializerSettings HierarchyJsonSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None
        };

        /// <summary>
        /// Serializes pending hierarchy snapshots to JSON strings on the main thread
        /// (Newtonsoft is thread-safe, but caller wants to clear the dict atomically).
        /// </summary>
        internal static Dictionary<string, string> SerializeHierarchies(
            Dictionary<string, HierarchySnapshot> pendingHierarchies)
        {
            var hierarchyData = new Dictionary<string, string>();
            foreach (var kvp in pendingHierarchies)
            {
                try { hierarchyData[kvp.Key] = JsonConvert.SerializeObject(kvp.Value, HierarchyJsonSettings); }
                catch (Exception ex) { Debug.LogWarning($"[TestReport] Hierarchy serialization failed for {kvp.Key}: {ex.Message}"); }
            }
            return hierarchyData;
        }

        /// <summary>
        /// Writes serialized hierarchy JSON files into the session folder.
        /// </summary>
        internal static void WriteHierarchyFiles(string sessionFolder, Dictionary<string, string> hierarchyData)
        {
            foreach (var kvp in hierarchyData)
            {
                try { File.WriteAllText(Path.Combine(sessionFolder, kvp.Key), kvp.Value); }
                catch { }
            }
        }

        /// <summary>
        /// Writes session.json into the session folder.
        /// </summary>
        internal static void WriteSessionJsonFile(string sessionFolder, string sessionJson)
        {
            if (sessionJson == null) return;
            try { File.WriteAllText(Path.Combine(sessionFolder, "session.json"), sessionJson); }
            catch { }
        }

        /// <summary>
        /// Writes log.txt into the session folder.
        /// </summary>
        internal static void WriteLogFile(string sessionFolder, string logText)
        {
            try { File.WriteAllText(Path.Combine(sessionFolder, "log.txt"), logText); }
            catch { }
        }

        /// <summary>
        /// Captures Application.persistentDataPath into the session folder
        /// when the test failed and capture is enabled.
        /// </summary>
        internal static void CapturePersistentDataIfFailed(string sessionFolder, bool failed, bool capturePersistentData)
        {
            if (!failed || !capturePersistentData) return;

            try
            {
                var persistentPath = Application.persistentDataPath;
                if (Directory.Exists(persistentPath))
                {
                    var destDir = Path.Combine(sessionFolder, "persistent_data");
                    CopyDirectory(persistentPath, destDir, maxDepth: 3, maxTotalBytes: 50 * 1024 * 1024);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestReport] Failed to capture persistentDataPath: {ex.Message}");
            }
        }

        /// <summary>
        /// Packages the session folder into a zip and deletes the loose folder.
        /// Returns the zip path on success, or null on failure (caller falls back to folder).
        /// </summary>
        internal static string PackToZip(string sessionFolder)
        {
            var zipPath = sessionFolder + ".zip";
            try
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
                ZipFile.CreateFromDirectory(sessionFolder, zipPath, System.IO.Compression.CompressionLevel.NoCompression, false);
                Directory.Delete(sessionFolder, true);
                return zipPath;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Recursively copies a directory with depth and size limits to prevent bloated zips.
        /// Skips files that would exceed maxTotalBytes.
        /// </summary>
        internal static void CopyDirectory(string sourceDir, string destDir, int maxDepth, long maxTotalBytes)
        {
            long totalBytes = 0;
            CopyDirectoryRecursive(sourceDir, destDir, 0, maxDepth, ref totalBytes, maxTotalBytes);
        }

        static void CopyDirectoryRecursive(string sourceDir, string destDir, int depth, int maxDepth, ref long totalBytes, long maxTotalBytes)
        {
            if (depth > maxDepth || totalBytes >= maxTotalBytes) return;

            Directory.CreateDirectory(destDir);

            try
            {
                foreach (var file in Directory.GetFiles(sourceDir))
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (totalBytes + fileInfo.Length > maxTotalBytes) continue;
                        var destFile = Path.Combine(destDir, fileInfo.Name);
                        File.Copy(file, destFile, true);
                        totalBytes += fileInfo.Length;
                    }
                    catch { } // Skip locked/inaccessible files
                }

                foreach (var subDir in Directory.GetDirectories(sourceDir))
                {
                    var dirName = Path.GetFileName(subDir);
                    CopyDirectoryRecursive(subDir, Path.Combine(destDir, dirName), depth + 1, maxDepth, ref totalBytes, maxTotalBytes);
                }
            }
            catch { } // Skip inaccessible directories
        }

        /// <summary>
        /// Writes session.json directly during failure reporting (synchronous, mid-test).
        /// </summary>
        internal static void WriteSessionJsonDirect(string sessionFolder, DiagSession session)
        {
            if (sessionFolder == null || session == null) return;
            try
            {
                var path = Path.Combine(sessionFolder, "session.json");
                var json = JsonUtility.ToJson(session, true);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestReport] Failed to write session.json: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes log.txt directly during failure reporting (synchronous, mid-test).
        /// </summary>
        internal static void WriteLogFileDirect(string sessionFolder, List<string> log)
        {
            if (sessionFolder == null) return;
            try
            {
                var path = Path.Combine(sessionFolder, "log.txt");
                File.WriteAllText(path, string.Join("\n", log));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestReport] Failed to write log: {ex.Message}");
            }
        }
    }
}
