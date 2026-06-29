using System;
using System.Collections.Generic;
using System.IO;
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

        /// <summary>Best-effort LAN IPv4 of this machine, for the "connect from another device" URL.</summary>
        public static string LanIp()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        var a = ua.Address;
                        if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                            return a.ToString();
                    }
                }
            }
            catch { /* some platforms restrict interface enumeration */ }
            return null;
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
