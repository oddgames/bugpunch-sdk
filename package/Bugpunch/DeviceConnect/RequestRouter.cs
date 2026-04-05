using System;
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
        public WebRTCStreamer Streamer;
        public SceneCameraService SceneCamera;

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

                if (path.StartsWith("/resolve"))
                {
                    var chain = Q(path, "chain");
                    var info = Q(path, "info") == "true";
                    return Response.Json(Inspector?.ResolveChain(chain, info) ?? "{}");
                }

                // Performance
                if (path == "/perf" || path.StartsWith("/perf?"))
                    return Response.Json(Performance?.GetMetrics() ?? "{}");

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
                    return Response.Json(Streamer?.DrainIceCandidates() ?? "[]");

                // Capture — returns null, caller must handle with WaitForEndOfFrame
                if (path.StartsWith("/capture"))
                    return null;

                // WebRTC signaling — returns null, handled async by BugpunchClient
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
                        return Response.Json(SceneCamera.StartSceneCamera());
                    }

                    if (subPath == "/scene-camera/stop" && method == "POST")
                    {
                        return Response.Json(SceneCamera.StopSceneCamera());
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

                    if (subPath == "/scene-camera/state")
                    {
                        return Response.Json(SceneCamera.GetState());
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

                return Response.NotFound(path);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Bugpunch] Route error ({path}): {ex}");
                return Response.Error(ex.Message);
            }
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

        static string Q(string path, string key)
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
        static string JsonVal(string json, string key)
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
