using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ODDGames.Scripting.Ide;
using UnityEngine;

namespace ODDGames.Scripting.Unity
{
    /// <summary>
    /// Reusable host for an in-game web Script IDE. Owns the running <see cref="ScriptIdeServer"/> and its
    /// shared <see cref="Workspace"/>, seeds onboarding files (if absent), forwards Unity log messages into
    /// the IDE's browser console, and exposes the connect URL + guarded workspace file ops. Everything
    /// game-specific — window title, mods folder, seed files, compile options, the event catalog, and the
    /// Objects-tab object-catalog/attach hooks — is passed in via <see cref="Options"/>, so this class
    /// references no RuntimeEditor or game type. Hold one instance (e.g. statically) so the server survives
    /// UI open/close.
    /// </summary>
    public sealed class ScriptIdeServerHost
    {
        /// <summary>Everything a host supplies to stand up the IDE server. The functional inputs are all
        /// plain SDK values/delegates; the rest are display/branding strings.</summary>
        public sealed class Options
        {
            /// <summary>Absolute folder the workspace cache file lives in (e.g. <c>persistentDataPath/Mods</c>).</summary>
            public string RootDirectory;
            /// <summary>Display label for the workspace root shown in the IDE tree. Default "Mods".</summary>
            public string WorkspaceLabel = "Mods";
            /// <summary>TCP port; 0 picks a free one.</summary>
            public int Port;
            /// <summary>Browser tab title.</summary>
            public string Title = "Script IDE";
            public ScriptCompileOptions CompileOptions;
            public string ScriptPreamble;
            public IList<IdeEventDescriptor> EventCatalog;
            public Func<string> ObjectCatalogProvider;
            public Func<string, string> AttachHandler;
            /// <summary>Host command buttons (e.g. "AI Generate") shown in the IDE toolbar.</summary>
            public IList<IdeCommand> Commands;
            /// <summary>Runs a host command (requestJson → resultJson); see <see cref="IdeOptions.CommandHandler"/>.</summary>
            public Func<string, string> CommandHandler;
            /// <summary>Types the API-reference panel documents via reflection (facade + injected/context types).</summary>
            public IList<Type> ApiReferenceTypes;
            /// <summary>Optional descriptions for the API reference, keyed by "Type"/"Type.Member".</summary>
            public IDictionary<string, string> ApiReferenceDescriptions;
            /// <summary>Optional code examples for the API reference, keyed by "Type"/"Type.Member".</summary>
            public IDictionary<string, string> ApiReferenceExamples;
            /// <summary>Onboarding files (workspace-relative path → content) written only when absent.</summary>
            public IReadOnlyDictionary<string, string> SeedFiles;
        }

        private ScriptIdeServer _server;
        private Workspace _workspace;
        private Application.LogCallback _logHandler;

        public bool Running => _server != null;
        public int Port { get; private set; }

        /// <summary>Loopback connect URL (e.g. <c>http://127.0.0.1:8971/</c>), or null when not running.</summary>
        public string Url => _server?.Url;

        /// <summary>Start the server (idempotent). Returns the connect URL.</summary>
        public string Start(Options o)
        {
            if (_server != null) return _server.Url;
            if (o == null) throw new ArgumentNullException(nameof(o));

            string cacheFile = Path.Combine(o.RootDirectory, "workspace.json");
            Directory.CreateDirectory(o.RootDirectory);
            _workspace = new Workspace(cacheFile, o.WorkspaceLabel ?? "Mods", false);
            if (o.SeedFiles != null)
                foreach (var kv in o.SeedFiles)
                    if (!_workspace.Exists(kv.Key)) _workspace.Write(kv.Key, kv.Value);

            var options = new IdeOptions
            {
                RootDirectory = o.WorkspaceLabel ?? "Mods",
                Port = o.Port,
                Title = o.Title,
                CacheFile = cacheFile,
                CompileOptions = o.CompileOptions,
                ScriptPreamble = o.ScriptPreamble,
                EventCatalog = o.EventCatalog,
                ObjectCatalogProvider = o.ObjectCatalogProvider,
                AttachHandler = o.AttachHandler,
                Commands = o.Commands,
                CommandHandler = o.CommandHandler,
                ApiReferenceTypes = o.ApiReferenceTypes,
                ApiReferenceDescriptions = o.ApiReferenceDescriptions,
                ApiReferenceExamples = o.ApiReferenceExamples,
            };

            _server = ScriptIde.Host(_workspace, options);
            Port = _server.Port;

            if (_logHandler == null)
            {
                _logHandler = OnUnityLog;
                Application.logMessageReceived += _logHandler;
            }
            return _server.Url;
        }

        public void Stop()
        {
            if (_logHandler != null) { Application.logMessageReceived -= _logHandler; _logHandler = null; }
            if (_server != null) { try { _server.Stop(); } catch { /* already torn down */ } }
            _server = null;
            _workspace = null;
            Port = 0;
        }

        // ── guarded workspace file ops (host seeds/syncs per-object script working copies through these) ──

        public bool WorkspaceExists(string path)
        {
            try { return _workspace?.Exists(path) ?? false; }
            catch { return false; }
        }

        public void WorkspaceWrite(string path, string content)
        {
            try { _workspace?.Write(path, content ?? ""); }
            catch { /* workspace torn down mid-write */ }
        }

        public string WorkspaceReadOrNull(string path)
        {
            try { return _workspace?.ReadOrNull(path); }
            catch { return null; }
        }

        // ── helpers ──

        /// <summary>
        /// All usable LAN IPv4 addresses of this machine, best candidate first, for the "connect from another
        /// device" URL. A real Wi-Fi/Ethernet interface (one that owns a default gateway) ranks above virtual
        /// adapters (VirtualBox / Hyper-V / WSL / VMware / VPN), whose addresses a phone on the same Wi-Fi can't
        /// reach — picking the first interface blindly (the old behaviour) often returned exactly such a dead
        /// address. Loopback, APIPA (169.254.x), carrier-grade-NAT (100.64–127.x) and tunnel interfaces are dropped.
        /// </summary>
        public static IReadOnlyList<string> LanIps()
        {
            var ranked = new List<KeyValuePair<int, string>>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    var type = ni.NetworkInterfaceType;
                    if (type == NetworkInterfaceType.Loopback || type == NetworkInterfaceType.Tunnel) continue;

                    IPInterfaceProperties props;
                    try { props = ni.GetIPProperties(); }
                    catch { continue; }

                    bool hasGateway = false;
                    try
                    {
                        foreach (var g in props.GatewayAddresses)
                            if (g?.Address != null && g.Address.AddressFamily == AddressFamily.InterNetwork
                                && !g.Address.Equals(IPAddress.Any)) { hasGateway = true; break; }
                    }
                    catch { /* gateway query unsupported on some platforms */ }

                    bool virtualAdapter = IsVirtualAdapter(ni.Name + " " + ni.Description);

                    foreach (var ua in props.UnicastAddresses)
                    {
                        var a = ua.Address;
                        if (a.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(a)) continue;
                        var b = a.GetAddressBytes();
                        if (b[0] == 169 && b[1] == 254) continue;               // APIPA link-local (no DHCP)
                        if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) continue; // CGNAT (Tailscale / carrier)

                        int score = 0;
                        if (hasGateway) score += 100;                            // a real route off this machine
                        if (type == NetworkInterfaceType.Wireless80211) score += 20;
                        else if (type == NetworkInterfaceType.Ethernet) score += 18;
                        if (IsPrivateV4(b)) score += 10;                         // RFC1918 over a routable public IP
                        if (virtualAdapter) score -= 60;

                        ranked.Add(new KeyValuePair<int, string>(score, a.ToString()));
                    }
                }
            }
            catch { /* some platforms restrict interface enumeration */ }

            return ranked
                .GroupBy(p => p.Value)
                .Select(g => new KeyValuePair<int, string>(g.Max(p => p.Key), g.Key))
                .OrderByDescending(p => p.Key)
                .Select(p => p.Value)
                .ToList();
        }

        /// <summary>Best-effort single LAN IPv4 (the top-ranked of <see cref="LanIps"/>), or null.</summary>
        public static string LanIp()
        {
            var all = LanIps();
            return all.Count > 0 ? all[0] : null;
        }

        private static bool IsPrivateV4(byte[] b)
            => b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168);

        private static bool IsVirtualAdapter(string label)
        {
            if (string.IsNullOrEmpty(label)) return false;
            label = label.ToLowerInvariant();
            return label.Contains("virtual") || label.Contains("vmware") || label.Contains("vbox")
                || label.Contains("hyper-v") || label.Contains("vethernet") || label.Contains("wsl")
                || label.Contains("docker") || label.Contains("tailscale") || label.Contains("zerotier")
                || label.Contains("tap-") || label.Contains("npcap") || label.Contains("pseudo")
                || label.Contains("bluetooth") || label.Contains("vpn") || label.Contains("loopback");
        }

        private void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            var server = _server;
            if (server == null) return;
            string level;
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert: level = "error"; break;
                case LogType.Warning: level = "warning"; break;
                default: return; // plain Debug.Log spam — the console is for problems
            }
            string msg = (level == "error" && !string.IsNullOrEmpty(stackTrace)) ? condition + "\n" + stackTrace : condition;
            try { server.Log(level, msg); } catch { /* server torn down mid-callback */ }
        }
    }
}
