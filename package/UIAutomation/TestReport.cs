#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using ODDGames.Recorder;
using TMPro;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using Newtonsoft.Json;
using Debug = UnityEngine.Debug;

namespace ODDGames.UIAutomation
{
    /// <summary>
    /// Captures rich diagnostic data during test execution.
    /// Active only in interactive editor mode (!Application.isBatchMode).
    /// Records screenshots, hierarchy snapshots, video, and an action timeline.
    /// Sessions are packaged as portable .zip archives after each test.
    /// </summary>
    internal static class TestReport
    {
        #region Serializable data classes

        [Serializable]
        internal class SessionMetadata
        {
            public string runId;         // server-issued run ID for grouping test batches
            public string project;       // e.g. "MonsterTruckDestruction"
            public string branch;        // e.g. "main", "feature/new-ui"
            public string commit;        // short hash
            public string appVersion;    // Application.version
            public string platform;      // RuntimePlatform string
            public string machineName;   // Environment.MachineName
            public List<string> customKeys = new();   // parallel lists (JsonUtility doesn't support Dictionary)
            public List<string> customValues = new();
        }

        [Serializable]
        internal class DiagSession
        {
            public string testName;
            public string result;          // "pass", "fail", or "warn" — set from NUnit TestContext
            public string scene;
            public int screenWidth;
            public int screenHeight;
            public string startTime;
            public string videoFile;      // relative path to recording MP4
            public float videoDuration;   // video duration in seconds
            public float videoStartOffset; // seconds between session start and recording start
            public string videoTimestampsFile; // relative path to frame timestamp CSV sidecar
            public SessionMetadata metadata;
            public List<DiagEvent> events = new();
            public List<LogEntry> logs = new();
        }

        [Serializable]
        internal class LogEntry
        {
            public int logType;     // Unity LogType: 0=Error, 1=Assert, 2=Warning, 3=Log, 4=Exception
                                    // Custom: 5=Screenshot, 6=Snapshot, 7=ActionStart, 8=ActionSuccess, 9=ActionWarn, 10=ActionFailure
            public string message;
            public string stackTrace; // for screenshots/snapshots: contains the filename
            public float timestamp;
            public int frame;
        }

        [Serializable]
        internal class DiagEvent
        {
            public string type; // "start", "success", "warn", "failure", "search_snapshot"
            public string label;
            public float timestamp;
            public int frame;
            public string screenshotFile; // null if no screenshot
            public string hierarchyFile; // null if no hierarchy snapshot
            public string callerFile;   // source file path of test code
            public int callerLine;      // line number in test code
            public string callerMethod; // method name in test code
        }

        [Serializable]
        internal class HierarchySnapshot
        {
            public int screenWidth;
            public int screenHeight;
            public float[] cameraMatrix;    // 4x4 view-projection matrix (column-major) for 3D→screen projection
            public List<HierarchyNode> roots = new(); // nested tree — matches web viewer format
        }

        [Serializable]
        internal class HierarchyNode
        {
            public string name;
            public string path;
            public string text;             // text content from TMP_Text or legacy Text (null if no text)
            public bool active;
            public bool isScene;            // true for scene header nodes (top-level grouping)
            public int instanceId;          // GameObject.GetInstanceID() for unique identification
            public float x, y, w, h;       // screen-space bounds (for UI elements in screen space)
            public float depth;             // rendering order (lower = closer/frontmost for 3D, negative for UI)
            public float[] worldBounds;     // [cx, cy, cz, ex, ey, ez] — world-space AABB center+extents (3D objects only)
            public string[] annotations;
            public int childCount;          // total direct children
            public int siblingIndex;        // sibling index in parent — for Unity Hierarchy ordering
            public List<string> properties;  // detailed component properties (null when not detailed)
            public List<HierarchyNode> children = new();
        }

        #endregion

        /// <summary>
        /// Optional callback to supply or modify session metadata.
        /// Called during StartSession() after defaults are populated.
        /// Return the modified metadata (or a new instance) to override.
        /// </summary>
        public static Func<SessionMetadata, SessionMetadata> MetadataCallback;

        /// <summary>
        /// Fired after EndSession() completes and the zip is written.
        /// Passes the zip file path. Useful for auto-upload hooks.
        /// </summary>
        public static event Action<string> OnSessionEnded;

        /// <summary>
        /// When true (default), auto-detects Git/Plastic SCM in the editor
        /// and populates branch/commit in session metadata.
        /// </summary>
        public static bool AutoDetectVCS = true;

        /// <summary>
        /// When true (default), captures Application.persistentDataPath contents
        /// into the diagnostic zip on test failure. Set via Editor settings.
        /// </summary>
        public static bool CapturePersistentData = true;

        /// <summary>
        /// Server-issued run ID for grouping sessions from the same test batch.
        /// Set by the editor before tests start (via ICallbacks.RunStarted).
        /// </summary>
        internal static string RunId { get; set; }

        static string _sessionFolder;
        static string _testName;
        static readonly List<string> _log = new();
        static readonly List<string> _screenshotPaths = new();
        static readonly List<Task> _pendingFileWrites = new();
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

        /// <summary>Whether diagnostics are currently active (interactive editor only).</summary>
        public static bool IsActive => _active;

        /// <summary>Current session folder path, or null if no session.</summary>
        public static string SessionFolder => _sessionFolder;

        /// <summary>
        /// The folder path of the most recent failed session, persists across tests.
        /// </summary>
        public static string LastFailedSessionFolder => _lastFailedSessionFolder;

        /// <summary>
        /// The folder path of the most recent session (pass or fail), persists across tests.
        /// Used by the Test Runner button to open the diagnostics viewer.
        /// </summary>
        public static string LastSessionFolder => _lastSessionFolder;

        /// <summary>
        /// Returns the root diagnostics folder path.
        /// </summary>
        public static string DiagnosticsRoot =>
            Path.Combine(Application.temporaryCachePath, "UIAutomation_Diagnostics");

        /// <summary>
        /// Returns all diagnostic session zip files, most recent first.
        /// </summary>
        public static List<string> GetDiagnosticsFolders()
        {
            var root = DiagnosticsRoot;
            if (!Directory.Exists(root))
                return new List<string>();

            return Directory.GetFiles(root, "*.zip")
                .OrderByDescending(File.GetCreationTime)
                .ToList();
        }

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
            _pendingFileWrites.Clear();
            _pendingScreenshots.Clear();
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
                startTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                metadata = BuildSessionMetadata()
            };

            var safeName = SanitizeFileName(_testName);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
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
        /// Ends the diagnostic session. Called from UIAutomationTestFixture.TearDown().
        /// On pass: deletes the entire folder. On failure: keeps it and logs the path.
        /// Uses internal failure tracking (set by BuildFailureReport) because
        /// TestContext.Result.Outcome may not be set yet when TearDown runs.
        /// </summary>
        /// <summary>
        /// Async callback invoked after session packaging. Awaited before TearDown completes.
        /// Set by the editor auto-upload system to upload zips to the server.
        /// </summary>
        public static Func<string, Task> OnSessionEndedAsync;

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
                FlushPendingScreenshots();

                // Wait for any pending file writes (screenshots) to complete before zipping
                if (_pendingFileWrites.Count > 0)
                {
                    try { Task.WaitAll(_pendingFileWrites.ToArray()); }
                    catch { }
                    _pendingFileWrites.Clear();
                }

                // Set the result from NUnit's actual test outcome (not just internal failure tracking).
                // Tests that intentionally catch expected failures (e.g. ThrowsWhenNotFound) will
                // have _failureRecorded=true but NUnit reports them as Passed.
                if (_session != null)
                {
                    try
                    {
                        var status = NUnit.Framework.TestContext.CurrentContext.Result.Outcome.Status;
                        _session.result = status == NUnit.Framework.Interfaces.TestStatus.Passed ? "pass" : "fail";
                        // Override internal tracking with NUnit's actual result
                        _failureRecorded = _session.result == "fail";
                    }
                    catch
                    {
                        // Fallback to internal tracking if TestContext unavailable
                        _session.result = _failureRecorded ? "fail" : "pass";
                    }
                }

                // Serialize data on main thread (JsonUtility requires it)
                var sessionJson = _session != null ? JsonUtility.ToJson(_session, true) : null;
                var hierarchyData = new Dictionary<string, string>();
                foreach (var kvp in _pendingHierarchies)
                {
                    try { hierarchyData[kvp.Key] = JsonConvert.SerializeObject(kvp.Value, _hierarchyJsonSettings); }
                    catch (Exception ex) { Debug.LogWarning($"[TestReport] Hierarchy serialization failed for {kvp.Key}: {ex.Message}"); }
                }
                _pendingHierarchies.Clear();

                var logText = string.Join("\n", _log);
                var folder = _sessionFolder;
                var failed = _failureRecorded;

                // Stop recording as late as possible — captures the entire teardown sequence.
                // Must happen before writing files/zipping so the video file is on disk.
                StopRecording();

                // Re-serialize session JSON now that StopRecording() has set videoFile/videoDuration
                if (_session != null)
                    sessionJson = JsonUtility.ToJson(_session, true);

                // Write files and package zip synchronously
                try
                {
                    foreach (var kvp in hierarchyData)
                    {
                        try { File.WriteAllText(Path.Combine(folder, kvp.Key), kvp.Value); }
                        catch { }
                    }

                    if (sessionJson != null)
                    {
                        try { File.WriteAllText(Path.Combine(folder, "session.json"), sessionJson); }
                        catch { }
                    }

                    try { File.WriteAllText(Path.Combine(folder, "log.txt"), logText); }
                    catch { }

                    // Copy persistentDataPath on failure if enabled
                    if (failed && CapturePersistentData)
                    {
                        try
                        {
                            var persistentPath = Application.persistentDataPath;
                            if (Directory.Exists(persistentPath))
                            {
                                var destDir = Path.Combine(folder, "persistent_data");
                                CopyDirectory(persistentPath, destDir, maxDepth: 3, maxTotalBytes: 50 * 1024 * 1024);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[TestReport] Failed to capture persistentDataPath: {ex.Message}");
                        }
                    }

                    var zipPath = folder + ".zip";
                    try
                    {
                        if (File.Exists(zipPath)) File.Delete(zipPath);
                        ZipFile.CreateFromDirectory(folder, zipPath, System.IO.Compression.CompressionLevel.NoCompression, false);
                        Directory.Delete(folder, true);
                    }
                    catch { zipPath = null; }

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

        /// <summary>
        /// Logs an action entry with timestamp and frame number.
        /// </summary>
        public static void LogAction(string message)
        {
            LogAction(message, null, 0, null);
        }

        /// <summary>
        /// Logs an action entry with timestamp, frame number, and source code location.
        /// </summary>
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
            // When a session IS active, suppress OnLogMessageReceived to avoid duplicates —
            // we manually add a LogEntry with a custom action logType (7-10) below.
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

            // Capture hierarchy + screenshot on start and failure events at the exact
            // same moment so they're perfectly synced in the viewer. Hierarchy is cached
            // per frame so multiple events in the same frame share one snapshot.
            string hierarchyFile = null;
            if (type == "start" || type == "failure")
            {
                hierarchyFile = GetOrCaptureHierarchy();
            }

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

            // Capture screenshot on start/failure so the viewer always has a visual snapshot
            // pinned to the event. Called after adding the event so CaptureScreenshot links
            // it via the "last event" logic (sets screenshotFile on the event we just added).
            if (type == "start" || type == "failure")
            {
                CaptureScreenshot(type);
            }
            Profiler.EndSample(); // TestReport.LogAction
        }

        /// <summary>
        /// Marks the current session as having a failure.
        /// Called from ActionScope.Fail() so the folder is retained on TearDown.
        /// </summary>
        public static void RecordFailure()
        {
            _failureRecorded = true;
        }

        /// <summary>
        /// Queues a screenshot capture for end-of-frame when the back buffer is guaranteed
        /// to contain freshly rendered content. Metadata (event linking, log entry, filename)
        /// is recorded synchronously so it reflects the current action context. The actual
        /// pixel capture is deferred to WaitForEndOfFrame via a coroutine.
        /// </summary>
        public static void CaptureScreenshot(string label)
        {
            if (!_active) return;

            _screenshotCounter++;
            var safeLabel = SanitizeFileName(label);
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
            _pendingScreenshots.Enqueue(path);
            EnsureScreenshotCapturer();
        }

        // Queue of screenshot file paths waiting for end-of-frame capture
        static readonly Queue<string> _pendingScreenshots = new();
        static ScreenshotCapturer _screenshotCapturer;

        /// <summary>
        /// Creates or finds the coroutine host MonoBehaviour for end-of-frame captures.
        /// </summary>
        static void EnsureScreenshotCapturer()
        {
            if (_screenshotCapturer != null) return;
            var go = new GameObject("[TestReport.ScreenshotCapturer]")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            UnityEngine.Object.DontDestroyOnLoad(go);
            _screenshotCapturer = go.AddComponent<ScreenshotCapturer>();
        }

        /// <summary>
        /// Flushes all queued screenshots immediately (synchronous fallback).
        /// Called during EndSession to ensure no screenshots are lost.
        /// </summary>
        static void FlushPendingScreenshots()
        {
            while (_pendingScreenshots.Count > 0)
            {
                var path = _pendingScreenshots.Dequeue();
                try
                {
                    var texture = ScreenCapture.CaptureScreenshotAsTexture();
                    if (texture != null)
                    {
                        var pngBytes = texture.EncodeToPNG();
                        UnityEngine.Object.Destroy(texture);
                        _pendingFileWrites.Add(Task.Run(() =>
                        {
                            try { File.WriteAllBytes(path, pngBytes); }
                            catch { }
                        }));
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[TestReport] Screenshot flush failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Hidden MonoBehaviour that captures screenshots at WaitForEndOfFrame.
        /// This ensures the back buffer contains freshly rendered content.
        /// </summary>
        class ScreenshotCapturer : MonoBehaviour
        {
            System.Collections.IEnumerator Start()
            {
                while (true)
                {
                    yield return new WaitForEndOfFrame();
                    ProcessQueue();
                }
            }

            void ProcessQueue()
            {
                if (_pendingScreenshots.Count == 0) return;
                try
                {
                    // Capture once — all queued screenshots from this frame get the same pixels
                    var texture = ScreenCapture.CaptureScreenshotAsTexture();
                    if (texture == null) return;
                    var pngBytes = texture.EncodeToPNG();
                    UnityEngine.Object.Destroy(texture);

                    while (_pendingScreenshots.Count > 0)
                    {
                        var path = _pendingScreenshots.Dequeue();
                        var pathCopy = path; // capture for closure
                        _pendingFileWrites.Add(Task.Run(() =>
                        {
                            try { File.WriteAllBytes(pathCopy, pngBytes); }
                            catch { }
                        }));
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[TestReport] Screenshot capture failed: {ex.Message}");
                    _pendingScreenshots.Clear();
                }
            }

            void OnDestroy()
            {
                _screenshotCapturer = null;
            }
        }

        /// <summary>
        /// Captures a search-progress screenshot during Find() timeout.
        /// Called periodically (every ~2s) to show what's on screen while searching.
        /// </summary>
        public static void CaptureSearchSnapshot(string searchDescription, float elapsedSeconds)
        {
            if (!_active) return;

            // Add a search_snapshot event before capturing — skip hierarchy to avoid
            // blocking the main thread (can take seconds on complex scenes with 1500+ transforms)
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

            CaptureScreenshot($"search_{elapsedSeconds:F1}s_{SanitizeFileName(searchDescription)}");
        }

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

        // Reusable buffers to avoid per-capture allocations
        static readonly List<Component> _componentBuffer = new();
        static readonly Dictionary<Transform, HierarchyNode> _nodeMap = new();

        /// <summary>
        /// Computes a stable rendering order for a UI element within its canvas.
        /// Uses transform depth (distance from root) as primary order — deeper elements
        /// render on top. Sibling index as tiebreaker. Avoids allocations.
        /// </summary>
        static float GetHierarchyOrder(Transform t)
        {
            // Count depth (distance from root) — deeper = rendered later
            int depth = 0;
            var current = t.parent;
            while (current != null) { depth++; current = current.parent; }
            // Combine: depth dominates, sibling index breaks ties
            return depth * 1000f + t.GetSiblingIndex();
        }

        /// <summary>
        /// Captures a structured hierarchy snapshot in memory.
        /// Uses FindObjectsByType to get ALL transforms in one native call,
        /// then builds a nested tree matching the web viewer format.
        /// Returns the filename key; actual file is written at session end.
        /// </summary>
        static string CaptureHierarchySnapshot()
        {
            if (!_active) return null;
            Profiler.BeginSample("TestReport.CaptureHierarchy");

            try
            {
                _hierarchyCounter++;
                var filename = $"hierarchy_{_hierarchyCounter:D3}.json";

                var snapshot = new HierarchySnapshot
                {
                    screenWidth = Screen.width,
                    screenHeight = Screen.height
                };

                BuildHierarchy(snapshot, detailed: false);

                // Store in memory — written to disk at session end
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

        /// <summary>
        /// Builds a nested hierarchy tree from ALL GameObjects, grouped by scene.
        /// Single native FindObjectsByType call, then assembles parent-child relationships.
        /// Top-level roots are scene header nodes (isScene=true) containing root GameObjects.
        /// Matches Unity's Hierarchy window layout.
        /// </summary>
        static void BuildHierarchy(HierarchySnapshot snapshot, bool detailed = false, int maxDepth = -1)
        {
            _nodeMap.Clear();

            // Single native call — gets ALL Transforms across all scenes + DontDestroyOnLoad
            var allTransforms = UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            // Pass 1: Create a node for every Transform (skip HideInHierarchy objects)
            // Lightweight: name, active, instanceId, text content, screen bounds, depth.
            // No annotations, no properties. Paths are reconstructed by the front end.
            var corners = new Vector3[4];
            var mainCam = Camera.main;

            // Store camera VP matrix for client-side 3D→screen projection
            if (mainCam != null)
            {
                var vp = mainCam.projectionMatrix * mainCam.worldToCameraMatrix;
                snapshot.cameraMatrix = new float[16];
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 4; c++)
                        snapshot.cameraMatrix[r * 4 + c] = vp[r, c];
            }
            for (int i = 0; i < allTransforms.Length; i++)
            {
                var t = allTransforms[i];
                if (t == null) continue;

                var go = t.gameObject;
                if ((go.hideFlags & HideFlags.HideInHierarchy) != 0) continue;

                var node = new HierarchyNode
                {
                    name = t.name,
                    active = go.activeInHierarchy,
                    instanceId = go.GetInstanceID(),
                    childCount = t.childCount,
                    siblingIndex = t.GetSiblingIndex()
                };

                // Extract text content if present (cheap single check)
                if (go.TryGetComponent<TMP_Text>(out var tmp))
                    node.text = tmp.text;
                else if (go.TryGetComponent<Text>(out var legacyText))
                    node.text = legacyText.text;

                // Screen-space bounds for UI elements (RectTransform only)
                if (t is RectTransform rt)
                {
                    rt.GetWorldCorners(corners);
                    var canvas = go.GetComponentInParent<Canvas>();
                    Camera cam = null;
                    if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                        cam = canvas.worldCamera != null ? canvas.worldCamera : mainCam;

                    float minX = float.MaxValue, maxX = float.MinValue;
                    float minY = float.MaxValue, maxY = float.MinValue;
                    for (int c = 0; c < 4; c++)
                    {
                        Vector2 sp = cam != null
                            ? RectTransformUtility.WorldToScreenPoint(cam, corners[c])
                            : (Vector2)corners[c];
                        if (sp.x < minX) minX = sp.x;
                        if (sp.x > maxX) maxX = sp.x;
                        if (sp.y < minY) minY = sp.y;
                        if (sp.y > maxY) maxY = sp.y;
                    }
                    node.x = minX;
                    node.y = minY;
                    node.w = maxX - minX;
                    node.h = maxY - minY;

                    // Depth for UI: negative sort order so higher sort order = lower depth = in front
                    if (canvas != null)
                        node.depth = -(canvas.sortingOrder * 10000f + GetHierarchyOrder(t));
                }
                // World-space bounds for 3D objects — projected client-side using cameraMatrix
                else if (go.TryGetComponent<Renderer>(out var renderer) && renderer.isVisible)
                {
                    var b = renderer.bounds;
                    node.worldBounds = new[] { b.center.x, b.center.y, b.center.z, b.extents.x, b.extents.y, b.extents.z };
                }

                _nodeMap[t] = node;
            }

            // Pass 2: Assemble parent-child relationships, collect roots per scene
            var sceneRoots = new Dictionary<string, List<HierarchyNode>>();
            for (int i = 0; i < allTransforms.Length; i++)
            {
                var t = allTransforms[i];
                if (t == null || !_nodeMap.TryGetValue(t, out var node)) continue;

                if (t.parent != null && _nodeMap.TryGetValue(t.parent, out var parentNode))
                {
                    if (maxDepth >= 0)
                    {
                        int depth = 0;
                        var check = t.parent;
                        while (check != null)
                        {
                            depth++;
                            check = check.parent;
                        }
                        if (depth > maxDepth) continue;
                    }
                    parentNode.children.Add(node);
                }
                else
                {
                    // Root object — group by scene name
                    var scene = t.gameObject.scene;
                    var sceneName = scene.IsValid() ? scene.name : "DontDestroyOnLoad";
                    if (!sceneRoots.TryGetValue(sceneName, out var list))
                    {
                        list = new List<HierarchyNode>();
                        sceneRoots[sceneName] = list;
                    }
                    list.Add(node);
                }
            }

            // Pass 3: Create scene header nodes and add as top-level roots
            foreach (var kvp in sceneRoots)
            {
                var sceneNode = new HierarchyNode
                {
                    name = kvp.Key,
                    active = true,
                    isScene = true,
                    childCount = kvp.Value.Count,
                    children = kvp.Value
                };
                sceneNode.children.Sort((a, b) => a.siblingIndex.CompareTo(b.siblingIndex));
                snapshot.roots.Add(sceneNode);
            }

            // Sort scenes: loaded scenes first, DontDestroyOnLoad last
            snapshot.roots.Sort((a, b) =>
            {
                if (a.name == "DontDestroyOnLoad") return 1;
                if (b.name == "DontDestroyOnLoad") return -1;
                return string.Compare(a.name, b.name, StringComparison.Ordinal);
            });

            // Sort all children recursively by sibling index
            foreach (var sceneNode in snapshot.roots)
                SortChildrenRecursive(sceneNode.children);

            _nodeMap.Clear();
        }

        /// <summary>
        /// Recursively builds hierarchy paths from the assembled tree structure.
        /// Much cheaper than calling Search.GetHierarchyPath per node (which re-walks Transform parents).
        /// </summary>
        static void BuildPaths(List<HierarchyNode> nodes, string parentPath)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                node.path = string.IsNullOrEmpty(parentPath) ? node.name : $"{parentPath}/{node.name}";
                if (node.children.Count > 0)
                    BuildPaths(node.children, node.path);
            }
        }

        static void SortChildrenRecursive(List<HierarchyNode> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var children = nodes[i].children;
                if (children.Count > 1)
                    children.Sort((a, b) => a.siblingIndex.CompareTo(b.siblingIndex));
                if (children.Count > 0)
                    SortChildrenRecursive(children);
            }
        }

        /// <summary>
        /// Captures a detailed hierarchy snapshot with full component properties.
        /// Called explicitly by ActionExecutor.Snapshot() for on-demand diagnostic captures.
        /// Unlike the automatic snapshots, this includes property details on every node.
        /// </summary>
        /// <param name="maxDepth">Maximum tree depth to capture. -1 for unlimited.</param>
        /// <returns>The hierarchy filename key, or null on failure.</returns>
        public static string CaptureDetailedSnapshot(int maxDepth = -1)
        {
            if (!_active) return null;

            try
            {
                _hierarchyCounter++;
                var filename = $"hierarchy_{_hierarchyCounter:D3}.json";

                var snapshot = new HierarchySnapshot
                {
                    screenWidth = Screen.width,
                    screenHeight = Screen.height
                };

                BuildHierarchy(snapshot, detailed: true, maxDepth: maxDepth);

                _pendingHierarchies[filename] = snapshot;

                // Log to session as a snapshot event
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

                    // Add snapshot as a log entry so it appears in the console
                    _session.logs.Add(new LogEntry
                    {
                        logType = 6, // Snapshot
                        message = $"Hierarchy snapshot (depth={depthStr})",
                        stackTrace = filename, // store hierarchy filename for viewer
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
        /// Gets detailed properties for a transform's components using Newtonsoft serialization.
        /// Each component is serialized as "ComponentType: { json }" — no manual property extraction needed.
        /// </summary>
        static List<string> GetDetailedProperties(Transform t)
        {
            t.GetComponents(_componentBuffer);
            if (_componentBuffer.Count <= 1) { _componentBuffer.Clear(); return null; } // only Transform

            var props = new List<string>();

            for (int i = 0; i < _componentBuffer.Count; i++)
            {
                var c = _componentBuffer[i];
                if (c == null || c is Transform) continue;

                try
                {
                    var json = JsonConvert.SerializeObject(c, _componentJsonSettings);
                    props.Add($"{c.GetType().Name}: {json}");
                }
                catch
                {
                    props.Add($"{c.GetType().Name}: {{}}");
                }
            }

            _componentBuffer.Clear();
            return props.Count > 0 ? props : null;
        }

        static readonly JsonSerializerSettings _componentJsonSettings = new()
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            MaxDepth = 2,
            Error = (_, args) => args.ErrorContext.Handled = true,
            Formatting = Formatting.None,
            ContractResolver = new ComponentContractResolver()
        };

        /// <summary>
        /// Custom contract resolver that only serializes value-type and string properties
        /// on Unity components — skips object references (other components, GameObjects, etc.)
        /// to avoid circular references and massive output.
        /// </summary>
        class ComponentContractResolver : Newtonsoft.Json.Serialization.DefaultContractResolver
        {
            protected override IList<Newtonsoft.Json.Serialization.JsonProperty> CreateProperties(
                Type type, Newtonsoft.Json.MemberSerialization memberSerialization)
            {
                var allProps = base.CreateProperties(type, memberSerialization);
                var filtered = new List<Newtonsoft.Json.Serialization.JsonProperty>();

                for (int i = 0; i < allProps.Count; i++)
                {
                    var p = allProps[i];
                    var pt = p.PropertyType;
                    if (pt == null) continue;

                    // Keep: primitives, enums, strings, Vector2/3/4, Color, Rect, Bounds, Quaternion
                    if (pt.IsPrimitive || pt.IsEnum || pt == typeof(string)
                        || pt == typeof(Vector2) || pt == typeof(Vector3) || pt == typeof(Vector4)
                        || pt == typeof(Color) || pt == typeof(Color32)
                        || pt == typeof(Rect) || pt == typeof(Bounds)
                        || pt == typeof(Quaternion)
                        || pt == typeof(Vector2Int) || pt == typeof(Vector3Int))
                    {
                        // Skip deprecated/obsolete properties
                        var prop = type.GetProperty(p.PropertyName,
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (prop != null && System.Attribute.IsDefined(prop, typeof(ObsoleteAttribute)))
                            continue;

                        filtered.Add(p);
                    }
                }

                return filtered;
            }
        }

        /// <summary>
        /// Gets annotation strings for a transform's UI components.
        /// Uses a reusable component buffer to avoid per-call array allocation.
        /// </summary>
        static string[] GetAnnotations(Transform t)
        {
            // Reusable buffer — avoids allocating a new Component[] per node
            t.GetComponents(_componentBuffer);
            if (_componentBuffer.Count <= 1)
            {
                _componentBuffer.Clear();
                return Array.Empty<string>(); // only Transform
            }

            List<string> annotations = null;

            for (int i = 0; i < _componentBuffer.Count; i++)
            {
                var c = _componentBuffer[i];
                if (c == null) continue;

                string annotation = c switch
                {
                    // UI components
                    TMP_Text tmp => $"[TMP] \"{Truncate(tmp.text, 50)}\"",
                    Text text => $"[Text] \"{Truncate(text.text, 50)}\"",
                    Image img when img.sprite != null => $"[Image] sprite={img.sprite.name}",
                    RawImage raw when raw.texture != null => $"[RawImage] texture={raw.texture.name}",
                    Button btn => btn.interactable ? "[Button]" : "[Button:disabled]",
                    Toggle tog => tog.interactable
                        ? $"[Toggle:{(tog.isOn ? "ON" : "OFF")}]"
                        : $"[Toggle:disabled:{(tog.isOn ? "ON" : "OFF")}]",
                    TMP_InputField inp => $"[InputField] \"{Truncate(inp.text, 30)}\"",
                    Slider sld => $"[Slider:{sld.value:F2}]",
                    TMP_Dropdown dd => $"[Dropdown:{dd.captionText?.text ?? "?"}]",
                    ScrollRect _ => "[ScrollRect]",
                    Canvas cvs => $"[Canvas:{cvs.renderMode}]",
                    CanvasGroup cg when !cg.interactable || cg.alpha < 1f =>
                        $"[CanvasGroup:alpha={cg.alpha:F2},interactable={cg.interactable}]",
                    // 3D / scene components
                    Camera cam => $"[Camera:{cam.clearFlags}]",
                    Light light => $"[Light:{light.type}]",
                    MeshRenderer mr => $"[MeshRenderer]",
                    SkinnedMeshRenderer smr => $"[SkinnedMesh]",
                    MeshFilter mf when mf.sharedMesh != null => $"[Mesh:{mf.sharedMesh.name}]",
                    Collider col => $"[{col.GetType().Name}]",
                    Rigidbody rb => rb.isKinematic ? "[Rigidbody:kinematic]" : "[Rigidbody]",
                    Animator anim => $"[Animator]",
                    AudioSource audio => audio.isPlaying ? "[AudioSource:playing]" : "[AudioSource]",
                    ParticleSystem ps => ps.isPlaying ? "[ParticleSystem:playing]" : "[ParticleSystem]",
                    _ => null
                };

                if (annotation != null)
                    (annotations ??= new List<string>()).Add(annotation);
            }

            _componentBuffer.Clear();
            return annotations?.ToArray() ?? Array.Empty<string>();
        }

        /// <summary>
        /// Dumps the full scene hierarchy to a file and returns the content.
        /// Includes component info: text content, sprite names, active/inactive, interactable state.
        /// </summary>
        public static string DumpHierarchy()
        {
            if (!_active) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine($"=== Scene Hierarchy: {SceneManager.GetActiveScene().name} ===");
            sb.AppendLine($"Frame: {Time.frameCount} | Time: {Time.time:F2}s");
            sb.AppendLine();

            var rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var root in rootObjects)
            {
                DumpTransform(root.transform, sb, 0);
            }

            // Also dump DontDestroyOnLoad objects
            var ddolObjects = GetDontDestroyOnLoadObjects();
            if (ddolObjects.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("=== DontDestroyOnLoad ===");
                foreach (var obj in ddolObjects)
                {
                    DumpTransform(obj.transform, sb, 0);
                }
            }

            var content = sb.ToString();

            try
            {
                var path = Path.Combine(_sessionFolder, "hierarchy.txt");
                File.WriteAllText(path, content);
                LogMessage("Hierarchy dumped to hierarchy.txt");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestReport] Failed to write hierarchy: {ex.Message}");
            }

            return content;
        }

        /// <summary>
        /// Builds a failure report for a search failure.
        /// Returns a short message suitable for the assertion exception.
        /// The full diagnostic report is written to FAILURE_REPORT.txt and
        /// stored in the diagnostic session for the viewer.
        /// </summary>
        public static string BuildFailureReport(Search search, float searchTime)
        {
            var shortMsg = $"Element not found within {searchTime}s — Search: {search}";

            if (!_active)
                return shortMsg;

            _failureRecorded = true;
            _lastFailedSessionFolder = _sessionFolder;

            // Take final failure screenshot
            CaptureScreenshot("failure");

            // Dump full hierarchy (text version)
            var hierarchy = DumpHierarchy();

            var sb = new StringBuilder();
            sb.AppendLine($"Element not found within {searchTime}s");
            sb.AppendLine();
            sb.AppendLine("--- DIAGNOSTIC REPORT ---");
            sb.AppendLine($"Search: {search}");
            sb.AppendLine($"Scene: {SceneManager.GetActiveScene().name} (frame {Time.frameCount})");
            sb.AppendLine($"Screen: {Screen.width}x{Screen.height}");
            sb.AppendLine();

            // Show text elements on screen (most useful for debugging)
            AppendVisibleTextElements(sb);

            // Show near-misses — objects that match some but not all conditions
            AppendNearMisses(sb, search);

            // Receiver info if applicable
            if (search.UsesReceiverFilter)
            {
                var receivers = search.Receivers;
                if (receivers != null && receivers.Count > 0)
                    sb.AppendLine($"Receivers at position: {string.Join(", ", receivers.Select(r => r.name))}");
                else
                    sb.AppendLine("No receivers found at element position");
                sb.AppendLine("Tip: Remove .RequiresReceiver() from the search to click without receiver validation");
                sb.AppendLine();
            }

            // Screenshot summary
            sb.AppendLine($"Screenshots captured: {_screenshotPaths.Count} files");
            foreach (var path in _screenshotPaths)
                sb.AppendLine($"  {Path.GetFileName(path)}");
            sb.AppendLine();

            sb.AppendLine($"Diagnostic folder: {_sessionFolder}");

            // Write combined report file
            var report = sb.ToString();
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
            WriteSessionJson();
            WriteLogFile();

            return shortMsg;
        }

        #region Recording

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

        #endregion

        #region Metadata & VCS

        /// <summary>
        /// Builds session metadata with platform defaults and optional VCS detection.
        /// </summary>
        static SessionMetadata BuildSessionMetadata()
        {
            var meta = new SessionMetadata
            {
                runId = RunId,
                appVersion = Application.version,
                platform = Application.platform.ToString(),
                machineName = Environment.MachineName,
                project = Application.productName
            };

#if UNITY_EDITOR
            if (AutoDetectVCS)
            {
                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                if (Directory.Exists(Path.Combine(projectRoot, ".git")))
                    DetectGit(meta, projectRoot);
                else if (Directory.Exists(Path.Combine(projectRoot, ".plastic")))
                    DetectPlastic(meta, projectRoot);
            }
#endif

            if (MetadataCallback != null)
            {
                try { meta = MetadataCallback(meta) ?? meta; }
                catch (Exception ex) { Debug.LogWarning($"[TestReport] MetadataCallback error: {ex.Message}"); }
            }

            return meta;
        }

#if UNITY_EDITOR
        static void DetectGit(SessionMetadata meta, string workingDir)
        {
            try
            {
                meta.branch = RunProcess("git", "rev-parse --abbrev-ref HEAD", workingDir)?.Trim();
                meta.commit = RunProcess("git", "rev-parse --short HEAD", workingDir)?.Trim();
            }
            catch { /* Git not available — leave fields null */ }
        }

        static void DetectPlastic(SessionMetadata meta, string workingDir)
        {
            try
            {
                // Try reading workspace selector file for branch info
                var selectorPath = Path.Combine(workingDir, ".plastic", "plastic.selector");
                if (File.Exists(selectorPath))
                {
                    var lines = File.ReadAllLines(selectorPath);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("rep \"") || trimmed.StartsWith("repository \""))
                        {
                            // Extract repo name
                            var start = trimmed.IndexOf('"') + 1;
                            var end = trimmed.IndexOf('"', start);
                            if (end > start)
                                meta.project = trimmed.Substring(start, end - start);
                        }
                        else if (trimmed.StartsWith("path \"/") || trimmed.StartsWith("smartbranch \"") || trimmed.StartsWith("branch \""))
                        {
                            var start = trimmed.IndexOf('"') + 1;
                            var end = trimmed.IndexOf('"', start);
                            if (end > start)
                                meta.branch = trimmed.Substring(start, end - start);
                        }
                    }
                }

                // Try cm for changeset
                var csInfo = RunProcess("cm", "status --head --machinereadable", workingDir);
                if (!string.IsNullOrEmpty(csInfo))
                    meta.commit = csInfo.Trim().Split('\n')[0].Trim();
            }
            catch { /* Plastic not available — leave fields as-is */ }
        }

        static string RunProcess(string fileName, string arguments, string workingDir)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return null;

                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);
                return proc.ExitCode == 0 ? output : null;
            }
            catch
            {
                return null;
            }
        }
#endif

        /// <summary>
        /// Uploads a diagnostic session zip to a remote server via HTTP multipart POST.
        /// </summary>
        /// <param name="zipPath">Path to the zip file.</param>
        /// <param name="serverUrl">Base URL of the server, e.g. "http://localhost:5000".</param>
        /// <param name="apiKey">Optional API key for authenticated servers.</param>
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
                // Don't 'using' MultipartFormDataContent separately — HttpRequestMessage
                // takes ownership and disposes the content. Double-dispose crashes on Mono.
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

        #endregion

        #region Private helpers

        /// <summary>
        /// Recursively copies a directory with depth and size limits to prevent bloated zips.
        /// Skips files that would exceed maxTotalBytes.
        /// </summary>
        static void CopyDirectory(string sourceDir, string destDir, int maxDepth, long maxTotalBytes)
        {
            long totalBytes = 0;
            CopyDirectoryRecursive(sourceDir, destDir, 0, maxDepth, ref totalBytes, maxTotalBytes);
        }

        static void CopyDirectoryRecursive(string sourceDir, string destDir, int depth, int maxDepth, ref long totalBytes, long maxTotalBytes)
        {
            if (depth > maxDepth || totalBytes >= maxTotalBytes) return;

            Directory.CreateDirectory(destDir);

            try
            {
                foreach (var file in Directory.GetFiles(sourceDir))
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (totalBytes + fileInfo.Length > maxTotalBytes) continue;
                        var destFile = Path.Combine(destDir, fileInfo.Name);
                        File.Copy(file, destFile, true);
                        totalBytes += fileInfo.Length;
                    }
                    catch { } // Skip locked/inaccessible files
                }

                foreach (var subDir in Directory.GetDirectories(sourceDir))
                {
                    var dirName = Path.GetFileName(subDir);
                    CopyDirectoryRecursive(subDir, Path.Combine(destDir, dirName), depth + 1, maxDepth, ref totalBytes, maxTotalBytes);
                }
            }
            catch { } // Skip inaccessible directories
        }

        /// <summary>
        /// Packages the session folder into a zip archive and deletes the loose folder.
        /// Returns the zip path, or null on failure.
        /// </summary>
        static string PackSessionToZip()
        {
            if (_sessionFolder == null || !Directory.Exists(_sessionFolder))
                return null;

            var zipPath = _sessionFolder + ".zip";
            try
            {
                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                ZipFile.CreateFromDirectory(_sessionFolder, zipPath, System.IO.Compression.CompressionLevel.NoCompression, false);
                Directory.Delete(_sessionFolder, true);
                LogMessage($"Session packaged: {Path.GetFileName(zipPath)}");
                return zipPath;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestReport] Failed to package session zip: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Writes all in-memory hierarchy snapshots to disk.
        /// Called at session end, before zipping.
        /// Uses manual JSON serialization to avoid JsonUtility's 10-level depth limit.
        /// </summary>
        static void FlushHierarchiesToDisk()
        {
            if (_sessionFolder == null || _pendingHierarchies.Count == 0) return;

            foreach (var kvp in _pendingHierarchies)
            {
                try
                {
                    var path = Path.Combine(_sessionFolder, kvp.Key);
                    var json = JsonConvert.SerializeObject(kvp.Value, _hierarchyJsonSettings);
                    File.WriteAllText(path, json);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[TestReport] Failed to write hierarchy {kvp.Key}: {ex.Message}");
                }
            }

            _pendingHierarchies.Clear();
        }

        static readonly JsonSerializerSettings _hierarchyJsonSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None
        };

        static void LogMessage(string message)
        {
            var entry = $"[{DateTime.Now:HH:mm:ss.fff}] [frame {Time.frameCount}] {message}";
            _log.Add(entry);
        }

        /// <summary>
        /// Captures all Unity console output (Debug.Log/LogWarning/LogError/exceptions)
        /// into the session's structured log list.
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

        static void WriteLogFile()
        {
            if (!_active || _sessionFolder == null) return;
            try
            {
                var path = Path.Combine(_sessionFolder, "log.txt");
                File.WriteAllText(path, string.Join("\n", _log));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestReport] Failed to write log: {ex.Message}");
            }
        }

        static void WriteSessionJson()
        {
            if (!_active || _sessionFolder == null || _session == null) return;
            try
            {
                var path = Path.Combine(_sessionFolder, "session.json");
                var json = JsonUtility.ToJson(_session, true);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestReport] Failed to write session.json: {ex.Message}");
            }
        }

        static void DumpTransform(Transform t, StringBuilder sb, int depth)
        {
            var indent = new string(' ', depth * 2);
            var activeFlag = t.gameObject.activeInHierarchy ? "" : " [INACTIVE]";
            var annotations = GetAnnotations(t);
            var annotationStr = annotations.Length > 0 ? " " + string.Join(" ", annotations) : "";

            sb.AppendLine($"{indent}{t.name}{activeFlag}{annotationStr}");

            // Recurse children
            for (int i = 0; i < t.childCount; i++)
            {
                DumpTransform(t.GetChild(i), sb, depth + 1);
            }
        }

        static void AppendVisibleTextElements(StringBuilder sb)
        {
            sb.AppendLine("Visible text elements on screen:");

            var screenRect = new Rect(0, 0, Screen.width, Screen.height);
            var textElements = UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var visibleTexts = new List<(string path, string text, Vector2 pos)>();

            foreach (var tmp in textElements)
            {
                if (string.IsNullOrEmpty(tmp.text)) continue;
                var bounds = InputInjector.GetScreenBounds(tmp.gameObject);
                if (bounds.width > 0 && bounds.height > 0 && bounds.Overlaps(screenRect))
                {
                    var path = Search.GetHierarchyPath(tmp.transform);
                    var pos = InputInjector.GetScreenPosition(tmp.gameObject);
                    visibleTexts.Add((path, tmp.text, pos));
                }
            }

            // Also check legacy Text
            var legacyTexts = UnityEngine.Object.FindObjectsByType<Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var text in legacyTexts)
            {
                if (string.IsNullOrEmpty(text.text)) continue;
                var bounds = InputInjector.GetScreenBounds(text.gameObject);
                if (bounds.width > 0 && bounds.height > 0 && bounds.Overlaps(screenRect))
                {
                    var path = Search.GetHierarchyPath(text.transform);
                    var pos = InputInjector.GetScreenPosition(text.gameObject);
                    visibleTexts.Add((path, text.text, pos));
                }
            }

            if (visibleTexts.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                foreach (var (path, text, pos) in visibleTexts.OrderBy(t => -t.pos.y).ThenBy(t => t.pos.x))
                {
                    sb.AppendLine($"  {path} \"{Truncate(text, 60)}\" at ({pos.x:F0},{pos.y:F0})");
                }
            }
            sb.AppendLine();
        }

        static void AppendNearMisses(StringBuilder sb, Search search)
        {
            var conditions = search.Conditions;
            var descriptions = search.DescriptionParts;
            if (conditions == null || conditions.Count == 0) return;

            sb.AppendLine("Near-misses (matched some conditions but not all):");

            var allObjects = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var nearMisses = new List<(GameObject go, int matched, int total, string failedConditions)>();

            foreach (var t in allObjects)
            {
                if (t == null) continue;
                var go = t.gameObject;

                int matched = 0;
                var failedDescs = new List<string>();

                for (int i = 0; i < conditions.Count; i++)
                {
                    try
                    {
                        if (conditions[i](go))
                            matched++;
                        else if (i < descriptions.Count)
                            failedDescs.Add(descriptions[i]);
                    }
                    catch
                    {
                        if (i < descriptions.Count)
                            failedDescs.Add(descriptions[i]);
                    }
                }

                // Near-miss: matched at least 1 condition but not all
                if (matched > 0 && matched < conditions.Count)
                {
                    var failedStr = string.Join(", ", failedDescs);
                    nearMisses.Add((go, matched, conditions.Count, failedStr));
                }
            }

            if (nearMisses.Count == 0)
            {
                sb.AppendLine("  (none — no objects matched any condition)");
            }
            else
            {
                // Show top 20 near-misses, sorted by most conditions matched
                foreach (var (go, matched, total, failed) in nearMisses
                    .OrderByDescending(n => n.matched)
                    .Take(20))
                {
                    var path = Search.GetHierarchyPath(go.transform);
                    var activeStr = go.activeInHierarchy ? "" : " [INACTIVE]";
                    sb.AppendLine($"  {path}{activeStr} — matched {matched}/{total}, failed: {failed}");
                }

                if (nearMisses.Count > 20)
                    sb.AppendLine($"  ... and {nearMisses.Count - 20} more");
            }
            sb.AppendLine();
        }

        static List<GameObject> GetDontDestroyOnLoadObjects()
        {
            var results = new List<GameObject>();
            try
            {
                // Find objects not in the active scene
                var activeScene = SceneManager.GetActiveScene();
                var allRoots = new List<GameObject>();
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene != activeScene && scene.isLoaded)
                    {
                        allRoots.AddRange(scene.GetRootGameObjects());
                    }
                }
                return allRoots;
            }
            catch
            {
                return results;
            }
        }

        static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return sb.ToString();
        }

        static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\n", "\\n").Replace("\r", "");
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }

        #endregion
    }
}
#endif
