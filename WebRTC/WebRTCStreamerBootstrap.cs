using UnityEngine;
using ODDGames.BugpunchSdk;

namespace ODDGames.BugpunchSdk.RemoteIDE
{
    /// <summary>
    /// Registers the Unity.WebRTC-backed <see cref="WebRTCStreamer"/> with the core
    /// runtime at startup.
    ///
    /// This assembly (ODDGames.BugpunchSdk.WebRTC) references Unity.WebRTC directly
    /// and is always in the build — com.unity.webrtc is a hard package.json
    /// dependency on every lane. The precompiled ODDGames.Bugpunch.dll never
    /// references Unity.WebRTC — the dependency points one way only: this module →
    /// core (for IStreamer).
    ///
    /// Replaces the old reflection discovery (Type.GetType), which returned null on
    /// IL2CPP/AOT because name-based assembly resolution is unreliable there (#59).
    /// RuntimeInitializeOnLoadMethod is an IL2CPP linker root, so this registrar —
    /// and the WebRTCStreamer it references — survive managed stripping without any
    /// link.xml preserve.
    ///
    /// We register a factory delegate, NOT an instance: WebRTCStreamer is still
    /// created lazily by BugpunchClient only when a debug session starts, so the
    /// native libwebrtc load stays deferred (it must not happen at boot — see the
    /// note on WebRTCStreamer).
    /// </summary>
    static class WebRTCStreamerBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            BugpunchClient.RegisterManagedStreamerFactory(go => go.AddComponent<WebRTCStreamer>());
        }
    }
}
