using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using ODDGames.Bugpunch.DeviceConnect.UI;
using UnityEngine;
using Debug = UnityEngine.Debug;

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

        [Header("Auto-capture on exception")]
        [Tooltip("Automatically file a bug report when an uncaught exception is logged")]
        public bool autoReportExceptions = true;

        [Tooltip("Minimum seconds between auto-reports so a tight exception loop doesn't flood the server")]
        public float autoReportCooldownSeconds = 30f;

        [Header("Video Buffer (ring buffer recorder — Android + iOS)")]
        [Tooltip("Seconds of video to keep in the rolling window before an exception")]
        public int videoBufferSeconds = 30;

        [Tooltip("Recording frame rate")]
        public int videoFps = 30;

        [Tooltip("H.264 bitrate (bits per second) — 2 Mbps is a good default for 720p")]
        public int videoBitrate = 2_000_000;

        [Tooltip("Record at this max dimension (the smaller screen axis). Lowering saves memory in the ring buffer.")]
        public int videoMaxDimension = 720;

        // Native ring buffer recorder — Android + iOS
        RingBufferRecorder _nativeRecorder;
        float _lastAutoReportTime = float.NegativeInfinity;

        // State
        bool _reportDialogOpen;
        bool _recordingModeActive;

        // Log buffer (last 500 entries)
        readonly List<LogEntry> _logBuffer = new();
        const int MAX_LOGS = 500;

        // Custom data attached by the game
        readonly Dictionary<string, string> _customData = new();

        // Callbacks
        public event Action OnReportStarted;
        public event Action<bool> OnReportSent; // true = success

        struct LogEntry
        {
            public float time;
            public string message;
            public string stackTrace;
            public LogType type;
        }

        void Awake()
        {
            Application.logMessageReceivedThreaded += OnLog;
        }

        void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            if (_nativeRecorder != null) _nativeRecorder.StopRecording();
        }

        /// <summary>
        /// Enable video ring buffer recording. Call this explicitly for alpha builds
        /// or feedback mode. Not enabled by default — video capture requires user consent
        /// (MediaProjection on Android, ReplayKit on iOS).
        /// </summary>
        public void EnableVideoCapture()
        {
            if (_nativeRecorder != null) return; // already running
            if (!RingBufferRecorder.IsSupported) return;

            _nativeRecorder = gameObject.AddComponent<RingBufferRecorder>();

            int sw = Screen.width, sh = Screen.height;
            float scale = videoMaxDimension > 0
                ? Mathf.Min(1f, (float)videoMaxDimension / Mathf.Min(sw, sh))
                : 1f;
            int rw = Mathf.Max(16, Mathf.RoundToInt(sw * scale) & ~1);
            int rh = Mathf.Max(16, Mathf.RoundToInt(sh * scale) & ~1);

            _nativeRecorder.StartRecording(rw, rh, videoBitrate, videoFps, videoBufferSeconds);
            Debug.Log("[Bugpunch] Video ring buffer enabled");
        }

        void Update()
        {
            // Drain pending auto-report from the log callback (which may fire on a worker thread).
            var pending = _pendingAutoReport;
            if (pending != null)
            {
                _pendingAutoReport = null;
                StartReport(pending.title, pending.description, BugReportType.Crash);
                return;
            }

            // Desktop: F12 to report
            if (Input.GetKeyDown(reportKey))
            {
                if (_recordingModeActive)
                    OnRecordingReportTapped();
                else
                    StartReportFlow();
                return;
            }

            // Mobile: shake to report
            if (shakeToReport && Input.acceleration.sqrMagnitude > shakeThreshold * shakeThreshold)
            {
                if (!_recordingModeActive)
                    StartReportFlow();
            }
        }

        // Thread-safe clock for OnLog — Unity's Time.realtimeSinceStartup is
        // main-thread only, but log callbacks can fire on worker threads.
        static readonly Stopwatch _logClock = Stopwatch.StartNew();

        void OnLog(string message, string stackTrace, LogType type)
        {
            lock (_logBuffer)
            {
                if (_logBuffer.Count >= MAX_LOGS)
                    _logBuffer.RemoveAt(0);
                _logBuffer.Add(new LogEntry
                {
                    time = (float)_logClock.Elapsed.TotalSeconds,
                    message = message,
                    stackTrace = stackTrace,
                    type = type
                });
            }

            // Auto-trigger a bug report on uncaught exceptions. We run the actual
            // send on the main thread because this callback can fire on a worker
            // thread (logMessageReceivedThreaded).
            if (autoReportExceptions && type == LogType.Exception && !_reportDialogOpen)
            {
                var now = (float)_logClock.Elapsed.TotalSeconds;
                if (now - _lastAutoReportTime < autoReportCooldownSeconds) return;
                _lastAutoReportTime = now;

                // Marshal to the main thread via a pending flag — StartReport calls
                // StartCoroutine which is main-thread only.
                _pendingAutoReport = new PendingReport
                {
                    title = "Exception: " + (message ?? "").Substring(0, Mathf.Min(120, (message ?? "").Length)),
                    description = stackTrace ?? ""
                };
            }
        }

        // Set from the (possibly-background) log callback; consumed on the main thread in Update.
        volatile PendingReport _pendingAutoReport;
        class PendingReport { public string title; public string description; }

        // ── Report Flow (welcome → record → report) ──

        /// <summary>
        /// Start the full report flow: show the native welcome overlay, then enter
        /// recording mode with a floating report button. Stays active until app restart.
        /// </summary>
        public void StartReportFlow()
        {
            if (_reportDialogOpen || _recordingModeActive) return;

            var dialog = NativeDialogFactory.Create();
            dialog.ShowReportWelcome(
                onConfirm: () => StartRecordingReport(),
                onCancel: () => { }
            );
        }

        /// <summary>
        /// Enter recording mode directly (skip welcome). Starts the ring buffer and
        /// shows the floating report button. Stays active until app restart.
        /// </summary>
        public void StartRecordingReport()
        {
            if (_recordingModeActive) return;
            _recordingModeActive = true;

            // Start the ring buffer recorder
            EnableVideoCapture();

            // Show the native floating button
            var dialog = NativeDialogFactory.Create();
            dialog.ShowRecordingOverlay(onStopRecording: OnRecordingReportTapped);
        }

        void OnRecordingReportTapped()
        {
            // User tapped the floating report button — capture and send.
            // The overlay stays (recording mode persists), but we file the report.
            // After report completes, the button is still there for next time.
            StartReport();
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

            // Dump the native rolling-window video to an MP4 file.
            // On unsupported platforms or if the native recorder isn't running,
            // videoBytes will be null and the report just won't have video.
            byte[] videoBytes = null;
            if (_nativeRecorder != null && _nativeRecorder.IsRecording)
            {
                videoBytes = _nativeRecorder.DumpToBytes();
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
            report.Append($"\"fps\":{(Time.deltaTime > 0f ? Mathf.RoundToInt(1f / Time.deltaTime) : 0)},");
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

            // Native MP4 video (base64). Single blob instead of a frame array —
            // the server gets an actual H.264 MP4 it can store/play directly.
            report.Append($"\"videoFormat\":\"mp4\",");
            report.Append($"\"videoFps\":{videoFps},");
            if (videoBytes != null && videoBytes.Length > 0)
            {
                report.Append($"\"video\":\"{Convert.ToBase64String(videoBytes)}\"");
            }
            else
            {
                report.Append("\"video\":null");
            }

            report.Append("}");

            // POST to /api/reports/bug — works even when the device isn't on the
            // live tunnel (crash reports need to ship from prod players who have
            // no debug session open).
            var client = BugpunchClient.Instance;
            var serverUrl = client?.Config?.serverUrl;
            var apiKey = client?.Config?.apiKey;
            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(apiKey))
            {
                Debug.LogWarning("[Bugpunch] Can't send bug report — serverUrl/apiKey not configured");
                OnReportSent?.Invoke(false);
                _reportDialogOpen = false;
                yield break;
            }

            var url = HttpBase(serverUrl) + "/api/reports/bug";
            var body = System.Text.Encoding.UTF8.GetBytes(report.ToString());
            using (var req = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(body);
                req.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("X-Api-Key", apiKey);
                req.timeout = 60; // large video upload

                yield return req.SendWebRequest();

                var videoKb = videoBytes != null ? videoBytes.Length / 1024 : 0;
                if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[Bugpunch] Bug report sent ({videoKb}KB MP4, {screenshot.Length / 1024}KB screenshot, HTTP {req.responseCode})");
                    OnReportSent?.Invoke(true);
                }
                else
                {
                    Debug.LogWarning($"[Bugpunch] Bug report POST failed: {req.error} (HTTP {req.responseCode})");
                    // TODO: queue to disk and retry when back online
                    OnReportSent?.Invoke(false);
                }
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
            var serverUrl = client?.Config?.serverUrl;
            var apiKey = client?.Config?.apiKey;
            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(apiKey))
            {
                Debug.LogWarning("[Bugpunch] Can't send feedback — serverUrl/apiKey not configured");
                yield break;
            }

            var url = HttpBase(serverUrl) + "/api/reports/feedback";
            var body = System.Text.Encoding.UTF8.GetBytes(feedback.ToString());
            using (var req = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(body);
                req.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("X-Api-Key", apiKey);
                req.timeout = 20;
                yield return req.SendWebRequest();
                if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    Debug.Log("[Bugpunch] Feedback sent");
                else
                    Debug.LogWarning($"[Bugpunch] Feedback POST failed: {req.error}");
            }
        }

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t") ?? "";

        /// <summary>
        /// Normalize a configured serverUrl to an HTTP base URL.
        /// Config is typically a WebSocket URL (ws://… or wss://…) for the tunnel,
        /// but the bug report endpoint is plain HTTP.
        /// </summary>
        static string HttpBase(string serverUrl)
        {
            var trimmed = serverUrl.TrimEnd('/');
            if (trimmed.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
                return "https://" + trimmed.Substring("wss://".Length);
            if (trimmed.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
                return "http://" + trimmed.Substring("ws://".Length);
            return trimmed;
        }
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
