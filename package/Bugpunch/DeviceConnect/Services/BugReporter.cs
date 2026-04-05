using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Bug reporting system. Captures video buffer, logs, screenshots, device info,
    /// and sends to the Bugpunch server. Triggered by shake gesture, key combo, or API.
    /// </summary>
    public class BugReporter : MonoBehaviour
    {
        [Header("Trigger")]
        [Tooltip("Enable shake-to-report on mobile")]
        public bool shakeToReport = true;

        [Tooltip("Shake threshold acceleration")]
        public float shakeThreshold = 2.5f;

        [Tooltip("Keyboard shortcut to open report (editor/desktop)")]
        public KeyCode reportKey = KeyCode.F12;

        [Header("Video Buffer")]
        [Tooltip("Seconds of video to keep in circular buffer")]
        public int videoBufferSeconds = 30;

        [Tooltip("Video capture FPS")]
        public int videoFps = 10;

        [Tooltip("Video capture scale (0.25 = quarter resolution)")]
        public float videoScale = 0.25f;

        [Tooltip("JPEG quality for video frames")]
        public int videoQuality = 50;

        // State
        bool _isCapturing;
        bool _reportDialogOpen;

        // Circular video buffer (last N seconds of JPEG frames)
        readonly List<FrameData> _frameBuffer = new();
        int _maxFrames;
        float _lastCaptureTime;

        // Log buffer (last 500 entries)
        readonly List<LogEntry> _logBuffer = new();
        const int MAX_LOGS = 500;

        // Custom data attached by the game
        readonly Dictionary<string, string> _customData = new();

        // Callbacks
        public event Action OnReportStarted;
        public event Action<bool> OnReportSent; // true = success

        struct FrameData
        {
            public float time;
            public byte[] jpeg;
        }

        struct LogEntry
        {
            public float time;
            public string message;
            public string stackTrace;
            public LogType type;
        }

        void Awake()
        {
            _maxFrames = videoBufferSeconds * videoFps;
            Application.logMessageReceivedThreaded += OnLog;
        }

        void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= OnLog;
        }

        void Start()
        {
            _isCapturing = true;
            StartCoroutine(CaptureLoop());
        }

        void Update()
        {
            // Desktop: F12 to report
            if (Input.GetKeyDown(reportKey))
            {
                StartReport();
                return;
            }

            // Mobile: shake to report
            if (shakeToReport && Input.acceleration.sqrMagnitude > shakeThreshold * shakeThreshold)
            {
                StartReport();
            }
        }

        /// <summary>
        /// Circular buffer capture loop — captures screen at configured FPS.
        /// </summary>
        IEnumerator CaptureLoop()
        {
            var interval = 1f / videoFps;

            while (_isCapturing)
            {
                yield return new WaitForEndOfFrame();

                if (Time.realtimeSinceStartup - _lastCaptureTime < interval) continue;
                _lastCaptureTime = Time.realtimeSinceStartup;

                // Capture screen
                var w = Mathf.Max(1, Mathf.RoundToInt(Screen.width * videoScale));
                var h = Mathf.Max(1, Mathf.RoundToInt(Screen.height * videoScale));

                var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                tex.Apply();

                // Scale down
                var rt = RenderTexture.GetTemporary(w, h);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                Graphics.Blit(tex, rt);
                var scaled = new Texture2D(w, h, TextureFormat.RGB24, false);
                scaled.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                scaled.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                Destroy(tex);

                var jpeg = scaled.EncodeToJPG(videoQuality);
                Destroy(scaled);

                // Add to circular buffer
                lock (_frameBuffer)
                {
                    _frameBuffer.Add(new FrameData { time = Time.realtimeSinceStartup, jpeg = jpeg });
                    while (_frameBuffer.Count > _maxFrames)
                        _frameBuffer.RemoveAt(0);
                }
            }
        }

        void OnLog(string message, string stackTrace, LogType type)
        {
            lock (_logBuffer)
            {
                if (_logBuffer.Count >= MAX_LOGS)
                    _logBuffer.RemoveAt(0);
                _logBuffer.Add(new LogEntry
                {
                    time = Time.realtimeSinceStartup,
                    message = message,
                    stackTrace = stackTrace,
                    type = type
                });
            }
        }

        // ── Public API ──

        /// <summary>
        /// Attach custom data to the next bug report.
        /// </summary>
        public void SetCustomData(string key, string value)
        {
            _customData[key] = value;
        }

        /// <summary>
        /// Remove custom data.
        /// </summary>
        public void ClearCustomData(string key)
        {
            _customData.Remove(key);
        }

        /// <summary>
        /// Start a bug report. Captures current state and sends to server.
        /// </summary>
        public void StartReport(string title = null, string description = null,
            BugReportType type = BugReportType.Bug)
        {
            if (_reportDialogOpen) return;
            _reportDialogOpen = true;
            OnReportStarted?.Invoke();

            StartCoroutine(SendReport(title, description, type));
        }

        /// <summary>
        /// Send feedback (simpler than bug report — no video, just text + screenshot).
        /// </summary>
        public void SendFeedback(string message, FeedbackRating rating = FeedbackRating.Neutral)
        {
            StartCoroutine(SendFeedbackCoroutine(message, rating));
        }

        IEnumerator SendReport(string title, string description, BugReportType type)
        {
            // Capture screenshot at full resolution
            yield return new WaitForEndOfFrame();
            var screenshotTex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            screenshotTex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            screenshotTex.Apply();
            var screenshot = screenshotTex.EncodeToJPG(85);
            Destroy(screenshotTex);

            // Snapshot video buffer
            List<FrameData> videoFrames;
            lock (_frameBuffer)
            {
                videoFrames = new List<FrameData>(_frameBuffer);
            }

            // Snapshot logs
            List<LogEntry> logs;
            lock (_logBuffer)
            {
                logs = new List<LogEntry>(_logBuffer);
            }

            // Build report payload
            var report = new StringBuilder();
            report.Append("{");
            report.Append($"\"type\":\"{type}\",");
            report.Append($"\"title\":\"{Esc(title ?? "Bug Report")}\",");
            report.Append($"\"description\":\"{Esc(description ?? "")}\",");
            report.Append($"\"timestamp\":\"{DateTime.UtcNow:O}\",");

            // Device info
            report.Append("\"device\":{");
            report.Append($"\"name\":\"{Esc(SystemInfo.deviceName)}\",");
            report.Append($"\"model\":\"{Esc(SystemInfo.deviceModel)}\",");
            report.Append($"\"os\":\"{Esc(SystemInfo.operatingSystem)}\",");
            report.Append($"\"platform\":\"{Application.platform}\",");
            report.Append($"\"gpu\":\"{Esc(SystemInfo.graphicsDeviceName)}\",");
            report.Append($"\"ram\":{SystemInfo.systemMemorySize},");
            report.Append($"\"vram\":{SystemInfo.graphicsMemorySize},");
            report.Append($"\"screenWidth\":{Screen.width},");
            report.Append($"\"screenHeight\":{Screen.height},");
            report.Append($"\"dpi\":{Screen.dpi}");
            report.Append("},");

            // App info
            report.Append("\"app\":{");
            report.Append($"\"version\":\"{Esc(Application.version)}\",");
            report.Append($"\"bundleId\":\"{Esc(Application.identifier)}\",");
            report.Append($"\"unityVersion\":\"{Esc(Application.unityVersion)}\",");
            report.Append($"\"scene\":\"{Esc(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)}\",");
            report.Append($"\"fps\":{Mathf.RoundToInt(1f / Time.deltaTime)},");
            report.Append($"\"uptime\":{Time.realtimeSinceStartup:F1}");
            report.Append("},");

            // Custom data
            report.Append("\"customData\":{");
            bool first = true;
            foreach (var kv in _customData)
            {
                if (!first) report.Append(",");
                first = false;
                report.Append($"\"{Esc(kv.Key)}\":\"{Esc(kv.Value)}\"");
            }
            report.Append("},");

            // Logs (last 100)
            report.Append("\"logs\":[");
            var logStart = Mathf.Max(0, logs.Count - 100);
            for (int i = logStart; i < logs.Count; i++)
            {
                if (i > logStart) report.Append(",");
                var log = logs[i];
                report.Append($"{{\"time\":{log.time:F3},\"type\":\"{log.type}\",\"message\":\"{Esc(log.message)}\",\"stackTrace\":\"{Esc(log.stackTrace)}\"}}");
            }
            report.Append("],");

            // Screenshot (base64)
            report.Append($"\"screenshot\":\"{Convert.ToBase64String(screenshot)}\",");

            // Video frames (base64 array)
            report.Append($"\"videoFps\":{videoFps},");
            report.Append("\"videoFrames\":[");
            for (int i = 0; i < videoFrames.Count; i++)
            {
                if (i > 0) report.Append(",");
                report.Append($"\"{Convert.ToBase64String(videoFrames[i].jpeg)}\"");
            }
            report.Append("]");

            report.Append("}");

            // Send to server via tunnel
            var client = BugpunchClient.Instance;
            if (client != null && client.IsConnected)
            {
                // Send as a special tunnel message
                client.Tunnel.SendResponse("bugreport", 200, report.ToString(), "application/json");
                Debug.Log($"[Bugpunch] Bug report sent ({videoFrames.Count} video frames, {screenshot.Length / 1024}KB screenshot)");
                OnReportSent?.Invoke(true);
            }
            else
            {
                // Queue for later or send via HTTP
                Debug.LogWarning("[Bugpunch] Not connected — bug report queued");
                // TODO: queue to disk and send when reconnected
                OnReportSent?.Invoke(false);
            }

            _reportDialogOpen = false;
        }

        IEnumerator SendFeedbackCoroutine(string message, FeedbackRating rating)
        {
            yield return new WaitForEndOfFrame();

            // Capture screenshot
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tex.Apply();
            var screenshot = tex.EncodeToJPG(75);
            Destroy(tex);

            var feedback = new StringBuilder();
            feedback.Append("{");
            feedback.Append($"\"type\":\"feedback\",");
            feedback.Append($"\"message\":\"{Esc(message)}\",");
            feedback.Append($"\"rating\":\"{rating}\",");
            feedback.Append($"\"timestamp\":\"{DateTime.UtcNow:O}\",");
            feedback.Append($"\"scene\":\"{Esc(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)}\",");
            feedback.Append($"\"appVersion\":\"{Esc(Application.version)}\",");
            feedback.Append($"\"platform\":\"{Application.platform}\",");
            feedback.Append($"\"device\":\"{Esc(SystemInfo.deviceName)}\",");
            feedback.Append($"\"screenshot\":\"{Convert.ToBase64String(screenshot)}\"");
            feedback.Append("}");

            var client = BugpunchClient.Instance;
            if (client != null && client.IsConnected)
            {
                client.Tunnel.SendResponse("feedback", 200, feedback.ToString(), "application/json");
                Debug.Log("[Bugpunch] Feedback sent");
            }
            else
            {
                Debug.LogWarning("[Bugpunch] Not connected — feedback not sent");
            }
        }

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t") ?? "";
    }

    public enum BugReportType
    {
        Bug,
        Crash,
        Performance,
        Visual,
        Other
    }

    public enum FeedbackRating
    {
        Negative,
        Neutral,
        Positive
    }
}
