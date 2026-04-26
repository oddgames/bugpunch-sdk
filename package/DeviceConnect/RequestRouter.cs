using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ODDGames.Bugpunch.DeviceConnect.Database;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Routes incoming tunnel requests to the appropriate service.
    /// Pure routing logic — no transport knowledge.
    /// </summary>
    public class RequestRouter
    {
        public HierarchyService Hierarchy;
        public ConsoleService Console;
        public ScreenCaptureService ScreenCapture;
        public InspectorService Inspector;
        public PerformanceService Performance;
        public IScriptRunner ScriptRunner;
        public IStreamer Streamer;
        public SceneCameraService SceneCamera;
        public FileService Files;
        public DeviceInfoService DeviceInfo;
        public DatabasePluginRegistry DatabasePlugins;
        public TextureService Textures;
        public MaterialService Materials;
        public WatchService Watch;
        public MemorySnapshotService MemorySnapshots;
        public PlayerPrefsService PlayerPrefs;
        public ShaderProfilerService ShaderProfiler;
        public SettingsService Settings;

        public struct Response
        {
            public int status;
            public string body;
            public string contentType;
            public byte[] binaryBody;
            public bool isBinary;

            public static Response Json(string body, int status = 200) =>
                new() { status = status, body = body, contentType = "application/json" };

            public static Response Binary(byte[] data, string contentType, int status = 200) =>
                new() { status = status, binaryBody = data, contentType = contentType, isBinary = true };

            public static Response Error(string message, int status = 500) =>
                Json($"{{\"error\":\"{Esc(message)}\"}}", status);

            public static Response NotFound(string path) =>
                Json($"{{\"error\":\"Unknown path: {Esc(path)}\"}}", 404);
        }

        /// <summary>
        /// Route a request. Returns null for capture requests (need WaitForEndOfFrame).
        /// </summary>
        public Response? Route(string method, string path, string body)
        {
            try
            {
                // Role gate — all Remote IDE requests are interactive and
                // require the device to be tagged Internal on shipped builds.
                // Editor + debug builds stay unrestricted so a developer
                // opening Remote IDE on their own workstation doesn't need
                // a role tag.
                bool isDevContext = Application.isEditor || Debug.isDebugBuild;
                if (!isDevContext && !RoleState.IsInternal)
                    return Response.Error("Device not enrolled for remote debugging", 403);

                // Hierarchy
                if (path == "/hierarchy" || path.StartsWith("/hierarchy?"))
                    return Response.Json(Hierarchy?.GetHierarchy() ?? "[]");

                if (path == "/scenes" || path.StartsWith("/scenes?"))
                    return Response.Json(Hierarchy?.GetScenes() ?? "[]");

                if (path.StartsWith("/children"))
                {
                    var id = Q(path, "id");
                    return Response.Json(Hierarchy?.GetChildren(id) ?? "[]");
                }

                if (path.StartsWith("/hierarchy/delete") && method == "POST")
                {
                    var id = Q(path, "instanceid");
                    return Response.Json(Hierarchy?.DeleteGameObject(id) ?? "{\"ok\":false}");
                }

                // GameObject header (active / name / static / tag / layer)
                if (path.StartsWith("/gameobject"))
                {
                    var id = Q(path, "instanceid");
                    if (method == "POST")
                        return Response.Json(Hierarchy?.ApplyGameObject(id, body) ?? "{\"ok\":false}");
                    return Response.Json(Hierarchy?.GetGameObject(id) ?? "{}");
                }

                // Inspector
                if (path.StartsWith("/inspect"))
                {
                    var id = Q(path, "instanceid");
                    return Response.Json(Inspector?.InspectGameObject(id) ?? "[]");
                }

                if (path.StartsWith("/component"))
                {
                    var id = Q(path, "instanceid");
                    var cid = Q(path, "componentid");
                    return Response.Json(Inspector?.GetComponent(id, cid) ?? "{}");
                }

                if (path.StartsWith("/fields"))
                {
                    var id = Q(path, "instanceid");
                    var cid = Q(path, "componentid");
                    var debug = Q(path, "debug") == "true";
                    return Response.Json(Inspector?.GetFields(id, cid, debug) ?? "[]");
                }

                if (path.StartsWith("/methods"))
                {
                    var id = Q(path, "instanceid");
                    var cid = Q(path, "componentid");
                    var debug = Q(path, "debug") == "true";
                    return Response.Json(Inspector?.GetMethods(id, cid, debug) ?? "[]");
                }

                if (path.StartsWith("/invoke") && method == "POST")
                {
                    var id = Q(path, "instanceid");
                    var cid = Q(path, "componentid");
                    var m = Q(path, "method");
                    var args = Q(path, "args");
                    return Response.Json(Inspector?.InvokeMethod(id, cid, m, args) ?? "{\"ok\":false}");
                }

                if (path.StartsWith("/apply") && method == "POST")
                {
                    var id = Q(path, "instanceid");
                    var cid = Q(path, "componentid");
                    return Response.Json(Inspector?.ApplyComponent(id, cid, body) ?? "{\"ok\":false}");
                }

                // IntelliSense
                if (path == "/types" || path.StartsWith("/types?"))
                    return Response.Json(Inspector?.GetTypes() ?? "[]");

                if (path == "/namespaces" || path.StartsWith("/namespaces?"))
                    return Response.Json(Inspector?.GetNamespaces() ?? "[]");

                if (path.StartsWith("/members"))
                {
                    var type = Q(path, "type");
                    return Response.Json(Inspector?.GetMembers(type) ?? "[]");
                }

                if (path.StartsWith("/signatures"))
                {
                    var type = Q(path, "type");
                    var m = Q(path, "method");
                    return Response.Json(Inspector?.GetSignatures(type, m) ?? "[]");
                }

                if (path.StartsWith("/resolve-element-type"))
                {
                    var chain = Q(path, "chain");
                    return Response.Json(Inspector?.ResolveElementType(chain) ?? "\"\"");
                }

                if (path.StartsWith("/resolve"))
                {
                    var chain = Q(path, "chain");
                    var info = Q(path, "info") == "true";
                    return Response.Json(Inspector?.ResolveChain(chain, info) ?? "{}");
                }

                // Performance
                if (path == "/perf" || path.StartsWith("/perf?"))
                    return Response.Json(Performance?.GetMetrics() ?? "{}");

                // Runtime settings (Time, Physics, Quality, Render, Audio, etc.)
                if (path.StartsWith("/settings"))
                {
                    if (Settings == null)
                        return Response.Error("Settings service not available", 501);

                    var subPath = path.Split('?')[0];

                    if (subPath == "/settings" || subPath == "/settings/")
                        return Response.Json(Settings.GetAll());

                    // /settings/<group> — GET reads, POST applies
                    if (subPath.StartsWith("/settings/"))
                    {
                        var group = subPath.Substring("/settings/".Length).TrimEnd('/');

                        // Layer collision matrix toggle: POST /settings/layers/collision
                        if (group == "layers/collision" && method == "POST")
                            return Response.Json(Settings.ApplyLayers(body));

                        if (method == "POST")
                        {
                            switch (group)
                            {
                                case "time":        return Response.Json(Settings.ApplyTime(body));
                                case "physics":     return Response.Json(Settings.ApplyPhysics(body));
                                case "physics2d":   return Response.Json(Settings.ApplyPhysics2D(body));
                                case "quality":     return Response.Json(Settings.ApplyQuality(body));
                                case "render":      return Response.Json(Settings.ApplyRender(body));
                                case "audio":       return Response.Json(Settings.ApplyAudio(body));
                                case "application": return Response.Json(Settings.ApplyApplication(body));
                                case "shader":      return Response.Json(Settings.ApplyShader(body));
                                default:            return Response.NotFound(path);
                            }
                        }

                        return Response.Json(Settings.GetGroup(group));
                    }

                    return Response.NotFound(path);
                }

                // Shader / material profiler
                if (path.StartsWith("/shader-profile"))
                {
                    if (ShaderProfiler == null)
                        return Response.Error("Shader profiler service not available", 501);

                    var subPath = path.Split('?')[0];

                    if (subPath == "/shader-profile/groups")
                    {
                        var by = Q(path, "by") ?? "shader";
                        return Response.Json(ShaderProfiler.ListGroups(by));
                    }

                    if (subPath == "/shader-profile/start" && method == "POST")
                    {
                        var by = JsonVal(body, "by") ?? "shader";
                        var secs = float.TryParse(JsonVal(body, "secondsPerGroup"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 3f;
                        var warm = int.TryParse(JsonVal(body, "warmupFrames"), out var w) ? w : 30;
                        var pauseStr = JsonVal(body, "pauseGame");
                        bool pause = pauseStr == null || pauseStr.ToLowerInvariant() != "false";
                        var keysCsv = JsonVal(body, "keysCsv");
                        return Response.Json(ShaderProfiler.BeginProfile(by, secs, warm, pause, keysCsv));
                    }

                    if (subPath == "/shader-profile/status")
                        return Response.Json(ShaderProfiler.GetStatus(Q(path, "jobId")));

                    if (subPath == "/shader-profile/result")
                        return Response.Json(ShaderProfiler.GetResult(Q(path, "jobId")));

                    if (subPath == "/shader-profile/cancel" && method == "POST")
                        return Response.Json(ShaderProfiler.Cancel(Q(path, "jobId") ?? JsonVal(body, "jobId")));

                    if (subPath == "/shader-profile/spotlight" && method == "POST")
                    {
                        var by = JsonVal(body, "by") ?? "shader";
                        var key = JsonVal(body, "key") ?? "";
                        return Response.Json(ShaderProfiler.Spotlight(by, key));
                    }

                    return Response.NotFound(path);
                }

                // Console
                if (path.StartsWith("/log"))
                {
                    var logId = int.TryParse(Q(path, "logId"), out var lid) ? lid : 0;
                    return Response.Json(Console?.GetLogs(logId) ?? "[]");
                }

                // Cameras
                if (path == "/cameras" || path.StartsWith("/cameras?"))
                    return Response.Json(ScreenCapture?.GetCameras() ?? "[]");

                // WebRTC device ICE candidates (browser polls this)
                if (path == "/webrtc-ice-candidates" || path.StartsWith("/webrtc-ice-candidates?"))
                {
                    if (Streamer == null) return Response.Json("{\"error\":\"Streaming unavailable — WebRTC not initialized\"}", 501);
                    return Response.Json(Streamer.DrainIceCandidates());
                }

                // WebRTC quality control
                if (path == "/webrtc-quality" && method == "POST")
                {
                    if (Streamer == null) return Response.Json("{\"error\":\"Streaming unavailable — WebRTC not initialized\"}", 501);
                    var w = int.TryParse(JsonVal(body, "width"), out var qw) ? qw : 960;
                    var h = int.TryParse(JsonVal(body, "height"), out var qh) ? qh : 540;
                    var f = int.TryParse(JsonVal(body, "fps"), out var qf) ? qf : 30;
                    return Response.Json(Streamer.SetQuality(w, h, f));
                }

                if (path == "/webrtc-quality" || path.StartsWith("/webrtc-quality?"))
                {
                    if (Streamer == null) return Response.Json("{\"error\":\"Streaming unavailable — WebRTC not initialized\"}", 501);
                    return Response.Json(Streamer.GetQuality());
                }

                // Capture — returns null, caller must handle with WaitForEndOfFrame
                if (path.StartsWith("/capture"))
                    return null;

                // WebRTC signaling — returns null, handled async by BugpunchClient.
                // If Streamer is null, BugpunchClient returns 501 gracefully.
                if (path.StartsWith("/webrtc-"))
                    return null;

                // Scene Camera
                if (path.StartsWith("/scene-camera"))
                {
                    if (SceneCamera == null)
                        return Response.Error("Scene camera service not available", 501);

                    var subPath = path.Split('?')[0];

                    if (subPath == "/scene-camera/start" && method == "POST")
                    {
                        int w = 0, h = 0;
                        if (!string.IsNullOrEmpty(body))
                        {
                            var wStr = JsonVal(body, "width");
                            var hStr = JsonVal(body, "height");
                            if (wStr != null) int.TryParse(wStr, out w);
                            if (hStr != null) int.TryParse(hStr, out h);
                        }
                        return Response.Json(SceneCamera.StartSceneCamera(w, h));
                    }

                    if (subPath == "/scene-camera/stop" && method == "POST")
                    {
                        return Response.Json(SceneCamera.StopSceneCamera());
                    }

                    if (subPath == "/scene-camera/aspect" && method == "POST")
                    {
                        var w = int.TryParse(JsonVal(body, "width"), out var aw) ? aw : 0;
                        var h = int.TryParse(JsonVal(body, "height"), out var ah) ? ah : 0;
                        return Response.Json(SceneCamera.SetAspect(w, h));
                    }

                    if (subPath == "/scene-camera/orbit" && method == "POST")
                    {
                        var dx = float.TryParse(JsonVal(body, "deltaX"), out var odx) ? odx : 0f;
                        var dy = float.TryParse(JsonVal(body, "deltaY"), out var ody) ? ody : 0f;
                        return Response.Json(SceneCamera.Orbit(dx, dy));
                    }

                    if (subPath == "/scene-camera/pan" && method == "POST")
                    {
                        var dx = float.TryParse(JsonVal(body, "deltaX"), out var pdx) ? pdx : 0f;
                        var dy = float.TryParse(JsonVal(body, "deltaY"), out var pdy) ? pdy : 0f;
                        return Response.Json(SceneCamera.Pan(dx, dy));
                    }

                    if (subPath == "/scene-camera/zoom" && method == "POST")
                    {
                        var d = float.TryParse(JsonVal(body, "delta"), out var zd) ? zd : 0f;
                        return Response.Json(SceneCamera.Zoom(d));
                    }

                    if (subPath == "/scene-camera/transform" && method == "POST")
                    {
                        var px = float.TryParse(JsonVal(body, "px"), out var tpx) ? tpx : 0f;
                        var py = float.TryParse(JsonVal(body, "py"), out var tpy) ? tpy : 0f;
                        var pz = float.TryParse(JsonVal(body, "pz"), out var tpz) ? tpz : 0f;
                        var rx = float.TryParse(JsonVal(body, "rx"), out var trx) ? trx : 0f;
                        var ry = float.TryParse(JsonVal(body, "ry"), out var try_) ? try_ : 0f;
                        var rz = float.TryParse(JsonVal(body, "rz"), out var trz) ? trz : 0f;
                        return Response.Json(SceneCamera.UpdateTransform(
                            new Vector3(tpx, tpy, tpz), new Vector3(trx, try_, trz)));
                    }

                    if (subPath == "/scene-camera/focus" && method == "POST")
                    {
                        var id = int.TryParse(JsonVal(body, "instanceId"), out var fid) ? fid : 0;
                        return Response.Json(SceneCamera.FocusOn(id));
                    }

                    if (subPath == "/scene-camera/look" && method == "POST")
                    {
                        var dx = float.TryParse(JsonVal(body, "deltaX"), out var ldx) ? ldx : 0f;
                        var dy = float.TryParse(JsonVal(body, "deltaY"), out var ldy) ? ldy : 0f;
                        return Response.Json(SceneCamera.Look(dx, dy));
                    }

                    if (subPath == "/scene-camera/fly" && method == "POST")
                    {
                        var fwd = float.TryParse(JsonVal(body, "forward"), out var ff) ? ff : 0f;
                        var rt = float.TryParse(JsonVal(body, "right"), out var fr) ? fr : 0f;
                        var up = float.TryParse(JsonVal(body, "up"), out var fu) ? fu : 0f;
                        var spd = float.TryParse(JsonVal(body, "speed"), out var fs) ? fs : 1f;
                        return Response.Json(SceneCamera.Fly(fwd, rt, up, spd));
                    }

                    if (subPath == "/scene-camera/state")
                    {
                        return Response.Json(SceneCamera.GetState());
                    }

                    if (subPath == "/scene-camera/projection" && method == "POST")
                    {
                        return Response.Json(SceneCamera.ToggleProjection());
                    }

                    if (subPath == "/scene-camera/snap" && method == "POST")
                    {
                        var axis = JsonVal(body, "axis") ?? "front";
                        return Response.Json(SceneCamera.SnapToAxis(axis));
                    }

                    if (subPath == "/scene-camera/grid" && method == "POST")
                    {
                        var enabled = JsonVal(body, "enabled")?.ToLower() != "false";
                        return Response.Json(SceneCamera.SetGrid(enabled));
                    }

                    if (subPath == "/scene-camera/zoom-drag" && method == "POST")
                    {
                        var d = float.TryParse(JsonVal(body, "delta"), out var zdd) ? zdd : 0f;
                        return Response.Json(SceneCamera.ZoomDrag(d));
                    }

                    if (subPath == "/scene-camera/render-mode" && method == "POST")
                    {
                        var mode = JsonVal(body, "mode") ?? "default";
                        return Response.Json(SceneCamera.SetRenderMode(mode));
                    }

                    if (subPath == "/scene-camera/bounds")
                    {
                        var dist = float.TryParse(Q(path, "distance"), out var bd) ? bd : 200f;
                        var max = int.TryParse(Q(path, "max"), out var bm) ? bm : 500;
                        return Response.Json(SceneCamera.GetSceneBounds(dist, max));
                    }

                    if (subPath == "/scene-camera/colliders")
                    {
                        var dist = float.TryParse(Q(path, "distance"), out var cd) ? cd : 500f;
                        var max = int.TryParse(Q(path, "max"), out var cm) ? cm : 200;
                        var reset = Q(path, "reset") == "1";
                        return Response.Json(SceneCamera.GetColliders(dist, max, reset));
                    }

                    if (subPath == "/scene-camera/collider-transforms")
                    {
                        return Response.Json(SceneCamera.GetColliderTransforms());
                    }

                    if (subPath == "/scene-camera/raycast" && method == "POST")
                    {
                        var nx = float.TryParse(JsonVal(body, "x") ?? Q(path, "x"), out var rx) ? rx : 0.5f;
                        var ny = float.TryParse(JsonVal(body, "y") ?? Q(path, "y"), out var ry) ? ry : 0.5f;
                        return Response.Json(SceneCamera.Raycast(nx, ny));
                    }

                    return Response.NotFound(path);
                }

                // JSON action execution via ActionExecutor
                if (path == "/action" && method == "POST")
                    return null; // handled async — needs coroutine context

                // Script execution
                if (path == "/run" && method == "POST")
                {
                    if (ScriptRunner == null || !ScriptRunner.IsAvailable)
                        return Response.Error("Script execution not available", 501);
                    return Response.Json(ScriptRunner.Execute(body));
                }

                // Compile-only diagnostics for live editor squiggles
                if (path == "/diagnose" && method == "POST")
                {
                    if (ScriptRunner == null || !ScriptRunner.IsAvailable)
                        return Response.Error("Script execution not available", 501);
                    return Response.Json(ScriptRunner.Diagnose(body));
                }

                // Device info
                if (path == "/device-info" || path.StartsWith("/device-info?"))
                    return Response.Json(DeviceInfo?.GetDeviceInfo() ?? "{}");

                // Game config — resolved variables from the dashboard
                if (path == "/game-config" || path.StartsWith("/game-config?"))
                    return Response.Json(BugpunchClient.GetGameConfigJson());

                // Database plugins (device-side parsing for Siaqodb, Odin, etc.)
                if (path.StartsWith("/databases/"))
                {
                    if (DatabasePlugins == null)
                        return Response.Error("Database plugin registry not available", 501);
                    var subPath = path.Split('?')[0];
                    if (subPath == "/databases/providers")
                        return Response.Json(DatabasePlugins.ListProviders());
                    if (subPath == "/databases/parse")
                        return Response.Json(DatabasePlugins.Parse(Q(path, "path"), Q(path, "provider")));
                    return Response.NotFound(path);
                }

                // Memory snapshots
                if (path.StartsWith("/memory"))
                {
                    if (MemorySnapshots == null)
                        return Response.Error("Memory snapshot service not available", 501);

                    var subPath = path.Split('?')[0];

                    if (subPath == "/memory/snapshot" && method == "POST")
                        return Response.Json(MemorySnapshots.TakeSnapshot());

                    if (subPath == "/memory/status")
                        return Response.Json(MemorySnapshots.GetStatus());

                    if (subPath == "/memory/list")
                        return Response.Json(MemorySnapshots.ListSnapshots());

                    if (subPath == "/memory/delete" && method == "POST")
                        return Response.Json(MemorySnapshots.DeleteSnapshot(Q(path, "path") ?? JsonVal(body, "path")));

                    if (subPath == "/memory/stats")
                        return Response.Json(MemorySnapshots.GetMemoryStats());

                    if (subPath == "/memory/assets")
                    {
                        var assetType = Q(path, "type") ?? "texture";
                        var limitStr = Q(path, "limit");
                        int limit = 200;
                        if (!string.IsNullOrEmpty(limitStr)) int.TryParse(limitStr, out limit);
                        return Response.Json(MemorySnapshots.ListAssets(assetType, limit));
                    }

                    if (subPath == "/memory/users")
                    {
                        var assetType = Q(path, "type") ?? "texture";
                        var idStr = Q(path, "id");
                        int id = 0;
                        if (!string.IsNullOrEmpty(idStr)) int.TryParse(idStr, out id);
                        return Response.Json(MemorySnapshots.GetAssetUsers(assetType, id));
                    }

                    return Response.NotFound(path);
                }

                // PlayerPrefs
                if (path.StartsWith("/playerprefs"))
                {
                    if (PlayerPrefs == null)
                        return Response.Error("PlayerPrefs service not available", 501);

                    var subPath = path.Split('?')[0];

                    if (subPath == "/playerprefs/list" || subPath == "/playerprefs")
                        return Response.Json(PlayerPrefs.GetAll());

                    if (subPath == "/playerprefs/set" && method == "POST")
                    {
                        var key = Q(path, "key") ?? JsonVal(body, "key");
                        var type = Q(path, "type") ?? JsonVal(body, "type") ?? "string";
                        var value = Q(path, "value") ?? JsonVal(body, "value") ?? "";
                        return Response.Json(PlayerPrefs.SetPref(key, type, value));
                    }

                    if (subPath == "/playerprefs/delete" && method == "POST")
                    {
                        var key = Q(path, "key") ?? JsonVal(body, "key");
                        return Response.Json(PlayerPrefs.DeletePref(key));
                    }

                    if (subPath == "/playerprefs/delete-all" && method == "POST")
                        return Response.Json(PlayerPrefs.DeleteAll());

                    return Response.NotFound(path);
                }

                // Textures
                if (path.StartsWith("/textures"))
                {
                    if (Textures == null)
                        return Response.Error("Texture service not available", 501);

                    var subPath = path.Split('?')[0];

                    if (subPath == "/textures/list" || subPath == "/textures")
                    {
                        var filter = Q(path, "filter");
                        var typeFilter = Q(path, "type");
                        return Response.Json(Textures.ListTextures(filter, typeFilter));
                    }

                    if (subPath == "/textures/thumbnail")
                    {
                        var id = int.TryParse(Q(path, "id"), out var tid) ? tid : 0;
                        var maxSize = int.TryParse(Q(path, "maxSize"), out var ms) ? ms : 128;
                        var quality = int.TryParse(Q(path, "quality"), out var tq) ? tq : 75;
                        var jpeg = Textures.GetThumbnail(id, maxSize, quality);
                        if (jpeg != null) return Response.Binary(jpeg, "image/jpeg");
                        return Response.Error("Failed to generate thumbnail", 500);
                    }

                    if (subPath == "/textures/full")
                    {
                        var id = int.TryParse(Q(path, "id"), out var fid) ? fid : 0;
                        var scale = float.TryParse(Q(path, "scale"), out var fs) ? fs : 1f;
                        var quality = int.TryParse(Q(path, "quality"), out var fq) ? fq : 85;
                        var jpeg = Textures.GetFullTexture(id, scale, quality);
                        if (jpeg != null) return Response.Binary(jpeg, "image/jpeg");
                        return Response.Error("Failed to capture texture", 500);
                    }

                    if (subPath == "/textures/info")
                    {
                        var id = int.TryParse(Q(path, "id"), out var iid) ? iid : 0;
                        return Response.Json(Textures.GetTextureInfo(iid));
                    }

                    return Response.NotFound(path);
                }

                // Materials
                if (path.StartsWith("/materials"))
                {
                    if (Materials == null)
                        return Response.Error("Material service not available", 501);

                    var subPath = path.Split('?')[0];

                    if (subPath == "/materials/list" || subPath == "/materials")
                        return Response.Json(Materials.ListMaterials());

                    if (subPath == "/materials/textures")
                    {
                        var id = int.TryParse(Q(path, "id"), out var mid) ? mid : 0;
                        return Response.Json(Materials.GetTextureProperties(id));
                    }

                    if (subPath == "/materials/properties")
                    {
                        var id = int.TryParse(Q(path, "id"), out var mid) ? mid : 0;
                        return Response.Json(Materials.GetProperties(id));
                    }

                    if (subPath == "/materials/set-property" && method == "POST")
                    {
                        var id = int.TryParse(Q(path, "id") ?? JsonVal(body, "id"), out var mid) ? mid : 0;
                        var name = Q(path, "name") ?? JsonVal(body, "name");
                        var type = Q(path, "type") ?? JsonVal(body, "type");
                        return Response.Json(Materials.SetProperty(id, name, type, body));
                    }

                    if (subPath == "/materials/set-keyword" && method == "POST")
                    {
                        var id = int.TryParse(Q(path, "id") ?? JsonVal(body, "id"), out var mid) ? mid : 0;
                        var keyword = Q(path, "keyword") ?? JsonVal(body, "keyword");
                        var enabledStr = Q(path, "enabled") ?? JsonVal(body, "enabled");
                        var enabled = string.Equals(enabledStr, "true", StringComparison.OrdinalIgnoreCase);
                        return Response.Json(Materials.SetKeyword(id, keyword, enabled));
                    }

                    if (subPath == "/materials/render-queue" && method == "POST")
                    {
                        var id = int.TryParse(Q(path, "id") ?? JsonVal(body, "id"), out var mid) ? mid : 0;
                        var q = int.TryParse(Q(path, "queue") ?? JsonVal(body, "queue"), out var qv) ? qv : -1;
                        return Response.Json(Materials.SetRenderQueue(id, q));
                    }

                    // Thumbnail + texture need rendering — return null for async handling
                    if (subPath == "/materials/thumbnail")
                        return null;

                    if (subPath == "/materials/texture")
                        return null;

                    return Response.NotFound(path);
                }

                // File browser
                if (path.StartsWith("/files"))
                {
                    if (Files == null)
                        return Response.Error("File service not available", 501);

                    var subPath = path.Split('?')[0];

                    if (subPath == "/files/paths")
                        return Response.Json(Files.GetPaths());

                    if (subPath == "/files/list")
                        return Response.Json(Files.ListDirectory(Q(path, "path")));

                    if (subPath == "/files/read")
                    {
                        var maxBytes = int.TryParse(Q(path, "maxBytes"), out var mb) ? mb : 1048576;
                        return Response.Json(Files.ReadFile(Q(path, "path"), maxBytes));
                    }

                    if (subPath == "/files/write" && method == "POST")
                    {
                        var isBase64 = Q(path, "base64") == "true";
                        return Response.Json(Files.WriteFile(Q(path, "path"), body, isBase64));
                    }

                    if (subPath == "/files/delete" && method == "POST")
                    {
                        var recursive = Q(path, "recursive") == "true";
                        return Response.Json(Files.DeletePath(Q(path, "path"), recursive));
                    }

                    if (subPath == "/files/mkdir" && method == "POST")
                        return Response.Json(Files.CreateDirectory(Q(path, "path")));

                    if (subPath == "/files/info")
                        return Response.Json(Files.GetFileInfo(Q(path, "path")));

                    if (subPath == "/files/zip/start" && method == "POST")
                        return Response.Json(Files.StartZipJob(Q(path, "path"), Q(path, "excludeDirPrefixes")));

                    if (subPath == "/files/zip/progress")
                        return Response.Json(Files.GetZipProgress(Q(path, "jobId")));

                    if (subPath == "/files/zip/result")
                        return Response.Json(Files.GetZipResult(Q(path, "jobId")));

                    if (subPath == "/files/unzip" && method == "POST")
                    {
                        var clearFirst = Q(path, "clear") != "false";
                        return Response.Json(Files.UnzipToDirectory(Q(path, "path"), body, clearFirst, Q(path, "preserveDirPrefixes")));
                    }

                    if (subPath == "/files/prefs/export" && method == "POST")
                        return Response.Json(Files.ExportPlayerPrefs(body));

                    if (subPath == "/files/prefs/import" && method == "POST")
                    {
                        var clear = Q(path, "clear") == "true";
                        return Response.Json(Files.ImportPlayerPrefs(body, clear));
                    }

                    return Response.NotFound(path);
                }

                // Test automation
                if (path == "/test/reset" && method == "POST")
                {
                    return Response.Json(InvokeTestResetMethods());
                }

                if (path.StartsWith("/test/reload-scene") && method == "POST")
                {
                    var sceneName = Q(path, "name") ?? JsonVal(body, "name");
                    if (string.IsNullOrEmpty(sceneName))
                        return Response.Error("Scene name required", 400);
                    try
                    {
                        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
                        return Response.Json("{\"ok\":true}");
                    }
                    catch (Exception ex)
                    {
                        return Response.Error(ex.Message, 500);
                    }
                }

                // Variable Watch
                if (path.StartsWith("/watch"))
                {
                    if (Watch == null)
                        return Response.Error("Watch service not available", 501);

                    var subPath = path.Split('?')[0];

                    if (subPath == "/watch/search")
                    {
                        var q = Q(path, "q") ?? "";
                        var max = int.TryParse(Q(path, "max"), out var m) ? m : 50;
                        return Response.Json(Watch.Search(q, max));
                    }

                    if (subPath == "/watch/add" && method == "POST")
                    {
                        return Response.Json(Watch.AddWatch(
                            Q(path, "instanceid"),
                            Q(path, "componentid"),
                            Q(path, "field"),
                            Q(path, "isproperty") ?? "false"));
                    }

                    if (subPath == "/watch/remove" && method == "POST")
                        return Response.Json(Watch.RemoveWatch(Q(path, "id")));

                    if (subPath == "/watch/clear" && method == "POST")
                        return Response.Json(Watch.ClearAll());

                    if (subPath == "/watch/list")
                        return Response.Json(Watch.GetWatchList());

                    if (subPath == "/watch/poll")
                        return Response.Json(Watch.Poll());

                    if (subPath == "/watch/rescan" && method == "POST")
                        return Response.Json(Watch.Rescan());

                    if (subPath == "/watch/apply" && method == "POST")
                    {
                        return Response.Json(Watch.ApplyValue(
                            Q(path, "instanceid"),
                            Q(path, "componentid"),
                            Q(path, "field"),
                            body));
                    }

                    if (subPath == "/watch/apply-batch" && method == "POST")
                        return Response.Json(Watch.ApplyBatch(body));

                    return Response.NotFound(path);
                }

                // Input — live touch polling + injection
                if (path.StartsWith("/input/"))
                {
                    // GET /input/touches?trail=500 — poll native OS touch data
                    if ((path == "/input/touches" || path.StartsWith("/input/touches?")) && method == "GET")
                    {
                        var trail = int.TryParse(Q(path, "trail"), out var t) ? t : 500;
                        return Response.Json(GetLiveTouches(trail));
                    }

                    // POST — tap/swipe injection, handled async by BugpunchClient
                    if (method == "POST")
                        return null;

                    return Response.NotFound(path);
                }

                return Response.NotFound(path);
            }
            catch (Exception ex)
            {
                BugpunchNative.ReportSdkError($"RequestRouter.Route({path})", ex);
                return Response.Error(ex.Message);
            }
        }

        /// <summary>
        /// Handle material thumbnail request. Renders material on a sphere.
        /// </summary>
        public Response HandleMaterialThumbnail(string path)
        {
            var id = int.TryParse(Q(path, "id"), out var mid) ? mid : 0;
            var size = int.TryParse(Q(path, "size"), out var ms) ? ms : 128;
            var quality = int.TryParse(Q(path, "quality"), out var mq) ? mq : 80;
            var shape = Q(path, "shape");
            var jpeg = Materials?.RenderThumbnail(id, size, quality, shape);
            if (jpeg != null) return Response.Binary(jpeg, "image/jpeg");
            return Response.Error("Failed to render material thumbnail", 500);
        }

        /// <summary>
        /// Handle material texture request. Extracts a texture from a material.
        /// </summary>
        public Response HandleMaterialTexture(string path)
        {
            var id = int.TryParse(Q(path, "id"), out var mid) ? mid : 0;
            var prop = Q(path, "property");
            var maxSize = int.TryParse(Q(path, "maxSize"), out var ms) ? ms : 1024;
            var png = Materials?.GetTexture(id, prop, maxSize);
            if (png != null) return Response.Binary(png, "image/png");
            return Response.Error("Failed to extract texture", 500);
        }

        /// <summary>
        /// Find and invoke all static methods marked with [BugpunchTestReset].
        /// </summary>
        static string InvokeTestResetMethods()
        {
            int called = 0;
            var errors = new System.Text.StringBuilder();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                        {
                            if (method.GetCustomAttributes(typeof(BugpunchTestResetAttribute), false).Length > 0)
                            {
                                try
                                {
                                    method.Invoke(null, null);
                                    called++;
                                    BugpunchLog.Info("RequestRouter", $"TestReset: called {type.Name}.{method.Name}()");
                                }
                                catch (Exception ex)
                                {
                                    errors.Append($"{type.Name}.{method.Name}: {ex.InnerException?.Message ?? ex.Message}; ");
                                }
                            }
                        }
                    }
                }
                catch { /* skip assemblies that can't be reflected */ }
            }
            if (errors.Length > 0)
                return $"{{\"ok\":false,\"called\":{called},\"error\":\"{EscapeJson(errors.ToString())}\"}}";
            return $"{{\"ok\":true,\"called\":{called}}}";
        }

        /// <summary>
        /// Handle capture request. Must be called after WaitForEndOfFrame.
        /// </summary>
        public Response HandleCapture(string path, float defaultScale, int defaultQuality)
        {
            var scale = float.TryParse(Q(path, "scale"), out var s) ? s : defaultScale;
            var quality = int.TryParse(Q(path, "quality"), out var q) ? q : defaultQuality;
            var cameraId = Q(path, "id");

            byte[] jpeg;
            if (cameraId != null && int.TryParse(cameraId, out var camId))
                jpeg = ScreenCapture?.CaptureFromCamera(camId, scale, quality);
            else
                jpeg = ScreenCapture?.CaptureScreen(scale, quality);

            if (jpeg != null)
                return Response.Binary(jpeg, "image/jpeg");

            return Response.Error("Capture failed", 500);
        }

        // ── Native touch bridge ─────────────────────────────────────────────

#if !UNITY_EDITOR && UNITY_IOS
        [DllImport("__Internal")] static extern IntPtr BugpunchTouch_GetLiveTouches(int trailMs);
        [DllImport("__Internal")] static extern void BugpunchTouch_FreeJson(IntPtr json);
#endif

        /// <summary>
        /// Get touch data formatted for embedding in the WebRTC data channel.
        /// Returns JSON fields (no outer braces): "touches":[...],"tw":1080,"th":1920
        /// </summary>
        internal static string GetLiveTouchesForStream(int trailMs)
        {
            var full = GetLiveTouches(trailMs);
            // Parse the events array and dimensions from the full JSON
            // Full format: {"events":[...],"w":1080,"h":1920}
            // We need:     "touches":[...],"tw":1080,"th":1920
            try
            {
                // Extract events array
                var evIdx = full.IndexOf("\"events\":", StringComparison.Ordinal);
                if (evIdx < 0) return "\"touches\":[],\"tw\":0,\"th\":0";
                var arrStart = full.IndexOf('[', evIdx);
                if (arrStart < 0) return "\"touches\":[],\"tw\":0,\"th\":0";
                // Find matching ]
                int depth = 0;
                int arrEnd = -1;
                for (int i = arrStart; i < full.Length; i++)
                {
                    if (full[i] == '[') depth++;
                    else if (full[i] == ']') { depth--; if (depth == 0) { arrEnd = i; break; } }
                }
                if (arrEnd < 0) return "\"touches\":[],\"tw\":0,\"th\":0";

                var eventsArr = full.Substring(arrStart, arrEnd - arrStart + 1);

                // Extract w and h
                var w = JsonVal(full, "w") ?? "0";
                var h = JsonVal(full, "h") ?? "0";

                return $"\"touches\":{eventsArr},\"tw\":{w},\"th\":{h}";
            }
            catch
            {
                return "\"touches\":[],\"tw\":0,\"th\":0";
            }
        }

        /// <summary>
        /// Get live OS touch data from native touch recorder.
        /// Android: calls BugpunchTouchRecorder.getLiveTouches via JNI.
        /// iOS: calls BugpunchTouch_GetLiveTouches via P/Invoke.
        /// Editor: returns mouse position as a single touch.
        /// </summary>
        static string GetLiveTouches(int trailMs)
        {
#if UNITY_EDITOR
            // Editor fallback: report mouse as a single touch
            var sb = new StringBuilder(256);
            sb.Append("{\"events\":[");
            if (Input.GetMouseButton(0))
            {
                var pos = Input.mousePosition;
                sb.Append($"{{\"t\":0,\"id\":0,\"x\":{pos.x:F1},\"y\":{(Screen.height - pos.y):F1},\"phase\":\"moved\"}}");
            }
            sb.Append($"],\"w\":{Screen.width},\"h\":{Screen.height}}}");
            return sb.ToString();
#elif UNITY_ANDROID
            try
            {
                using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchTouchRecorder");
                return cls.CallStatic<string>("getLiveTouches", trailMs)
                       ?? $"{{\"events\":[],\"w\":{Screen.width},\"h\":{Screen.height}}}";
            }
            catch (Exception e)
            {
                BugpunchNative.ReportSdkError("RequestRouter.getLiveTouches", e);
                return $"{{\"events\":[],\"w\":{Screen.width},\"h\":{Screen.height}}}";
            }
#elif UNITY_IOS
            try
            {
                var ptr = BugpunchTouch_GetLiveTouches(trailMs);
                if (ptr == IntPtr.Zero)
                    return $"{{\"events\":[],\"w\":{Screen.width},\"h\":{Screen.height}}}";
                var json = Marshal.PtrToStringAnsi(ptr);
                BugpunchTouch_FreeJson(ptr);
                return json ?? $"{{\"events\":[],\"w\":{Screen.width},\"h\":{Screen.height}}}";
            }
            catch (Exception e)
            {
                BugpunchNative.ReportSdkError("RequestRouter.getLiveTouches", e);
                return $"{{\"events\":[],\"w\":{Screen.width},\"h\":{Screen.height}}}";
            }
#else
            return $"{{\"events\":[],\"w\":{Screen.width},\"h\":{Screen.height}}}";
#endif
        }

        public static string Q(string path, string key)
        {
            var qi = path.IndexOf('?');
            if (qi < 0) return null;
            foreach (var pair in path.Substring(qi + 1).Split('&'))
            {
                var kv = pair.Split('=');
                if (kv.Length == 2 && kv[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(kv[1]);
            }
            return null;
        }

        static string Esc(string s) => EscapeJson(s);

        /// <summary>
        /// Escape a string for safe inclusion in a JSON string value.
        /// </summary>
        public static string EscapeJson(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") ?? "";

        /// <summary>
        /// Minimal JSON value extractor for flat objects. Returns the raw string value for a key.
        /// Handles strings, numbers, booleans. Not a full parser — good enough for simple request bodies.
        /// </summary>
        public static string JsonVal(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var needle = $"\"{key}\"";
            var ki = json.IndexOf(needle, StringComparison.Ordinal);
            if (ki < 0) return null;

            var ci = json.IndexOf(':', ki + needle.Length);
            if (ci < 0) return null;

            // Skip whitespace after colon
            var vi = ci + 1;
            while (vi < json.Length && (json[vi] == ' ' || json[vi] == '\t')) vi++;
            if (vi >= json.Length) return null;

            if (json[vi] == '"')
            {
                // String value
                var end = json.IndexOf('"', vi + 1);
                return end > vi ? json.Substring(vi + 1, end - vi - 1) : null;
            }

            // Number or boolean — read until comma, brace, or end
            var start = vi;
            while (vi < json.Length && json[vi] != ',' && json[vi] != '}' && json[vi] != ']')
                vi++;
            return json.Substring(start, vi - start).Trim();
        }
    }
}
