using System;
using System.Collections;
using UnityEngine;

namespace ODDGames.UIAutomation.DeviceConnect
{
    /// <summary>
    /// Main OddDev client — manages connection to the server and device services.
    /// Auto-creates on play if config has autoConnect enabled.
    /// </summary>
    public class OddDevClient : MonoBehaviour
    {
        public static OddDevClient Instance { get; private set; }

        public OddDevConfig Config { get; private set; }
        public TunnelClient Tunnel { get; private set; }
        public bool IsConnected => Tunnel != null && Tunnel.IsConnected;

        // Services
        public HierarchyService Hierarchy { get; private set; }
        public ConsoleService Console { get; private set; }
        public ScreenCaptureService ScreenCapture { get; private set; }
        public InspectorService Inspector { get; private set; }

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnError;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInitialize()
        {
            var config = OddDevConfig.Load();
            if (config == null || !config.autoConnect) return;
            if (string.IsNullOrEmpty(config.serverUrl) || string.IsNullOrEmpty(config.apiKey)) return;

            Initialize(config);
        }

        /// <summary>
        /// Initialize and connect with the given config.
        /// </summary>
        public static OddDevClient Initialize(OddDevConfig config)
        {
            if (Instance != null)
            {
                Debug.LogWarning("[OddDev] Already initialized");
                return Instance;
            }

            var go = new GameObject("[OddDev Client]");
            DontDestroyOnLoad(go);

            var client = go.AddComponent<OddDevClient>();
            client.Config = config;
            Instance = client;

            client.Setup();
            return client;
        }

        void Setup()
        {
            Debug.Log($"[OddDev] Initializing — server: {Config.serverUrl}");

            // Create tunnel
            Tunnel = new TunnelClient(Config);
            Tunnel.OnConnected += HandleConnected;
            Tunnel.OnDisconnected += HandleDisconnected;
            Tunnel.OnError += HandleError;
            Tunnel.OnRequest += HandleRequest;

            // Create services
            if (Config.enableHierarchy)
                Hierarchy = new HierarchyService();
            if (Config.enableConsole)
                Console = new ConsoleService();
            if (Config.enableScreenCapture)
                ScreenCapture = gameObject.AddComponent<ScreenCaptureService>();
            if (Config.enableInspector)
                Inspector = new InspectorService();

            // Connect
            StartCoroutine(ConnectLoop());
        }

        IEnumerator ConnectLoop()
        {
            while (true)
            {
                if (!Tunnel.IsConnected && !Tunnel.IsConnecting)
                {
                    Tunnel.Connect();
                }
                yield return new WaitForSeconds(Config.reconnectDelay);
            }
        }

        void Update()
        {
            Tunnel?.ProcessMessages();
        }

        void HandleConnected()
        {
            Debug.Log("[OddDev] Connected to server");
            OnConnected?.Invoke();
        }

        void HandleDisconnected()
        {
            Debug.Log("[OddDev] Disconnected from server");
            OnDisconnected?.Invoke();
        }

        void HandleError(string error)
        {
            Debug.LogError($"[OddDev] Error: {error}");
            OnError?.Invoke(error);
        }

        /// <summary>
        /// Handle incoming request from server (proxied from dashboard user)
        /// </summary>
        void HandleRequest(string requestId, string method, string path, string body)
        {
            StartCoroutine(ProcessRequest(requestId, method, path, body));
        }

        IEnumerator ProcessRequest(string requestId, string method, string path, string body)
        {
            string responseBody = null;
            int status = 200;
            string contentType = "application/json";

            try
            {
                // Route to appropriate service
                if (path.StartsWith("/hierarchy"))
                {
                    responseBody = Hierarchy?.GetHierarchy();
                }
                else if (path.StartsWith("/inspect"))
                {
                    var instanceId = GetQueryParam(path, "instanceid");
                    responseBody = Inspector?.InspectGameObject(instanceId);
                }
                else if (path.StartsWith("/component"))
                {
                    var instanceId = GetQueryParam(path, "instanceid");
                    var componentId = GetQueryParam(path, "componentid");
                    responseBody = Inspector?.GetComponent(instanceId, componentId);
                }
                else if (path.StartsWith("/fields"))
                {
                    var instanceId = GetQueryParam(path, "instanceid");
                    var componentId = GetQueryParam(path, "componentid");
                    var debug = GetQueryParam(path, "debug") == "true";
                    responseBody = Inspector?.GetFields(instanceId, componentId, debug);
                }
                else if (path.StartsWith("/methods"))
                {
                    var instanceId = GetQueryParam(path, "instanceid");
                    var componentId = GetQueryParam(path, "componentid");
                    var debug = GetQueryParam(path, "debug") == "true";
                    responseBody = Inspector?.GetMethods(instanceId, componentId, debug);
                }
                else if (path.StartsWith("/invoke"))
                {
                    var instanceId = GetQueryParam(path, "instanceid");
                    var componentId = GetQueryParam(path, "componentid");
                    var methodName = GetQueryParam(path, "method");
                    var args = GetQueryParam(path, "args");
                    responseBody = Inspector?.InvokeMethod(instanceId, componentId, methodName, args);
                }
                else if (path.StartsWith("/capture"))
                {
                    var scale = float.TryParse(GetQueryParam(path, "scale"), out var s) ? s : Config.captureScale;
                    var quality = int.TryParse(GetQueryParam(path, "quality"), out var q) ? q : Config.captureQuality;
                    // Need to wait for end of frame for screen capture
                    yield return new WaitForEndOfFrame();
                    var jpegBytes = ScreenCapture?.CaptureScreen(scale, quality);
                    if (jpegBytes != null)
                    {
                        Tunnel.SendBinaryResponse(requestId, jpegBytes, "image/jpeg");
                        yield break;
                    }
                    status = 404;
                    responseBody = "{\"error\":\"Screen capture not available\"}";
                }
                else if (path.StartsWith("/log"))
                {
                    var logIdStr = GetQueryParam(path, "logId");
                    var logId = int.TryParse(logIdStr, out var lid) ? lid : 0;
                    responseBody = Console?.GetLogs(logId);
                }
                else if (path.StartsWith("/cameras"))
                {
                    responseBody = ScreenCapture?.GetCameras();
                }
                else if (path == "/run" && method == "POST")
                {
                    // C# script execution — for security, only if enabled
                    if (Config.enableScriptRunner)
                    {
                        responseBody = "{\"error\":\"Script runner not yet implemented\"}";
                        status = 501;
                    }
                    else
                    {
                        responseBody = "{\"error\":\"Script execution disabled\"}";
                        status = 403;
                    }
                }
                else if (path.StartsWith("/types"))
                {
                    responseBody = Inspector?.GetTypes();
                }
                else if (path.StartsWith("/namespaces"))
                {
                    responseBody = Inspector?.GetNamespaces();
                }
                else if (path.StartsWith("/members"))
                {
                    var type = GetQueryParam(path, "type");
                    responseBody = Inspector?.GetMembers(type);
                }
                else if (path.StartsWith("/signatures"))
                {
                    var type = GetQueryParam(path, "type");
                    var methodName = GetQueryParam(path, "method");
                    responseBody = Inspector?.GetSignatures(type, methodName);
                }
                else if (path.StartsWith("/scenes"))
                {
                    responseBody = Hierarchy?.GetScenes();
                }
                else
                {
                    status = 404;
                    responseBody = $"{{\"error\":\"Unknown path: {path}\"}}";
                }
            }
            catch (Exception ex)
            {
                status = 500;
                responseBody = $"{{\"error\":\"{EscapeJson(ex.Message)}\"}}";
                Debug.LogError($"[OddDev] Request error ({path}): {ex}");
            }

            Tunnel.SendResponse(requestId, status, responseBody ?? "{}", contentType);
        }

        static string GetQueryParam(string path, string key)
        {
            var queryStart = path.IndexOf('?');
            if (queryStart < 0) return null;

            var query = path.Substring(queryStart + 1);
            foreach (var pair in query.Split('&'))
            {
                var kv = pair.Split('=');
                if (kv.Length == 2 && kv[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(kv[1]);
            }
            return null;
        }

        static string EscapeJson(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

        void OnDestroy()
        {
            Tunnel?.Disconnect();
            Instance = null;
        }
    }
}
