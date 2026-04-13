using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Native crash handler bridge. Initializes platform-specific signal handlers
    /// on startup, detects pending crash reports from previous sessions, and uploads
    /// them to the Bugpunch server.
    ///
    /// Catches crashes that Unity's Application.logMessageReceived misses:
    /// - Native signals: SIGABRT, SIGSEGV, SIGBUS, SIGFPE, SIGILL
    /// - Mach exceptions (iOS)
    /// - ANR (Android main thread hang)
    /// - C# AppDomain.UnhandledException and TaskScheduler.UnobservedTaskException
    ///
    /// Usage: Automatically initialized by BugpunchClient. Can also be used standalone:
    ///   NativeCrashHandler.Initialize(config);
    /// </summary>
    public class NativeCrashHandler : MonoBehaviour
    {
        // ── Configuration ──

        [Tooltip("ANR detection timeout in milliseconds. 0 = disabled. 5000ms is a good default.")]
        public int anrTimeoutMs = 5000;

        [Tooltip("Maximum number of pending crash reports to upload per session")]
        public int maxPendingUploads = 10;

        // ── State ──

        static NativeCrashHandler s_instance;
        BugpunchConfig _config;
        bool _initialized;
        string _crashDir;

        // C# exception handlers
        static volatile string _pendingCSharpCrash;

        // ── Platform bindings ──

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern bool Bugpunch_InstallCrashHandlers(string crashDir);
        [DllImport("__Internal")] static extern void Bugpunch_UninstallCrashHandlers();
        [DllImport("__Internal")] static extern void Bugpunch_SetCrashMetadata(
            string appVersion, string bundleId, string unityVersion,
            string deviceModel, string osVersion, string gpuName);
        [DllImport("__Internal")] static extern string Bugpunch_GetPendingCrashFiles();
        [DllImport("__Internal")] static extern string Bugpunch_ReadCrashFile(string path);
        [DllImport("__Internal")] static extern bool Bugpunch_DeleteCrashFile(string path);
#endif

        // ── Public API ──

        /// <summary>
        /// Initialize the native crash handler. Called automatically by BugpunchClient.
        /// Safe to call multiple times (no-ops after first init).
        /// </summary>
        public void Initialize(BugpunchConfig config)
        {
            if (_initialized) return;
            _config = config;
            s_instance = this;

            _crashDir = Path.Combine(Application.persistentDataPath, "bugpunch_crashes");
            if (!Directory.Exists(_crashDir))
                Directory.CreateDirectory(_crashDir);

            // Install C# exception handlers (all platforms)
            InstallCSharpHandlers();

            // Install native handlers (platform-specific)
            InstallNativeHandlers();

            // Set metadata for crash reports
            UpdateMetadata();

            _initialized = true;
            Debug.Log($"[Bugpunch] NativeCrashHandler initialized (crashDir={_crashDir})");

            // Check for pending crash reports from previous session
            StartCoroutine(UploadPendingCrashReports());
        }

        void OnDestroy()
        {
            UninstallCSharpHandlers();
            UninstallNativeHandlers();
            if (s_instance == this) s_instance = null;
        }

        void Update()
        {
            // Tick ANR watchdog on Android
#if UNITY_ANDROID && !UNITY_EDITOR
            TickAnrWatchdog();
#endif

            // Check for pending C# crash written by background thread
            var pending = _pendingCSharpCrash;
            if (pending != null)
            {
                _pendingCSharpCrash = null;
                WriteCSharpCrashFile(pending);
            }
        }

        // ── C# Exception Handlers (all platforms) ──

        void InstallCSharpHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        void UninstallCSharpHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        }

        static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var ex = e.ExceptionObject as Exception;
                var message = ex != null
                    ? $"{ex.GetType().FullName}: {ex.Message}"
                    : e.ExceptionObject?.ToString() ?? "Unknown exception";
                var stackTrace = ex?.StackTrace ?? "";

                var report = BuildCSharpCrashReport("UNHANDLED_EXCEPTION", message, stackTrace,
                    e.IsTerminating);

                // We might be on a non-main thread, so write directly to disk
                // rather than going through Unity APIs
                WriteCrashFileDirect(report);
            }
            catch
            {
                // Can't throw in an unhandled exception handler
            }
        }

        static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                var ex = e.Exception?.Flatten();
                var message = ex != null
                    ? $"{ex.GetType().FullName}: {ex.Message}"
                    : "Unknown task exception";
                var stackTrace = ex?.StackTrace ?? "";

                var report = BuildCSharpCrashReport("UNOBSERVED_TASK_EXCEPTION", message,
                    stackTrace, isTerminating: false);

                WriteCrashFileDirect(report);

                // Don't observe it — let it propagate normally
            }
            catch
            {
                // Can't throw here
            }
        }

        static string BuildCSharpCrashReport(string type, string message, string stackTrace,
            bool isTerminating)
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("BUGPUNCH_CRASH_V1");
            sb.Append("type:").AppendLine(type);
            sb.Append("timestamp:").AppendLine(DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            sb.Append("is_terminating:").AppendLine(isTerminating ? "true" : "false");
            sb.Append("platform:").AppendLine(Application.platform.ToString());
            sb.Append("thread:").AppendLine(Thread.CurrentThread.ManagedThreadId.ToString());

            // Metadata — safe to read static strings
            sb.Append("app_version:").AppendLine(Application.version);
            sb.Append("bundle_id:").AppendLine(Application.identifier);
            sb.Append("unity_version:").AppendLine(Application.unityVersion);
            sb.Append("device_model:").AppendLine(SystemInfo.deviceModel);
            sb.Append("os_version:").AppendLine(SystemInfo.operatingSystem);

            sb.AppendLine("---MESSAGE---");
            sb.AppendLine(message);
            sb.AppendLine("---END_MESSAGE---");

            sb.AppendLine("---STACK---");
            sb.AppendLine(stackTrace);

            // Also capture the current thread's managed stack
            sb.AppendLine("--- Managed call stack ---");
            sb.AppendLine(Environment.StackTrace);
            sb.AppendLine("---END---");

            return sb.ToString();
        }

        static void WriteCrashFileDirect(string report)
        {
            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "bugpunch_crashes");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var filename = $"csharp_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.crash";
                var path = Path.Combine(dir, filename);
                File.WriteAllText(path, report, Encoding.UTF8);
            }
            catch
            {
                // Best effort — if we can't write, we can't write
            }
        }

        void WriteCSharpCrashFile(string report)
        {
            try
            {
                var filename = $"csharp_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.crash";
                var path = Path.Combine(_crashDir, filename);
                File.WriteAllText(path, report, Encoding.UTF8);
                Debug.Log($"[Bugpunch] C# crash report written: {path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Bugpunch] Failed to write C# crash report: {e.Message}");
            }
        }

        // ── Native Handler Installation ──

        void InstallNativeHandlers()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            InstallAndroidHandlers();
#elif UNITY_IOS && !UNITY_EDITOR
            InstallIOSHandlers();
#else
            Debug.Log("[Bugpunch] NativeCrashHandler: native handlers not available on this platform (C# handlers active)");
#endif
        }

        void UninstallNativeHandlers()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            UninstallAndroidHandlers();
#elif UNITY_IOS && !UNITY_EDITOR
            Bugpunch_UninstallCrashHandlers();
#endif
        }

        // ── Android ──

#if UNITY_ANDROID && !UNITY_EDITOR
        const string JAVA_CRASH_HANDLER = "au.com.oddgames.bugpunch.BugpunchCrashHandler";

        void InstallAndroidHandlers()
        {
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var handler = new AndroidJavaClass(JAVA_CRASH_HANDLER);

                bool ok = handler.CallStatic<bool>("initialize", activity, anrTimeoutMs);
                if (!ok)
                    Debug.LogWarning("[Bugpunch] Android native crash handler initialization failed");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Bugpunch] Failed to install Android crash handlers: {e.Message}");
            }
        }

        void UninstallAndroidHandlers()
        {
            try
            {
                using var handler = new AndroidJavaClass(JAVA_CRASH_HANDLER);
                handler.CallStatic("shutdown");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Bugpunch] Failed to uninstall Android crash handlers: {e.Message}");
            }
        }

        void TickAnrWatchdog()
        {
            try
            {
                using var handler = new AndroidJavaClass(JAVA_CRASH_HANDLER);
                handler.CallStatic("tickWatchdog");
            }
            catch
            {
                // Ignore — not critical
            }
        }
#endif

        // ── iOS ──

#if UNITY_IOS && !UNITY_EDITOR
        void InstallIOSHandlers()
        {
            bool ok = Bugpunch_InstallCrashHandlers(_crashDir);
            if (!ok)
                Debug.LogWarning("[Bugpunch] iOS native crash handler installation failed");
        }
#endif

        // ── Metadata ──

        void UpdateMetadata()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var handler = new AndroidJavaClass(JAVA_CRASH_HANDLER);
                handler.CallStatic("setMetadata",
                    Application.version, Application.identifier, Application.unityVersion,
                    SystemInfo.deviceModel, SystemInfo.operatingSystem,
                    SystemInfo.graphicsDeviceName);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Bugpunch] Failed to set Android crash metadata: {e.Message}");
            }
#elif UNITY_IOS && !UNITY_EDITOR
            Bugpunch_SetCrashMetadata(
                Application.version, Application.identifier, Application.unityVersion,
                SystemInfo.deviceModel, SystemInfo.operatingSystem,
                SystemInfo.graphicsDeviceName);
#endif
        }

        // ── Pending Crash Report Upload ──

        IEnumerator UploadPendingCrashReports()
        {
            // Wait a frame for BugpunchClient to finish initializing
            yield return null;

            var crashFiles = GetPendingCrashFiles();
            if (crashFiles.Count == 0)
            {
                Debug.Log("[Bugpunch] No pending crash reports from previous session");
                yield break;
            }

            Debug.Log($"[Bugpunch] Found {crashFiles.Count} pending crash report(s) from previous session");

            var serverUrl = _config?.serverUrl;
            var apiKey = _config?.apiKey;
            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(apiKey))
            {
                Debug.LogWarning("[Bugpunch] Can't upload crash reports — serverUrl/apiKey not configured");
                yield break;
            }

            int uploaded = 0;
            foreach (var filePath in crashFiles)
            {
                if (uploaded >= maxPendingUploads) break;

                string contents;
                try
                {
                    contents = File.ReadAllText(filePath, Encoding.UTF8);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Bugpunch] Failed to read crash file {filePath}: {e.Message}");
                    continue;
                }

                var parsed = ParseCrashFile(contents);
                yield return UploadCrashReport(parsed, serverUrl, apiKey);

                // Delete the file after successful upload (or after max retries)
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Bugpunch] Failed to delete crash file {filePath}: {e.Message}");
                }

                uploaded++;
            }

            Debug.Log($"[Bugpunch] Uploaded {uploaded} crash report(s)");
        }

        List<string> GetPendingCrashFiles()
        {
            var result = new List<string>();

            // Check the C#-managed crash dir (works on all platforms)
            if (Directory.Exists(_crashDir))
            {
                try
                {
                    var files = Directory.GetFiles(_crashDir, "*.crash");
                    result.AddRange(files);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Bugpunch] Error scanning crash dir: {e.Message}");
                }
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            // Also check the native crash dir (managed by Java side)
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var handler = new AndroidJavaClass(JAVA_CRASH_HANDLER);

                var nativeFiles = handler.CallStatic<string[]>("getPendingCrashFiles", activity);
                if (nativeFiles != null)
                {
                    foreach (var f in nativeFiles)
                    {
                        if (!result.Contains(f))
                            result.Add(f);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Bugpunch] Error getting Android native crash files: {e.Message}");
            }
#elif UNITY_IOS && !UNITY_EDITOR
            // iOS native crash files are in the same dir, already scanned above
            // But also check via native API in case the dir path differs
            try
            {
                var nativePaths = Bugpunch_GetPendingCrashFiles();
                if (!string.IsNullOrEmpty(nativePaths))
                {
                    foreach (var p in nativePaths.Split('\n'))
                    {
                        if (!string.IsNullOrEmpty(p) && !result.Contains(p))
                            result.Add(p);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Bugpunch] Error getting iOS native crash files: {e.Message}");
            }
#endif

            return result;
        }

        // ── Crash File Parsing ──

        /// <summary>
        /// Parse the BUGPUNCH_CRASH_V1 text format into a dictionary for upload.
        /// </summary>
        static CrashReport ParseCrashFile(string contents)
        {
            var report = new CrashReport();
            var lines = contents.Split('\n');
            var section = "";
            var sectionContent = new StringBuilder();

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');

                // Section markers
                if (line == "---STACK---" || line == "---MESSAGE---" ||
                    line == "---MAPS---" || line == "---ALL_THREADS---")
                {
                    section = line.Trim('-');
                    sectionContent.Clear();
                    continue;
                }
                if (line == "---END---" || line == "---END_MESSAGE---" ||
                    line == "---END_MAPS---" || line == "---END_ALL_THREADS---")
                {
                    switch (section)
                    {
                        case "STACK": report.stackTrace = sectionContent.ToString(); break;
                        case "MESSAGE": report.message = sectionContent.ToString(); break;
                        case "MAPS": report.maps = sectionContent.ToString(); break;
                        case "ALL_THREADS": report.allThreads = sectionContent.ToString(); break;
                    }
                    section = "";
                    continue;
                }

                if (!string.IsNullOrEmpty(section))
                {
                    sectionContent.AppendLine(line);
                    continue;
                }

                // Key:value pairs
                var colonIdx = line.IndexOf(':');
                if (colonIdx > 0 && colonIdx < line.Length - 1)
                {
                    var key = line.Substring(0, colonIdx);
                    var value = line.Substring(colonIdx + 1);
                    switch (key)
                    {
                        case "type": report.type = value; break;
                        case "signal": report.signal = value; break;
                        case "signal_code": report.signalCode = value; break;
                        case "fault_addr": report.faultAddr = value; break;
                        case "exception_type": report.exceptionType = value; break;
                        case "exception_code": report.exceptionCode = value; break;
                        case "timestamp": report.timestamp = value; break;
                        case "platform": report.platform = value; break;
                        case "app_version": report.appVersion = value; break;
                        case "bundle_id": report.bundleId = value; break;
                        case "unity_version": report.unityVersion = value; break;
                        case "device_model": report.deviceModel = value; break;
                        case "os_version": report.osVersion = value; break;
                        case "gpu": report.gpu = value; break;
                        case "pid": report.pid = value; break;
                        case "tid": report.tid = value; break;
                        case "thread": report.threadName = value; break;
                        case "elapsed_ms": report.anrElapsedMs = value; break;
                        case "is_terminating": report.isTerminating = value; break;
                    }
                }
            }

            return report;
        }

        IEnumerator UploadCrashReport(CrashReport report, string serverUrl, string apiKey)
        {
            // Build the JSON payload
            var json = new StringBuilder(8192);
            json.Append("{");
            json.Append($"\"type\":\"Crash\",");
            json.Append($"\"title\":\"{Esc(BuildCrashTitle(report))}\",");
            json.Append($"\"description\":\"{Esc(BuildCrashDescription(report))}\",");
            json.Append($"\"timestamp\":\"{Esc(report.timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())}\",");
            json.Append($"\"crashType\":\"{Esc(report.type ?? "UNKNOWN")}\",");

            // Device info
            json.Append("\"device\":{");
            json.Append($"\"model\":\"{Esc(report.deviceModel ?? "")}\",");
            json.Append($"\"os\":\"{Esc(report.osVersion ?? "")}\",");
            json.Append($"\"platform\":\"{Esc(report.platform ?? "")}\",");
            json.Append($"\"gpu\":\"{Esc(report.gpu ?? "")}\"");
            json.Append("},");

            // App info
            json.Append("\"app\":{");
            json.Append($"\"version\":\"{Esc(report.appVersion ?? "")}\",");
            json.Append($"\"bundleId\":\"{Esc(report.bundleId ?? "")}\",");
            json.Append($"\"unityVersion\":\"{Esc(report.unityVersion ?? "")}\"");
            json.Append("},");

            // Crash details
            json.Append("\"crash\":{");
            json.Append($"\"signal\":\"{Esc(report.signal ?? "")}\",");
            json.Append($"\"signalCode\":\"{Esc(report.signalCode ?? "")}\",");
            json.Append($"\"faultAddr\":\"{Esc(report.faultAddr ?? "")}\",");
            json.Append($"\"exceptionType\":\"{Esc(report.exceptionType ?? "")}\",");
            json.Append($"\"exceptionCode\":\"{Esc(report.exceptionCode ?? "")}\",");
            json.Append($"\"pid\":\"{Esc(report.pid ?? "")}\",");
            json.Append($"\"tid\":\"{Esc(report.tid ?? "")}\",");
            json.Append($"\"thread\":\"{Esc(report.threadName ?? "")}\",");
            json.Append($"\"anrElapsedMs\":\"{Esc(report.anrElapsedMs ?? "")}\",");
            json.Append($"\"isTerminating\":\"{Esc(report.isTerminating ?? "")}\",");
            json.Append($"\"stackTrace\":\"{Esc(report.stackTrace ?? "")}\",");
            json.Append($"\"message\":\"{Esc(report.message ?? "")}\",");
            json.Append($"\"maps\":\"{Esc(report.maps ?? "")}\",");
            json.Append($"\"allThreads\":\"{Esc(report.allThreads ?? "")}\"");
            json.Append("}");

            json.Append("}");

            var url = HttpBase(serverUrl) + "/api/reports/crash";
            var body = Encoding.UTF8.GetBytes(json.ToString());

            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(body);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("X-Api-Key", apiKey);
                req.timeout = 30;

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[Bugpunch] Crash report uploaded ({report.type}: {report.signal ?? report.message ?? "unknown"})");
                }
                else
                {
                    Debug.LogWarning($"[Bugpunch] Crash report upload failed: {req.error} (HTTP {req.responseCode})");
                }
            }
        }

        static string BuildCrashTitle(CrashReport report)
        {
            switch (report.type)
            {
                case "NATIVE_SIGNAL":
                    return $"Native Crash: {report.signal ?? "UNKNOWN"} at {report.faultAddr ?? "?"}";
                case "MACH_EXCEPTION":
                    return $"Mach Exception: type={report.exceptionType} code={report.exceptionCode}";
                case "ANR":
                    return $"ANR: Main thread unresponsive ({report.anrElapsedMs ?? "?"}ms)";
                case "UNHANDLED_EXCEPTION":
                    var msg = report.message ?? "";
                    return $"Unhandled Exception: {(msg.Length > 100 ? msg.Substring(0, 100) : msg)}";
                case "UNOBSERVED_TASK_EXCEPTION":
                    var tmsg = report.message ?? "";
                    return $"Unobserved Task Exception: {(tmsg.Length > 80 ? tmsg.Substring(0, 80) : tmsg)}";
                default:
                    return $"Crash: {report.type ?? "UNKNOWN"}";
            }
        }

        static string BuildCrashDescription(CrashReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Platform: {report.platform}");
            sb.AppendLine($"Device: {report.deviceModel}");
            sb.AppendLine($"OS: {report.osVersion}");
            sb.AppendLine($"App: {report.bundleId} v{report.appVersion}");
            sb.AppendLine($"Unity: {report.unityVersion}");
            if (!string.IsNullOrEmpty(report.signal))
                sb.AppendLine($"Signal: {report.signal} (code={report.signalCode})");
            if (!string.IsNullOrEmpty(report.faultAddr))
                sb.AppendLine($"Fault address: {report.faultAddr}");
            if (!string.IsNullOrEmpty(report.message))
            {
                sb.AppendLine("Message:");
                sb.AppendLine(report.message);
            }
            if (!string.IsNullOrEmpty(report.stackTrace))
            {
                sb.AppendLine("Stack trace:");
                sb.AppendLine(report.stackTrace);
            }
            return sb.ToString();
        }

        // ── Helpers ──

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"")
             .Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t") ?? "";

        static string HttpBase(string serverUrl)
        {
            var trimmed = serverUrl.TrimEnd('/');
            if (trimmed.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
                return "https://" + trimmed.Substring("wss://".Length);
            if (trimmed.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
                return "http://" + trimmed.Substring("ws://".Length);
            return trimmed;
        }

        // ── Crash report data structure ──

        class CrashReport
        {
            // Header
            public string type;       // NATIVE_SIGNAL, MACH_EXCEPTION, ANR, UNHANDLED_EXCEPTION, UNOBSERVED_TASK_EXCEPTION
            public string timestamp;
            public string platform;

            // Signal info
            public string signal;     // SIGSEGV, SIGABRT, etc.
            public string signalCode;
            public string faultAddr;

            // Mach exception info
            public string exceptionType;
            public string exceptionCode;

            // Process info
            public string pid;
            public string tid;
            public string threadName;

            // ANR info
            public string anrElapsedMs;

            // C# exception info
            public string isTerminating;
            public string message;

            // App/device metadata
            public string appVersion;
            public string bundleId;
            public string unityVersion;
            public string deviceModel;
            public string osVersion;
            public string gpu;

            // Sections
            public string stackTrace;
            public string maps;       // /proc/self/maps (Android only)
            public string allThreads; // All thread stacks (ANR only)
        }
    }
}
