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

#if !UNITY_EDITOR && UNITY_IOS
        [DllImport("__Internal")] static extern bool Bugpunch_StartDebugMode(string configJson);
        [DllImport("__Internal")] static extern void Bugpunch_StopDebugMode();
        [DllImport("__Internal")] static extern void Bugpunch_ReportBug(string type,
            string title, string description, string extraJson);
        [DllImport("__Internal")] static extern void Bugpunch_SetCustomData(string key, string value);
        [DllImport("__Internal")] static extern void Bugpunch_UpdateScene(string scene);
        [DllImport("__Internal")] static extern void Bugpunch_EnterDebugMode(int skipConsent);
#endif

        // ── Build config JSON from BugpunchConfig ScriptableObject ──

        static string BuildConfigJson(BugpunchConfig c)
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            Field(sb, "serverUrl", HttpBase(c.serverUrl)); sb.Append(',');
            Field(sb, "apiKey",    c.apiKey);              sb.Append(',');
            sb.Append("\"anrTimeoutMs\":").Append(c.anrTimeoutMs).Append(',');
            sb.Append("\"logBufferSize\":500,");
            sb.Append("\"autoReportCooldownSeconds\":30,");
            sb.Append("\"shake\":{\"enabled\":false,\"threshold\":2.5},");
            sb.Append("\"video\":{\"enabled\":true,\"bufferSeconds\":")
                .Append(c.videoBufferSeconds).Append(",\"fps\":")
                .Append(c.bugReportVideoFps).Append("},");
            sb.Append("\"metadata\":{");
            Field(sb, "appVersion",   Application.version);           sb.Append(',');
            Field(sb, "bundleId",     Application.identifier);        sb.Append(',');
            Field(sb, "unityVersion", Application.unityVersion);      sb.Append(',');
            Field(sb, "deviceModel",  SystemInfo.deviceModel);        sb.Append(',');
            Field(sb, "osVersion",    SystemInfo.operatingSystem);    sb.Append(',');
            Field(sb, "gpu",          SystemInfo.graphicsDeviceName);
            sb.Append('}');
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
    /// Pushes the active Unity scene name to native on change. The only piece
    /// of runtime state that can't be derived on the native side. Added by
    /// BugpunchClient after <see cref="BugpunchNative.Start"/>.
    /// </summary>
    public class BugpunchSceneTick : MonoBehaviour
    {
        void OnEnable()
        {
            SceneManager.activeSceneChanged += OnSceneChanged;
            BugpunchNative.UpdateScene(SceneManager.GetActiveScene().name);
        }
        void OnDisable() => SceneManager.activeSceneChanged -= OnSceneChanged;

        void OnSceneChanged(Scene _, Scene next) => BugpunchNative.UpdateScene(next.name);
    }
}
