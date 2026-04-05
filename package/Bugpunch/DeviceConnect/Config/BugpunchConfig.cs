using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    [CreateAssetMenu(fileName = "BugpunchConfig", menuName = "ODD Games/Bugpunch Config")]
    public class BugpunchConfig : ScriptableObject
    {
        [Header("Server Connection")]
        [Tooltip("Bugpunch server URL (e.g., wss://yourserver.com)")]
        public string serverUrl = "ws://localhost:5000";

        [Tooltip("API key for authentication (from project settings)")]
        public string apiKey = "";

        [Tooltip("Project ID (auto-filled from API key validation)")]
        public string projectId = "";

        [Header("Device Identity")]
        [Tooltip("Custom device name (defaults to SystemInfo.deviceName)")]
        public string deviceName = "";

        [Header("Features")]
        [Tooltip("Enable remote hierarchy inspection")]
        public bool enableHierarchy = true;

        [Tooltip("Enable remote console log streaming")]
        public bool enableConsole = true;

        [Tooltip("Enable remote screen capture")]
        public bool enableScreenCapture = true;

        [Tooltip("Enable C# script execution")]
        public bool enableScriptRunner = true;

        [Tooltip("Enable component inspector")]
        public bool enableInspector = true;

        [Header("Screen Capture")]
        [Tooltip("Capture quality (1-100)")]
        [Range(1, 100)]
        public int captureQuality = 75;

        [Tooltip("Capture scale (0.1-1.0)")]
        [Range(0.1f, 1f)]
        public float captureScale = 0.5f;

        [Tooltip("Max frames per second for streaming")]
        [Range(1, 30)]
        public int streamFps = 10;

        [Header("Bug Reporting")]
        [Tooltip("Enable shake-to-report on mobile")]
        public bool enableShakeToReport = true;

        [Tooltip("Seconds of video to buffer for bug reports")]
        public int videoBufferSeconds = 30;

        [Tooltip("Bug report video capture FPS")]
        public int bugReportVideoFps = 10;

        [Header("Connection")]
        [Tooltip("Auto-connect on play mode")]
        public bool autoConnect = true;

        [Tooltip("Reconnect delay in seconds after disconnect")]
        public float reconnectDelay = 5f;

        [Tooltip("Heartbeat interval in seconds")]
        public float heartbeatInterval = 10f;

        [Header("Connection Mode")]
        [Tooltip("Auto = debug builds use WebSocket, release use poll. Poll = always poll. Debug = always WebSocket")]
        public ConnectionMode connectionMode = ConnectionMode.Auto;

        [Tooltip("Poll interval in seconds (poll mode only)")]
        [Range(10, 120)]
        public float pollInterval = 30f;

        [Header("Script Permissions")]
        [Tooltip("How to handle remote script execution requests")]
        public ScriptPermission scriptPermission = ScriptPermission.Ask;

        public enum ConnectionMode { Auto, Poll, Debug }
        public enum ScriptPermission { Ask, Always, Never }

        /// <summary>
        /// Whether to use WebSocket (debug) or HTTP poll based on config and build type
        /// </summary>
        public bool ShouldUseWebSocket
        {
            get
            {
                // Editor always uses WebSocket — it's always a debug environment
                if (Application.isEditor) return true;
                if (connectionMode == ConnectionMode.Debug) return true;
                if (connectionMode == ConnectionMode.Poll) return false;
                return UnityEngine.Debug.isDebugBuild;
            }
        }

        /// <summary>
        /// Get the HTTP base URL for poll endpoints (converts ws:// to http://)
        /// </summary>
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

        /// <summary>
        /// Get the effective device name (custom or system default)
        /// </summary>
        public string EffectiveDeviceName =>
            string.IsNullOrEmpty(deviceName) ? SystemInfo.deviceName : deviceName;

        /// <summary>
        /// Get the WebSocket tunnel URL
        /// </summary>
        public string TunnelUrl
        {
            get
            {
                var baseUrl = serverUrl.TrimEnd('/');
                // Convert http(s) to ws(s)
                if (baseUrl.StartsWith("https://"))
                    baseUrl = "wss://" + baseUrl.Substring(8);
                else if (baseUrl.StartsWith("http://"))
                    baseUrl = "ws://" + baseUrl.Substring(7);
                return baseUrl + "/api/devices/tunnel";
            }
        }

        /// <summary>
        /// Load the config from Resources
        /// </summary>
        public static BugpunchConfig Load()
        {
            var config = Resources.Load<BugpunchConfig>("BugpunchConfig");
            if (config == null)
            {
                Debug.LogWarning("[Bugpunch] No BugpunchConfig found in Resources/. Create one via Assets > Create > ODD Games > Bugpunch Config");
            }
            return config;
        }
    }
}
