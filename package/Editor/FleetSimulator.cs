#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ODDGames.Bugpunch.DeviceConnect;
using UnityEditor;
using UnityEngine;

namespace ODDGames.Bugpunch.Editor
{
    /// <summary>
    /// Spawns N synthetic SDK clients from inside the Editor and fires
    /// realistic-looking analytics, perf, and crash traffic at the server.
    /// Each "device" gets a unique stable_device_id, randomized model / OS /
    /// app version, and runs as an async loop with cancellation. Lets you
    /// fill the dashboard's Performance + Analytics + Issues pages with
    /// useful data without standing up real fleets.
    ///
    /// <para>HTTP-only — uses the same /api/v1/analytics/events,
    /// /api/v1/perf/events, /api/issues/ingest endpoints the production SDK
    /// uses. Does NOT open a WebSocket tunnel, so simulated devices don't
    /// appear "online" on the Devices page; they only show up in event-
    /// driven views (Analytics, Performance, Issues).</para>
    /// </summary>
    public class FleetSimulator : EditorWindow
    {
        // ── Tunable knobs (persisted via EditorPrefs so they survive reloads) ──
        int _deviceCount = 25;
        int _durationMinutes = 5;
        float _eventsPerDevicePerMinute = 20f;
        float _perfSamplesPerDevicePerMinute = 4f;
        float _crashChancePerDevicePerMinute = 0.05f;       // 5% / device / minute
        bool _includeIos = true;
        bool _includeAndroid = true;
        bool _includeWindows = false;
        string _appVersionsCsv = "1.0.0,1.0.1,1.1.0";

        // ── Runtime state ──
        CancellationTokenSource _cts;
        readonly object _statsLock = new();
        int _eventsSent;
        int _perfSent;
        int _crashesSent;
        int _errors;
        string _lastError = "";
        int _activeDevices;

        const string PREF_PREFIX = "Bugpunch.FleetSim.";

        [MenuItem("Bugpunch/Fleet Simulator")]
        public static void Open()
        {
            var win = GetWindow<FleetSimulator>("Bugpunch Fleet Simulator");
            win.minSize = new Vector2(440, 380);
        }

        void OnEnable()
        {
            _deviceCount = EditorPrefs.GetInt(PREF_PREFIX + "DeviceCount", 25);
            _durationMinutes = EditorPrefs.GetInt(PREF_PREFIX + "DurationMinutes", 5);
            _eventsPerDevicePerMinute = EditorPrefs.GetFloat(PREF_PREFIX + "EventsPerMin", 20f);
            _perfSamplesPerDevicePerMinute = EditorPrefs.GetFloat(PREF_PREFIX + "PerfPerMin", 4f);
            _crashChancePerDevicePerMinute = EditorPrefs.GetFloat(PREF_PREFIX + "CrashRate", 0.05f);
            _includeIos = EditorPrefs.GetBool(PREF_PREFIX + "Ios", true);
            _includeAndroid = EditorPrefs.GetBool(PREF_PREFIX + "Android", true);
            _includeWindows = EditorPrefs.GetBool(PREF_PREFIX + "Windows", false);
            _appVersionsCsv = EditorPrefs.GetString(PREF_PREFIX + "Versions", "1.0.0,1.0.1,1.1.0");
        }

        void OnDisable()
        {
            EditorPrefs.SetInt(PREF_PREFIX + "DeviceCount", _deviceCount);
            EditorPrefs.SetInt(PREF_PREFIX + "DurationMinutes", _durationMinutes);
            EditorPrefs.SetFloat(PREF_PREFIX + "EventsPerMin", _eventsPerDevicePerMinute);
            EditorPrefs.SetFloat(PREF_PREFIX + "PerfPerMin", _perfSamplesPerDevicePerMinute);
            EditorPrefs.SetFloat(PREF_PREFIX + "CrashRate", _crashChancePerDevicePerMinute);
            EditorPrefs.SetBool(PREF_PREFIX + "Ios", _includeIos);
            EditorPrefs.SetBool(PREF_PREFIX + "Android", _includeAndroid);
            EditorPrefs.SetBool(PREF_PREFIX + "Windows", _includeWindows);
            EditorPrefs.SetString(PREF_PREFIX + "Versions", _appVersionsCsv);

            // Don't leak running tasks if the window is closed.
            try { _cts?.Cancel(); } catch { }
        }

        void OnInspectorUpdate()
        {
            // Keep the live counters fresh while the simulator is running.
            if (_cts != null) Repaint();
        }

        void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bugpunch Fleet Simulator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Spawns synthetic devices that POST analytics / perf / crash events " +
                "to the configured server. Useful for populating the dashboard while " +
                "iterating on Performance + Analytics pages.",
                MessageType.None);

            var config = BugpunchConfig.Load();
            if (config == null || string.IsNullOrEmpty(config.serverUrl) || string.IsNullOrEmpty(config.apiKey))
            {
                EditorGUILayout.HelpBox(
                    "BugpunchConfig is missing or incomplete. Set serverUrl + apiKey on the " +
                    "config asset in Resources before running the simulator.",
                    MessageType.Warning);
                return;
            }

            EditorGUI.BeginDisabledGroup(_cts != null);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Server", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField("URL", config.serverUrl);
            EditorGUILayout.LabelField("Project", "(resolved from API key)");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Fleet", EditorStyles.miniBoldLabel);
            _deviceCount = EditorGUILayout.IntSlider("Devices", _deviceCount, 1, 200);
            _durationMinutes = EditorGUILayout.IntSlider("Duration (min)", _durationMinutes, 1, 60);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Event rates (per device, per minute)", EditorStyles.miniBoldLabel);
            _eventsPerDevicePerMinute   = EditorGUILayout.Slider("Analytics events", _eventsPerDevicePerMinute, 0f, 120f);
            _perfSamplesPerDevicePerMinute = EditorGUILayout.Slider("Perf samples", _perfSamplesPerDevicePerMinute, 0f, 60f);
            _crashChancePerDevicePerMinute = EditorGUILayout.Slider("Crash chance", _crashChancePerDevicePerMinute, 0f, 1f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Platform mix", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            _includeIos     = GUILayout.Toggle(_includeIos,     "iOS",     "Button");
            _includeAndroid = GUILayout.Toggle(_includeAndroid, "Android", "Button");
            _includeWindows = GUILayout.Toggle(_includeWindows, "Windows", "Button");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            _appVersionsCsv = EditorGUILayout.TextField("App versions (csv)", _appVersionsCsv);

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (_cts == null)
                {
                    if (GUILayout.Button("Start", GUILayout.Height(28)))
                    {
                        if (!_includeIos && !_includeAndroid && !_includeWindows)
                            _includeIos = true; // fail-safe
                        StartSimulation(config);
                    }
                }
                else
                {
                    if (GUILayout.Button("Stop", GUILayout.Height(28)))
                        Stop();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Live", EditorStyles.miniBoldLabel);
            int events, perf, crashes, errs, active;
            string lastErr;
            lock (_statsLock)
            {
                events = _eventsSent; perf = _perfSent; crashes = _crashesSent;
                errs = _errors; active = _activeDevices; lastErr = _lastError;
            }
            EditorGUILayout.LabelField("Active devices", active.ToString());
            EditorGUILayout.LabelField("Analytics events sent", events.ToString());
            EditorGUILayout.LabelField("Perf samples sent", perf.ToString());
            EditorGUILayout.LabelField("Crashes sent", crashes.ToString());
            EditorGUILayout.LabelField("Errors", errs.ToString());
            if (!string.IsNullOrEmpty(lastErr))
                EditorGUILayout.HelpBox(lastErr, MessageType.Error);
        }

        // ── Lifecycle ──

        void StartSimulation(BugpunchConfig config)
        {
            _cts = new CancellationTokenSource();
            lock (_statsLock)
            {
                _eventsSent = _perfSent = _crashesSent = _errors = 0;
                _activeDevices = 0;
                _lastError = "";
            }

            var platforms = new List<string>();
            if (_includeIos) platforms.Add("iOS");
            if (_includeAndroid) platforms.Add("Android");
            if (_includeWindows) platforms.Add("Windows");

            var versions = ParseCsv(_appVersionsCsv);
            if (versions.Count == 0) versions.Add("1.0.0");

            var rng = new System.Random();
            var devices = new List<SimDevice>();
            for (int i = 0; i < _deviceCount; i++)
            {
                devices.Add(SimDevice.Random(rng, platforms, versions));
            }

            var deadline = DateTime.UtcNow.AddMinutes(_durationMinutes);
            var token = _cts.Token;
            for (int i = 0; i < devices.Count; i++)
            {
                var dev = devices[i];
                _ = Task.Run(() => RunDevice(dev, config, deadline, token), token);
            }
            BugpunchLog.Info("FleetSim", $"Started {devices.Count} devices for {_durationMinutes}m");
        }

        void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            _cts = null;
            BugpunchLog.Info("FleetSim", "Stopped");
        }

        // ── One device's lifetime ──

        async Task RunDevice(SimDevice dev, BugpunchConfig config, DateTime deadline, CancellationToken token)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", config.apiKey);
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            lock (_statsLock) _activeDevices++;

            try
            {
                // Stagger start so events don't all land on the same second.
                await Task.Delay(TimeSpan.FromMilliseconds(dev.RandomStartDelayMs), token).ConfigureAwait(false);

                // Convert per-minute rates to inter-arrival delays in ms.
                int analyticsIntervalMs = _eventsPerDevicePerMinute > 0
                    ? Mathf.Max(50, (int)(60_000f / _eventsPerDevicePerMinute))
                    : int.MaxValue;
                int perfIntervalMs = _perfSamplesPerDevicePerMinute > 0
                    ? Mathf.Max(500, (int)(60_000f / _perfSamplesPerDevicePerMinute))
                    : int.MaxValue;

                var nextAnalytics = DateTime.UtcNow.AddMilliseconds(analyticsIntervalMs);
                var nextPerf = DateTime.UtcNow.AddMilliseconds(perfIntervalMs);
                var nextCrashRoll = DateTime.UtcNow.AddMinutes(1);

                while (!token.IsCancellationRequested && DateTime.UtcNow < deadline)
                {
                    var now = DateTime.UtcNow;

                    if (now >= nextAnalytics)
                    {
                        await SendAnalytics(http, config, dev, token).ConfigureAwait(false);
                        nextAnalytics = now.AddMilliseconds(analyticsIntervalMs);
                    }
                    if (now >= nextPerf)
                    {
                        await SendPerf(http, config, dev, token).ConfigureAwait(false);
                        nextPerf = now.AddMilliseconds(perfIntervalMs);
                    }
                    if (now >= nextCrashRoll)
                    {
                        // Roll once per minute against the configured chance.
                        if (dev.Rng.NextDouble() < _crashChancePerDevicePerMinute)
                            await SendCrash(http, config, dev, token).ConfigureAwait(false);
                        nextCrashRoll = now.AddMinutes(1);
                    }

                    await Task.Delay(150, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* expected on Stop */ }
            catch (Exception e)
            {
                RecordError(e.Message);
            }
            finally
            {
                lock (_statsLock) _activeDevices--;
            }
        }

        // ── Wire helpers ──

        async Task SendAnalytics(HttpClient http, BugpunchConfig config, SimDevice dev, CancellationToken token)
        {
            // Batch a small bunch — server takes up to 500 per request, but
            // matching the SDK's pacing keeps the flow looking real.
            int n = dev.Rng.Next(1, 4);
            var sb = new StringBuilder(256 * n);
            sb.Append("{\"events\":[");
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(',');
                AppendAnalyticsEvent(sb, dev);
            }
            sb.Append("]}");
            await PostJson(http, config.serverUrl + "/api/v1/analytics/events", sb.ToString(), token,
                onSuccess: bytes => { lock (_statsLock) _eventsSent += n; });
        }

        async Task SendPerf(HttpClient http, BugpunchConfig config, SimDevice dev, CancellationToken token)
        {
            var json = BuildPerfJson(dev);
            await PostJson(http, config.serverUrl + "/api/v1/perf/events", json, token,
                onSuccess: _ => { lock (_statsLock) _perfSent++; });
        }

        async Task SendCrash(HttpClient http, BugpunchConfig config, SimDevice dev, CancellationToken token)
        {
            var json = BuildCrashJson(dev);
            await PostJson(http, config.serverUrl + "/api/issues/ingest", json, token,
                onSuccess: _ => { lock (_statsLock) _crashesSent++; });
        }

        async Task PostJson(HttpClient http, string url, string json, CancellationToken token, Action<int> onSuccess)
        {
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await http.PostAsync(url, content, token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    onSuccess?.Invoke(0);
                }
                else
                {
                    var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    RecordError($"HTTP {(int)resp.StatusCode} {url.Substring(url.LastIndexOf('/') + 1)}: {Trim(body, 200)}");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { RecordError(e.Message); }
        }

        void RecordError(string msg)
        {
            lock (_statsLock)
            {
                _errors++;
                _lastError = msg;
            }
        }

        // ── Event payload builders ──

        static readonly string[] s_eventNames = {
            "level_started", "level_completed", "level_failed",
            "purchase", "ad_shown", "ad_clicked",
            "tutorial_step", "settings_opened",
            "session_start", "session_end",
            "button_click", "screen_view",
            "iap_initiated", "iap_completed",
        };

        static readonly string[] s_scenes = {
            "Main", "Lobby", "Battle", "Shop", "Settings", "Loading",
        };

        static void AppendAnalyticsEvent(StringBuilder sb, SimDevice dev)
        {
            var name = s_eventNames[dev.Rng.Next(s_eventNames.Length)];
            var scene = s_scenes[dev.Rng.Next(s_scenes.Length)];
            var ts = DateTime.UtcNow.ToString("o");
            sb.Append('{');
            sb.Append("\"name\":\"").Append(name).Append("\",");
            sb.Append("\"timestamp\":\"").Append(ts).Append("\",");
            sb.Append("\"deviceId\":\"").Append(dev.DeviceId).Append("\",");
            sb.Append("\"userId\":\"").Append(dev.UserId).Append("\",");
            sb.Append("\"sessionId\":\"").Append(dev.SessionId).Append("\",");
            sb.Append("\"platform\":\"").Append(dev.Platform.ToLowerInvariant()).Append("\",");
            sb.Append("\"buildVersion\":\"").Append(dev.AppVersion).Append("\",");
            sb.Append("\"scene\":\"").Append(scene).Append("\",");
            // A couple of properties just so the dashboard's facet sidebar has something to chew on
            sb.Append("\"properties\":{\"level\":").Append(dev.Rng.Next(1, 50))
              .Append(",\"score\":").Append(dev.Rng.Next(0, 10000)).Append('}');
            sb.Append('}');
        }

        static string BuildPerfJson(SimDevice dev)
        {
            // Plausible distributions: low-tier devices skew lower FPS + memory.
            float fpsAvg = dev.Tier switch {
                "low"  => 30 + (float)dev.Rng.NextDouble() * 15,
                "mid"  => 45 + (float)dev.Rng.NextDouble() * 20,
                _      => 60 + (float)dev.Rng.NextDouble() * 30,   // high
            };
            float fpsMin = Mathf.Max(5, fpsAvg - 10 - (float)dev.Rng.NextDouble() * 15);
            float fpsMax = fpsAvg + (float)dev.Rng.NextDouble() * 10;
            float fpsP5 = Mathf.Max(fpsMin, fpsAvg - 5 - (float)dev.Rng.NextDouble() * 8);
            int memTotal = dev.Tier switch { "low" => 2048, "mid" => 4096, _ => 8192 };
            int memPeak = (int)(memTotal * (0.4f + (float)dev.Rng.NextDouble() * 0.4f));
            int memAvail = memTotal - memPeak;

            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"trigger\":\"sample\",");
            sb.Append("\"scene\":\"").Append(s_scenes[dev.Rng.Next(s_scenes.Length)]).Append("\",");
            sb.Append("\"buildVersion\":\"").Append(dev.AppVersion).Append("\",");
            sb.Append("\"platform\":\"").Append(dev.Platform.ToLowerInvariant()).Append("\",");
            sb.Append("\"deviceId\":\"").Append(dev.DeviceId).Append("\",");
            sb.Append("\"deviceName\":\"").Append(dev.Model).Append("\",");
            sb.Append("\"deviceModel\":\"").Append(dev.Model).Append("\",");
            sb.Append("\"deviceTier\":\"").Append(dev.Tier).Append("\",");
            sb.Append("\"gpu\":\"").Append(dev.Gpu).Append("\",");
            sb.Append("\"screenSize\":\"").Append(dev.ScreenSize).Append("\",");
            sb.Append("\"systemMemoryMB\":").Append(memTotal).Append(',');
            sb.Append("\"fpsMin\":").Append(fpsMin.ToString("F2")).Append(',');
            sb.Append("\"fpsAvg\":").Append(fpsAvg.ToString("F2")).Append(',');
            sb.Append("\"fpsMax\":").Append(fpsMax.ToString("F2")).Append(',');
            sb.Append("\"fpsP5\":").Append(fpsP5.ToString("F2")).Append(',');
            sb.Append("\"memoryTotalMB\":").Append(memTotal).Append(',');
            sb.Append("\"memoryPeakMB\":").Append(memPeak).Append(',');
            sb.Append("\"memoryAvailableMB\":").Append(memAvail);
            sb.Append('}');
            return sb.ToString();
        }

        static readonly (string err, string stack)[] s_crashFingerprints = {
            ("NullReferenceException: Object reference not set",
             "at GameLogic.Update () [0x00012] in GameLogic.cs:42\n  at UnityEngine.MonoBehaviour:UpdateInternal ()"),
            ("IndexOutOfRangeException: Index was outside the bounds of the array",
             "at Inventory.GetItem (Int32 i) [0x00007] in Inventory.cs:128\n  at Player.UseItem () in Player.cs:96"),
            ("UnityException: Texture format not supported",
             "at TextureLoader.Load (System.String path) in TextureLoader.cs:33"),
            ("InvalidOperationException: Collection was modified",
             "at System.Collections.Generic.List`1+Enumerator[T].MoveNextRare () [0x00007]\n  at EnemySpawner.Tick () in EnemySpawner.cs:51"),
            ("OutOfMemoryException",
             "at UnityEngine.Texture2D..ctor (Int32 w, Int32 h) [0x00000]\n  at LevelLoader.Load () in LevelLoader.cs:24"),
        };

        static string BuildCrashJson(SimDevice dev)
        {
            var pick = s_crashFingerprints[dev.Rng.Next(s_crashFingerprints.Length)];
            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"errorMessage\":\"").Append(BugpunchJson.Esc(pick.err)).Append("\",");
            sb.Append("\"stackTrace\":\"").Append(BugpunchJson.Esc(pick.stack)).Append("\",");
            sb.Append("\"type\":\"exception\",");
            sb.Append("\"buildVersion\":\"").Append(dev.AppVersion).Append("\",");
            sb.Append("\"platform\":\"").Append(dev.Platform.ToLowerInvariant()).Append("\",");
            sb.Append("\"deviceId\":\"").Append(dev.DeviceId).Append("\",");
            sb.Append("\"deviceName\":\"").Append(dev.Model).Append("\",");
            sb.Append("\"userId\":\"").Append(dev.UserId).Append("\",");
            sb.Append("\"clientTimestampMs\":").Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).Append(',');
            sb.Append("\"customData\":{\"osVersion\":\"").Append(dev.OsVersion).Append("\",\"sim\":\"true\"}");
            sb.Append('}');
            return sb.ToString();
        }

        // ── Helpers ──

        static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        static List<string> ParseCsv(string s)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(s)) return list;
            foreach (var part in s.Split(','))
            {
                var t = part.Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }

        // ── Synthetic device profile ──

        sealed class SimDevice
        {
            public string DeviceId;
            public string UserId;
            public string SessionId;
            public string Platform;
            public string OsVersion;
            public string AppVersion;
            public string Model;
            public string Gpu;
            public string ScreenSize;
            public string Tier;
            public int RandomStartDelayMs;
            public System.Random Rng;

            static readonly string[] s_iosModels    = { "iPhone15,2", "iPhone14,5", "iPhone13,3", "iPhone12,1", "iPad13,1" };
            static readonly string[] s_iosOs        = { "17.4.1", "17.2", "16.6", "15.7" };
            static readonly string[] s_androidModels = { "Pixel 8 Pro", "Pixel 7", "SM-S908B", "SM-A536U", "Redmi Note 12", "OnePlus 11" };
            static readonly string[] s_androidOs    = { "14", "13", "12", "11" };
            static readonly string[] s_winModels    = { "DESKTOP-PC", "Steam Deck", "ROG Ally" };
            static readonly string[] s_winOs        = { "Windows 11", "Windows 10" };
            static readonly string[] s_screens      = { "1170x2532", "1290x2796", "1080x2400", "1440x3200", "1920x1080", "2560x1440" };
            static readonly string[] s_gpus         = { "Apple A16 GPU", "Apple A17 Pro GPU", "Adreno 740", "Adreno 730", "Mali-G715", "GeForce RTX 4070", "GeForce GTX 1660" };
            static readonly string[] s_tiers        = { "low", "mid", "high" };

            public static SimDevice Random(System.Random rng, List<string> platforms, List<string> versions)
            {
                var platform = platforms[rng.Next(platforms.Count)];
                string model, osVer;
                if (platform == "iOS")
                {
                    model = s_iosModels[rng.Next(s_iosModels.Length)];
                    osVer = s_iosOs[rng.Next(s_iosOs.Length)];
                }
                else if (platform == "Android")
                {
                    model = s_androidModels[rng.Next(s_androidModels.Length)];
                    osVer = s_androidOs[rng.Next(s_androidOs.Length)];
                }
                else
                {
                    model = s_winModels[rng.Next(s_winModels.Length)];
                    osVer = s_winOs[rng.Next(s_winOs.Length)];
                }
                return new SimDevice
                {
                    DeviceId    = Guid.NewGuid().ToString(),
                    UserId      = "u_" + rng.Next(1000, 9999),
                    SessionId   = Guid.NewGuid().ToString(),
                    Platform    = platform,
                    OsVersion   = osVer,
                    AppVersion  = versions[rng.Next(versions.Count)],
                    Model       = model,
                    Gpu         = s_gpus[rng.Next(s_gpus.Length)],
                    ScreenSize  = s_screens[rng.Next(s_screens.Length)],
                    Tier        = s_tiers[rng.Next(s_tiers.Length)],
                    RandomStartDelayMs = rng.Next(0, 5_000),
                    Rng         = new System.Random(rng.Next()),
                };
            }
        }
    }
}
#endif
