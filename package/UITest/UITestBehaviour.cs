using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ODDGames.UITest
{
    public abstract class UITestBehaviour : MonoBehaviour
    {
        #region Inlined Utilities

        sealed class TimeYielder
        {
            private readonly long m_yieldThreshold;
            private long m_lastYield;

            public bool WantsToYield => Environment.TickCount - m_lastYield > m_yieldThreshold;

            public TimeYielder(TimeSpan threshold)
            {
                m_yieldThreshold = (long)threshold.TotalMilliseconds;
                m_lastYield = Environment.TickCount;
            }

            public async UniTask<bool> Yield(PlayerLoopTiming timing = PlayerLoopTiming.Update)
            {
                await UniTask.Yield(timing);
                m_lastYield = Environment.TickCount;
                return true;
            }

            public UniTask<bool> YieldOptional(PlayerLoopTiming timing = PlayerLoopTiming.Update)
            {
                return WantsToYield ? Yield(timing) : UniTask.FromResult(false);
            }
        }

        static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            var path = new StringBuilder();
            while (transform != null)
            {
                if (path.Length > 0)
                    path.Insert(0, "/");
                path.Insert(0, transform.name);
                transform = transform.parent;
            }
            return "/" + path.ToString();
        }

        static bool TryGetComponentInChildren<T>(GameObject obj, out T component) where T : class
        {
            if (obj == null)
            {
                component = default;
                return false;
            }
            component = obj.GetComponentInChildren<T>();
            return component != null;
        }

        #endregion

        public class TestException : Exception
        {
            public TestException(string message) : base(message) { }
        }

        [Flags]
        public enum Availability
        {
            None = 0,
            Active = 1,
            Enabled = 2,
            All = Active | Enabled
        }

        /// <summary>
        /// Direction for swipe gestures.
        /// </summary>
        public enum SwipeDirection
        {
            Left,
            Right,
            Up,
            Down
        }

        public static int Interval { get; set; } = 100;
        static readonly List<Type> clickablesList = new() { typeof(Selectable) };
        static Type[] clickablesArray = { typeof(Selectable) };
        public static Type[] Clickables => clickablesArray;

        static CancellationTokenSource testCts;
        protected static CancellationToken TestCancellationToken => testCts != null ? testCts.Token : CancellationToken.None;

        public static void RegisterClickable(Type type)
        {
            if (type != null && !clickablesList.Contains(type))
            {
                clickablesList.Add(type);
                clickablesArray = clickablesList.ToArray();
            }
        }

        public static void RegisterClickable<T>() => RegisterClickable(typeof(T));

        public static void UnregisterClickable(Type type)
        {
            if (clickablesList.Remove(type))
                clickablesArray = clickablesList.ToArray();
        }

        public static void UnregisterClickable<T>() => UnregisterClickable(typeof(T));

        /// <summary>
        /// Delegate for custom key press handlers (e.g., EzGUI).
        /// Returns true if the key was handled, false to fall back to Unity UI handling.
        /// </summary>
        public delegate bool KeyPressHandler(GameObject target, KeyCode key);

        static readonly List<KeyPressHandler> keyPressHandlers = new();

        /// <summary>
        /// Register a custom key press handler (e.g., for EzGUI support).
        /// Handlers are tried in order until one returns true.
        /// </summary>
        public static void RegisterKeyPressHandler(KeyPressHandler handler)
        {
            if (handler != null && !keyPressHandlers.Contains(handler))
                keyPressHandlers.Add(handler);
        }

        /// <summary>
        /// Unregister a custom key press handler.
        /// </summary>
        public static void UnregisterKeyPressHandler(KeyPressHandler handler)
        {
            keyPressHandlers.Remove(handler);
        }

        public static void StopTest()
        {
            if (testCts != null)
                testCts.Cancel();
        }

        protected virtual void OnDestroy()
        {
            if (testCts != null && Scenario == testScenario)
            {
                testCts.Cancel();
                testCts.Dispose();
                testCts = null;
            }
        }

        static float lastActionTime;

        /// <summary>
        /// Called at the end of each action to wait for the interval before continuing.
        /// </summary>
        static async UniTask ActionComplete()
        {
            lastActionTime = Time.realtimeSinceStartup;

            // Always yield at least one frame to allow Unity to process events
            await UniTask.Yield(PlayerLoopTiming.Update, TestCancellationToken);

            // Then wait for the remaining interval time
            await UniTask.Delay(Interval, true, PlayerLoopTiming.Update, TestCancellationToken);
        }

        /// <summary>
        /// Waits until the editor is fully in play mode and ready for testing.
        /// </summary>
        static async UniTask WaitForPlayModeReady()
        {
#if UNITY_EDITOR
            // Wait until EditorApplication.isPlaying is true and isPlayingOrWillChangePlaymode settles
            while (!UnityEditor.EditorApplication.isPlaying || UnityEditor.EditorApplication.isCompiling)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, TestCancellationToken);
            }
#endif
            // Wait for Time.timeScale to be non-zero (game not paused)
            while (Time.timeScale == 0)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, TestCancellationToken);
            }

            // Yield a few frames to let Unity fully settle
            for (int i = 0; i < 3; i++)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, TestCancellationToken);
            }
        }

        static bool CheckAvailability(UnityEngine.Object obj, Availability availability)
        {
            if (availability == Availability.None)
                return true;

            if (obj == null)
                return false;

            GameObject go = null;
            Behaviour behaviour = null;

            if (obj is GameObject g)
                go = g;
            else if (obj is UnityEngine.Component c)
            {
                go = c.gameObject;
                behaviour = c as Behaviour;
            }

            if (go == null)
                return false;

            if ((availability & Availability.Active) != 0)
            {
                if (!go.activeInHierarchy)
                    return false;
            }

            if ((availability & Availability.Enabled) != 0)
            {
                if (behaviour != null && !behaviour.enabled)
                    return false;

                var canvasGroup = go.GetComponentInParent<CanvasGroup>();
                if (canvasGroup != null && (canvasGroup.alpha <= 0 || !canvasGroup.interactable))
                    return false;
            }

            return true;
        }

        public int Scenario
        {
            get
            {
                var attr = GetType().GetCustomAttribute<UITestAttribute>();
                if (attr == null)
                    throw new InvalidOperationException($"{GetType().Name} missing [UITest] attribute");
                if (attr.Scenario <= 0)
                    throw new InvalidOperationException($"{GetType().Name} [UITest] Scenario must be > 0");
                return attr.Scenario;
            }
        }

        public static bool Active => testScenario != 0;

        static int testScenario = 0;
        static string lastKnownScene;
        static float lastSceneChangeTime;

        protected void CaptureScreenshot(string name = "screenshot")
        {
            string path = Path.Combine(Application.temporaryCachePath, $"{name}_{DateTime.Now.Ticks}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[UITEST] Screenshot: {path}");
        }

        protected void AttachJson(string name, object data)
        {
            string json = JsonUtility.ToJson(data, true);
            Debug.Log($"[UITEST] Attach JSON '{name}': {json}");
        }

        protected void AttachText(string name, string content)
        {
            Debug.Log($"[UITEST] Attach Text '{name}': {content}");
        }

        protected void AttachFile(string name, string filePath, string mimeType)
        {
            if (File.Exists(filePath))
            {
                Debug.Log($"[UITEST] Attach File '{name}': {filePath} ({mimeType})");
            }
        }

        protected void AttachFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                Debug.Log($"[UITEST] Attach File: {filePath}");
            }
        }

        protected void AddParameter(string name, string value)
        {
            Debug.Log($"[UITEST] Parameter: {name}={value}");
        }

        protected void PauseRecording()
        {
        }

        protected void ResumeRecording()
        {
        }


        public static string TestRunName { get; private set; } = "";

#if UNITY_EDITOR
        [ContextMenu("Run Test")]
        void RunTest()
        {
            StartTest(clearData: false, testDataPath: null);
        }

        [ContextMenu("Run Test (Clear Data)")]
        void RunTestClearData()
        {
            StartTest(clearData: true, testDataPath: null);
        }

        [ContextMenu("Run Test with Data Folder...")]
        void RunTestWithDataFolder()
        {
            string folder = UnityEditor.EditorUtility.OpenFolderPanel("Select Test Data Folder", Application.persistentDataPath, "data");
            if (!string.IsNullOrEmpty(folder))
            {
                StartTest(clearData: false, testDataPath: folder);
            }
        }

        [ContextMenu("Run Test with Data Zip...")]
        void RunTestWithDataZip()
        {
            string zipPath = UnityEditor.EditorUtility.OpenFilePanel("Select Test Data Zip", Application.persistentDataPath, "zip");
            if (!string.IsNullOrEmpty(zipPath))
            {
                StartTest(clearData: false, testDataPath: zipPath);
            }
        }

        void StartTest(bool clearData, string testDataPath)
        {
            int scenario = Scenario;
            TestRunName = GetType().Name + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            if (clearData)
            {
                try
                {
                    string folder = Path.Combine(Application.persistentDataPath, "data");
                    if (Directory.Exists(folder))
                        Directory.Delete(folder, true);
                    PlayerPrefs.DeleteAll();
                    Debug.Log("[UITEST] Cleared test data");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[UITEST] Failed to clear: {ex.Message}");
                }
            }

            UnityEditor.SessionState.SetBool("GAME_LOOP_TEST", true);
            UnityEditor.SessionState.SetInt("GAME_LOOP_TEST_SCENARIO", scenario);
            UnityEditor.SessionState.SetString("GAME_LOOP_TEST_NAME", TestRunName);
            UnityEditor.SessionState.SetString("GAME_LOOP_TEST_DATA_PATH", testDataPath ?? "");
            UnityEditor.EditorApplication.isPlaying = true;
        }

        string GetTestDataPath()
        {
            string testFolder = Path.Combine(Application.dataPath, "UITestBehaviours", "GeneratedTests");
            string testName = GetType().Name;

            if (!Directory.Exists(testFolder))
                return null;

            var directories = Directory.GetDirectories(testFolder, "*", SearchOption.TopDirectoryOnly);
            foreach (var dir in directories)
            {
                string zipPath = Path.Combine(dir, "testdata.zip");
                if (File.Exists(zipPath))
                {
                    string scriptPath = Path.Combine(dir, $"{testName}.cs");
                    if (File.Exists(scriptPath))
                        return zipPath;
                }
            }

            return null;
        }
#endif

        void PrepareTestData()
        {
            var attr = GetType().GetCustomAttribute<UITestAttribute>();
            var dataMode = attr != null ? attr.DataMode : TestDataMode.Ask;

            if (dataMode == TestDataMode.UseCurrent)
            {
                Debug.Log("[UITEST] DataMode=UseCurrent, using existing data");
                return;
            }

            string testDataPath = FindTestData();
            if (string.IsNullOrEmpty(testDataPath))
            {
                Debug.Log("[UITEST] No test data found, using existing data");
                return;
            }

            string targetPath = Path.Combine(Application.persistentDataPath, "data");

            try
            {
                if (Directory.Exists(targetPath))
                    Directory.Delete(targetPath, true);

                if (testDataPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    System.IO.Compression.ZipFile.ExtractToDirectory(testDataPath, Application.persistentDataPath);
                    Debug.Log($"[UITEST] Extracted test data from: {testDataPath}");
                }
                else if (Directory.Exists(testDataPath))
                {
                    CopyDirectoryRuntime(testDataPath, targetPath);
                    Debug.Log($"[UITEST] Copied test data from: {testDataPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UITEST] Failed to prepare test data: {ex.Message}");
            }
        }

        string FindTestData()
        {
#if UNITY_EDITOR
            // Check if a custom data path was specified via context menu
            string customPath = UnityEditor.SessionState.GetString("GAME_LOOP_TEST_DATA_PATH", "");
            if (!string.IsNullOrEmpty(customPath))
            {
                UnityEditor.SessionState.EraseString("GAME_LOOP_TEST_DATA_PATH");
                if (File.Exists(customPath) || Directory.Exists(customPath))
                    return customPath;
            }

            string editorPath = GetTestDataPath();
            if (!string.IsNullOrEmpty(editorPath))
                return editorPath;
#endif
            return FindTestDataInStreamingAssets();
        }

        string FindTestDataInStreamingAssets()
        {
            string testName = GetType().Name;
            string streamingPath = Application.streamingAssetsPath;

            string zipPath = Path.Combine(streamingPath, "UITestData", $"{testName}.zip");
            if (File.Exists(zipPath))
                return zipPath;

            string folderPath = Path.Combine(streamingPath, "UITestData", testName);
            if (Directory.Exists(folderPath))
                return folderPath;

            return null;
        }

        static void CopyDirectoryRuntime(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string targetFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, targetFile, true);
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string targetSubDir = Path.Combine(targetDir, Path.GetFileName(dir));
                CopyDirectoryRuntime(dir, targetSubDir);
            }
        }


        protected virtual void LateUpdate()
        {
        }

        async void Start()
        {
            await UniTask.Yield();

#if UNITY_EDITOR
            if (UnityEditor.SessionState.GetBool("GAME_LOOP_TEST", false))
            {
                testScenario = UnityEditor.SessionState.GetInt("GAME_LOOP_TEST_SCENARIO", 0);
                TestRunName = UnityEditor.SessionState.GetString("GAME_LOOP_TEST_NAME", "");
            }

            UnityEditor.SessionState.SetBool("GAME_LOOP_TEST", false);
#endif

            if (Scenario != 0 && Scenario == testScenario)
            {
                GameObject.DontDestroyOnLoad(this.gameObject);
                EnsureSceneCallbackRegistered();

                PrepareTestData();

                if (testCts != null)
                {
                    testCts.Cancel();
                    testCts.Dispose();
                }
                testCts = new CancellationTokenSource();

                Debug.Log($"[UITEST] Test Start: {GetType().Name}");

                // Wait for editor to be fully in play mode and scene to initialize
                await WaitForPlayModeReady();
                await UniTask.Delay(1000, true, PlayerLoopTiming.Update, TestCancellationToken);

                try
                {
                    await Test();
                    Debug.Log($"[UITEST] Test PASSED: {GetType().Name}");
                }
                catch (OperationCanceledException)
                {
                    Debug.Log($"[UITEST] Test CANCELLED: {GetType().Name}");
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    Debug.Log($"[UITEST] Test FAILED: {GetType().Name}");
                }
                finally
                {
                    Debug.Log($"[UITEST] Test End: {GetType().Name}");

                    if (testCts != null)
                    {
                        testCts.Cancel();
                        testCts.Dispose();
                        testCts = null;
                    }

                    await UniTask.Yield();

#if UNITY_EDITOR
                    Debug.Log($"[UITEST] Stopping play mode...");
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit(0);
#endif
                }
            }
            else
            {
                GameObject.Destroy(this.gameObject);
            }
        }
        static bool sceneCallbackRegistered;

        static void EnsureSceneCallbackRegistered()
        {
            if (sceneCallbackRegistered)
                return;
            sceneCallbackRegistered = true;
            lastKnownScene = SceneManager.GetActiveScene().name;
            lastSceneChangeTime = Time.realtimeSinceStartup;
            SceneManager.sceneLoaded += OnSceneLoadedStatic;
        }

        static void OnSceneLoadedStatic(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != lastKnownScene)
            {
                Debug.Log($"[UITEST] Scene changed: {lastKnownScene} -> {scene.name}");
                lastKnownScene = scene.name;
                lastSceneChangeTime = Time.realtimeSinceStartup;
            }
        }

        public static bool GetScenario(out int test, out string logFile)
        {

            test = 0;
            logFile = "";

#if UNITY_ANDROID && !UNITY_EDITOR

            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            var intent = activity.Call<AndroidJavaObject>("getIntent");
            string action = intent.Call<string>("getAction");

            if (action.Equals("com.google.intent.action.TEST_LOOP"))
            {
                test = intent.Call<int>("getIntExtra", "scenario", 0);
                logFile = intent.Call<string>("getDataString");
                return true;

            }

#endif

            return false;

        }
        protected abstract UniTask Test();

        protected async UniTask Wait(float seconds = 1f)
        {
            await UniTask.Delay((int)(seconds * 1000), true, PlayerLoopTiming.Update, TestCancellationToken);
            await ActionComplete();
        }

        protected async UniTask Wait(string[] search, int seconds = 10)
        {
            Debug.Log($"[UITEST] Wait ({seconds}) [{string.Join(",", search)}]");
            await Find<MonoBehaviour>(search, true, seconds);
        }

        protected async UniTask Wait(string search, int seconds = 10)
        {
            Debug.Log($"[UITEST] Wait ({seconds}) [{search}]");
            await Find<MonoBehaviour>(search, true, seconds);
        }

        protected async UniTask WaitFor(Func<bool> condition, float seconds = 60, string description = "condition")
        {
            Debug.Log($"[UITEST] WaitFor ({seconds}) [{description}]");

            var startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying && !TestCancellationToken.IsCancellationRequested)
            {
                if (condition())
                {
                    await ActionComplete();
                    return;
                }

                await UniTask.Delay(Interval, true, PlayerLoopTiming.Update, TestCancellationToken);
            }

            TestCancellationToken.ThrowIfCancellationRequested();
            throw new System.TimeoutException($"Condition '{description}' not met within {seconds} seconds");
        }

        protected async UniTask SceneChange(float seconds = 30, float recentThreshold = 1f)
        {
            string startScene = SceneManager.GetActiveScene().name;
            float startTime = Time.realtimeSinceStartup;

            Debug.Log($"[UITEST] SceneChange - waiting for scene change from '{startScene}' (timeout: {seconds}s)");

            float timeSinceLastChange = startTime - lastSceneChangeTime;
            if (timeSinceLastChange < recentThreshold && lastKnownScene != startScene)
            {
                Debug.Log($"[UITEST] SceneChange - scene recently changed ({timeSinceLastChange:F2}s ago) to '{lastKnownScene}'");
                await ActionComplete();
                return;
            }

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying && !TestCancellationToken.IsCancellationRequested)
            {
                string currentScene = SceneManager.GetActiveScene().name;
                if (currentScene != startScene)
                {
                    Debug.Log($"[UITEST] SceneChange - scene changed to '{currentScene}'");
                    await ActionComplete();
                    return;
                }

                await UniTask.Delay(Interval, true, PlayerLoopTiming.Update, TestCancellationToken);
            }

            TestCancellationToken.ThrowIfCancellationRequested();
            throw new System.TimeoutException($"Scene did not change from '{startScene}' within {seconds} seconds");
        }

        protected async UniTask WaitFramerate(int averageFps, float sampleDuration = 2f, float timeout = 60f)
        {
            Debug.Log($"[UITEST] WaitFramerate - waiting for {averageFps} FPS (sample: {sampleDuration}s, timeout: {timeout}s)");

            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying && !TestCancellationToken.IsCancellationRequested)
            {
                float sampleStart = Time.realtimeSinceStartup;
                int frameCount = 0;

                while ((Time.realtimeSinceStartup - sampleStart) < sampleDuration && Application.isPlaying && !TestCancellationToken.IsCancellationRequested)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, TestCancellationToken);
                    frameCount++;
                }

                float elapsed = Time.realtimeSinceStartup - sampleStart;
                float currentFps = frameCount / elapsed;

                if (currentFps >= averageFps)
                {
                    Debug.Log($"[UITEST] WaitFramerate - achieved {currentFps:F1} FPS (target: {averageFps})");
                    await ActionComplete();
                    return;
                }

                Debug.Log($"[UITEST] WaitFramerate - current {currentFps:F1} FPS, waiting for {averageFps}...");
            }

            TestCancellationToken.ThrowIfCancellationRequested();
            throw new System.TimeoutException($"Framerate did not reach {averageFps} FPS within {timeout} seconds");
        }

        /// <summary>
        /// Enters text into an input field using Input System injection (click to focus, type characters).
        /// </summary>
        /// <param name="search">Name or search pattern for the input field</param>
        /// <param name="input">Text to enter</param>
        /// <param name="seconds">Timeout for finding the input field</param>
        /// <param name="pressEnter">Whether to press Enter after typing</param>
        protected async UniTask TextInput(string search, string input, float seconds = 10, bool pressEnter = false)
        {
            Debug.Log($"[UITEST] TextInput ({seconds}) [{search}] '{input}' pressEnter={pressEnter}");

            // Try to find TMP_InputField or legacy InputField - quick check first (single iteration)
            var findStart = Time.realtimeSinceStartup;
            var tmpInput = await Find<TMP_InputField>(search, false, 0.1f);
            var legacyInputQuick = tmpInput == null ? await Find<InputField>(search, false, 0.1f) : null;

            // If neither found on quick check, do full timeout search for TMP first, then legacy
            if (tmpInput == null && legacyInputQuick == null)
            {
                Debug.Log($"[UITEST] Quick check failed, doing full {seconds}s search...");
                tmpInput = await Find<TMP_InputField>(search, false, seconds);
            }

            Debug.Log($"[UITEST] Find took {(Time.realtimeSinceStartup - findStart) * 1000:F0}ms, TMP={tmpInput != null}, Legacy={legacyInputQuick != null}");

            if (tmpInput != null)
            {
                // Click to focus
                Vector2 screenPosition = GetScreenPosition(tmpInput.gameObject);
                await InjectMouseClick(screenPosition);
                await UniTask.Yield();

                // Type characters using ProcessEvent (adds to text) + ForceLabelUpdate (updates display)
                // TMP_InputField uses IMGUI Event.PopEvent() internally which we can't inject into,
                // so we must call ProcessEvent directly and then force the label update
                if (!string.IsNullOrEmpty(input))
                {
                    foreach (char c in input)
                    {
                        var keyEvent = new Event
                        {
                            type = EventType.KeyDown,
                            character = c,
                            keyCode = CharToKeyCode(c)
                        };
                        tmpInput.ProcessEvent(keyEvent);
                        tmpInput.ForceLabelUpdate();
                        await UniTask.Yield();
                    }
                }

                if (pressEnter)
                {
                    var enterEvent = new Event
                    {
                        type = EventType.KeyDown,
                        character = '\n',
                        keyCode = KeyCode.Return
                    };
                    tmpInput.ProcessEvent(enterEvent);
                }

                await ActionComplete();
                return;
            }

            // Fall back to legacy InputField (use quick result if found, otherwise full search)
            var legacyInput = legacyInputQuick ?? await Find<InputField>(search, true, seconds);

            // Click to focus the input field
            Vector2 legacyScreenPosition = GetScreenPosition(legacyInput.gameObject);
            await InjectMouseClick(legacyScreenPosition);

            // Type characters using ProcessEvent + ForceLabelUpdate
            if (!string.IsNullOrEmpty(input))
            {
                foreach (char c in input)
                {
                    var keyEvent = new Event
                    {
                        type = EventType.KeyDown,
                        character = c,
                        keyCode = CharToKeyCode(c)
                    };
                    legacyInput.ProcessEvent(keyEvent);
                    legacyInput.ForceLabelUpdate();
                    await UniTask.Yield();
                }
            }

            if (pressEnter)
            {
                var enterEvent = new Event
                {
                    type = EventType.KeyDown,
                    character = '\n',
                    keyCode = KeyCode.Return
                };
                legacyInput.ProcessEvent(enterEvent);
            }

            await ActionComplete();
        }

        /// <summary>
        /// Delegate to get the currently focused object from custom UI systems (e.g., EzGUI UIManager.FocusObject).
        /// </summary>
        public delegate GameObject FocusedObjectGetter();

        static readonly List<FocusedObjectGetter> focusedObjectGetters = new();

        /// <summary>
        /// Register a getter for the currently focused object in a custom UI system.
        /// </summary>
        public static void RegisterFocusedObjectGetter(FocusedObjectGetter getter)
        {
            if (getter != null && !focusedObjectGetters.Contains(getter))
                focusedObjectGetters.Add(getter);
        }

        /// <summary>
        /// Simulates a key press. Sends to the currently focused/selected object.
        /// Works with Unity UI (EventSystem.currentSelectedGameObject) and custom UI systems.
        /// </summary>
        protected async UniTask PressKey(KeyCode key)
        {
            GameObject target = GetFocusedObject();
            Debug.Log($"[UITEST] PressKey [{key}] target='{(target != null ? target.name : "none")}'");

            if (target != null && TryCustomKeyHandlers(target, key))
            {
                await ActionComplete();
                return;
            }

            await PressKeyUnityUI(target, key);
            await ActionComplete();
        }

        /// <summary>
        /// Simulates a key press on a specific target found by search pattern.
        /// </summary>
        protected async UniTask PressKey(KeyCode key, string search, float seconds = 10)
        {
            var component = await Find<Component>(search, true, seconds);
            GameObject target = component?.gameObject;
            Debug.Log($"[UITEST] PressKey [{key}] search='{search}' target='{(target != null ? target.name : "none")}'");

            if (target != null && TryCustomKeyHandlers(target, key))
            {
                await ActionComplete();
                return;
            }

            await PressKeyUnityUI(target, key);
            await ActionComplete();
        }

        /// <summary>
        /// Simulates a key press using a character (e.g., 'a', '1', ' ').
        /// </summary>
        protected async UniTask PressKey(char c)
        {
            var keyCode = CharToKeyCode(c);
            if (keyCode == KeyCode.None)
            {
                Debug.LogWarning($"[UITEST] PressKey - Unable to map character '{c}' to KeyCode");
                return;
            }
            await PressKey(keyCode);
        }

        /// <summary>
        /// Simulates a key press using a key name string (e.g., "Enter", "Space", "A", "Escape").
        /// </summary>
        protected async UniTask PressKey(string keyName)
        {
            // Try single character first
            if (keyName.Length == 1)
            {
                await PressKey(keyName[0]);
                return;
            }

            // Try parsing as KeyCode name
            if (System.Enum.TryParse<KeyCode>(keyName, true, out var keyCode))
            {
                await PressKey(keyCode);
                return;
            }

            // Handle common aliases
            var mappedKey = keyName.ToLowerInvariant() switch
            {
                "enter" => KeyCode.Return,
                "esc" => KeyCode.Escape,
                "up" => KeyCode.UpArrow,
                "down" => KeyCode.DownArrow,
                "left" => KeyCode.LeftArrow,
                "right" => KeyCode.RightArrow,
                "bs" or "backspace" => KeyCode.Backspace,
                "del" => KeyCode.Delete,
                _ => KeyCode.None
            };

            if (mappedKey != KeyCode.None)
            {
                await PressKey(mappedKey);
                return;
            }

            Debug.LogWarning($"[UITEST] PressKey - Unable to map key name '{keyName}' to KeyCode");
        }

        /// <summary>
        /// Types a sequence of characters by injecting keyboard events.
        /// Uses Input System TextEvent for focused input fields.
        /// </summary>
        protected async UniTask PressKeys(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            Debug.Log($"[UITEST] PressKeys - Typing '{text}' ({text.Length} characters)");

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                Debug.LogWarning("[UITEST] PressKeys - No keyboard device found");
                return;
            }

            // Inject each character as a text event via Input System
            foreach (char c in text)
            {
                var textEvent = TextEvent.Create(keyboard.deviceId, c);
                InputSystem.QueueEvent(ref textEvent);
                await UniTask.Yield();
            }
        }

        /// <summary>
        /// Maps a character to its corresponding KeyCode.
        /// </summary>
        private static KeyCode CharToKeyCode(char c)
        {
            // Letters (case-insensitive)
            if (c >= 'a' && c <= 'z')
                return KeyCode.A + (c - 'a');
            if (c >= 'A' && c <= 'Z')
                return KeyCode.A + (c - 'A');

            // Numbers
            if (c >= '0' && c <= '9')
                return KeyCode.Alpha0 + (c - '0');

            // Common symbols
            return c switch
            {
                ' ' => KeyCode.Space,
                '\n' or '\r' => KeyCode.Return,
                '\t' => KeyCode.Tab,
                '\b' => KeyCode.Backspace,
                '`' or '~' => KeyCode.BackQuote,
                '-' or '_' => KeyCode.Minus,
                '=' or '+' => KeyCode.Equals,
                '[' or '{' => KeyCode.LeftBracket,
                ']' or '}' => KeyCode.RightBracket,
                '\\' or '|' => KeyCode.Backslash,
                ';' or ':' => KeyCode.Semicolon,
                '\'' or '"' => KeyCode.Quote,
                ',' or '<' => KeyCode.Comma,
                '.' or '>' => KeyCode.Period,
                '/' or '?' => KeyCode.Slash,
                _ => KeyCode.None
            };
        }

        private static GameObject GetFocusedObject()
        {
            // Try custom UI systems first (e.g., EzGUI)
            foreach (var getter in focusedObjectGetters)
            {
                var focused = getter();
                if (focused != null)
                    return focused;
            }
            // Fall back to Unity UI EventSystem
            return EventSystem.current?.currentSelectedGameObject;
        }

        private static bool TryCustomKeyHandlers(GameObject target, KeyCode key)
        {
            foreach (var handler in keyPressHandlers)
            {
                if (handler(target, key))
                    return true;
            }
            return false;
        }

        private async UniTask PressKeyUnityUI(GameObject target, KeyCode key)
        {
            // Use the new Input System to inject true key press events
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                Debug.LogWarning("[UITEST] PressKey - No keyboard device found, cannot inject key");
                return;
            }

            var inputKey = KeyCodeToKey(key);
            if (inputKey == Key.None)
            {
                Debug.LogWarning($"[UITEST] PressKey - Unable to map KeyCode.{key} to Input System Key");
                return;
            }

            // Get the character for this key (for text input)
            char? textChar = KeyCodeToChar(key);

            // Inject key down
            using (StateEvent.From(keyboard, out var eventPtr))
            {
                keyboard[inputKey].WriteValueIntoEvent(1f, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }

            // Also inject a text event if this is a printable character
            // This is required for InputField to receive the character
            if (textChar.HasValue)
            {
                var textEvent = TextEvent.Create(keyboard.deviceId, textChar.Value);
                InputSystem.QueueEvent(ref textEvent);
            }

            // Wait a frame for the input to be processed
            await UniTask.Yield();

            // Inject key up
            using (StateEvent.From(keyboard, out var eventPtr))
            {
                keyboard[inputKey].WriteValueIntoEvent(0f, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }
        }

        /// <summary>
        /// Maps a KeyCode to its printable character (if applicable).
        /// </summary>
        private static char? KeyCodeToChar(KeyCode keyCode)
        {
            // Letters (lowercase)
            if (keyCode >= KeyCode.A && keyCode <= KeyCode.Z)
                return (char)('a' + (keyCode - KeyCode.A));

            // Numbers
            if (keyCode >= KeyCode.Alpha0 && keyCode <= KeyCode.Alpha9)
                return (char)('0' + (keyCode - KeyCode.Alpha0));

            // Common symbols
            return keyCode switch
            {
                KeyCode.Space => ' ',
                KeyCode.BackQuote => '`',
                KeyCode.Minus => '-',
                KeyCode.Equals => '=',
                KeyCode.LeftBracket => '[',
                KeyCode.RightBracket => ']',
                KeyCode.Backslash => '\\',
                KeyCode.Semicolon => ';',
                KeyCode.Quote => '\'',
                KeyCode.Comma => ',',
                KeyCode.Period => '.',
                KeyCode.Slash => '/',
                _ => null
            };
        }

        /// <summary>
        /// Maps a legacy KeyCode to the new Input System Key.
        /// </summary>
        private static Key KeyCodeToKey(KeyCode keyCode)
        {
            return keyCode switch
            {
                // Letters
                KeyCode.A => Key.A, KeyCode.B => Key.B, KeyCode.C => Key.C, KeyCode.D => Key.D,
                KeyCode.E => Key.E, KeyCode.F => Key.F, KeyCode.G => Key.G, KeyCode.H => Key.H,
                KeyCode.I => Key.I, KeyCode.J => Key.J, KeyCode.K => Key.K, KeyCode.L => Key.L,
                KeyCode.M => Key.M, KeyCode.N => Key.N, KeyCode.O => Key.O, KeyCode.P => Key.P,
                KeyCode.Q => Key.Q, KeyCode.R => Key.R, KeyCode.S => Key.S, KeyCode.T => Key.T,
                KeyCode.U => Key.U, KeyCode.V => Key.V, KeyCode.W => Key.W, KeyCode.X => Key.X,
                KeyCode.Y => Key.Y, KeyCode.Z => Key.Z,

                // Numbers
                KeyCode.Alpha0 => Key.Digit0, KeyCode.Alpha1 => Key.Digit1, KeyCode.Alpha2 => Key.Digit2,
                KeyCode.Alpha3 => Key.Digit3, KeyCode.Alpha4 => Key.Digit4, KeyCode.Alpha5 => Key.Digit5,
                KeyCode.Alpha6 => Key.Digit6, KeyCode.Alpha7 => Key.Digit7, KeyCode.Alpha8 => Key.Digit8,
                KeyCode.Alpha9 => Key.Digit9,

                // Numpad
                KeyCode.Keypad0 => Key.Numpad0, KeyCode.Keypad1 => Key.Numpad1, KeyCode.Keypad2 => Key.Numpad2,
                KeyCode.Keypad3 => Key.Numpad3, KeyCode.Keypad4 => Key.Numpad4, KeyCode.Keypad5 => Key.Numpad5,
                KeyCode.Keypad6 => Key.Numpad6, KeyCode.Keypad7 => Key.Numpad7, KeyCode.Keypad8 => Key.Numpad8,
                KeyCode.Keypad9 => Key.Numpad9,
                KeyCode.KeypadDivide => Key.NumpadDivide, KeyCode.KeypadMultiply => Key.NumpadMultiply,
                KeyCode.KeypadMinus => Key.NumpadMinus, KeyCode.KeypadPlus => Key.NumpadPlus,
                KeyCode.KeypadEnter => Key.NumpadEnter, KeyCode.KeypadPeriod => Key.NumpadPeriod,

                // Function keys
                KeyCode.F1 => Key.F1, KeyCode.F2 => Key.F2, KeyCode.F3 => Key.F3, KeyCode.F4 => Key.F4,
                KeyCode.F5 => Key.F5, KeyCode.F6 => Key.F6, KeyCode.F7 => Key.F7, KeyCode.F8 => Key.F8,
                KeyCode.F9 => Key.F9, KeyCode.F10 => Key.F10, KeyCode.F11 => Key.F11, KeyCode.F12 => Key.F12,

                // Arrow keys
                KeyCode.UpArrow => Key.UpArrow, KeyCode.DownArrow => Key.DownArrow,
                KeyCode.LeftArrow => Key.LeftArrow, KeyCode.RightArrow => Key.RightArrow,

                // Special keys
                KeyCode.Space => Key.Space, KeyCode.Return => Key.Enter, KeyCode.Escape => Key.Escape,
                KeyCode.Tab => Key.Tab, KeyCode.Backspace => Key.Backspace, KeyCode.Delete => Key.Delete,
                KeyCode.Insert => Key.Insert, KeyCode.Home => Key.Home, KeyCode.End => Key.End,
                KeyCode.PageUp => Key.PageUp, KeyCode.PageDown => Key.PageDown,

                // Modifiers
                KeyCode.LeftShift => Key.LeftShift, KeyCode.RightShift => Key.RightShift,
                KeyCode.LeftControl => Key.LeftCtrl, KeyCode.RightControl => Key.RightCtrl,
                KeyCode.LeftAlt => Key.LeftAlt, KeyCode.RightAlt => Key.RightAlt,
                KeyCode.LeftCommand => Key.LeftMeta, KeyCode.RightCommand => Key.RightMeta,
                KeyCode.LeftWindows => Key.LeftWindows, KeyCode.RightWindows => Key.RightWindows,

                // Symbols
                KeyCode.BackQuote => Key.Backquote, KeyCode.Minus => Key.Minus, KeyCode.Equals => Key.Equals,
                KeyCode.LeftBracket => Key.LeftBracket, KeyCode.RightBracket => Key.RightBracket,
                KeyCode.Backslash => Key.Backslash, KeyCode.Semicolon => Key.Semicolon, KeyCode.Quote => Key.Quote,
                KeyCode.Comma => Key.Comma, KeyCode.Period => Key.Period, KeyCode.Slash => Key.Slash,

                // Other
                KeyCode.CapsLock => Key.CapsLock, KeyCode.Numlock => Key.NumLock,
                KeyCode.ScrollLock => Key.ScrollLock, KeyCode.Pause => Key.Pause,
                KeyCode.Print => Key.PrintScreen,

                _ => Key.None
            };
        }


        protected async UniTask ClickAny(params string[] searches)
        {
            await ClickAny(searches, throwIfMissing: true, seconds: 5);
        }

        protected async UniTask ClickAny(string[] searches, bool throwIfMissing = true, float seconds = 5, Availability availability = Availability.Active | Availability.Enabled)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying)
            {
                var b = await Find<IPointerClickHandler>(searches, false, 0.5f, availability, Clickables);

                if (b != null)
                {
                    await SimulateClick(b);
                    await ActionComplete();
                    return;
                }

                await UniTask.Delay(100, true);
            }

            if (throwIfMissing)
            {
                throw new TestException($"ClickAny on '{string.Join(", ", searches)}' could not find any matching target within {seconds}s");
            }
        }

        private async UniTask SimulateClick(object target)
        {
            if (target is UnityEngine.Component component)
            {
                string path = GetHierarchyPath(component.transform);
                string textContent = "";
                if (TryGetComponentInChildren(component.gameObject, out TMP_Text tmpText) && tmpText != null)
                    textContent = tmpText.text;
                else if (TryGetComponentInChildren(component.gameObject, out Text uiText) && uiText != null)
                    textContent = uiText.text;

                Debug.Log($"[UITEST] CLICK executing - Name: '{component.name}' Path: '{path}' Text: '{textContent}'");

                // Get screen position of target
                Vector2 screenPosition = GetScreenPosition(component.gameObject);

                // Use Input System to inject mouse click
                await InjectMouseClick(screenPosition);
            }
        }

        /// <summary>
        /// Gets the screen position of a GameObject (works with both UI and world-space objects).
        /// </summary>
        private static Vector2 GetScreenPosition(GameObject go)
        {
            if (go.TryGetComponent<RectTransform>(out var rt))
            {
                // UI element - get center of rect in screen space
                Vector3[] corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                Vector3 center = (corners[0] + corners[2]) / 2f;

                // Find the canvas to determine if it's screen space or world space
                var canvas = go.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    return center;
                }
                else
                {
                    // World space or camera space canvas
                    Camera cam = canvas?.worldCamera ?? Camera.main;
                    return cam != null ? RectTransformUtility.WorldToScreenPoint(cam, center) : (Vector2)center;
                }
            }
            else
            {
                // World-space object
                Camera cam = Camera.main;
                return cam != null ? cam.WorldToScreenPoint(go.transform.position) : Vector2.zero;
            }
        }

        /// <summary>
        /// Injects a mouse click at the specified screen position using the Input System.
        /// </summary>
        private static async UniTask InjectMouseClick(Vector2 screenPosition)
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                Debug.LogWarning("[UITEST] Click - No mouse device found, cannot inject click");
                return;
            }

            // Move mouse to position
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, posPtr);
                InputSystem.QueueEvent(posPtr);
            }

            // Wait a frame for position update
            await UniTask.Yield();

            // Mouse button down
            using (StateEvent.From(mouse, out var downPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, downPtr);
                mouse.leftButton.WriteValueIntoEvent(1f, downPtr);
                InputSystem.QueueEvent(downPtr);
            }

            // Wait a frame
            await UniTask.Yield();

            // Mouse button up
            using (StateEvent.From(mouse, out var upPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, upPtr);
                mouse.leftButton.WriteValueIntoEvent(0f, upPtr);
                InputSystem.QueueEvent(upPtr);
            }

            // Wait for click to be processed
            await UniTask.Yield();
        }


        protected async UniTask Hold(string search, float seconds, bool throwIfMissing = true, float searchTime = 10, Availability availability = Availability.Active | Availability.Enabled)
        {
            Debug.Log($"[UITEST] Hold ({searchTime}) [{search}] for {seconds}s");

            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var b = await Find<IPointerDownHandler>(search, false, 0.5f, availability, Clickables);

                if (b != null && b is UnityEngine.Component c1)
                {
                    Vector2 screenPosition = GetScreenPosition(c1.gameObject);
                    await InjectMouseHold(screenPosition, seconds);
                    await ActionComplete();
                    return;
                }

                await UniTask.Delay(100, true);
            }

            if (throwIfMissing)
            {
                throw new TestException($"Hold on '{search}' could not find any matching target within {searchTime}s");
            }
        }

        /// <summary>
        /// Injects a mouse hold (press and hold for duration) at the specified screen position.
        /// </summary>
        private static async UniTask InjectMouseHold(Vector2 screenPosition, float holdSeconds)
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                Debug.LogWarning("[UITEST] Hold - No mouse device found, cannot inject hold");
                return;
            }

            // Move mouse to position
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, posPtr);
                InputSystem.QueueEvent(posPtr);
            }

            await UniTask.Yield();

            // Mouse button down
            using (StateEvent.From(mouse, out var downPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, downPtr);
                mouse.leftButton.WriteValueIntoEvent(1f, downPtr);
                InputSystem.QueueEvent(downPtr);
            }

            // Hold for specified duration
            await UniTask.Delay(TimeSpan.FromSeconds(holdSeconds), true);

            // Mouse button up
            using (StateEvent.From(mouse, out var upPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, upPtr);
                mouse.leftButton.WriteValueIntoEvent(0f, upPtr);
                InputSystem.QueueEvent(upPtr);
            }

            await UniTask.Yield();
        }

        protected async UniTask Click(string search = "", bool throwIfMissing = true, float searchTime = 10, int repeat = 0, Availability availability = Availability.Active | Availability.Enabled, int index = 0)
        {
            await Click(new string[] { search }, throwIfMissing, searchTime, repeat, availability, index);
        }

        protected async UniTask Click(string[] search, bool throwIfMissing = true, float searchTime = 10, int repeat = 0, Availability availability = Availability.Active | Availability.Enabled, int index = 0)
        {
            do
            {
                string indexInfo = index > 0 ? $" index={index}" : "";
                Debug.Log($"[UITEST] Click ({searchTime}) [{string.Join(", ", search)}]{indexInfo}");

                float startTime = Time.realtimeSinceStartup;

                while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
                {
                    if (index > 0)
                    {
                        var items = await FindAll<IPointerClickHandler>(search[0].ToLower(), 0.5f, availability, Clickables);
                        var itemList = items.ToList();

                        if (index < itemList.Count && itemList[index] != null)
                        {
                            await SimulateClick(itemList[index]);
                            await ActionComplete();
                            goto nextRepeat;
                        }
                    }
                    else
                    {
                        var b = await Find<IPointerClickHandler>(search, false, 0.5f, availability, Clickables);

                        if (b != null)
                        {
                            await SimulateClick(b);
                            await ActionComplete();
                            goto nextRepeat;
                        }
                    }

                    await UniTask.Delay(100, true);
                }

                if (throwIfMissing)
                {
                    string indexMsg = index > 0 ? $" at index {index}" : "";
                    throw new TestException($"Click on '{string.Join(", ", search)}'{indexMsg} could not find any matching target within {searchTime}s");
                }

                nextRepeat:
                repeat--;
            }
            while (repeat > 0);
        }

        private bool IsWildcardMatch(string subject, string wildcardPattern)
        {
            if (string.CompareOrdinal(subject, wildcardPattern) == 0)
                return true;

            if (string.IsNullOrWhiteSpace(wildcardPattern))
                return false;

            int wildcardCount = wildcardPattern.Count(x => x.Equals('*'));

            if (wildcardCount <= 0)
            {
                return subject.Equals(wildcardPattern, StringComparison.CurrentCultureIgnoreCase);
            }
            else if (wildcardCount == 1)
            {
                string newWildcardPattern = wildcardPattern.Replace("*", "");

                if (wildcardPattern.StartsWith("*"))
                    return subject.EndsWith(newWildcardPattern, StringComparison.CurrentCultureIgnoreCase);
                else if (wildcardPattern.EndsWith("*"))
                    return subject.StartsWith(newWildcardPattern, StringComparison.CurrentCultureIgnoreCase);
            }
            else if (wildcardCount == 2 && wildcardPattern.StartsWith("*") && wildcardPattern.EndsWith("*"))
            {
                // *text* = contains
                string searchText = wildcardPattern.Substring(1, wildcardPattern.Length - 2);
                return subject.IndexOf(searchText, StringComparison.CurrentCultureIgnoreCase) >= 0;
            }

            return false;
        }

        protected async UniTask Click(bool throwIfMissing = true)
        {
            Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
            Debug.Log($"[UITEST] Click (screen center) at ({screenCenter.x:F0}, {screenCenter.y:F0})");

            // Use Input System to inject click at screen center
            await InjectMouseClick(screenCenter);
            await ActionComplete();
        }

        /// <summary>
        /// Double-clicks on a UI element by name.
        /// </summary>
        /// <param name="search">Name or search pattern for the element</param>
        /// <param name="throwIfMissing">Whether to throw if element not found</param>
        /// <param name="searchTime">Timeout for finding the element</param>
        protected async UniTask DoubleClick(string search, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] DoubleClick [{search}]");

            var target = await Find<IPointerClickHandler>(search, throwIfMissing, searchTime);
            if (target == null) return;

            if (target is UnityEngine.Component c)
            {
                Vector2 screenPosition = GetScreenPosition(c.gameObject);
                await InjectMouseDoubleClick(screenPosition);
                await ActionComplete();
            }
        }

        /// <summary>
        /// Double-clicks at screen center.
        /// </summary>
        protected async UniTask DoubleClick()
        {
            Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
            Debug.Log($"[UITEST] DoubleClick (screen center) at ({screenCenter.x:F0}, {screenCenter.y:F0})");

            await InjectMouseDoubleClick(screenCenter);
            await ActionComplete();
        }

        /// <summary>
        /// Injects a mouse double-click at the specified screen position.
        /// </summary>
        private static async UniTask InjectMouseDoubleClick(Vector2 screenPosition)
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                Debug.LogWarning("[UITEST] DoubleClick - No mouse device found");
                return;
            }

            // First click
            await InjectMouseClick(screenPosition);

            // Short delay between clicks (typical double-click threshold is ~500ms)
            await UniTask.Delay(50, true);

            // Second click
            await InjectMouseClick(screenPosition);
        }

        /// <summary>
        /// Scrolls the mouse wheel at the specified element or screen center.
        /// </summary>
        /// <param name="search">Name or search pattern for the element to scroll on</param>
        /// <param name="delta">Scroll delta (positive = up, negative = down)</param>
        /// <param name="throwIfMissing">Whether to throw if element not found</param>
        /// <param name="searchTime">Timeout for finding the element</param>
        protected async UniTask Scroll(string search, float delta, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] Scroll [{search}] delta={delta}");

            var target = await Find<RectTransform>(search, throwIfMissing, searchTime);
            if (target == null) return;

            Vector2 screenPos = GetScreenPosition(target.gameObject);
            await InjectMouseScroll(screenPos, delta);
            await ActionComplete();
        }

        /// <summary>
        /// Scrolls the mouse wheel at screen center.
        /// </summary>
        /// <param name="delta">Scroll delta (positive = up, negative = down)</param>
        protected async UniTask Scroll(float delta)
        {
            Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
            Debug.Log($"[UITEST] Scroll (screen center) delta={delta}");

            await InjectMouseScroll(screenCenter, delta);
            await ActionComplete();
        }

        /// <summary>
        /// Injects a mouse scroll event at the specified position.
        /// </summary>
        private static async UniTask InjectMouseScroll(Vector2 screenPosition, float delta)
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                Debug.LogWarning("[UITEST] Scroll - No mouse device found");
                return;
            }

            // Move mouse to position first
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, posPtr);
                InputSystem.QueueEvent(posPtr);
            }

            await UniTask.Yield();

            // Send scroll event
            using (StateEvent.From(mouse, out var scrollPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, scrollPtr);
                mouse.scroll.WriteValueIntoEvent(new Vector2(0, delta * 120), scrollPtr); // 120 is standard scroll delta unit
                InputSystem.QueueEvent(scrollPtr);
            }

            await UniTask.Yield();
        }

        /// <summary>
        /// Swipes on an element in the specified direction.
        /// </summary>
        /// <param name="search">Name or search pattern for the element</param>
        /// <param name="direction">Swipe direction (Left, Right, Up, Down)</param>
        /// <param name="distance">Swipe distance as percentage of screen height (0.2 = 20%)</param>
        /// <param name="duration">Duration of the swipe</param>
        /// <param name="throwIfMissing">Whether to throw if element not found</param>
        /// <param name="searchTime">Timeout for finding the element</param>
        protected async UniTask Swipe(string search, SwipeDirection direction, float distance = 0.2f, float duration = 0.3f, bool throwIfMissing = true, float searchTime = 10)
        {
            float distancePixels = distance * Screen.height;
            Vector2 delta = direction switch
            {
                SwipeDirection.Left => new Vector2(-distancePixels, 0),
                SwipeDirection.Right => new Vector2(distancePixels, 0),
                SwipeDirection.Up => new Vector2(0, distancePixels),
                SwipeDirection.Down => new Vector2(0, -distancePixels),
                _ => Vector2.zero
            };

            Debug.Log($"[UITEST] Swipe [{search}] {direction} duration={duration}s distance={distance:P0} ({distancePixels:F0}px)");

            await Drag(search, delta, duration, throwIfMissing, searchTime);
        }

        /// <summary>
        /// Swipes at screen center in the specified direction.
        /// </summary>
        /// <param name="direction">Swipe direction (Left, Right, Up, Down)</param>
        /// <param name="distance">Swipe distance as percentage of screen height (0.2 = 20%)</param>
        /// <param name="duration">Duration of the swipe</param>
        protected async UniTask Swipe(SwipeDirection direction, float distance = 0.2f, float duration = 0.3f)
        {
            float distancePixels = distance * Screen.height;
            Vector2 delta = direction switch
            {
                SwipeDirection.Left => new Vector2(-distancePixels, 0),
                SwipeDirection.Right => new Vector2(distancePixels, 0),
                SwipeDirection.Up => new Vector2(0, distancePixels),
                SwipeDirection.Down => new Vector2(0, -distancePixels),
                _ => Vector2.zero
            };

            Debug.Log($"[UITEST] Swipe (screen center) {direction} distance={distance:P0} ({distancePixels:F0}px)");

            await Drag(delta, duration);
        }

        /// <summary>
        /// Performs a pinch gesture on an element.
        /// </summary>
        /// <param name="search">Name or search pattern for the element</param>
        /// <param name="scale">Scale factor: &gt;1 = zoom in (fingers spread), &lt;1 = zoom out (fingers pinch)</param>
        /// <param name="duration">Duration of the gesture</param>
        /// <param name="fingerDistance">Starting distance of each finger from center as percentage of screen height (0.05 = 5%)</param>
        /// <param name="throwIfMissing">Whether to throw if element not found</param>
        /// <param name="searchTime">Timeout for finding the element</param>
        protected async UniTask Pinch(string search, float scale, float duration = 0.5f, float fingerDistance = 0.05f, bool throwIfMissing = true, float searchTime = 10)
        {
            float distancePixels = fingerDistance * Screen.height;
            Debug.Log($"[UITEST] Pinch [{search}] scale={scale} duration={duration}s fingerDistance={fingerDistance:P0} ({distancePixels:F0}px)");

            var target = await Find<Transform>(search, throwIfMissing, searchTime);
            if (target == null) return;

            Vector2 center = GetScreenPosition(target.gameObject);
            await PinchAt(center, scale, duration, distancePixels);
        }

        /// <summary>
        /// Performs a pinch gesture at screen center.
        /// </summary>
        /// <param name="scale">Scale factor: &gt;1 = zoom in (fingers spread), &lt;1 = zoom out (fingers pinch)</param>
        /// <param name="duration">Duration of the gesture</param>
        /// <param name="fingerDistance">Starting distance of each finger from center as percentage of screen height (0.05 = 5%)</param>
        protected async UniTask Pinch(float scale, float duration = 0.5f, float fingerDistance = 0.05f)
        {
            Vector2 center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            float distancePixels = fingerDistance * Screen.height;
            Debug.Log($"[UITEST] Pinch (screen center) scale={scale} fingerDistance={fingerDistance:P0} ({distancePixels:F0}px)");

            await PinchAt(center, scale, duration, distancePixels);
        }

        /// <summary>
        /// Performs a pinch gesture at the specified screen position.
        /// </summary>
        private async UniTask PinchAt(Vector2 center, float scale, float duration, float fingerDistancePixels)
        {
            float endDistance = fingerDistancePixels * scale;

            Vector2 finger1Start = center + new Vector2(-fingerDistancePixels, 0);
            Vector2 finger2Start = center + new Vector2(fingerDistancePixels, 0);
            Vector2 finger1End = center + new Vector2(-endDistance, 0);
            Vector2 finger2End = center + new Vector2(endDistance, 0);

            await InjectTwoFingerGesture(finger1Start, finger1End, finger2Start, finger2End, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Performs a two-finger swipe gesture on an element.
        /// </summary>
        /// <param name="search">Name or search pattern for the element</param>
        /// <param name="direction">Swipe direction</param>
        /// <param name="distance">Swipe distance as percentage of screen height (0.2 = 20%)</param>
        /// <param name="duration">Duration of the gesture</param>
        /// <param name="fingerSpacing">Distance between the two fingers as percentage of screen height (0.03 = 3%)</param>
        /// <param name="throwIfMissing">Whether to throw if element not found</param>
        /// <param name="searchTime">Timeout for finding the element</param>
        protected async UniTask TwoFingerSwipe(string search, SwipeDirection direction, float distance = 0.2f, float duration = 0.3f, float fingerSpacing = 0.03f, bool throwIfMissing = true, float searchTime = 10)
        {
            float distancePixels = distance * Screen.height;
            float spacingPixels = fingerSpacing * Screen.height;
            Debug.Log($"[UITEST] TwoFingerSwipe [{search}] {direction} duration={duration}s distance={distance:P0} ({distancePixels:F0}px) spacing={fingerSpacing:P0} ({spacingPixels:F0}px)");

            var target = await Find<Transform>(search, throwIfMissing, searchTime);
            if (target == null)
            {
                Debug.LogWarning($"[UITEST] TwoFingerSwipe - target '{search}' not found");
                return;
            }

            Vector2 center = GetScreenPosition(target.gameObject);
            await TwoFingerSwipeAt(center, direction, distancePixels, duration, spacingPixels);
        }

        /// <summary>
        /// Performs a two-finger swipe gesture at screen center.
        /// </summary>
        /// <param name="direction">Swipe direction</param>
        /// <param name="distance">Swipe distance as percentage of screen height (0.2 = 20%)</param>
        /// <param name="duration">Duration of the gesture</param>
        /// <param name="fingerSpacing">Distance between the two fingers as percentage of screen height (0.03 = 3%)</param>
        protected async UniTask TwoFingerSwipe(SwipeDirection direction, float distance = 0.2f, float duration = 0.3f, float fingerSpacing = 0.03f)
        {
            Vector2 center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            float distancePixels = distance * Screen.height;
            float spacingPixels = fingerSpacing * Screen.height;
            Debug.Log($"[UITEST] TwoFingerSwipe (screen center) {direction} distance={distance:P0} ({distancePixels:F0}px) spacing={fingerSpacing:P0} ({spacingPixels:F0}px)");

            await TwoFingerSwipeAt(center, direction, distancePixels, duration, spacingPixels);
        }

        /// <summary>
        /// Performs a two-finger swipe at the specified screen position.
        /// </summary>
        private async UniTask TwoFingerSwipeAt(Vector2 center, SwipeDirection direction, float distancePixels, float duration, float spacingPixels)
        {
            Vector2 delta = direction switch
            {
                SwipeDirection.Left => new Vector2(-distancePixels, 0),
                SwipeDirection.Right => new Vector2(distancePixels, 0),
                SwipeDirection.Up => new Vector2(0, distancePixels),
                SwipeDirection.Down => new Vector2(0, -distancePixels),
                _ => Vector2.zero
            };

            float halfSpacing = spacingPixels / 2f;
            Vector2 finger1Start = center + new Vector2(-halfSpacing, 0);
            Vector2 finger2Start = center + new Vector2(halfSpacing, 0);
            Vector2 finger1End = finger1Start + delta;
            Vector2 finger2End = finger2Start + delta;

            await InjectTwoFingerGesture(finger1Start, finger1End, finger2Start, finger2End, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Performs a two-finger rotation gesture on an element.
        /// </summary>
        /// <param name="search">Name or search pattern for the element</param>
        /// <param name="degrees">Rotation angle in degrees (positive = clockwise)</param>
        /// <param name="duration">Duration of the gesture</param>
        /// <param name="fingerDistance">Distance of each finger from center as percentage of screen height (0.05 = 5%)</param>
        /// <param name="throwIfMissing">Whether to throw if element not found</param>
        /// <param name="searchTime">Timeout for finding the element</param>
        protected async UniTask Rotate(string search, float degrees, float duration = 0.5f, float fingerDistance = 0.05f, bool throwIfMissing = true, float searchTime = 10)
        {
            float radiusPixels = fingerDistance * Screen.height;
            Debug.Log($"[UITEST] Rotate [{search}] {degrees} degrees duration={duration}s fingerDistance={fingerDistance:P0} ({radiusPixels:F0}px)");

            var target = await Find<Transform>(search, throwIfMissing, searchTime);
            if (target == null)
            {
                Debug.LogWarning($"[UITEST] Rotate - target '{search}' not found");
                return;
            }

            var center = GetScreenPosition(target.gameObject);
            await RotateAt(center, degrees, duration, radiusPixels);
        }

        /// <summary>
        /// Performs a two-finger rotation gesture at the center of the screen.
        /// </summary>
        /// <param name="degrees">Rotation angle in degrees (positive = clockwise)</param>
        /// <param name="duration">Duration of the gesture</param>
        /// <param name="fingerDistance">Distance of each finger from center as percentage of screen height (0.05 = 5%)</param>
        protected async UniTask Rotate(float degrees, float duration = 0.5f, float fingerDistance = 0.05f)
        {
            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            float radiusPixels = fingerDistance * Screen.height;
            Debug.Log($"[UITEST] Rotate (screen center) {degrees} degrees fingerDistance={fingerDistance:P0} ({radiusPixels:F0}px)");

            await RotateAt(center, degrees, duration, radiusPixels);
        }

        /// <summary>
        /// Performs a rotation gesture at the specified screen position.
        /// </summary>
        private static async UniTask RotateAt(Vector2 center, float degrees, float duration, float radiusPixels)
        {
            float startAngle = 0f;
            float endAngle = degrees * Mathf.Deg2Rad;

            await InjectTwoFingerRotation(center, radiusPixels, startAngle, endAngle, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Injects a two-finger rotation gesture. Uses circular interpolation for smooth rotation.
        /// </summary>
        private static async UniTask InjectTwoFingerRotation(Vector2 center, float radius, float startAngle, float endAngle, float duration)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                touchscreen = InputSystem.AddDevice<Touchscreen>();
                if (touchscreen == null)
                {
                    Debug.LogWarning("[UITEST] Rotate - Could not create touchscreen device");
                    return;
                }
            }

            int steps = Mathf.Max(10, (int)(duration * 60));
            int delayPerStep = Mathf.Max(1, (int)(duration * 1000 / steps));

            // Calculate initial positions
            Vector2 finger1Start = center + new Vector2(Mathf.Cos(startAngle) * radius, Mathf.Sin(startAngle) * radius);
            Vector2 finger2Start = center + new Vector2(Mathf.Cos(startAngle + Mathf.PI) * radius, Mathf.Sin(startAngle + Mathf.PI) * radius);

            // Begin touches
            using (StateEvent.From(touchscreen, out var beginPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(1, beginPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(finger1Start, beginPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);

                touchscreen.touches[1].touchId.WriteValueIntoEvent(2, beginPtr);
                touchscreen.touches[1].position.WriteValueIntoEvent(finger2Start, beginPtr);
                touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);

                InputSystem.QueueEvent(beginPtr);
            }

            await UniTask.Yield();

            // Move touches in a circular path
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                float currentAngle = Mathf.Lerp(startAngle, endAngle, t);

                Vector2 pos1 = center + new Vector2(Mathf.Cos(currentAngle) * radius, Mathf.Sin(currentAngle) * radius);
                Vector2 pos2 = center + new Vector2(Mathf.Cos(currentAngle + Mathf.PI) * radius, Mathf.Sin(currentAngle + Mathf.PI) * radius);

                using (StateEvent.From(touchscreen, out var movePtr))
                {
                    touchscreen.touches[0].touchId.WriteValueIntoEvent(1, movePtr);
                    touchscreen.touches[0].position.WriteValueIntoEvent(pos1, movePtr);
                    touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, movePtr);

                    touchscreen.touches[1].touchId.WriteValueIntoEvent(2, movePtr);
                    touchscreen.touches[1].position.WriteValueIntoEvent(pos2, movePtr);
                    touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, movePtr);

                    InputSystem.QueueEvent(movePtr);
                }

                await UniTask.Delay(delayPerStep, true);
            }

            // End touches
            Vector2 finger1End = center + new Vector2(Mathf.Cos(endAngle) * radius, Mathf.Sin(endAngle) * radius);
            Vector2 finger2End = center + new Vector2(Mathf.Cos(endAngle + Mathf.PI) * radius, Mathf.Sin(endAngle + Mathf.PI) * radius);

            using (StateEvent.From(touchscreen, out var endPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(1, endPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(finger1End, endPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);

                touchscreen.touches[1].touchId.WriteValueIntoEvent(2, endPtr);
                touchscreen.touches[1].position.WriteValueIntoEvent(finger2End, endPtr);
                touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);

                InputSystem.QueueEvent(endPtr);
            }

            await UniTask.Yield();
        }

        /// <summary>
        /// Injects a two-finger touch gesture using the Input System.
        /// </summary>
        private static async UniTask InjectTwoFingerGesture(Vector2 finger1Start, Vector2 finger1End, Vector2 finger2Start, Vector2 finger2End, float duration)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                // Add a touchscreen if none exists
                touchscreen = InputSystem.AddDevice<Touchscreen>();
                if (touchscreen == null)
                {
                    Debug.LogWarning("[UITEST] TwoFingerGesture - Could not create touchscreen device");
                    return;
                }
            }

            int steps = Mathf.Max(10, (int)(duration * 60));
            int delayPerStep = Mathf.Max(1, (int)(duration * 1000 / steps));

            // Begin touches
            using (StateEvent.From(touchscreen, out var beginPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(1, beginPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(finger1Start, beginPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);

                touchscreen.touches[1].touchId.WriteValueIntoEvent(2, beginPtr);
                touchscreen.touches[1].position.WriteValueIntoEvent(finger2Start, beginPtr);
                touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);

                InputSystem.QueueEvent(beginPtr);
            }

            await UniTask.Yield();

            // Move touches
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                Vector2 pos1 = Vector2.Lerp(finger1Start, finger1End, t);
                Vector2 pos2 = Vector2.Lerp(finger2Start, finger2End, t);

                using (StateEvent.From(touchscreen, out var movePtr))
                {
                    touchscreen.touches[0].touchId.WriteValueIntoEvent(1, movePtr);
                    touchscreen.touches[0].position.WriteValueIntoEvent(pos1, movePtr);
                    touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, movePtr);

                    touchscreen.touches[1].touchId.WriteValueIntoEvent(2, movePtr);
                    touchscreen.touches[1].position.WriteValueIntoEvent(pos2, movePtr);
                    touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, movePtr);

                    InputSystem.QueueEvent(movePtr);
                }

                await UniTask.Delay(delayPerStep, true);
            }

            // End touches
            using (StateEvent.From(touchscreen, out var endPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(1, endPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(finger1End, endPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);

                touchscreen.touches[1].touchId.WriteValueIntoEvent(2, endPtr);
                touchscreen.touches[1].position.WriteValueIntoEvent(finger2End, endPtr);
                touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);

                InputSystem.QueueEvent(endPtr);
            }

            await UniTask.Yield();
        }

        protected async UniTask Drag(Vector2 direction, float duration = 0.5f)
        {
            Vector2 startPos = new Vector2(Screen.width / 2f, Screen.height / 2f);
            await DragFromTo(startPos, startPos + direction, duration);
        }

        protected async UniTask Drag(string search, Vector2 direction, float duration = 0.5f, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] Drag ({duration}s) [{search}] delta=({direction.x:F0},{direction.y:F0})");

            var target = await Find<RectTransform>(search, throwIfMissing, searchTime);
            if (target == null) return;

            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector2 center = (corners[0] + corners[2]) / 2f;
            Vector2 screenCenter = RectTransformUtility.WorldToScreenPoint(null, center);

            await DragFromTo(screenCenter, screenCenter + direction, duration);
        }

        protected async UniTask DragFromTo(Vector2 startPos, Vector2 endPos, float duration = 0.5f)
        {
            Debug.Log($"[UITEST] DragFromTo ({duration}s) from ({startPos.x:F0},{startPos.y:F0}) to ({endPos.x:F0},{endPos.y:F0})");

            await InjectMouseDrag(startPos, endPos, duration);

            await ActionComplete();
        }

        /// <summary>
        /// Drags one element to another element (drag and drop).
        /// </summary>
        /// <param name="sourceSearch">Name or search pattern for the element to drag</param>
        /// <param name="targetSearch">Name or search pattern for the drop target</param>
        /// <param name="duration">Duration of the drag animation</param>
        /// <param name="throwIfMissing">Whether to throw if elements not found</param>
        /// <param name="searchTime">Timeout for finding the elements</param>
        protected async UniTask DragTo(string sourceSearch, string targetSearch, float duration = 0.5f, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] DragTo ({duration}s) '{sourceSearch}' -> '{targetSearch}'");

            var source = await Find<RectTransform>(sourceSearch, throwIfMissing, searchTime);
            if (source == null) return;

            var target = await Find<RectTransform>(targetSearch, throwIfMissing, searchTime);
            if (target == null) return;

            Vector2 sourcePos = GetScreenPosition(source.gameObject);
            Vector2 targetPos = GetScreenPosition(target.gameObject);

            Debug.Log($"[UITEST] DragTo - dragging from ({sourcePos.x:F0},{sourcePos.y:F0}) to ({targetPos.x:F0},{targetPos.y:F0})");

            await InjectMouseDrag(sourcePos, targetPos, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Clicks on a slider at a percentage position (0-1) of its visible area.
        /// </summary>
        /// <param name="search">Name or search pattern for the slider</param>
        /// <param name="percent">Position to click as percentage (0 = left/bottom, 1 = right/top)</param>
        /// <param name="throwIfMissing">Whether to throw if slider not found</param>
        /// <param name="searchTime">Timeout for finding the slider</param>
        protected async UniTask ClickSlider(string search, float percent, bool throwIfMissing = true, float searchTime = 10)
        {
            percent = Mathf.Clamp01(percent);
            Debug.Log($"[UITEST] ClickSlider ({searchTime}s) [{search}] at {percent:P0}");

            var slider = await Find<Slider>(search, throwIfMissing, searchTime);
            if (slider == null) return;

            Vector2 clickPos = GetSliderPositionAtPercent(slider, percent);
            Debug.Log($"[UITEST] ClickSlider - clicking at ({clickPos.x:F0},{clickPos.y:F0})");

            await InjectMouseClick(clickPos);
            await ActionComplete();
        }

        /// <summary>
        /// Drags on a slider from one percentage position to another.
        /// </summary>
        /// <param name="search">Name or search pattern for the slider</param>
        /// <param name="fromPercent">Start position as percentage (0-1)</param>
        /// <param name="toPercent">End position as percentage (0-1)</param>
        /// <param name="throwIfMissing">Whether to throw if slider not found</param>
        /// <param name="searchTime">Timeout for finding the slider</param>
        /// <param name="duration">Duration of the drag animation</param>
        protected async UniTask DragSlider(string search, float fromPercent, float toPercent, bool throwIfMissing = true, float searchTime = 10, float duration = 0.3f)
        {
            fromPercent = Mathf.Clamp01(fromPercent);
            toPercent = Mathf.Clamp01(toPercent);
            Debug.Log($"[UITEST] DragSlider ({searchTime}s) [{search}] from {fromPercent:P0} to {toPercent:P0}");

            var slider = await Find<Slider>(search, throwIfMissing, searchTime);
            if (slider == null) return;

            Vector2 startPos = GetSliderPositionAtPercent(slider, fromPercent);
            Vector2 endPos = GetSliderPositionAtPercent(slider, toPercent);

            Debug.Log($"[UITEST] DragSlider - dragging from ({startPos.x:F0},{startPos.y:F0}) to ({endPos.x:F0},{endPos.y:F0})");

            await InjectMouseDrag(startPos, endPos, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Gets the screen position at a percentage along the slider's visible area.
        /// </summary>
        private static Vector2 GetSliderPositionAtPercent(Slider slider, float percent)
        {
            var sliderRT = slider.GetComponent<RectTransform>();

            Vector3[] corners = new Vector3[4];
            sliderRT.GetWorldCorners(corners);

            // corners: 0=bottom-left, 1=top-left, 2=top-right, 3=bottom-right
            Vector2 bottomLeft = corners[0];
            Vector2 topRight = corners[2];

            var canvas = slider.GetComponentInParent<Canvas>();
            Camera cam = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                cam = canvas.worldCamera ?? Camera.main;
            }

            // Determine direction - most sliders are horizontal left-to-right
            bool isHorizontal = slider.direction == Slider.Direction.LeftToRight ||
                               slider.direction == Slider.Direction.RightToLeft;
            bool isReversed = slider.direction == Slider.Direction.RightToLeft ||
                             slider.direction == Slider.Direction.TopToBottom;

            if (isReversed)
                percent = 1f - percent;

            Vector3 worldPos;
            if (isHorizontal)
            {
                float x = Mathf.Lerp(bottomLeft.x, topRight.x, percent);
                float y = (bottomLeft.y + topRight.y) / 2f;
                worldPos = new Vector3(x, y, 0);
            }
            else
            {
                float x = (bottomLeft.x + topRight.x) / 2f;
                float y = Mathf.Lerp(bottomLeft.y, topRight.y, percent);
                worldPos = new Vector3(x, y, 0);
            }

            return cam != null
                ? RectTransformUtility.WorldToScreenPoint(cam, worldPos)
                : (Vector2)worldPos;
        }

        /// <summary>
        /// Selects a dropdown option by index using actual clicks.
        /// Clicks the dropdown to open it, then clicks the option at the specified index.
        /// Supports both legacy Dropdown and TMP_Dropdown.
        /// </summary>
        /// <param name="search">Name or search pattern for the dropdown</param>
        /// <param name="optionIndex">Index of the option to select (0-based)</param>
        /// <param name="throwIfMissing">Whether to throw if dropdown not found</param>
        /// <param name="searchTime">Timeout for finding the dropdown</param>
        protected async UniTask ClickDropdown(string search, int optionIndex, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] ClickDropdown ({searchTime}s) [{search}] option index={optionIndex}");

            // Try to find legacy Dropdown first, then TMP_Dropdown
            var legacyDropdown = await Find<Dropdown>(search, false, searchTime / 2);
            var tmpDropdown = legacyDropdown == null ? await Find<TMP_Dropdown>(search, false, searchTime / 2) : null;

            GameObject dropdownGO = null;
            if (legacyDropdown != null)
                dropdownGO = legacyDropdown.gameObject;
            else if (tmpDropdown != null)
                dropdownGO = tmpDropdown.gameObject;

            if (dropdownGO == null)
            {
                if (throwIfMissing)
                    throw new TestException($"ClickDropdown - Could not find Dropdown or TMP_Dropdown '{search}'");
                return;
            }

            // Click the dropdown to open it
            Vector2 dropdownPos = GetScreenPosition(dropdownGO);
            await InjectMouseClick(dropdownPos);

            // Wait for dropdown list to appear and stabilize
            await UniTask.Delay(150, true);

            // Find the dropdown list - it's created as a child when opened
            Transform dropdownList = dropdownGO.transform.Find("Dropdown List");
            if (dropdownList == null)
            {
                // Try finding it in the scene (some implementations create it elsewhere)
                var allDropdownLists = GameObject.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (var t in allDropdownLists)
                {
                    if (t.name == "Dropdown List" && t.gameObject.activeInHierarchy)
                    {
                        dropdownList = t;
                        break;
                    }
                }
            }

            if (dropdownList == null)
            {
                Debug.LogWarning($"[UITEST] ClickDropdown - Dropdown list not found after clicking '{search}'");
                return;
            }

            // Find the content container with the items
            Transform content = dropdownList.Find("Viewport/Content");
            if (content == null)
            {
                // Try alternative paths
                content = dropdownList.Find("Content");
                if (content == null)
                {
                    // Search recursively for Content
                    content = FindChildRecursive(dropdownList, "Content");
                }
            }

            if (content == null)
            {
                Debug.LogWarning($"[UITEST] ClickDropdown - Content not found in dropdown list");
                return;
            }

            // Find the item at the target index - items are named "Item N: Label" or just "Item N"
            Transform targetItem = null;
            int itemCount = 0;
            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i);
                // Skip template items (usually inactive)
                if (!child.gameObject.activeInHierarchy)
                    continue;

                // Check if this is the item we want
                if (child.name.StartsWith("Item "))
                {
                    if (itemCount == optionIndex)
                    {
                        targetItem = child;
                        break;
                    }
                    itemCount++;
                }
            }

            if (targetItem == null)
            {
                Debug.LogWarning($"[UITEST] ClickDropdown - Item at index {optionIndex} not found (found {itemCount} items)");
                return;
            }

            // Click the item
            Vector2 itemPos = GetScreenPosition(targetItem.gameObject);
            await InjectMouseClick(itemPos);

            await ActionComplete();
        }

        /// <summary>
        /// Selects a dropdown option by label text using actual clicks.
        /// Supports both legacy Dropdown and TMP_Dropdown.
        /// </summary>
        /// <param name="search">Name or search pattern for the dropdown</param>
        /// <param name="optionLabel">The text label of the option to select</param>
        /// <param name="throwIfMissing">Whether to throw if dropdown not found</param>
        /// <param name="searchTime">Timeout for finding the dropdown</param>
        protected async UniTask ClickDropdown(string search, string optionLabel, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] ClickDropdown ({searchTime}s) [{search}] option='{optionLabel}'");

            // Try to find legacy Dropdown first, then TMP_Dropdown
            var legacyDropdown = await Find<Dropdown>(search, false, searchTime / 2);
            var tmpDropdown = legacyDropdown == null ? await Find<TMP_Dropdown>(search, false, searchTime / 2) : null;

            int optionIndex = -1;

            if (legacyDropdown != null)
            {
                for (int i = 0; i < legacyDropdown.options.Count; i++)
                {
                    if (legacyDropdown.options[i].text == optionLabel)
                    {
                        optionIndex = i;
                        break;
                    }
                }
            }
            else if (tmpDropdown != null)
            {
                for (int i = 0; i < tmpDropdown.options.Count; i++)
                {
                    if (tmpDropdown.options[i].text == optionLabel)
                    {
                        optionIndex = i;
                        break;
                    }
                }
            }
            else
            {
                if (throwIfMissing)
                    throw new TestException($"ClickDropdown - Could not find Dropdown or TMP_Dropdown '{search}'");
                return;
            }

            if (optionIndex < 0)
            {
                Debug.LogWarning($"[UITEST] ClickDropdown - Option '{optionLabel}' not found in dropdown");
                return;
            }

            await ClickDropdown(search, optionIndex, throwIfMissing, searchTime: 0.5f);
        }

        /// <summary>
        /// Finds a child transform recursively by name.
        /// </summary>
        private static Transform FindChildRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child;
                var found = FindChildRecursive(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>
        /// Injects a mouse drag from start to end position using the Input System.
        /// </summary>
        private static async UniTask InjectMouseDrag(Vector2 startPos, Vector2 endPos, float duration)
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                Debug.LogWarning("[UITEST] Drag - No mouse device found, cannot inject drag");
                return;
            }

            Debug.Log($"[UITEST] InjectMouseDrag - start=({startPos.x:F0},{startPos.y:F0}) end=({endPos.x:F0},{endPos.y:F0}) duration={duration}s");

            // Move mouse to start position
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(startPos, posPtr);
                InputSystem.QueueEvent(posPtr);
            }

            await UniTask.Yield();

            // Mouse button down at start
            using (StateEvent.From(mouse, out var downPtr))
            {
                mouse.position.WriteValueIntoEvent(startPos, downPtr);
                mouse.leftButton.WriteValueIntoEvent(1f, downPtr);
                InputSystem.QueueEvent(downPtr);
            }

            Debug.Log($"[UITEST] InjectMouseDrag - mouse down at ({startPos.x:F0},{startPos.y:F0})");

            // Wait a frame for the press to register before starting drag
            await UniTask.Yield();

            // Interpolate mouse position over duration
            int steps = Mathf.Max(10, (int)(duration * 60));
            int delayPerStep = Mathf.Max(1, (int)(duration * 1000 / steps));

            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                Vector2 currentPos = Vector2.Lerp(startPos, endPos, t);

                using (StateEvent.From(mouse, out var movePtr))
                {
                    mouse.position.WriteValueIntoEvent(currentPos, movePtr);
                    mouse.leftButton.WriteValueIntoEvent(1f, movePtr);
                    InputSystem.QueueEvent(movePtr);
                }

                await UniTask.Delay(delayPerStep, true);
            }

            Debug.Log($"[UITEST] InjectMouseDrag - drag complete, releasing at ({endPos.x:F0},{endPos.y:F0})");

            // Mouse button up at end
            using (StateEvent.From(mouse, out var upPtr))
            {
                mouse.position.WriteValueIntoEvent(endPos, upPtr);
                mouse.leftButton.WriteValueIntoEvent(0f, upPtr);
                InputSystem.QueueEvent(upPtr);
            }

            await UniTask.Yield();
        }

        protected async UniTask SimulatePlay(int seconds = 20, params string[] targets)
        {

            Debug.Log($"[UITEST] SimulatePlay ({seconds}) [{string.Join(',', targets)}]");

            await UniTask.Delay(Interval, true);

            var startTime = Time.realtimeSinceStartup;

            foreach (var t in targets)
            {
                SimulatePlayTarget(t, startTime, seconds).Forget();
            }

            await UniTask.Delay(TimeSpan.FromSeconds(seconds), true);

        }

        private async UniTaskVoid SimulatePlayTarget(string t, float startTime, int seconds)
        {
            var target = await Find<IPointerDownHandler>(t, true, seconds, Clickables);
            if (target == null || !(target is UnityEngine.Component component))
                return;

            Vector2 screenPosition = GetScreenPosition(component.gameObject);

            while (Time.realtimeSinceStartup - startTime < seconds && Application.isPlaying)
            {
                // Random hold duration
                int holdDuration = UnityEngine.Random.Range(300, Mathf.Min(3000, seconds * 1000));
                float holdSeconds = holdDuration / 1000f;

                await InjectMouseHold(screenPosition, holdSeconds);

                await UniTask.Delay(UnityEngine.Random.Range(10, 100), true);
            }
        }



        protected async UniTask ClickAny(string search, float seconds = 10, bool throwIfMissing = true, Availability availability = Availability.Active | Availability.Enabled)
        {
            Debug.Log($"[UITEST] ClickAny ({seconds}) [{search}]");

            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying)
            {
                try
                {
                    var list = await FindAll<IPointerClickHandler>(search.ToLower(), 0.5f, availability, Clickables);
                    var rnd = new System.Random((int)DateTime.Now.Millisecond);
                    var clicktargets = list.OrderBy(i => rnd.Next());

                    foreach (var item in clicktargets)
                    {
                        if (item != null)
                        {
                            await SimulateClick(item);
                            await ActionComplete();
                            return;
                        }
                    }
                }
                catch (TimeoutException) { }

                await UniTask.Delay(100, true);
            }

            if (throwIfMissing)
            {
                throw new TestException($"ClickAny on '{search}' could not find any matching target within {seconds}s");
            }
        }


        protected async UniTask<T> Find<T>(bool throwIfMissing = true, float seconds = 10, Availability availability = Availability.Active | Availability.Enabled)
            where T : MonoBehaviour
        {
            var startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < seconds)
            {
                await UniTask.Delay(Interval, true);

                var result = GameObject.FindAnyObjectByType<T>();

                if (result == null)
                    continue;

                if (!CheckAvailability(result, availability))
                    continue;

                return result;
            }

            if (throwIfMissing)
                throw new System.TimeoutException($"Unable to locate {typeof(T).Name} in {seconds} seconds");

            return default;
        }

        protected UniTask<T> Find<T>(string search, bool throwIfMissing = true, float seconds = 10, params Type[] filterTypes)
        {
            return Find<T>(new string[] { search }, throwIfMissing, seconds, Availability.Active | Availability.Enabled, filterTypes);
        }

        protected UniTask<T> Find<T>(string search, bool throwIfMissing, float seconds, Availability availability, params Type[] filterTypes)
        {
            return Find<T>(new string[] { search }, throwIfMissing, seconds, availability, filterTypes);
        }

        protected UniTask<T> Find<T>(string[] searches, bool throwIfMissing, float seconds, params Type[] filterTypes)
        {
            return Find<T>(searches, throwIfMissing, seconds, Availability.Active | Availability.Enabled, filterTypes);
        }

        protected async UniTask<T> Find<T>(string[] searches = null, bool throwIfMissing = true, float seconds = 10, Availability availability = Availability.Active | Availability.Enabled, params Type[] filterTypes)
        {
            var startTime = Time.realtimeSinceStartup;

            TimeYielder yielder = new TimeYielder(TimeSpan.FromSeconds(0.1f));
            int iteration = 0;

            // Check if T is a Behaviour type - if not, we need to use GameObject search
            bool isBehaviour = typeof(Behaviour).IsAssignableFrom(typeof(T));

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying && !TestCancellationToken.IsCancellationRequested)
            {
                iteration++;

                if (!isBehaviour && searches != null)
                {
                    // For non-Behaviour types (like RectTransform), search by GameObject name and GetComponent
                    var allTransforms = GameObject.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                    foreach (var t in allTransforms)
                    {
                        var name = t.name.ToLowerInvariant();
                        foreach (string s in searches)
                        {
                            string search = s.ToLowerInvariant();
                            if (string.CompareOrdinal(search, name) == 0 || IsWildcardMatch(name, search))
                            {
                                var component = t.GetComponent<T>();
                                if (component != null && CheckAvailability(component as UnityEngine.Object, availability))
                                {
                                    Debug.Log($"[UITEST] Find<{typeof(T).Name}> '{string.Join(',', searches)}' found on iteration {iteration} after {(Time.realtimeSinceStartup - startTime) * 1000:F0}ms");
                                    await ActionComplete();
                                    return component;
                                }
                            }
                        }
                    }

                    await UniTask.Delay(Interval, true, PlayerLoopTiming.Update, TestCancellationToken);
                    continue;
                }

                List<Behaviour> behaviours = new List<Behaviour>();

                if (filterTypes == null || filterTypes.Length == 0)
                {
                    behaviours.AddRange(GameObject.FindObjectsByType<Behaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
                }
                else
                {
                    foreach (var filterType in filterTypes)
                        behaviours.AddRange(GameObject.FindObjectsByType(filterType, FindObjectsInactive.Exclude, FindObjectsSortMode.None).OfType<Behaviour>());
                }

                if (searches == null)
                {
                    var first = behaviours.OfType<T>().FirstOrDefault(b => CheckAvailability(b as UnityEngine.Object, availability));
                    if (first != null)
                        return first;
                    continue;
                }

                List<T> stringMatches = new List<T>();

                foreach (var behaviour in behaviours)
                {
                    if (behaviour == null)
                        continue;

                    if (behaviour is T item)
                    {
                        foreach (string s in searches)
                        {
                            string search = s.ToLowerInvariant();
                            var name = behaviour.name.ToLowerInvariant();

                            if (string.CompareOrdinal(search, name) == 0 || IsWildcardMatch(name, search))
                            {
                                stringMatches.Add(item);
                                break;
                            }

                            var path = GetHierarchyPath(behaviour.transform).ToLowerInvariant();
                            if (string.CompareOrdinal(search, path) == 0 || IsWildcardMatch(path, search))
                            {
                                stringMatches.Add(item);
                                break;
                            }

                            if (TryGetComponentInChildren(behaviour.gameObject, out Text t2) && t2 != null && IsWildcardMatch(t2.text != null ? t2.text.ToLowerInvariant() : "", search))
                            {
                                stringMatches.Add(item);
                                break;
                            }

                            if (TryGetComponentInChildren(behaviour.gameObject, out TMP_Text t3) && t3 != null && IsWildcardMatch(t3.text != null ? t3.text.ToLowerInvariant() : "", search))
                            {
                                stringMatches.Add(item);
                                break;
                            }

                            await yielder.YieldOptional();
                        }
                    }

                    await yielder.YieldOptional();
                }

                if (stringMatches.Count > 0)
                {
                    var topMatches = new List<T>();

                    foreach (var match in stringMatches)
                    {
                        if (match is UnityEngine.Component c)
                        {
                            if (!CheckAvailability(c, availability))
                                continue;

                            // Skip raycast check - elements just need to exist and be active
                            // Click operations will still raycast to find the actual click target
                            topMatches.Add(match);
                        }
                    }

                    if (topMatches.Count > 0)
                    {
                        Debug.Log($"[UITEST] Find<{typeof(T).Name}> '{string.Join(',', searches ?? new string[0])}' found on iteration {iteration} after {(Time.realtimeSinceStartup - startTime) * 1000:F0}ms (stringMatches={stringMatches.Count}, topMatches={topMatches.Count})");
                        await ActionComplete();
                        return topMatches[0];
                    }
                }

                await UniTask.Delay(Interval, true, PlayerLoopTiming.Update, TestCancellationToken);
            }

            TestCancellationToken.ThrowIfCancellationRequested();

            if (throwIfMissing)
                throw new System.TimeoutException($"Unable to locate {typeof(T).Name} '{string.Join(',', searches)}' in {seconds} seconds after {iteration} iterations");

            return default;
        }


        protected UniTask<IEnumerable<T>> FindAll<T>(string search, float seconds, params Type[] filterTypes)
        {
            return FindAll<T>(search, seconds, Availability.Active | Availability.Enabled, filterTypes);
        }

        protected async UniTask<IEnumerable<T>> FindAll<T>(string search = "", float seconds = 10, Availability availability = Availability.Active | Availability.Enabled, params Type[] filterTypes)
        {
            var startTime = Time.realtimeSinceStartup;

            TimeYielder yielder = new TimeYielder(TimeSpan.FromSeconds(0.1f));

            while (Application.isPlaying && !TestCancellationToken.IsCancellationRequested)
            {
                if ((Time.realtimeSinceStartup - startTime) > seconds)
                    break;

                List<Behaviour> behaviours = new List<Behaviour>();

                if (filterTypes == null || filterTypes.Length == 0)
                {
                    behaviours.AddRange(GameObject.FindObjectsByType<Behaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
                }
                else
                {
                    foreach (var filterType in filterTypes)
                        behaviours.AddRange(GameObject.FindObjectsByType(filterType, FindObjectsInactive.Exclude, FindObjectsSortMode.None).OfType<Behaviour>());
                }

                if (string.IsNullOrEmpty(search))
                    return behaviours.OfType<T>().Where(b => CheckAvailability(b as UnityEngine.Object, availability));

                List<T> stringMatches = new List<T>();

                foreach (var behaviour in behaviours)
                {
                    if (behaviour == null)
                        continue;

                    if (behaviour is T item)
                    {
                        var name = behaviour.name.ToLowerInvariant();

                        if (string.CompareOrdinal(search, name) == 0 || IsWildcardMatch(name, search))
                        {
                            stringMatches.Add(item);
                            continue;
                        }

                        var path = GetHierarchyPath(behaviour.transform).ToLower();

                        if (string.CompareOrdinal(search, path) == 0 || IsWildcardMatch(path, search))
                        {
                            stringMatches.Add(item);
                            continue;
                        }

                        if (TryGetComponentInChildren(behaviour.gameObject, out Text t2) && t2 != null)
                            if (IsWildcardMatch(t2.text != null ? t2.text.ToLowerInvariant() : "", search))
                            {
                                stringMatches.Add(item);
                                continue;
                            }

                        if (TryGetComponentInChildren(behaviour.gameObject, out TMP_Text t3) && t3 != null)
                            if (IsWildcardMatch(t3.text != null ? t3.text.ToLowerInvariant() : "", search))
                            {
                                stringMatches.Add(item);
                                continue;
                            }
                    }

                    await yielder.YieldOptional();
                }

                if (stringMatches.Count > 0)
                {
                    var results = stringMatches.Where(m => CheckAvailability(m as UnityEngine.Object, availability)).ToList();
                    if (results.Count > 0)
                        return results;
                }

                await UniTask.Delay(Interval, true, PlayerLoopTiming.Update, TestCancellationToken);
            }

            TestCancellationToken.ThrowIfCancellationRequested();
            throw new System.TimeoutException($"Unable to locate {typeof(T).Name} '{search}' in {seconds} seconds");
        }
    }
}
