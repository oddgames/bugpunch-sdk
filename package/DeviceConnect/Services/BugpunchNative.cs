using System;
using System.Collections;
using System.Collections.Generic;
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

#if !UNITY_EDITOR && UNITY_ANDROID
        // ── Cached AndroidJavaClass refs ──
        // Each `new AndroidJavaClass(name)` allocates a JNI global ref that has
        // to be released — doing that on every call costs an attach/detach pair
        // per call. We cache the wrappers for the lifetime of the process.
        // Caller threads can include AppDomain.UnhandledException / TaskScheduler
        // (background threads), so dictionary access is locked.
        static readonly Dictionary<string, AndroidJavaClass> s_javaClasses = new();

        static AndroidJavaClass JavaClass(string name)
        {
            lock (s_javaClasses)
            {
                if (!s_javaClasses.TryGetValue(name, out var cls))
                {
                    cls = new AndroidJavaClass(name);
                    s_javaClasses[name] = cls;
                }
                return cls;
            }
        }

        // UnityPlayer is cached; the activity instance is NOT (Unity may swap
        // it across activity recreation, so we resolve fresh every call).
        static AndroidJavaObject CurrentActivity()
            => JavaClass("com.unity3d.player.UnityPlayer").GetStatic<AndroidJavaObject>("currentActivity");

        static void CallStatic(string cls, string method, string sourceLabel, params object[] args)
        {
            try { JavaClass(cls).CallStatic(method, args); }
            catch (Exception e) { ReportSdkError(sourceLabel, e); }
        }

        // Silent variant — used where the original code had `catch { }` (best-
        // effort fire-and-forget paths that mustn't recurse into ReportSdkError).
        static void CallStaticSilent(string cls, string method, params object[] args)
        {
            try { JavaClass(cls).CallStatic(method, args); }
            catch { /* best-effort */ }
        }

        static T CallStatic<T>(string cls, string method, T fallback, string sourceLabel, params object[] args)
        {
            try { return JavaClass(cls).CallStatic<T>(method, args); }
            catch (Exception e) { ReportSdkError(sourceLabel, e); return fallback; }
        }

        static T CallStaticSilent<T>(string cls, string method, T fallback, params object[] args)
        {
            try { return JavaClass(cls).CallStatic<T>(method, args); }
            catch { return fallback; }
        }
#endif

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
                s_started = JavaClass("au.com.oddgames.bugpunch.BugpunchRuntime")
                    .CallStatic<bool>("start", CurrentActivity(), json);
            }
            catch (Exception e) { ReportSdkError("BugpunchNative.Start", e); }
            return s_started;
#elif UNITY_IOS
            try
            {
                s_started = Bugpunch_StartDebugMode(json);
                if (s_started) Bugpunch_StartTunnel(json);
            }
            catch (Exception e) { ReportSdkError("BugpunchNative.Start", e); }
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
            CallStatic("au.com.oddgames.bugpunch.BugpunchReportingService", "reportBug",
                "BugpunchNative.ReportBug",
                type ?? "bug", title ?? "", description ?? "", extraJson ?? "");
#elif UNITY_IOS
            try { Bugpunch_ReportBug(type ?? "bug", title ?? "", description ?? "", extraJson ?? ""); }
            catch (Exception e) { ReportSdkError("BugpunchNative.ReportBug", e); }
#endif
        }

        public static void SetCustomData(string key, string value)
        {
            if (!s_started || string.IsNullOrEmpty(key)) return;
#if UNITY_EDITOR
#elif UNITY_ANDROID
            CallStaticSilent("au.com.oddgames.bugpunch.BugpunchRuntime", "setCustomData", key, value);
#elif UNITY_IOS
            try { Bugpunch_SetCustomData(key, value); } catch { }
#endif
        }

        // ── SDK self-diagnostic sink ──
        // Routes internal SDK errors (silent catches that would otherwise just
        // log) to the native overlay banner. Re-entrant calls are dropped via
        // [ThreadStatic] guard — if reporting an SDK error itself fails we
        // can't recurse into another error report.

        [ThreadStatic] static bool t_inSdkError;

        /// <summary>
        /// Internal SDK self-diagnostic. Routes to a native banner that surfaces
        /// the error on screen plus prints to the Unity console. Always safe to
        /// call (no init required, never throws). Source is a short subsystem
        /// label like <c>"BugpunchClient"</c>; <paramref name="message"/> is a
        /// short human-readable description; <paramref name="stackTrace"/> is
        /// optional and shown when the user taps the banner to expand it.
        /// </summary>
        public static void ReportSdkError(string source, string message, string stackTrace = null)
        {
            if (t_inSdkError) return;
            t_inSdkError = true;
            try
            {
                var src = string.IsNullOrEmpty(source) ? "Bugpunch" : source;
                var msg = message ?? "";
                BugpunchLog.Error("{src}", $"{msg}");
#if UNITY_EDITOR
#elif UNITY_ANDROID
                // Direct call — must not route through CallStatic because that
                // would recurse back into ReportSdkError on failure.
                try { JavaClass("au.com.oddgames.bugpunch.BugpunchRuntime")
                        .CallStatic("reportSdkError", src, msg, stackTrace ?? ""); }
                catch { /* native may not be loaded yet — already logged above */ }
#elif UNITY_IOS
                try { Bugpunch_ReportSdkError(src, msg, stackTrace ?? ""); }
                catch { /* native may not be loaded yet — already logged above */ }
#endif
            }
            catch { /* never throw out of the sink */ }
            finally { t_inSdkError = false; }
        }

        /// <summary>
        /// Convenience overload — pulls type + message + stack from an Exception.
        /// </summary>
        public static void ReportSdkError(string source, Exception ex)
        {
            if (ex == null) return;
            var msg = ex.GetType().Name + ": " + (ex.Message ?? "");
            ReportSdkError(source, msg, ex.StackTrace);
        }

        /// <summary>
        /// Toggle the SDK error banner at runtime. Useful for hiding it during
        /// player-facing demo flows without disabling collection. Errors still
        /// log to the Unity console regardless.
        /// </summary>
        public static void SetSdkErrorOverlay(bool enabled)
        {
            if (!s_started) return;
#if UNITY_EDITOR
#elif UNITY_ANDROID
            CallStaticSilent("au.com.oddgames.bugpunch.BugpunchRuntime", "setSdkErrorOverlay", enabled);
#elif UNITY_IOS
            try { Bugpunch_SetSdkErrorOverlay(enabled ? 1 : 0); } catch { }
#endif
        }

        // ── Native report-tunnel accessors ──
        // The bug-report WebSocket (pin config, log sink, device actions)
        // lives natively. These accessors let the managed side read its
        // registered deviceId so the managed IdeTunnel can reuse it and
        // both tunnels appear as the same device on the server.

        public static bool TunnelIsConnected()
        {
#if UNITY_EDITOR
            return false;
#elif UNITY_ANDROID
            return CallStaticSilent<bool>("au.com.oddgames.bugpunch.BugpunchTunnel", "isConnected", false);
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
            return CallStaticSilent<string>("au.com.oddgames.bugpunch.BugpunchTunnel", "getDeviceId", "") ?? "";
#elif UNITY_IOS
            try { return Bugpunch_TunnelDeviceId() ?? ""; }
            catch { return ""; }
#else
            return "";
#endif
        }

        // ── Native tester-role accessor ──
        // Role lives natively (SharedPreferences on Android, Keychain on iOS)
        // and is refreshed at handshake time from the signed roleConfig
        // payload. Returns "internal" | "external" | "public"; defaults to
        // "public" if native isn't available or hasn't registered yet.
        //
        // This is the only way C# reads role on device; RoleState delegates
        // through here.

        public static string GetTesterRole()
        {
#if UNITY_EDITOR
            return "public";
#elif UNITY_ANDROID
            return CallStaticSilent<string>("au.com.oddgames.bugpunch.BugpunchTunnel", "getTesterRole", "public") ?? "public";
#elif UNITY_IOS
            try { return Bugpunch_GetTesterRole() ?? "public"; }
            catch { return "public"; }
#else
            return "public";
#endif
        }

        /// <summary>
        /// Ship a response frame through the native report tunnel. Kept for
        /// native-originated flows (e.g. PostScriptResult dispatches an
        /// envelope back to server) — Remote IDE RPC responses now use the
        /// managed <see cref="IdeTunnel"/> directly. No-op in Editor.
        /// </summary>
        public static void TunnelSendResponse(string responseJson)
        {
            if (string.IsNullOrEmpty(responseJson)) return;
#if UNITY_EDITOR
#elif UNITY_ANDROID
            CallStatic("au.com.oddgames.bugpunch.BugpunchTunnel", "sendResponse",
                "BugpunchNative.TunnelSendResponse", responseJson);
#elif UNITY_IOS
            try { Bugpunch_TunnelSendResponse(responseJson); }
            catch (Exception e) { ReportSdkError("BugpunchNative.TunnelSendResponse", e); }
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
                return JavaClass("au.com.oddgames.bugpunch.BugpunchIdentity")
                    .CallStatic<string>("getStableDeviceId", CurrentActivity()) ?? "";
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
            return CallStaticSilent<string>("au.com.oddgames.bugpunch.BugpunchRuntime", "getMetadata", "unknown", "installerMode") ?? "unknown";
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
                JavaClass("au.com.oddgames.bugpunch.BugpunchDebugMode")
                    .CallStatic("enter", CurrentActivity(), skipConsent);
            }
            catch (Exception e) { ReportSdkError("BugpunchNative.EnterDebugMode", e); }
#elif UNITY_IOS
            try { Bugpunch_EnterDebugMode(skipConsent ? 1 : 0); }
            catch (Exception e) { ReportSdkError("BugpunchNative.EnterDebugMode", e); }
#endif
        }

        /// <summary>
        /// True if the native video recorder is running in buffer-input mode
        /// (i.e. MediaProjection consent was denied and we fell back to
        /// Unity-surface capture). Unity polls this to decide whether to run
        /// the <see cref="BugpunchSurfaceRecorder"/> pipeline. Android-only.
        /// </summary>
        public static bool IsVideoBufferMode()
        {
            if (!s_started) return false;
#if UNITY_EDITOR
            return false;
#elif UNITY_ANDROID
            try
            {
                using var inst = JavaClass("au.com.oddgames.bugpunch.BugpunchRecorder")
                    .CallStatic<AndroidJavaObject>("getInstance");
                return inst != null && inst.Call<bool>("isBufferMode");
            }
            catch { return false; }
#else
            return false;
#endif
        }

        /// <summary>
        /// Width/height the native recorder was configured with — used by the
        /// Unity surface recorder to size its mirror RenderTexture so frames
        /// match the codec's expected buffer size. Returns (0, 0) if not in
        /// buffer mode. Android-only.
        /// </summary>
        public static (int width, int height) GetVideoBufferSize()
        {
            if (!s_started) return (0, 0);
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var inst = JavaClass("au.com.oddgames.bugpunch.BugpunchRecorder")
                    .CallStatic<AndroidJavaObject>("getInstance");
                if (inst == null) return (0, 0);
                int w = inst.Call<int>("getWidth");
                int h = inst.Call<int>("getHeight");
                return (w, h);
            }
            catch { return (0, 0); }
#else
            return (0, 0);
#endif
        }

        /// <summary>
        /// Submit a single NV12-encoded frame to the native recorder's
        /// pending-frame queue. Non-blocking — if the queue is full (encoder
        /// lagging) the oldest queued frame is dropped. Android-only.
        /// </summary>
        public static void QueueVideoFrame(byte[] nv12, long ptsUs)
        {
            if (!s_started || nv12 == null) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var inst = JavaClass("au.com.oddgames.bugpunch.BugpunchRecorder")
                    .CallStatic<AndroidJavaObject>("getInstance");
                if (inst == null) return;
                // AndroidJavaObject.Call marshals the byte[] via JNI automatically.
                inst.Call<bool>("queueFrame", nv12, ptsUs);
            }
            catch (Exception e) { ReportSdkError("BugpunchNative.QueueVideoFrame", e); }
#endif
        }

        public static void Trace(string label, string tagsJson)
        {
            if (!s_started || string.IsNullOrEmpty(label)) return;
#if UNITY_EDITOR
#elif UNITY_ANDROID
            CallStatic("au.com.oddgames.bugpunch.BugpunchReportingService", "addTrace",
                "BugpunchNative.Trace", label, tagsJson);
#elif UNITY_IOS
            try { Bugpunch_Trace(label, tagsJson); }
            catch (Exception e) { ReportSdkError("BugpunchNative.Trace", e); }
#endif
        }

        public static void TraceScreenshot(string label, string tagsJson)
        {
            if (!s_started || string.IsNullOrEmpty(label)) return;
#if UNITY_EDITOR
#elif UNITY_ANDROID
            CallStatic("au.com.oddgames.bugpunch.BugpunchReportingService", "addTraceScreenshot",
                "BugpunchNative.TraceScreenshot", label, tagsJson);
#elif UNITY_IOS
            try { Bugpunch_TraceScreenshot(label, tagsJson); }
            catch (Exception e) { ReportSdkError("BugpunchNative.TraceScreenshot", e); }
#endif
        }

        public static void UpdateScene(string scene)
        {
            if (!s_started) return;
#if UNITY_EDITOR
#elif UNITY_ANDROID
            CallStaticSilent("au.com.oddgames.bugpunch.BugpunchRuntime", "updateScene", scene ?? "");
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
            CallStatic("au.com.oddgames.bugpunch.BugpunchRuntime", "trackEvent",
                "BugpunchNative.TrackEvent", name, propertiesJson);
#elif UNITY_IOS
            try { Bugpunch_TrackEvent(name, propertiesJson); }
            catch (Exception e) { ReportSdkError("BugpunchNative.TrackEvent", e); }
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
            CallStaticSilent("au.com.oddgames.bugpunch.BugpunchInput", "pushTouch",
                type, t, x, y, path ?? "", scene ?? "", label ?? "");
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
            CallStaticSilent("au.com.oddgames.bugpunch.BugpunchInput", "pushKey",
                type, t, keyCode, scene ?? "");
#endif
        }

        public static void PushInputSceneChange(string scene)
        {
            if (!s_started) return;
            long t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
#if UNITY_EDITOR
#elif UNITY_ANDROID
            CallStaticSilent("au.com.oddgames.bugpunch.BugpunchInput", "pushSceneChange",
                t, scene ?? "");
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
            CallStaticSilent("au.com.oddgames.bugpunch.BugpunchInput", "pushCustom",
                t, category ?? "", message ?? "", scene ?? "");
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
        /// <c>POST /api/issues/events/:id/enrich</c> endpoint via its upload
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
            CallStatic("au.com.oddgames.bugpunch.BugpunchRuntime", "postDirectiveResult",
                "BugpunchNative.PostDirectiveResult", directiveId, resultJson ?? "");
#elif UNITY_IOS
            try { Bugpunch_PostDirectiveResult(directiveId, resultJson ?? ""); }
            catch (Exception e) { ReportSdkError("BugpunchNative.PostDirectiveResult", e); }
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
            BugpunchLog.Info("BugpunchNative", "StartPoll: no-op in Editor");
#elif UNITY_ANDROID
            try
            {
                JavaClass("au.com.oddgames.bugpunch.BugpunchPoller")
                    .CallStatic("start", CurrentActivity(), perm, interval);
            }
            catch (Exception e) { ReportSdkError("BugpunchNative.StartPoll", e); }
#elif UNITY_IOS
            try { Bugpunch_StartPoll(perm, interval); }
            catch (Exception e) { ReportSdkError("BugpunchNative.StartPoll", e); }
#endif
        }

        public static void StopPoll()
        {
#if UNITY_EDITOR
#elif UNITY_ANDROID
            CallStaticSilent("au.com.oddgames.bugpunch.BugpunchPoller", "stop");
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
            CallStatic("au.com.oddgames.bugpunch.BugpunchPoller", "postScriptResult",
                "BugpunchNative.PostScriptResult",
                scheduledScriptId, output ?? "", errors ?? "", success, durationMs);
#elif UNITY_IOS
            try { Bugpunch_PostScriptResult(scheduledScriptId, output ?? "", errors ?? "", success, durationMs); }
            catch (Exception e) { ReportSdkError("BugpunchNative.PostScriptResult", e); }
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
            CallStatic("au.com.oddgames.bugpunch.BugpunchDirectives", "onPollDirectives",
                "BugpunchNative.ProcessPollDirectives", pendingDirectivesJson);
#elif UNITY_IOS
            try { BPDirectives_OnPollDirectives(pendingDirectivesJson); }
            catch (Exception e) { ReportSdkError("BugpunchNative.ProcessPollDirectives", e); }
#endif
        }

        /// <summary>
        /// Launch the platform-native image picker. The callback is invoked
        /// with the absolute path of the picked file on disk, or null/empty
        /// if the user cancelled. Callback is dispatched on the Unity main
        /// thread by whichever native surface routes the UnitySendMessage.
        ///
        /// Android: uses <c>Intent.ACTION_PICK</c> with <c>image/*</c> via
        /// <c>BugpunchImagePicker.java</c>, which copies the chosen URI's
        /// bytes into the app's cache dir and sends the path back through
        /// <c>UnitySendMessage("BugpunchReportCallback", "OnImagePicked",
        /// path)</c>.
        /// iOS: <c>Bugpunch_PickImage</c> wraps <c>PHPickerViewController</c>
        /// and writes the selected image to a tmp file, returning the path
        /// via the same C# callback hook.
        /// Editor / other platforms: no-op (caller should show a fallback
        /// toast).
        /// </summary>
        public static void PickImage(Action<string> callback)
        {
            if (callback == null) return;
            // Only one pending pick at a time — replace any previous pending
            // callback, since the user tapped the button again.
            s_pendingImagePicked = callback;

#if UNITY_EDITOR
            // No native picker in editor — fire null back so UI can show a toast
            // without the caller handling this platform separately.
            callback.Invoke(null);
            s_pendingImagePicked = null;
#elif UNITY_ANDROID
            try
            {
                JavaClass("au.com.oddgames.bugpunch.BugpunchImagePicker")
                    .CallStatic("pick", CurrentActivity());
            }
            catch (Exception e)
            {
                ReportSdkError("BugpunchNative.PickImage", e);
                callback.Invoke(null);
                s_pendingImagePicked = null;
            }
#elif UNITY_IOS
            try { Bugpunch_PickImage(); }
            catch (Exception e)
            {
                ReportSdkError("BugpunchNative.PickImage", e);
                callback.Invoke(null);
                s_pendingImagePicked = null;
            }
#else
            callback.Invoke(null);
            s_pendingImagePicked = null;
#endif
        }

        static Action<string> s_pendingImagePicked;

        /// <summary>
        /// Native → managed dispatch for image-pick results. Called from the
        /// Android / iOS UnitySendMessage receivers. Empty / null path means
        /// the user cancelled; the registered callback still fires so the
        /// caller can unblock its UI.
        /// </summary>
        internal static void DispatchImagePicked(string path)
        {
            var cb = s_pendingImagePicked;
            s_pendingImagePicked = null;
            if (cb == null) return;
            try { cb.Invoke(string.IsNullOrEmpty(path) ? null : path); }
            catch (Exception e) { ReportSdkError("BugpunchNative.DispatchImagePicked", e); }
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
            BugpunchLog.Info("BugpunchNative", "Perf monitor: no-op in Editor");
#elif UNITY_ANDROID
            CallStatic("au.com.oddgames.bugpunch.BugpunchPerfMonitor", "start",
                "BugpunchNative.StartPerfMonitor", configJson);
#elif UNITY_IOS
            try { Bugpunch_StartPerfMonitor(configJson); }
            catch (Exception e) { ReportSdkError("BugpunchNative.StartPerfMonitor", e); }
#endif
        }

#if !UNITY_EDITOR && UNITY_IOS
        [DllImport("__Internal")] static extern string Bugpunch_GetStableDeviceId();
        [DllImport("__Internal")] static extern void Bugpunch_TunnelSendResponse(string responseJson);
        [DllImport("__Internal")] static extern bool Bugpunch_TunnelIsConnected();
        [DllImport("__Internal")] static extern string Bugpunch_TunnelDeviceId();
        [DllImport("__Internal")] static extern string Bugpunch_GetTesterRole();
        [DllImport("__Internal")] static extern bool Bugpunch_StartDebugMode(string configJson);
        [DllImport("__Internal")] static extern void Bugpunch_StartTunnel(string configJson);
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
        [DllImport("__Internal")] static extern void Bugpunch_PickImage();
        [DllImport("__Internal")] static extern void Bugpunch_ReportSdkError(string source, string message, string stackTrace);
        [DllImport("__Internal")] static extern void Bugpunch_SetSdkErrorOverlay(int enabled);
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
            BugpunchLog.Info("BugpunchNative", $"Device tier: {DeviceTier} (RAM={memMb}MB, cores={cores})");
        }

        // Cached because Resources.Load is expensive and the value never changes
        // for the lifetime of the process (it's compiled into the build).
        static string s_buildFingerprint;
        static bool s_buildFingerprintLoaded;

        /// <summary>
        /// Read the build fingerprint stamped at editor build time. Returns
        /// "" when running in Editor or in a build that wasn't produced by a
        /// SDK-aware editor (no Resources/BugpunchBuildInfo.txt).
        /// </summary>
        static string ReadBuildFingerprint()
        {
            if (s_buildFingerprintLoaded) return s_buildFingerprint;
            s_buildFingerprintLoaded = true;
            try
            {
                var ta = Resources.Load<TextAsset>("BugpunchBuildInfo");
                s_buildFingerprint = ta != null ? (ta.text ?? "").Trim() : "";
            }
            catch { s_buildFingerprint = ""; }
            return s_buildFingerprint;
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
            var useNativeTunnel = Debug.isDebugBuild || c.buildChannel == BugpunchConfig.BuildChannel.Internal;
            sb.Append("\"useNativeTunnel\":").Append(useNativeTunnel ? "true" : "false").Append(',');
            Field(sb, "buildChannel", c.buildChannel.ToString().ToLowerInvariant()); sb.Append(',');
            Field(sb, "deviceTier", DeviceTier);           sb.Append(',');
            sb.Append("\"anrTimeoutMs\":").Append(c.anrTimeoutMs).Append(',');
            sb.Append("\"logBufferSize\":").Append(logBuffer).Append(',');
            sb.Append("\"sdkErrorOverlay\":").Append(c.showSdkErrorOverlay ? "true" : "false").Append(',');
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
            // Build fingerprint — opaque UUID stamped into Resources by the
            // editor pre-build hook. Same value on the APK upload, so the
            // server can dedup builds even when Application.version is wrong.
            // Empty in Editor + builds without the package's BuildHooks.
            Field(sb, "buildFingerprint", ReadBuildFingerprint());        sb.Append(',');
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

            // Strings — every user-facing label, drawn from BugpunchStrings.
            // Same shape on every platform: { locale, defaults, translations }.
            // BugpunchStrings.ToJson() handles the serialisation so callers
            // don't have to know the field list.
            var strings = c.Strings ?? new BugpunchStrings();
            sb.Append(",\"strings\":").Append(strings.ToJson());

            // Theme — shared schema consumed by both C# UIToolkit and native
            // Android / iOS surfaces. Colours serialise via BugpunchTheme.ToHex
            // (#RRGGBB when opaque, #RRGGBBAA otherwise); sizes go out as ints.
            var theme = c.Theme ?? new BugpunchTheme();
            sb.Append(",\"theme\":{");
            Field(sb, "cardBackground", BugpunchTheme.ToHex(theme.cardBackground)); sb.Append(',');
            Field(sb, "cardBorder",     BugpunchTheme.ToHex(theme.cardBorder));     sb.Append(',');
            Field(sb, "backdrop",       BugpunchTheme.ToHex(theme.backdrop));       sb.Append(',');
            sb.Append("\"cardRadius\":").Append(theme.cardRadius).Append(',');
            Field(sb, "textPrimary",    BugpunchTheme.ToHex(theme.textPrimary));    sb.Append(',');
            Field(sb, "textSecondary",  BugpunchTheme.ToHex(theme.textSecondary));  sb.Append(',');
            Field(sb, "textMuted",      BugpunchTheme.ToHex(theme.textMuted));      sb.Append(',');
            Field(sb, "accentPrimary",  BugpunchTheme.ToHex(theme.accentPrimary));  sb.Append(',');
            Field(sb, "accentRecord",   BugpunchTheme.ToHex(theme.accentRecord));   sb.Append(',');
            Field(sb, "accentChat",     BugpunchTheme.ToHex(theme.accentChat));     sb.Append(',');
            Field(sb, "accentFeedback", BugpunchTheme.ToHex(theme.accentFeedback)); sb.Append(',');
            Field(sb, "accentBug",      BugpunchTheme.ToHex(theme.accentBug));      sb.Append(',');
            sb.Append("\"fontSizeTitle\":").Append(theme.fontSizeTitle).Append(',');
            sb.Append("\"fontSizeBody\":").Append(theme.fontSizeBody).Append(',');
            sb.Append("\"fontSizeCaption\":").Append(theme.fontSizeCaption);
            sb.Append('}');

            // Unity-resolved storage paths for the native FileService. Native
            // /files/* handlers gate access to these roots — anything outside
            // returns 403. PlayerPrefs paths stay C#-only (UnityEngine API).
            sb.Append(",\"paths\":{");
            Field(sb, "persistent",      Application.persistentDataPath);  sb.Append(',');
            Field(sb, "cache",           Application.temporaryCachePath);  sb.Append(',');
            Field(sb, "data",            Application.dataPath);            sb.Append(',');
            Field(sb, "streamingAssets", Application.streamingAssetsPath); sb.Append(',');
            Field(sb, "consoleLog",      Application.consoleLogPath ?? "");
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
    /// Pushes the active Unity scene name to native on change, emits a
    /// <c>scene_change</c> analytics event with duration in the previous
    /// scene, and forwards Unity log messages to the native log buffer on
    /// iOS (Android's logcat reader already captures them). Added by
    /// BugpunchClient after <see cref="BugpunchNative.Start"/>.
    /// </summary>
    public class BugpunchSceneTick : MonoBehaviour
    {
        // Scene-timing state. The very first scene_change fires with from=null
        // so "app opened in scene X" is capturable as an entry-point signal.
        string m_CurrentScene;
        float m_SceneEnteredAt;

        void OnEnable()
        {
            SceneManager.activeSceneChanged += OnSceneChanged;
            var initial = SceneManager.GetActiveScene().name;
            BugpunchNative.UpdateScene(initial);
            // Entry event — from=null marks "app opened in this scene". Duration
            // is zero because we don't know when the previous session ended.
            EmitSceneChange(null, initial, 0);
            m_CurrentScene = initial;
            m_SceneEnteredAt = Time.realtimeSinceStartup;
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

        void OnSceneChanged(Scene prev, Scene next)
        {
            var nextName = next.name;
            BugpunchNative.UpdateScene(nextName);

            // Additive-load activations can fire activeSceneChanged with the
            // same name (e.g. a preload → promote sequence). Skip the event
            // but keep the native scene pushed — no-op analytically.
            if (string.Equals(nextName, m_CurrentScene, StringComparison.Ordinal)) return;

            var now = Time.realtimeSinceStartup;
            var durationMs = (long)((now - m_SceneEnteredAt) * 1000f);
            if (durationMs < 0) durationMs = 0;
            EmitSceneChange(m_CurrentScene, nextName, durationMs);

            m_CurrentScene = nextName;
            m_SceneEnteredAt = now;
        }

        static void EmitSceneChange(string from, string to, long durationMs)
        {
            // Calls the public TrackEvent facade so sampling / shutdown
            // guards in Bugpunch.cs apply uniformly.
            try
            {
                var props = new System.Collections.Generic.Dictionary<string, object> {
                    ["from"] = from,         // null on app-entry event
                    ["to"] = to,
                    ["duration_ms"] = durationMs,
                };
                Bugpunch.TrackEvent("scene_change", props);
            }
            catch (Exception ex) { BugpunchNative.ReportSdkError("BugpunchSceneTick.scene_change", ex); }
        }

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
