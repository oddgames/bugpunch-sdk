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
            Raycastable = 4,
            All = Active | Enabled | Raycastable
        }

        public static int Interval { get; set; } = 500;
        static readonly List<Type> clickablesList = new() { typeof(Selectable) };
        static Type[] clickablesArray = { typeof(Selectable) };
        public static Type[] Clickables => clickablesArray;
        public static List<BaseRaycaster> Raycasters { get; } = new();

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

        public static void RegisterRaycaster(BaseRaycaster raycaster)
        {
            if (raycaster != null && !Raycasters.Contains(raycaster))
                Raycasters.Add(raycaster);
        }

        public static void UnregisterRaycaster(BaseRaycaster raycaster)
        {
            Raycasters.Remove(raycaster);
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

        static void RaycastAll(PointerEventData pointer, List<RaycastResult> results)
        {
            results.Clear();
            if (Raycasters.Count > 0)
            {
                foreach (var raycaster in Raycasters)
                {
                    if (raycaster != null && raycaster.IsActive())
                        raycaster.Raycast(pointer, results);
                }
                results.Sort((a, b) => b.depth.CompareTo(a.depth));
            }
            else
            {
                EventSystem.current.RaycastAll(pointer, results);
            }
        }

        static float lastActionTime;

        static async UniTask WaitForInterval()
        {
            float elapsed = (Time.realtimeSinceStartup - lastActionTime) * 1000f;
            if (elapsed < Interval)
            {
                int remaining = Interval - (int)elapsed;
                await UniTask.Delay(remaining, true, PlayerLoopTiming.Update, TestCancellationToken);
            }
        }

        static void ActionComplete()
        {
            lastActionTime = Time.realtimeSinceStartup;
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

            if ((availability & Availability.Raycastable) != 0)
            {
                var camera = Camera.main;
                if (camera == null)
                    return false;

                RectTransform rt = go.GetComponent<RectTransform>();
                if (rt != null)
                {
                    Vector3 screenPoint = RectTransformUtility.WorldToScreenPoint(camera, rt.position);
                    var pointer = new PointerEventData(EventSystem.current) { position = screenPoint };
                    var results = new List<RaycastResult>();
                    RaycastAll(pointer, results);
                    bool found = results.Any(r => r.gameObject == go || r.gameObject.transform.IsChildOf(go.transform));
                    if (!found)
                        return false;
                }
                else
                {
                    Ray ray = camera.ScreenPointToRay(camera.WorldToScreenPoint(go.transform.position));
                    if (!Physics.Raycast(ray, out RaycastHit hit))
                        return false;
                    if (hit.collider.gameObject != go && !hit.collider.transform.IsChildOf(go.transform))
                        return false;
                }
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
        [Sirenix.OdinInspector.Button]
        void RunTest()
        {
            var attr = GetType().GetCustomAttribute<UITestAttribute>();
            var dataMode = attr != null ? attr.DataMode : TestDataMode.Ask;
            int scenario = Scenario;

            UITestStartWindow.Show(GetType().Name, dataMode, (runName, clearData) =>
            {
                TestRunName = runName;

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
                UnityEditor.SessionState.SetString("GAME_LOOP_TEST_NAME", runName);
                UnityEditor.EditorApplication.isPlaying = true;
            });
        }

        class UITestStartWindow : UnityEditor.EditorWindow
        {
            string runName = "";
            bool clearData;
            Action<string, bool> onStart;
            TestDataMode dataMode;

            public static void Show(string defaultName, TestDataMode mode, Action<string, bool> callback)
            {
                var window = GetWindow<UITestStartWindow>(true, "Start UI Test", true);
                window.runName = defaultName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm");
                window.dataMode = mode;
                window.onStart = callback;
                window.minSize = new Vector2(350, 120);
                window.maxSize = new Vector2(350, 120);
                window.ShowUtility();
            }

            void OnGUI()
            {
                GUILayout.Space(10);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Run Name:", GUILayout.Width(70));
                runName = GUILayout.TextField(runName);
                GUILayout.EndHorizontal();

                GUILayout.Space(5);

                if (dataMode == TestDataMode.Ask)
                {
                    clearData = GUILayout.Toggle(clearData, "Clear existing data");
                }

                GUILayout.Space(15);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Start Test", GUILayout.Height(30)))
                {
                    Close();
                    if (onStart != null)
                        onStart.Invoke(runName, clearData);
                }
                if (GUILayout.Button("Cancel", GUILayout.Height(30)))
                {
                    Close();
                }
                GUILayout.EndHorizontal();
            }
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
                    bool isRunnerControlled = UnityEditor.SessionState.GetBool("GAME_LOOP_TEST_RUNNER", false);

                    if (isRunnerControlled)
                    {
                        UnityEditor.EditorApplication.isPlaying = false;
                    }
                    else if (Application.isBatchMode)
                    {
                        UnityEditor.EditorApplication.Exit(0);
                    }
                    else
                    {
                        UnityEditor.EditorApplication.isPlaying = false;
                    }
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

        protected async UniTask Wait(int seconds = 1)
        {
            await UniTask.Delay(seconds * 1000, true, PlayerLoopTiming.Update, TestCancellationToken);
        }

        protected async UniTask Wait(string[] search, int seconds = 10)
        {
            await UniTask.Delay(Interval, true, PlayerLoopTiming.Update, TestCancellationToken);

            Debug.Log($"[UITEST] Wait ({seconds}) [{string.Join(",", search)}]");

            await Find<MonoBehaviour>(search, true, seconds);
        }

        protected async UniTask Wait(string search, int seconds = 10)
        {
            await UniTask.Delay(Interval, true, PlayerLoopTiming.Update, TestCancellationToken);

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
                    return;

                await UniTask.Delay(Interval, true, PlayerLoopTiming.Update, TestCancellationToken);
            }

            TestCancellationToken.ThrowIfCancellationRequested();
            throw new System.TimeoutException($"Condition '{description}' not met within {seconds} seconds");
        }

        protected async UniTask SceneChange(float seconds = 30, float recentThreshold = 1f)
        {
            await WaitForInterval();

            string startScene = SceneManager.GetActiveScene().name;
            float startTime = Time.realtimeSinceStartup;

            Debug.Log($"[UITEST] SceneChange - waiting for scene change from '{startScene}' (timeout: {seconds}s)");

            float timeSinceLastChange = startTime - lastSceneChangeTime;
            if (timeSinceLastChange < recentThreshold && lastKnownScene != startScene)
            {
                Debug.Log($"[UITEST] SceneChange - scene recently changed ({timeSinceLastChange:F2}s ago) to '{lastKnownScene}'");
                ActionComplete();
                return;
            }

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying && !TestCancellationToken.IsCancellationRequested)
            {
                string currentScene = SceneManager.GetActiveScene().name;
                if (currentScene != startScene)
                {
                    Debug.Log($"[UITEST] SceneChange - scene changed to '{currentScene}'");
                    ActionComplete();
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
                    return;
                }

                Debug.Log($"[UITEST] WaitFramerate - current {currentFps:F1} FPS, waiting for {averageFps}...");
            }

            TestCancellationToken.ThrowIfCancellationRequested();
            throw new System.TimeoutException($"Framerate did not reach {averageFps} FPS within {timeout} seconds");
        }

        protected async UniTask TextInput(string search, string input, float seconds = 10)
        {
            await WaitForInterval();

            Debug.Log($"[UITEST] TextInput ({seconds}) [{search}] {input}");

            var t = await Find<InputField>(search, true, seconds);

            t.text = input;
            ActionComplete();
        }


        protected async UniTask ClickAny(params string[] searches)
        {
            await ClickAny(searches, throwIfMissing: true, seconds: 5);
        }

        protected async UniTask ClickAny(string[] searches, bool throwIfMissing = true, float seconds = 5, Availability availability = Availability.Active | Availability.Enabled)
        {
            await WaitForInterval();

            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying)
            {
                var b = await Find<IPointerClickHandler>(searches, false, 0.5f, availability, Clickables);

                if (b != null)
                {
                    await SimulateClick(b);
                    ActionComplete();
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

                var pointer = new PointerEventData(EventSystem.current);

                if (target is UIBehaviour ui && ui.TryGetComponent<RectTransform>(out RectTransform rt))
                    pointer.position = rt.position;

                if (target is IPointerDownHandler)
                    ExecuteEvents.Execute(component.gameObject, pointer, ExecuteEvents.pointerDownHandler);

                await UniTask.Delay(20, true);

                if (target is IPointerUpHandler)
                    ExecuteEvents.Execute(component.gameObject, pointer, ExecuteEvents.pointerUpHandler);

                if (target is IPointerClickHandler)
                    ExecuteEvents.Execute(component.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            }
        }


        protected async UniTask Hold(string search, float seconds, bool throwIfMissing = true, float searchTime = 10, Availability availability = Availability.Active | Availability.Enabled)
        {
            await WaitForInterval();

            Debug.Log($"[UITEST] Hold ({searchTime}) [{search}] for {seconds}s");

            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var b = await Find<IPointerDownHandler>(search, false, 0.5f, availability, Clickables);

                if (b != null && b is UnityEngine.Component c1)
                {
                    var pointer = new PointerEventData(EventSystem.current);
                    ExecuteEvents.Execute(c1.gameObject, pointer, ExecuteEvents.pointerDownHandler);

                    await UniTask.Delay(TimeSpan.FromSeconds(seconds), true);

                    if (b is IPointerUpHandler && b is UnityEngine.Component c2)
                        ExecuteEvents.Execute(c2.gameObject, pointer, ExecuteEvents.pointerUpHandler);

                    ActionComplete();
                    return;
                }

                await UniTask.Delay(100, true);
            }

            if (throwIfMissing)
            {
                throw new TestException($"Hold on '{search}' could not find any matching target within {searchTime}s");
            }
        }

        protected async UniTask Click(string search = "", bool throwIfMissing = true, float searchTime = 10, int repeat = 0, Availability availability = Availability.Active | Availability.Enabled, int index = 0)
        {
            await Click(new string[] { search }, throwIfMissing, searchTime, repeat, availability, index);
        }

        protected async UniTask Click(string[] search, bool throwIfMissing = true, float searchTime = 10, int repeat = 0, Availability availability = Availability.Active | Availability.Enabled, int index = 0)
        {
            await WaitForInterval();

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
                            ActionComplete();
                            goto nextRepeat;
                        }
                    }
                    else
                    {
                        var b = await Find<IPointerClickHandler>(search, false, 0.5f, availability, Clickables);

                        if (b != null)
                        {
                            await SimulateClick(b);
                            ActionComplete();
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
            {
                return false;
            }

            int wildcardCount = wildcardPattern.Count(x => x.Equals('*'));

            if (wildcardCount <= 0)
            {
                return subject.Equals(wildcardPattern, StringComparison.CurrentCultureIgnoreCase);
            }
            else if (wildcardCount == 1)
            {
                string newWildcardPattern = wildcardPattern.Replace("*", "");

                if (wildcardPattern.StartsWith("*"))
                {
                    return subject.EndsWith(newWildcardPattern, StringComparison.CurrentCultureIgnoreCase);
                }
                else if (wildcardPattern.EndsWith("*"))
                {
                    return subject.StartsWith(newWildcardPattern, StringComparison.CurrentCultureIgnoreCase);
                }
            }

            return false;

        }

        protected async UniTask Click(bool throwIfMissing = true)
        {
            await WaitForInterval();

            Debug.Log($"[UITEST] Click (screen center)");

            var pointer = new PointerEventData(EventSystem.current)
            {
                position = new Vector2(Screen.width / 2, Screen.height / 2)
            };

            var raycastResults = new List<RaycastResult>();
            RaycastAll(pointer, raycastResults);

            if (raycastResults.Count > 0)
            {
                var target = raycastResults[0].gameObject;
                ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
                ActionComplete();
                return;
            }

            if (throwIfMissing)
            {
                throw new TestException("Click (screen center) could not find any target at screen center");
            }
        }

        protected async UniTask Drag(Vector2 direction, float duration = 0.5f)
        {
            Vector2 startPos = new Vector2(Screen.width / 2f, Screen.height / 2f);
            await DragFromTo(startPos, startPos + direction, duration);
        }

        protected async UniTask Drag(string search, Vector2 direction, float duration = 0.5f, bool throwIfMissing = true, float searchTime = 10)
        {
            await WaitForInterval();

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
            await WaitForInterval();

            Debug.Log($"[UITEST] DragFromTo ({duration}s) from ({startPos.x:F0},{startPos.y:F0}) to ({endPos.x:F0},{endPos.y:F0})");

            var pointer = new PointerEventData(EventSystem.current)
            {
                position = startPos,
                pressPosition = startPos,
                button = PointerEventData.InputButton.Left
            };

            var raycastResults = new List<RaycastResult>();
            RaycastAll(pointer, raycastResults);

            GameObject dragTarget = null;
            if (raycastResults.Count > 0)
            {
                dragTarget = raycastResults[0].gameObject;
            }

            if (dragTarget != null)
            {
                ExecuteEvents.Execute(dragTarget, pointer, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(dragTarget, pointer, ExecuteEvents.beginDragHandler);
            }

            float elapsed = 0;
            int steps = Mathf.Max(10, (int)(duration * 60));
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                pointer.position = Vector2.Lerp(startPos, endPos, t);
                pointer.delta = (endPos - startPos) / steps;

                if (dragTarget != null)
                {
                    ExecuteEvents.Execute(dragTarget, pointer, ExecuteEvents.dragHandler);
                }

                await UniTask.Delay((int)(duration * 1000 / steps), true);
                elapsed += duration / steps;
            }

            if (dragTarget != null)
            {
                ExecuteEvents.Execute(dragTarget, pointer, ExecuteEvents.endDragHandler);
                ExecuteEvents.Execute(dragTarget, pointer, ExecuteEvents.pointerUpHandler);
            }

            ActionComplete();
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

            while (Time.realtimeSinceStartup - startTime < seconds && Application.isPlaying)
            {
                if (target != null)
                    target.OnPointerDown(new PointerEventData(EventSystem.current));

                await UniTask.Delay(UnityEngine.Random.Range(300, Mathf.Min(3000, seconds * 1000)), true);

                if (target is IPointerUpHandler upHandler)
                    upHandler.OnPointerUp(new PointerEventData(EventSystem.current));

                await UniTask.Delay(UnityEngine.Random.Range(10, 100), true);
            }
        }



        protected async UniTask ClickAny(string search, float seconds = 10, bool throwIfMissing = true, Availability availability = Availability.Active | Availability.Enabled)
        {
            await WaitForInterval();

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
                            ActionComplete();
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
                Debug.Log($"[UITEST] Find ({seconds}) {typeof(T).Name}");

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

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying && !TestCancellationToken.IsCancellationRequested)
            {
                Debug.Log($"[UITEST] Find ({seconds}) [{string.Join(',', searches)}] {typeof(T).Name}");

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

                            RectTransform rt = c.GetComponent<RectTransform>();
                            if (rt == null)
                                continue;

                            Vector3 screenPoint = RectTransformUtility.WorldToScreenPoint(null, rt.position);
                            var pointer = new PointerEventData(EventSystem.current) { position = screenPoint };
                            var results = new List<RaycastResult>();
                            RaycastAll(pointer, results);

                            bool isTop = results.Count > 0 && (results[0].gameObject == c.gameObject || results[0].gameObject.transform.IsChildOf(c.transform) || c.transform.IsChildOf(results[0].gameObject.transform));

                            if (isTop)
                            {
                                Debug.Log($"[UITEST] Match (top): '{c.name}' Path: '{GetHierarchyPath(c.transform)}'");
                                topMatches.Add(match);
                            }
                            else
                            {
                                string topHit = results.Count > 0 ? results[0].gameObject.name : "none";
                                Debug.Log($"[UITEST] Match (blocked by '{topHit}'): '{c.name}' Path: '{GetHierarchyPath(c.transform)}'");
                            }
                        }
                    }

                    if (topMatches.Count > 0)
                        return topMatches[0];
                }

                await UniTask.Delay(Interval, true, PlayerLoopTiming.Update, TestCancellationToken);
            }

            TestCancellationToken.ThrowIfCancellationRequested();

            if (throwIfMissing)
                throw new System.TimeoutException($"Unable to locate {typeof(T).Name} '{string.Join(',', searches)}' in {seconds} seconds");

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
                Debug.Log($"[UITEST] FindAll ({seconds}) {typeof(T).Name} {search}");

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
