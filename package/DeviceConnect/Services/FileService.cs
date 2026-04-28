using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ODDGames.Bugpunch.DeviceConnect.Database;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Provides file system access for remote browsing via the Bugpunch tunnel.
    /// Only allows access to paths under known Unity Application directories.
    /// </summary>
    public class FileService
    {
        readonly List<(string name, string path)> _roots;
        readonly Dictionary<string, ZipJob> _zipJobs = new Dictionary<string, ZipJob>();
        readonly object _zipJobsLock = new object();
        readonly string _tempCachePath;

        /// <summary>
        /// Optional. When set, snapshot zip jobs run every available database
        /// plugin against the source tree and embed the parsed JSON under
        /// <c>_databases/{relativePath}.json</c> so a viewer doesn't need a
        /// live device to drill into the snapshot's databases.
        /// </summary>
        public DatabasePluginRegistry DatabasePlugins;

        public FileService()
        {
            _roots = new List<(string, string)>();
            TryAddRoot("Persistent", Application.persistentDataPath);
            TryAddRoot("Cache", Application.temporaryCachePath);
            TryAddRoot("Data", Application.dataPath);
            TryAddRoot("StreamingAssets", Application.streamingAssetsPath);
            TryAddRoot("ConsoleLog", Application.consoleLogPath);
            _tempCachePath = Application.temporaryCachePath;
        }

        void TryAddRoot(string name, string path)
        {
            if (!string.IsNullOrEmpty(path))
                _roots.Add((name, NormalizePath(path)));
        }

        // ------------------------------------------------------------------
        // Public API — each returns a JSON string
        // ------------------------------------------------------------------

        /// <summary>
        /// List known storage paths.
        /// </summary>
        public string GetPaths()
        {
            var sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < _roots.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var (name, path) = _roots[i];
                sb.Append("{");
                sb.Append($"\"name\":\"{BugpunchJson.Esc(name)}\",");
                sb.Append($"\"path\":\"{BugpunchJson.Esc(path)}\",");
                sb.Append($"\"exists\":{(Directory.Exists(path) ? "true" : "false")}");
                sb.Append("}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// List directory contents.
        /// </summary>
        public string ListDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
                return Error("Path is required");

            path = NormalizePath(path);
            if (!IsAllowed(path))
                return Error("Access denied: path is outside allowed roots");

            if (!Directory.Exists(path))
                return Error("Directory not found: " + path);

            try
            {
                var sb = new StringBuilder();
                sb.Append("[");
                bool first = true;

                // Directories first
                foreach (var dir in Directory.GetDirectories(path))
                {
                    if (!first) sb.Append(",");
                    first = false;
                    var info = new DirectoryInfo(dir);
                    sb.Append("{");
                    sb.Append($"\"name\":\"{BugpunchJson.Esc(info.Name)}\",");
                    sb.Append("\"isDirectory\":true,");
                    sb.Append("\"size\":0,");
                    sb.Append($"\"modified\":\"{info.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture)}\"");
                    sb.Append("}");
                }

                // Files
                foreach (var file in Directory.GetFiles(path))
                {
                    if (!first) sb.Append(",");
                    first = false;
                    var info = new FileInfo(file);
                    sb.Append("{");
                    sb.Append($"\"name\":\"{BugpunchJson.Esc(info.Name)}\",");
                    sb.Append("\"isDirectory\":false,");
                    sb.Append(string.Format(CultureInfo.InvariantCulture, "\"size\":{0},", info.Length));
                    sb.Append($"\"modified\":\"{info.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture)}\"");
                    sb.Append("}");
                }

                sb.Append("]");
                return sb.ToString();
            }
            catch (UnauthorizedAccessException)
            {
                return Error("Access denied");
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        }

        /// <summary>
        /// Read a file as text (or base64 for binary).
        /// </summary>
        public string ReadFile(string path, int maxBytes = 1048576)
        {
            if (string.IsNullOrEmpty(path))
                return Error("Path is required");

            path = NormalizePath(path);
            if (!IsAllowed(path))
                return Error("Access denied: path is outside allowed roots");

            if (!File.Exists(path))
                return Error("File not found: " + path);

            try
            {
                var info = new FileInfo(path);
                var size = info.Length;

                if (size > maxBytes)
                {
                    // Read truncated (FileShare.ReadWrite to handle locked files)
                    var bytes = new byte[maxBytes];
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        fs.Read(bytes, 0, maxBytes);

                    if (IsBinary(bytes, Math.Min(maxBytes, 8192)))
                    {
                        var sb = new StringBuilder();
                        sb.Append("{\"ok\":true,");
                        sb.Append($"\"content\":\"{Convert.ToBase64String(bytes)}\",");
                        sb.Append(string.Format(CultureInfo.InvariantCulture, "\"size\":{0},", size));
                        sb.Append($"\"truncated\":true,");
                        sb.Append(string.Format(CultureInfo.InvariantCulture, "\"readBytes\":{0},", maxBytes));
                        sb.Append("\"encoding\":\"base64\"}");
                        return sb.ToString();
                    }
                    else
                    {
                        var text = Encoding.UTF8.GetString(bytes);
                        var sb = new StringBuilder();
                        sb.Append("{\"ok\":true,");
                        sb.Append($"\"content\":\"{BugpunchJson.Esc(text)}\",");
                        sb.Append(string.Format(CultureInfo.InvariantCulture, "\"size\":{0},", size));
                        sb.Append($"\"truncated\":true,");
                        sb.Append(string.Format(CultureInfo.InvariantCulture, "\"readBytes\":{0},", maxBytes));
                        sb.Append("\"encoding\":\"utf-8\"}");
                        return sb.ToString();
                    }
                }

                // Read full file (use FileShare.ReadWrite to handle locked files like Editor.log)
                byte[] allBytes;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    allBytes = new byte[fs.Length];
                    fs.Read(allBytes, 0, allBytes.Length);
                }
                if (IsBinary(allBytes, Math.Min(allBytes.Length, 8192)))
                {
                    var sb = new StringBuilder();
                    sb.Append("{\"ok\":true,");
                    sb.Append($"\"content\":\"{Convert.ToBase64String(allBytes)}\",");
                    sb.Append(string.Format(CultureInfo.InvariantCulture, "\"size\":{0},", size));
                    sb.Append("\"encoding\":\"base64\"}");
                    return sb.ToString();
                }
                else
                {
                    var text = Encoding.UTF8.GetString(allBytes);
                    var sb = new StringBuilder();
                    sb.Append("{\"ok\":true,");
                    sb.Append($"\"content\":\"{BugpunchJson.Esc(text)}\",");
                    sb.Append(string.Format(CultureInfo.InvariantCulture, "\"size\":{0},", size));
                    sb.Append("\"encoding\":\"utf-8\"}");
                    return sb.ToString();
                }
            }
            catch (UnauthorizedAccessException)
            {
                return Error("Access denied");
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        }

        /// <summary>
        /// Write/create a file.
        /// </summary>
        public string WriteFile(string path, string content, bool isBase64 = false)
        {
            if (string.IsNullOrEmpty(path))
                return Error("Path is required");

            path = NormalizePath(path);
            if (!IsAllowed(path))
                return Error("Access denied: path is outside allowed roots");

            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                byte[] bytes;
                if (isBase64)
                    bytes = Convert.FromBase64String(content ?? "");
                else
                    bytes = Encoding.UTF8.GetBytes(content ?? "");

                File.WriteAllBytes(path, bytes);

                var sb = new StringBuilder();
                sb.Append("{\"ok\":true,");
                sb.Append(string.Format(CultureInfo.InvariantCulture, "\"size\":{0}", bytes.Length));
                sb.Append("}");
                return sb.ToString();
            }
            catch (UnauthorizedAccessException)
            {
                return Error("Access denied");
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        }

        /// <summary>
        /// Delete a file or directory.
        /// </summary>
        public string DeletePath(string path, bool recursive = false)
        {
            if (string.IsNullOrEmpty(path))
                return Error("Path is required");

            path = NormalizePath(path);
            if (!IsAllowed(path))
                return Error("Access denied: path is outside allowed roots");

            // Prevent deleting a root itself
            foreach (var (_, root) in _roots)
            {
                if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
                    return Error("Cannot delete a root directory");
            }

            try
            {
                if (Directory.Exists(path))
                {
                    if (!recursive)
                        return Error("Path is a directory — set recursive=true to delete");
                    Directory.Delete(path, true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
                else
                {
                    return Error("Path not found");
                }

                return "{\"ok\":true}";
            }
            catch (UnauthorizedAccessException)
            {
                return Error("Access denied");
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        }

        /// <summary>
        /// Create a directory.
        /// </summary>
        public string CreateDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
                return Error("Path is required");

            path = NormalizePath(path);
            if (!IsAllowed(path))
                return Error("Access denied: path is outside allowed roots");

            try
            {
                Directory.CreateDirectory(path);
                return "{\"ok\":true}";
            }
            catch (UnauthorizedAccessException)
            {
                return Error("Access denied");
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        }

        /// <summary>
        /// Get info about a file or directory.
        /// </summary>
        public string GetFileInfo(string path)
        {
            if (string.IsNullOrEmpty(path))
                return Error("Path is required");

            path = NormalizePath(path);
            if (!IsAllowed(path))
                return Error("Access denied: path is outside allowed roots");

            try
            {
                if (Directory.Exists(path))
                {
                    var info = new DirectoryInfo(path);
                    var sb = new StringBuilder();
                    sb.Append("{\"ok\":true,");
                    sb.Append($"\"name\":\"{BugpunchJson.Esc(info.Name)}\",");
                    sb.Append($"\"path\":\"{BugpunchJson.Esc(info.FullName)}\",");
                    sb.Append("\"isDirectory\":true,");
                    sb.Append("\"size\":0,");
                    sb.Append($"\"created\":\"{info.CreationTimeUtc.ToString("o", CultureInfo.InvariantCulture)}\",");
                    sb.Append($"\"modified\":\"{info.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture)}\"");
                    sb.Append("}");
                    return sb.ToString();
                }

                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    var sb = new StringBuilder();
                    sb.Append("{\"ok\":true,");
                    sb.Append($"\"name\":\"{BugpunchJson.Esc(info.Name)}\",");
                    sb.Append($"\"path\":\"{BugpunchJson.Esc(info.FullName)}\",");
                    sb.Append("\"isDirectory\":false,");
                    sb.Append(string.Format(CultureInfo.InvariantCulture, "\"size\":{0},", info.Length));
                    sb.Append($"\"extension\":\"{BugpunchJson.Esc(info.Extension)}\",");
                    sb.Append($"\"created\":\"{info.CreationTimeUtc.ToString("o", CultureInfo.InvariantCulture)}\",");
                    sb.Append($"\"modified\":\"{info.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture)}\"");
                    sb.Append("}");
                    return sb.ToString();
                }

                return Error("Path not found");
            }
            catch (UnauthorizedAccessException)
            {
                return Error("Access denied");
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // Zip jobs — async, with progress polling.
        //
        // Flow: StartZipJob → poll GetZipProgress until stage == "done"
        //       → GetZipResult returns {ok, size, base64} (and deletes temp).
        //
        // Running on Task.Run so the tunnel thread returns immediately; the
        // work is pure File IO (no Unity API) so background threads are safe.
        // ------------------------------------------------------------------

        class ZipJob
        {
            public string JobId;
            public string SourcePath;
            public string TempZipPath;
            // Stage transitions are single-writer (the worker task) — volatile
            // is sufficient for readers. Counters use Interlocked so they must
            // be plain fields (not volatile / properties).
            public volatile string Stage;       // scanning | zipping | done | error
            public int TotalFiles;              // set once after scan
            public int ProcessedFiles;          // Interlocked during zipping
            public long BytesWritten;           // Interlocked during zipping
            public long TotalSize;              // final zip size
            public string Error;
            public DateTime StartedAtUtc;
            public DateTime CompletedAtUtc;
            public string[] ExcludeDirPrefixes; // e.g. "bugpunch_" — directories whose name starts with any of these are skipped
        }

        /// <summary>
        /// Kick off a zip job for a directory. Returns a jobId immediately;
        /// use <see cref="GetZipProgress"/> to poll and <see cref="GetZipResult"/>
        /// to fetch the base64 once the stage is "done".
        ///
        /// <paramref name="excludeDirPrefixes"/> is a comma-separated list of
        /// directory-name prefixes to skip (used by snapshots to exclude the
        /// SDK's own folders like "bugpunch_uploads", "bugpunch_crashes", …).
        /// </summary>
        public string StartZipJob(string path, string excludeDirPrefixes = null)
        {
            if (string.IsNullOrEmpty(path))
                return Error("Path is required");

            path = NormalizePath(path);
            if (!IsAllowed(path))
                return Error("Access denied: path is outside allowed roots");

            if (!Directory.Exists(path))
                return Error("Directory not found: " + path);

            string[] excludes = null;
            if (!string.IsNullOrEmpty(excludeDirPrefixes))
            {
                var parts = excludeDirPrefixes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                var list = new List<string>(parts.Length);
                foreach (var p in parts)
                {
                    var t = p.Trim();
                    if (t.Length > 0) list.Add(t);
                }
                if (list.Count > 0) excludes = list.ToArray();
            }

            var job = new ZipJob
            {
                JobId = Guid.NewGuid().ToString("N"),
                SourcePath = path,
                Stage = "scanning",
                StartedAtUtc = DateTime.UtcNow,
                ExcludeDirPrefixes = excludes,
            };
            job.TempZipPath = Path.Combine(_tempCachePath, $"bpzip_{job.JobId}.zip");

            lock (_zipJobsLock)
            {
                PruneOldJobs_Locked();
                _zipJobs[job.JobId] = job;
            }

            Task.Run(() => RunZipJob(job));

            return $"{{\"ok\":true,\"jobId\":\"{job.JobId}\"}}";
        }

        /// <summary>
        /// Poll the progress of a running/finished zip job.
        /// </summary>
        public string GetZipProgress(string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
                return Error("jobId is required");
            ZipJob job;
            lock (_zipJobsLock) _zipJobs.TryGetValue(jobId, out job);
            if (job == null) return Error("Unknown jobId");

            var sb = new StringBuilder();
            sb.Append("{\"ok\":true,");
            sb.Append($"\"jobId\":\"{BugpunchJson.Esc(job.JobId)}\",");
            sb.Append($"\"stage\":\"{BugpunchJson.Esc(job.Stage)}\",");
            sb.Append(string.Format(CultureInfo.InvariantCulture, "\"totalFiles\":{0},", job.TotalFiles));
            sb.Append(string.Format(CultureInfo.InvariantCulture, "\"processedFiles\":{0},", Interlocked.CompareExchange(ref job.ProcessedFiles, 0, 0)));
            sb.Append(string.Format(CultureInfo.InvariantCulture, "\"bytesWritten\":{0},", Interlocked.Read(ref job.BytesWritten)));
            sb.Append(string.Format(CultureInfo.InvariantCulture, "\"totalSize\":{0},", job.TotalSize));
            sb.Append($"\"done\":{(job.Stage == "done" ? "true" : "false")}");
            if (job.Error != null)
                sb.Append($",\"error\":\"{BugpunchJson.Esc(job.Error)}\"");
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// Fetch the base64-encoded zip for a completed job. Deletes the temp
        /// file and evicts the job entry on success.
        /// </summary>
        public string GetZipResult(string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
                return Error("jobId is required");
            ZipJob job;
            lock (_zipJobsLock) _zipJobs.TryGetValue(jobId, out job);
            if (job == null) return Error("Unknown jobId");

            if (job.Stage == "error")
                return Error(job.Error ?? "Zip failed");
            if (job.Stage != "done")
                return "{\"ok\":false,\"pending\":true}";

            try
            {
                var bytes = File.ReadAllBytes(job.TempZipPath);
                var base64 = Convert.ToBase64String(bytes);
                var sb = new StringBuilder(base64.Length + 64);
                sb.Append("{\"ok\":true,");
                sb.Append(string.Format(CultureInfo.InvariantCulture, "\"size\":{0},", bytes.Length));
                sb.Append($"\"base64\":\"{base64}\"");
                sb.Append("}");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
            finally
            {
                try { if (File.Exists(job.TempZipPath)) File.Delete(job.TempZipPath); } catch { }
                lock (_zipJobsLock) _zipJobs.Remove(jobId);
            }
        }

        void RunZipJob(ZipJob job)
        {
            try
            {
                if (File.Exists(job.TempZipPath))
                {
                    try { File.Delete(job.TempZipPath); } catch { }
                }

                // Phase 1 — scan
                var files = new List<string>();
                EnumerateFilesRecursive(job.SourcePath, files, job.ExcludeDirPrefixes);
                job.TotalFiles = files.Count;
                job.Stage = "zipping";

                // Phase 2 — zip one file at a time with per-file progress
                var srcRoot = NormalizePath(job.SourcePath);
                var buf = new byte[81920];
                using (var fs = new FileStream(job.TempZipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    foreach (var file in files)
                    {
                        var full = NormalizePath(file);
                        var rel = full.Length > srcRoot.Length
                            ? full.Substring(srcRoot.Length).TrimStart('/')
                            : Path.GetFileName(full);
                        if (string.IsNullOrEmpty(rel)) continue;
                        var entry = zip.CreateEntry(rel, System.IO.Compression.CompressionLevel.Fastest);
                        using (var es = entry.Open())
                        using (var ifs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            int n;
                            while ((n = ifs.Read(buf, 0, buf.Length)) > 0)
                            {
                                es.Write(buf, 0, n);
                                Interlocked.Add(ref job.BytesWritten, n);
                            }
                        }
                        Interlocked.Increment(ref job.ProcessedFiles);
                    }

                    // Phase 3 — pre-parse databases so snapshot viewers don't
                    // need a live device. Best-effort; failures are logged
                    // and the zip still completes.
                    if (DatabasePlugins != null)
                    {
                        try
                        {
                            foreach (var entry in DatabasePlugins.ScanAndParseAll(srcRoot))
                            {
                                var entryPath = "_databases/" + entry.RelativePath + ".json";
                                var ze = zip.CreateEntry(entryPath, System.IO.Compression.CompressionLevel.Fastest);
                                using (var es = ze.Open())
                                using (var sw = new StreamWriter(es, new UTF8Encoding(false)))
                                {
                                    sw.Write(entry.Json);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            BugpunchNative.ReportSdkError("FileService.ZipDatabaseParse", ex);
                        }
                    }
                }

                job.TotalSize = new FileInfo(job.TempZipPath).Length;
                job.Stage = "done";
                job.CompletedAtUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                job.Error = ex.Message;
                job.Stage = "error";
                job.CompletedAtUtc = DateTime.UtcNow;
                try { if (File.Exists(job.TempZipPath)) File.Delete(job.TempZipPath); } catch { }
            }
        }

        static void EnumerateFilesRecursive(string dir, List<string> output, string[] excludeDirPrefixes = null)
        {
            try
            {
                foreach (var f in Directory.GetFiles(dir)) output.Add(f);
                foreach (var d in Directory.GetDirectories(dir))
                {
                    if (excludeDirPrefixes != null)
                    {
                        var name = Path.GetFileName(d);
                        bool skip = false;
                        for (int i = 0; i < excludeDirPrefixes.Length; i++)
                        {
                            if (!string.IsNullOrEmpty(name) && name.StartsWith(excludeDirPrefixes[i], StringComparison.OrdinalIgnoreCase))
                            {
                                skip = true;
                                break;
                            }
                        }
                        if (skip) continue;
                    }
                    EnumerateFilesRecursive(d, output, excludeDirPrefixes);
                }
            }
            catch { /* skip inaccessible subtrees */ }
        }

        void PruneOldJobs_Locked()
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-10);
            List<string> stale = null;
            foreach (var kv in _zipJobs)
            {
                var j = kv.Value;
                if (j.CompletedAtUtc != default && j.CompletedAtUtc < cutoff)
                {
                    (stale ??= new List<string>()).Add(kv.Key);
                    try { if (!string.IsNullOrEmpty(j.TempZipPath) && File.Exists(j.TempZipPath)) File.Delete(j.TempZipPath); } catch { }
                }
            }
            if (stale != null) foreach (var k in stale) _zipJobs.Remove(k);
        }

        /// <summary>
        /// Extract a base64-encoded zip to a directory (replaces contents).
        ///
        /// <paramref name="preserveDirPrefixes"/> is a comma-separated list of
        /// directory-name prefixes left untouched by <c>clearFirst</c>.
        /// Snapshots exclude SDK-internal folders (e.g. <c>bugpunch_uploads</c>);
        /// the restore mirrors that so the live SDK state isn't wiped.
        /// </summary>
        public string UnzipToDirectory(string path, string base64Zip, bool clearFirst = true, string preserveDirPrefixes = null)
        {
            if (string.IsNullOrEmpty(path))
                return Error("Path is required");

            path = NormalizePath(path);
            if (!IsAllowed(path))
                return Error("Access denied: path is outside allowed roots");

            try
            {
                var bytes = Convert.FromBase64String(base64Zip);
                var tempZip = Path.Combine(Application.temporaryCachePath, $"restore_{DateTime.UtcNow:yyyyMMddHHmmss}.zip");
                File.WriteAllBytes(tempZip, bytes);

                string[] preserves = null;
                if (!string.IsNullOrEmpty(preserveDirPrefixes))
                {
                    var parts = preserveDirPrefixes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    var list = new List<string>(parts.Length);
                    foreach (var p in parts)
                    {
                        var t = p.Trim();
                        if (t.Length > 0) list.Add(t);
                    }
                    if (list.Count > 0) preserves = list.ToArray();
                }

                if (clearFirst && Directory.Exists(path))
                {
                    // Delete contents but not the directory itself
                    foreach (var f in Directory.GetFiles(path)) File.Delete(f);
                    foreach (var d in Directory.GetDirectories(path))
                    {
                        if (preserves != null)
                        {
                            var name = Path.GetFileName(d);
                            bool keep = false;
                            for (int i = 0; i < preserves.Length; i++)
                            {
                                if (!string.IsNullOrEmpty(name) && name.StartsWith(preserves[i], StringComparison.OrdinalIgnoreCase))
                                {
                                    keep = true;
                                    break;
                                }
                            }
                            if (keep) continue;
                        }
                        Directory.Delete(d, true);
                    }
                }

                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                ZipFile.ExtractToDirectory(tempZip, path);
                File.Delete(tempZip);

                return "{\"ok\":true}";
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // PlayerPrefs Export/Import
        // ------------------------------------------------------------------

        /// <summary>
        /// Export all known PlayerPrefs keys as JSON.
        /// Since Unity doesn't provide a way to enumerate all keys,
        /// we export a provided list of keys or use common patterns.
        /// </summary>
        public string ExportPlayerPrefs(string keysJson)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("{\"ok\":true,\"prefs\":{");
                bool first = true;

                // Parse key list from JSON array, or use empty
                string[] keys = null;
                if (!string.IsNullOrEmpty(keysJson))
                {
                    // Simple JSON array parse: ["key1","key2"]
                    var trimmed = keysJson.Trim();
                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        var inner = trimmed.Substring(1, trimmed.Length - 2);
                        keys = inner.Split(',');
                        for (int i = 0; i < keys.Length; i++)
                            keys[i] = keys[i].Trim().Trim('"');
                    }
                }

                if (keys != null)
                {
                    foreach (var key in keys)
                    {
                        if (string.IsNullOrEmpty(key)) continue;
                        if (!UnityEngine.PlayerPrefs.HasKey(key)) continue;

                        if (!first) sb.Append(",");
                        first = false;

                        // Try int, then float, then string
                        var intVal = UnityEngine.PlayerPrefs.GetInt(key, int.MinValue);
                        var floatVal = UnityEngine.PlayerPrefs.GetFloat(key, float.MinValue);
                        var strVal = UnityEngine.PlayerPrefs.GetString(key, null);

                        sb.Append($"\"{BugpunchJson.Esc(key)}\":");
                        if (strVal != null)
                            sb.Append($"{{\"type\":\"string\",\"value\":\"{BugpunchJson.Esc(strVal)}\"}}");
                        else if (intVal != int.MinValue)
                            sb.Append($"{{\"type\":\"int\",\"value\":{intVal}}}");
                        else if (floatVal != float.MinValue)
                            sb.Append($"{{\"type\":\"float\",\"value\":{floatVal.ToString("G", CultureInfo.InvariantCulture)}}}");
                    }
                }

                sb.Append("}}");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        }

        /// <summary>
        /// Import PlayerPrefs from a JSON object.
        /// </summary>
        public string ImportPlayerPrefs(string prefsJson, bool clearFirst = false)
        {
            try
            {
                if (clearFirst)
                    UnityEngine.PlayerPrefs.DeleteAll();

                // Simple parsing — expects {"key":{"type":"string","value":"val"}, ...}
                // For robustness, use the JsonVal helper pattern
                int imported = 0;
                // Crude but works for flat key-value prefs
                var pairs = prefsJson.Trim().TrimStart('{').TrimEnd('}').Split(new[] { "}," }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var pair in pairs)
                {
                    var colonIdx = pair.IndexOf(':');
                    if (colonIdx < 0) continue;
                    var key = pair.Substring(0, colonIdx).Trim().Trim('"');
                    var rest = pair.Substring(colonIdx + 1).TrimEnd('}');

                    var typeStart = rest.IndexOf("\"type\":\"") + 8;
                    var typeEnd = rest.IndexOf("\"", typeStart);
                    if (typeStart < 8 || typeEnd < 0) continue;
                    var type = rest.Substring(typeStart, typeEnd - typeStart);

                    var valStart = rest.IndexOf("\"value\":") + 8;
                    if (valStart < 8) continue;
                    var valStr = rest.Substring(valStart).Trim().Trim('"');

                    switch (type)
                    {
                        case "string":
                            UnityEngine.PlayerPrefs.SetString(key, valStr);
                            imported++;
                            break;
                        case "int":
                            if (int.TryParse(valStr, out var iv)) { UnityEngine.PlayerPrefs.SetInt(key, iv); imported++; }
                            break;
                        case "float":
                            if (float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv)) { UnityEngine.PlayerPrefs.SetFloat(key, fv); imported++; }
                            break;
                    }
                }

                UnityEngine.PlayerPrefs.Save();
                return $"{{\"ok\":true,\"imported\":{imported}}}";
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // Security
        // ------------------------------------------------------------------

        bool IsAllowed(string path)
        {
            foreach (var (_, root) in _roots)
            {
                if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        static string NormalizePath(string path)
        {
            // Normalize separators and resolve relative segments
            try
            {
                return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
            }
            catch
            {
                return path.Replace('\\', '/').TrimEnd('/');
            }
        }

        /// <summary>
        /// Simple binary detection — checks for null bytes in the first N bytes.
        /// </summary>
        static bool IsBinary(byte[] data, int checkLength)
        {
            var len = Math.Min(data.Length, checkLength);
            for (int i = 0; i < len; i++)
            {
                if (data[i] == 0) return true;
            }
            return false;
        }

        static string Error(string message)
        {
            return $"{{\"ok\":false,\"error\":\"{BugpunchJson.Esc(message)}\"}}";
        }

    }
}
