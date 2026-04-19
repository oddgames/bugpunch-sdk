using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
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

        /// <summary>
        /// Resolved game config variables. Global variables merged with
        /// device-matched overrides. Populated by <see cref="FetchGameConfig"/>.
        /// </summary>
        static readonly Dictionary<string, string> s_variables = new();
        static bool s_configFetched;

        /// <summary>
        /// Read a game config variable set on the Bugpunch dashboard.
        /// Returns <paramref name="defaultValue"/> if the variable is not set
        /// or config hasn't been fetched yet.
        /// </summary>
        public static string GetVariable(string key, string defaultValue = null)
        {
            if (key == null) return defaultValue;
            lock (s_variables) { return s_variables.TryGetValue(key, out var v) ? v : defaultValue; }
        }

        /// <summary>
        /// Returns all resolved game config variables as a JSON object string.
        /// Used by the Remote IDE to display current config on the device.
        /// </summary>
        public static string GetGameConfigJson()
        {
            lock (s_variables)
            {
                if (s_variables.Count == 0) return "{}";
                var sb = new StringBuilder(256);
                sb.Append('{');
                bool first = true;
                foreach (var kv in s_variables)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"');
                    sb.Append(RequestRouter.EscapeJson(kv.Key));
                    sb.Append("\":\"");
                    sb.Append(RequestRouter.EscapeJson(kv.Value));
                    sb.Append('"');
                }
                sb.Append('}');
                return sb.ToString();
            }
        }

        /// <summary>
        /// Attachment rules added at runtime via <see cref="Bugpunch.AddAttachmentRule"/>.
        /// Merged with the ScriptableObject rules when building the native
        /// startup config and when resolving server directives.
        /// </summary>
        static readonly List<BugpunchConfig.AttachmentRule> s_runtimeAttachmentRules = new();

        internal static void AddRuntimeAttachmentRule(BugpunchConfig.AttachmentRule rule)
        {
            if (rule == null) return;
            lock (s_runtimeAttachmentRules)
            {
                s_runtimeAttachmentRules.Add(rule);
            }
        }

        public static IReadOnlyList<BugpunchConfig.AttachmentRule> GetEffectiveAttachmentRules(BugpunchConfig config)
        {
            var list = new List<BugpunchConfig.AttachmentRule>();
            if (config != null && config.attachmentRules != null)
                list.AddRange(config.attachmentRules);
            lock (s_runtimeAttachmentRules)
            {
                list.AddRange(s_runtimeAttachmentRules);
            }
            return list;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInitialize()
        {
            // Release builds are opt-in: the game must explicitly call
            // BugpunchClient.StartConnection() to enable remote debugging.
            // This prevents shipping a live debug channel to end users.
            if (!Debug.isDebugBuild && !Application.isEditor) return;

            var config = BugpunchConfig.Load();
            if (config == null) return;
            if (!config.autoStart)
            {
                Debug.Log("[Bugpunch] Auto-start disabled in config; call BugpunchClient.StartConnection() manually.");
                return;
            }
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
            var textures = new TextureService();
            var materials = gameObject.AddComponent<MaterialService>();
            var memorySnapshots = new MemorySnapshotService();
            var playerPrefs = new PlayerPrefsService();

            // Create scene camera service
            SceneCamera = gameObject.AddComponent<SceneCameraService>();

            // Create watch service (MonoBehaviour — samples in FixedUpdate)
            var watch = gameObject.AddComponent<WatchService>();

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
                Textures = textures,
                Materials = materials,
                Watch = watch,
                MemorySnapshots = memorySnapshots,
                PlayerPrefs = playerPrefs,
                Streamer = null
            };

            // Start native debug mode — owns crash handlers, log capture,
            // shake detection, screenshot, video ring buffer, upload queue.
            // C# just pushes scene/fps and forwards managed exceptions.
            BugpunchNative.Start(Config);
            gameObject.AddComponent<BugpunchSceneTick>();
            // Managed-side paxscript bridge for server "Request More Info"
            // directives. Everything else (directive fetching, caching,
            // queue matching, file globs, dialogs, denial prefs) lives
            // natively — this component just exists so native has a
            // UnitySendMessage target named "BugpunchClient".
            gameObject.AddComponent<CrashDirectiveHandler>().Init();

            // C# managed exception catcher — forwards to native ReportBug.
            if (Config.enableNativeCrashHandler)
            {
                // Ensure exception logs include stack traces. Default in
                // release builds can be None, which would leave us with just
                // the message and no frames at all. Don't downgrade if the
                // game has explicitly set Full.
                if (Application.GetStackTraceLogType(LogType.Exception) == StackTraceLogType.None)
                    Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.ScriptOnly);

                UnityExceptionForwarder.Install();
            }

            // Fetch unified game config from server (perf thresholds, variables,
            // overrides). Non-blocking — game runs normally if fetch fails.
            StartCoroutine(FetchGameConfig());

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

        /// <summary>
        /// Fetch unified game config from the server on startup. Populates
        /// variables (global + device-matched overrides) and starts the native
        /// performance monitor if enabled. Non-blocking — game runs normally
        /// if the fetch fails.
        /// </summary>
        IEnumerator FetchGameConfig()
        {
            var url = Config.HttpBaseUrl + "/api/v1/config";
            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("X-Api-Key", Config.apiKey);
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.Log($"[Bugpunch] Game config fetch failed ({req.error}) — using defaults");
                yield break;
            }

            var json = req.downloadHandler.text;
            if (string.IsNullOrEmpty(json))
            {
                Debug.Log("[Bugpunch] Game config empty — using defaults");
                yield break;
            }

            Debug.Log("[Bugpunch] Game config fetched");

            // Parse variables + overrides and resolve for this device
            ResolveVariables(json);

            // Push resolved game config variables to native custom data so they
            // are included in crash/bug reports automatically.
            PushGameConfigToNative();

            // Start native performance monitor if enabled in the ScriptableObject
            // config AND the server hasn't explicitly disabled it.
            if (Config != null && Config.performanceMonitoring)
            {
                var perfJson = ExtractJsonObject(json, "performance");
                // Default to enabled — only skip if the server explicitly sends enabled:false
                bool serverDisabled = perfJson != null && perfJson.Contains("\"enabled\":false");
                if (!serverDisabled)
                {
                    BugpunchNative.StartPerfMonitor(perfJson ?? "{\"enabled\":true}");
                }
            }

            s_configFetched = true;
        }

        /// <summary>
        /// Parse global variables and overrides from the config JSON, evaluate
        /// overrides against this device's properties, and merge into the
        /// resolved variables dictionary.
        /// </summary>
        void ResolveVariables(string configJson)
        {
            // Extract variables object
            var varsJson = ExtractJsonObject(configJson, "variables");
            var pairs = ExtractStringPairs(varsJson);

            // Extract overrides array and evaluate each against this device
            var overridesJson = ExtractJsonArray(configJson, "overrides");
            if (overridesJson != null)
            {
                // For each override, check if device matches, merge variables
                // Simple approach: extract each override's match and variables
                // Device info for matching
                var gpu = SystemInfo.graphicsDeviceName ?? "";
                var memMB = SystemInfo.systemMemorySize;
                var screenW = Screen.width;
                var screenH = Screen.height;
                var platform = Application.platform.ToString();

                foreach (var ovr in ExtractArrayElements(overridesJson))
                {
                    var matchJson = ExtractJsonObject(ovr, "match");
                    if (matchJson == null) continue;

                    if (!DeviceMatchesOverride(matchJson, gpu, memMB, screenW, screenH, platform))
                        continue;

                    // This override matches — merge its variables on top
                    var ovrVars = ExtractJsonObject(ovr, "variables");
                    if (ovrVars != null)
                    {
                        foreach (var kv in ExtractStringPairs(ovrVars))
                            pairs[kv.Key] = kv.Value;
                    }
                }
            }

            lock (s_variables)
            {
                s_variables.Clear();
                foreach (var kv in pairs)
                    s_variables[kv.Key] = kv.Value;
            }

            if (pairs.Count > 0)
                Debug.Log($"[Bugpunch] Resolved {pairs.Count} game config variable(s)");
        }

        /// <summary>
        /// Push all resolved game config variables to native custom data with a
        /// "gc." prefix so they are included in crash/bug reports and visible on
        /// the dashboard Game Config tab.
        /// </summary>
        void PushGameConfigToNative()
        {
            Dictionary<string, string> snapshot;
            lock (s_variables)
            {
                snapshot = new Dictionary<string, string>(s_variables);
            }
            foreach (var kv in snapshot)
            {
                BugpunchNative.SetCustomData($"gc.{kv.Key}", kv.Value);
            }
        }

        static bool DeviceMatchesOverride(string matchJson, string gpu, int memMB, int screenW, int screenH, string platform)
        {
            // Check gpu glob match
            var gpuPattern = ExtractJsonString(matchJson, "gpu");
            if (gpuPattern != null && !GlobMatch(gpu, gpuPattern)) return false;

            // Check platform
            var platPattern = ExtractJsonString(matchJson, "platform");
            if (platPattern != null && !GlobMatch(platform, platPattern)) return false;

            // Check systemMemoryMB range
            var memObj = ExtractJsonObject(matchJson, "systemMemoryMB");
            if (memObj != null)
            {
                var maxStr = ExtractJsonString(memObj, "max") ?? ExtractJsonNumber(memObj, "max");
                if (maxStr != null && int.TryParse(maxStr, out var max) && memMB > max) return false;
                var minStr = ExtractJsonString(memObj, "min") ?? ExtractJsonNumber(memObj, "min");
                if (minStr != null && int.TryParse(minStr, out var min) && memMB < min) return false;
            }

            // Check screenSize range
            var screenObj = ExtractJsonObject(matchJson, "screenSize");
            if (screenObj != null)
            {
                var maxWStr = ExtractJsonString(screenObj, "maxWidth") ?? ExtractJsonNumber(screenObj, "maxWidth");
                if (maxWStr != null && int.TryParse(maxWStr, out var maxW) && screenW > maxW) return false;
                var minWStr = ExtractJsonString(screenObj, "minWidth") ?? ExtractJsonNumber(screenObj, "minWidth");
                if (minWStr != null && int.TryParse(minWStr, out var minW) && screenW < minW) return false;
            }

            return true;
        }

        /// <summary>Simple glob matcher supporting * wildcards.</summary>
        static bool GlobMatch(string input, string pattern)
        {
            if (pattern == "*") return true;
            // Convert glob to simple check: "Mali-G*" matches "Mali-G76"
            if (pattern.EndsWith("*"))
                return input.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.OrdinalIgnoreCase);
            if (pattern.StartsWith("*"))
                return input.EndsWith(pattern.Substring(1), StringComparison.OrdinalIgnoreCase);
            return string.Equals(input, pattern, StringComparison.OrdinalIgnoreCase);
        }

        // ── Minimal JSON extraction helpers ──
        // Avoids pulling in a full JSON parser for startup config. The config
        // JSON has a known, simple structure emitted by the server.

        static string ExtractJsonObject(string json, string key)
        {
            if (json == null) return null;
            var needle = "\"" + key + "\":";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i += needle.Length;
            while (i < json.Length && json[i] == ' ') i++;
            if (i >= json.Length || json[i] != '{') return null;
            return ExtractBalanced(json, i, '{', '}');
        }

        static string ExtractJsonArray(string json, string key)
        {
            if (json == null) return null;
            var needle = "\"" + key + "\":";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i += needle.Length;
            while (i < json.Length && json[i] == ' ') i++;
            if (i >= json.Length || json[i] != '[') return null;
            return ExtractBalanced(json, i, '[', ']');
        }

        static string ExtractBalanced(string json, int start, char open, char close)
        {
            int depth = 0;
            bool inString = false;
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && inString) { i++; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;
                if (c == open) depth++;
                else if (c == close) { depth--; if (depth == 0) return json.Substring(start, i - start + 1); }
            }
            return null;
        }

        /// <summary>Extract top-level string key/value pairs from a JSON object.</summary>
        static Dictionary<string, string> ExtractStringPairs(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json)) return result;
            // Simple scanner: find "key":"value" pairs
            int i = 0;
            while (i < json.Length)
            {
                // Find next key
                int kStart = json.IndexOf('"', i);
                if (kStart < 0) break;
                int kEnd = json.IndexOf('"', kStart + 1);
                if (kEnd < 0) break;
                var key = json.Substring(kStart + 1, kEnd - kStart - 1);

                // Skip to colon
                int colon = json.IndexOf(':', kEnd + 1);
                if (colon < 0) break;
                int vi = colon + 1;
                while (vi < json.Length && json[vi] == ' ') vi++;

                if (vi < json.Length && json[vi] == '"')
                {
                    // String value
                    var sb = new StringBuilder();
                    vi++;
                    while (vi < json.Length)
                    {
                        char c = json[vi++];
                        if (c == '\\' && vi < json.Length) { sb.Append(json[vi++]); continue; }
                        if (c == '"') break;
                        sb.Append(c);
                    }
                    result[key] = sb.ToString();
                    i = vi;
                }
                else if (vi < json.Length)
                {
                    // Non-string value (bool, number) — read until , or }
                    int vEnd = vi;
                    while (vEnd < json.Length && json[vEnd] != ',' && json[vEnd] != '}') vEnd++;
                    result[key] = json.Substring(vi, vEnd - vi).Trim();
                    i = vEnd;
                }
                else break;
            }
            return result;
        }

        static string ExtractJsonString(string json, string key)
        {
            if (json == null) return null;
            var needle = "\"" + key + "\":\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i += needle.Length;
            var sb = new StringBuilder();
            while (i < json.Length)
            {
                char c = json[i++];
                if (c == '\\' && i < json.Length) { sb.Append(json[i++]); continue; }
                if (c == '"') return sb.ToString();
                sb.Append(c);
            }
            return null;
        }

        static string ExtractJsonNumber(string json, string key)
        {
            if (json == null) return null;
            var needle = "\"" + key + "\":";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i += needle.Length;
            while (i < json.Length && json[i] == ' ') i++;
            if (i >= json.Length || json[i] == '"' || json[i] == '{' || json[i] == '[') return null;
            int start = i;
            while (i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != ']') i++;
            return json.Substring(start, i - start).Trim();
        }

        /// <summary>Split a JSON array into its top-level element strings.</summary>
        static List<string> ExtractArrayElements(string arrayJson)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(arrayJson) || arrayJson[0] != '[') return result;
            int i = 1;
            while (i < arrayJson.Length)
            {
                while (i < arrayJson.Length && (arrayJson[i] == ' ' || arrayJson[i] == ',')) i++;
                if (i >= arrayJson.Length || arrayJson[i] == ']') break;
                if (arrayJson[i] == '{')
                {
                    var obj = ExtractBalanced(arrayJson, i, '{', '}');
                    if (obj != null) { result.Add(obj); i += obj.Length; }
                    else break;
                }
                else i++;
            }
            return result;
        }

        void Update() => Tunnel?.ProcessMessages();

        // Android keeps half-open TCP sockets for 1–2 min after WiFi↔mobile swaps,
        // so IsConnected stays true and ConnectLoop skips reconnect. Dropping the
        // socket on resume forces ConnectLoop to rebuild immediately.
        void OnApplicationPause(bool paused)
        {
            if (paused || Tunnel == null || !Tunnel.IsConnected) return;
            Tunnel.Disconnect();
        }

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

            if (response == null && path.StartsWith("/materials/thumbnail"))
            {
                // Material sphere render needs rendering context
                yield return new WaitForEndOfFrame();
                var matResponse = Router.HandleMaterialThumbnail(path);
                if (matResponse.isBinary)
                    Tunnel.SendBinaryResponse(requestId, matResponse.binaryBody, matResponse.contentType);
                else
                    Tunnel.SendResponse(requestId, matResponse.status, matResponse.body, matResponse.contentType);
                yield break;
            }

            if (response == null && path.StartsWith("/materials/texture"))
            {
                // Material texture extraction needs rendering context
                yield return new WaitForEndOfFrame();
                var matTexResponse = Router.HandleMaterialTexture(path);
                if (matTexResponse.isBinary)
                    Tunnel.SendBinaryResponse(requestId, matTexResponse.binaryBody, matTexResponse.contentType);
                else
                    Tunnel.SendResponse(requestId, matTexResponse.status, matTexResponse.body, matTexResponse.contentType);
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

            // Input injection — native OS touch on Android, InputInjector fallback on iOS/Editor
            if (response == null && path.StartsWith("/input/"))
            {
                var subPath = path.Split('?')[0];

                if (subPath == "/input/tap")
                {
                    var nx = Mathf.Clamp01(float.TryParse(RequestRouter.Q(path, "x") ?? RequestRouter.JsonVal(body, "x"), out var px) ? px : 0.5f);
                    var ny = Mathf.Clamp01(float.TryParse(RequestRouter.Q(path, "y") ?? RequestRouter.JsonVal(body, "y"), out var py) ? py : 0.5f);
#if !UNITY_EDITOR && UNITY_ANDROID
                    // Native OS injection — fires on background thread, captured by touch recorder
                    RequestRouter.NativeInjectTap(nx, ny);
                    var screenPos = new Vector2(nx * Screen.width, (1f - ny) * Screen.height);
                    Tunnel.SendResponse(requestId, 200, $"{{\"ok\":true,\"native\":true,\"screen\":[{screenPos.x:F0},{screenPos.y:F0}]}}", "application/json");
#else
                    // Fallback to Unity Input System injection
                    var screenPos2 = new Vector2(nx * Screen.width, (1f - ny) * Screen.height);
                    var tapTask = InputInjector.InjectPointerTap(screenPos2);
                    while (!tapTask.IsCompleted) yield return null;
                    if (tapTask.IsFaulted)
                        Tunnel.SendResponse(requestId, 500, $"{{\"ok\":false,\"error\":\"{RequestRouter.EscapeJson(tapTask.Exception?.InnerException?.Message ?? "Unknown")}\"}}", "application/json");
                    else
                        Tunnel.SendResponse(requestId, 200, $"{{\"ok\":true,\"screen\":[{screenPos2.x:F0},{screenPos2.y:F0}]}}", "application/json");
#endif
                    yield break;
                }

                if (subPath == "/input/swipe")
                {
                    var x1 = Mathf.Clamp01(float.TryParse(RequestRouter.JsonVal(body, "x1"), out var sx1) ? sx1 : 0.5f);
                    var y1 = Mathf.Clamp01(float.TryParse(RequestRouter.JsonVal(body, "y1"), out var sy1) ? sy1 : 0.5f);
                    var x2 = Mathf.Clamp01(float.TryParse(RequestRouter.JsonVal(body, "x2"), out var sx2) ? sx2 : 0.5f);
                    var y2 = Mathf.Clamp01(float.TryParse(RequestRouter.JsonVal(body, "y2"), out var sy2) ? sy2 : 0.5f);
                    var durationMs = int.TryParse(RequestRouter.Q(path, "duration") ?? RequestRouter.JsonVal(body, "duration"), out var d) ? d : 300;
#if !UNITY_EDITOR && UNITY_ANDROID
                    RequestRouter.NativeInjectSwipe(x1, y1, x2, y2, durationMs);
                    Tunnel.SendResponse(requestId, 200, "{\"ok\":true,\"native\":true}", "application/json");
#else
                    var from = new Vector2(x1 * Screen.width, (1f - y1) * Screen.height);
                    var to = new Vector2(x2 * Screen.width, (1f - y2) * Screen.height);
                    var swipeTask = InputInjector.InjectPointerDrag(from, to, durationMs / 1000f);
                    while (!swipeTask.IsCompleted) yield return null;
                    if (swipeTask.IsFaulted)
                        Tunnel.SendResponse(requestId, 500, $"{{\"ok\":false,\"error\":\"{RequestRouter.EscapeJson(swipeTask.Exception?.InnerException?.Message ?? "Unknown")}\"}}", "application/json");
                    else
                        Tunnel.SendResponse(requestId, 200, "{\"ok\":true}", "application/json");
#endif
                    yield break;
                }

                if (subPath == "/input/pointer")
                {
                    var action = RequestRouter.JsonVal(body, "action") ?? "up";
                    var nx = Mathf.Clamp01(float.TryParse(RequestRouter.JsonVal(body, "x"), out var pxp) ? pxp : 0.5f);
                    var ny = Mathf.Clamp01(float.TryParse(RequestRouter.JsonVal(body, "y"), out var pyp) ? pyp : 0.5f);
#if !UNITY_EDITOR && UNITY_ANDROID
                    RequestRouter.NativeInjectPointer(action, nx, ny);
                    Tunnel.SendResponse(requestId, 200, $"{{\"ok\":true,\"action\":\"{action}\"}}", "application/json");
#else
                    // iOS / Editor: stateful Unity Input System lifecycle.
                    var screenP = new Vector2(nx * Screen.width, (1f - ny) * Screen.height);
                    Task t = action switch
                    {
                        "down"   => InputInjector.InjectPointerDown(screenP),
                        "move"   => InputInjector.InjectPointerMove(screenP),
                        "up"     => InputInjector.InjectPointerUp(screenP),
                        "cancel" => InputInjector.InjectPointerCancel(),
                        _        => Task.CompletedTask,
                    };
                    while (!t.IsCompleted) yield return null;
                    Tunnel.SendResponse(requestId, 200, $"{{\"ok\":true,\"action\":\"{action}\"}}", "application/json");
#endif
                    yield break;
                }

                Tunnel.SendResponse(requestId, 404, $"{{\"error\":\"Unknown input: {subPath}\"}}", "application/json");
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
