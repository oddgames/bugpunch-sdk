using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using ODDGames.Recorder;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

using Newtonsoft.Json;
using Debug = UnityEngine.Debug;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// Captures rich diagnostic data during test execution.
    /// Active only in interactive editor mode (!Application.isBatchMode).
    /// Records screenshots, hierarchy snapshots, video, and an action timeline.
    /// Sessions are packaged as portable .zip archives after each test.
    ///
    /// This is a thin facade — capture, I/O, hierarchy building, failure reporting
    /// and metadata collection live in the TestReport/ subfolder. Public API stays here.
    /// </summary>
    internal static class TestReport
    {
        // ---------- Public API: callbacks & flags ----------

        /// <summary>
        /// Optional callback to supply or modify session metadata.
        /// Called during StartSession() after defaults are populated.
        /// </summary>
        public static Func<SessionMetadata, SessionMetadata> MetadataCallback;

        /// <summary>Fired after EndSession() completes and the zip is written.</summary>
        public static event Action<string> OnSessionEnded;

        /// <summary>
        /// Async callback invoked after session packaging. Awaited before TearDown completes.
        /// </summary>
        public static Func<string, Task> OnSessionEndedAsync;

        /// <summary>Auto-detect Git/Plastic SCM in the editor for branch/commit metadata.</summary>
        public static bool AutoDetectVCS = true;

        /// <summary>Capture Application.persistentDataPath into the diag zip on failure.</summary>
        public static bool CapturePersistentData = true;

        /// <summary>Server-issued run ID for grouping sessions from the same test batch.</summary>
        internal static string RunId { get; set; }

        // ---------- Session state ----------

        static string _sessionFolder;
        static string _testName;
        static readonly List<string> _log = new();
        static readonly List<string> _screenshotPaths = new();
        static int _screenshotCounter;
        static int _hierarchyCounter;
        static int _lastHierarchyFrame = -1;
        static string _lastHierarchyFile;
        static readonly Dictionary<string, HierarchySnapshot> _pendingHierarchies = new();
        static bool _active;
        static bool _failureRecorded;
        static bool _suppressLogCapture;
        static float _sessionStartTime;
        static DiagSession _session;
        static string _lastFailedSessionFolder;
        static string _lastSessionFolder;
        static RecordingSession _recordingSession;
        static float _recordingStartTime;
        static VideoTimestampTracker _timestampTracker;
        static readonly HttpClient _httpClient = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        static DateTime _uploadBackoffUntil;

        // ---------- Public read-only properties ----------

        /// <summary>Whether diagnostics are currently active (interactive editor only).</summary>
        public static bool IsActive => _active;

        /// <summary>Current session folder path, or null if no session.</summary>
        public static string SessionFolder => _sessionFolder;

        /// <summary>The folder path of the most recent failed session, persists across tests.</summary>
        public static string LastFailedSessionFolder => _lastFailedSessionFolder;

        /// <summary>The folder path of the most recent session (pass or fail), persists across tests.</summary>
        public static string LastSessionFolder => _lastSessionFolder;

        /// <summary>Returns the root diagnostics folder path.</summary>
        public static string DiagnosticsRoot =>
            Path.Combine(Application.temporaryCachePath, "UIAutomation_Diagnostics");

        /// <summary>Returns all diagnostic session zip files, most recent first.</summary>
        public static List<string> GetDiagnosticsFolders()
        {
            var root = DiagnosticsRoot;
            if (!Directory.Exists(root))
                return new List<string>();

            return Directory.GetFiles(root, "*.zip")
                .OrderByDescending(File.GetCreationTime)
                .ToList();
        }

        // ---------- Lifecycle ----------

        /// <summary>
        /// Starts a new diagnostic session. Called from UIAutomationTestFixture.SetUp().
        /// Creates a timestamped folder under Application.temporaryCachePath/UIAutomation_Diagnostics/.
        /// </summary>
        public static void StartSession(string testName)
        {
            if (Application.isBatchMode)
            {
                _active = false;
                return;
            }

            _active = true;
            _failureRecorded = false;
            _testName = testName ?? "UnknownTest";
            _log.Clear();
            _screenshotPaths.Clear();
            SessionManager.PendingFileWrites.Clear();
            SessionManager.PendingScreenshots.Clear();
            _screenshotCounter = 0;
            _hierarchyCounter = 0;
            _lastHierarchyFrame = -1;
            _lastHierarchyFile = null;
            _pendingHierarchies.Clear();
            _sessionStartTime = Time.realtimeSinceStartup;

            _session = new DiagSession
            {
                testName = _testName,
                scene = SceneManager.GetActiveScene().name,
                screenWidth = Screen.width,
                screenHeight = Screen.height,
                startTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                metadata = MetadataCollector.Build(RunId, AutoDetectVCS, MetadataCallback)
            };

            var safeName = SessionManager.SanitizeFileName(_testName);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var sessionName = $"{safeName}_{timestamp}";
            var root = DiagnosticsRoot;
            _sessionFolder = Path.Combine(root, sessionName);

            try
            {
                Directory.CreateDirectory(_sessionFolder);
                LogMessage($"Session started: {_testName}");
                LogMessage($"Scene: {_session.scene}");
                LogMessage($"Screen: {Screen.width}x{Screen.height}");
                LogMessage($"Time: {_session.startTime}");
                LogMessage($"Folder: {_sessionFolder}");

                // Start capturing all console output
                Application.logMessageReceived += OnLogMessageReceived;

                StartRecording();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestReport] Failed to create session folder: {ex.Message}");
                _active = false;
            }
        }

        /// <summary>
        /// Ends the diagnostic session. On pass: deletes the entire folder.
        /// On failure: keeps it and logs the path. Uses internal failure tracking
        /// because TestContext.Result.Outcome may not be set when TearDown runs.
        /// </summary>
        public static async Task EndSession()
        {
            if (!_active) return;
            Profiler.BeginSample("TestReport.EndSession");

            string zipResult = null;
            try
            {
                // Stop capturing console output
                Application.logMessageReceived -= OnLogMessageReceived;

                // Flush any screenshots still waiting for end-of-frame capture
                SessionManager.FlushPendingScreenshots();

                // Wait for any pending file writes (screenshots) to complete before zipping
                if (SessionManager.PendingFileWrites.Count > 0)
                {
                    try { Task.WaitAll(SessionManager.PendingFileWrites.ToArray()); }
                    catch { }
                    SessionManager.PendingFileWrites.Clear();
                }

                // Set the result from NUnit's actual test outcome.
                if (_session != null)
                {
#if UNITY_INCLUDE_TESTS
                    try
                    {
                        var status = NUnit.Framework.TestContext.CurrentContext.Result.Outcome.Status;
                        _session.result = status == NUnit.Framework.Interfaces.TestStatus.Passed ? "pass" : "fail";
                        _failureRecorded = _session.result == "fail";
                    }
                    catch
                    {
                        _session.result = _failureRecorded ? "fail" : "pass";
                    }
#else
                    _session.result = _failureRecorded ? "fail" : "pass";
#endif
                }

                // Serialize data on main thread (JsonUtility requires it)
                var sessionJson = _session != null ? JsonUtility.ToJson(_session, true) : null;
                var hierarchyData = SessionIO.SerializeHierarchies(_pendingHierarchies);
                _pendingHierarchies.Clear();

                var logText = string.Join("\n", _log);
                var folder = _sessionFolder;
                var failed = _failureRecorded;

                // Stop recording as late as possible — captures the entire teardown sequence.
                StopRecording();

                // Re-serialize session JSON now that StopRecording() has set videoFile/videoDuration
                if (_session != null)
                    sessionJson = JsonUtility.ToJson(_session, true);

                // Write files and package zip synchronously
                try
                {
                    SessionIO.WriteHierarchyFiles(folder, hierarchyData);
                    SessionIO.WriteSessionJsonFile(folder, sessionJson);
                    SessionIO.WriteLogFile(folder, logText);
                    SessionIO.CapturePersistentDataIfFailed(folder, failed, CapturePersistentData);

                    var zipPath = SessionIO.PackToZip(folder);
                    _lastSessionFolder = zipPath ?? folder;
                    if (failed)
                        _lastFailedSessionFolder = _lastSessionFolder;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[TestReport] Session packaging failed: {ex.Message}");
                    _lastSessionFolder = folder;
                }

                zipResult = _lastSessionFolder;

                if (_failureRecorded)
                {
                    _lastFailedSessionFolder = _lastSessionFolder;
                    Debug.Log($"[TestReport] Test FAILED — diagnostics: {_lastSessionFolder}");
                }
                else
                {
                    Debug.Log($"[TestReport] Test PASSED — diagnostics: {_lastSessionFolder}");
                }

                // Fire event synchronously for non-upload listeners
                try { OnSessionEnded?.Invoke(_lastSessionFolder); }
                catch (Exception evtEx) { Debug.LogWarning($"[TestReport] OnSessionEnded handler error: {evtEx.Message}"); }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestReport] EndSession error: {ex.Message}");
            }
            finally
            {
                _active = false;
                _sessionFolder = null;
                _testName = null;
                _session = null;
                Profiler.EndSample(); // TestReport.EndSession
            }

            // Invoke async callback (e.g. auto-upload) — awaited so TearDown blocks until complete
            if (!string.IsNullOrEmpty(zipResult) && OnSessionEndedAsync != null)
            {
                try { await OnSessionEndedAsync(zipResult); }
                catch (Exception ex) { Debug.LogWarning($"[TestReport] OnSessionEndedAsync error: {ex.Message}"); }
            }
        }

        /// <summary>Logs an action entry with timestamp and frame number.</summary>
        public static void LogAction(string message)
        {
            LogAction(message, null, 0, null);
        }

        /// <summary>Logs an action entry with timestamp, frame number, and source code location.</summary>
        public static void LogAction(string message, string callerFile, int callerLine, string callerMethod)
        {
            Profiler.BeginSample("TestReport.LogAction");

            // Determine action type from message prefix
            string type = "info";
            if (message.StartsWith("START:")) type = "start";
            else if (message.StartsWith("COMPLETE:")) type = "success";
            else if (message.StartsWith("WARN:")) type = "warn";
            else if (message.StartsWith("FAILED:")) type = "failure";

            // Always log to Unity console (even when TestReport session isn't active).
            float ts = _active ? Time.realtimeSinceStartup - _sessionStartTime : 0f;
            int actionLogType;
            string formattedMsg;
            if (_active) _suppressLogCapture = true;
            if (type == "failure")
            {
                formattedMsg = $"<color=#FF5555>FAILED</color> [{ts:F2}s] {message.Substring(8)}"; // strip "FAILED: "
                Debug.LogError(formattedMsg); actionLogType = 10;
            }
            else if (type == "warn")
            {
                formattedMsg = $"<color=#FFAA33>WARN</color> [{ts:F2}s] {message.Substring(6)}"; // strip "WARN: "
                Debug.LogWarning(formattedMsg); actionLogType = 9;
            }
            else if (type == "success")
            {
                formattedMsg = $"<color=#55CC55>DONE</color> [{ts:F2}s] {message.Substring(10)}"; // strip "COMPLETE: "
                Debug.Log(formattedMsg); actionLogType = 8;
            }
            else if (type == "start")
            {
                formattedMsg = $"<color=#5599FF>START</color> [{ts:F2}s] {message.Substring(7)}"; // strip "START: "
                Debug.Log(formattedMsg); actionLogType = 7;
            }
            else
            {
                formattedMsg = $"[{ts:F2}s] {message}";
                Debug.Log(formattedMsg); actionLogType = 3;
            }
            if (_active) _suppressLogCapture = false;

            // Add structured data only when session is active
            if (!_active || _session == null) { Profiler.EndSample(); return; }

            LogMessage(message);

            // Capture the action as a log entry with custom type
            _session.logs.Add(new LogEntry
            {
                logType = actionLogType,
                message = formattedMsg,
                timestamp = ts,
                frame = Time.frameCount
            });

            // Capture hierarchy + screenshot on start and failure events at the exact same moment
            string hierarchyFile = null;
            if (type == "start" || type == "failure")
                hierarchyFile = GetOrCaptureHierarchy();

            _session.events.Add(new DiagEvent
            {
                type = type,
                label = message,
                timestamp = Time.realtimeSinceStartup - _sessionStartTime,
                frame = Time.frameCount,
                hierarchyFile = hierarchyFile,
                callerFile = callerFile,
                callerLine = callerLine,
                callerMethod = callerMethod
            });

            // Capture screenshot on start/failure so the viewer always has a visual snapshot.
            if (type == "start" || type == "failure")
                CaptureScreenshot(type);

            Profiler.EndSample(); // TestReport.LogAction
        }

        /// <summary>Marks the current session as having a failure.</summary>
        public static void RecordFailure()
        {
            _failureRecorded = true;
        }

        /// <summary>
        /// Queues a screenshot capture for end-of-frame. Metadata is recorded synchronously;
        /// the actual pixel capture is deferred to WaitForEndOfFrame via a coroutine.
        /// </summary>
        public static void CaptureScreenshot(string label)
        {
            if (!_active) return;

            _screenshotCounter++;
            var safeLabel = SessionManager.SanitizeFileName(label);
            if (safeLabel.Length > 60) safeLabel = safeLabel.Substring(0, 60);
            var filename = $"screenshot_{_screenshotCounter:D3}_{safeLabel}.png";
            var path = Path.Combine(_sessionFolder, filename);

            // Record metadata synchronously — links to current event context
            _screenshotPaths.Add(path);
            LogMessage($"Screenshot: {filename}");

            if (_session != null)
            {
                _session.logs.Add(new LogEntry
                {
                    logType = 5, // Screenshot
                    message = $"Screenshot: {label}",
                    stackTrace = filename, // store filename for viewer to load
                    timestamp = Time.realtimeSinceStartup - _sessionStartTime,
                    frame = Time.frameCount
                });
            }

            // Link screenshot to the most recent event
            if (_session != null && _session.events.Count > 0)
            {
                var lastEvent = _session.events[_session.events.Count - 1];
                lastEvent.screenshotFile = filename;
            }

            // Queue the actual pixel capture for end of frame
            SessionManager.QueueScreenshot(path);
        }

        /// <summary>
        /// Captures a search-progress screenshot during Find() timeout.
        /// Skips hierarchy to avoid blocking the main thread.
        /// </summary>
        public static void CaptureSearchSnapshot(string searchDescription, float elapsedSeconds)
        {
            if (!_active) return;

            if (_session != null)
            {
                _session.events.Add(new DiagEvent
                {
                    type = "search_snapshot",
                    label = $"Searching: {searchDescription} ({elapsedSeconds:F1}s)",
                    timestamp = Time.realtimeSinceStartup - _sessionStartTime,
                    frame = Time.frameCount
                });
            }

            CaptureScreenshot($"search_{elapsedSeconds:F1}s_{SessionManager.SanitizeFileName(searchDescription)}");
        }

        /// <summary>
        /// Captures a detailed hierarchy snapshot with full component properties.
        /// Called explicitly by ActionExecutor.Snapshot() for on-demand diagnostic captures.
        /// </summary>
        public static string CaptureDetailedSnapshot(int maxDepth = -1)
        {
            if (!_active) return null;

            try
            {
                _hierarchyCounter++;
                var filename = $"hierarchy_{_hierarchyCounter:D3}.json";

                var snapshot = SessionManager.BuildSnapshot(detailed: true, maxDepth: maxDepth);
                _pendingHierarchies[filename] = snapshot;

                if (_session != null)
                {
                    var depthStr = (maxDepth < 0) ? "unlimited" : maxDepth.ToString();
                    float ts = Time.realtimeSinceStartup - _sessionStartTime;

                    _session.events.Add(new DiagEvent
                    {
                        type = "snapshot",
                        label = $"Hierarchy snapshot (depth={depthStr})",
                        timestamp = ts,
                        frame = Time.frameCount,
                        hierarchyFile = filename
                    });

                    _session.logs.Add(new LogEntry
                    {
                        logType = 6, // Snapshot
                        message = $"Hierarchy snapshot (depth={depthStr})",
                        stackTrace = filename,
                        timestamp = ts,
                        frame = Time.frameCount
                    });
                }

                return filename;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestReport] Detailed snapshot failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Dumps the full scene hierarchy to a file and returns the content.
        /// </summary>
        public static string DumpHierarchy()
        {
            if (!_active) return string.Empty;
            return FailureReporter.DumpHierarchy(_sessionFolder, LogMessage);
        }

        /// <summary>
        /// Builds a failure report for a search failure. Returns a short message
        /// for the assertion exception. Full diagnostic report goes to FAILURE_REPORT.txt.
        /// </summary>
        public static string BuildFailureReport(Search search, float searchTime)
        {
            var shortMsg = $"Element not found within {searchTime}s — Search: {search}";

            if (!_active) return shortMsg;

            _failureRecorded = true;
            _lastFailedSessionFolder = _sessionFolder;

            // Take final failure screenshot
            CaptureScreenshot("failure");

            // Dump full hierarchy (text version)
            FailureReporter.DumpHierarchy(_sessionFolder, LogMessage);

            // Build the combined report text
            var report = FailureReporter.BuildReportText(search, searchTime, _sessionFolder, _screenshotPaths);

            try
            {
                var reportPath = Path.Combine(_sessionFolder, "FAILURE_REPORT.txt");
                File.WriteAllText(reportPath, report);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestReport] Failed to write report: {ex.Message}");
            }

            // Write session JSON and log
            SessionIO.WriteSessionJsonDirect(_sessionFolder, _session);
            SessionIO.WriteLogFileDirect(_sessionFolder, _log);

            return shortMsg;
        }

        /// <summary>
        /// Uploads a diagnostic session zip to a remote server via HTTP multipart POST.
        /// </summary>
        public static async Task UploadSession(string zipPath, string serverUrl, string apiKey = null)
        {
            if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
            {
                Debug.LogWarning($"[TestReport] Upload skipped — file not found: {zipPath}");
                return;
            }

            if (string.IsNullOrEmpty(serverUrl))
            {
                Debug.LogWarning("[TestReport] Upload skipped — no server URL configured");
                return;
            }

            // Back off for 60s after a connection failure to avoid spamming errors
            if (DateTime.UtcNow < _uploadBackoffUntil)
                return;

            try
            {
                var url = serverUrl.TrimEnd('/') + "/api/sessions/upload";
                var content = new MultipartFormDataContent();
                var fileBytes = await Task.Run(() => File.ReadAllBytes(zipPath)).ConfigureAwait(false);
                content.Add(new ByteArrayContent(fileBytes), "file", Path.GetFileName(zipPath));

                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                if (!string.IsNullOrEmpty(apiKey))
                    request.Headers.Add("X-Api-Key", apiKey);

                using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    Debug.Log($"[TestReport] Uploaded session to {serverUrl}: {Path.GetFileName(zipPath)}");
                }
                else
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Debug.LogWarning($"[TestReport] Upload failed ({response.StatusCode}): {body}");
                }
            }
            catch (Exception ex)
            {
                _uploadBackoffUntil = DateTime.UtcNow.AddSeconds(60);
                Debug.LogWarning($"[TestReport] Upload to {serverUrl} failed — retrying in 60s\n{ex}");
            }
        }

        // ---------- Internal helpers (state shared with sub-modules) ----------

        /// <summary>
        /// Returns a cached hierarchy filename for this frame, or captures a new one.
        /// Hierarchy is built in memory and deferred to disk at session end.
        /// </summary>
        static string GetOrCaptureHierarchy()
        {
            if (!_active) return null;

            if (Time.frameCount == _lastHierarchyFrame && _lastHierarchyFile != null)
                return _lastHierarchyFile;

            var filename = CaptureHierarchySnapshot();
            _lastHierarchyFrame = Time.frameCount;
            _lastHierarchyFile = filename;
            return filename;
        }

        static string CaptureHierarchySnapshot()
        {
            if (!_active) return null;
            Profiler.BeginSample("TestReport.CaptureHierarchy");

            try
            {
                _hierarchyCounter++;
                var filename = $"hierarchy_{_hierarchyCounter:D3}.json";
                var snapshot = SessionManager.BuildSnapshot(detailed: false);
                _pendingHierarchies[filename] = snapshot;
                return filename;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestReport] Hierarchy snapshot failed: {ex.Message}");
                return null;
            }
            finally
            {
                Profiler.EndSample(); // TestReport.CaptureHierarchy
            }
        }

        static void LogMessage(string message)
        {
            var entry = $"[{DateTime.UtcNow:HH:mm:ss.fff}] [frame {Time.frameCount}] {message}";
            _log.Add(entry);
        }

        /// <summary>
        /// Captures all Unity console output into the session's structured log list.
        /// </summary>
        static void OnLogMessageReceived(string message, string stackTrace, LogType logType)
        {
            if (!_active || _session == null || _suppressLogCapture) return;

            _session.logs.Add(new LogEntry
            {
                logType = (int)logType,
                message = message,
                stackTrace = stackTrace,
                timestamp = Time.realtimeSinceStartup - _sessionStartTime,
                frame = Time.frameCount
            });
        }

        // ---------- Recording ----------

        static async void StartRecording()
        {
            try
            {
                var settings = new RecorderSettings
                {
                    OutputPath = Path.Combine(_sessionFolder, "recording"),
                    Width = 1280,
                    Height = 720,
                    FrameRate = 15,
                    IncludeAudio = false
                };
                _recordingSession = await MediaRecorder.StartAsync(settings);
                _recordingStartTime = Time.realtimeSinceStartup;
                _timestampTracker = VideoTimestampTracker.Begin(_sessionStartTime);
                LogMessage("Recording started");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestReport] Failed to start recording: {ex.Message}");
                _recordingSession = null;
            }
        }

        static void StopRecording()
        {
            if (_recordingSession == null) return;
            Profiler.BeginSample("TestReport.StopRecording");

            try
            {
                // Stop the timestamp tracker and write the sidecar CSV before stopping the recorder
                if (_timestampTracker != null)
                {
                    var csvPath = Path.Combine(_sessionFolder, "video_timestamps.csv");
                    _timestampTracker.StopAndWrite(csvPath);
                    UnityEngine.Object.Destroy(_timestampTracker.gameObject);
                    _timestampTracker = null;

                    if (_session != null && File.Exists(csvPath))
                    {
                        _session.videoTimestampsFile = "video_timestamps.csv";
                        _session.videoStartOffset = _recordingStartTime - _sessionStartTime;
                    }
                }

                if (_recordingSession.IsRecording)
                {
                    var stopTask = _recordingSession.StopAsync();
                    // Block briefly — we're in TearDown, need the file path before cleanup
                    if (stopTask.Wait(TimeSpan.FromSeconds(5)))
                    {
                        var videoPath = stopTask.Result;
                        if (_session != null && !string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
                        {
                            _session.videoFile = Path.GetFileName(videoPath);
                            _session.videoDuration = Time.realtimeSinceStartup - _recordingStartTime;
                            LogMessage($"Recording saved: {_session.videoFile}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[TestReport] Recording stop timed out");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestReport] Failed to stop recording: {ex.Message}");
            }
            finally
            {
                _recordingSession?.Dispose();
                _recordingSession = null;
                Profiler.EndSample(); // TestReport.StopRecording
            }
        }
    }
}
