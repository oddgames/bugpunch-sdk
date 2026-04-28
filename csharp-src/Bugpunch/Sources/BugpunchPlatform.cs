// =============================================================================
// BugpunchPlatform — SDK lane router and architectural source of truth.
//
// The Bugpunch SDK is split across three platform lanes. Exactly one is
// active in any given build:
//
//   ┌────────────────────────┬──────────────┬──────────────────────────────┐
//   │ Lane                   │ Language     │ Files                        │
//   ├────────────────────────┼──────────────┼──────────────────────────────┤
//   │ Android player         │ Java + NDK   │ sdk/android-src/bugpunch/    │
//   │                        │              │ src/main/java/.../*.java     │
//   │                        │              │ src/main/cpp/*.c             │
//   ├────────────────────────┼──────────────┼──────────────────────────────┤
//   │ iOS player             │ Obj-C++      │ sdk/package/Plugins/iOS/*.mm │
//   ├────────────────────────┼──────────────┼──────────────────────────────┤
//   │ Editor + Standalone    │ C#           │ sdk/package/*.cs (cross-lane) │
//   │ (Win / Mac / Linux)    │              │ sdk/package/RemoteIDE/ (IDE) │
//   └────────────────────────┴──────────────┴──────────────────────────────┘
//
// Class names mirror across lanes — `BugpunchPoller` exists as
// `BugpunchPoller.java`, `BugpunchPoller.mm`, and `BugpunchPoller.cs`. Every
// feature is implemented once per lane it ships on; lanes never reach into
// another lane's lane-specific implementation.
//
// What lives where:
//   • Unity-bound features (scene/component access, UIToolkit, MonoBehaviour,
//     RenderPipelineManager, PlayerPrefs, SystemInfo, Profiler, …) live in
//     C# regardless of lane — they're the only things Unity exposes to the
//     managed runtime. They run on every lane because C# is the language
//     Unity speaks.
//   • Platform-local features that don't read Unity state live native: chat
//     heartbeat + banner, crash handlers, upload queue, log capture, shake
//     detection, screenshots, video recording, native dialogs. The C#
//     side either no-ops or bridges via `BugpunchNative` (JNI / P-Invoke).
//   • Editor / Standalone has no native lane, so any feature that ships
//     on those targets must have a working C# path. That's why the C#
//     UIToolkit chat board / feedback board still exist — they're the
//     desktop implementation, not a fallback.
//
// On first load this class logs which lane is active, so the very first
// Bugpunch line in any build tells you exactly where the work is running.
// =============================================================================

using UnityEngine;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// Identifies which platform lane is responsible for the heavy lifting
    /// in the current build. See file header for the architectural map.
    /// </summary>
    public enum BugpunchLane
    {
        /// <summary>Android player — Java + NDK is the implementation;
        /// C# is the JNI bridge only.</summary>
        AndroidJava,

        /// <summary>iOS player — Obj-C++ is the implementation; C# is
        /// the P-Invoke bridge only.</summary>
        iOSObjectiveCpp,

        /// <summary>Unity Editor or Standalone (Win / Mac / Linux). No
        /// native lane; C# is the implementation.</summary>
        CSharpEditorOrStandalone,

        /// <summary>Unrecognised platform — likely a console target or a
        /// future Unity platform we haven't classified.</summary>
        Unknown,
    }

    public static class BugpunchPlatform
    {
        /// <summary>Which lane this build is compiled for.</summary>
        public static BugpunchLane ActiveLane =>
#if UNITY_ANDROID && !UNITY_EDITOR
            BugpunchLane.AndroidJava;
#elif UNITY_IOS && !UNITY_EDITOR
            BugpunchLane.iOSObjectiveCpp;
#elif UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
            BugpunchLane.CSharpEditorOrStandalone;
#else
            BugpunchLane.Unknown;
#endif

        /// <summary>Human-readable lane label (e.g. for log lines).</summary>
        public static string ActiveLaneName
        {
            get
            {
                switch (ActiveLane)
                {
                    case BugpunchLane.AndroidJava: return "Android (Java + NDK)";
                    case BugpunchLane.iOSObjectiveCpp: return "iOS (Obj-C++)";
                    case BugpunchLane.CSharpEditorOrStandalone: return "Editor / Standalone (C#)";
                    default: return "Unknown";
                }
            }
        }

        /// <summary>
        /// True when C# is the implementation (Editor + Standalone), not a
        /// bridge. Features that have no native fallback — UIToolkit chat /
        /// feedback boards, the in-process heartbeat, etc. — should gate on
        /// this so they don't double-fire alongside their native lane.
        /// </summary>
        public static bool IsManagedLane =>
            ActiveLane == BugpunchLane.CSharpEditorOrStandalone;

        /// <summary>
        /// True on Android / iOS player builds. C# code on these lanes
        /// should be minimal — just the JNI / P-Invoke bridge through
        /// <c>BugpunchNative</c>. If you find yourself adding business
        /// logic to the C# side and this is true, you're probably in the
        /// wrong lane.
        /// </summary>
        public static bool IsNativeLane =>
            ActiveLane == BugpunchLane.AndroidJava ||
            ActiveLane == BugpunchLane.iOSObjectiveCpp;

        /// <summary>
        /// Logs the active lane on subsystem registration so the first
        /// Bugpunch log line in any build tells you which lane is doing
        /// the work. Fires before scene load — earliest hook Unity gives
        /// us that runs even if no `BugpunchClient` is in the scene.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void LogActiveLane()
        {
            Debug.Log($"[Bugpunch] Active lane: {ActiveLaneName}");
        }
    }
}
