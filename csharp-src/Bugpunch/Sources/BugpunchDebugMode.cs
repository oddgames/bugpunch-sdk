// =============================================================================
// LANE: Editor + Standalone (C#)
//
// BugpunchDebugMode — managed-runtime "always-on" coordinator. Mirrors the
// native `BugpunchDebugMode.java` (Android) and `BugpunchDebugMode.mm` (iOS)
// by name and purpose so a feature owner can grep one identifier across
// all three lanes.
//
// What "always-on" means per lane:
//   • Android — BugpunchDebugMode.java owns config, metadata, custom data,
//     crash handlers, log capture, shake detection, screenshot ring,
//     video ring, upload queue. C# delegates via BugpunchNative.Start.
//   • iOS — BugpunchDebugMode.mm owns the same. C# delegates the same way.
//   • Editor + Standalone (this file) — no native lane, so this class
//     owns the managed-runtime equivalents that are still applicable:
//       · push the active scene name (BugpunchSceneTick) — native can
//         measure FPS itself but it can't see SceneManager.
//       · capture downscaled UI press frames into the input ring
//         (BugpunchInputCapture) — rescue path for screenshot_at_crash.
//       · install BugpunchCrashHandler (C# managed-exception flavor; mirrors
//         BugpunchCrashHandler.java/.mm by name) — only Mono/IL2CPP can
//         hook AppDomain.UnhandledException + TaskScheduler.UnobservedTask
//         Exception, so this stays C# regardless of lane.
//       · mount CrashDirectiveHandler so "Request More Info" UnitySendMessage
//         pushes from native land on a live receiver.
//       · on Android player only, mount BugpunchSurfaceRecorder as the
//         fallback NV12 source when MediaProjection consent is denied.
//
// BugpunchClient remains the C#-only public entry point (MonoBehaviour
// lifecycle, public StartConnection / StopConnection API, IDE tunnel
// state). Phase 1 init code that used to live on BugpunchClient.InitAlwaysOn
// has moved into BugpunchDebugMode.Start so the always-on coordinator is
// the same identifier across all three lanes.
// =============================================================================

using UnityEngine;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// Always-on init for the managed lane.
    ///
    /// Cross-lane rule: this class reads its inputs from
    /// <see cref="BugpunchRuntime"/> only — no <see cref="BugpunchClient"/>
    /// reference. Mirrors how <c>BugpunchDebugMode.java</c> reads from
    /// <c>BugpunchRuntime</c> on Android.
    /// </summary>
    public static class BugpunchDebugMode
    {
        /// <summary>
        /// Run the Phase 1 (always-on) init steps. Reads config + host
        /// gameObject from <see cref="BugpunchRuntime"/>. Idempotent —
        /// re-calling is harmless because the underlying components /
        /// native start guard against double-init themselves.
        /// </summary>
        public static void Start()
        {
            var config = BugpunchRuntime.Config;
            var hostGo = BugpunchRuntime.HostGameObject;
            if (config == null) { BugpunchLog.Warn("BugpunchDebugMode", "Start: BugpunchRuntime.Config not set — call BugpunchRuntime.Init first"); return; }
            if (hostGo == null) { BugpunchLog.Warn("BugpunchDebugMode", "Start: BugpunchRuntime.HostGameObject not set — call BugpunchRuntime.Init first"); return; }

            // Native runtime — owns crash handlers, log capture, shake
            // detection, screenshot ring, video ring buffer, upload queue.
            // C# just pushes scene/fps and forwards managed exceptions.
            BugpunchNative.Start(config);

            // Scene name push + scene_change analytics events. Native can
            // measure FPS itself but it can't see SceneManager.
            hostGo.AddComponent<BugpunchSceneTick>();

            // Storyboard input capture — captures a downscaled frame + press
            // metadata into the native ring on every UI press. Replaces the
            // older 1 Hz rolling buffer; the newest ring slot is the rescue
            // path for screenshot_at_crash. Has to be eager because input
            // that happens before any IDE / report request still needs to
            // be captured into the ring for crashes that follow it.
            hostGo.AddComponent<BugpunchInputCapture>();

#if UNITY_ANDROID && !UNITY_EDITOR
            // Fallback video source for when MediaProjection consent is denied
            // — the native recorder switches to buffer mode and this component
            // feeds it NV12 frames from a mirror RenderTexture. Always mounted;
            // it polls native state and stays idle until buffer mode activates.
            hostGo.AddComponent<BugpunchSurfaceRecorder>();
#endif

            // UnitySendMessage receiver for native "Request More Info" directives.
            // The component itself is the target — no Init() needed; Awake binds
            // the singleton.
            hostGo.AddComponent<CrashDirectiveHandler>();

            // Managed exception forwarder — must hook AppDomain events at boot
            // to catch exceptions thrown before any IDE session opens.
            if (config.enableNativeCrashHandler)
            {
                // Ensure exception logs include stack traces. Default in
                // release builds can be None, which would leave us with just
                // the message and no frames at all. Don't downgrade if the
                // game has explicitly set Full.
                if (Application.GetStackTraceLogType(LogType.Exception) == StackTraceLogType.None)
                    Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.ScriptOnly);
                BugpunchCrashHandler.Install();
            }
        }
    }
}
