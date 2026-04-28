using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace ODDGames.Bugpunch.RemoteIDE.Database
{
    /// <summary>
    /// Discovers and caches <see cref="IDatabasePlugin"/> implementations at
    /// startup by scanning all loaded assemblies. Handles <c>/databases/*</c>
    /// tunnel requests and the snapshot pre-parse hook.
    /// </summary>
    public class DatabasePluginRegistry
    {
        readonly Dictionary<string, IDatabasePlugin> _plugins = new();
        bool _scanned;

        /// <summary>
        /// Scan assemblies for IDatabasePlugin implementations. Safe to call
        /// multiple times — only scans once.
        /// </summary>
        public void ScanIfNeeded()
        {
            if (_scanned) return;
            _scanned = true;

            var iface = typeof(IDatabasePlugin);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in asm.GetTypes())
                    {
                        if (type.IsAbstract || type.IsInterface) continue;
                        if (!iface.IsAssignableFrom(type)) continue;

                        try
                        {
                            var plugin = (IDatabasePlugin)Activator.CreateInstance(type);
                            if (plugin.IsAvailable())
                            {
                                _plugins[plugin.ProviderId] = plugin;
                                BugpunchLog.Info("DatabasePluginRegistry", $"Database plugin registered: {plugin.DisplayName} ({plugin.ProviderId})");
                            }
                            else
                            {
                                BugpunchLog.Info("DatabasePluginRegistry", $"Database plugin skipped (library not found): {plugin.DisplayName}");
                            }
                        }
                        catch (Exception ex)
                        {
                            BugpunchNative.ReportSdkError($"DatabasePluginRegistry.Instantiate({type.Name})", ex);
                        }
                    }
                }
                catch { /* skip unloadable assemblies */ }
            }
        }

        /// <summary>
        /// JSON listing of all detected plugins and their availability.
        /// </summary>
        public string ListProviders()
        {
            ScanIfNeeded();
            var sb = new StringBuilder();
            sb.Append("[");
            bool first = true;
            foreach (var p in _plugins.Values)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append("{\"id\":\"").Append(BugpunchJson.Esc(p.ProviderId));
                sb.Append("\",\"displayName\":\"").Append(BugpunchJson.Esc(p.DisplayName));
                sb.Append("\",\"extensions\":[");
                for (int i = 0; i < p.Extensions.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append("\"").Append(BugpunchJson.Esc(p.Extensions[i])).Append("\"");
                }
                sb.Append("],\"available\":true}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Parse a file using the named plugin. Returns the wire-format JSON
        /// expected by the dashboard.
        /// </summary>
        public string Parse(string filePath, string providerId)
        {
            ScanIfNeeded();
            if (!_plugins.TryGetValue(providerId, out var plugin))
                return Serialize(new ParseResult { ok = false, error = $"No plugin found for provider '{providerId}'" });

            return Serialize(SafeParse(plugin, filePath));
        }

        /// <summary>
        /// Walk <paramref name="rootDir"/>, run every available plugin against
        /// every file matching its extensions, and emit a list of
        /// <c>(relativePath, providerId, parsedJson)</c> tuples ready to be
        /// embedded in a snapshot zip.
        /// <para>Used by the snapshot pre-parse hook so snapshots viewed
        /// later don't need a live device.</para>
        /// </summary>
        public IEnumerable<SnapshotParseEntry> ScanAndParseAll(string rootDir)
        {
            ScanIfNeeded();
            if (string.IsNullOrEmpty(rootDir) || !Directory.Exists(rootDir)) yield break;

            var rootFull = Path.GetFullPath(rootDir);
            // Collect (filePath, providerId) pairs first so directory-based
            // providers (Siaqodb) only get one parse per containing dir.
            var jobs = new List<(string path, IDatabasePlugin plugin)>();
            var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in EnumerateFilesSafe(rootFull))
            {
                var nameLower = Path.GetFileName(file).ToLowerInvariant();
                foreach (var p in _plugins.Values)
                {
                    bool matches = false;
                    foreach (var ext in p.Extensions)
                        if (nameLower.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) { matches = true; break; }
                    if (!matches) continue;

                    // Siaqodb stores many .sqo files in one dir; emit a single
                    // entry keyed off the directory's first matching file.
                    if (p.ProviderId == "sqo")
                    {
                        var dir = Path.GetDirectoryName(file) ?? rootFull;
                        if (!seenDirs.Add(dir)) continue;
                    }

                    jobs.Add((file, p));
                    break;
                }
            }

            foreach (var (path, plugin) in jobs)
            {
                ParseResult result;
                try { result = plugin.Parse(path); }
                catch (Exception ex)
                {
                    BugpunchNative.ReportSdkError($"DatabasePluginRegistry.SnapshotParse({plugin.ProviderId})", ex);
                    result = new ParseResult { ok = false, error = ex.Message };
                }

                var rel = path.Length > rootFull.Length
                    ? path.Substring(rootFull.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : Path.GetFileName(path);
                rel = rel.Replace('\\', '/');

                yield return new SnapshotParseEntry
                {
                    RelativePath = rel,
                    ProviderId = plugin.ProviderId,
                    Json = Serialize(result),
                };
            }
        }

        // -----------------------------------------------------------------
        // Internals
        // -----------------------------------------------------------------

        static ParseResult SafeParse(IDatabasePlugin plugin, string filePath)
        {
            try { return plugin.Parse(filePath) ?? new ParseResult { ok = false, error = "Plugin returned null" }; }
            catch (Exception ex)
            {
                BugpunchNative.ReportSdkError($"DatabasePluginRegistry.Parse({plugin.ProviderId})", ex);
                return new ParseResult { ok = false, error = ex.Message };
            }
        }

        static string Serialize(ParseResult result)
        {
            // NullValueHandling.Ignore drops the unused "error" field on success
            // and the unused "tables" field on failure, matching the previous
            // hand-rolled wire format.
            return JsonConvert.SerializeObject(result, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
            });
        }

        static IEnumerable<string> EnumerateFilesSafe(string dir)
        {
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { yield break; }
            foreach (var f in files) yield return f;

            string[] subdirs;
            try { subdirs = Directory.GetDirectories(dir); }
            catch { yield break; }
            foreach (var sub in subdirs)
            {
                // Skip SDK-internal folders to avoid recursion into our own caches.
                var name = Path.GetFileName(sub);
                if (!string.IsNullOrEmpty(name) && name.StartsWith("bugpunch_", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var f in EnumerateFilesSafe(sub)) yield return f;
            }
        }
    }

    /// <summary>One pre-parsed database entry destined for a snapshot zip.</summary>
    public class SnapshotParseEntry
    {
        /// <summary>Path relative to the snapshot root, with forward slashes.</summary>
        public string RelativePath;
        public string ProviderId;
        /// <summary>Serialized <see cref="ParseResult"/> ready to write into the zip.</summary>
        public string Json;
    }
}
