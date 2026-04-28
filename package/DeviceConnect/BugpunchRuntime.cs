// =============================================================================
// LANE: Editor + Standalone (C#)
//
// BugpunchRuntime — process-wide shared state for the C# lane. Mirrors the
// native `BugpunchRuntime.java` (Android) by name and purpose: a static
// singleton that everything else on the lane reads from / writes to.
//
// Why this exists:
//   `BugpunchClient` is the C#-only public entry point — MonoBehaviour
//   lifecycle, public StartConnection / PushSuppression API. It is NOT a
//   shared state holder. Cross-lane classes (`BugpunchPoller`,
//   `BugpunchDebugMode`, anything that has a sibling on Android Java or
//   iOS Obj-C++) MUST NOT depend on `BugpunchClient` — they must read from
//   `BugpunchRuntime` instead, which is the C# mirror of the native
//   `BugpunchRuntime` static. `BugpunchClient` ALSO talks to
//   `BugpunchRuntime` (it populates the state on init), but stays out
//   of the cross-lane code path.
//
//   This mirrors how Java's BugpunchPoller doesn't import any "client"
//   class — it just reads `BugpunchRuntime.getServerUrl()` etc. The C#
//   lane should look the same shape.
//
// What it holds:
//   • Config           — the resolved BugpunchConfig asset.
//   • Host             — the BugpunchClient MonoBehaviour instance, used
//                        for coroutine hosting + AddComponent calls.
//                        Only intended for internal C# wiring; cross-lane
//                        classes shouldn't reach into it (use HostGameObject
//                        when you need an AddComponent target).
//   • SuppressActive   — true while any caller is holding a
//                        BugpunchClient.PushSuppression() scope. Read by
//                        the chat heartbeat to skip ticks during cutscenes
//                        / tutorials / modal UI.
//   • AutoFulfill      — delegate slot registered by BugpunchClient on
//                        startup. Lets BugpunchPoller dispatch
//                        playerprefs / file / deviceinfo data requests
//                        without taking a direct reference to
//                        BugpunchClient.
// =============================================================================

using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Async callback that fulfills a `playerprefs` / `file` / `deviceinfo`
    /// data request — Unity-bound (PlayerPrefs / SystemInfo / Application
    /// paths) so it must live on the managed lane. Registered by
    /// BugpunchClient on Setup; invoked by BugpunchPoller.
    /// </summary>
    public delegate Task BugpunchAutoFulfillCallback(string messageId, string kind, string body);

    /// <summary>
    /// Process-wide shared state for the C# lane. Mirrors
    /// `BugpunchRuntime.java` / `BugpunchRuntime.mm` (the Java sibling
    /// already exists at sdk/android-src/.../BugpunchRuntime.java).
    /// </summary>
    public static class BugpunchRuntime
    {
        static BugpunchConfig s_config;
        static MonoBehaviour s_host;
        static int s_suppressCount;
        static BugpunchAutoFulfillCallback s_autoFulfill;

        // ─── Accessors ──────────────────────────────────────────────────

        /// <summary>Resolved SDK config — set by BugpunchClient on Setup.</summary>
        public static BugpunchConfig Config => s_config;

        /// <summary>Server base URL with the `/api` suffix stripped — the
        /// shape every cross-lane HTTP call expects.</summary>
        public static string HttpBaseUrl => s_config?.HttpBaseUrl ?? "";

        /// <summary>Project API key — sent on every SDK-side HTTP request.</summary>
        public static string ApiKey => s_config?.apiKey ?? "";

        /// <summary>The BugpunchClient MonoBehaviour. Cross-lane code
        /// should prefer <see cref="HostGameObject"/> over reaching into
        /// the BugpunchClient type directly (the rule is "no
        /// cross-lane → BugpunchClient dependency").</summary>
        public static MonoBehaviour Host => s_host;

        /// <summary>The host GameObject — used by BugpunchDebugMode to
        /// AddComponent the always-on MonoBehaviours (BugpunchSceneTick
        /// etc.).</summary>
        public static GameObject HostGameObject => s_host == null ? null : s_host.gameObject;

        /// <summary>True while any caller is holding a
        /// BugpunchClient.PushSuppression() scope. Read by the chat
        /// heartbeat to skip ticks while the player shouldn't be
        /// interrupted (cutscenes, tutorials, modal UI).</summary>
        public static bool SuppressActive => s_suppressCount > 0;

        /// <summary>Number of nested PushSuppression scopes currently
        /// open. Diagnostic only — most callers want
        /// <see cref="SuppressActive"/>.</summary>
        public static int SuppressCount => s_suppressCount;

        /// <summary>Currently-registered auto-fulfill callback. Null
        /// before BugpunchClient.Setup runs.</summary>
        public static BugpunchAutoFulfillCallback AutoFulfill => s_autoFulfill;

        // ─── Init / mutation ────────────────────────────────────────────

        /// <summary>Called by BugpunchClient.Setup once. Idempotent —
        /// repeat calls overwrite (useful when the SDK is re-initialized
        /// in Editor playmode).</summary>
        public static void Init(BugpunchConfig config, MonoBehaviour host)
        {
            s_config = config;
            s_host = host;
        }

        /// <summary>Register the Unity-bound auto-fulfill handler.
        /// BugpunchClient does this on Setup.</summary>
        public static void RegisterAutoFulfill(BugpunchAutoFulfillCallback callback)
        {
            s_autoFulfill = callback;
        }

        /// <summary>Suppression counter — internal accessors used by the
        /// BugpunchClient.PushSuppression() handle. Game code should
        /// continue to call PushSuppression(), not these directly.</summary>
        internal static void IncrementSuppress()
        {
            s_suppressCount++;
        }

        internal static void DecrementSuppress()
        {
            if (s_suppressCount > 0) s_suppressCount--;
        }
    }
}
