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
        public IScriptRunner ScriptRunner;
        public WebRTCStreamer Streamer;

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

                // Console
                if (path.StartsWith("/log"))
                {
                    var logId = int.TryParse(Q(path, "logId"), out var lid) ? lid : 0;
                    return Response.Json(Console?.GetLogs(logId) ?? "[]");
                }

                // Cameras
                if (path == "/cameras" || path.StartsWith("/cameras?"))
                    return Response.Json(ScreenCapture?.GetCameras() ?? "[]");

                // Capture — returns null, caller must handle with WaitForEndOfFrame
                if (path.StartsWith("/capture"))
                    return null;

                // WebRTC signaling — returns null, handled async by BugpunchClient
                if (path.StartsWith("/webrtc-"))
                    return null;

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

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") ?? "";
    }
}
