using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Main Bugpunch client. Auto-connects to the server, routes requests to services.
    /// </summary>
    public class BugpunchClient : MonoBehaviour
    {
        public static BugpunchClient Instance { get; private set; }

        public BugpunchConfig Config { get; private set; }
        public TunnelClient Tunnel { get; private set; }
        public RequestRouter Router { get; private set; }
        public WebRTCStreamer Streamer { get; private set; }
        public SceneCameraService SceneCamera { get; private set; }
        public BugReporter Reporter { get; private set; }
        public DeviceRegistration Registration { get; private set; }
        public bool IsConnected => (Tunnel != null && Tunnel.IsConnected) || (Registration != null && Registration.IsRegistered);

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnError;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInitialize()
        {
            var config = BugpunchConfig.Load();
            if (config == null || !config.autoConnect) return;
            if (string.IsNullOrEmpty(config.serverUrl) || string.IsNullOrEmpty(config.apiKey)) return;

            Initialize(config);
        }

        public static BugpunchClient Initialize(BugpunchConfig config)
        {
            if (Instance != null)
            {
                Debug.LogWarning("[Bugpunch] Already initialized");
                return Instance;
            }

            var go = new GameObject("[Bugpunch Client]");
            DontDestroyOnLoad(go);

            var client = go.AddComponent<BugpunchClient>();
            client.Config = config;
            Instance = client;
            client.Setup();
            return client;
        }

        void Setup()
        {
            Debug.Log($"[Bugpunch] Initializing — server: {Config.serverUrl}");

            // Create services
            var hierarchy = Config.enableHierarchy ? new HierarchyService() : null;
            var console = Config.enableConsole ? new ConsoleService() : null;
            var screenCapture = Config.enableScreenCapture ? gameObject.AddComponent<ScreenCaptureService>() : null;
            var inspector = Config.enableInspector ? new InspectorService() : null;
            var perf = new PerformanceService();
            var files = new FileService();
            var deviceInfo = new DeviceInfoService();
            IScriptRunner scriptRunner = null;
#if BUGPUNCH_HAS_PAXSCRIPT
            if (Config.enableScriptRunner)
                scriptRunner = new PaxScriptRunner();
#endif

            // Create scene camera service
            SceneCamera = gameObject.AddComponent<SceneCameraService>();

            // Create router
            Router = new RequestRouter
            {
                Hierarchy = hierarchy,
                Console = console,
                ScreenCapture = screenCapture,
                Inspector = inspector,
                Performance = perf,
                ScriptRunner = scriptRunner,
                SceneCamera = SceneCamera,
                Files = files,
                DeviceInfo = deviceInfo,
                Streamer = null // set after streamer created
            };

            // Create bug reporter
            Reporter = gameObject.AddComponent<BugReporter>();
            Reporter.shakeToReport = Config.enableShakeToReport;
            Reporter.videoBufferSeconds = Config.videoBufferSeconds;
            Reporter.videoFps = Config.bugReportVideoFps;

            // Create WebRTC streamer
            Streamer = gameObject.AddComponent<WebRTCStreamer>();
            SceneCamera.SetStreamer(Streamer);

            if (Config.ShouldUseWebSocket)
            {
                // WebSocket debug mode — full tunnel for live features
                Tunnel = new TunnelClient(Config);
                Tunnel.OnConnected += () =>
                {
                    Debug.Log("[Bugpunch] Connected");
                    StartCoroutine(FetchIceServers());
                    OnConnected?.Invoke();
                };
                Tunnel.OnDisconnected += () => { Debug.Log("[Bugpunch] Disconnected"); OnDisconnected?.Invoke(); };
                Tunnel.OnError += e => { Debug.LogError($"[Bugpunch] {e}"); OnError?.Invoke(e); };
                Tunnel.OnRequest += HandleRequest;

                Router.Streamer = Streamer;
                Streamer.Initialize(Tunnel, Config.captureScale > 0 ? (int)(1920 * Config.captureScale) : 1280,
                    Config.captureScale > 0 ? (int)(1080 * Config.captureScale) : 720, Config.streamFps);

                StartCoroutine(ConnectLoop());
                Debug.Log("[Bugpunch] Starting in WebSocket debug mode");
            }
            else
            {
                // Poll mode — lightweight HTTP registration + polling
                Registration = new DeviceRegistration(Config);
                Registration.OnUpgradeRequested += HandleUpgradeToWebSocket;
                Registration.OnScriptsReceived += HandlePollScripts;
                StartCoroutine(Registration.RegisterAndPoll());
                Debug.Log("[Bugpunch] Starting in poll mode");
            }
        }

        IEnumerator ConnectLoop()
        {
            while (true)
            {
                if (!Tunnel.IsConnected && !Tunnel.IsConnecting)
                    Tunnel.Connect();
                yield return new WaitForSeconds(Config.reconnectDelay);
            }
        }

        void Update() => Tunnel?.ProcessMessages();

        void HandleRequest(string requestId, string method, string path, string body)
        {
            StartCoroutine(ProcessRequest(requestId, method, path, body));
        }

        IEnumerator ProcessRequest(string requestId, string method, string path, string body)
        {
            // Try synchronous route first
            var response = Router.Route(method, path, body);

            if (response == null && path.StartsWith("/capture"))
            {
                // Capture needs end of frame
                yield return new WaitForEndOfFrame();
                var captureResponse = Router.HandleCapture(path, Config.captureScale, Config.captureQuality);

                if (captureResponse.isBinary)
                    Tunnel.SendBinaryResponse(requestId, captureResponse.binaryBody, captureResponse.contentType);
                else
                    Tunnel.SendResponse(requestId, captureResponse.status, captureResponse.body, captureResponse.contentType);
                yield break;
            }

#if UNITY_INCLUDE_TESTS
            if (response == null && path == "/action" && method == "POST")
            {
                // Execute JSON action via ActionExecutor (async) — run on main thread
                var actionTask = ODDGames.Bugpunch.ActionExecutor.Execute(body);
                while (!actionTask.IsCompleted)
                    yield return null;

                if (actionTask.IsFaulted)
                {
                    var err = actionTask.Exception?.InnerException?.Message ?? "Unknown error";
                    Tunnel.SendResponse(requestId, 500,
                        $"{{\"ok\":false,\"error\":\"{RequestRouter.EscapeJson(err)}\",\"elapsedMs\":0}}",
                        "application/json");
                }
                else
                {
                    var result = actionTask.Result;
                    var okStr = result.Success ? "true" : "false";
                    var errStr = result.Error != null
                        ? $",\"error\":\"{RequestRouter.EscapeJson(result.Error)}\""
                        : "";
                    Tunnel.SendResponse(requestId, result.Success ? 200 : 422,
                        $"{{\"ok\":{okStr}{errStr},\"elapsedMs\":{result.ElapsedMs:F0}}}",
                        "application/json");
                }
                yield break;
            }
#endif

#if BUGPUNCH_WEBRTC
            if (response == null && path.StartsWith("/webrtc-"))
            {
                var type = path.Split('?')[0].TrimStart('/');
                if (type == "webrtc-offer" && Streamer != null)
                {
                    // Handle offer — response sent asynchronously via tunnel when answer is ready
                    var rid = requestId;
                    Streamer.HandleOfferAsync(body,
                        answer => Tunnel.SendResponse(rid, 200, answer, "application/json"),
                        err => Tunnel.SendResponse(rid, 500, $"{{\"error\":\"{err}\"}}", "application/json"));
                }
                else if (Streamer != null)
                {
                    Streamer.HandleSignalingMessage(type, requestId, body);
                    Tunnel.SendResponse(requestId, 200, "{\"ok\":true}", "application/json");
                }
                else
                {
                    Tunnel.SendResponse(requestId, 501, "{\"error\":\"WebRTC not available\"}", "application/json");
                }
                yield break;
            }
#endif

            // Input injection — needs main thread for Input System
            if (response == null && path.StartsWith("/input/"))
            {
                var subPath = path.Split('?')[0];
                System.Threading.Tasks.Task inputTask = null;
                string inputResponse = null;

                if (subPath == "/input/tap")
                {
                    var nx = Mathf.Clamp01(float.TryParse(RequestRouter.Q(path, "x") ?? RequestRouter.JsonVal(body, "x"), out var px) ? px : 0.5f);
                    var ny = Mathf.Clamp01(float.TryParse(RequestRouter.Q(path, "y") ?? RequestRouter.JsonVal(body, "y"), out var py) ? py : 0.5f);
                    var screenPos = new Vector2(nx * Screen.width, (1f - ny) * Screen.height);
                    inputTask = InputInjector.InjectPointerTap(screenPos);
                    inputResponse = $"{{\"ok\":true,\"screen\":[{screenPos.x:F0},{screenPos.y:F0}]}}";
                }
                else if (subPath == "/input/swipe")
                {
                    var x1 = Mathf.Clamp01(float.TryParse(RequestRouter.JsonVal(body, "x1"), out var sx1) ? sx1 : 0.5f);
                    var y1 = Mathf.Clamp01(float.TryParse(RequestRouter.JsonVal(body, "y1"), out var sy1) ? sy1 : 0.5f);
                    var x2 = Mathf.Clamp01(float.TryParse(RequestRouter.JsonVal(body, "x2"), out var sx2) ? sx2 : 0.5f);
                    var y2 = Mathf.Clamp01(float.TryParse(RequestRouter.JsonVal(body, "y2"), out var sy2) ? sy2 : 0.5f);
                    var from = new Vector2(x1 * Screen.width, (1f - y1) * Screen.height);
                    var to = new Vector2(x2 * Screen.width, (1f - y2) * Screen.height);
                    inputTask = InputInjector.InjectPointerDrag(from, to, 0.3f);
                    inputResponse = "{\"ok\":true}";
                }

                if (inputTask != null)
                {
                    while (!inputTask.IsCompleted) yield return null;
                    if (inputTask.IsFaulted)
                        Tunnel.SendResponse(requestId, 500, $"{{\"ok\":false,\"error\":\"{RequestRouter.EscapeJson(inputTask.Exception?.InnerException?.Message ?? "Unknown")}\"}}", "application/json");
                    else
                        Tunnel.SendResponse(requestId, 200, inputResponse, "application/json");
                }
                else
                {
                    Tunnel.SendResponse(requestId, 404, $"{{\"error\":\"Unknown input: {subPath}\"}}", "application/json");
                }
                yield break;
            }

            if (response == null)
            {
                Tunnel.SendResponse(requestId, 404, "{\"error\":\"Not found\"}", "application/json");
                yield break;
            }

            var r = response.Value;
            if (r.isBinary)
                Tunnel.SendBinaryResponse(requestId, r.binaryBody, r.contentType);
            else
                Tunnel.SendResponse(requestId, r.status, r.body, r.contentType);
        }

        void HandleUpgradeToWebSocket()
        {
            Debug.Log("[Bugpunch] Upgrading from poll to WebSocket debug mode");
            Registration?.Stop();
            Registration = null;

            Tunnel = new TunnelClient(Config);
            Tunnel.OnConnected += () => { Debug.Log("[Bugpunch] Connected"); OnConnected?.Invoke(); };
            Tunnel.OnDisconnected += () => { Debug.Log("[Bugpunch] Disconnected"); OnDisconnected?.Invoke(); };
            Tunnel.OnError += e => { Debug.LogError($"[Bugpunch] {e}"); OnError?.Invoke(e); };
            Tunnel.OnRequest += HandleRequest;
            StartCoroutine(ConnectLoop());
        }

        void HandlePollScripts(DeviceRegistration.PendingScript[] scripts)
        {
            foreach (var script in scripts)
            {
                Debug.Log($"[Bugpunch] Received script via poll: {script.Name}");
                // TODO: permission check via NativeDialogFactory, then execute via ScriptRunner
            }
        }

        /// <summary>
        /// Fetch ICE server config from the server and pass to WebRTC streamer.
        /// Called once after tunnel connects.
        /// </summary>
        IEnumerator FetchIceServers()
        {
#if BUGPUNCH_WEBRTC
            var url = Config.HttpBaseUrl + "/api/devices/ice-servers";
            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("X-API-Key", Config.apiKey);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Bugpunch] Failed to fetch ICE servers: {req.error} — using default STUN");
                yield break;
            }

            try
            {
                var json = req.downloadHandler.text;
                var response = JsonUtility.FromJson<IceServersResponse>(json);
                if (response.iceServers == null || response.iceServers.Length == 0)
                {
                    Debug.Log("[Bugpunch] No ICE servers returned, using default STUN");
                    yield break;
                }

                var servers = new Unity.WebRTC.RTCIceServer[response.iceServers.Length];
                for (int i = 0; i < response.iceServers.Length; i++)
                {
                    var s = response.iceServers[i];
                    servers[i] = new Unity.WebRTC.RTCIceServer
                    {
                        urls = new[] { s.urls },
                        username = s.username ?? "",
                        credential = s.credential ?? ""
                    };
                }

                if (Streamer != null)
                    Streamer.SetIceServers(servers);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch] Failed to parse ICE servers: {ex.Message}");
            }
#else
            yield break;
#endif
        }

        [Serializable]
        struct IceServersResponse
        {
            public IceServerEntry[] iceServers;
        }

        [Serializable]
        struct IceServerEntry
        {
            public string urls;
            public string username;
            public string credential;
        }

        void OnDestroy()
        {
            Tunnel?.Disconnect();
            Registration?.Stop();
            Instance = null;
        }
    }
}
