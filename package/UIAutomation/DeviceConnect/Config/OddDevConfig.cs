using UnityEngine;

namespace ODDGames.UIAutomation.DeviceConnect
{
    [CreateAssetMenu(fileName = "OddDevConfig", menuName = "ODD Games/OddDev Config")]
    public class OddDevConfig : ScriptableObject
    {
        [Header("Server Connection")]
        [Tooltip("OddDev server URL (e.g., wss://yourserver.com)")]
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
        public static OddDevConfig Load()
        {
            var config = Resources.Load<OddDevConfig>("OddDevConfig");
            if (config == null)
            {
                Debug.LogWarning("[OddDev] No OddDevConfig found in Resources/. Create one via Assets > Create > ODD Games > OddDev Config");
            }
            return config;
        }
    }
}
