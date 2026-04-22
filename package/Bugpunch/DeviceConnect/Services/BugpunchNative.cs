using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Thin C# bridge to the native Bugpunch runtime.
    ///
    /// All real work lives in native (crash handlers, screenshot, log capture,
    /// shake detection, upload queue). C# just calls <see cref="Start"/> once
    /// at init, pushes scene/fps updates, forwards managed exceptions, and
    /// relays game-initiated bug reports.
    ///
    /// On Android the always-on machinery (crash / ANR / exception / bug
    /// report / analytics / traces / perf) is served by
    /// <c>au.com.oddgames.bugpunch.BugpunchRuntime</c>; the separate
    /// <c>BugpunchDebugMode</c> class handles opt-in video recording only
    /// (consent sheet + MediaProjection + dump). iOS still routes through
    /// the single <c>Bugpunch_*</c> P-Invoke surface — its split is a
    /// future follow-up.
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
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchRuntime");
                s_started = cls.CallStatic<bool>("start", activity, json);
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
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchReportingService");
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
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchRuntime");
                cls.CallStatic("setCustomData", key, value);
            }
            catch { }
#elif UNITY_IOS
            try { Bugpunch_SetCustomData(key, value); } catch { }
#endif
        }

        // ── N6: native tunnel state accessors ──
        // The WebSocket lifecycle lives natively. These accessors let the
        // managed side query connection state + the persistent deviceId
        // without maintaining its own tunnel. Editor returns sensible
        // defaults since the managed TunnelClient still runs there.

        public static bool TunnelIsConnected()
        {
#if UNITY_EDITOR
            return false;
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchTunnel");
                return cls.CallStatic<bool>("isConnected");
            }
            catch { return false; }
#elif UNITY_IOS
            try { return Bugpunch_TunnelIsConnected(); }
            catch { return false; }
#else
            return false;
#endif
        }

        public static string TunnelDeviceId()
        {
#if UNITY_EDITOR
            return "";
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchTunnel");
                return cls.CallStatic<string>("getDeviceId") ?? "";
            }
            catch { return ""; }
#elif UNITY_IOS
            try { return Bugpunch_TunnelDeviceId() ?? ""; }
            catch { return ""; }
#else
            return "";
#endif
        }

        // ── N4: native pin state accessors ──
        // Pin state lives natively (SharedPreferences on Android, Keychain on
        // iOS) and only applies when consent == "accepted". These accessors
        // are the only way C# reads pin state on device; PinState delegates
        // through here.

        public static bool PinAlwaysLog()
        {
#if UNITY_EDITOR
            return false;
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchTunnel");
                return cls.CallStatic<bool>("isAlwaysLog");
            }
            catch { return false; }
#elif UNITY_IOS
            try { return Bugpunch_PinAlwaysLog() != 0; }
            catch { return false; }
#else
            return false;
#endif
        }

        public static bool PinAlwaysRemote()
        {
#if UNITY_EDITOR
            return false;
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchTunnel");
                return cls.CallStatic<bool>("isAlwaysRemote");
            }
            catch { return false; }
#elif UNITY_IOS
            try { return Bugpunch_PinAlwaysRemote() != 0; }
            catch { return false; }
#else
            return false;
#endif
        }

        public static bool PinAlwaysDebug()
        {
#if UNITY_EDITOR
            return false;
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchTunnel");
                return cls.CallStatic<bool>("isAlwaysDebug");
            }
            catch { return false; }
#elif UNITY_IOS
            try { return Bugpunch_PinAlwaysDebug() != 0; }
            catch { return false; }
#else
            return false;
#endif
        }

        public static string PinConsent()
        {
#if UNITY_EDITOR
            return "unknown";
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchTunnel");
                return cls.CallStatic<string>("getConsent") ?? "unknown";
            }
            catch { return "unknown"; }
#elif UNITY_IOS
            try { return Bugpunch_PinConsent() ?? "unknown"; }
            catch { return "unknown"; }
#else
            return "unknown";
#endif
        }

        /// <summary>
        /// Ship a tunnel response frame through the native WebSocket (N3
        /// dispatch bridge). C# routes incoming Remote IDE requests through
        /// the existing <c>RequestRouter</c> and then hands the pre-built
        /// response envelope to native via this method, so every transport
        /// hop after here is native-owned. No-op in the Editor — the C#
        /// <c>TunnelClient</c> still carries local-dev traffic there.
        /// </summary>
        public static void TunnelSendResponse(string responseJson)
        {
            if (string.IsNullOrEmpty(responseJson)) return;
#if UNITY_EDITOR
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchTunnel");
                cls.CallStatic("sendResponse", responseJson);
            }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] TunnelSendResponse failed: {e.Message}"); }
#elif UNITY_IOS
            try { Bugpunch_TunnelSendResponse(responseJson); }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] TunnelSendResponse failed: {e.Message}"); }
#endif
        }

        /// <summary>
        /// Returns the reinstall-surviving stable device id for pin enrollment
        /// (Keychain UUID on iOS, ANDROID_ID on Android). Empty string in the
        /// Editor or if native can't provide one.
        /// </summary>
        public static string GetStableDeviceId()
        {
#if UNITY_EDITOR
            return "";
#elif UNITY_ANDROID
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchIdentity");
                return cls.CallStatic<string>("getStableDeviceId", activity) ?? "";
            }
            catch { return ""; }
#elif UNITY_IOS
            try
            {
                var ptr = Bugpunch_GetStableDeviceId();
                return ptr ?? "";
            }
            catch { return ""; }
#else
            return "";
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
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchRuntime");
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
                cls.CallStatic("enter", activity, skipConsent);
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
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchReportingService");
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
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchReportingService");
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
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchRuntime");
                cls.CallStatic("updateScene", scene ?? "");
            }
            catch { }
#elif UNITY_IOS
            try { Bugpunch_UpdateScene(scene ?? ""); } catch { }
#endif
        }

        /// <summary>
        /// Enqueue a custom product-analytics event. Native holds an
        /// in-memory ring buffer and flushes in batches to
        /// /api/v1/analytics/events. Fire-and-forget; cheap.
        /// </summary>
        public static void TrackEvent(string name, string propertiesJson)
        {
            if (!s_started || string.IsNullOrEmpty(name)) return;
#if UNITY_EDITOR
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchRuntime");
                cls.CallStatic("trackEvent", name, propertiesJson);
            }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] TrackEvent failed: {e.Message}"); }
#elif UNITY_IOS
            try { Bugpunch_TrackEvent(name, propertiesJson); }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] TrackEvent failed: {e.Message}"); }
#endif
        }

        // Input breadcrumb event types — must match BugpunchInput.java.
        public const int INPUT_TOUCH_DOWN = 0;
        public const int INPUT_TOUCH_UP = 1;
        public const int INPUT_TOUCH_MOVE = 2;
        public const int INPUT_KEY_DOWN = 3;
        public const int INPUT_KEY_UP = 4;
        public const int INPUT_SCENE_CHANGE = 5;
        public const int INPUT_CUSTOM = 6;

        /// <summary>
        /// Push one pointer event into the native breadcrumb ring. Called from
        /// the early-execution capture component on tap-down / tap-up. The
        /// native ring survives a Mono/IL2CPP meltdown because it's a direct
        /// ByteBuffer outside the managed heap.
        ///
        /// <paramref name="label"/> is the visible label on / around the
        /// tapped element — the Button's TMP_Text child, a Toggle's label,
        /// etc. — optionally prefixed with the component type (e.g.
        /// "Button: Buy Now"). Empty when there's no text to display.
        /// </summary>
        public static void PushInputTouch(int type, float x, float y, string path, string scene, string label)
        {
            if (!s_started) return;
            long t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
#if UNITY_EDITOR
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchInput");
                cls.CallStatic("pushTouch", type, t, x, y, path ?? "", scene ?? "", label ?? "");
            }
            catch { }
#elif UNITY_IOS
            // iOS parity — implement Bugpunch_PushInputTouch when we port.
#endif
        }

        public static void PushInputKey(int type, int keyCode, string scene)
        {
            if (!s_started) return;
            long t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
#if UNITY_EDITOR
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchInput");
                cls.CallStatic("pushKey", type, t, keyCode, scene ?? "");
            }
            catch { }
#endif
        }

        public static void PushInputSceneChange(string scene)
        {
            if (!s_started) return;
            long t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
#if UNITY_EDITOR
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchInput");
                cls.CallStatic("pushSceneChange", t, scene ?? "");
            }
            catch { }
#endif
        }

        /// <summary>
        /// Push a game-authored custom breadcrumb into the native ring.
        /// Called by <see cref="Bugpunch.Breadcrumb"/>.
        /// </summary>
        public static void PushInputCustom(string category, string message)
        {
            if (!s_started) return;
            long t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
#if UNITY_EDITOR
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchInput");
                cls.CallStatic("pushCustom", t, category ?? "", message ?? "", scene ?? "");
            }
            catch { }
#endif
        }

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

        /// <summary>
        /// Callback from <see cref="CrashDirectiveHandler"/> back into native after
        /// the script finishes. Native posts the result to the server's
        /// <c>POST /api/crashes/events/:id/enrich</c> endpoint via its upload
        /// queue, keyed by the pending directive + event ids stored when the
        /// directive match was received.
        /// (Distinct from <c>PostScriptResult</c> which serves the poll-scheduled
        /// scripts flow.)
        /// </summary>
        public static void PostDirectiveResult(string directiveId, string resultJson)
        {
            if (!s_started || string.IsNullOrEmpty(directiveId)) return;
#if UNITY_EDITOR
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchRuntime");
                cls.CallStatic("postDirectiveResult", directiveId, resultJson ?? "");
            }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] PostDirectiveResult failed: {e.Message}"); }
#elif UNITY_IOS
            try { Bugpunch_PostDirectiveResult(directiveId, resultJson ?? ""); }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] PostDirectiveResult failed: {e.Message}"); }
#endif
        }

        // ─── Native poll loop (replaces the C# DeviceRegistration) ──────────
        //
        // Owns register + heartbeat poll + directive dispatch + token
        // persistence. C# receives two callbacks via UnitySendMessage:
        //   BugpunchClient.OnPollUpgradeRequested  — server asked for a
        //     live tunnel; C# starts the WebSocket client.
        //   BugpunchClient.OnPollScripts           — scheduled script(s)
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
        /// Hand a poll response's pendingDirectives array to native for
        /// execution. JSON shape: [ { "id": "...", "actions": [...] }, ... ].
        /// Native runs the same action handlers as the crash path; results
        /// are POSTed to /api/directives/{id}/result (no eventId).
        /// </summary>
        public static void ProcessPollDirectives(string pendingDirectivesJson)
        {
            if (!s_started || string.IsNullOrEmpty(pendingDirectivesJson)) return;
#if UNITY_EDITOR
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchDirectives");
                cls.CallStatic("onPollDirectives", pendingDirectivesJson);
            }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] ProcessPollDirectives failed: {e.Message}"); }
#elif UNITY_IOS
            try { BPDirectives_OnPollDirectives(pendingDirectivesJson); }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch] ProcessPollDirectives failed: {e.Message}"); }
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
        [DllImport("__Internal")] static extern string Bugpunch_GetStableDeviceId();
        [DllImport("__Internal")] static extern void Bugpunch_TunnelSendResponse(string responseJson);
        [DllImport("__Internal")] static extern bool Bugpunch_TunnelIsConnected();
        [DllImport("__Internal")] static extern string Bugpunch_TunnelDeviceId();
        [DllImport("__Internal")] static extern int Bugpunch_PinAlwaysLog();
        [DllImport("__Internal")] static extern int Bugpunch_PinAlwaysRemote();
        [DllImport("__Internal")] static extern int Bugpunch_PinAlwaysDebug();
        [DllImport("__Internal")] static extern string Bugpunch_PinConsent();
        [DllImport("__Internal")] static extern bool Bugpunch_StartDebugMode(string configJson);
        [DllImport("__Internal")] static extern void Bugpunch_StopDebugMode();
        [DllImport("__Internal")] static extern void Bugpunch_ReportBug(string type,
            string title, string description, string extraJson);
        [DllImport("__Internal")] static extern void Bugpunch_SetCustomData(string key, string value);
        [DllImport("__Internal")] static extern void Bugpunch_UpdateScene(string scene);
        [DllImport("__Internal")] static extern void Bugpunch_EnterDebugMode(int skipConsent);
        [DllImport("__Internal")] static extern void Bugpunch_Trace(string label, string tagsJson);
        [DllImport("__Internal")] static extern void Bugpunch_TrackEvent(string name, string propertiesJson);
        [DllImport("__Internal")] static extern void Bugpunch_TraceScreenshot(string label, string tagsJson);
        [DllImport("__Internal")] static extern void Bugpunch_PostDirectiveResult(string directiveId, string resultJson);
        [DllImport("__Internal")] static extern void BPDirectives_OnPollDirectives(string pendingDirectivesJson);
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
            Field(sb, "appVersion",   Application.version);               sb.Append(',');
            Field(sb, "bundleId",     Application.identifier);            sb.Append(',');
            Field(sb, "unityVersion", Application.unityVersion);          sb.Append(',');
            Field(sb, "deviceModel",  SystemInfo.deviceModel);            sb.Append(',');
            Field(sb, "osVersion",    SystemInfo.operatingSystem);        sb.Append(',');
            Field(sb, "deviceId",     SystemInfo.deviceUniqueIdentifier); sb.Append(',');
            Field(sb, "gpu",          SystemInfo.graphicsDeviceName);     sb.Append(',');
            // Release labels — surfaces in Issues/Performance filters + groupBy so
            // you can slice by staging/beta/prod branch or by specific build SHA.
            // Empty strings are fine; native defaults to "" via getOrDefault.
            Field(sb, "branch",       BugpunchClient.GetEffectiveBranch(c));       sb.Append(',');
            Field(sb, "changeset",    BugpunchClient.GetEffectiveChangeset(c));
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
