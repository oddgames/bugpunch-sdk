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

        public enum ScriptPermission { Ask, Always, Never }

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

        public static BugpunchConfig Load()
        {
            var config = Resources.Load<BugpunchConfig>("BugpunchConfig");
            if (config == null)
                Debug.LogWarning("[Bugpunch] No BugpunchConfig found in Resources/. Create one via Assets > Create > ODD Games > Bugpunch Config");
            return config;
        }
    }
}
