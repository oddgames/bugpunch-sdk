using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
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

        public FileService()
        {
            _roots = new List<(string, string)>();
            TryAddRoot("Persistent", Application.persistentDataPath);
            TryAddRoot("Cache", Application.temporaryCachePath);
            TryAddRoot("Data", Application.dataPath);
            TryAddRoot("StreamingAssets", Application.streamingAssetsPath);
            TryAddRoot("ConsoleLog", Application.consoleLogPath);
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
                sb.Append($"\"name\":\"{Esc(name)}\",");
                sb.Append($"\"path\":\"{Esc(path)}\",");
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
                    sb.Append($"\"name\":\"{Esc(info.Name)}\",");
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
                    sb.Append($"\"name\":\"{Esc(info.Name)}\",");
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
                    // Read truncated
                    var bytes = new byte[maxBytes];
                    using (var fs = File.OpenRead(path))
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
                        sb.Append($"\"content\":\"{Esc(text)}\",");
                        sb.Append(string.Format(CultureInfo.InvariantCulture, "\"size\":{0},", size));
                        sb.Append($"\"truncated\":true,");
                        sb.Append(string.Format(CultureInfo.InvariantCulture, "\"readBytes\":{0},", maxBytes));
                        sb.Append("\"encoding\":\"utf-8\"}");
                        return sb.ToString();
                    }
                }

                // Read full file
                var allBytes = File.ReadAllBytes(path);
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
                    sb.Append($"\"content\":\"{Esc(text)}\",");
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
                    sb.Append($"\"name\":\"{Esc(info.Name)}\",");
                    sb.Append($"\"path\":\"{Esc(info.FullName)}\",");
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
                    sb.Append($"\"name\":\"{Esc(info.Name)}\",");
                    sb.Append($"\"path\":\"{Esc(info.FullName)}\",");
                    sb.Append("\"isDirectory\":false,");
                    sb.Append(string.Format(CultureInfo.InvariantCulture, "\"size\":{0},", info.Length));
                    sb.Append($"\"extension\":\"{Esc(info.Extension)}\",");
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

        /// <summary>
        /// Create a zip of a directory and return it as base64.
        /// </summary>
        public string ZipDirectory(string path)
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
                var tempZip = Path.Combine(Application.temporaryCachePath, $"snapshot_{DateTime.UtcNow:yyyyMMddHHmmss}.zip");
                if (File.Exists(tempZip)) File.Delete(tempZip);
                ZipFile.CreateFromDirectory(path, tempZip);
                var bytes = File.ReadAllBytes(tempZip);
                File.Delete(tempZip);
                var base64 = Convert.ToBase64String(bytes);

                var sb = new StringBuilder();
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
        }

        /// <summary>
        /// Extract a base64-encoded zip to a directory (replaces contents).
        /// </summary>
        public string UnzipToDirectory(string path, string base64Zip, bool clearFirst = true)
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

                if (clearFirst && Directory.Exists(path))
                {
                    // Delete contents but not the directory itself
                    foreach (var f in Directory.GetFiles(path)) File.Delete(f);
                    foreach (var d in Directory.GetDirectories(path)) Directory.Delete(d, true);
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
            return $"{{\"ok\":false,\"error\":\"{Esc(message)}\"}}";
        }

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t") ?? "";
    }
}
