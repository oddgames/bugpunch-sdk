using System;
using System.Collections;
using UnityEngine;

namespace ODDGames.UIAutomation.DeviceConnect
{
    /// <summary>
    /// Main OddDev client. Auto-connects to the server, routes requests to services.
    /// </summary>
    public class OddDevClient : MonoBehaviour
    {
        public static OddDevClient Instance { get; private set; }

        public OddDevConfig Config { get; private set; }
        public TunnelClient Tunnel { get; private set; }
        public RequestRouter Router { get; private set; }
        public bool IsConnected => Tunnel != null && Tunnel.IsConnected;

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

            // Create services
            var hierarchy = Config.enableHierarchy ? new HierarchyService() : null;
            var console = Config.enableConsole ? new ConsoleService() : null;
            var screenCapture = Config.enableScreenCapture ? gameObject.AddComponent<ScreenCaptureService>() : null;
            var inspector = Config.enableInspector ? new InspectorService() : null;
            var scriptRunner = Config.enableScriptRunner ? new PaxScriptRunner() as IScriptRunner : null;

            // Create router
            Router = new RequestRouter
            {
                Hierarchy = hierarchy,
                Console = console,
                ScreenCapture = screenCapture,
                Inspector = inspector,
                ScriptRunner = scriptRunner
            };

            // Create tunnel
            Tunnel = new TunnelClient(Config);
            Tunnel.OnConnected += () => { Debug.Log("[OddDev] Connected"); OnConnected?.Invoke(); };
            Tunnel.OnDisconnected += () => { Debug.Log("[OddDev] Disconnected"); OnDisconnected?.Invoke(); };
            Tunnel.OnError += e => { Debug.LogError($"[OddDev] {e}"); OnError?.Invoke(e); };
            Tunnel.OnRequest += HandleRequest;

            StartCoroutine(ConnectLoop());
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

        void OnDestroy()
        {
            Tunnel?.Disconnect();
            Instance = null;
        }
    }
}
