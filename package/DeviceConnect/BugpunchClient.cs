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

        /// <summary>
        /// Managed WebSocket to the Remote IDE tunnel. Present on every
        /// platform — Editor, Standalone, Android, iOS. The native
        /// BugpunchTunnel carries bug reports / pins / logs on its own
        /// separate WebSocket; Remote IDE RPC is C# end-to-end.
        /// </summary>
        public IdeTunnel Tunnel { get; private set; }

        public RequestRouter Router { get; private set; }

        /// <summary>
        /// WebRTC streamer — null until a debug session is started and WebRTC
        /// initializes successfully. The concrete type (WebRTCStreamer) lives in
        /// a separate assembly to avoid loading the native WebRTC lib at startup.
        /// </summary>
        public IStreamer Streamer { get; private set; }

        public SceneCameraService SceneCamera { get; private set; }

        /// <summary>
        /// True once the native poll loop is running (or the WebSocket tunnel
        /// is live). Registration/polling happens entirely in native now —
        /// this flag flips when we've asked native to start.
        /// </summary>
        public bool PollActive { get; private set; }
        public bool IsConnected => (Tunnel?.IsConnected ?? false) || PollActive;

        /// <summary>
        /// Ref-counted suppression depth. When &gt; 0, Bugpunch defers anything
        /// that would interrupt the player — incoming debug session requests
        /// from the server are declined with "busy", and queued prompts
        /// (directive questions, QA reply popups, feedback responses) hold
        /// until the count drops back to zero. Increment via
        /// <see cref="PushSuppression"/> during cutscenes, tutorials, modal
        /// UIs, or any other gameplay sequence the user shouldn't be yanked
        /// out of — multiple systems can nest their suppression scopes safely.
        /// </summary>
        public int SuppressInteractionsCount { get; private set; }

        /// <summary>
        /// True whenever something on this client is currently holding a
        /// suppression scope. Getter-only — use <see cref="PushSuppression"/>
        /// to modify.
        /// </summary>
        public bool SuppressInteractions => SuppressInteractionsCount > 0;

        /// <summary>
        /// Push a new suppression scope. Each call increments
        /// <see cref="SuppressInteractionsCount"/>; disposing the returned
        /// token decrements it by exactly one (idempotent — disposing twice
        /// is a no-op). Safe to nest across systems (cutscene + tutorial +
        /// QA popup queue). Typical usage:
        /// <code>
        /// using (BugpunchClient.Instance.PushSuppression())
        /// {
        ///     // play cutscene
        /// }
        /// </code>
        /// </summary>
        public IDisposable PushSuppression() => new SuppressionHandle(this);

        sealed class SuppressionHandle : IDisposable
        {
            BugpunchClient _owner;
            public SuppressionHandle(BugpunchClient owner)
            {
                _owner = owner;
                if (_owner != null) _owner.SuppressInteractionsCount++;
            }
            public void Dispose()
            {
                if (_owner == null) return;
                _owner.SuppressInteractionsCount = Math.Max(0, _owner.SuppressInteractionsCount - 1);
                _owner = null;
            }
        }

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

        // Runtime overrides for BugpunchConfig.branch / .changeset. CI pipelines
        // call SetBranch / SetChangeset before SDK init so each build reports its
        // own git metadata without editing the ScriptableObject in source control.
        static string s_branchOverride;
        static string s_changesetOverride;

        internal static void SetBranchOverride(string value) => s_branchOverride = value;
        internal static void SetChangesetOverride(string value) => s_changesetOverride = value;

        /// <summary>Effective branch label — runtime override wins over config field.</summary>
        internal static string GetEffectiveBranch(BugpunchConfig config)
            => !string.IsNullOrEmpty(s_branchOverride) ? s_branchOverride : (config?.branch ?? "");

        /// <summary>Effective changeset id — runtime override wins over config field.</summary>
        internal static string GetEffectiveChangeset(BugpunchConfig config)
            => !string.IsNullOrEmpty(s_changesetOverride) ? s_changesetOverride : (config?.changeset ?? "");

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
                BugpunchLog.Info("BugpunchClient", "Auto-start disabled in config; call BugpunchClient.StartConnection() manually.");
                return;
            }
            if (string.IsNullOrEmpty(config.serverUrl) || string.IsNullOrEmpty(config.apiKey)) return;

#if UNITY_EDITOR
            // In Editor: respect the "Enable Bugpunch" toggle (default OFF).
            // Prevents remote connections to developers while they're working.
            if (!UnityEditor.EditorPrefs.GetBool("Bugpunch_Enabled", false))
            {
                BugpunchLog.Info("BugpunchClient", "Disabled in Editor (enable via Bugpunch > Enable Connection in toolbar)");
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
                BugpunchLog.Warn("BugpunchClient", "StartConnection: no BugpunchConfig found in Resources");
                return null;
            }
            if (string.IsNullOrEmpty(config.serverUrl) || string.IsNullOrEmpty(config.apiKey))
            {
                BugpunchLog.Warn("BugpunchClient", "StartConnection: config is missing serverUrl or apiKey");
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
            if (config == null) { BugpunchLog.Warn("BugpunchClient", "StartConnection: config is null"); return null; }
            return Initialize(config);
        }

        public static BugpunchClient Initialize(BugpunchConfig config)
        {
            if (Instance != null)
            {
                BugpunchLog.Warn("BugpunchClient", "Already initialized");
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

        // Setup runs in three phases — see CLAUDE.md "SDK activation contract":
        //   1. Always-on init  — must run at boot to be useful.
        //   2. Lazy services   — instantiated cheaply, no work until the
        //                        router routes a request to their endpoint.
        //   3. Connection mode — debug builds open IDE WebSocket; release
        //                        builds run native HTTP poll until upgraded.
        void Setup()
        {
            BugpunchLog.Info("BugpunchClient", $"Initializing — server: {Config.serverUrl}");

            InitAlwaysOn();
            BuildLazyServices();
            StartConnectionMode();

            // QA → player chat reply heartbeat. Cheap poll every 30s; if a
            // thread has unread QA messages AND we're not suppressed, surface
            // the chat board. Guarded so we don't re-pop every 30s if the
            // user dismisses without replying (guard resets server-side when
            // the unread count drops to 0).
            StartCoroutine(ChatReplyHeartbeat());
        }

        // ── Phase 1 — always-on init ───────────────────────────────────────
        //
        // Everything here MUST be eager. Either it hooks an event source the
        // SDK can't replay (logs, exceptions, scene changes) or it owns
        // process-wide state native depends on (config, custom data, FPS).
        // If you find yourself adding to this method, double-check the work
        // can't move into Phase 2's lazy services.
        void InitAlwaysOn()
        {
            // Native runtime — owns crash handlers, log capture, shake
            // detection, screenshot ring, video ring buffer, upload queue.
            // C# just pushes scene/fps and forwards managed exceptions.
            BugpunchNative.Start(Config);

            // Scene name push + scene_change analytics events. Native can
            // measure FPS itself but it can't see SceneManager.
            gameObject.AddComponent<BugpunchSceneTick>();

#if UNITY_ANDROID && !UNITY_EDITOR
            // Fallback video source for when MediaProjection consent is denied —
            // the native recorder switches to buffer mode and this component
            // feeds it NV12 frames from a mirror RenderTexture. Always mounted;
            // it polls native state and stays idle until buffer mode activates.
            gameObject.AddComponent<BugpunchSurfaceRecorder>();
#endif

            // UnitySendMessage receiver for native "Request More Info" directives.
            // The component itself is the target — no Init() needed; Awake binds
            // the singleton.
            gameObject.AddComponent<CrashDirectiveHandler>();

            // Managed exception forwarder — must hook AppDomain events at boot
            // to catch exceptions thrown before any IDE session opens.
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

            // Game config (perf thresholds, variables, overrides). Game code
            // can call Bugpunch.GetVariable at any time, so the fetch has to
            // start at boot rather than wait for an IDE session. Non-blocking
            // — game runs normally if the fetch fails.
            StartCoroutine(FetchGameConfig());
        }

        // ── Phase 2 — lazy services ────────────────────────────────────────
        //
        // Construction is cheap: no field beyond defaults, no scene scans, no
        // event subscriptions. The first request the router routes to a
        // service triggers any heavy init that service needs (see e.g.
        // DatabasePluginRegistry.ScanIfNeeded, WatchService._declaredDirty).
        // ConsoleService is the one exception — it eagerly hooks
        // logMessageReceivedThreaded in its ctor because logs emitted before
        // the IDE connects would otherwise be lost.
        void BuildLazyServices()
        {
            var hierarchy        = new HierarchyService();
            var console          = new ConsoleService();
            var inspector        = new InspectorService();
            var perf             = new PerformanceService();
            var files            = new FileService();
            var deviceInfo       = new DeviceInfoService();
            var dbPlugins        = new DatabasePluginRegistry();
            IScriptRunner runner = new ScriptRunner();
            var textures         = new TextureService();
            var memorySnapshots  = new MemorySnapshotService();
            var playerPrefs      = new PlayerPrefsService();
            var settings         = new SettingsService();

            var screenCapture    = gameObject.AddComponent<ScreenCaptureService>();
            var materials        = gameObject.AddComponent<MaterialService>();
            var shaderProfiler   = gameObject.AddComponent<ShaderProfilerService>();
            var watch            = gameObject.AddComponent<WatchService>();
            SceneCamera          = gameObject.AddComponent<SceneCameraService>();

            // Streamer is null until a debug session is requested — the
            // Unity.WebRTC assembly + libwebrtc.so are heavy to load and most
            // sessions never need them.
            Router = new RequestRouter
            {
                Hierarchy        = hierarchy,
                Console          = console,
                ScreenCapture    = screenCapture,
                Inspector        = inspector,
                Performance      = perf,
                ScriptRunner     = runner,
                SceneCamera      = SceneCamera,
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
        }

        // ── Phase 3 — connection mode ──────────────────────────────────────
        //
        // Debug + editor builds open the IDE WebSocket immediately so devs
        // get a Remote IDE session as soon as the dashboard connects. Release
        // builds run a cheap native HTTP poll instead and only spin up the
        // WebSocket when the server flips upgradeToWebSocket on a poll
        // response (see HandleUpgradeToWebSocket).
        void StartConnectionMode()
        {
            if (Debug.isDebugBuild || Application.isEditor)
            {
                BugpunchLog.Info("BugpunchClient", "Debug build — connecting Remote IDE via WebSocket");
                SpinUpIdeTunnel(eagerInitStreamer: false);
            }
            else
            {
                BugpunchLog.Info("BugpunchClient", "Release build — starting native poll mode");
                StartPollMode();
            }
        }

        // ── Connection helpers (used by Phase 3 + upgrade / teardown) ──────

        /// <summary>
        /// Stand up the IDE WebSocket, attach the console log sink, wire all
        /// callbacks, and kick the reconnect loop. Single source of truth so
        /// boot-path connect (release: never; debug: always) and
        /// poll-upgrade connect stay identical. <paramref name="eagerInitStreamer"/>
        /// initializes the WebRTC streamer as soon as the tunnel connects;
        /// the cold path leaves it null and waits for the first webrtc-offer.
        /// </summary>
        void SpinUpIdeTunnel(bool eagerInitStreamer)
        {
            Tunnel = new IdeTunnel(Config);
            Router?.Console?.AttachTunnel(Tunnel);
            Tunnel.OnConnected += () =>
            {
                BugpunchLog.Info("BugpunchClient", "IDE tunnel connected");
                _debugSessionActive = true;
                if (eagerInitStreamer) InitializeStreamerLazy();
                OnConnected?.Invoke();
                OnAnyConnected?.Invoke();
            };
            Tunnel.OnDisconnected += () =>
            {
                BugpunchLog.Info("BugpunchClient", "IDE tunnel disconnected");
                OnDisconnected?.Invoke();
            };
            Tunnel.OnError += e =>
            {
                BugpunchNative.ReportSdkError("BugpunchClient.IdeTunnel", e);
                OnError?.Invoke(e);
            };
            Tunnel.OnRequest += HandleRequest;
            StartCoroutine(ConnectLoop());
        }

        /// <summary>
        /// Hand the native poller the script-permission setting and poll
        /// interval, then mark the client as live. Used by both initial
        /// release-build setup and EndDebugSession's return-to-poll path.
        /// </summary>
        void StartPollMode()
        {
            BugpunchNative.StartPoll(
                Config.scriptPermission.ToString().ToLower(),
                Mathf.Max(5, (int)Config.pollInterval));
            PollActive = true;
        }

        // ─── Chat reply heartbeat ───────────────────────────────────────
        //
        // The chat board itself polls when it's open (5s). This heartbeat
        // runs whether the board is open or not, so QA replies land on the
        // player even if they haven't been thinking about chat. The native
        // tunnel is the long-term home for this push, but v2 just polls —
        // one request every 30s is trivial cost.

        bool _chatReplyPopupShown;

        IEnumerator ChatReplyHeartbeat()
        {
            // Small initial delay so we don't race the very first connect.
            yield return new WaitForSeconds(10f);
            while (true)
            {
                // Skip the check if the user's in a no-interrupt state; we'll
                // check again on the next tick. Don't even bother hitting the
                // server while suppressed — feels more polite and saves bytes.
                if (!SuppressInteractions)
                    yield return CheckChatUnreadCoroutine();
                yield return new WaitForSeconds(30f);
            }
        }

        IEnumerator CheckChatUnreadCoroutine()
        {
            if (Config == null || string.IsNullOrEmpty(Config.HttpBaseUrl) || string.IsNullOrEmpty(Config.apiKey))
                yield break;

            // Single-thread-per-device API: GET /chat/thread returns
            // { thread, messages } when a thread exists, 404 when none does.
            // We only need to know if ANY message has Sender=qa AND
            // ReadBySdkAt=null — if so, auto-open the board (subject to the
            // suppression gate) and stop rechecking until the user clears it.
            var url = Config.HttpBaseUrl + "/api/v1/chat/thread";
            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Authorization", "Bearer " + (Config.apiKey ?? ""));
            req.SetRequestHeader("X-Device-Id", DeviceIdentity.GetDeviceId() ?? "");
            req.SetRequestHeader("Accept", "application/json");
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success) yield break;
            // 404 is the "no thread yet" sentinel — treat as zero unread and
            // reset the popup guard so the next QA-originated message (if
            // any) re-arms the auto-open.
            if (req.responseCode == 404)
            {
                _chatReplyPopupShown = false;
                yield break;
            }
            if (req.responseCode < 200 || req.responseCode >= 300) yield break;

            bool unread = false;
            try
            {
                var body = req.downloadHandler != null ? req.downloadHandler.text : "";
                var obj = Newtonsoft.Json.Linq.JObject.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                var arr = obj["messages"] as Newtonsoft.Json.Linq.JArray;
                if (arr != null)
                {
                    foreach (var m in arr)
                    {
                        var sender = (string)m["Sender"] ?? (string)m["sender"];
                        if (!string.Equals(sender, "qa", StringComparison.Ordinal)) continue;
                        var readAt = (string)m["ReadBySdkAt"] ?? (string)m["readBySdkAt"];
                        if (string.IsNullOrEmpty(readAt)) { unread = true; break; }
                    }
                }
            }
            catch (Exception ex)
            {
                BugpunchNative.ReportSdkError("BugpunchClient.ChatReplyHeartbeat", ex);
                yield break;
            }

            if (!unread)
            {
                // Server-side guard reset — when the user catches up or QA
                // clears the queue, we re-arm the popup.
                _chatReplyPopupShown = false;
                yield break;
            }
            if (_chatReplyPopupShown) yield break;
            if (SuppressInteractions) yield break; // double-check — state may have flipped during request

            _chatReplyPopupShown = true;
            UI.BugpunchChatBoard.Show();
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
                BugpunchLog.Info("BugpunchClient", $"Game config fetch failed ({req.error}) — using defaults");
                yield break;
            }

            var json = req.downloadHandler.text;
            if (string.IsNullOrEmpty(json))
            {
                BugpunchLog.Info("BugpunchClient", "Game config empty — using defaults");
                yield break;
            }

            BugpunchLog.Info("BugpunchClient", "Game config fetched");

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
                BugpunchLog.Info("BugpunchClient", $"Resolved {pairs.Count} game config variable(s)");
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

        void Update()
        {
            Tunnel?.ProcessMessages();
            Router?.Console?.Tick();
        }

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
                    Tunnel?.SendBinaryResponse(requestId, captureResponse.binaryBody, captureResponse.contentType);
                else
                    Tunnel?.SendResponse(requestId, captureResponse.status, captureResponse.body, captureResponse.contentType);
                yield break;
            }

            if (response == null && path.StartsWith("/materials/thumbnail"))
            {
                // Material sphere render needs rendering context
                yield return new WaitForEndOfFrame();
                var matResponse = Router.HandleMaterialThumbnail(path);
                if (matResponse.isBinary)
                    Tunnel?.SendBinaryResponse(requestId, matResponse.binaryBody, matResponse.contentType);
                else
                    Tunnel?.SendResponse(requestId, matResponse.status, matResponse.body, matResponse.contentType);
                yield break;
            }

            if (response == null && path.StartsWith("/materials/texture"))
            {
                // Material texture extraction needs rendering context
                yield return new WaitForEndOfFrame();
                var matTexResponse = Router.HandleMaterialTexture(path);
                if (matTexResponse.isBinary)
                    Tunnel?.SendBinaryResponse(requestId, matTexResponse.binaryBody, matTexResponse.contentType);
                else
                    Tunnel?.SendResponse(requestId, matTexResponse.status, matTexResponse.body, matTexResponse.contentType);
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
                    Tunnel?.SendResponse(requestId, 500,
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
                    Tunnel?.SendResponse(requestId, result.Success ? 200 : 422,
                        $"{{\"ok\":{okStr}{errStr},\"elapsedMs\":{result.ElapsedMs:F0}}}",
                        "application/json");
                }
                yield break;
            }

            if (response == null && path.StartsWith("/webrtc-"))
            {
                var type = path.Split('?')[0].TrimStart('/');
                BugpunchLog.Info("BugpunchClient", $"webrtc request: {type} id={requestId} bodyLen={body?.Length ?? 0}");
                // Lazy-init WebRTC on first offer from the dashboard
                if (type == "webrtc-offer" && Streamer == null)
                {
                    BugpunchLog.Info("BugpunchClient", "webrtc-offer but Streamer null — initializing lazy");
                    InitializeStreamerLazy();
                }
                if (type == "webrtc-offer" && Streamer != null)
                {
                    BugpunchLog.Info("BugpunchClient", $"handling webrtc-offer, answer will be async id={requestId}");
                    // Handle offer — response sent asynchronously via tunnel when answer is ready
                    var rid = requestId;
                    Streamer.HandleOfferAsync(body,
                        answer => {
                            BugpunchLog.Info("BugpunchClient", $"webrtc answer ready id={rid} len={answer?.Length ?? 0}");
                            Tunnel?.SendResponse(rid, 200, answer, "application/json");
                        },
                        err => {
                            BugpunchNative.ReportSdkError("BugpunchClient.WebRTCOffer", $"webrtc offer failed id={rid}: {err}");
                            Tunnel?.SendResponse(rid, 500, $"{{\"error\":\"{err}\"}}", "application/json");
                        });
                }
                else if (Streamer != null)
                {
                    BugpunchLog.Info("BugpunchClient", $"webrtc signal: {type} id={requestId}");
                    Streamer.HandleSignalingMessage(type, requestId, body);
                    Tunnel?.SendResponse(requestId, 200, "{\"ok\":true}", "application/json");
                }
                else
                {
                    BugpunchLog.Warn("BugpunchClient", $"webrtc-{type} but Streamer unavailable — returning 501 id={requestId}");
                    Tunnel?.SendResponse(requestId, 501, "{\"error\":\"Streaming unavailable — WebRTC not initialized\"}", "application/json");
                }
                yield break;
            }

            // Input injection — native OS touch on Android, InputInjector fallback on iOS/Editor
            if (response == null && path.StartsWith("/input/"))
            {
                var subPath = path.Split('?')[0];

                // WebRTC captures only the Unity render texture — not the OS
                // screen — so injection stays inside the Unity Input System.
                // The native Android `Instrumentation.sendPointerSync` path was
                // removed; it would land on Android UI that the dashboard
                // never sees.
                if (subPath == "/input/tap")
                {
                    var nx = Mathf.Clamp01(float.TryParse(RequestRouter.Q(path, "x") ?? RequestRouter.JsonVal(body, "x"), out var px) ? px : 0.5f);
                    var ny = Mathf.Clamp01(float.TryParse(RequestRouter.Q(path, "y") ?? RequestRouter.JsonVal(body, "y"), out var py) ? py : 0.5f);
                    var screenPos = new Vector2(nx * Screen.width, (1f - ny) * Screen.height);
                    var tapTask = InputInjector.InjectPointerTap(screenPos);
                    while (!tapTask.IsCompleted) yield return null;
                    if (tapTask.IsFaulted)
                        Tunnel?.SendResponse(requestId, 500, $"{{\"ok\":false,\"error\":\"{RequestRouter.EscapeJson(tapTask.Exception?.InnerException?.Message ?? "Unknown")}\"}}", "application/json");
                    else
                        Tunnel?.SendResponse(requestId, 200, $"{{\"ok\":true,\"screen\":[{screenPos.x:F0},{screenPos.y:F0}]}}", "application/json");
                    yield break;
                }

                if (subPath == "/input/swipe")
                {
                    var x1 = Mathf.Clamp01(float.TryParse(RequestRouter.JsonVal(body, "x1"), out var sx1) ? sx1 : 0.5f);
                    var y1 = Mathf.Clamp01(float.TryParse(RequestRouter.JsonVal(body, "y1"), out var sy1) ? sy1 : 0.5f);
                    var x2 = Mathf.Clamp01(float.TryParse(RequestRouter.JsonVal(body, "x2"), out var sx2) ? sx2 : 0.5f);
                    var y2 = Mathf.Clamp01(float.TryParse(RequestRouter.JsonVal(body, "y2"), out var sy2) ? sy2 : 0.5f);
                    var durationMs = int.TryParse(RequestRouter.Q(path, "duration") ?? RequestRouter.JsonVal(body, "duration"), out var d) ? d : 300;
                    var from = new Vector2(x1 * Screen.width, (1f - y1) * Screen.height);
                    var to = new Vector2(x2 * Screen.width, (1f - y2) * Screen.height);
                    var swipeTask = InputInjector.InjectPointerDrag(from, to, durationMs / 1000f);
                    while (!swipeTask.IsCompleted) yield return null;
                    if (swipeTask.IsFaulted)
                        Tunnel?.SendResponse(requestId, 500, $"{{\"ok\":false,\"error\":\"{RequestRouter.EscapeJson(swipeTask.Exception?.InnerException?.Message ?? "Unknown")}\"}}", "application/json");
                    else
                        Tunnel?.SendResponse(requestId, 200, "{\"ok\":true}", "application/json");
                    yield break;
                }

                if (subPath == "/input/pointer")
                {
                    var action = RequestRouter.JsonVal(body, "action") ?? "up";
                    var nx = Mathf.Clamp01(float.TryParse(RequestRouter.JsonVal(body, "x"), out var pxp) ? pxp : 0.5f);
                    var ny = Mathf.Clamp01(float.TryParse(RequestRouter.JsonVal(body, "y"), out var pyp) ? pyp : 0.5f);
                    var screenP = new Vector2(nx * Screen.width, (1f - ny) * Screen.height);
                    BugpunchLog.Info("BugpunchClient", $"/input/pointer action={action} norm=({nx:F3},{ny:F3}) screen=({screenP.x:F0},{screenP.y:F0}) screenWH=({Screen.width}x{Screen.height}) reqId={requestId} bodyLen={body?.Length ?? 0}");
                    Task t = action switch
                    {
                        "down"   => InputInjector.InjectPointerDown(screenP),
                        "move"   => InputInjector.InjectPointerMove(screenP),
                        "up"     => InputInjector.InjectPointerUp(screenP),
                        "cancel" => InputInjector.InjectPointerCancel(),
                        _        => Task.CompletedTask,
                    };
                    while (!t.IsCompleted) yield return null;
                    Tunnel?.SendResponse(requestId, 200, $"{{\"ok\":true,\"action\":\"{action}\"}}", "application/json");
                    yield break;
                }

                Tunnel?.SendResponse(requestId, 404, $"{{\"error\":\"Unknown input: {subPath}\"}}", "application/json");
                yield break;
            }

            if (response == null)
            {
                Tunnel?.SendResponse(requestId, 404, "{\"error\":\"Not found\"}", "application/json");
                yield break;
            }

            var r = response.Value;
            if (r.isBinary)
                Tunnel?.SendBinaryResponse(requestId, r.binaryBody, r.contentType);
            else
                Tunnel?.SendResponse(requestId, r.status, r.body, r.contentType);
        }

        /// <summary>
        /// UnitySendMessage receiver. Fired by the native poller when the
        /// server flips the upgradeToWebSocket flag on a poll response.
        /// Parameter is unused (UnitySendMessage always delivers a string).
        /// </summary>
        public void OnPollUpgradeRequested(string _)
        {
            HandleUpgradeToWebSocket();
        }

        /// <summary>
        /// UnitySendMessage receiver. On Android the chat board is now a
        /// native Activity (BugpunchChatActivity) and never routes here.
        /// iOS still bounces through this until the iOS port lands; Editor
        /// / Standalone fallback also reaches the C# UI Toolkit chat board
        /// via this entry point.
        /// </summary>
        public void OnShowChatBoardRequested(string _)
        {
            UI.BugpunchChatBoard.Show();
        }

        /// <summary>
        /// UnitySendMessage receiver. Fired by the native recording bar's
        /// chat icon — routes into the chat board. Kept distinct from
        /// <see cref="OnShowChatBoardRequested"/> so we can log / instrument
        /// the two entry points separately later.
        /// </summary>
        public void OnRecordingBarChatTapped(string _)
        {
            UI.BugpunchChatBoard.Show();
        }

        /// <summary>
        /// UnitySendMessage receiver. Fired by the native recording bar's
        /// feedback icon — routes through the dialog factory so Android lands
        /// on the native <c>BugpunchFeedbackActivity</c> and iOS / Editor
        /// fall back to the C# UIToolkit feedback board.
        /// </summary>
        public void OnRecordingBarFeedbackTapped(string _)
        {
            var dialog = UI.NativeDialogFactory.Create();
            if (dialog != null && dialog.IsSupported) dialog.ShowFeedbackBoard();
            else UI.BugpunchFeedbackBoard.Show();
        }

        /// <summary>
        /// UnitySendMessage receiver. Fires when the poll response contains
        /// scheduled scripts to run against managed code. Payload is the raw
        /// JSON array: <c>[{"Id":"...","Name":"...","Code":"..."}]</c>. Runs
        /// each via <see cref="ScriptRunner"/> and POSTs the result back through
        /// <see cref="BugpunchNative.PostScriptResult"/>.
        /// </summary>
        public void OnPollScripts(string scriptsJson)
        {
            if (string.IsNullOrEmpty(scriptsJson)) return;
            try
            {
                // Lightweight parse using a tiny wrapper struct — scripts are
                // a flat shape so JsonUtility handles it.
                var wrapped = "{\"items\":" + scriptsJson + "}";
                var payload = JsonUtility.FromJson<PollScriptsPayload>(wrapped);
                if (payload?.items == null) return;
                foreach (var s in payload.items)
                {
                    if (string.IsNullOrEmpty(s.Id) || string.IsNullOrEmpty(s.Code)) continue;
                    RunScheduledScript(s);
                }
            }
            catch (Exception ex)
            {
                BugpunchNative.ReportSdkError("BugpunchClient.OnPollScripts", ex);
            }
        }

        [Serializable] struct PollScript { public string Id, Name, Code; }
        [Serializable] class PollScriptsPayload { public PollScript[] items; }

        void RunScheduledScript(PollScript script)
        {
            BugpunchLog.Info("BugpunchClient", $"Running scheduled script: {script.Name}");
            var started = DateTime.UtcNow;
            string envelope = "";
            string errorsOut = "";
            bool ok = false;
            try
            {
                // ScriptRunner returns a JSON envelope: {"ok":bool,"output":"...","errors":[...]}
                envelope = new ScriptRunner().Execute(script.Code) ?? "";
                ok = envelope.Contains("\"ok\":true");
            }
            catch (Exception ex)
            {
                errorsOut = ex.Message + "\n" + (ex.StackTrace ?? "");
            }
            int durationMs = (int)(DateTime.UtcNow - started).TotalMilliseconds;
            BugpunchNative.PostScriptResult(script.Id, envelope, errorsOut, ok, durationMs);
        }

        // ─── Approved chat scriptRequest / dataRequest receivers ─────────
        //
        // Native chat (Android BugpunchChatActivity / iOS
        // BugpunchChatViewController) handles the Approve / Decline tap
        // and posts the answer to /api/v1/chat/request/answer itself.
        // For an approved scriptRequest / dataRequest, native bounces the
        // body up here as "<messageId>|<base64-utf8(body)>" so this end
        // can run it through ODDGames.Scripting and POST the result as a
        // follow-up chat message. Pipe + base64 keeps newlines / quotes /
        // pipes inside the source from corrupting the UnitySendMessage
        // string round-trip.

        /// <summary>
        /// UnitySendMessage receiver. Fired by native chat when the user
        /// approves a scriptRequest bubble. Decodes the body, runs it via
        /// <see cref="ScriptRunner"/>, and POSTs the JSON envelope back as
        /// a chat reply linked to the source request.
        /// </summary>
        public void OnApprovedScriptRequest(string payload)
        {
            if (!TryParseApprovedPayload(payload, out var messageId, out var source)) return;
            _ = ApprovedScriptRequestAsync(messageId, source);
        }

        /// <summary>
        /// UnitySendMessage receiver. Fired by native chat when the user
        /// approves a dataRequest bubble. v1 just acknowledges so the
        /// approve flow completes end-to-end — the tunnel-based collector
        /// pipeline is the next slice (#31 follow-up).
        /// </summary>
        public void OnApprovedDataRequest(string payload)
        {
            if (!TryParseApprovedPayload(payload, out var messageId, out var source)) return;
            _ = ApprovedDataRequestAsync(messageId, source);
        }

        static bool TryParseApprovedPayload(string payload, out string messageId, out string body)
        {
            messageId = null; body = null;
            if (string.IsNullOrEmpty(payload)) return false;
            var pipe = payload.IndexOf('|');
            if (pipe <= 0) return false;
            messageId = payload.Substring(0, pipe);
            try
            {
                var b64 = payload.Substring(pipe + 1);
                body = string.IsNullOrEmpty(b64) ? "" : Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                return !string.IsNullOrEmpty(messageId);
            }
            catch (Exception ex)
            {
                BugpunchNative.ReportSdkError("BugpunchClient.TryParseApprovedPayload", ex);
                return false;
            }
        }

        async Task ApprovedScriptRequestAsync(string messageId, string source)
        {
            string envelope;
            try
            {
                envelope = new ScriptRunner().Execute(source ?? "") ?? "";
            }
            catch (Exception ex)
            {
                BugpunchNative.ReportSdkError("BugpunchClient.ApprovedScriptRequest", ex);
                envelope = "{\"ok\":false,\"errors\":[{\"message\":\"" + RequestRouter.EscapeJson(ex.Message) + "\"}]}";
            }
            await PostChatReplyAsync(messageId, envelope);
        }

        async Task ApprovedDataRequestAsync(string messageId, string source)
        {
            // v1 stub — tunnel-based data collectors are the next slice.
            // Keep the shape identical to scriptRequest so the dashboard
            // can render the reply uniformly.
            var preview = string.IsNullOrEmpty(source) ? "" : source.Trim();
            if (preview.Length > 200) preview = preview.Substring(0, 200) + "…";
            var body = string.IsNullOrEmpty(preview)
                ? "Got your data request — tunnel-based collectors are next."
                : "Got your data request: " + preview + "\n\nTunnel-based collectors are next.";
            await PostChatReplyAsync(messageId, body);
        }

        /// <summary>
        /// POST a chat message linked to the source request via
        /// <c>inReplyTo</c>. Mirrors the helper in
        /// <see cref="UI.BugpunchChatBoard"/> but lives here so the
        /// approved-request path doesn't depend on the chat board class
        /// being open.
        /// </summary>
        async Task PostChatReplyAsync(string inReplyToMessageId, string body)
        {
            if (Config == null || string.IsNullOrEmpty(Config.HttpBaseUrl) || string.IsNullOrEmpty(Config.apiKey))
                return;

            var payload = new Newtonsoft.Json.Linq.JObject
            {
                ["body"] = body ?? "",
                ["inReplyTo"] = inReplyToMessageId ?? "",
            };
            var json = payload.ToString(Newtonsoft.Json.Formatting.None);

            var url = Config.HttpBaseUrl + "/api/v1/chat/message";
            using var req = new UnityWebRequest(url, "POST");
            var bytes = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bytes) { contentType = "application/json" };
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + (Config.apiKey ?? ""));
            req.SetRequestHeader("X-Device-Id", DeviceIdentity.GetDeviceId() ?? "");
            req.SetRequestHeader("Accept", "application/json");
            req.timeout = 15;

            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success || req.responseCode < 200 || req.responseCode >= 300)
            {
                BugpunchLog.Warn("BugpunchClient", $"PostChatReply failed: HTTP {req.responseCode} {req.error}");
            }
        }

        void HandleUpgradeToWebSocket()
        {
            if (SuppressInteractions)
            {
                BugpunchLog.Info("BugpunchClient", "Debug session requested but SuppressInteractions is true — declining");
                // TODO: respond "busy" to server via poll endpoint
                return;
            }

            BugpunchLog.Info("BugpunchClient", "Upgrading from poll to WebSocket debug mode");
            _debugSessionActive = true;
            BugpunchNative.StopPoll();
            PollActive = false;

            // First-time upgrade — open the IDE tunnel, eager-init the
            // streamer once it connects so the dashboard's webrtc-offer
            // doesn't have to wait. If the tunnel is already alive (debug
            // build that's already connected), just promote the existing
            // session to streaming.
            if (Tunnel == null)
            {
                SpinUpIdeTunnel(eagerInitStreamer: true);
            }
            else
            {
                InitializeStreamerLazy();
                OnConnected?.Invoke();
                OnAnyConnected?.Invoke();
            }
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
            BugpunchLog.Info("BugpunchClient", "InitializeStreamerLazy: starting");
            try
            {
                BugpunchLog.Info("BugpunchClient", "InitializeStreamerLazy: adding WebRTCStreamer component");
                var streamer = gameObject.AddComponent<WebRTCStreamer>();
                Streamer = streamer;

                SceneCamera.SetStreamer(Streamer);
                Streamer.SceneCameraRef = SceneCamera;

                Router.Streamer = Streamer;
                var w = Config.captureScale > 0 ? (int)(1920 * Config.captureScale) : 1280;
                var h = Config.captureScale > 0 ? (int)(1080 * Config.captureScale) : 720;
                var fps = Config.streamFps;
                BugpunchLog.Info("BugpunchClient", $"InitializeStreamerLazy: calling Streamer.Initialize({w}x{h}@{fps}fps)");
                Streamer.Initialize(w, h, fps);

                StartCoroutine(FetchIceServers());
                BugpunchLog.Info("BugpunchClient", "InitializeStreamerLazy: WebRTCStreamer component added and initialized");
            }
            catch (Exception ex)
            {
                // WebRTC native lib failed to load — streaming unavailable but tunnel works
                BugpunchNative.ReportSdkError("BugpunchClient.InitializeStreamerLazy", ex);
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

            BugpunchLog.Info("BugpunchClient", "Ending debug session, returning to poll mode");

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

            // Tear down the IDE WebSocket. Native report tunnel stays up
            // across debug-session boundaries — it's a separate pipe.
            Tunnel?.Disconnect();
            Tunnel = null;

            StartPollMode();
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
            BugpunchLog.Info("BugpunchClient", $"FetchIceServers: fetching from {url}");
            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("X-API-Key", Config.apiKey);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                BugpunchLog.Warn("BugpunchClient", $"FetchIceServers: failed ({req.error}) — using default STUN");
                yield break;
            }

            var json = req.downloadHandler.text;
            BugpunchLog.Info("BugpunchClient", $"FetchIceServers: received {json}");
            try
            {
                // Pass the raw JSON to the streamer — it handles parsing and
                // creating RTCIceServer instances so we don't reference Unity.WebRTC here.
                Streamer.SetIceServersFromJson(json);
                BugpunchLog.Info("BugpunchClient", "FetchIceServers: passed JSON to streamer");
            }
            catch (Exception ex)
            {
                BugpunchNative.ReportSdkError("BugpunchClient.FetchIceServers", ex);
            }
        }

        /// <summary>Disconnect and destroy the Bugpunch client.</summary>
        public void Disconnect()
        {
            BugpunchLog.Info("BugpunchClient", "Disconnecting...");
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            Streamer?.StopStreaming();
            Tunnel?.Disconnect();
            BugpunchNative.StopPoll();
            PollActive = false;
            Instance = null;
        }
    }
}
