using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect.Database
{
    /// <summary>
    /// Discovers and caches <see cref="IDatabasePlugin"/> implementations at
    /// startup by scanning all loaded assemblies. The registry handles
    /// /databases/* tunnel requests.
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
                                Debug.Log($"[Bugpunch] Database plugin registered: {plugin.DisplayName} ({plugin.ProviderId})");
                            }
                            else
                            {
                                Debug.Log($"[Bugpunch] Database plugin skipped (library not found): {plugin.DisplayName}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[Bugpunch] Failed to instantiate database plugin {type.Name}: {ex.Message}");
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
                sb.Append("{\"id\":\"").Append(Esc(p.ProviderId));
                sb.Append("\",\"displayName\":\"").Append(Esc(p.DisplayName));
                sb.Append("\",\"extensions\":[");
                for (int i = 0; i < p.Extensions.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append("\"").Append(Esc(p.Extensions[i])).Append("\"");
                }
                sb.Append("],\"available\":true}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Parse a file using the named plugin. Returns JSON result.
        /// </summary>
        public string Parse(string filePath, string providerId)
        {
            ScanIfNeeded();
            if (!_plugins.TryGetValue(providerId, out var plugin))
                return Error($"No plugin found for provider '{providerId}'");

            try
            {
                return plugin.Parse(filePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Bugpunch] Database plugin {providerId} parse error: {ex}");
                return Error(ex.Message);
            }
        }

        static string Error(string msg) =>
            $"{{\"ok\":false,\"error\":\"{Esc(msg)}\"}}";

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"")
              .Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t") ?? "";
    }
}
