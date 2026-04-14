using System;
using System.Collections;
using ODDGames.Bugpunch.DeviceConnect.Database;
using UnityEngine;
using UnityEngine.Networking;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Main Bugpunch client. Auto-connects to the server, routes requests to services.
    /// Starts in lightweight poll mode; upgrades to WebSocket + WebRTC on demand.
    /// </summary>
    public class BugpunchClient : MonoBehaviour
    {
        public static BugpunchClient Instance { get; private set; }

        public BugpunchConfig Config { get; private set; }
        public TunnelClient Tunnel { get; private set; }
        public RequestRouter Router { get; private set; }

        /// <summary>
        /// WebRTC streamer — null until a debug session is started and WebRTC
        /// initializes successfully. The concrete type (WebRTCStreamer) lives in
        /// a separate assembly to avoid loading the native WebRTC lib at startup.
        /// </summary>
        public IStreamer Streamer { get; private set; }

        public SceneCameraService SceneCamera { get; private set; }
        public BugReporter Reporter { get; private set; }
        public NativeCrashHandler CrashHandler { get; private set; }
        public DeviceRegistration Registration { get; private set; }
        public bool IsConnected => (Tunnel != null && Tunnel.IsConnected) || (Registration != null && Registration.IsRegistered);

        /// <summary>
        /// When true, incoming debug session requests from the server are
        /// automatically declined with "busy". Games can set this during
        /// gameplay sequences that should not be interrupted.
        /// </summary>
        public bool SuppressDebugRequests { get; set; }

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnError;

        /// <summary>
        /// Fires whenever any BugpunchClient instance successfully connects.
        /// Used by editor hooks (e.g. TypeDB auto-upload) that need to react without
        /// holding a reference to the instance.
        /// </summary>
        public static event Action OnAnyConnected;

        bool _debugSessionActive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInitialize()
        {
            // Release builds are opt-in: the game must explicitly call
            // BugpunchClient.StartConnection() to enable remote debugging.
            // This prevents shipping a live debug channel to end users.
            if (!Debug.isDebugBuild && !Application.isEditor) return;

            var config = BugpunchConfig.Load();
            if (config == null) return;
            if (string.IsNullOrEmpty(config.serverUrl) || string.IsNullOrEmpty(config.apiKey)) return;

#if UNITY_EDITOR
            // In Editor: respect the "Enable Bugpunch" toggle (default OFF).
            // Prevents remote connections to developers while they're working.
            if (!UnityEditor.EditorPrefs.GetBool("Bugpunch_Enabled", false))
            {
                Debug.Log("[Bugpunch] Disabled in Editor (enable via Bugpunch > Enable Connection in toolbar)");
                return;
            }
#endif

            Initialize(config);
        }

        /// <summary>
        /// Manually start the Bugpunch connection. Use this on release builds
        /// where auto-connect is disabled. Loads config from Resources and
        /// connects to the configured server. Safe to call multiple times —
        /// will no-op if already initialized.
        /// </summary>
        /// <returns>The client instance, or null if config is missing/invalid.</returns>
        public static BugpunchClient StartConnection()
        {
            if (Instance != null) return Instance;

            var config = BugpunchConfig.Load();
            if (config == null)
            {
                Debug.LogWarning("[Bugpunch] StartConnection: no BugpunchConfig found in Resources");
                return null;
            }
            if (string.IsNullOrEmpty(config.serverUrl) || string.IsNullOrEmpty(config.apiKey))
            {
                Debug.LogWarning("[Bugpunch] StartConnection: config is missing serverUrl or apiKey");
                return null;
            }
            return Initialize(config);
        }

        /// <summary>
        /// Manually start the Bugpunch connection with a specific config.
        /// Use this to override the config in Resources at runtime.
        /// </summary>
        public static BugpunchClient StartConnection(BugpunchConfig config)
        {
            if (Instance != null) return Instance;
            if (config == null) { Debug.LogWarning("[Bugpunch] StartConnection: config is null"); return null; }
            return Initialize(config);
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
            var hierarchy = new HierarchyService();
            var console = new ConsoleService();
            var screenCapture = gameObject.AddComponent<ScreenCaptureService>();
            var inspector = new InspectorService();
            var perf = new PerformanceService();
            var files = new FileService();
            var deviceInfo = new DeviceInfoService();
            var dbPlugins = new DatabasePluginRegistry();
            dbPlugins.ScanIfNeeded();
            IScriptRunner scriptRunner = new PaxScriptRunner();

            // Create scene camera service
            SceneCamera = gameObject.AddComponent<SceneCameraService>();

            // Create router — Streamer starts null, set when debug session starts
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
                DatabasePlugins = dbPlugins,
                Streamer = null
            };

            // Create bug reporter
            Reporter = gameObject.AddComponent<BugReporter>();
            Reporter.shakeToReport = true;
            Reporter.videoBufferSeconds = Config.videoBufferSeconds;
            Reporter.videoFps = Config.bugReportVideoFps;

            // Create native crash handler — catches SIGSEGV/SIGABRT/ANR/etc.
            // that Unity's logMessageReceived doesn't see
            if (Config.enableNativeCrashHandler)
            {
                CrashHandler = gameObject.AddComponent<NativeCrashHandler>();
                CrashHandler.anrTimeoutMs = Config.anrTimeoutMs;
                CrashHandler.Initialize(Config);
            }

            // NOTE: WebRTCStreamer is NOT created here. It is initialized lazily
            // when a debug session starts (or WebSocket connects). This avoids
            // loading the Unity.WebRTC assembly (and native libwebrtc.so) at startup.

            // Debug builds (editor + development builds): connect via WebSocket directly
            // for immediate Remote IDE access. WebRTC still lazy-loaded.
            // Release builds: lightweight HTTP poll, upgrade to WebSocket on demand.
            if (Debug.isDebugBuild || Application.isEditor)
            {
                Debug.Log("[Bugpunch] Debug build — connecting via WebSocket");
                Tunnel = new TunnelClient(Config);
                Tunnel.OnConnected += () =>
                {
                    Debug.Log("[Bugpunch] Connected");
                    _debugSessionActive = true;
                    // WebRTC is NOT initialized here — only when the dashboard
                    // requests streaming (first webrtc-offer triggers lazy init)
                    OnConnected?.Invoke();
                    OnAnyConnected?.Invoke();
                };
                Tunnel.OnDisconnected += () => { OnDisconnected?.Invoke(); };
                Tunnel.OnRequest += HandleRequest;
                StartCoroutine(ConnectLoop());
            }
            else
            {
                Debug.Log("[Bugpunch] Release build — starting in poll mode");
                Registration = new DeviceRegistration(Config);
                Registration.OnUpgradeRequested += HandleUpgradeToWebSocket;
                Registration.OnScriptsReceived += HandlePollScripts;
                StartCoroutine(Registration.RegisterAndPoll());
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

            if (response == null && path.StartsWith("/webrtc-"))
            {
                var type = path.Split('?')[0].TrimStart('/');
                // Lazy-init WebRTC on first offer from the dashboard
                if (type == "webrtc-offer" && Streamer == null)
                    InitializeStreamerLazy();
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
                    Tunnel.SendResponse(requestId, 501, "{\"error\":\"Streaming unavailable — WebRTC not initialized\"}", "application/json");
                }
                yield break;
            }

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
            if (SuppressDebugRequests)
            {
                Debug.Log("[Bugpunch] Debug session requested but SuppressDebugRequests is true — declining");
                // TODO: respond "busy" to server via poll endpoint
                return;
            }

            Debug.Log("[Bugpunch] Upgrading from poll to WebSocket debug mode");
            _debugSessionActive = true;
            Registration?.Stop();
            Registration = null;

            // Create WebSocket tunnel
            Tunnel = new TunnelClient(Config);
            Tunnel.OnConnected += () =>
            {
                Debug.Log("[Bugpunch] Connected (debug session)");

                // Lazily initialize WebRTC now that the tunnel is up.
                // This is the first time Unity.WebRTC types are touched —
                // the native lib loads here, not at app startup.
                InitializeStreamerLazy();

                OnConnected?.Invoke();
                OnAnyConnected?.Invoke();
            };
            Tunnel.OnDisconnected += () =>
            {
                Debug.Log("[Bugpunch] Disconnected");
                OnDisconnected?.Invoke();
            };
            Tunnel.OnError += e => { Debug.LogError($"[Bugpunch] {e}"); OnError?.Invoke(e); };
            Tunnel.OnRequest += HandleRequest;
            StartCoroutine(ConnectLoop());
        }

        /// <summary>
        /// Lazily create and initialize the WebRTC streamer. Called only when
        /// a debug session starts. Uses reflection to add the WebRTCStreamer
        /// component (which lives in ODDGames.Bugpunch.WebRTC assembly) so the
        /// main assembly never directly references Unity.WebRTC types.
        ///
        /// If WebRTC fails to load (e.g. native lib crash on certain Android
        /// devices), the tunnel still works — streaming is just unavailable.
        /// </summary>
        void InitializeStreamerLazy()
        {
            if (Streamer != null) return;

            try
            {
                var streamer = gameObject.AddComponent<WebRTCStreamer>();
                Streamer = streamer;

                SceneCamera.SetStreamer(Streamer);
                Streamer.SceneCameraRef = SceneCamera;

                Router.Streamer = Streamer;
                Streamer.Initialize(Tunnel,
                    Config.captureScale > 0 ? (int)(1920 * Config.captureScale) : 1280,
                    Config.captureScale > 0 ? (int)(1080 * Config.captureScale) : 720,
                    Config.streamFps);

                StartCoroutine(FetchIceServers());
                Debug.Log("[Bugpunch] WebRTC streamer initialized (lazy)");
            }
            catch (Exception ex)
            {
                // WebRTC native lib failed to load — streaming unavailable but tunnel works
                Debug.LogWarning($"[Bugpunch] WebRTC initialization failed — streaming unavailable: {ex.Message}");
                Streamer = null;
                Router.Streamer = null;
            }
        }

        /// <summary>
        /// End the debug session and return to poll mode.
        /// </summary>
        public void EndDebugSession()
        {
            if (!_debugSessionActive) return;
            _debugSessionActive = false;

            Debug.Log("[Bugpunch] Ending debug session, returning to poll mode");

            // Tear down WebRTC
            if (Streamer != null)
            {
                Streamer.StopStreaming();
                // Streamer is a MonoBehaviour added via reflection — destroy the component
                var streamerComponent = Streamer as Component;
                if (streamerComponent != null) Destroy(streamerComponent);
                Streamer = null;
                Router.Streamer = null;
                SceneCamera.SetStreamer(null);
            }

            // Tear down WebSocket
            Tunnel?.Disconnect();
            Tunnel = null;

            // Resume polling
            Registration = new DeviceRegistration(Config);
            Registration.OnUpgradeRequested += HandleUpgradeToWebSocket;
            Registration.OnScriptsReceived += HandlePollScripts;
            StartCoroutine(Registration.RegisterAndPoll());
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
        /// Called once after streamer is initialized. Does NOT reference Unity.WebRTC
        /// types — the streamer handles conversion internally.
        /// </summary>
        IEnumerator FetchIceServers()
        {
            if (Streamer == null) yield break;

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
                // Pass the raw JSON to the streamer — it handles parsing and
                // creating RTCIceServer instances so we don't reference Unity.WebRTC here.
                Streamer.SetIceServersFromJson(req.downloadHandler.text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch] Failed to parse ICE servers: {ex.Message}");
            }
        }

        /// <summary>Disconnect and destroy the Bugpunch client.</summary>
        public void Disconnect()
        {
            Debug.Log("[Bugpunch] Disconnecting...");
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            Streamer?.StopStreaming();
            Tunnel?.Disconnect();
            Registration?.Stop();
            Instance = null;
        }
    }
}
