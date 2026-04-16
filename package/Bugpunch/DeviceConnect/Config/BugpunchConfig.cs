using System;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    [CreateAssetMenu(fileName = "BugpunchConfig", menuName = "ODD Games/Bugpunch Config")]
    public class BugpunchConfig : ScriptableObject
    {
        [Header("Bugpunch")]
        [Tooltip("Server URL")]
        public string serverUrl = "https://bugpunchserver-7lrpxt00.b4a.run";

        [Tooltip("API key (from project settings on the dashboard)")]
        public string apiKey = "";

        [Header("Advanced")]
        [Tooltip("Script permission policy for remote execution")]
        public ScriptPermission scriptPermission = ScriptPermission.Ask;

        [Tooltip("Auto-connect on app start (debug builds / editor). When off, the game must call BugpunchClient.StartConnection() explicitly.")]
        public bool autoStart = false;

        [Tooltip("Upload Android unstripped symbols (and — via upload-ios-symbols.sh — iOS dSYMs) after each Player build. " +
                 "Feature is held off by default until server-side symbol storage + symbolicator RAM budget are resolved " +
                 "(bugpunch-server#208, #209). Native crash reports still write build-IDs regardless; enabling this only " +
                 "changes whether those IDs can be resolved to source on the dashboard.")]
        public bool symbolUploadEnabled = false;

        [Header("Crash Attachment Allow-list")]
        [Tooltip("Files Bugpunch is allowed to read and upload with a crash report. " +
                 "Server 'Request More Info' directives can only reference paths that " +
                 "match at least one rule here \u2014 the game stays in control of what " +
                 "can leave the device. Supported path tokens: [PersistentDataPath], " +
                 "[TemporaryCachePath], [DataPath].")]
        public AttachmentRule[] attachmentRules = Array.Empty<AttachmentRule>();

        public enum ScriptPermission { Ask, Always, Never }

        [Serializable]
        public class AttachmentRule
        {
            [Tooltip("Friendly identifier shown in the dashboard.")]
            public string name = "saves";

            [Tooltip("Directory \u2014 use a token like [PersistentDataPath]/saves.")]
            public string path = "[PersistentDataPath]";

            [Tooltip("Glob pattern, e.g. *.json or save_*.dat. Use * for everything.")]
            public string pattern = "*";

            [Tooltip("Per-file size cap in bytes. Files larger than this are skipped.")]
            public long maxBytes = 1024 * 1024;
        }

        // -- Defaults (not exposed in inspector) --

        // Screen capture
        internal int captureQuality = 75;
        internal float captureScale = 0.5f;
        internal int streamFps = 10;

        // Bug reporting
        internal int videoBufferSeconds = 30;
        internal int bugReportVideoFps = 10;

        // Crash handling
        internal bool enableNativeCrashHandler = true;
        internal int anrTimeoutMs = 5000;

        // Connection
        internal float reconnectDelay = 5f;
        internal float heartbeatInterval = 10f;
        internal float pollInterval = 30f;

        // -- Derived properties --

        public string HttpBaseUrl
        {
            get
            {
                var url = serverUrl.TrimEnd('/');
                if (url.StartsWith("ws://")) url = "http://" + url.Substring(5);
                else if (url.StartsWith("wss://")) url = "https://" + url.Substring(6);
                return url;
            }
        }

        public string EffectiveDeviceName => SystemInfo.deviceName;

        public string TunnelUrl
        {
            get
            {
                var baseUrl = serverUrl.TrimEnd('/');
                if (baseUrl.StartsWith("https://"))
                    baseUrl = "wss://" + baseUrl.Substring(8);
                else if (baseUrl.StartsWith("http://"))
                    baseUrl = "ws://" + baseUrl.Substring(7);
                return baseUrl + "/api/devices/tunnel";
            }
        }

        /// <summary>
        /// Resolve a path that may begin with a known Unity token
        /// ([PersistentDataPath], [TemporaryCachePath], [DataPath]) to an
        /// absolute path. Returns null if the token is unknown or unavailable
        /// on the current platform. Caller is responsible for further
        /// validation (rejecting "..", etc).
        /// </summary>
        public static string ResolvePathToken(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (path.StartsWith("[PersistentDataPath]"))
                return Application.persistentDataPath + path.Substring("[PersistentDataPath]".Length);
            if (path.StartsWith("[TemporaryCachePath]"))
                return Application.temporaryCachePath + path.Substring("[TemporaryCachePath]".Length);
            if (path.StartsWith("[DataPath]"))
                return Application.dataPath + path.Substring("[DataPath]".Length);
            return null;
        }

        public static BugpunchConfig Load()
        {
            var config = Resources.Load<BugpunchConfig>("BugpunchConfig");
            if (config == null)
                Debug.LogWarning("[Bugpunch] No BugpunchConfig found in Resources/. Create one via Assets > Create > ODD Games > Bugpunch Config");
            return config;
        }
    }
}
