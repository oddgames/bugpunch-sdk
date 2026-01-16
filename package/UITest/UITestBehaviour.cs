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

        #region Search Helper

        /// <summary>
        /// Creates a new Search query, optionally matching text content.
        /// This is a convenience method - you can also use Search.Name(), Search.Text(), etc. directly.
        /// </summary>
        /// <param name="textPattern">Optional text pattern to match (searches visible text content).</param>
        /// <returns>A new Search instance for chaining conditions.</returns>
        /// <example>await Click(Search("Play"));  // finds element with "Play" text</example>
        /// <example>await Click(Search().Name("Button*").Type&lt;Button&gt;());  // chain conditions</example>
        /// <example>await Click(new Search("Play"));  // text search via constructor</example>
        /// <example>await Click(new Search().Type&lt;Button&gt;().Text("Submit"));  // chained</example>
        protected static Search Search(string textPattern = null) => new Search(textPattern);

        /// <summary>
        /// Creates a Search query that matches GameObjects by name. Supports * wildcards for pattern matching.
        /// </summary>
        /// <param name="pattern">The name pattern to match. Use * as wildcard (e.g., "Button*" matches "ButtonPlay", "ButtonExit").</param>
        /// <returns>A new Search instance for chaining additional conditions.</returns>
        /// <example>
        /// // Find and click a button by name
        /// await Click(Name("PlayButton"));
        ///
        /// // Find all buttons starting with "Tab"
        /// var tabs = await FindAll&lt;Button&gt;(Name("Tab*"));
        /// </example>
        protected static Search Name(string pattern) => new Search().Name(pattern);

        /// <summary>
        /// Creates a Search query that matches GameObjects by visible text content (TMP_Text or legacy Text).
        /// Supports * wildcards for pattern matching.
        /// </summary>
        /// <param name="pattern">The text pattern to match. Use * as wildcard (e.g., "*Settings*" matches "Game Settings").</param>
        /// <returns>A new Search instance for chaining additional conditions.</returns>
        /// <example>
        /// // Click a button with specific text
        /// await Click(Text("Play"));
        ///
        /// // Find all elements containing "Level"
        /// var levelElements = await FindAll&lt;Button&gt;(Text("*Level*"));
        /// </example>
        protected static Search Text(string pattern) => new Search().Text(pattern);

        /// <summary>
        /// Creates a Search query that matches GameObjects having a specific component type.
        /// </summary>
        /// <typeparam name="T">The component type to search for (must inherit from Component).</typeparam>
        /// <returns>A new Search instance for chaining additional conditions.</returns>
        /// <example>
        /// // Find a slider component
        /// var slider = await Find&lt;Slider&gt;(Type&lt;Slider&gt;().Name("VolumeSlider"));
        ///
        /// // Find all buttons
        /// var buttons = await FindAll&lt;Button&gt;(Type&lt;Button&gt;());
        /// </example>
        protected static Search Type<T>() where T : Component => new Search().Type<T>();

        /// <summary>
        /// Creates a Search query that matches GameObjects having a component with the specified type name.
        /// Supports * wildcards for pattern matching.
        /// </summary>
        /// <param name="typeName">The component type name to match. Use * as wildcard (e.g., "*Renderer" matches "MeshRenderer", "SpriteRenderer").</param>
        /// <returns>A new Search instance for chaining additional conditions.</returns>
        /// <example>
        /// // Find any renderer component
        /// await Click(Type("*Renderer"));
        /// </example>
        protected static Search Type(string typeName) => new Search().Type(typeName);

        /// <summary>
        /// Creates a Search query that matches GameObjects by their sprite name (Image or SpriteRenderer).
        /// Supports * wildcards for pattern matching.
        /// </summary>
        /// <param name="pattern">The texture/sprite name pattern to match. Use * as wildcard (e.g., "icon_*" matches "icon_play", "icon_settings").</param>
        /// <returns>A new Search instance for chaining additional conditions.</returns>
        /// <example>
        /// // Click an element by its texture/sprite
        /// await Click(Texture("icon_settings"));
        ///
        /// // Find all icon images
        /// var icons = await FindAll&lt;Image&gt;(Texture("icon_*"));
        /// </example>
        protected static Search Texture(string pattern) => new Search().Texture(pattern);

        /// <summary>
        /// Creates a Search query that matches GameObjects by their hierarchy path.
        /// Supports * wildcards for pattern matching.
        /// </summary>
        /// <param name="pattern">The hierarchy path pattern to match. Use * as wildcard (e.g., "Canvas/*/Button" matches buttons in any panel).</param>
        /// <returns>A new Search instance for chaining additional conditions.</returns>
        /// <example>
        /// // Find a specific element by path
        /// await Click(Path("/Canvas/MainMenu/PlayButton"));
        ///
        /// // Find all buttons in any panel
        /// var buttons = await FindAll&lt;Button&gt;(Path("Canvas/*/Button*"));
        /// </example>
        protected static Search Path(string pattern) => new Search().Path(pattern);

        /// <summary>
        /// Creates a Search query that matches GameObjects by their Unity tag.
        /// </summary>
        /// <param name="tag">The exact tag name to match (e.g., "Player", "Enemy", "Interactable").</param>
        /// <returns>A new Search instance for chaining additional conditions.</returns>
        /// <example>
        /// // Find a tagged object
        /// var player = await Find&lt;Transform&gt;(Tag("Player"));
        ///
        /// // Click an interactable object
        /// await Click(Tag("Interactable"));
        /// </example>
        protected static Search Tag(string tag) => new Search().Tag(tag);

        /// <summary>
        /// Creates a Search query that matches GameObjects by any property (name, text, sprite, or path).
        /// Supports * wildcards for pattern matching. This is useful when you don't know which property contains the value.
        /// </summary>
        /// <param name="pattern">The pattern to match against name, text, sprite, and path. Use * as wildcard.</param>
        /// <returns>A new Search instance for chaining additional conditions.</returns>
        /// <example>
        /// // Find element by any matching property
        /// await Click(Any("Submit"));
        ///
        /// // Find anything with "Settings" in any property
        /// var settings = await FindAll&lt;Selectable&gt;(Any("*Settings*"));
        /// </example>
        protected static Search Any(string pattern) => new Search().Any(pattern);

        /// <summary>
        /// Creates a Search query that finds an interactable element adjacent to a text label.
        /// Useful for finding input fields next to their labels (e.g., "Username:" label with input field to its right).
        /// </summary>
        /// <param name="textPattern">The text label pattern to find the adjacent element for.</param>
        /// <param name="direction">The direction to look for the adjacent element (Right, Left, Above, Below). Default is Right.</param>
        /// <returns>A new Search instance for chaining additional conditions.</returns>
        /// <example>
        /// // Find input field to the right of "Username:" label
        /// await Click(Adjacent("Username:"));
        ///
        /// // Find button below a header
        /// await Click(Adjacent("Game Options", Direction.Below));
        /// </example>
        protected static Search Adjacent(string textPattern, Direction direction = Direction.Right) => new Search().Adjacent(textPattern, direction);

        /// <summary>
        /// Creates a Search query that finds elements near a text label, ordered by distance.
        /// Unlike Adjacent(), Near() matches all elements in the specified direction and orders them by distance.
        /// </summary>
        /// <param name="textPattern">The text label pattern to find nearby elements for.</param>
        /// <param name="direction">Optional direction filter (Right, Left, Above, Below). If null, matches all directions.</param>
        /// <returns>A new Search instance for chaining additional conditions.</returns>
        /// <example>
        /// // Find elements below "Center Flag", closest first
        /// await Click(Near("Center Flag", Direction.Below).Text("Texture"));
        ///
        /// // Find closest element to "Settings" in any direction
        /// await Click(Near("Settings"));
        /// </example>
        protected static Search Near(string textPattern, Direction? direction = null) => new Search().Near(textPattern, direction);

        #endregion

        public class TestException : Exception
        {
            public TestException(string message) : base(message) { }
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

        /// <summary>
        /// Gets or sets the delay in milliseconds after a successful action.
        /// Default is 200ms. Increase for slower, more visible test playback.
        /// </summary>
        /// <example>
        /// UITestBehaviour.Interval = 500; // Slower playback
        /// </example>
        public static int Interval { get; set; } = 200;

        /// <summary>
        /// Gets or sets the polling interval in milliseconds during search/wait operations.
        /// Default is 100ms. This is separate from Interval to allow fast polling while still having
        /// a longer delay after successful actions.
        /// </summary>
        public static int PollInterval { get; set; } = 100;

        /// <summary>
        /// When true, increases all intervals and enables verbose logging for debugging tests.
        /// </summary>
        public static bool DebugMode { get; set; } = false;

        /// <summary>
        /// Multiplier for all intervals when DebugMode is enabled. Default is 3x.
        /// </summary>
        public static float DebugIntervalMultiplier { get; set; } = 3f;

        /// <summary>
        /// Gets the effective action interval, accounting for debug mode multiplier.
        /// </summary>
        static int EffectiveInterval => DebugMode ? (int)(Interval * DebugIntervalMultiplier) : Interval;

        /// <summary>
        /// Gets the effective poll interval, accounting for debug mode multiplier.
        /// </summary>
        static int EffectivePollInterval => DebugMode ? (int)(PollInterval * DebugIntervalMultiplier) : PollInterval;

        static void LogDebug(string message)
        {
            if (DebugMode)
                Debug.Log($"[UITEST:DEBUG] {message}");
        }

        static readonly List<Type> clickablesList = new() { typeof(Selectable) };
        static Type[] clickablesArray = { typeof(Selectable) };

        /// <summary>
        /// Gets the array of types considered clickable by the test framework.
        /// By default includes Selectable (Button, Toggle, Slider, etc.).
        /// Use RegisterClickable to add custom clickable types.
        /// </summary>
        public static Type[] Clickables => clickablesArray;

        static CancellationTokenSource testCts;

        /// <summary>
        /// Gets the cancellation token for the current test. Use this to check for test cancellation
        /// in long-running operations or to pass to async methods that support cancellation.
        /// </summary>
        protected static CancellationToken TestCancellationToken => testCts != null ? testCts.Token : CancellationToken.None;

        /// <summary>
        /// Registers a custom type as clickable, allowing Click methods to find it.
        /// Use this for custom UI systems that don't inherit from Selectable.
        /// </summary>
        /// <param name="type">The type to register as clickable.</param>
        /// <example>
        /// UITestBehaviour.RegisterClickable(typeof(MyCustomButton));
        /// </example>
        public static void RegisterClickable(Type type)
        {
            if (type != null && !clickablesList.Contains(type))
            {
                clickablesList.Add(type);
                clickablesArray = clickablesList.ToArray();
            }
        }

        /// <summary>
        /// Registers a custom type as clickable using generics.
        /// </summary>
        /// <typeparam name="T">The type to register as clickable.</typeparam>
        /// <example>
        /// UITestBehaviour.RegisterClickable&lt;MyCustomButton&gt;();
        /// </example>
        public static void RegisterClickable<T>() => RegisterClickable(typeof(T));

        /// <summary>
        /// Unregisters a type from being considered clickable.
        /// </summary>
        /// <param name="type">The type to unregister.</param>
        public static void UnregisterClickable(Type type)
        {
            if (clickablesList.Remove(type))
                clickablesArray = clickablesList.ToArray();
        }

        /// <summary>
        /// Unregisters a type from being considered clickable using generics.
        /// </summary>
        /// <typeparam name="T">The type to unregister.</typeparam>
        public static void UnregisterClickable<T>() => UnregisterClickable(typeof(T));

        /// <summary>
        /// Stops the currently running test by cancelling its cancellation token.
        /// Call this to abort a test from outside the test method.
        /// </summary>
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

        /// <summary>
        /// Called at the end of each action to wait for the interval before continuing.
        /// </summary>
        static async UniTask ActionComplete()
        {

            // Always yield at least one frame to allow Unity to process events
            await UniTask.Yield(PlayerLoopTiming.Update, TestCancellationToken);

            // Then wait for the remaining interval time (uses EffectiveInterval for debug mode)
            int delay = EffectiveInterval;
            LogDebug($"ActionComplete: waiting {delay}ms");
            await UniTask.Delay(delay, true, PlayerLoopTiming.Update, TestCancellationToken);
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

        /// <summary>
        /// Checks if an object meets the availability requirements specified by a Search query.
        /// By default (no IncludeInactive/IncludeDisabled), requires active and enabled.
        /// </summary>
        static bool CheckAvailability(UnityEngine.Object obj, Search search)
        {
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

            // Check active state (unless IncludeInactive is set)
            if (search == null || !search.ShouldIncludeInactive)
            {
                if (!go.activeInHierarchy)
                    return false;
            }

            // Check enabled/interactable state (unless IncludeDisabled is set)
            if (search == null || !search.ShouldIncludeDisabled)
            {
                if (behaviour != null && !behaviour.enabled)
                    return false;

                var canvasGroup = go.GetComponentInParent<CanvasGroup>();
                if (canvasGroup != null && (canvasGroup.alpha <= 0 || !canvasGroup.interactable))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Gets the scenario number for this test from the [UITest] attribute.
        /// Throws if the attribute is missing or the scenario is invalid.
        /// </summary>
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

        /// <summary>
        /// Gets whether a UI test is currently active/running.
        /// </summary>
        public static bool Active => testScenario != 0;

        static int testScenario = 0;
        static string lastKnownScene;
        static float lastSceneChangeTime;

        /// <summary>
        /// Captures a screenshot and saves it to the temporary cache path.
        /// The screenshot is logged with its full path for easy access.
        /// </summary>
        /// <param name="name">Base name for the screenshot file. Timestamp will be appended.</param>
        /// <example>
        /// CaptureScreenshot("before_click");
        /// await Click("PlayButton");
        /// CaptureScreenshot("after_click");
        /// </example>
        protected void CaptureScreenshot(string name = "screenshot")
        {
            string path = System.IO.Path.Combine(Application.temporaryCachePath, $"{name}_{DateTime.Now.Ticks}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[UITEST] Screenshot: {path}");
        }

        /// <summary>
        /// Attaches JSON data to the test report by logging it.
        /// Useful for capturing test state or debugging information.
        /// </summary>
        /// <param name="name">A descriptive name for the JSON attachment.</param>
        /// <param name="data">The object to serialize to JSON.</param>
        /// <example>
        /// AttachJson("PlayerState", new { health = 100, position = player.transform.position });
        /// </example>
        protected void AttachJson(string name, object data)
        {
            string json = JsonUtility.ToJson(data, true);
            Debug.Log($"[UITEST] Attach JSON '{name}': {json}");
        }

        /// <summary>
        /// Attaches text content to the test report by logging it.
        /// Useful for capturing debug messages or custom information.
        /// </summary>
        /// <param name="name">A descriptive name for the text attachment.</param>
        /// <param name="content">The text content to attach.</param>
        /// <example>
        /// AttachText("CurrentScene", SceneManager.GetActiveScene().name);
        /// </example>
        protected void AttachText(string name, string content)
        {
            Debug.Log($"[UITEST] Attach Text '{name}': {content}");
        }

        /// <summary>
        /// Attaches a file to the test report by logging its path and MIME type.
        /// The file must exist at the specified path.
        /// </summary>
        /// <param name="name">A descriptive name for the file attachment.</param>
        /// <param name="filePath">The absolute path to the file.</param>
        /// <param name="mimeType">The MIME type of the file (e.g., "image/png", "text/plain").</param>
        /// <example>
        /// AttachFile("SaveData", Path.Combine(Application.persistentDataPath, "save.json"), "application/json");
        /// </example>
        protected void AttachFile(string name, string filePath, string mimeType)
        {
            if (File.Exists(filePath))
            {
                Debug.Log($"[UITEST] Attach File '{name}': {filePath} ({mimeType})");
            }
        }

        /// <summary>
        /// Attaches a file to the test report by logging its path.
        /// The MIME type is inferred from the file extension.
        /// </summary>
        /// <param name="filePath">The absolute path to the file to attach.</param>
        protected void AttachFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                Debug.Log($"[UITEST] Attach File: {filePath}");
            }
        }

        /// <summary>
        /// Adds a named parameter to the test report.
        /// Useful for tracking test configuration or input values.
        /// </summary>
        /// <param name="name">The parameter name.</param>
        /// <param name="value">The parameter value.</param>
        /// <example>
        /// AddParameter("Difficulty", "Hard");
        /// AddParameter("PlayerLevel", "10");
        /// </example>
        protected void AddParameter(string name, string value)
        {
            Debug.Log($"[UITEST] Parameter: {name}={value}");
        }

        /// <summary>
        /// Gets the name of the current test run. Generated from the test class name and timestamp
        /// when the test starts (e.g., "MyTest_20260113_143052").
        /// </summary>
        public static string TestRunName { get; private set; } = "";

#if UNITY_EDITOR
        [ContextMenu("Run Test")]
        void RunTest()
        {
            StartTest(clearData: false, testDataPath: null, debugMode: false);
        }

        [ContextMenu("Run Test (Debug)")]
        void RunTestDebug()
        {
            StartTest(clearData: false, testDataPath: null, debugMode: true);
        }

        [ContextMenu("Run Test (Clear Data)")]
        void RunTestClearData()
        {
            StartTest(clearData: true, testDataPath: null, debugMode: false);
        }

        [ContextMenu("Run Test with Data Folder...")]
        void RunTestWithDataFolder()
        {
            string folder = UnityEditor.EditorUtility.OpenFolderPanel("Select Test Data Folder", Application.persistentDataPath, "data");
            if (!string.IsNullOrEmpty(folder))
            {
                StartTest(clearData: false, testDataPath: folder, debugMode: false);
            }
        }

        [ContextMenu("Run Test with Data Zip...")]
        void RunTestWithDataZip()
        {
            string zipPath = UnityEditor.EditorUtility.OpenFilePanel("Select Test Data Zip", Application.persistentDataPath, "zip");
            if (!string.IsNullOrEmpty(zipPath))
            {
                StartTest(clearData: false, testDataPath: zipPath, debugMode: false);
            }
        }

        void StartTest(bool clearData, string testDataPath, bool debugMode)
        {
            int scenario = Scenario;
            TestRunName = GetType().Name + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            if (clearData)
            {
                try
                {
                    string folder = System.IO.Path.Combine(Application.persistentDataPath, "data");
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
            UnityEditor.SessionState.SetBool("GAME_LOOP_TEST_DEBUG", debugMode);
            UnityEditor.EditorApplication.isPlaying = true;
        }

        string GetTestDataPath()
        {
            string testFolder = System.IO.Path.Combine(Application.dataPath, "UITestBehaviours", "GeneratedTests");
            string testName = GetType().Name;

            if (!Directory.Exists(testFolder))
                return null;

            var directories = Directory.GetDirectories(testFolder, "*", SearchOption.TopDirectoryOnly);
            foreach (var dir in directories)
            {
                string zipPath = System.IO.Path.Combine(dir, "testdata.zip");
                if (File.Exists(zipPath))
                {
                    string scriptPath = System.IO.Path.Combine(dir, $"{testName}.cs");
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

            string targetPath = System.IO.Path.Combine(Application.persistentDataPath, "data");

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

            string zipPath = System.IO.Path.Combine(streamingPath, "UITestData", $"{testName}.zip");
            if (File.Exists(zipPath))
                return zipPath;

            string folderPath = System.IO.Path.Combine(streamingPath, "UITestData", testName);
            if (Directory.Exists(folderPath))
                return folderPath;

            return null;
        }

        static void CopyDirectoryRuntime(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string targetFile = System.IO.Path.Combine(targetDir, System.IO.Path.GetFileName(file));
                File.Copy(file, targetFile, true);
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string targetSubDir = System.IO.Path.Combine(targetDir, System.IO.Path.GetFileName(dir));
                CopyDirectoryRuntime(dir, targetSubDir);
            }
        }

        async void Start()
        {
            await UniTask.Yield();

#if UNITY_EDITOR
            if (UnityEditor.SessionState.GetBool("GAME_LOOP_TEST", false))
            {
                testScenario = UnityEditor.SessionState.GetInt("GAME_LOOP_TEST_SCENARIO", 0);
                TestRunName = UnityEditor.SessionState.GetString("GAME_LOOP_TEST_NAME", "");
                DebugMode = UnityEditor.SessionState.GetBool("GAME_LOOP_TEST_DEBUG", false);
            }

            UnityEditor.SessionState.SetBool("GAME_LOOP_TEST", false);
            UnityEditor.SessionState.SetBool("GAME_LOOP_TEST_DEBUG", false);
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

                if (DebugMode)
                {
                    Debug.Log($"[UITEST] Test Start (DEBUG MODE): {GetType().Name}");
                    Debug.Log($"[UITEST:DEBUG] Interval: {Interval}ms x {DebugIntervalMultiplier} = {EffectiveInterval}ms");
                }
                else
                {
                    Debug.Log($"[UITEST] Test Start: {GetType().Name}");
                }

                // Wait for editor to be fully in play mode and scene to initialize
                await WaitForPlayModeReady();
                await UniTask.Delay(1000, true, PlayerLoopTiming.Update, TestCancellationToken);

                float testStartTime = Time.realtimeSinceStartup;
                try
                {
                    await Test();
                    float duration = Time.realtimeSinceStartup - testStartTime;
                    Debug.Log($"[UITEST] Test PASSED: {GetType().Name} Duration: {duration:F2}s");
                }
                catch (OperationCanceledException)
                {
                    float duration = Time.realtimeSinceStartup - testStartTime;
                    Debug.Log($"[UITEST] Test CANCELLED: {GetType().Name} Duration: {duration:F2}s");
                }
                catch (Exception ex)
                {
                    float duration = Time.realtimeSinceStartup - testStartTime;
                    Debug.LogException(ex);
                    Debug.Log($"[UITEST] Test FAILED: {GetType().Name} Duration: {duration:F2}s");
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

        /// <summary>
        /// Gets the test scenario information from Android Test Loop intent.
        /// Used for Firebase Test Lab integration on Android.
        /// </summary>
        /// <param name="test">Output: The scenario number from the test intent.</param>
        /// <param name="logFile">Output: The log file path from the test intent.</param>
        /// <returns>True if running as a Firebase Test Loop scenario, false otherwise.</returns>
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
        /// <summary>
        /// Override this method to implement your test logic.
        /// This is the main entry point for your UI test.
        /// </summary>
        /// <returns>A UniTask that completes when the test finishes.</returns>
        /// <example>
        /// protected override async UniTask Test()
        /// {
        ///     await Click("PlayButton");
        ///     await Wait(1);
        ///     await Click("SettingsButton");
        ///     Assert(/* condition */, "Expected condition to be true");
        /// }
        /// </example>
        protected abstract UniTask Test();

        /// <summary>
        /// Asserts that a condition is true. Throws TestException if the condition is false.
        /// Use this to verify expected test outcomes.
        /// </summary>
        /// <param name="condition">The condition to evaluate. Test fails if this is false.</param>
        /// <param name="message">The error message to include in the exception if assertion fails.</param>
        /// <exception cref="TestException">Thrown when the condition is false.</exception>
        /// <example>
        /// var button = await Find&lt;Button&gt;(Name("PlayButton"));
        /// Assert(button != null, "Play button should exist");
        /// Assert(button.interactable, "Play button should be interactable");
        /// </example>
        protected void Assert(bool condition, string message = "Assertion failed")
        {
            if (!condition)
            {
                throw new TestException(message);
            }
        }

        /// <summary>
        /// Waits for the specified duration before continuing the test.
        /// </summary>
        /// <param name="seconds">The number of seconds to wait. Default is 1 second.</param>
        /// <returns>A UniTask that completes after the specified duration.</returns>
        /// <example>
        /// await Click("PlayButton");
        /// await Wait(2); // Wait 2 seconds for animation
        /// await Click("NextButton");
        /// </example>
        protected async UniTask Wait(float seconds = 1f)
        {
            LogDebug($"Wait: {seconds}s");
            await UniTask.Delay((int)(seconds * 1000), true, PlayerLoopTiming.Update, TestCancellationToken);
            await ActionComplete();
        }

        /// <summary>
        /// Waits until an element matching the search query appears on screen.
        /// Throws TimeoutException if the element is not found within the specified time.
        /// </summary>
        /// <param name="search">The search query to match elements against.</param>
        /// <param name="seconds">Maximum time to wait in seconds. Default is 10 seconds.</param>
        /// <returns>A UniTask that completes when the element is found.</returns>
        /// <exception cref="TimeoutException">Thrown when element is not found within timeout.</exception>
        /// <example>
        /// await Click("LoadGameButton");
        /// await Wait(Name("GameScene"), 30); // Wait for game to load
        /// </example>
        protected async UniTask Wait(Search search, int seconds = 10)
        {
            Debug.Log($"[UITEST] Wait ({seconds}s)");
            LogDebug($"Wait: with {seconds}s timeout");
            await Find<Transform>(search, true, seconds);
        }

        /// <summary>
        /// Waits until a custom condition becomes true.
        /// Throws TimeoutException if the condition is not met within the specified time.
        /// </summary>
        /// <param name="condition">A function that returns true when the wait should complete.</param>
        /// <param name="seconds">Maximum time to wait in seconds. Default is 60 seconds.</param>
        /// <param name="description">A description of the condition for logging purposes.</param>
        /// <returns>A UniTask that completes when the condition is true.</returns>
        /// <exception cref="TimeoutException">Thrown when condition is not met within timeout.</exception>
        /// <example>
        /// // Wait until player health is full
        /// await WaitFor(() => player.health >= 100, 30, "player health to reach 100");
        ///
        /// // Wait until loading is complete
        /// await WaitFor(() => loadingScreen == null, 60, "loading to complete");
        /// </example>
        protected async UniTask WaitFor(Func<bool> condition, float seconds = 60, string description = "condition")
        {
            Debug.Log($"[UITEST] WaitFor ({seconds}) [{description}]");
            LogDebug($"WaitFor: '{description}' with {seconds}s timeout");

            var startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying && !TestCancellationToken.IsCancellationRequested)
            {
                if (condition())
                {
                    LogDebug($"WaitFor: '{description}' satisfied after {Time.realtimeSinceStartup - startTime:F2}s");
                    await ActionComplete();
                    return;
                }

                await UniTask.Delay(EffectivePollInterval, true, PlayerLoopTiming.Update, TestCancellationToken);
            }

            TestCancellationToken.ThrowIfCancellationRequested();
            throw new System.TimeoutException($"Condition '{description}' not met within {seconds} seconds");
        }

        /// <summary>
        /// Waits until the scene changes from the current scene.
        /// Can detect recently occurred scene changes within recentThreshold seconds.
        /// </summary>
        /// <param name="seconds">Maximum time to wait in seconds. Default is 30 seconds.</param>
        /// <param name="recentThreshold">If scene changed within this many seconds before call, returns immediately. Default is 1 second.</param>
        /// <returns>A UniTask that completes when the scene changes.</returns>
        /// <exception cref="TimeoutException">Thrown when scene does not change within timeout.</exception>
        /// <example>
        /// await Click("StartGameButton");
        /// await SceneChange(60); // Wait for scene to load (up to 60 seconds)
        /// </example>
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

                await UniTask.Delay(EffectivePollInterval, true, PlayerLoopTiming.Update, TestCancellationToken);
            }

            TestCancellationToken.ThrowIfCancellationRequested();
            throw new System.TimeoutException($"Scene did not change from '{startScene}' within {seconds} seconds");
        }

        /// <summary>
        /// Waits until the game achieves a target framerate. Useful for waiting until loading is complete
        /// or GPU-intensive operations finish. Samples framerate over a duration to get accurate average.
        /// </summary>
        /// <param name="averageFps">The target average FPS to wait for.</param>
        /// <param name="sampleDuration">Duration in seconds to sample framerate. Default is 2 seconds.</param>
        /// <param name="timeout">Maximum time to wait in seconds. Default is 60 seconds.</param>
        /// <returns>A UniTask that completes when target framerate is achieved.</returns>
        /// <exception cref="TimeoutException">Thrown when framerate target is not reached within timeout.</exception>
        /// <example>
        /// await Click("LoadLevelButton");
        /// await WaitFramerate(30, 2f, 120); // Wait until 30 FPS sustained for 2 seconds
        /// </example>
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
        /// Enters text into an input field using realistic input injection.
        /// Clicks the input field to focus it, then types each character.
        /// Supports both TMP_InputField and legacy InputField components.
        /// </summary>
        /// <param name="search">Search query to find the input field.</param>
        /// <param name="input">The text to type into the input field.</param>
        /// <param name="seconds">Maximum time in seconds to search for the input field. Default is 10 seconds.</param>
        /// <param name="pressEnter">If true, presses Enter after typing the text. Useful for submitting forms.</param>
        /// <returns>A UniTask that completes when text entry is finished.</returns>
        /// <example>
        /// // Type a username and press Enter to submit
        /// await TextInput(Name("UsernameInput"), "Player123", pressEnter: true);
        ///
        /// // Type into a search field
        /// await TextInput(Adjacent("Search:"), "sword");
        /// </example>
        protected async UniTask TextInput(Search search, string input, float seconds = 10, bool pressEnter = false)
        {
            Debug.Log($"[UITEST] TextInput ({seconds}s) '{input}' search={search}");

            // Try to find TMP_InputField or legacy InputField
            Debug.Log($"[UITEST] TextInput - searching for TMP_InputField...");
            var tmpInput = await Find<TMP_InputField>(search, false, 0.1f);
            if (tmpInput != null)
            {
                Debug.Log($"[UITEST] TextInput - found TMP_InputField: {tmpInput.name}");
                await InputInjector.TypeIntoField(tmpInput.gameObject, input, clearFirst: true, pressEnter: pressEnter);
                await ActionComplete();
                return;
            }
            Debug.Log($"[UITEST] TextInput - TMP_InputField not found, searching for legacy InputField...");

            var legacyInput = await Find<InputField>(search, true, seconds);
            Debug.Log($"[UITEST] TextInput - found InputField: {legacyInput.name}");
            await InputInjector.TypeIntoField(legacyInput.gameObject, input, clearFirst: true, pressEnter: pressEnter);
            await ActionComplete();
        }

        /// <summary>
        /// Delegate to get the currently focused object from custom UI systems.
        /// Used by RegisterFocusedObjectGetter to integrate with non-Unity UI systems.
        /// </summary>
        /// <returns>The currently focused GameObject, or null if none.</returns>
        public delegate GameObject FocusedObjectGetter();

        static readonly List<FocusedObjectGetter> focusedObjectGetters = new();

        /// <summary>
        /// Registers a getter for the currently focused object in a custom UI system.
        /// Call this to integrate non-Unity UI frameworks (e.g., EzGUI, NGUI) with PressKey methods.
        /// </summary>
        /// <param name="getter">A delegate that returns the currently focused GameObject.</param>
        /// <example>
        /// // Integration with a custom UI system
        /// UITestBehaviour.RegisterFocusedObjectGetter(() => MyUIManager.FocusedObject);
        /// </example>
        public static void RegisterFocusedObjectGetter(FocusedObjectGetter getter)
        {
            if (getter != null && !focusedObjectGetters.Contains(getter))
                focusedObjectGetters.Add(getter);
        }

        /// <summary>
        /// Simulates a key press and release. Sends the key event to the currently focused/selected UI element.
        /// Works with Unity UI (EventSystem.currentSelectedGameObject) and custom UI systems via registered handlers.
        /// </summary>
        /// <param name="key">The KeyCode to press (e.g., KeyCode.Return, KeyCode.Escape, KeyCode.A).</param>
        /// <returns>A UniTask that completes when the key press is processed.</returns>
        /// <example>
        /// await Click(Name("InputField")); // Focus input
        /// await PressKey(KeyCode.Return); // Submit
        ///
        /// await PressKey(KeyCode.Escape); // Close dialog
        /// </example>
        protected async UniTask PressKey(KeyCode key)
        {
            GameObject target = GetFocusedObject();
            Debug.Log($"[UITEST] PressKey [{key}] target='{(target != null ? target.name : "none")}'");

            await PressKeyUnityUI(target, key);
            await ActionComplete();
        }

        /// <summary>
        /// Simulates a key press on a specific UI element found by search query.
        /// First finds the element, then sends the key event to it.
        /// </summary>
        /// <param name="key">The KeyCode to press.</param>
        /// <param name="search">Search query to find the target element.</param>
        /// <param name="seconds">Maximum time in seconds to search for the element. Default is 10 seconds.</param>
        /// <returns>A UniTask that completes when the key press is processed.</returns>
        /// <example>
        /// await PressKey(KeyCode.Return, Name("SearchInput")); // Press Enter on search field
        /// </example>
        protected async UniTask PressKey(KeyCode key, Search search, float seconds = 10)
        {
            var component = await Find<Component>(search, true, seconds);
            GameObject target = component?.gameObject;
            Debug.Log($"[UITEST] PressKey [{key}] target='{(target != null ? target.name : "none")}'");

            await PressKeyUnityUI(target, key);
            await ActionComplete();
        }

        /// <summary>
        /// Simulates a key press using a character. Automatically converts the character to the appropriate KeyCode.
        /// </summary>
        /// <param name="c">The character to type (e.g., 'a', 'A', '1', ' ').</param>
        /// <returns>A UniTask that completes when the key press is processed.</returns>
        /// <example>
        /// await PressKey('a'); // Press the 'A' key
        /// await PressKey(' '); // Press spacebar
        /// </example>
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
        /// Simulates a key press using a key name string. Supports KeyCode names and common aliases.
        /// </summary>
        /// <param name="keyName">The key name (e.g., "Enter", "Space", "A", "Escape", "up", "down").</param>
        /// <returns>A UniTask that completes when the key press is processed.</returns>
        /// <example>
        /// await PressKey("Enter"); // Press Enter
        /// await PressKey("Escape"); // Press Escape
        /// await PressKey("up"); // Press Up arrow
        /// await PressKey("A"); // Press A key
        /// </example>
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
        /// Types a sequence of characters by injecting keyboard text events via the Input System.
        /// Each character is sent as a TextEvent to the currently focused input field.
        /// Use TextInput for a complete input field interaction (click + type).
        /// </summary>
        /// <param name="text">The text string to type, one character at a time.</param>
        /// <returns>A UniTask that completes when all characters have been typed.</returns>
        /// <example>
        /// await Click(Name("InputField")); // Focus the input
        /// await PressKeys("Hello World!"); // Type the text
        /// </example>
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

        #region Key Hold / Sequence

        /// <summary>
        /// Holds a key down for the specified duration, then releases it.
        /// Useful for movement controls (WASD, arrow keys) or charged attacks.
        /// </summary>
        /// <param name="key">The KeyCode to hold (e.g., KeyCode.W for forward movement).</param>
        /// <param name="duration">How long to hold the key in seconds.</param>
        /// <returns>A UniTask that completes when the key is released.</returns>
        /// <example>
        /// // Hold W key for 2 seconds (move forward)
        /// await HoldKey(KeyCode.W, 2f);
        ///
        /// // Hold shift for sprint
        /// await HoldKey(KeyCode.LeftShift, 3f);
        /// </example>
        protected async UniTask HoldKey(KeyCode key, float duration)
        {
            var inputKey = KeyCodeToKey(key);
            if (inputKey == Key.None)
            {
                Debug.LogWarning($"[UITEST] HoldKey - Unable to map KeyCode.{key} to Input System Key");
                return;
            }
            await ActionExecutor.HoldKeyAsync(inputKey, duration);
        }

        /// <summary>
        /// Holds a key down for the specified duration using Input System Key enum.
        /// </summary>
        /// <param name="key">The Input System Key to hold.</param>
        /// <param name="duration">How long to hold the key in seconds.</param>
        /// <returns>A UniTask that completes when the key is released.</returns>
        /// <example>
        /// await HoldKey(Key.W, 2f); // Move forward for 2 seconds
        /// </example>
        protected async UniTask HoldKey(Key key, float duration)
        {
            await ActionExecutor.HoldKeyAsync(key, duration);
        }

        /// <summary>
        /// Holds multiple keys down simultaneously for the specified duration.
        /// Useful for diagonal movement (W+A, W+D), sprinting (Shift+W), or key combinations.
        /// </summary>
        /// <param name="duration">How long to hold the keys in seconds.</param>
        /// <param name="keys">The KeyCode values to hold together.</param>
        /// <returns>A UniTask that completes when all keys are released.</returns>
        /// <example>
        /// // Diagonal movement (forward-left)
        /// await HoldKeys(2f, KeyCode.W, KeyCode.A);
        ///
        /// // Sprint forward
        /// await HoldKeys(3f, KeyCode.LeftShift, KeyCode.W);
        /// </example>
        protected async UniTask HoldKeys(float duration, params KeyCode[] keys)
        {
            var inputKeys = new Key[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                inputKeys[i] = KeyCodeToKey(keys[i]);
                if (inputKeys[i] == Key.None)
                {
                    Debug.LogWarning($"[UITEST] HoldKeys - Unable to map KeyCode.{keys[i]} to Input System Key");
                    return;
                }
            }
            await ActionExecutor.HoldKeysAsync(inputKeys, duration);
        }

        /// <summary>
        /// Holds multiple keys down simultaneously for the specified duration using Input System Key enum.
        /// </summary>
        /// <param name="duration">How long to hold the keys in seconds.</param>
        /// <param name="keys">The Input System Key values to hold together.</param>
        /// <returns>A UniTask that completes when all keys are released.</returns>
        protected async UniTask HoldKeys(float duration, params Key[] keys)
        {
            await ActionExecutor.HoldKeysAsync(keys, duration);
        }

        #endregion


        /// <summary>
        /// Clicks any one of the elements matching the provided search patterns.
        /// Tries each pattern in order and clicks the first matching element found.
        /// </summary>
        /// <param name="searches">Array of name/text patterns to search for.</param>
        /// <returns>A UniTask that completes when an element is clicked.</returns>
        /// <exception cref="TestException">Thrown when no matching element is found within timeout.</exception>
        /// <example>
        /// // Click whichever button appears first
        /// await ClickAny("OK", "Continue", "Next");
        /// </example>
        protected async UniTask ClickAny(params string[] searches)
        {
            await ClickAny(searches, throwIfMissing: true, seconds: 5);
        }

        /// <summary>
        /// Clicks any one of the elements matching the provided search patterns.
        /// Tries each pattern in order and clicks the first matching element found.
        /// </summary>
        /// <param name="searches">Array of name/text patterns to search for.</param>
        /// <param name="throwIfMissing">If true, throws an exception when no element is found. Default is true.</param>
        /// <param name="seconds">Maximum time in seconds to search. Default is 5 seconds.</param>
        /// <returns>A UniTask that completes when an element is clicked.</returns>
        /// <exception cref="TestException">Thrown when no matching element is found and throwIfMissing is true.</exception>
        protected async UniTask ClickAny(string[] searches, bool throwIfMissing = true, float seconds = 5)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying)
            {
                // Try each search pattern
                foreach (var searchPattern in searches)
                {
                    var b = await Find<IPointerClickHandler>(new Search().Any(searchPattern), throwIfMissing: false, seconds: 0.1f);

                    if (b != null && b is UnityEngine.Component c)
                    {
                        await ActionExecutor.ClickAsync(c.gameObject);
                        await ActionComplete();
                        return;
                    }
                }

                await UniTask.Delay(100, true);
            }

            if (throwIfMissing)
            {
                throw new TestException($"ClickAny on '{string.Join(", ", searches)}' could not find any matching target within {seconds}s");
            }
        }

        /// <summary>
        /// Performs a long-press (hold) gesture on an element found by search.
        /// Holds the pointer down for the specified duration, then releases.
        /// </summary>
        /// <param name="search">Search query to find the element to hold.</param>
        /// <param name="seconds">Duration in seconds to hold the element.</param>
        /// <param name="throwIfMissing">If true, throws an exception when element is not found. Default is true.</param>
        /// <param name="searchTime">Maximum time in seconds to search for the element. Default is 10 seconds.</param>
        /// <returns>A UniTask that completes when the hold gesture is finished.</returns>
        /// <exception cref="TestException">Thrown when element is not found and throwIfMissing is true.</exception>
        /// <example>
        /// // Long-press to show context menu
        /// await Hold(Name("InventoryItem"), 1.5f);
        ///
        /// // Hold a button for charged action
        /// await Hold(Name("ChargeButton"), 3f);
        /// </example>
        protected async UniTask Hold(Search search, float seconds, bool throwIfMissing = true, float searchTime = 10)
        {
            bool found = await ActionExecutor.HoldAsync(search, seconds, searchTime);

            if (found)
            {
                await ActionComplete();
            }
            else if (throwIfMissing)
            {
                throw new TestException($"Hold({search}) could not find any matching target within {searchTime}s");
            }
        }

        /// <summary>
        /// Clicks on a UI element matching the Search query using realistic input injection.
        /// This is the primary click method for UI testing.
        /// </summary>
        /// <param name="search">Search query to find the clickable element. Supports implicit string conversion for text search.</param>
        /// <param name="throwIfMissing">If true, throws an exception when element is not found. Default is true.</param>
        /// <param name="searchTime">Maximum time in seconds to search for the element. Default is 10 seconds.</param>
        /// <param name="repeat">Number of additional times to repeat the click. 0 = click once, 1 = click twice, etc.</param>
        /// <param name="index">Index of the element to click when multiple elements match (0-based). Default is 0 (first match).</param>
        /// <returns>A UniTask that completes when the click is performed.</returns>
        /// <exception cref="TestException">Thrown when element is not found and throwIfMissing is true.</exception>
        /// <example>
        /// // Click by text (implicit conversion)
        /// await Click("Play");
        ///
        /// // Click by name pattern
        /// await Click(Name("Button*"));
        ///
        /// // Click with options
        /// await Click(Text("Submit"), throwIfMissing: false, searchTime: 5);
        ///
        /// // Click the second matching element
        /// await Click(Name("Tab*"), index: 1);
        ///
        /// // Triple-click (select all text)
        /// await Click(Name("InputField"), repeat: 2);
        /// </example>
        protected async UniTask Click(Search search, bool throwIfMissing = true, float searchTime = 10, int repeat = 0, int index = 0)
        {
            do
            {
                bool found = await ActionExecutor.ClickAsync(search, searchTime, index);

                if (found)
                {
                    await ActionComplete();
                }
                else if (throwIfMissing)
                {
                    string indexMsg = index > 0 ? $" at index {index}" : "";
                    throw new TestException($"Click({search}){indexMsg} could not find any matching target within {searchTime}s");
                }

                repeat--;
            }
            while (repeat > 0);
        }

        /// <summary>
        /// Clicks at the center of the screen. Useful for dismissing dialogs or advancing tutorials.
        /// </summary>
        /// <param name="throwIfMissing">Unused parameter for API consistency.</param>
        /// <returns>A UniTask that completes when the click is performed.</returns>
        /// <example>
        /// // Click center of screen to dismiss
        /// await Click();
        /// </example>
        protected async UniTask Click(bool throwIfMissing = true)
        {
            await ClickAt(0.5f, 0.5f);
        }

        /// <summary>
        /// Clicks at a screen position specified as percentages (0-1).
        /// Useful for clicking at fixed screen positions regardless of resolution.
        /// </summary>
        /// <param name="xPercent">X position as percentage of screen width (0 = left, 0.5 = center, 1 = right).</param>
        /// <param name="yPercent">Y position as percentage of screen height (0 = bottom, 0.5 = center, 1 = top).</param>
        /// <returns>A UniTask that completes when the click is performed.</returns>
        /// <example>
        /// // Click at screen center
        /// await ClickAt(0.5f, 0.5f);
        ///
        /// // Click in top-right area
        /// await ClickAt(0.9f, 0.9f);
        /// </example>
        protected async UniTask ClickAt(float xPercent, float yPercent)
        {
            Vector2 pos = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            Debug.Log($"[UITEST] ClickAt ({xPercent:P0}, {yPercent:P0}) at ({pos.x:F0}, {pos.y:F0})");

            await ActionExecutor.ClickAtAsync(pos);
            await ActionComplete();
        }

        /// <summary>
        /// Clicks on a specific GameObject. Useful when you already have a reference to the target.
        /// </summary>
        /// <param name="target">The GameObject to click on.</param>
        /// <returns>A UniTask that completes when the click is performed.</returns>
        /// <example>
        /// // Click on a specific button reference
        /// await Click(submitButton.gameObject);
        /// </example>
        protected async UniTask Click(GameObject target)
        {
            await ActionExecutor.ClickAsync(target);
            await ActionComplete();
        }

        /// <summary>
        /// Performs a double-click at a screen position specified as percentages (0-1).
        /// Useful for selecting text, zooming, or triggering double-click actions.
        /// </summary>
        /// <param name="xPercent">X position as percentage of screen width (0 = left, 0.5 = center, 1 = right).</param>
        /// <param name="yPercent">Y position as percentage of screen height (0 = bottom, 0.5 = center, 1 = top).</param>
        /// <returns>A UniTask that completes when the double-click is performed.</returns>
        /// <example>
        /// // Double-click at screen center
        /// await DoubleClickAt(0.5f, 0.5f);
        /// </example>
        protected async UniTask DoubleClickAt(float xPercent, float yPercent)
        {
            Vector2 pos = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            Debug.Log($"[UITEST] DoubleClickAt ({xPercent:P0}, {yPercent:P0}) at ({pos.x:F0}, {pos.y:F0})");

            await ActionExecutor.DoubleClickAtAsync(pos);
            await ActionComplete();
        }

        /// <summary>
        /// Performs a double-click on a UI element found by search.
        /// Useful for selecting text, expanding items, or triggering double-click handlers.
        /// </summary>
        /// <param name="search">Search query to find the element.</param>
        /// <param name="throwIfMissing">If true, throws an exception when element is not found. Default is true.</param>
        /// <param name="searchTime">Maximum time in seconds to search for the element. Default is 10 seconds.</param>
        /// <returns>A UniTask that completes when the double-click is performed.</returns>
        /// <example>
        /// // Double-click to select all text in an input field
        /// await DoubleClick(Name("InputField"));
        ///
        /// // Double-click to expand a tree node
        /// await DoubleClick(Name("TreeItem"));
        /// </example>
        protected async UniTask DoubleClick(Search search, bool throwIfMissing = true, float searchTime = 10)
        {
            bool found = await ActionExecutor.DoubleClickAsync(search, searchTime);

            if (found)
            {
                await ActionComplete();
            }
            else if (throwIfMissing)
            {
                throw new TestException($"DoubleClick({search}) could not find any matching target within {searchTime}s");
            }
        }

        /// <summary>
        /// Performs a double-click at screen center.
        /// </summary>
        /// <returns>A UniTask that completes when the double-click is performed.</returns>
        protected async UniTask DoubleClick()
        {
            Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
            Debug.Log($"[UITEST] DoubleClick (screen center) at ({screenCenter.x:F0}, {screenCenter.y:F0})");

            await ActionExecutor.DoubleClickAtAsync(screenCenter);
            await ActionComplete();
        }

        /// <summary>
        /// Double-clicks on a specific GameObject. Useful when you already have a reference to the target.
        /// </summary>
        /// <param name="target">The GameObject to double-click on.</param>
        /// <returns>A UniTask that completes when the double-click is performed.</returns>
        /// <example>
        /// // Double-click on a specific button reference
        /// await DoubleClick(listItem.gameObject);
        /// </example>
        protected async UniTask DoubleClick(GameObject target)
        {
            await ActionExecutor.DoubleClickAsync(target);
            await ActionComplete();
        }

        /// <summary>
        /// Triple-clicks on an element found by search.
        /// Performs three rapid clicks in succession.
        /// Uses realistic mouse click injection via the Input System.
        /// </summary>
        /// <param name="search">Search query to find the element to triple-click on.</param>
        /// <param name="throwIfMissing">If true, throws an exception when element is not found. Default is true.</param>
        /// <param name="searchTime">Maximum time in seconds to search for the element. Default is 10 seconds.</param>
        /// <returns>A UniTask that completes when the triple-click is performed.</returns>
        /// <example>
        /// // Triple-click on a button
        /// await TripleClick(Name("MyButton"));
        /// </example>
        protected async UniTask TripleClick(Search search, bool throwIfMissing = true, float searchTime = 10)
        {
            bool found = await ActionExecutor.TripleClickAsync(search, searchTime);

            if (found)
            {
                await ActionComplete();
            }
            else if (throwIfMissing)
            {
                throw new TestException($"TripleClick({search}) could not find any matching target within {searchTime}s");
            }
        }

        /// <summary>
        /// Performs a triple-click at a specific screen position.
        /// Performs three rapid clicks in succession.
        /// </summary>
        /// <param name="xPercent">X position as percentage of screen width (0-1).</param>
        /// <param name="yPercent">Y position as percentage of screen height (0-1).</param>
        /// <returns>A UniTask that completes when the triple-click is performed.</returns>
        /// <example>
        /// // Triple-click at screen center
        /// await TripleClickAt(0.5f, 0.5f);
        /// </example>
        protected async UniTask TripleClickAt(float xPercent, float yPercent)
        {
            Vector2 pos = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            Debug.Log($"[UITEST] TripleClickAt ({xPercent:P0}, {yPercent:P0}) at ({pos.x:F0}, {pos.y:F0})");

            await ActionExecutor.TripleClickAtAsync(pos);
            await ActionComplete();
        }

        /// <summary>
        /// Performs a triple-click at screen center.
        /// Performs three rapid clicks in succession.
        /// </summary>
        /// <returns>A UniTask that completes when the triple-click is performed.</returns>
        protected async UniTask TripleClick()
        {
            Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
            Debug.Log($"[UITEST] TripleClick (screen center) at ({screenCenter.x:F0}, {screenCenter.y:F0})");

            await ActionExecutor.TripleClickAtAsync(screenCenter);
            await ActionComplete();
        }

        /// <summary>
        /// Triple-clicks on a specific GameObject. Useful when you already have a reference to the target.
        /// </summary>
        /// <param name="target">The GameObject to triple-click on.</param>
        /// <returns>A UniTask that completes when the triple-click is performed.</returns>
        /// <example>
        /// // Triple-click to select all text in a specific input field
        /// await TripleClick(inputField.gameObject);
        /// </example>
        protected async UniTask TripleClick(GameObject target)
        {
            await ActionExecutor.TripleClickAsync(target);
            await ActionComplete();
        }

        /// <summary>
        /// Scrolls the mouse wheel over an element found by search.
        /// Uses realistic mouse wheel input injection.
        /// </summary>
        /// <param name="search">Search query to find the element to scroll on.</param>
        /// <param name="delta">Scroll delta (positive = scroll up/zoom in, negative = scroll down/zoom out).</param>
        /// <param name="throwIfMissing">If true, throws an exception when element is not found. Default is true.</param>
        /// <param name="searchTime">Maximum time in seconds to search for the element. Default is 10 seconds.</param>
        /// <returns>A UniTask that completes when the scroll is performed.</returns>
        /// <example>
        /// // Scroll up on a list
        /// await Scroll(Name("ItemList"), 1f);
        ///
        /// // Scroll down
        /// await Scroll(Name("ScrollArea"), -2f);
        /// </example>
        protected async UniTask Scroll(Search search, float delta, bool throwIfMissing = true, float searchTime = 10)
        {
            bool found = await ActionExecutor.ScrollAsync(search, delta, searchTime);

            if (found)
            {
                await ActionComplete();
            }
            else if (throwIfMissing)
            {
                throw new TestException($"Scroll({search}) could not find any matching target within {searchTime}s");
            }
        }

        /// <summary>
        /// Scrolls the mouse wheel at screen center.
        /// </summary>
        /// <param name="delta">Scroll delta (positive = scroll up/zoom in, negative = scroll down/zoom out).</param>
        /// <returns>A UniTask that completes when the scroll is performed.</returns>
        protected async UniTask Scroll(float delta)
        {
            Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
            Debug.Log($"[UITEST] Scroll (screen center) delta={delta}");

            await ActionExecutor.ScrollAtAsync(screenCenter, delta);
            await ActionComplete();
        }

        /// <summary>
        /// Scrolls the mouse wheel at a screen position specified as percentages (0-1).
        /// </summary>
        /// <param name="xPercent">X position as percentage of screen width (0 = left, 0.5 = center, 1 = right).</param>
        /// <param name="yPercent">Y position as percentage of screen height (0 = bottom, 0.5 = center, 1 = top).</param>
        /// <param name="delta">Scroll delta (positive = scroll up/zoom in, negative = scroll down/zoom out).</param>
        /// <returns>A UniTask that completes when the scroll is performed.</returns>
        protected async UniTask ScrollAt(float xPercent, float yPercent, float delta)
        {
            Vector2 pos = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            Debug.Log($"[UITEST] ScrollAt ({xPercent:P0}, {yPercent:P0}) delta={delta} at ({pos.x:F0}, {pos.y:F0})");

            await ActionExecutor.ScrollAtAsync(pos, delta);
            await ActionComplete();
        }

        /// <summary>
        /// Performs a swipe gesture on an element in the specified direction.
        /// Uses realistic touch/mouse drag input injection.
        /// </summary>
        /// <param name="search">Search query to find the element to swipe on.</param>
        /// <param name="direction">Swipe direction (Left, Right, Up, Down).</param>
        /// <param name="distance">Swipe distance as percentage of screen height (0.2 = 20%). Default is 0.2.</param>
        /// <param name="duration">Duration of the swipe animation in seconds. Default is 0.3 seconds.</param>
        /// <param name="throwIfMissing">If true, throws an exception when element is not found. Default is true.</param>
        /// <param name="searchTime">Maximum time in seconds to search for the element. Default is 10 seconds.</param>
        /// <returns>A UniTask that completes when the swipe is finished.</returns>
        /// <example>
        /// // Swipe left on a card to dismiss
        /// await Swipe(Name("Card"), SwipeDirection.Left);
        ///
        /// // Swipe up on a scroll view to scroll down
        /// await Swipe(Name("ScrollView"), SwipeDirection.Up, 0.3f);
        /// </example>
        protected async UniTask Swipe(Search search, SwipeDirection direction, float distance = 0.2f, float duration = 1.0f, bool throwIfMissing = true, float searchTime = 10)
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

            Debug.Log($"[UITEST] Swipe {direction} duration={duration}s distance={distance:P0} ({distancePixels:F0}px)");
            LogDebug($"Swipe: delta=({delta.x:F0}, {delta.y:F0}), Screen.height={Screen.height}");

            await Drag(search, delta, duration, throwIfMissing, searchTime);
        }

        /// <summary>
        /// Performs a swipe gesture at screen center in the specified direction.
        /// </summary>
        /// <param name="direction">Swipe direction (Left, Right, Up, Down).</param>
        /// <param name="distance">Swipe distance as percentage of screen height (0.2 = 20%). Default is 0.2.</param>
        /// <param name="duration">Duration of the swipe animation in seconds. Default is 0.3 seconds.</param>
        /// <returns>A UniTask that completes when the swipe is finished.</returns>
        /// <example>
        /// // Swipe left at screen center
        /// await Swipe(SwipeDirection.Left);
        /// </example>
        protected async UniTask Swipe(SwipeDirection direction, float distance = 0.2f, float duration = 1.0f)
        {
            await SwipeAt(0.5f, 0.5f, direction, distance, duration);
        }

        /// <summary>
        /// Performs a swipe gesture at a screen position specified as percentages (0-1).
        /// </summary>
        /// <param name="xPercent">X start position as percentage of screen width (0 = left, 0.5 = center, 1 = right).</param>
        /// <param name="yPercent">Y start position as percentage of screen height (0 = bottom, 0.5 = center, 1 = top).</param>
        /// <param name="direction">Swipe direction (Left, Right, Up, Down).</param>
        /// <param name="distance">Swipe distance as percentage of screen height (0.2 = 20%). Default is 0.2.</param>
        /// <param name="duration">Duration of the swipe animation in seconds. Default is 0.3 seconds.</param>
        /// <returns>A UniTask that completes when the swipe is finished.</returns>
        /// <example>
        /// // Swipe down from top of screen
        /// await SwipeAt(0.5f, 0.9f, SwipeDirection.Down, 0.3f);
        /// </example>
        protected async UniTask SwipeAt(float xPercent, float yPercent, SwipeDirection direction, float distance = 0.2f, float duration = 1.0f)
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

            Debug.Log($"[UITEST] SwipeAt ({xPercent:P0}, {yPercent:P0}) {direction} distance={distance:P0} ({distancePixels:F0}px)");

            await DragAt(xPercent, yPercent, delta, duration);
        }

        /// <summary>
        /// Performs a two-finger pinch gesture on an element.
        /// Use for zoom in/out controls in maps, images, or other zoomable content.
        /// </summary>
        /// <param name="search">Search query to find the element to pinch on.</param>
        /// <param name="scale">Scale factor: values greater than 1 zoom in (fingers spread apart), values less than 1 zoom out (fingers pinch together).</param>
        /// <param name="duration">Duration of the gesture in seconds. Default is 0.5 seconds.</param>
        /// <param name="fingerDistance">Starting distance of each finger from center as percentage of screen height (0.05 = 5%). Default is 0.05.</param>
        /// <param name="throwIfMissing">If true, throws an exception when element is not found. Default is true.</param>
        /// <param name="searchTime">Maximum time in seconds to search for the element. Default is 10 seconds.</param>
        /// <returns>A UniTask that completes when the pinch gesture is finished.</returns>
        /// <example>
        /// // Pinch to zoom in (spread fingers)
        /// await Pinch(Name("MapView"), 1.5f);
        ///
        /// // Pinch to zoom out (pinch fingers)
        /// await Pinch(Name("ImageViewer"), 0.5f);
        /// </example>
        protected async UniTask Pinch(Search search, float scale, float duration = 1.0f, float fingerDistance = 0.05f, bool throwIfMissing = true, float searchTime = 10)
        {
            float distancePixels = fingerDistance * Screen.height;
            Debug.Log($"[UITEST] Pinch scale={scale} duration={duration}s fingerDistance={fingerDistance:P0} ({distancePixels:F0}px)");

            var target = await Find<Transform>(search, throwIfMissing, searchTime);
            if (target == null) return;

            Vector2 center = InputInjector.GetScreenPosition(target.gameObject);
            await PinchAt(center, scale, duration, distancePixels);
        }

        /// <summary>
        /// Performs a two-finger pinch gesture at screen center.
        /// </summary>
        /// <param name="scale">Scale factor: values greater than 1 zoom in (fingers spread), values less than 1 zoom out (fingers pinch).</param>
        /// <param name="duration">Duration of the gesture in seconds. Default is 0.5 seconds.</param>
        /// <param name="fingerDistance">Starting distance of each finger from center as percentage of screen height (0.05 = 5%). Default is 0.05.</param>
        /// <returns>A UniTask that completes when the pinch gesture is finished.</returns>
        protected async UniTask Pinch(float scale, float duration = 1.0f, float fingerDistance = 0.05f)
        {
            await PinchAt(0.5f, 0.5f, scale, duration, fingerDistance);
        }

        /// <summary>
        /// Performs a two-finger pinch gesture at a screen position specified as percentages (0-1).
        /// </summary>
        /// <param name="xPercent">X position as percentage of screen width (0 = left, 0.5 = center, 1 = right).</param>
        /// <param name="yPercent">Y position as percentage of screen height (0 = bottom, 0.5 = center, 1 = top).</param>
        /// <param name="scale">Scale factor: values greater than 1 zoom in (fingers spread), values less than 1 zoom out (fingers pinch).</param>
        /// <param name="duration">Duration of the gesture in seconds. Default is 0.5 seconds.</param>
        /// <param name="fingerDistance">Starting distance of each finger from center as percentage of screen height (0.05 = 5%). Default is 0.05.</param>
        /// <returns>A UniTask that completes when the pinch gesture is finished.</returns>
        protected async UniTask PinchAt(float xPercent, float yPercent, float scale, float duration = 1.0f, float fingerDistance = 0.05f)
        {
            Vector2 center = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            float distancePixels = fingerDistance * Screen.height;
            Debug.Log($"[UITEST] PinchAt ({xPercent:P0}, {yPercent:P0}) scale={scale} fingerDistance={fingerDistance:P0} ({distancePixels:F0}px)");

            await PinchAt(center, scale, duration, distancePixels);
        }

        /// <summary>
        /// Performs a pinch gesture at the specified screen position.
        /// </summary>
        private async UniTask PinchAt(Vector2 center, float scale, float duration, float fingerDistancePixels)
        {
            await ActionExecutor.PinchAtAsync(center, scale, duration, fingerDistancePixels);
            await ActionComplete();
        }

        /// <summary>
        /// Performs a two-finger swipe gesture on an element.
        /// Use for special gestures that require two simultaneous touches moving in the same direction.
        /// </summary>
        /// <param name="search">Search query to find the element to swipe on.</param>
        /// <param name="direction">Swipe direction (Left, Right, Up, Down).</param>
        /// <param name="distance">Swipe distance as percentage of screen height (0.2 = 20%). Default is 0.2.</param>
        /// <param name="duration">Duration of the gesture in seconds. Default is 0.3 seconds.</param>
        /// <param name="fingerSpacing">Distance between the two fingers as percentage of screen height (0.03 = 3%). Default is 0.03.</param>
        /// <param name="throwIfMissing">If true, throws an exception when element is not found. Default is true.</param>
        /// <param name="searchTime">Maximum time in seconds to search for the element. Default is 10 seconds.</param>
        /// <returns>A UniTask that completes when the gesture is finished.</returns>
        /// <example>
        /// // Two-finger swipe to navigate back
        /// await TwoFingerSwipe(Name("BrowserView"), SwipeDirection.Left);
        /// </example>
        protected async UniTask TwoFingerSwipe(Search search, SwipeDirection direction, float distance = 0.2f, float duration = 1.0f, float fingerSpacing = 0.03f, bool throwIfMissing = true, float searchTime = 10)
        {
            float distancePixels = distance * Screen.height;
            float spacingPixels = fingerSpacing * Screen.height;
            Debug.Log($"[UITEST] TwoFingerSwipe {direction} duration={duration}s distance={distance:P0} ({distancePixels:F0}px) spacing={fingerSpacing:P0} ({spacingPixels:F0}px)");

            var target = await Find<Transform>(search, throwIfMissing, searchTime);
            if (target == null)
            {
                Debug.LogWarning($"[UITEST] TwoFingerSwipe - target not found");
                return;
            }

            Vector2 center = InputInjector.GetScreenPosition(target.gameObject);
            await TwoFingerSwipeAt(center, direction, distancePixels, duration, spacingPixels);
        }

        /// <summary>
        /// Performs a two-finger swipe gesture at screen center.
        /// </summary>
        /// <param name="direction">Swipe direction (Left, Right, Up, Down).</param>
        /// <param name="distance">Swipe distance as percentage of screen height (0.2 = 20%). Default is 0.2.</param>
        /// <param name="duration">Duration of the gesture in seconds. Default is 0.3 seconds.</param>
        /// <param name="fingerSpacing">Distance between the two fingers as percentage of screen height (0.03 = 3%). Default is 0.03.</param>
        /// <returns>A UniTask that completes when the gesture is finished.</returns>
        protected async UniTask TwoFingerSwipe(SwipeDirection direction, float distance = 0.2f, float duration = 1.0f, float fingerSpacing = 0.03f)
        {
            await TwoFingerSwipeAt(0.5f, 0.5f, direction, distance, duration, fingerSpacing);
        }

        /// <summary>
        /// Performs a two-finger swipe gesture at a screen position specified as percentages (0-1).
        /// </summary>
        /// <param name="xPercent">X position as percentage of screen width (0 = left, 0.5 = center, 1 = right).</param>
        /// <param name="yPercent">Y position as percentage of screen height (0 = bottom, 0.5 = center, 1 = top).</param>
        /// <param name="direction">Swipe direction (Left, Right, Up, Down).</param>
        /// <param name="distance">Swipe distance as percentage of screen height (0.2 = 20%). Default is 0.2.</param>
        /// <param name="duration">Duration of the gesture in seconds. Default is 0.3 seconds.</param>
        /// <param name="fingerSpacing">Distance between the two fingers as percentage of screen height (0.03 = 3%). Default is 0.03.</param>
        /// <returns>A UniTask that completes when the gesture is finished.</returns>
        protected async UniTask TwoFingerSwipeAt(float xPercent, float yPercent, SwipeDirection direction, float distance = 0.2f, float duration = 1.0f, float fingerSpacing = 0.03f)
        {
            Vector2 center = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            float distancePixels = distance * Screen.height;
            float spacingPixels = fingerSpacing * Screen.height;
            Debug.Log($"[UITEST] TwoFingerSwipeAt ({xPercent:P0}, {yPercent:P0}) {direction} distance={distance:P0} ({distancePixels:F0}px)");

            await TwoFingerSwipeAt(center, direction, distancePixels, duration, spacingPixels);
        }

        /// <summary>
        /// Performs a two-finger swipe at the specified screen position.
        /// </summary>
        private async UniTask TwoFingerSwipeAt(Vector2 center, SwipeDirection direction, float distancePixels, float duration, float spacingPixels)
        {
            // Convert enum to string direction
            string directionStr = direction.ToString().ToLower();
            // Convert pixel values to normalized values
            float normalizedDistance = distancePixels / Screen.height;
            float normalizedSpacing = spacingPixels / Screen.height;

            await ActionExecutor.TwoFingerSwipeAtAsync(center, directionStr, normalizedDistance, duration, normalizedSpacing);
            await ActionComplete();
        }

        /// <summary>
        /// Performs a two-finger rotation gesture on an element.
        /// Use for rotating images, dials, or other rotatable UI elements.
        /// </summary>
        /// <param name="search">Search query to find the element to rotate.</param>
        /// <param name="degrees">Rotation angle in degrees (positive = clockwise, negative = counter-clockwise).</param>
        /// <param name="duration">Duration of the gesture in seconds. Default is 0.5 seconds.</param>
        /// <param name="fingerDistance">Distance of each finger from center as percentage of screen height (0.05 = 5%). Default is 0.05.</param>
        /// <param name="throwIfMissing">If true, throws an exception when element is not found. Default is true.</param>
        /// <param name="searchTime">Maximum time in seconds to search for the element. Default is 10 seconds.</param>
        /// <returns>A UniTask that completes when the rotation gesture is finished.</returns>
        /// <example>
        /// // Rotate an image 90 degrees clockwise
        /// await Rotate(Name("RotatableImage"), 90f);
        ///
        /// // Rotate counter-clockwise
        /// await Rotate(Name("Dial"), -45f);
        /// </example>
        protected async UniTask Rotate(Search search, float degrees, float duration = 1.0f, float fingerDistance = 0.05f, bool throwIfMissing = true, float searchTime = 10)
        {
            float radiusPixels = fingerDistance * Screen.height;
            Debug.Log($"[UITEST] Rotate {degrees} degrees duration={duration}s fingerDistance={fingerDistance:P0} ({radiusPixels:F0}px)");

            var target = await Find<Transform>(search, throwIfMissing, searchTime);
            if (target == null)
            {
                Debug.LogWarning($"[UITEST] Rotate - target not found");
                return;
            }

            var center = InputInjector.GetScreenPosition(target.gameObject);
            await RotateAt(center, degrees, duration, radiusPixels);
        }

        /// <summary>
        /// Performs a two-finger rotation gesture at the center of the screen.
        /// </summary>
        /// <param name="degrees">Rotation angle in degrees (positive = clockwise, negative = counter-clockwise).</param>
        /// <param name="duration">Duration of the gesture in seconds. Default is 0.5 seconds.</param>
        /// <param name="fingerDistance">Distance of each finger from center as percentage of screen height (0.05 = 5%). Default is 0.05.</param>
        /// <returns>A UniTask that completes when the rotation gesture is finished.</returns>
        protected async UniTask Rotate(float degrees, float duration = 1.0f, float fingerDistance = 0.05f)
        {
            await RotateAt(0.5f, 0.5f, degrees, duration, fingerDistance);
        }

        /// <summary>
        /// Performs a two-finger rotation gesture at a screen position specified as percentages (0-1).
        /// </summary>
        /// <param name="xPercent">X position as percentage of screen width (0 = left, 0.5 = center, 1 = right).</param>
        /// <param name="yPercent">Y position as percentage of screen height (0 = bottom, 0.5 = center, 1 = top).</param>
        /// <param name="degrees">Rotation angle in degrees (positive = clockwise, negative = counter-clockwise).</param>
        /// <param name="duration">Duration of the gesture in seconds. Default is 0.5 seconds.</param>
        /// <param name="fingerDistance">Distance of each finger from center as percentage of screen height (0.05 = 5%). Default is 0.05.</param>
        /// <returns>A UniTask that completes when the rotation gesture is finished.</returns>
        protected async UniTask RotateAt(float xPercent, float yPercent, float degrees, float duration = 1.0f, float fingerDistance = 0.05f)
        {
            var center = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            float radiusPixels = fingerDistance * Screen.height;
            Debug.Log($"[UITEST] RotateAt ({xPercent:P0}, {yPercent:P0}) {degrees} degrees fingerDistance={fingerDistance:P0} ({radiusPixels:F0}px)");

            await RotateAt(center, degrees, duration, radiusPixels);
        }

        /// <summary>
        /// Performs a rotation gesture at the specified screen position.
        /// </summary>
        private static async UniTask RotateAt(Vector2 center, float degrees, float duration, float radiusPixels)
        {
            await ActionExecutor.RotateAtPixelsAsync(center, degrees, duration, radiusPixels);
            await ActionComplete();
        }

        /// <summary>
        /// Drags from screen center in the specified direction.
        /// </summary>
        /// <param name="direction">The drag direction and distance in pixels.</param>
        /// <param name="duration">Duration of the drag animation in seconds. Default is 0.5 seconds.</param>
        /// <param name="button">Which mouse button to use for dragging. Default is left button.</param>
        /// <returns>A UniTask that completes when the drag is finished.</returns>
        /// <example>
        /// // Drag right 200 pixels from center
        /// await Drag(new Vector2(200, 0));
        ///
        /// // Drag down 100 pixels
        /// await Drag(new Vector2(0, -100));
        ///
        /// // Right-click drag for camera rotation
        /// await Drag(new Vector2(100, 0), button: PointerButton.Right);
        /// </example>
        protected async UniTask Drag(Vector2 direction, float duration = 1.0f, PointerButton button = PointerButton.Left)
        {
            Vector2 startPos = new Vector2(Screen.width / 2f, Screen.height / 2f);
            await DragFromTo(startPos, startPos + direction, duration, button);
        }

        /// <summary>
        /// Drags from a screen position specified as percentages (0-1) in the given direction.
        /// </summary>
        /// <param name="xPercent">X start position as percentage of screen width (0 = left, 1 = right).</param>
        /// <param name="yPercent">Y start position as percentage of screen height (0 = bottom, 1 = top).</param>
        /// <param name="direction">The drag direction and distance in pixels.</param>
        /// <param name="duration">Duration of the drag animation in seconds. Default is 0.5 seconds.</param>
        /// <param name="button">Which mouse button to use for dragging. Default is left button.</param>
        /// <returns>A UniTask that completes when the drag is finished.</returns>
        /// <example>
        /// // Drag from top-center downward
        /// await DragAt(0.5f, 0.9f, new Vector2(0, -200));
        /// </example>
        protected async UniTask DragAt(float xPercent, float yPercent, Vector2 direction, float duration = 1.0f, PointerButton button = PointerButton.Left)
        {
            Vector2 startPos = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            Debug.Log($"[UITEST] DragAt ({xPercent:P0}, {yPercent:P0}) delta=({direction.x:F0},{direction.y:F0}) button={button}");
            await DragFromTo(startPos, startPos + direction, duration, button);
        }

        /// <summary>
        /// Drags an element found by search in the specified direction.
        /// Useful for scrolling, swiping, or moving UI elements.
        /// </summary>
        /// <param name="search">Search query to find the element to drag from.</param>
        /// <param name="direction">The drag direction and distance in pixels.</param>
        /// <param name="duration">Duration of the drag animation in seconds. Default is 0.5 seconds.</param>
        /// <param name="throwIfMissing">If true, throws an exception when element is not found. Default is true.</param>
        /// <param name="searchTime">Maximum time in seconds to search for the element. Default is 10 seconds.</param>
        /// <param name="button">Which mouse button to use for dragging. Default is left button.</param>
        /// <returns>A UniTask that completes when the drag is finished.</returns>
        /// <example>
        /// // Scroll a list down
        /// await Drag(Name("ScrollView"), new Vector2(0, -200));
        ///
        /// // Drag an item to the right
        /// await Drag(Name("ListItem"), new Vector2(100, 0), 0.3f);
        ///
        /// // Right-click drag on a 3D object for rotation
        /// await Drag(Name("Model"), new Vector2(50, 0), button: PointerButton.Right);
        /// </example>
        protected async UniTask Drag(Search search, Vector2 direction, float duration = 1.0f, bool throwIfMissing = true, float searchTime = 10, PointerButton button = PointerButton.Left)
        {
            bool found = await ActionExecutor.DragAsync(search, direction, duration, searchTime, button: button);

            if (found)
            {
                await ActionComplete();
            }
            else if (throwIfMissing)
            {
                throw new TestException($"Drag({search}) could not find any matching target within {searchTime}s");
            }
        }

        /// <summary>
        /// Drags from one screen position to another. Low-level drag method used by other Drag methods.
        /// </summary>
        /// <param name="startPos">Start position in screen coordinates (pixels).</param>
        /// <param name="endPos">End position in screen coordinates (pixels).</param>
        /// <param name="duration">Duration of the drag animation in seconds. Default is 0.5 seconds.</param>
        /// <param name="button">Which mouse button to use for dragging. Default is left button.</param>
        /// <returns>A UniTask that completes when the drag is finished.</returns>
        /// <example>
        /// // Drag from top-left to bottom-right
        /// await DragFromTo(new Vector2(100, 500), new Vector2(300, 200), 0.5f);
        ///
        /// // Right-click drag for camera panning
        /// await DragFromTo(start, end, 0.5f, PointerButton.Right);
        /// </example>
        protected async UniTask DragFromTo(Vector2 startPos, Vector2 endPos, float duration = 1.0f, PointerButton button = PointerButton.Left)
        {
            Debug.Log($"[UITEST] DragFromTo ({duration}s) from ({startPos.x:F0},{startPos.y:F0}) to ({endPos.x:F0},{endPos.y:F0}) button={button}");

            await ActionExecutor.DragFromToAsync(startPos, endPos, duration, button: button);

            await ActionComplete();
        }

        /// <summary>
        /// Drags one element to another element (drag and drop).
        /// Finds both elements by search, then performs a drag from source to target.
        /// </summary>
        /// <param name="sourceSearch">Search query to find the element to drag.</param>
        /// <param name="targetSearch">Search query to find the drop target element.</param>
        /// <param name="duration">Duration of the drag animation in seconds. Default is 1.0 seconds.</param>
        /// <param name="throwIfMissing">If true, throws an exception when elements are not found. Default is true.</param>
        /// <param name="searchTime">Maximum time in seconds to search for each element. Default is 10 seconds.</param>
        /// <param name="holdTime">Time to hold at start position before dragging. Useful for elements that require hold-to-drag. Default is 0.5 seconds.</param>
        /// <param name="button">Which mouse button to use for dragging. Default is left button.</param>
        /// <returns>A UniTask that completes when the drag is finished.</returns>
        /// <example>
        /// // Drag an inventory item to a slot
        /// await DragTo(Name("SwordItem"), Name("EquipSlot"));
        ///
        /// // Drag and drop with custom duration
        /// await DragTo(Name("Card"), Name("DeckArea"), 0.3f);
        ///
        /// // Drag with longer hold time for hold-to-drag elements (like level editor props)
        /// await DragTo(Name("Prop"), Name("Stage"), holdTime: 1.5f);
        /// </example>
        protected async UniTask DragTo(Search sourceSearch, Search targetSearch, float duration = 1.0f, bool throwIfMissing = true, float searchTime = 10, float holdTime = 0.5f, PointerButton button = PointerButton.Left)
        {
            Debug.Log($"[UITEST] DragTo (Search, Search) timeout={searchTime}s duration={duration}s holdTime={holdTime}s button={button}");

            bool found = await ActionExecutor.DragToAsync(sourceSearch, targetSearch, duration, searchTime, holdTime, button);

            if (found)
            {
                await ActionComplete();
            }
            else if (throwIfMissing)
            {
                throw new TestException($"DragTo (Search, Search) could not find source or target within {searchTime}s");
            }
        }

        /// <summary>
        /// Drags an element found by search to a screen position specified as percentages (0-1).
        /// </summary>
        /// <param name="sourceSearch">Search query for the element to drag</param>
        /// <param name="targetPercent">Target position as screen percentage (0,0 = bottom-left, 1,1 = top-right)</param>
        /// <param name="duration">Duration of the drag animation</param>
        /// <param name="throwIfMissing">Whether to throw if element not found</param>
        /// <param name="searchTime">Timeout for finding the element</param>
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        /// <param name="button">Which mouse button to use for dragging</param>
        protected async UniTask DragTo(Search sourceSearch, Vector2 targetPercent, float duration = 1.0f, bool throwIfMissing = true, float searchTime = 10, float holdTime = 0.5f, PointerButton button = PointerButton.Left)
        {
            var source = await Find<RectTransform>(sourceSearch, throwIfMissing, searchTime);
            if (source == null) return;

            Vector2 targetPos = new Vector2(targetPercent.x * Screen.width, targetPercent.y * Screen.height);

            Debug.Log($"[UITEST] DragTo '{source.name}' -> ({targetPercent.x:P0}, {targetPercent.y:P0}) holdTime={holdTime}s button={button}");

            await ActionExecutor.DragToAsync(source.gameObject, targetPos, duration, holdTime, button);
            await ActionComplete();
        }

        /// <summary>
        /// Drags an element found by name to a screen position specified as percentages (0-1).
        /// </summary>
        /// <param name="button">Which mouse button to use for dragging</param>
        protected async UniTask DragTo(string sourceName, Vector2 targetPercent, float duration = 1.0f, bool throwIfMissing = true, float searchTime = 10, float holdTime = 0.5f, PointerButton button = PointerButton.Left)
        {
            await DragTo(new Search().Name(sourceName), targetPercent, duration, throwIfMissing, searchTime, holdTime, button);
        }

        /// <summary>
        /// Drags from a screen position (percentages 0-1) to an element found by search.
        /// </summary>
        /// <param name="sourcePercent">Source position as screen percentage (0,0 = bottom-left, 1,1 = top-right)</param>
        /// <param name="targetSearch">Search query for the target element</param>
        /// <param name="duration">Duration of the drag animation</param>
        /// <param name="throwIfMissing">Whether to throw if element not found</param>
        /// <param name="searchTime">Timeout for finding the element</param>
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        /// <param name="button">Which mouse button to use for dragging</param>
        protected async UniTask DragTo(Vector2 sourcePercent, Search targetSearch, float duration = 1.0f, bool throwIfMissing = true, float searchTime = 10, float holdTime = 0.5f, PointerButton button = PointerButton.Left)
        {
            var target = await Find<RectTransform>(targetSearch, throwIfMissing, searchTime);
            if (target == null) return;

            Vector2 sourcePos = new Vector2(sourcePercent.x * Screen.width, sourcePercent.y * Screen.height);
            Vector2 targetPos = InputInjector.GetScreenPosition(target.gameObject);

            Debug.Log($"[UITEST] DragTo ({sourcePercent.x:P0}, {sourcePercent.y:P0}) -> '{target.name}' holdTime={holdTime}s button={button}");

            await InputInjector.InjectPointerDrag(sourcePos, targetPos, duration, holdTime, button);
            await ActionComplete();
        }

        /// <summary>
        /// Clicks on a slider at a percentage position (0-1) of its visible area.
        /// Automatically handles horizontal (LeftToRight, RightToLeft) and vertical (TopToBottom, BottomToTop) sliders.
        /// </summary>
        /// <param name="search">Search query to find the Slider component.</param>
        /// <param name="percent">Position to click as percentage (0 = left/bottom, 1 = right/top). Clamped to 0-1.</param>
        /// <param name="throwIfMissing">If true, throws an exception when slider is not found. Default is true.</param>
        /// <param name="searchTime">Maximum time in seconds to search for the slider. Default is 10 seconds.</param>
        /// <returns>A UniTask that completes when the click is performed.</returns>
        /// <example>
        /// // Set volume to 75%
        /// await ClickSlider(Name("VolumeSlider"), 0.75f);
        ///
        /// // Set brightness to minimum
        /// await ClickSlider(Name("BrightnessSlider"), 0f);
        /// </example>
        protected async UniTask ClickSlider(Search search, float percent, bool throwIfMissing = true, float searchTime = 10)
        {
            percent = Mathf.Clamp01(percent);
            bool found = await ActionExecutor.ClickSliderAsync(search, percent, searchTime);

            if (found)
            {
                await ActionComplete();
            }
            else if (throwIfMissing)
            {
                throw new TestException($"ClickSlider (Search) could not find any matching Slider within {searchTime}s");
            }
        }

        /// <summary>
        /// Drags on a slider from one percentage position to another.
        /// Useful for testing slider interaction with realistic drag gestures.
        /// </summary>
        /// <param name="search">Search query to find the Slider component.</param>
        /// <param name="fromPercent">Start position as percentage (0-1). Clamped to 0-1.</param>
        /// <param name="toPercent">End position as percentage (0-1). Clamped to 0-1.</param>
        /// <param name="throwIfMissing">If true, throws an exception when slider is not found. Default is true.</param>
        /// <param name="searchTime">Maximum time in seconds to search for the slider. Default is 10 seconds.</param>
        /// <param name="duration">Duration of the drag animation in seconds. Default is 0.3 seconds.</param>
        /// <returns>A UniTask that completes when the drag is finished.</returns>
        /// <example>
        /// // Drag volume from 25% to 75%
        /// await DragSlider(Name("VolumeSlider"), 0.25f, 0.75f);
        ///
        /// // Slowly drag brightness from max to min
        /// await DragSlider(Name("BrightnessSlider"), 1f, 0f, duration: 1f);
        /// </example>
        protected async UniTask DragSlider(Search search, float fromPercent, float toPercent, bool throwIfMissing = true, float searchTime = 10, float duration = 1.0f)
        {
            fromPercent = Mathf.Clamp01(fromPercent);
            toPercent = Mathf.Clamp01(toPercent);

            bool found = await ActionExecutor.DragSliderAsync(search, fromPercent, toPercent, duration, searchTime);

            if (found)
            {
                await ActionComplete();
            }
            else if (throwIfMissing)
            {
                throw new TestException($"DragSlider (Search) could not find any matching Slider within {searchTime}s");
            }
        }

        /// <summary>
        /// Scrolls a ScrollRect using drag gestures until the target element becomes visible on screen, then returns it.
        /// Uses realistic input injection (drag gestures) - does not manipulate scroll position directly.
        /// Ideal for scrolling to items in long lists or grids.
        /// </summary>
        /// <param name="scrollViewSearch">Search query to find the ScrollRect component.</param>
        /// <param name="targetSearch">Search query to find the target element inside the scroll view.</param>
        /// <param name="maxScrollAttempts">Maximum number of scroll attempts before giving up. Default is 20.</param>
        /// <param name="throwIfMissing">If true, throws an exception when elements are not found. Default is true.</param>
        /// <param name="searchTime">Initial timeout in seconds for finding elements. Default is 5 seconds.</param>
        /// <returns>The target GameObject once visible, or null if not found.</returns>
        /// <exception cref="TestException">Thrown when elements are not found and throwIfMissing is true.</exception>
        /// <example>
        /// // Scroll to and click an item in a list
        /// var item = await ScrollTo(Name("InventoryList"), Name("SwordItem"));
        /// await Click(item);
        ///
        /// // Scroll to a specific player in a leaderboard
        /// await ScrollTo(Name("LeaderboardScroll"), Text("*Player123*"));
        /// </example>
        protected async UniTask<GameObject> ScrollTo(Search scrollViewSearch, Search targetSearch, int maxScrollAttempts = 20, bool throwIfMissing = true, float searchTime = 5)
        {
            Debug.Log($"[UITEST] ScrollTo - searching for scroll view and target");

            // Find the ScrollRect
            var scrollRect = await Find<ScrollRect>(scrollViewSearch, throwIfMissing, searchTime);
            if (scrollRect == null) return null;

            var viewport = scrollRect.viewport ?? scrollRect.GetComponent<RectTransform>();
            var content = scrollRect.content;

            if (content == null)
            {
                if (throwIfMissing)
                    throw new TestException("ScrollTo - ScrollRect has no content RectTransform assigned");
                return null;
            }

            // Get viewport bounds for visibility checks
            var canvas = scrollRect.GetComponentInParent<Canvas>();
            Camera cam = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = canvas.worldCamera ?? Camera.main;

            // Calculate scroll distances based on viewport size
            Vector3[] viewportCorners = new Vector3[4];
            viewport.GetWorldCorners(viewportCorners);

            // For ScreenSpaceOverlay, world corners ARE screen coordinates
            // For other render modes, convert using the camera
            Vector2 viewportMin, viewportMax;
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                viewportMin = viewportCorners[0];
                viewportMax = viewportCorners[2];
            }
            else
            {
                viewportMin = RectTransformUtility.WorldToScreenPoint(cam, viewportCorners[0]);
                viewportMax = RectTransformUtility.WorldToScreenPoint(cam, viewportCorners[2]);
            }

            float viewportHeight = Mathf.Abs(viewportMax.y - viewportMin.y);
            float viewportWidth = Mathf.Abs(viewportMax.x - viewportMin.x);
            // Use 40% of the smaller dimension to ensure drag stays within viewport bounds
            float scrollDistance = Mathf.Min(viewportHeight, viewportWidth) * 0.4f;

            Debug.Log($"[UITEST] ScrollTo - viewport: {viewport.name}, renderMode={canvas?.renderMode}, cam={cam}");
            Debug.Log($"[UITEST] ScrollTo - viewportMin={viewportMin}, viewportMax={viewportMax}, size=({viewportWidth}, {viewportHeight})");
            Debug.Log($"[UITEST] ScrollTo - Screen.width={Screen.width}, Screen.height={Screen.height}");

            Vector2 scrollCenter = (viewportMin + viewportMax) / 2f;
            Debug.Log($"[UITEST] ScrollTo - scrollCenter=({scrollCenter.x:F0},{scrollCenter.y:F0})");

            // Try to find target - first check if already visible
            var findMode = targetSearch.ShouldIncludeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
            int scrollAttempts = 0;

            while (scrollAttempts < maxScrollAttempts && Application.isPlaying)
            {
                await UniTask.Yield(); // Ensure layout is updated

                // S for target element within the content
                var allTargets = GameObject.FindObjectsByType<MonoBehaviour>(findMode, FindObjectsSortMode.None)
                    .Where(b => b != null && targetSearch.Matches(b.gameObject) && IsDescendantOf(b.transform, content))
                    .Select(b => b.gameObject)
                    .Distinct()
                    .ToList();

                // Check if any target is visible in viewport
                foreach (var target in allTargets)
                {
                    if (IsVisibleInViewport(target, viewport, cam))
                    {
                        Debug.Log($"[UITEST] ScrollTo - found visible target: {GetHierarchyPath(target.transform)}");
                        return target;
                    }
                }

                Vector2 dragDirection = Vector2.zero;
                bool canScrollVertical = scrollRect.vertical;
                bool canScrollHorizontal = scrollRect.horizontal;

                if (allTargets.Count > 0)
                {
                    // Target exists but not visible - determine drag direction based on target position
                    var target = allTargets[0];
                    var targetRect = target.GetComponent<RectTransform>();

                    if (targetRect != null)
                    {
                        Vector3[] targetCorners = new Vector3[4];
                        targetRect.GetWorldCorners(targetCorners);

                        // Get current viewport corners (fresh read)
                        Vector3[] currentViewportCorners = new Vector3[4];
                        viewport.GetWorldCorners(currentViewportCorners);

                        float targetCenterY = (targetCorners[0].y + targetCorners[2].y) / 2f;
                        float viewportCenterY = (currentViewportCorners[0].y + currentViewportCorners[2].y) / 2f;
                        float targetCenterX = (targetCorners[0].x + targetCorners[2].x) / 2f;
                        float viewportCenterX = (currentViewportCorners[0].x + currentViewportCorners[2].x) / 2f;

                        if (canScrollVertical)
                        {
                            if (targetCenterY < viewportCenterY)
                            {
                                // Target is below viewport (lower Y) - drag UP to bring lower content into view
                                // (dragging up on screen scrolls content upward, revealing lower content)
                                dragDirection.y = scrollDistance;
                            }
                            else
                            {
                                // Target is above viewport (higher Y) - drag DOWN to bring upper content into view
                                dragDirection.y = -scrollDistance;
                            }
                        }

                        if (canScrollHorizontal)
                        {
                            if (targetCenterX > viewportCenterX)
                            {
                                // Target is to the right - drag left to scroll content right
                                dragDirection.x = -scrollDistance;
                            }
                            else
                            {
                                // Target is to the left - drag right to scroll content left
                                dragDirection.x = scrollDistance;
                            }
                        }

                        Debug.Log($"[UITEST] ScrollTo - target found but not visible, scroll attempt {scrollAttempts + 1}, drag=({dragDirection.x:F0},{dragDirection.y:F0})");
                    }
                }
                else
                {
                    // No targets found yet - do a sequential search by scrolling through content
                    Debug.Log($"[UITEST] ScrollTo - no targets found yet, scroll attempt {scrollAttempts + 1}");

                    // Scroll down/right to search through content
                    // Drag UP to bring lower content into view (scroll down through list)
                    if (canScrollVertical)
                    {
                        dragDirection.y = scrollDistance; // Drag UP to scroll down through content
                    }
                    if (canScrollHorizontal)
                    {
                        dragDirection.x = -scrollDistance; // Drag LEFT to scroll right through content
                    }
                }

                if (dragDirection == Vector2.zero)
                {
                    Debug.LogWarning("[UITEST] ScrollTo - ScrollRect has no scroll direction enabled");
                    break;
                }

                // Perform drag gesture using realistic input injection
                float posBefore = scrollRect.vertical ? scrollRect.verticalNormalizedPosition : scrollRect.horizontalNormalizedPosition;

                // Debug: Log what the raycast would hit at the drag start position
                var pointerData = new PointerEventData(EventSystem.current) { position = scrollCenter };
                var raycastResults = new List<RaycastResult>();
                EventSystem.current.RaycastAll(pointerData, raycastResults);
                Debug.Log($"[UITEST] ScrollTo - drag start ({scrollCenter.x:F0},{scrollCenter.y:F0}), raycast hits: {raycastResults.Count}");
                foreach (var hit in raycastResults.Take(3))
                {
                    Debug.Log($"[UITEST] ScrollTo - raycast hit: {hit.gameObject.name} at depth {hit.depth}");
                }

                await ActionExecutor.DragFromToAsync(scrollCenter, scrollCenter + dragDirection, 0.15f);
                await UniTask.Delay(100, true); // Wait for scroll to settle
                float posAfter = scrollRect.vertical ? scrollRect.verticalNormalizedPosition : scrollRect.horizontalNormalizedPosition;
                Debug.Log($"[UITEST] ScrollTo - scroll position changed: {posBefore:F3} -> {posAfter:F3}");

                scrollAttempts++;
            }

            if (throwIfMissing)
                throw new TestException($"ScrollTo - Could not find visible target after {maxScrollAttempts} scroll attempts");

            return null;
        }

        /// <summary>
        /// Scrolls a ScrollRect until the target element becomes visible, then clicks on it.
        /// Combines ScrollTo and Click into a single convenience method.
        /// </summary>
        /// <param name="scrollViewSearch">Search query to find the ScrollRect component.</param>
        /// <param name="targetSearch">Search query to find the target element to click inside the scroll view.</param>
        /// <param name="maxScrollAttempts">Maximum number of scroll attempts before giving up. Default is 20.</param>
        /// <param name="throwIfMissing">If true, throws an exception when elements are not found. Default is true.</param>
        /// <param name="searchTime">Initial timeout in seconds for finding the scroll view. Default is 5 seconds.</param>
        /// <returns>A UniTask that completes when the element is scrolled to and clicked.</returns>
        /// <example>
        /// // Scroll to and click an inventory item
        /// await ScrollToAndClick(Name("InventoryList"), Name("SwordItem"));
        ///
        /// // Select a friend from a scrollable list
        /// await ScrollToAndClick(Name("FriendsScroll"), Text("*JohnDoe*"));
        /// </example>
        protected async UniTask ScrollToAndClick(Search scrollViewSearch, Search targetSearch, int maxScrollAttempts = 20, bool throwIfMissing = true, float searchTime = 5)
        {
            var target = await ScrollTo(scrollViewSearch, targetSearch, maxScrollAttempts, throwIfMissing, searchTime);
            if (target != null)
            {
                await ActionExecutor.ClickAsync(target);
                await ActionComplete();
            }
        }

        /// <summary>
        /// Checks if a GameObject is a descendant of the given parent transform.
        /// </summary>
        private static bool IsDescendantOf(Transform child, Transform parent)
        {
            Transform current = child;
            while (current != null)
            {
                if (current == parent)
                    return true;
                current = current.parent;
            }
            return false;
        }

        /// <summary>
        /// Checks if a GameObject is visible within a viewport RectTransform.
        /// Uses RectTransformUtility for accurate visibility detection regardless of canvas mode.
        /// </summary>
        private static bool IsVisibleInViewport(GameObject go, RectTransform viewport, Camera cam)
        {
            var rect = go.GetComponent<RectTransform>();
            if (rect == null)
            {
                Debug.Log($"[UITEST] IsVisibleInViewport: {go.name} has no RectTransform");
                return false;
            }

            // Get element's world corners
            Vector3[] elementCorners = new Vector3[4];
            rect.GetWorldCorners(elementCorners);

            // Get viewport's world corners
            Vector3[] viewportCorners = new Vector3[4];
            viewport.GetWorldCorners(viewportCorners);

            // Calculate bounds in world space
            float elementMinX = Mathf.Min(elementCorners[0].x, elementCorners[1].x, elementCorners[2].x, elementCorners[3].x);
            float elementMaxX = Mathf.Max(elementCorners[0].x, elementCorners[1].x, elementCorners[2].x, elementCorners[3].x);
            float elementMinY = Mathf.Min(elementCorners[0].y, elementCorners[1].y, elementCorners[2].y, elementCorners[3].y);
            float elementMaxY = Mathf.Max(elementCorners[0].y, elementCorners[1].y, elementCorners[2].y, elementCorners[3].y);

            float viewportMinX = Mathf.Min(viewportCorners[0].x, viewportCorners[1].x, viewportCorners[2].x, viewportCorners[3].x);
            float viewportMaxX = Mathf.Max(viewportCorners[0].x, viewportCorners[1].x, viewportCorners[2].x, viewportCorners[3].x);
            float viewportMinY = Mathf.Min(viewportCorners[0].y, viewportCorners[1].y, viewportCorners[2].y, viewportCorners[3].y);
            float viewportMaxY = Mathf.Max(viewportCorners[0].y, viewportCorners[1].y, viewportCorners[2].y, viewportCorners[3].y);

            // Check for overlap (element is at least partially visible)
            // Using 20% visibility threshold - element must have at least 20% overlap
            float elementWidth = elementMaxX - elementMinX;
            float elementHeight = elementMaxY - elementMinY;

            float overlapX = Mathf.Max(0, Mathf.Min(elementMaxX, viewportMaxX) - Mathf.Max(elementMinX, viewportMinX));
            float overlapY = Mathf.Max(0, Mathf.Min(elementMaxY, viewportMaxY) - Mathf.Max(elementMinY, viewportMinY));

            float overlapArea = overlapX * overlapY;
            float elementArea = elementWidth * elementHeight;
            float overlapPercent = elementArea > 0 ? overlapArea / elementArea : 0;

            bool isVisible = overlapPercent >= 0.2f; // 20% visibility threshold

            Debug.Log($"[UITEST] IsVisibleInViewport: {go.name} element=({elementMinX:F1},{elementMinY:F1})-({elementMaxX:F1},{elementMaxY:F1}), viewport=({viewportMinX:F1},{viewportMinY:F1})-({viewportMaxX:F1},{viewportMaxY:F1}), overlap={overlapPercent:P0}, visible={isVisible}");

            return isVisible;
        }

        /// <summary>
        /// Selects a dropdown option by index using realistic click interactions.
        /// Opens the dropdown by clicking it, then clicks the option at the specified index.
        /// Supports both legacy Unity Dropdown and TextMeshPro TMP_Dropdown.
        /// </summary>
        /// <param name="search">Search query to find the Dropdown or TMP_Dropdown component.</param>
        /// <param name="optionIndex">Index of the option to select (0-based).</param>
        /// <param name="throwIfMissing">If true, throws an exception when dropdown is not found. Default is true.</param>
        /// <param name="searchTime">Maximum time in seconds to search for the dropdown. Default is 10 seconds.</param>
        /// <returns>A UniTask that completes when the option is selected.</returns>
        /// <example>
        /// // Select the second option (index 1)
        /// await ClickDropdown(Name("CategoryDropdown"), 1);
        ///
        /// // Select first option in difficulty dropdown
        /// await ClickDropdown(Name("DifficultyDropdown"), 0);
        /// </example>
        protected async UniTask ClickDropdown(Search search, int optionIndex, bool throwIfMissing = true, float searchTime = 10)
        {
            bool found = await ActionExecutor.ClickDropdownAsync(search, optionIndex, searchTime);

            if (found)
            {
                await ActionComplete();
            }
            else if (throwIfMissing)
            {
                throw new TestException($"ClickDropdown (Search) could not find any matching Dropdown within {searchTime}s");
            }
        }

        /// <summary>
        /// Selects a dropdown option by label text using realistic click interactions.
        /// Opens the dropdown by clicking it, then finds and clicks the option with the matching label.
        /// Supports both legacy Unity Dropdown and TextMeshPro TMP_Dropdown.
        /// </summary>
        /// <param name="search">Search query to find the Dropdown or TMP_Dropdown component.</param>
        /// <param name="optionLabel">The exact text label of the option to select.</param>
        /// <param name="throwIfMissing">If true, throws an exception when dropdown is not found. Default is true.</param>
        /// <param name="searchTime">Maximum time in seconds to search for the dropdown. Default is 10 seconds.</param>
        /// <returns>A UniTask that completes when the option is selected.</returns>
        /// <example>
        /// // Select option by its label text
        /// await ClickDropdown(Name("CategoryDropdown"), "Electronics");
        ///
        /// // Select difficulty level by name
        /// await ClickDropdown(Name("DifficultyDropdown"), "Hard");
        /// </example>
        protected async UniTask ClickDropdown(Search search, string optionLabel, bool throwIfMissing = true, float searchTime = 10)
        {
            bool found = await ActionExecutor.ClickDropdownAsync(search, optionLabel, searchTime);

            if (found)
            {
                await ActionComplete();
            }
            else if (throwIfMissing)
            {
                throw new TestException($"ClickDropdown (Search) could not find Dropdown with option '{optionLabel}' within {searchTime}s");
            }
        }

        /// <summary>
        /// Clicks each dropdown option sequentially.
        /// Opens the dropdown, clicks an option, then repeats for all options.
        /// </summary>
        /// <param name="search">Search query to find the Dropdown or TMP_Dropdown component.</param>
        /// <param name="delayBetween">Delay in milliseconds between each option click. Default is 0.</param>
        /// <param name="throwIfMissing">If true, throws an exception when dropdown is not found. Default is true.</param>
        /// <param name="searchTime">Maximum time in seconds to search for the dropdown. Default is 10 seconds.</param>
        /// <returns>A UniTask that completes when all options have been clicked.</returns>
        protected async UniTask ClickDropdownItems(Search search, int delayBetween = 0, bool throwIfMissing = true, float searchTime = 10)
        {
            var go = search.FindFirst();
            if (go == null)
            {
                if (throwIfMissing)
                    throw new TestException($"ClickDropdownItems({search}) could not find dropdown");
                return;
            }

            int optionCount = 0;
            var tmpDropdown = go.GetComponent<TMP_Dropdown>();
            var legacyDropdown = go.GetComponent<Dropdown>();

            if (tmpDropdown != null)
                optionCount = tmpDropdown.options.Count;
            else if (legacyDropdown != null)
                optionCount = legacyDropdown.options.Count;

            if (optionCount == 0)
            {
                if (throwIfMissing)
                    throw new TestException($"ClickDropdownItems({search}) found '{go.name}' but it has no Dropdown component or no options");
                return;
            }

            Debug.Log($"[UITEST] ClickDropdownItems({search}) -> '{go.name}' found {optionCount} options");

            for (int i = 0; i < optionCount; i++)
            {
                await ClickDropdown(search, i, throwIfMissing, searchTime);

                if (delayBetween > 0 && i < optionCount - 1)
                    await UniTask.Delay(delayBetween);
            }
        }

        /// <summary>
        /// Clicks each clickable child item inside a ScrollRect sequentially.
        /// Scrolls to each item before clicking to ensure it's visible.
        /// </summary>
        /// <param name="search">Search query to find the ScrollRect.</param>
        /// <param name="delayBetween">Delay in milliseconds between each click. Default is 0.</param>
        /// <param name="throwIfMissing">If true, throws an exception when scroll view is not found. Default is true.</param>
        /// <param name="searchTime">Maximum time in seconds to search for the scroll view. Default is 10 seconds.</param>
        /// <returns>A UniTask that completes when all items have been clicked.</returns>
        protected async UniTask ClickScrollItems(Search search, int delayBetween = 0, bool throwIfMissing = true, float searchTime = 10)
        {
            var scrollRect = await Find<ScrollRect>(search, throwIfMissing, searchTime);
            if (scrollRect == null) return;

            var content = scrollRect.content;
            if (content == null)
            {
                if (throwIfMissing)
                    throw new TestException($"ClickScrollItems({search}) ScrollRect '{scrollRect.name}' has no content assigned");
                return;
            }

            var selectables = content.GetComponentsInChildren<Selectable>(true)
                .Where(s => s.interactable)
                .ToList();

            Debug.Log($"[UITEST] ClickScrollItems({search}) -> '{scrollRect.name}' found {selectables.Count} items");

            for (int i = 0; i < selectables.Count; i++)
            {
                var item = selectables[i];
                // ScrollTo handles visibility check and scrolling
                await ScrollTo(search, Name(item.name), throwIfMissing: false);
                await Click(item.gameObject);

                if (delayBetween > 0 && i < selectables.Count - 1)
                    await UniTask.Delay(delayBetween);
            }
        }

        /// <summary>
        /// Clicks any one of the elements matching the search query.
        /// Randomly selects from matching elements and clicks the first one found.
        /// </summary>
        /// <param name="search">Search query to find clickable elements.</param>
        /// <param name="seconds">Maximum time in seconds to search for elements. Default is 10 seconds.</param>
        /// <param name="throwIfMissing">If true, throws an exception when no element is found. Default is true.</param>
        /// <returns>A UniTask that completes when an element is clicked.</returns>
        /// <exception cref="TestException">Thrown when no matching element is found and throwIfMissing is true.</exception>
        protected async UniTask ClickAny(Search search, float seconds = 10, bool throwIfMissing = true)
        {
            bool found = await ActionExecutor.ClickAnyAsync(search, seconds);

            if (found)
            {
                await ActionComplete();
            }
            else if (throwIfMissing)
            {
                throw new TestException($"ClickAny({search}) could not find any matching target within {seconds}s");
            }
        }


        /// <summary>
        /// Finds a component by type in the scene. By default only active and enabled components are returned.
        /// </summary>
        /// <typeparam name="T">The MonoBehaviour type to search for.</typeparam>
        /// <param name="throwIfMissing">If true, throws TimeoutException when component is not found. Default is true.</param>
        /// <param name="seconds">Maximum time in seconds to search. Default is 10 seconds.</param>
        /// <param name="includeInactive">If true, includes inactive GameObjects in the search. Default is false.</param>
        /// <param name="includeDisabled">If true, includes disabled/non-interactable components. Default is false.</param>
        /// <returns>The found component, or default if not found and throwIfMissing is false.</returns>
        /// <exception cref="TimeoutException">Thrown when component is not found and throwIfMissing is true.</exception>
        /// <example>
        /// // Find any Button in scene
        /// var button = await Find&lt;Button&gt;();
        ///
        /// // Find including disabled
        /// var slider = await Find&lt;Slider&gt;(includeDisabled: true);
        /// </example>
        protected async UniTask<T> Find<T>(bool throwIfMissing = true, float seconds = 10, bool includeInactive = false, bool includeDisabled = false)
            where T : MonoBehaviour
        {
            var startTime = Time.realtimeSinceStartup;
            var findMode = includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;

            while ((Time.realtimeSinceStartup - startTime) < seconds)
            {
                await UniTask.Delay(EffectivePollInterval, true);

                var result = GameObject.FindAnyObjectByType<T>(findMode);

                if (result == null)
                    continue;

                // Check availability manually for the non-S version
                if (!includeInactive && !result.gameObject.activeInHierarchy)
                    continue;

                if (!includeDisabled)
                {
                    if (!result.enabled)
                        continue;

                    var canvasGroup = result.GetComponentInParent<CanvasGroup>();
                    if (canvasGroup != null && (canvasGroup.alpha <= 0 || !canvasGroup.interactable))
                        continue;
                }

                return result;
            }

            if (throwIfMissing)
                throw new System.TimeoutException($"Unable to locate {typeof(T).Name} in {seconds} seconds");

            return default;
        }

        /// <summary>
        /// Finds a component matching the Search query.
        /// Supports implicit string conversion: Find&lt;Button&gt;("Play") becomes Find&lt;Button&gt;(S.ByText("Play")).
        /// Use search.IncludeInactive() to find inactive GameObjects.
        /// Use search.IncludeDisabled() to find disabled/non-interactable components.
        /// </summary>
        /// <typeparam name="T">The component type to search for.</typeparam>
        /// <param name="search">Search query to match elements against.</param>
        /// <param name="throwIfMissing">If true, throws TimeoutException when component is not found. Default is true.</param>
        /// <param name="seconds">Maximum time in seconds to search. Default is 10 seconds.</param>
        /// <returns>The found component, or default if not found and throwIfMissing is false.</returns>
        /// <exception cref="TimeoutException">Thrown when component is not found and throwIfMissing is true.</exception>
        /// <example>
        /// // Find button by text
        /// var playButton = await Find&lt;Button&gt;("Play");
        ///
        /// // Find by name pattern
        /// var slider = await Find&lt;Slider&gt;(Name("Volume*"));
        ///
        /// // Optional find (no throw)
        /// var popup = await Find&lt;CanvasGroup&gt;(Name("Popup"), throwIfMissing: false);
        /// </example>
        protected async UniTask<T> Find<T>(Search search, bool throwIfMissing = true, float seconds = 10)
        {
            var startTime = Time.realtimeSinceStartup;
            LogDebug($"Find<{typeof(T).Name}>: using Search query with {seconds}s timeout, includeInactive={search.ShouldIncludeInactive}, includeDisabled={search.ShouldIncludeDisabled}");

            int iteration = 0;
            var findMode = search.ShouldIncludeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying && !TestCancellationToken.IsCancellationRequested)
            {
                iteration++;

                // When the search has a target transform (Parent/Child/Sibling), we need to search all GameObjects
                // because the source object may not have a MonoBehaviour (e.g., a Panel with only Image component)
                if (search.HasTargetTransform)
                {
                    var allTransforms = GameObject.FindObjectsByType<Transform>(findMode, FindObjectsSortMode.None);
                    var matchingGameObjects = new List<GameObject>();

                    foreach (var transform in allTransforms)
                    {
                        if (transform == null) continue;
                        var go = transform.gameObject;
                        if (!search.Matches(go)) continue;
                        matchingGameObjects.Add(go);
                    }

                    if (matchingGameObjects.Count > 0)
                    {
                        var transformedGos = search.ApplyPostProcessing(matchingGameObjects).ToList();
                        foreach (var go in transformedGos)
                        {
                            if (go == null) continue;

                            // Check availability on the transformed target
                            var anyComponent = go.GetComponent<Component>();
                            if (anyComponent != null && !CheckAvailability(anyComponent, search)) continue;

                            var component = go.GetComponent<T>();
                            if (component != null)
                            {
                                Debug.Log($"[UITEST] Find<{typeof(T).Name}> (Search with transform) found on iteration {iteration} after {(Time.realtimeSinceStartup - startTime) * 1000:F0}ms");
                                await ActionComplete();
                                return component;
                            }
                        }
                    }
                }
                else
                {
                    // Standard path: find MonoBehaviours that match and have component T
                    var allObjects = GameObject.FindObjectsByType<MonoBehaviour>(findMode, FindObjectsSortMode.None);
                    var matches = new List<(MonoBehaviour obj, T result)>();

                    // Track which GameObjects we've already processed to avoid duplicates
                    var processedGameObjects = new HashSet<GameObject>();

                    foreach (var obj in allObjects)
                    {
                        if (obj == null) continue;

                        // Skip if we've already processed this GameObject
                        if (!processedGameObjects.Add(obj.gameObject)) continue;

                        if (!CheckAvailability(obj, search)) continue;
                        if (!search.Matches(obj.gameObject)) continue;

                        // Check if this object or its components match type T
                        if (obj is T match)
                        {
                            matches.Add((obj, match));
                        }
                        else
                        {
                            var component = obj.GetComponent<T>();
                            if (component != null)
                                matches.Add((obj, component));
                        }
                    }

                    // Apply post-processing if specified (ordering, skip, take, etc.)
                    if (matches.Count > 0)
                    {
                        if (search.HasPostProcessing)
                        {
                            var orderedGos = search.ApplyPostProcessing(matches.Select(m => m.obj.gameObject)).ToList();
                            foreach (var go in orderedGos)
                            {
                                if (go == null) continue;

                                var component = go.GetComponent<T>();
                                if (component != null)
                                {
                                    Debug.Log($"[UITEST] Find<{typeof(T).Name}> (Search) found on iteration {iteration} after {(Time.realtimeSinceStartup - startTime) * 1000:F0}ms");
                                    await ActionComplete();
                                    return component;
                                }
                            }
                        }
                        else
                        {
                            var first = matches.FirstOrDefault();
                            if (first.result != null)
                            {
                                Debug.Log($"[UITEST] Find<{typeof(T).Name}> (Search) found on iteration {iteration} after {(Time.realtimeSinceStartup - startTime) * 1000:F0}ms");
                                await ActionComplete();
                                return first.result;
                            }
                        }
                    }
                }

                await UniTask.Delay(EffectivePollInterval, true, PlayerLoopTiming.Update, TestCancellationToken);
            }

            TestCancellationToken.ThrowIfCancellationRequested();

            if (throwIfMissing)
                throw new TimeoutException($"Unable to locate {typeof(T).Name} matching '{search}' in {seconds} seconds after {iteration} iterations");

            return default;
        }


        /// <summary>
        /// Finds all components matching the Search query.
        /// Supports implicit string conversion.
        /// Use search.IncludeInactive() to find inactive GameObjects.
        /// Use search.IncludeDisabled() to find disabled/non-interactable components.
        /// Supports post-processing: First(), Last(), Skip(), OrderBy().
        /// </summary>
        /// <typeparam name="T">The component type to search for.</typeparam>
        /// <param name="search">Search query to match elements against.</param>
        /// <param name="seconds">Maximum time in seconds to wait for at least one result. Default is 10 seconds.</param>
        /// <returns>An enumerable of all matching components. Returns empty enumerable if none found after timeout.</returns>
        /// <example>
        /// // Find all buttons with "Tab" in name
        /// var tabs = await FindAll&lt;Button&gt;(Name("Tab*"));
        /// foreach (var tab in tabs)
        /// {
        ///     await Click(tab);
        /// }
        ///
        /// // Find all menu items by text pattern
        /// var menuItems = await FindAll&lt;Button&gt;(Text("*Menu*"));
        /// </example>
        protected async UniTask<IEnumerable<T>> FindAll<T>(Search search, float seconds = 10)
        {
            var startTime = Time.realtimeSinceStartup;
            var findMode = search.ShouldIncludeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;

            while (Application.isPlaying && !TestCancellationToken.IsCancellationRequested)
            {
                if ((Time.realtimeSinceStartup - startTime) > seconds)
                    break;

                // When the search has a target transform (Parent/Child/Sibling), we need to search all GameObjects
                // because the source object may not have a MonoBehaviour (e.g., a Panel with only Image component)
                if (search.HasTargetTransform)
                {
                    var allTransforms = GameObject.FindObjectsByType<Transform>(findMode, FindObjectsSortMode.None);
                    var matchingGameObjects = new List<GameObject>();

                    foreach (var transform in allTransforms)
                    {
                        if (transform == null) continue;
                        var go = transform.gameObject;
                        if (!search.Matches(go)) continue;
                        matchingGameObjects.Add(go);
                    }

                    if (matchingGameObjects.Count > 0)
                    {
                        var transformedGos = search.ApplyPostProcessing(matchingGameObjects).ToList();
                        return transformedGos
                            .Where(go => go != null)
                            .Select(go => go.GetComponent<T>())
                            .Where(c => c != null);
                    }
                }
                else
                {
                    // Standard path: find MonoBehaviours that match and have component T
                    var allObjects = GameObject.FindObjectsByType<MonoBehaviour>(findMode, FindObjectsSortMode.None);
                    var matches = new List<(MonoBehaviour obj, T result)>();

                    // Track which GameObjects we've already processed to avoid duplicates
                    var processedGameObjects = new HashSet<GameObject>();

                    foreach (var obj in allObjects)
                    {
                        if (obj == null) continue;

                        // Skip if we've already processed this GameObject
                        if (!processedGameObjects.Add(obj.gameObject)) continue;

                        if (!CheckAvailability(obj, search)) continue;
                        if (!search.Matches(obj.gameObject)) continue;

                        if (obj is T match)
                        {
                            matches.Add((obj, match));
                        }
                        else
                        {
                            var component = obj.GetComponent<T>();
                            if (component != null)
                                matches.Add((obj, component));
                        }
                    }

                    if (matches.Count > 0)
                    {
                        // Apply post-processing if specified (ordering, skip, take, etc.)
                        if (search.HasPostProcessing)
                        {
                            var orderedGos = search.ApplyPostProcessing(matches.Select(m => m.obj.gameObject)).ToList();
                            return orderedGos
                                .Where(go => go != null)
                                .Select(go => go.GetComponent<T>())
                                .Where(c => c != null);
                        }
                        return matches.Select(m => m.result);
                    }
                }

                await UniTask.Delay(EffectivePollInterval, true, PlayerLoopTiming.Update, TestCancellationToken);
            }

            TestCancellationToken.ThrowIfCancellationRequested();
            return Enumerable.Empty<T>();
        }

        /// <summary>
        /// Represents a container with its child items for iteration.
        /// Use with foreach to iterate over items while having access to the container for ScrollTo.
        /// </summary>
        public class ItemContainer
        {
            public Component Container { get; }
            public ScrollRect ScrollRect => Container as ScrollRect;
            public IEnumerable<RectTransform> Items { get; }

            public ItemContainer(Component container, IEnumerable<RectTransform> items)
            {
                Container = container;
                Items = items;
            }

            public IEnumerator<(Component Container, RectTransform Item)> GetEnumerator()
            {
                foreach (var item in Items)
                {
                    yield return (Container, item);
                }
            }
        }

        /// <summary>
        /// Finds a container (ScrollRect, Dropdown, LayoutGroup) and its child items.
        /// Returns pairs of (Container, Item) for use with ScrollTo and Click.
        /// Supports: ScrollRect, TMP_Dropdown, Dropdown, LayoutGroup (Horizontal/Vertical/Grid).
        /// </summary>
        /// <param name="containerSearch">Search query to find the container component.</param>
        /// <param name="itemSearch">Optional additional search criteria to filter child items.</param>
        /// <param name="throwIfMissing">If true, throws an exception when container is not found. Default is true.</param>
        /// <param name="seconds">Maximum time in seconds to search for the container. Default is 10 seconds.</param>
        /// <returns>An ItemContainer with the container and its child items.</returns>
        /// <example>
        /// // ScrollRect - scroll to each item
        /// foreach (var (list, item) in await FindItems("InventoryList"))
        /// {
        ///     await ScrollTo(list, item);
        ///     await Click(item);
        /// }
        ///
        /// // Dropdown options
        /// foreach (var (dropdown, option) in await FindItems("CategoryDropdown"))
        /// {
        ///     await Click(option);
        /// }
        /// </example>
        protected async UniTask<ItemContainer> FindItems(Search containerSearch, Search itemSearch = null)
        {
            // Try each supported container type in order of most common
            Component container = await Find<ScrollRect>(containerSearch, throwIfMissing: false, seconds: 2);
            container ??= await Find<VerticalLayoutGroup>(containerSearch, throwIfMissing: false, seconds: 1);
            container ??= await Find<HorizontalLayoutGroup>(containerSearch, throwIfMissing: false, seconds: 1);
            container ??= await Find<GridLayoutGroup>(containerSearch, throwIfMissing: false, seconds: 1);
            container ??= await Find<TMP_Dropdown>(containerSearch, throwIfMissing: false, seconds: 1);
            container ??= await Find<Dropdown>(containerSearch, throwIfMissing: false, seconds: 1);

            if (container == null)
                throw new TestException($"FindItems could not find a supported container (ScrollRect, Dropdown, LayoutGroup) matching: {containerSearch}");

            var items = GetContainerItems(container);

            if (itemSearch != null)
            {
                items = items.Where(item => itemSearch.Matches(item.gameObject));
            }

            return new ItemContainer(container, items);
        }

        /// <summary>
        /// Finds a container by name and its child items.
        /// Convenience method that wraps the container name in a Name() search.
        /// </summary>
        /// <param name="containerName">The name of the container to find.</param>
        /// <param name="itemSearch">Optional additional search criteria to filter child items.</param>
        /// <returns>An ItemContainer with the container and its child items.</returns>
        protected async UniTask<ItemContainer> FindItems(string containerName, Search itemSearch = null)
        {
            return await FindItems(new Search().Name(containerName), itemSearch);
        }

        private IEnumerable<RectTransform> GetContainerItems(Component container)
        {
            switch (container)
            {
                case ScrollRect scrollRect:
                {
                    var content = scrollRect.content ?? scrollRect.GetComponent<RectTransform>();
                    var items = new List<RectTransform>();
                    foreach (Transform child in content)
                    {
                        var rect = child.GetComponent<RectTransform>();
                        if (rect != null && child.gameObject.activeInHierarchy)
                            items.Add(rect);
                    }
                    // Order by position
                    return items.OrderBy(item => scrollRect.vertical ? -item.anchoredPosition.y : item.anchoredPosition.x);
                }

                case TMP_Dropdown tmpDropdown:
                {
                    // Return option data as conceptual items - caller will use ClickDropdown
                    // For now, return the template items if dropdown is open
                    var template = tmpDropdown.template;
                    if (template != null && template.gameObject.activeInHierarchy)
                    {
                        var content = template.GetComponentInChildren<ToggleGroup>()?.transform ?? template;
                        return content.GetComponentsInChildren<RectTransform>()
                            .Where(r => r.GetComponent<Toggle>() != null)
                            .OrderBy(r => -r.anchoredPosition.y);
                    }
                    return Enumerable.Empty<RectTransform>();
                }

                case Dropdown dropdown:
                {
                    var template = dropdown.template;
                    if (template != null && template.gameObject.activeInHierarchy)
                    {
                        var content = template.GetComponentInChildren<ToggleGroup>()?.transform ?? template;
                        return content.GetComponentsInChildren<RectTransform>()
                            .Where(r => r.GetComponent<Toggle>() != null)
                            .OrderBy(r => -r.anchoredPosition.y);
                    }
                    return Enumerable.Empty<RectTransform>();
                }

                case HorizontalLayoutGroup hlg:
                {
                    var items = new List<RectTransform>();
                    foreach (Transform child in hlg.transform)
                    {
                        var rect = child.GetComponent<RectTransform>();
                        if (rect != null && child.gameObject.activeInHierarchy)
                            items.Add(rect);
                    }
                    return items.OrderBy(r => r.anchoredPosition.x);
                }

                case VerticalLayoutGroup vlg:
                {
                    var items = new List<RectTransform>();
                    foreach (Transform child in vlg.transform)
                    {
                        var rect = child.GetComponent<RectTransform>();
                        if (rect != null && child.gameObject.activeInHierarchy)
                            items.Add(rect);
                    }
                    return items.OrderBy(r => -r.anchoredPosition.y);
                }

                case GridLayoutGroup glg:
                {
                    var items = new List<RectTransform>();
                    foreach (Transform child in glg.transform)
                    {
                        var rect = child.GetComponent<RectTransform>();
                        if (rect != null && child.gameObject.activeInHierarchy)
                            items.Add(rect);
                    }
                    // Order top-to-bottom, left-to-right (reading order)
                    return items.OrderBy(r => -r.anchoredPosition.y).ThenBy(r => r.anchoredPosition.x);
                }

                default:
                    return Enumerable.Empty<RectTransform>();
            }
        }

        #region Random Click and Auto-Explore

        /// <summary>
        /// Random number generator for deterministic random clicks.
        /// Set a seed before using RandomClick for reproducible test runs.
        /// </summary>
        protected System.Random RandomGenerator { get; private set; } = new System.Random();

        /// <summary>
        /// Sets the random seed for deterministic random clicks.
        /// Use the same seed to reproduce the exact same click sequence.
        /// </summary>
        /// <param name="seed">The seed value for the random number generator.</param>
        /// <example>
        /// // Reproduce the same random click sequence
        /// SetRandomSeed(12345);
        /// await RandomClick(); // Will always click the same element with same seed
        /// </example>
        protected void SetRandomSeed(int seed)
        {
            RandomGenerator = new System.Random(seed);
            Debug.Log($"[UITEST] RandomSeed set to {seed}");
        }

        /// <summary>
        /// Clicks a random clickable UI element from those currently visible.
        /// Uses the current RandomGenerator for reproducible selection.
        /// </summary>
        /// <param name="filter">Optional search filter to narrow down clickable elements.</param>
        /// <returns>The component that was clicked, or null if none found.</returns>
        /// <example>
        /// // Click any random button
        /// await RandomClick();
        ///
        /// // Click a random button matching a pattern
        /// await RandomClick(Name("Menu*"));
        /// </example>
        protected async UniTask<Component> RandomClick(Search filter = null)
        {
            var clickables = await GetClickableElements(filter);
            if (clickables.Count == 0)
            {
                Debug.Log("[UITEST] RandomClick - No clickable elements found");
                return null;
            }

            int index = RandomGenerator.Next(clickables.Count);
            var target = clickables[index];
            Debug.Log($"[UITEST] RandomClick - Selected '{target.gameObject.name}' (index {index} of {clickables.Count})");

            await Click(target);
            return target;
        }

        /// <summary>
        /// Clicks a random clickable element, excluding elements matching the specified searches.
        /// Useful for avoiding known problematic buttons or exit buttons during exploration.
        /// </summary>
        /// <param name="exclude">Search patterns to exclude from random selection.</param>
        /// <returns>The component that was clicked, or null if none found.</returns>
        /// <example>
        /// // Click any button except Exit and Close buttons
        /// await RandomClickExcept(Name("*Exit*"), Name("*Close*"));
        /// </example>
        protected async UniTask<Component> RandomClickExcept(params Search[] exclude)
        {
            var clickables = await GetClickableElements(null);

            // Filter out excluded elements
            foreach (var excludeSearch in exclude)
            {
                clickables = clickables.Where(c => !excludeSearch.Matches(c.gameObject)).ToList();
            }

            if (clickables.Count == 0)
            {
                Debug.Log("[UITEST] RandomClickExcept - No clickable elements found after exclusions");
                return null;
            }

            int index = RandomGenerator.Next(clickables.Count);
            var target = clickables[index];
            Debug.Log($"[UITEST] RandomClickExcept - Selected '{target.gameObject.name}' (index {index} of {clickables.Count})");

            await Click(target);
            return target;
        }

        /// <summary>
        /// Gets all currently clickable UI elements.
        /// </summary>
        private async UniTask<List<Component>> GetClickableElements(Search filter)
        {
            await UniTask.Yield(); // Let UI update

            var allSelectables = GameObject.FindObjectsByType<Selectable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var clickables = new List<Component>();

            foreach (var selectable in allSelectables)
            {
                if (selectable == null) continue;
                if (!selectable.interactable) continue;
                if (!selectable.gameObject.activeInHierarchy) continue;

                // Check if it's visible (has a canvas renderer and is not fully transparent)
                var canvasGroup = selectable.GetComponentInParent<CanvasGroup>();
                if (canvasGroup != null && (canvasGroup.alpha <= 0 || !canvasGroup.interactable)) continue;

                // Apply filter if provided
                if (filter != null && !filter.Matches(selectable.gameObject)) continue;

                clickables.Add(selectable);
            }

            return clickables;
        }

        /// <summary>
        /// Condition for stopping auto-exploration.
        /// </summary>
        public enum ExploreStopCondition
        {
            /// <summary>Stop after a specified duration</summary>
            Time,
            /// <summary>Stop after a specified number of actions</summary>
            ActionCount,
            /// <summary>Stop when no new clickable elements are found (dead end)</summary>
            DeadEnd,
            /// <summary>Stop when a specific element appears</summary>
            ElementAppears,
            /// <summary>Stop when a specific element disappears</summary>
            ElementDisappears
        }

        /// <summary>
        /// Result of an auto-explore session.
        /// </summary>
        public class ExploreResult
        {
            public int ActionsPerformed { get; set; }
            public float DurationSeconds { get; set; }
            public bool ReachedDeadEnd { get; set; }
            public List<string> ClickedElements { get; } = new List<string>();
            public List<string> VisitedScenes { get; } = new List<string>();
            public ExploreStopCondition StopReason { get; set; }
        }

        /// <summary>
        /// Common back/exit button patterns to try when stuck.
        /// </summary>
        private static readonly string[] BackButtonPatterns = new[]
        {
            "*Back*", "*Close*", "*Exit*", "*Cancel*", "*Done*", "*Return*",
            "*Dismiss*", "*OK*", "*No*", "*X*", "BackButton", "CloseButton"
        };

        /// <summary>
        /// Auto-explores the UI by randomly clicking elements.
        /// Stops based on the specified condition. This is the main AutoExplore method with full control.
        /// </summary>
        /// <param name="stopCondition">When to stop exploring (Time, ActionCount, DeadEnd, etc.).</param>
        /// <param name="value">Value for the stop condition (seconds for Time, count for ActionCount). Default is 60.</param>
        /// <param name="seed">Optional random seed for reproducibility (null = random).</param>
        /// <param name="delayBetweenActions">Delay between actions in seconds. Default is 0.5.</param>
        /// <param name="tryBackOnStuck">Whether to try back/exit buttons when no new elements found. Default is true.</param>
        /// <returns>ExploreResult with statistics about the exploration session.</returns>
        /// <example>
        /// // Explore for 60 seconds
        /// var result = await AutoExplore(ExploreStopCondition.Time, 60f, seed: 12345);
        ///
        /// // Explore for 100 actions
        /// await AutoExplore(ExploreStopCondition.ActionCount, 100);
        ///
        /// // Explore until stuck
        /// await AutoExplore(ExploreStopCondition.DeadEnd, tryBackOnStuck: true);
        /// </example>
        protected async UniTask<ExploreResult> AutoExplore(
            ExploreStopCondition stopCondition,
            float value = 60f,
            int? seed = null,
            float delayBetweenActions = 0.5f,
            bool tryBackOnStuck = true)
        {
            if (seed.HasValue)
                SetRandomSeed(seed.Value);
            else
                SetRandomSeed(Environment.TickCount);

            var result = new ExploreResult();
            var startTime = Time.realtimeSinceStartup;
            var seenElements = new HashSet<string>();
            var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            result.VisitedScenes.Add(currentScene);

            int consecutiveFailures = 0;
            const int maxConsecutiveFailures = 5;

            Debug.Log($"[UITEST] AutoExplore started - StopCondition: {stopCondition}, Value: {value}, Seed: {seed ?? -1}");

            while (Application.isPlaying && !TestCancellationToken.IsCancellationRequested)
            {
                result.DurationSeconds = Time.realtimeSinceStartup - startTime;

                // Check stop conditions
                switch (stopCondition)
                {
                    case ExploreStopCondition.Time:
                        if (result.DurationSeconds >= value)
                        {
                            result.StopReason = ExploreStopCondition.Time;
                            Debug.Log($"[UITEST] AutoExplore stopped - Time limit reached ({value}s)");
                            return result;
                        }
                        break;

                    case ExploreStopCondition.ActionCount:
                        if (result.ActionsPerformed >= (int)value)
                        {
                            result.StopReason = ExploreStopCondition.ActionCount;
                            Debug.Log($"[UITEST] AutoExplore stopped - Action count reached ({(int)value})");
                            return result;
                        }
                        break;

                    case ExploreStopCondition.DeadEnd:
                        if (consecutiveFailures >= maxConsecutiveFailures)
                        {
                            result.ReachedDeadEnd = true;
                            result.StopReason = ExploreStopCondition.DeadEnd;
                            Debug.Log($"[UITEST] AutoExplore stopped - Dead end reached (no new elements after {maxConsecutiveFailures} attempts)");
                            return result;
                        }
                        break;
                }

                // Check for scene changes
                var newScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (newScene != currentScene)
                {
                    currentScene = newScene;
                    if (!result.VisitedScenes.Contains(newScene))
                        result.VisitedScenes.Add(newScene);
                    Debug.Log($"[UITEST] AutoExplore - Scene changed to: {newScene}");
                    consecutiveFailures = 0; // Reset on scene change
                }

                // Get clickable elements
                var clickables = await GetClickableElements(null);
                if (clickables.Count == 0)
                {
                    consecutiveFailures++;
                    Debug.Log($"[UITEST] AutoExplore - No clickable elements found (failure {consecutiveFailures}/{maxConsecutiveFailures})");

                    if (tryBackOnStuck && consecutiveFailures >= 2)
                    {
                        // Try to find and click a back button
                        await TryClickBackButton();
                    }

                    await UniTask.Delay((int)(delayBetweenActions * 1000));
                    continue;
                }

                // Prefer elements we haven't clicked yet
                var newElements = clickables.Where(c => !seenElements.Contains(GetElementKey(c))).ToList();
                Component target;

                if (newElements.Count > 0)
                {
                    int index = RandomGenerator.Next(newElements.Count);
                    target = newElements[index];
                    consecutiveFailures = 0;
                }
                else if (tryBackOnStuck)
                {
                    // All elements seen, try back button or random from all
                    consecutiveFailures++;
                    if (consecutiveFailures >= 2)
                    {
                        var clickedBack = await TryClickBackButton();
                        if (clickedBack)
                        {
                            result.ActionsPerformed++;
                            result.ClickedElements.Add("[Back/Exit]");
                            await UniTask.Delay((int)(delayBetweenActions * 1000));
                            continue;
                        }
                    }

                    // Fall back to random click
                    int index = RandomGenerator.Next(clickables.Count);
                    target = clickables[index];
                }
                else
                {
                    consecutiveFailures++;
                    int index = RandomGenerator.Next(clickables.Count);
                    target = clickables[index];
                }

                // Click the target
                var elementKey = GetElementKey(target);
                seenElements.Add(elementKey);
                result.ClickedElements.Add(target.gameObject.name);

                Debug.Log($"[UITEST] AutoExplore - Clicking '{target.gameObject.name}' (action {result.ActionsPerformed + 1})");
                await Click(target);
                result.ActionsPerformed++;

                await UniTask.Delay((int)(delayBetweenActions * 1000));
            }

            Debug.Log($"[UITEST] AutoExplore stopped - Application quit or cancelled");
            return result;
        }

        /// <summary>
        /// Auto-explores the UI by randomly clicking elements for a specified duration.
        /// Convenience method for time-based exploration.
        /// </summary>
        /// <param name="seconds">Duration in seconds to explore.</param>
        /// <param name="seed">Optional random seed for reproducibility.</param>
        /// <param name="delayBetweenActions">Delay between actions in seconds. Default is 0.5.</param>
        /// <returns>ExploreResult with statistics about the exploration session.</returns>
        protected async UniTask<ExploreResult> AutoExploreForSeconds(float seconds, int? seed = null, float delayBetweenActions = 0.5f)
        {
            return await AutoExplore(ExploreStopCondition.Time, seconds, seed, delayBetweenActions);
        }

        /// <summary>
        /// Auto-explores the UI by randomly clicking elements for a specified number of actions.
        /// Convenience method for action-count-based exploration.
        /// </summary>
        /// <param name="actionCount">Number of clicks to perform.</param>
        /// <param name="seed">Optional random seed for reproducibility.</param>
        /// <param name="delayBetweenActions">Delay between actions in seconds. Default is 0.5.</param>
        /// <returns>ExploreResult with statistics about the exploration session.</returns>
        protected async UniTask<ExploreResult> AutoExploreForActions(int actionCount, int? seed = null, float delayBetweenActions = 0.5f)
        {
            return await AutoExplore(ExploreStopCondition.ActionCount, actionCount, seed, delayBetweenActions);
        }

        /// <summary>
        /// Auto-explores the UI until reaching a dead end (no new clickable elements).
        /// Convenience method for exhaustive exploration.
        /// </summary>
        /// <param name="seed">Optional random seed for reproducibility.</param>
        /// <param name="delayBetweenActions">Delay between actions in seconds. Default is 0.5.</param>
        /// <param name="tryBackOnStuck">Whether to try back/exit buttons when stuck. Default is false.</param>
        /// <returns>ExploreResult with statistics about the exploration session.</returns>
        protected async UniTask<ExploreResult> AutoExploreUntilDeadEnd(int? seed = null, float delayBetweenActions = 0.5f, bool tryBackOnStuck = false)
        {
            return await AutoExplore(ExploreStopCondition.DeadEnd, 0, seed, delayBetweenActions, tryBackOnStuck);
        }

        /// <summary>
        /// Tries to click a back/exit/close button to navigate backwards.
        /// Searches for common back button patterns like "Back", "Close", "Exit", "Cancel", etc.
        /// </summary>
        /// <returns>True if a back button was found and clicked, false otherwise.</returns>
        /// <example>
        /// // Try to navigate back after exploration
        /// if (await TryClickBackButton())
        ///     Debug.Log("Successfully navigated back");
        /// </example>
        protected async UniTask<bool> TryClickBackButton()
        {
            foreach (var pattern in BackButtonPatterns)
            {
                var backButton = await Find<Selectable>(new Search().Name(pattern), throwIfMissing: false, seconds: 0.1f);
                if (backButton != null && backButton.interactable)
                {
                    Debug.Log($"[UITEST] TryClickBackButton - Found and clicking '{backButton.gameObject.name}'");
                    await Click(backButton);
                    return true;
                }
            }

            // Also try by text
            var textPatterns = new[] { "Back", "Close", "Exit", "Cancel", "Done", "OK", "X" };
            foreach (var text in textPatterns)
            {
                var button = await Find<Button>(new Search(text), throwIfMissing: false, seconds: 0.1f);
                if (button != null && button.interactable)
                {
                    Debug.Log($"[UITEST] TryClickBackButton - Found button with text '{text}'");
                    await Click(button);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets a unique key for an element based on its name and hierarchy position.
        /// </summary>
        private string GetElementKey(Component component)
        {
            if (component == null) return "";
            var go = component.gameObject;
            return $"{go.name}_{go.transform.GetSiblingIndex()}_{go.transform.parent?.name ?? "root"}";
        }

        #endregion

        #region Assertions

        // --- UI Element Assertions ---

        /// <summary>
        /// Asserts that an element matching the search exists.
        /// </summary>
        protected static void AssertExists(Search search, string message = null)
            => ActionExecutor.AssertExists(search, message);

        /// <summary>
        /// Asserts that no element matching the search exists.
        /// </summary>
        protected static void AssertNotExists(Search search, string message = null)
            => ActionExecutor.AssertNotExists(search, message);

        /// <summary>
        /// Asserts that the text content of an element matches expected value.
        /// </summary>
        protected static void AssertText(Search search, string expected, string message = null)
            => ActionExecutor.AssertText(search, expected, message);

        /// <summary>
        /// Asserts that a toggle is in the expected state.
        /// </summary>
        protected static void AssertToggle(Search search, bool expectedOn, string message = null)
            => ActionExecutor.AssertToggle(search, expectedOn, message);

        /// <summary>
        /// Asserts that a slider value is within expected range.
        /// </summary>
        protected static void AssertSlider(Search search, float expected, float tolerance = 0.01f, string message = null)
            => ActionExecutor.AssertSlider(search, expected, tolerance, message);

        /// <summary>
        /// Asserts that an element is interactable.
        /// </summary>
        protected static void AssertInteractable(Search search, bool expectedInteractable = true, string message = null)
            => ActionExecutor.AssertInteractable(search, expectedInteractable, message);

        // --- Static Path Assertions ---

        /// <summary>
        /// Asserts that a static path resolves to a truthy value.
        /// </summary>
        /// <param name="path">Dot-separated path (e.g., "GameManager.Instance.IsReady")</param>
        protected static void Assert(string path, string message = null)
            => ActionExecutor.Assert(path, message);

        /// <summary>
        /// Asserts that a static path equals an expected value.
        /// </summary>
        protected static void Assert<T>(string path, T expected, string message = null)
            => ActionExecutor.Assert(path, expected, message);

        /// <summary>
        /// Asserts that a numeric value at a static path is greater than expected.
        /// </summary>
        protected static void AssertGreater(string path, double greaterThan, string message = null)
            => ActionExecutor.AssertGreater(path, greaterThan, message);

        /// <summary>
        /// Asserts that a numeric value at a static path is less than expected.
        /// </summary>
        protected static void AssertLess(string path, double lessThan, string message = null)
            => ActionExecutor.AssertLess(path, lessThan, message);

        // --- Waits ---

        /// <summary>
        /// Waits until an element matching the search exists.
        /// </summary>
        protected static UniTask<bool> WaitFor(Search search, float timeout = 10f)
            => ActionExecutor.WaitFor(search, timeout);

        /// <summary>
        /// Waits until an element's text matches the expected value.
        /// </summary>
        protected static UniTask<bool> WaitFor(Search search, string expectedText, float timeout = 10f)
            => ActionExecutor.WaitFor(search, expectedText, timeout);

        /// <summary>
        /// Waits until a toggle is in the expected state.
        /// </summary>
        protected static UniTask<bool> WaitFor(Search search, bool expectedOn, float timeout = 10f)
            => ActionExecutor.WaitFor(search, expectedOn, timeout);

        /// <summary>
        /// Waits until no element matching the search exists.
        /// </summary>
        protected static UniTask<bool> WaitForNot(Search search, float timeout = 10f)
            => ActionExecutor.WaitForNot(search, timeout);

        /// <summary>
        /// Waits until a static path resolves to a truthy value.
        /// </summary>
        protected static UniTask<bool> WaitFor(string path, float timeout = 10f)
            => ActionExecutor.WaitFor(path, timeout);

        /// <summary>
        /// Waits until a static path equals an expected value.
        /// </summary>
        protected static UniTask<bool> WaitFor<T>(string path, T expected, float timeout = 10f)
            => ActionExecutor.WaitFor(path, expected, timeout);

        #endregion
    }
}
