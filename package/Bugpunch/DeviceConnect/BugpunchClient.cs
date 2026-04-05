using System;
using System.Collections;
using UnityEngine;

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
            IScriptRunner scriptRunner = null;
#if BUGPUNCH_HAS_PAXSCRIPT
            if (Config.enableScriptRunner)
                scriptRunner = new PaxScriptRunner();
#endif

            // Create router
            Router = new RequestRouter
            {
                Hierarchy = hierarchy,
                Console = console,
                ScreenCapture = screenCapture,
                Inspector = inspector,
                ScriptRunner = scriptRunner,
                Streamer = null // set after streamer created
            };

            // Create bug reporter
            Reporter = gameObject.AddComponent<BugReporter>();
            Reporter.shakeToReport = Config.enableShakeToReport;
            Reporter.videoBufferSeconds = Config.videoBufferSeconds;
            Reporter.videoFps = Config.bugReportVideoFps;

            // Create WebRTC streamer
            Streamer = gameObject.AddComponent<WebRTCStreamer>();

            if (Config.ShouldUseWebSocket)
            {
                // WebSocket debug mode — full tunnel for live features
                Tunnel = new TunnelClient(Config);
                Tunnel.OnConnected += () => { Debug.Log("[Bugpunch] Connected"); OnConnected?.Invoke(); };
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

        void OnDestroy()
        {
            Tunnel?.Disconnect();
            Registration?.Stop();
            Instance = null;
        }
    }
}
