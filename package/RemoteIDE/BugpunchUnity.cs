// =============================================================================
// LANE: Editor + Standalone (C#) — but the IDE module is the only thing on
// every lane that actually has a C# implementation. Native lanes (Android,
// iOS) host the IDE WebSocket via the C# `IdeTunnel` regardless because the
// services it serves (HierarchyService, InspectorService, ScriptRunner …)
// are all Unity-bound.
//
// BugpunchUnity — single public entry point for the Remote IDE module.
//
// `BugpunchClient` should not reach into individual RemoteIDE service types
// (HierarchyService, InspectorService, RequestRouter constructor …). All it
// needs is here: build the service graph, get a router back, hold the few
// references it still legitimately exposes (Router for callbacks,
// SceneCamera as a public component handle).
//
// The name is intentionally not a mirror of Java/iOS — there is no native
// Remote IDE on those lanes, so there is nothing to mirror. (Java's
// `BugpunchUnity.java` is a *different* concept: a reflection helper for
// talking to UnityPlayer from Java land.)
//
// Future tightening: drive every type referenced only inside this module
// to `internal`, leaving `BugpunchUnity` and the few types BugpunchClient
// surfaces (`IdeTunnel`, `RequestRouter`, `SceneCameraService`) as the
// public surface.
// =============================================================================

using ODDGames.Bugpunch.RemoteIDE.Database;
using UnityEngine;

namespace ODDGames.Bugpunch.RemoteIDE
{
    /// <summary>
    /// Bundle of references BugpunchClient holds onto after the IDE module
    /// is built. Anything that's only needed internally (the individual
    /// services) is wired into <see cref="Router"/> and not exposed.
    /// </summary>
    public sealed class IdeServices
    {
        /// <summary>The fully wired request router — feed it tunnel
        /// requests from <c>IdeTunnel.OnRequest</c>.</summary>
        public RequestRouter Router { get; internal set; }

        /// <summary>Scene camera service component on the host GameObject.
        /// BugpunchClient surfaces this as a public property so external
        /// callers (the recorder, the scene-camera director) can reach
        /// it without going through the router.</summary>
        public SceneCameraService SceneCamera { get; internal set; }
    }

    /// <summary>
    /// Single entry point for the Remote IDE module. Owns construction of
    /// every IDE service, the request router, and the lazy WebRTC streamer
    /// init. <see cref="BugpunchClient"/> calls
    /// <see cref="BuildServices(MonoBehaviour)"/> once during
    /// <c>BuildLazyServices</c>; everything else is internal to the module.
    /// </summary>
    public static class BugpunchUnity
    {
        /// <summary>
        /// Construct the lazy IDE service graph and its request router,
        /// hosted on <paramref name="host"/>'s GameObject for any service
        /// that needs to live as a MonoBehaviour. Cheap — no scene scans,
        /// no event subscriptions beyond ConsoleService's eager log hook.
        /// </summary>
        public static IdeServices BuildServices(MonoBehaviour host)
        {
            if (host == null) return null;

            var go = host.gameObject;

            // Plain managed services — pure C#, no Unity component lifecycle.
            var hierarchy        = new HierarchyService();
            var console          = new ConsoleService();
            var inspector        = new InspectorService();
            var perf             = new PerformanceService();
            var dbPlugins        = new DatabasePluginRegistry();
            var files            = new FileService { DatabasePlugins = dbPlugins };
            var deviceInfo       = new DeviceInfoService();
            IScriptRunner runner = new ScriptRunner();
            var textures         = new TextureService();
            var memorySnapshots  = new MemorySnapshotService();
            var playerPrefs      = new PlayerPrefsService();
            var settings         = new SettingsService();

            // MonoBehaviour services — need Update / coroutine support or
            // OnRender hooks, so they must live as components.
            var screenCapture    = go.AddComponent<ScreenCaptureService>();
            var materials        = go.AddComponent<MaterialService>();
            var shaderProfiler   = go.AddComponent<ShaderProfilerService>();
            var watch            = go.AddComponent<WatchService>();
            var sceneCamera      = go.AddComponent<SceneCameraService>();

            // Streamer is null until the first IDE debug session — the
            // Unity.WebRTC assembly + libwebrtc.so are heavy to load and most
            // sessions never need them.
            var router = new RequestRouter
            {
                Hierarchy        = hierarchy,
                Console          = console,
                ScreenCapture    = screenCapture,
                Inspector        = inspector,
                Performance      = perf,
                ScriptRunner     = runner,
                SceneCamera      = sceneCamera,
                Files            = files,
                DeviceInfo       = deviceInfo,
                DatabasePlugins  = dbPlugins,
                Textures         = textures,
                Materials        = materials,
                Watch            = watch,
                MemorySnapshots  = memorySnapshots,
                PlayerPrefs      = playerPrefs,
                ShaderProfiler   = shaderProfiler,
                Settings         = settings,
                Streamer         = null,
            };

            return new IdeServices
            {
                Router = router,
                SceneCamera = sceneCamera,
            };
        }
    }
}
