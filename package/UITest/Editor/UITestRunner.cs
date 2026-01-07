using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
#if UNITY_RECORDER
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
#endif
using UnityEditor.SceneManagement;
using UnityEngine;
using ODDGames.UITest;

public class UITestRunner
{
#if UNITY_RECORDER
    private static RecorderControllerSettings recorderControllerSettings;
    private static RecorderController recorderController;
#endif
    private static List<string> capturedLogs = new List<string>();
    private static string currentVideoPath;
    private static Queue<UITestInfo> pendingTests;
    private static bool hasFailures;
    private static DateTime testStartTime;
    private static int currentTestTimeout;
    private static DateTime playModeTransitionStartTime;
    private static bool isWaitingForPlayMode;
    private static System.Threading.CancellationTokenSource timeoutCancellation;

    [MenuItem("Tools/UI Tests/Run All Tests in Batch Mode")]
    public static void RunAllTestsInBatchMode()
    {
        string outputDir = EditorUtility.SaveFolderPanel("Select Output Directory", "", "UITestOutput");
        if (string.IsNullOrEmpty(outputDir))
            return;

        RunUITestsFromCommandLine();
    }

    public static void RunUITestsFromCommandLine()
    {
        Debug.Log("[UITestRunner] ===== Starting UI Test Runner =====");
        Debug.Log($"[UITestRunner] Unity Version: {Application.unityVersion}");
        Debug.Log($"[UITestRunner] Platform: {Application.platform}");
        Debug.Log($"[UITestRunner] Is Batch Mode: {Application.isBatchMode}");
        Debug.Log($"[UITestRunner] Graphics Device Type: {UnityEngine.SystemInfo.graphicsDeviceType}");
        Debug.Log($"[UITestRunner] Graphics Device Name: {UnityEngine.SystemInfo.graphicsDeviceName}");
        Debug.Log($"[UITestRunner] Command Line: {string.Join(" ", System.Environment.GetCommandLineArgs())}");

        if (Application.isBatchMode && UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            Debug.LogWarning("[UITestRunner] WARNING: Running in batch mode with no graphics device (-nographics flag detected)");
            Debug.LogWarning("[UITestRunner] Play mode may not work correctly without graphics device. Consider removing -nographics flag.");
        }

        string outputDir = GetArgument("-outputDir");
        if (string.IsNullOrEmpty(outputDir))
        {
            outputDir = Path.Combine(Application.dataPath, "..", "TestResults");
        }
        Debug.Log($"[UITestRunner] Output Directory: {outputDir}");

        Directory.CreateDirectory(outputDir);

        var tests = FindAllUITestBehaviours();
        ValidateScenarios(tests);

        int specificScenario = GetIntArgument("-scenario", -1);
        if (specificScenario > 0)
        {
            tests = tests.Where(t => t.Scenario == specificScenario).ToList();
        }

        Debug.Log($"[UITestRunner] Found {tests.Count} UI test(s) to run");

        Application.logMessageReceived += CaptureLog;
        EditorApplication.update += CheckTimeout;

        SessionState.SetBool("GAME_LOOP_TEST_RUNNER", true);

        hasFailures = false;
        pendingTests = new Queue<UITestInfo>(tests);

        timeoutCancellation = new System.Threading.CancellationTokenSource();
        StartBackgroundTimeoutMonitor();

        RunNextTest();
    }

    private static void StartBackgroundTimeoutMonitor()
    {
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                var cancellation = timeoutCancellation;
                if (cancellation == null) return;

                while (!cancellation.Token.IsCancellationRequested)
                {
                    await System.Threading.Tasks.Task.Delay(1000, cancellation.Token);

                    if (isWaitingForPlayMode)
                    {
                        double elapsed = (DateTime.Now - playModeTransitionStartTime).TotalSeconds;
                        if (elapsed > 30)
                        {
                            Debug.LogError($"[UITestRunner] BACKGROUND MONITOR: Play mode transition timeout after {elapsed:F1}s - Unity may be frozen");
                            Debug.LogError($"[UITestRunner] BACKGROUND MONITOR: Forcing editor exit due to main thread freeze");

                            System.Environment.Exit(1);
                        }
                    }

                    if (currentTestTimeout > 0)
                    {
                        double elapsed = (DateTime.Now - testStartTime).TotalSeconds;
                        if (elapsed > currentTestTimeout + 10)
                        {
                            Debug.LogError($"[UITestRunner] BACKGROUND MONITOR: Test timeout after {elapsed:F1}s - Unity may be frozen");
                            Debug.LogError($"[UITestRunner] BACKGROUND MONITOR: Forcing editor exit due to main thread freeze");

                            System.Environment.Exit(1);
                        }
                    }
                }
            }
            catch (System.OperationCanceledException)
            {
            }
            catch (System.ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UITestRunner] Background monitor error: {ex}");
            }
        });
    }

    private static void CheckTimeout()
    {
        if (isWaitingForPlayMode)
        {
            double elapsed = (DateTime.Now - playModeTransitionStartTime).TotalSeconds;
            if (elapsed > 30)
            {
                hasFailures = true;
                Debug.LogError($"[UITestRunner] ===== PLAY MODE TRANSITION TIMEOUT =====");
                Debug.LogError($"[UITestRunner] Failed to enter Play Mode after {elapsed:F1} seconds");
                Debug.LogError($"[UITestRunner] This usually indicates compilation errors or corrupt caches");
                Debug.LogError($"[UITestRunner] Try: 1) Clear Burst cache, 2) Check for compilation errors, 3) Disable domain reload");
                Debug.Log($"[UITestRunner] isPlaying: {EditorApplication.isPlaying}, isPlayingOrWillChangePlaymode: {EditorApplication.isPlayingOrWillChangePlaymode}");

                StopVideoRecording();
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

                currentVideoPath = null;
                currentTestTimeout = 0;
                isWaitingForPlayMode = false;

                EditorApplication.delayCall += () =>
                {
                    Debug.Log("[UITestRunner] Transition timeout delayCall executing - moving to next test");
                    RunNextTest();
                };
                return;
            }
        }

        if (currentTestTimeout > 0)
        {
            double elapsed = (DateTime.Now - testStartTime).TotalSeconds;
            if (elapsed > currentTestTimeout)
            {
                hasFailures = true;
                Debug.LogError($"[UITestRunner] ===== TEST TIMEOUT =====");
                Debug.LogError($"[UITestRunner] Test timeout after {elapsed:F1} seconds (limit: {currentTestTimeout})");
                Debug.Log($"[UITestRunner] isPlaying: {EditorApplication.isPlaying}, pendingTests: {pendingTests?.Count ?? 0}");

                StopVideoRecording();

                if (EditorApplication.isPlaying)
                {
                    Debug.Log("[UITestRunner] Stopping Play mode due to timeout");
                    EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                    EditorApplication.isPlaying = false;
                }

                currentVideoPath = null;
                currentTestTimeout = 0;
                isWaitingForPlayMode = false;

                EditorApplication.delayCall += () =>
                {
                    Debug.Log("[UITestRunner] Timeout delayCall executing - moving to next test");
                    RunNextTest();
                };
            }
        }
    }

    private static void RunNextTest()
    {
        Debug.Log($"[UITestRunner] RunNextTest called. Pending: {pendingTests?.Count ?? 0}");

        if (pendingTests == null || pendingTests.Count == 0)
        {
            Debug.Log("[UITestRunner] No more tests to run - cleaning up");
            Application.logMessageReceived -= CaptureLog;
            EditorApplication.update -= CheckTimeout;

            if (timeoutCancellation != null)
            {
                timeoutCancellation.Cancel();
                timeoutCancellation.Dispose();
                timeoutCancellation = null;
            }

            SessionState.EraseBool("GAME_LOOP_TEST_RUNNER");
            SessionState.EraseBool("GAME_LOOP_TEST");
            SessionState.EraseInt("GAME_LOOP_TEST_SCENARIO");
            SessionState.EraseString("GAME_LOOP_TEST_TYPE");

            Debug.Log($"[UITestRunner] All tests completed. Failures: {hasFailures}, BatchMode: {Application.isBatchMode}");

            if (Application.isBatchMode)
            {
                Debug.Log($"[UITestRunner] Exiting editor with code: {(hasFailures ? 1 : 0)}");
                EditorApplication.Exit(hasFailures ? 1 : 0);
            }
            else
            {
                Debug.Log("[UITestRunner] Not in batch mode - staying in editor");
            }
            return;
        }

        var testInfo = pendingTests.Dequeue();
        Debug.Log($"[UITestRunner] Running next test. Remaining: {pendingTests.Count}");
        RunSingleTest(testInfo);
    }

    private static void RunSingleTest(UITestInfo testInfo)
    {
        Debug.Log($"[UITestRunner] Starting: {testInfo.Name} (Scenario {testInfo.Scenario})");

        capturedLogs.Clear();

        var attr = testInfo.TestType.GetCustomAttribute<UITestAttribute>();
        currentTestTimeout = attr != null ? attr.TimeoutSeconds : 180;
        testStartTime = DateTime.Now;

        Debug.Log($"[UITestRunner] Test timeout set to {currentTestTimeout} seconds");

        try
        {
            Debug.Log("[UITestRunner] Setting up video path");
            currentVideoPath = Path.Combine(Application.temporaryCachePath, $"{testInfo.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
            Debug.Log($"[UITestRunner] Video path: {currentVideoPath}");

            Debug.Log("[UITestRunner] Setting up video recording");
            SetupVideoRecording(currentVideoPath);
            Debug.Log("[UITestRunner] Video recording setup complete");

            Debug.Log("[UITestRunner] Getting main scene path");
            string mainScene = GetMainScenePath();
            Debug.Log($"[UITestRunner] Main scene: {mainScene ?? "null"}");

            if (!string.IsNullOrEmpty(mainScene))
            {
                Debug.Log($"[UITestRunner] Opening scene: {mainScene}");
                EditorSceneManager.OpenScene(mainScene);
                Debug.Log("[UITestRunner] Scene opened successfully");
            }

            Debug.Log("[UITestRunner] Setting SessionState BEFORE creating GameObject");
            SessionState.SetBool("GAME_LOOP_TEST", true);
            SessionState.SetInt("GAME_LOOP_TEST_SCENARIO", testInfo.Scenario);
            SessionState.SetString("GAME_LOOP_TEST_TYPE", testInfo.TestType.AssemblyQualifiedName);
            Debug.Log($"[UITestRunner] SessionState set - GAME_LOOP_TEST=true, GAME_LOOP_TEST_SCENARIO={testInfo.Scenario}, Type={testInfo.TestType.Name}");

            Debug.Log("[UITestRunner] Registering playModeStateChanged callback");
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            Debug.Log("[UITestRunner] Callback registered");

            Debug.Log($"[UITestRunner] Current state - isPlaying: {EditorApplication.isPlaying}, isPlayingOrWillChangePlaymode: {EditorApplication.isPlayingOrWillChangePlaymode}");
            Debug.Log("[UITestRunner] Calling EditorApplication.EnterPlaymode()");

            isWaitingForPlayMode = true;
            playModeTransitionStartTime = DateTime.Now;

            EditorApplication.EnterPlaymode();
            Debug.Log("[UITestRunner] EditorApplication.EnterPlaymode() returned - waiting for Play Mode transition");
        }
        catch (Exception ex)
        {
            hasFailures = true;
            Debug.LogError($"[UITestRunner] Test setup failed: {ex}");
            Debug.LogError($"[UITestRunner] Stack trace: {ex.StackTrace}");

            currentVideoPath = null;
            currentTestTimeout = 0;
            isWaitingForPlayMode = false;

            RunNextTest();
        }
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        Debug.Log($"[UITestRunner] ===== PlayModeStateChanged: {state} =====");
        Debug.Log($"[UITestRunner] State details - pendingTests: {pendingTests?.Count ?? 0}, currentTestTimeout: {currentTestTimeout}");
        Debug.Log($"[UITestRunner] Editor state - isPlaying: {EditorApplication.isPlaying}, isPlayingOrWillChangePlaymode: {EditorApplication.isPlayingOrWillChangePlaymode}");

        switch (state)
        {
            case PlayModeStateChange.ExitingEditMode:
                Debug.Log("[UITestRunner] Exiting Edit Mode - transitioning to Play mode");
                break;
            case PlayModeStateChange.EnteredPlayMode:
                Debug.Log("[UITestRunner] *** Entered Play Mode *** - creating test GameObject");
                isWaitingForPlayMode = false;

                if (SessionState.GetBool("GAME_LOOP_TEST", false))
                {
                    string typeName = SessionState.GetString("GAME_LOOP_TEST_TYPE", "");
                    if (!string.IsNullOrEmpty(typeName))
                    {
                        Debug.Log($"[UITestRunner] Creating test GameObject with type: {typeName}");
                        Type testType = Type.GetType(typeName);
                        if (testType != null)
                        {
                            GameObject testGO = new GameObject($"UITest_{testType.Name}");
                            testGO.AddComponent(testType);
                            Debug.Log($"[UITestRunner] Test GameObject created in Play mode");
                        }
                        else
                        {
                            Debug.LogError($"[UITestRunner] Failed to find type: {typeName}");
                        }
                    }
                }

                StartVideoRecording();
                break;
            case PlayModeStateChange.ExitingPlayMode:
                Debug.Log("[UITestRunner] Exiting Play Mode - stopping recording");
                StopVideoRecording();
                break;
            case PlayModeStateChange.EnteredEditMode:
                Debug.Log("[UITestRunner] Entered Edit Mode - checking if test is active");
                if (currentTestTimeout > 0)
                {
                    Debug.Log("[UITestRunner] Test is active - finalizing test");
                    FinalizeTest();
                }
                else
                {
                    Debug.Log("[UITestRunner] No active test");
                    if (pendingTests != null && pendingTests.Count == 0)
                    {
                        Debug.Log("[UITestRunner] All tests completed in EnteredEditMode handler");
                        Application.logMessageReceived -= CaptureLog;
                        EditorApplication.update -= CheckTimeout;

                        if (timeoutCancellation != null)
                        {
                            timeoutCancellation.Cancel();
                            timeoutCancellation.Dispose();
                            timeoutCancellation = null;
                        }

                        SessionState.EraseBool("GAME_LOOP_TEST_RUNNER");
                        SessionState.EraseBool("GAME_LOOP_TEST");
                        SessionState.EraseInt("GAME_LOOP_TEST_SCENARIO");
                        SessionState.EraseString("GAME_LOOP_TEST_TYPE");

                        if (Application.isBatchMode)
                        {
                            Debug.Log($"[UITestRunner] Exiting editor with code: {(hasFailures ? 1 : 0)}");
                            EditorApplication.Exit(hasFailures ? 1 : 0);
                        }
                    }
                }
                break;
        }
        Debug.Log($"[UITestRunner] ===== PlayModeStateChanged handler complete =====");
    }

    private static void FinalizeTest()
    {
        Debug.Log("[UITestRunner] FinalizeTest started");
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

        try
        {
            Debug.Log($"[UITestRunner] Attaching test artifacts. Video exists: {File.Exists(currentVideoPath)}, Logs count: {capturedLogs.Count}");

            if (File.Exists(currentVideoPath))
            {
                Debug.Log($"[UITestRunner] Video recorded: {currentVideoPath}");
            }

            string logsText = string.Join("\n", capturedLogs);
            Debug.Log($"[UITestRunner] Logs captured: {capturedLogs.Count} entries");
        }
        catch (Exception ex)
        {
            hasFailures = true;
            Debug.LogError($"[UITestRunner] Test finalization failed: {ex}");
        }
        finally
        {
            Debug.Log("[UITestRunner] Cleaning up and moving to next test");
            currentVideoPath = null;
            currentTestTimeout = 0;

            RunNextTest();
        }
    }

    private static void ValidateScenarios(List<UITestInfo> tests)
    {
        foreach (var test in tests)
        {
            if (test.Scenario <= 0)
                throw new InvalidOperationException($"Test {test.Name} has invalid Scenario {test.Scenario}. Must be > 0.");
        }

        var duplicates = tests.GroupBy(t => t.Scenario).Where(g => g.Count() > 1);
        if (duplicates.Any())
        {
            var dup = duplicates.First();
            throw new InvalidOperationException(
                $"Duplicate Scenario {dup.Key}: {string.Join(", ", dup.Select(t => t.Name))}"
            );
        }
    }

    private static void CaptureLog(string logString, string stackTrace, LogType type)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        string logEntry = $"[{timestamp}] {type}: {logString}";

        if (!string.IsNullOrEmpty(stackTrace) && (type == LogType.Exception || type == LogType.Error))
        {
            logEntry += $"\n{stackTrace}";
        }

        capturedLogs.Add(logEntry);
    }

    private static void SetupVideoRecording(string outputPath)
    {
#if UNITY_RECORDER
        if (Application.isBatchMode)
        {
            Debug.Log("[UITestRunner] Batch mode detected - using camera-based recording");
            recorderControllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();

            var videoRecorder = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            videoRecorder.name = "UI Test Recording";
            videoRecorder.Enabled = true;

            videoRecorder.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
            videoRecorder.OutputFile = outputPath;

            videoRecorder.ImageInputSettings = new CameraInputSettings
            {
                OutputWidth = 1920,
                OutputHeight = 1080,
                Source = ImageSource.MainCamera
            };

            videoRecorder.AudioInputSettings.PreserveAudio = false;

            videoRecorder.FrameRate = 30;
            videoRecorder.FrameRatePlayback = FrameRatePlayback.Constant;
            videoRecorder.CapFrameRate = true;

            recorderControllerSettings.AddRecorderSettings(videoRecorder);
            recorderControllerSettings.SetRecordModeToManual();

            recorderController = new RecorderController(recorderControllerSettings);
        }
        else
        {
            recorderControllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();

            var videoRecorder = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            videoRecorder.name = "UI Test Recording";
            videoRecorder.Enabled = true;

            videoRecorder.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
            videoRecorder.OutputFile = outputPath;

            videoRecorder.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = 1920,
                OutputHeight = 1080
            };

            videoRecorder.AudioInputSettings.PreserveAudio = false;

            videoRecorder.FrameRate = 30;
            videoRecorder.FrameRatePlayback = FrameRatePlayback.Constant;
            videoRecorder.CapFrameRate = true;

            recorderControllerSettings.AddRecorderSettings(videoRecorder);
            recorderControllerSettings.SetRecordModeToManual();

            recorderController = new RecorderController(recorderControllerSettings);
        }
#else
        Debug.LogWarning("[UITestRunner] Unity Recorder package not installed - video recording disabled");
#endif
    }

    private static void StartVideoRecording()
    {
#if UNITY_RECORDER
        if (recorderController != null && recorderController.StartRecording())
        {
            Debug.Log("[UITestRunner] Video recording started");
        }
        else
        {
            Debug.LogWarning("[UITestRunner] Failed to start video recording");
        }
#endif
    }

    private static void StopVideoRecording()
    {
#if UNITY_RECORDER
        if (recorderController != null && recorderController.IsRecording())
        {
            recorderController.StopRecording();
            Debug.Log("[UITestRunner] Video recording stopped");
        }
#endif
    }

    private static List<UITestInfo> FindAllUITestBehaviours()
    {
        var testInfos = new List<UITestInfo>();

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            try
            {
                var types = assembly.GetTypes();
                foreach (var type in types)
                {
                    if (type.IsAbstract || !type.IsSubclassOf(typeof(UITestBehaviour)))
                        continue;

                    var attr = type.GetCustomAttribute<UITestAttribute>();
                    if (attr != null && attr.Scenario > 0)
                    {
                        testInfos.Add(new UITestInfo
                        {
                            Name = type.Name,
                            TestType = type,
                            Scenario = attr.Scenario
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to process assembly {assembly.FullName}: {ex.Message}");
            }
        }

        return testInfos.OrderBy(t => t.Scenario).ToList();
    }

    private static string GetMainScenePath()
    {
        var scenes = EditorBuildSettings.scenes;
        if (scenes.Length > 0)
        {
            return scenes[0].path;
        }
        return null;
    }

    private static string GetArgument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == name && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static int GetIntArgument(string name, int defaultValue)
    {
        string value = GetArgument(name);
        if (int.TryParse(value, out int result))
        {
            return result;
        }
        return defaultValue;
    }

    private class UITestInfo
    {
        public string Name { get; set; }
        public Type TestType { get; set; }
        public int Scenario { get; set; }
    }
}
