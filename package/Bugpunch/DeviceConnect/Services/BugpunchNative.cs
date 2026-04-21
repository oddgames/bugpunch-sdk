using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Thin C# bridge to the native Bugpunch debug-mode coordinator.
    ///
    /// All real work lives in native (crash handlers, screenshot, log capture,
    /// shake detection, upload queue). C# just calls StartDebugMode once at
    /// init, pushes scene/fps updates, forwards managed exceptions, and
    /// relays game-initiated bug reports.
    ///
    /// In the Unity Editor everything is a no-op except <see cref="ReportBug"/>,
    /// which falls back to an in-process UnityWebRequest POST for round-trip
    /// testing against a dev server.
    /// </summary>
    public static class BugpunchNative
    {
        static bool s_started;

        public static bool Start(BugpunchConfig config)
        {
            if (s_started || config == null) return s_started;

            var json = BuildConfigJson(config);
#if UNITY_EDITOR
            s_started = true;
            return true;
#elif UNITY_ANDROID
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchDebugMode");
                s_started = cls.CallStatic<bool>("startDebugMode", activity, json);
            }
            catch (Exception e) { Debug.LogError($"[Bugpunch] Android start failed: {e.Message}"); }
            return s_started;
#elif UNITY_IOS
            try { s_started = Bugpunch_StartDebugMode(json); }
            catch (Exception e) { Debug.LogError($"[Bugpunch] iOS start failed: {e.Message}"); }
            return s_started;
#else
            s_started = true;
            return true;
#endif
        }

        /// <summary>
        /// Request a bug report. For <c>type == "bug"</c> native shows a form
        /// (screenshot preview, email, description, severity, tap-to-annotate);
        /// for <c>exception</c>/<c>crash</c>/<c>feedback</c> it silently
        /// enqueues.
        /// </summary>
        public static void ReportBug(string type, string title, string description,
            string extraJson = null)
        {
            if (!s_started) return;
#if UNITY_EDITOR
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchDebugMode");
                cls.CallStatic("reportBug", type ?? "bug", title ?? "", description ?? "", extraJson ?? "");
            }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] reportBug failed: {e.Message}"); }
#elif UNITY_IOS
            try { Bugpunch_ReportBug(type ?? "bug", title ?? "", description ?? "", extraJson ?? ""); }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] reportBug failed: {e.Message}"); }
#endif
        }

        public static void SetCustomData(string key, string value)
        {
            if (!s_started || string.IsNullOrEmpty(key)) return;
#if UNITY_EDITOR
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchDebugMode");
                cls.CallStatic("setCustomData", key, value);
            }
            catch { }
#elif UNITY_IOS
            try { Bugpunch_SetCustomData(key, value); } catch { }
#endif
        }

        /// <summary>
        /// Returns the installer mode detected by native at startup:
        /// "store", "testflight", "sideload", or "unknown".
        /// </summary>
        public static string GetInstallerMode()
        {
#if UNITY_EDITOR
            return "editor";
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchDebugMode");
                return cls.CallStatic<string>("getMetadata", "installerMode") ?? "unknown";
            }
            catch { return "unknown"; }
#elif UNITY_IOS
            try { return Bugpunch_GetInstallerMode() ?? "unknown"; }
            catch { return "unknown"; }
#else
            return "unknown";
#endif
        }

        /// <summary>
        /// Prompt the user with a native consent sheet for debug recording. On
        /// accept, the native ring buffer starts (on Android this chains to the
        /// MediaProjection system dialog); on cancel nothing happens. No-op if
        /// recording is already active.
        /// </summary>
        public static void EnterDebugMode(bool skipConsent = false)
        {
            if (!s_started) return;
#if UNITY_EDITOR
#elif UNITY_ANDROID
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchDebugMode");
                cls.CallStatic("enterDebugMode", activity, skipConsent);
            }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] enterDebugMode failed: {e.Message}"); }
#elif UNITY_IOS
            try { Bugpunch_EnterDebugMode(skipConsent ? 1 : 0); }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] enterDebugMode failed: {e.Message}"); }
#endif
        }

        public static void Trace(string label, string tagsJson)
        {
            if (!s_started || string.IsNullOrEmpty(label)) return;
#if UNITY_EDITOR
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchDebugMode");
                cls.CallStatic("addTrace", label, tagsJson);
            }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] Trace failed: {e.Message}"); }
#elif UNITY_IOS
            try { Bugpunch_Trace(label, tagsJson); }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] Trace failed: {e.Message}"); }
#endif
        }

        public static void TraceScreenshot(string label, string tagsJson)
        {
            if (!s_started || string.IsNullOrEmpty(label)) return;
#if UNITY_EDITOR
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchDebugMode");
                cls.CallStatic("addTraceScreenshot", label, tagsJson);
            }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] TraceScreenshot failed: {e.Message}"); }
#elif UNITY_IOS
            try { Bugpunch_TraceScreenshot(label, tagsJson); }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] TraceScreenshot failed: {e.Message}"); }
#endif
        }

        public static void UpdateScene(string scene)
        {
            if (!s_started) return;
#if UNITY_EDITOR
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchDebugMode");
                cls.CallStatic("updateScene", scene ?? "");
            }
            catch { }
#elif UNITY_IOS
            try { Bugpunch_UpdateScene(scene ?? ""); } catch { }
#endif
        }

        /// <summary>
        /// Callback from <see cref="CrashDirectiveHandler"/> back into native
        /// after PaxScript finishes. Native posts the result to the server's
        /// <c>POST /api/crashes/events/:id/enrich</c> endpoint via its upload
        /// queue, keyed by the pending directive + event ids stored when the
        /// directive match was received.
        /// </summary>
        /// <summary>
        /// Push a Unity log entry into the native log buffer. iOS only — on
        /// Android, Unity writes to logcat which the native reader already tails.
        /// </summary>
        public static void PushLogEntry(string type, string message, string stackTrace)
        {
            if (!s_started) return;
#if !UNITY_EDITOR && UNITY_IOS
            try { Bugpunch_PushLogEntry(type ?? "Log", message ?? "", stackTrace ?? ""); }
            catch { }
#endif
        }

        public static void PostPaxScriptResult(string directiveId, string resultJson)
        {
            if (!s_started || string.IsNullOrEmpty(directiveId)) return;
#if UNITY_EDITOR
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchDebugMode");
                cls.CallStatic("postPaxScriptResult", directiveId, resultJson ?? "");
            }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] PostPaxScriptResult failed: {e.Message}"); }
#elif UNITY_IOS
            try { Bugpunch_PostPaxScriptResult(directiveId, resultJson ?? ""); }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] PostPaxScriptResult failed: {e.Message}"); }
#endif
        }

        // ─── Native poll loop (replaces the C# DeviceRegistration) ──────────
        //
        // Owns register + heartbeat poll + directive dispatch + token
        // persistence. C# receives two callbacks via UnitySendMessage:
        //   BugpunchClient.OnPollUpgradeRequested  — server asked for a
        //     live tunnel; C# starts the WebSocket client.
        //   BugpunchClient.OnPollScripts           — scheduled PaxScript(s)
        //     to execute against managed code; result posted back via
        //     PostScriptResult.

        /// <summary>
        /// Start the native poll loop. Must be called AFTER
        /// <see cref="Start"/> so the native runtime has a populated config
        /// (serverUrl / apiKey / metadata).
        /// </summary>
        public static void StartPoll(string scriptPermission, int pollIntervalSeconds)
        {
            if (!s_started) return;
            string perm = string.IsNullOrEmpty(scriptPermission) ? "ask" : scriptPermission;
            int interval = Math.Max(5, pollIntervalSeconds);
#if UNITY_EDITOR
            Debug.Log("[Bugpunch] StartPoll: no-op in Editor");
#elif UNITY_ANDROID
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchPoller");
                cls.CallStatic("start", activity, perm, interval);
            }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] StartPoll failed: {e.Message}"); }
#elif UNITY_IOS
            try { Bugpunch_StartPoll(perm, interval); }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] StartPoll failed: {e.Message}"); }
#endif
        }

        public static void StopPoll()
        {
#if UNITY_EDITOR
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchPoller");
                cls.CallStatic("stop");
            }
            catch { /* best-effort */ }
#elif UNITY_IOS
            try { Bugpunch_StopPoll(); } catch { /* best-effort */ }
#endif
        }

        /// <summary>
        /// Post a scheduled-script execution result back to the server. Called
        /// from <see cref="BugpunchClient.OnPollScripts"/> after the C# runner
        /// finishes. Native handles the HTTP so C# doesn't touch the network.
        /// </summary>
        public static void PostScriptResult(string scheduledScriptId, string output,
            string errors, bool success, int durationMs)
        {
            if (!s_started || string.IsNullOrEmpty(scheduledScriptId)) return;
#if UNITY_EDITOR
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchPoller");
                cls.CallStatic("postScriptResult", scheduledScriptId, output ?? "",
                    errors ?? "", success, durationMs);
            }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] PostScriptResult failed: {e.Message}"); }
#elif UNITY_IOS
            try { Bugpunch_PostScriptResult(scheduledScriptId, output ?? "", errors ?? "", success, durationMs); }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] PostScriptResult failed: {e.Message}"); }
#endif
        }

        /// <summary>
        /// Start the native performance monitor with server-provided config.
        /// Called after the game config fetch succeeds and performance.enabled
        /// is true. The native monitor runs on a background thread, sampling
        /// FPS and memory every 1s. It fires perf events on memory pressure
        /// or sustained low FPS.
        /// </summary>
        public static void StartPerfMonitor(string configJson)
        {
            if (!s_started || string.IsNullOrEmpty(configJson)) return;
#if UNITY_EDITOR
            Debug.Log("[Bugpunch] Perf monitor: no-op in Editor");
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchPerfMonitor");
                cls.CallStatic("start", configJson);
            }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] StartPerfMonitor failed: {e.Message}"); }
#elif UNITY_IOS
            try { Bugpunch_StartPerfMonitor(configJson); }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] StartPerfMonitor failed: {e.Message}"); }
#endif
        }

#if !UNITY_EDITOR && UNITY_IOS
        [DllImport("__Internal")] static extern bool Bugpunch_StartDebugMode(string configJson);
        [DllImport("__Internal")] static extern void Bugpunch_StopDebugMode();
        [DllImport("__Internal")] static extern void Bugpunch_ReportBug(string type,
            string title, string description, string extraJson);
        [DllImport("__Internal")] static extern void Bugpunch_SetCustomData(string key, string value);
        [DllImport("__Internal")] static extern void Bugpunch_UpdateScene(string scene);
        [DllImport("__Internal")] static extern void Bugpunch_EnterDebugMode(int skipConsent);
        [DllImport("__Internal")] static extern void Bugpunch_Trace(string label, string tagsJson);
        [DllImport("__Internal")] static extern void Bugpunch_TraceScreenshot(string label, string tagsJson);
        [DllImport("__Internal")] static extern void Bugpunch_PostPaxScriptResult(string directiveId, string resultJson);
        [DllImport("__Internal")] static extern void Bugpunch_PushLogEntry(string type, string message, string stackTrace);
        [DllImport("__Internal")] static extern void Bugpunch_StartPerfMonitor(string configJson);
        [DllImport("__Internal")] static extern string Bugpunch_GetInstallerMode();
        [DllImport("__Internal")] static extern void Bugpunch_StartPoll(string scriptPermission, int pollIntervalSeconds);
        [DllImport("__Internal")] static extern void Bugpunch_StopPoll();
        [DllImport("__Internal")] static extern void Bugpunch_PostScriptResult(string scheduledScriptId,
            string output, string errors, bool success, int durationMs);
#endif

        // ── Build config JSON from BugpunchConfig ScriptableObject ──

        /// Device tier: "high" (6GB+, 6+ cores), "mid" (3-6GB, 4+ cores), "low" (rest).
        /// Used to scale buffer sizes, video quality, and screenshot frequency.
        internal static string DeviceTier { get; private set; } = "mid";

        static void DetectDeviceTier()
        {
            int memMb = SystemInfo.systemMemorySize;  // total RAM in MB
            int cores = SystemInfo.processorCount;
            DeviceTier = (memMb >= 6144 && cores >= 6) ? "high"
                       : (memMb >= 3072 && cores >= 4) ? "mid" : "low";
            Debug.Log($"[Bugpunch] Device tier: {DeviceTier} (RAM={memMb}MB, cores={cores})");
        }

        static string BuildConfigJson(BugpunchConfig c)
        {
            DetectDeviceTier();

            // Tier-based defaults — config ScriptableObject values override these
            // only if they differ from the hardcoded defaults (meaning the dev
            // intentionally set them).
            int logBuffer = DeviceTier switch { "low" => 500, "mid" => 2000, _ => 5000 };
            int videoBuffer = c.videoBufferSeconds != 30 ? c.videoBufferSeconds
                : DeviceTier switch { "low" => 10, "mid" => 20, _ => 30 };
            int videoFps = c.bugReportVideoFps != 10 ? c.bugReportVideoFps
                : DeviceTier switch { "low" => 10, "mid" => 15, _ => 30 };
            int videoBitrate = DeviceTier switch { "low" => 1_000_000, "mid" => 1_500_000, _ => 2_000_000 };

            var sb = new StringBuilder(512);
            sb.Append('{');
            Field(sb, "serverUrl", HttpBase(c.serverUrl)); sb.Append(',');
            Field(sb, "apiKey",    c.apiKey);              sb.Append(',');
            Field(sb, "deviceTier", DeviceTier);           sb.Append(',');
            sb.Append("\"anrTimeoutMs\":").Append(c.anrTimeoutMs).Append(',');
            sb.Append("\"logBufferSize\":").Append(logBuffer).Append(',');
            sb.Append("\"autoReportCooldownSeconds\":30,");
            sb.Append("\"shake\":{\"enabled\":false,\"threshold\":2.5},");
            sb.Append("\"video\":{\"enabled\":true,\"bufferSeconds\":")
                .Append(videoBuffer).Append(",\"fps\":")
                .Append(videoFps).Append(",\"bitrate\":")
                .Append(videoBitrate).Append("},");
            sb.Append("\"metadata\":{");
            Field(sb, "appVersion",   Application.version);           sb.Append(',');
            Field(sb, "bundleId",     Application.identifier);        sb.Append(',');
            Field(sb, "unityVersion", Application.unityVersion);      sb.Append(',');
            Field(sb, "deviceModel",  SystemInfo.deviceModel);        sb.Append(',');
            Field(sb, "osVersion",    SystemInfo.operatingSystem);    sb.Append(',');
            Field(sb, "gpu",          SystemInfo.graphicsDeviceName);
            sb.Append("},");

            // Attachment rules: game-declared allow-list of files Bugpunch may
            // upload alongside a crash report. Native resolves the glob at
            // upload time (not signal-handler time). Tokens are pre-resolved
            // here so native never has to know about Unity paths.
            sb.Append("\"attachmentRules\":[");
            var rules = BugpunchClient.GetEffectiveAttachmentRules(c);
            bool firstRule = true;
            foreach (var r in rules)
            {
                if (r == null || string.IsNullOrEmpty(r.path)) continue;
                var resolved = BugpunchConfig.ResolvePathToken(r.path);
                if (resolved == null || r.path.Contains("..")) continue;
                if (!firstRule) sb.Append(',');
                firstRule = false;
                sb.Append('{');
                Field(sb, "name", r.name ?? "rule"); sb.Append(',');
                Field(sb, "rawPath", r.path);        sb.Append(',');
                Field(sb, "path", resolved);         sb.Append(',');
                Field(sb, "pattern", string.IsNullOrEmpty(r.pattern) ? "*" : r.pattern);
                sb.Append(",\"maxBytes\":").Append(r.maxBytes);
                sb.Append('}');
            }
            sb.Append(']');

            sb.Append('}');
            return sb.ToString();
        }

        static void Field(StringBuilder sb, string k, string v)
        {
            sb.Append('"').Append(Esc(k)).Append("\":\"").Append(Esc(v)).Append('"');
        }

        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:X4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        static string HttpBase(string serverUrl)
        {
            if (string.IsNullOrEmpty(serverUrl)) return "";
            var trimmed = serverUrl.TrimEnd('/');
            if (trimmed.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
                return "https://" + trimmed.Substring("wss://".Length);
            if (trimmed.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
                return "http://" + trimmed.Substring("ws://".Length);
            return trimmed;
        }
    }

    /// <summary>
    /// Pushes the active Unity scene name to native on change, and forwards
    /// Unity log messages to the native log buffer on iOS (Android's logcat
    /// reader already captures them). Added by BugpunchClient after
    /// <see cref="BugpunchNative.Start"/>.
    /// </summary>
    public class BugpunchSceneTick : MonoBehaviour
    {
        void OnEnable()
        {
            SceneManager.activeSceneChanged += OnSceneChanged;
            BugpunchNative.UpdateScene(SceneManager.GetActiveScene().name);
#if !UNITY_EDITOR && UNITY_IOS
            Application.logMessageReceivedThreaded += OnLog;
#endif
        }
        void OnDisable()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
#if !UNITY_EDITOR && UNITY_IOS
            Application.logMessageReceivedThreaded -= OnLog;
#endif
        }

        void OnSceneChanged(Scene _, Scene next) => BugpunchNative.UpdateScene(next.name);

#if !UNITY_EDITOR && UNITY_IOS
        static void OnLog(string condition, string stackTrace, LogType type)
        {
            string nativeType;
            switch (type)
            {
                case LogType.Error:
                case LogType.Assert:
                    nativeType = "Error"; break;
                case LogType.Warning:
                    nativeType = "Warning"; break;
                case LogType.Exception:
                    nativeType = "Error"; break;
                default:
                    nativeType = "Log"; break;
            }
            BugpunchNative.PushLogEntry(nativeType, condition ?? "", stackTrace ?? "");
        }
#endif
    }
}
