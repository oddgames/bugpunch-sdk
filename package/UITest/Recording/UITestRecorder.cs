using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ODDGames.UITest
{
    public class UITestRecorder : MonoBehaviour
    {
        public static UITestRecorder Instance { get; private set; }

        public bool IsRecording { get; private set; }
        public UITestRecordingData CurrentRecording { get; private set; }

        public string LastRecordingFolder { get; private set; }

        const string TEST_DATA_SOURCE_PREF = "TOR.UITestRecorder.PendingTestDataSource";
        const string RECORDING_FOLDER_PREF = "TOR.UITestRecorder.PendingRecordingFolder";

        float recordingStartTime;
        StringBuilder logBuilder;
        int stepCounter;
        string screenshotsFolder;

        float lastStepTime;
        int frameCountSinceLastStep;
        float timeSinceLastStep;

        string currentSceneName;
        string previousSceneName;

        struct ActionFeedback
        {
            public string actionType;
            public string targetName;
            public Vector2 screenPosition;
            public float startTime;
            public Color color;
        }

        List<ActionFeedback> activeFeedbacks = new();
        const float FEEDBACK_DURATION = 1.5f;
        const float FEEDBACK_FADE_START = 0.8f;

        GUIStyle feedbackStyle;
        Texture2D circleTexture;
        Texture2D backgroundTexture;

        void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnEnable()
        {
            Application.logMessageReceived += OnLogMessage;
            SceneManager.sceneLoaded += OnSceneLoaded;
            currentSceneName = SceneManager.GetActiveScene().name;
        }

        void OnDisable()
        {
            Application.logMessageReceived -= OnLogMessage;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!IsRecording) return;

            previousSceneName = currentSceneName;
            currentSceneName = scene.name;

            if (previousSceneName != currentSceneName)
            {
                RecordSceneChange(previousSceneName, currentSceneName);
            }
        }

        void OnLogMessage(string message, string stackTrace, LogType type)
        {
            if (!IsRecording || logBuilder == null) return;

            float timestamp = Time.realtimeSinceStartup - recordingStartTime;
            logBuilder.AppendLine($"[{timestamp:F2}s] [{type}] {message}");
        }

        void OnGUI()
        {
            if (!Debug.isDebugBuild && !Application.isEditor) return;

            if (IsRecording && activeFeedbacks.Count > 0)
            {
                GUI.depth = -1000;
                DrawActionFeedbacks();
            }

            DrawRecorderUI();
        }

        void Update()
        {
            if (IsRecording)
            {
                frameCountSinceLastStep++;
                timeSinceLastStep += Time.unscaledDeltaTime;
            }

            if (activeFeedbacks.Count > 0)
            {
                float currentTime = Time.realtimeSinceStartup;
                for (int i = activeFeedbacks.Count - 1; i >= 0; i--)
                {
                    if (currentTime - activeFeedbacks[i].startTime >= FEEDBACK_DURATION)
                    {
                        activeFeedbacks.RemoveAt(i);
                    }
                }
            }
        }

        void EnsureStyles()
        {
            if (feedbackStyle == null)
            {
                feedbackStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            if (circleTexture == null)
            {
                circleTexture = CreateCircleTexture(64, Color.white);
            }

            if (backgroundTexture == null)
            {
                backgroundTexture = new Texture2D(1, 1);
                backgroundTexture.SetPixel(0, 0, Color.white);
                backgroundTexture.Apply();
            }
        }

        Texture2D CreateCircleTexture(int size, Color color)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float radius = size / 2f;
            float radiusSquared = radius * radius;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - radius;
                    float dy = y - radius;
                    float distSquared = dx * dx + dy * dy;

                    if (distSquared <= radiusSquared)
                    {
                        float dist = Mathf.Sqrt(distSquared);
                        float edge = radius - dist;
                        float alpha = Mathf.Clamp01(edge * 2f);
                        texture.SetPixel(x, y, new Color(color.r, color.g, color.b, alpha));
                    }
                    else
                    {
                        texture.SetPixel(x, y, Color.clear);
                    }
                }
            }

            texture.Apply();
            return texture;
        }

        void DrawActionFeedbacks()
        {
            EnsureStyles();

            float currentTime = Time.realtimeSinceStartup;

            foreach (var feedback in activeFeedbacks)
            {
                float elapsed = currentTime - feedback.startTime;
                if (elapsed >= FEEDBACK_DURATION) continue;

                float alpha = 1f;
                if (elapsed > FEEDBACK_FADE_START)
                {
                    alpha = 1f - ((elapsed - FEEDBACK_FADE_START) / (FEEDBACK_DURATION - FEEDBACK_FADE_START));
                }

                float scale = 1f + elapsed * 0.3f;
                float circleSize = 50 * scale;

                Vector2 guiPos = new Vector2(feedback.screenPosition.x, Screen.height - feedback.screenPosition.y);

                GUI.color = new Color(feedback.color.r, feedback.color.g, feedback.color.b, alpha * 0.6f);
                GUI.DrawTexture(
                    new Rect(guiPos.x - circleSize / 2, guiPos.y - circleSize / 2, circleSize, circleSize),
                    circleTexture
                );

                float labelWidth = 200;
                float labelHeight = 30;
                float labelY = guiPos.y - circleSize / 2 - labelHeight - 5;

                GUI.color = new Color(0, 0, 0, alpha * 0.8f);
                GUI.DrawTexture(
                    new Rect(guiPos.x - labelWidth / 2 - 2, labelY - 2, labelWidth + 4, labelHeight + 4),
                    backgroundTexture
                );

                GUI.color = new Color(feedback.color.r, feedback.color.g, feedback.color.b, alpha);
                feedbackStyle.normal.textColor = GUI.color;
                GUI.Label(
                    new Rect(guiPos.x - labelWidth / 2, labelY, labelWidth, labelHeight),
                    $"{feedback.actionType}: {feedback.targetName}",
                    feedbackStyle
                );

                GUI.color = Color.white;
            }
        }

        void ShowActionFeedback(string actionType, string targetName, Vector2 screenPosition, Color color)
        {
            activeFeedbacks.Add(new ActionFeedback
            {
                actionType = actionType,
                targetName = TruncateName(targetName, 20),
                screenPosition = screenPosition,
                startTime = Time.realtimeSinceStartup,
                color = color
            });
        }

        string TruncateName(string name, int maxLength)
        {
            if (string.IsNullOrEmpty(name)) return "???";
            return name.Length <= maxLength ? name : name.Substring(0, maxLength - 3) + "...";
        }

        void DrawRecorderUI()
        {
            float padding = 10;
            float buttonWidth = 150;
            float buttonHeight = 40;
            float y = Screen.height - buttonHeight - padding;

            if (!IsRecording)
            {
                if (GUI.Button(new Rect(padding, y, buttonWidth, buttonHeight), "Start Recording"))
                {
                    StartRecording();
                }
            }
            else
            {
                GUI.color = new Color(1, 0.3f, 0.3f);
                if (GUI.Button(new Rect(padding, y, buttonWidth, buttonHeight), "Stop Recording"))
                {
                    StopRecording();
                }
                GUI.color = Color.white;

                float infoY = y - 25;
                GUI.Label(new Rect(padding, infoY, 300, 20),
                    $"Recording: {CurrentRecording?.steps?.Count ?? 0} steps | {Time.realtimeSinceStartup - recordingStartTime:F1}s");

                if (GUI.Button(new Rect(padding + buttonWidth + 10, y, 100, buttonHeight), "Add Note"))
                {
                    RecordNote("Manual checkpoint");
                }
            }
        }

        public void StartRecording(string testName = null)
        {
            if (IsRecording) return;

            string baseName = testName ?? $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}";

            string testDataSource = null;
#if UNITY_EDITOR
            LastRecordingFolder = UnityEditor.EditorPrefs.GetString(RECORDING_FOLDER_PREF, null);
            UnityEditor.EditorPrefs.DeleteKey(RECORDING_FOLDER_PREF);

            testDataSource = UnityEditor.EditorPrefs.GetString(TEST_DATA_SOURCE_PREF, null);
            UnityEditor.EditorPrefs.DeleteKey(TEST_DATA_SOURCE_PREF);
#endif

            if (string.IsNullOrEmpty(LastRecordingFolder))
            {
                LastRecordingFolder = Path.Combine(Application.dataPath, "UITestBehaviours", "GeneratedTests", baseName);
            }

            Directory.CreateDirectory(LastRecordingFolder);

            screenshotsFolder = Path.Combine(LastRecordingFolder, "screenshots");
            Directory.CreateDirectory(screenshotsFolder);

            stepCounter = 0;
            frameCountSinceLastStep = 0;
            timeSinceLastStep = 0;
            activeFeedbacks.Clear();

            CurrentRecording = new UITestRecordingData
            {
                testName = baseName,
                description = "",
                testDataSourcePath = testDataSource
            };

            logBuilder = new StringBuilder();
            recordingStartTime = Time.realtimeSinceStartup;
            IsRecording = true;

            EnsureInputInterceptor();

            Debug.Log($"[UITestRecorder] Started recording: {baseName}");
            Debug.Log($"[UITestRecorder] Recording folder: {LastRecordingFolder}");
        }

        void EnsureInputInterceptor()
        {
            if (UITestInputInterceptor.Instance == null)
            {
                var go = new GameObject("UITestInputInterceptor");
                go.transform.SetParent(transform);
                go.AddComponent<UITestInputInterceptor>();
            }
        }

        public void StopRecording()
        {
            if (!IsRecording) return;

            IsRecording = false;
            CurrentRecording.totalDuration = Time.realtimeSinceStartup - recordingStartTime;

            File.WriteAllText(Path.Combine(LastRecordingFolder, "steps.log"), CurrentRecording.ToStepsLog());
            File.WriteAllText(Path.Combine(LastRecordingFolder, "log.txt"), logBuilder.ToString());
            File.WriteAllText(Path.Combine(LastRecordingFolder, "prompt.md"), UITestPromptGenerator.GeneratePrompt(CurrentRecording));

#if UNITY_EDITOR
            UnityEditor.EditorPrefs.SetString("TOR.UITestRecorder.LastRecordingFolder", LastRecordingFolder);
#endif

            Debug.Log($"[UITestRecorder] Recording saved to: {LastRecordingFolder}");
            Debug.Log($"[UITestRecorder] Total steps: {CurrentRecording.steps.Count}");
        }

        public void RecordClick(string targetName, string targetPath, string targetType, string parentName, Vector2 screenPosition, string textContent)
        {
            RecordClick(targetName, targetPath, targetType, parentName, screenPosition, textContent, 0, 1, null);
        }

        public void RecordClick(string targetName, string targetPath, string targetType, string parentName, Vector2 screenPosition, string textContent, int siblingIndex, int siblingCount, string grandparentName)
        {
            if (!IsRecording) return;

            string displayName = !string.IsNullOrEmpty(textContent) ? textContent : targetName;
            string siblingInfo = siblingCount > 1 ? $" [{siblingIndex + 1}/{siblingCount}]" : "";
            ShowActionFeedback("CLICK", displayName + siblingInfo, screenPosition, new Color(0.2f, 0.8f, 0.2f));

            Debug.Log($"[UITestRecorder] CLICK recorded - Name: '{targetName}' Path: '{targetPath}' Text: '{textContent}' Sibling: {siblingIndex + 1}/{siblingCount}");

            string screenshotPath = CaptureScreenshot($"step_{stepCounter:D4}_click");

            AddStep(new UITestRecordingStep
            {
                type = UITestRecordingStep.StepType.Click,
                timestamp = Time.realtimeSinceStartup - recordingStartTime,
                targetName = targetName,
                targetPath = targetPath,
                targetType = targetType,
                parentName = parentName,
                screenPosition = screenPosition,
                textContent = textContent,
                screenshotPath = screenshotPath,
                siblingIndex = siblingIndex,
                siblingCount = siblingCount,
                grandparentName = grandparentName
            });
        }

        public void RecordHold(string targetName, string targetPath, string targetType, string parentName, Vector2 screenPosition, float duration)
        {
            RecordHold(targetName, targetPath, targetType, parentName, screenPosition, duration, 0, 1, null);
        }

        public void RecordHold(string targetName, string targetPath, string targetType, string parentName, Vector2 screenPosition, float duration, int siblingIndex, int siblingCount, string grandparentName)
        {
            if (!IsRecording) return;

            string siblingInfo = siblingCount > 1 ? $" [{siblingIndex + 1}/{siblingCount}]" : "";
            ShowActionFeedback($"HOLD {duration:F1}s", targetName + siblingInfo, screenPosition, new Color(0.8f, 0.6f, 0.2f));

            string screenshotPath = CaptureScreenshot($"step_{stepCounter:D4}_hold");

            AddStep(new UITestRecordingStep
            {
                type = UITestRecordingStep.StepType.Hold,
                timestamp = Time.realtimeSinceStartup - recordingStartTime,
                duration = duration,
                targetName = targetName,
                targetPath = targetPath,
                targetType = targetType,
                parentName = parentName,
                screenPosition = screenPosition,
                screenshotPath = screenshotPath,
                siblingIndex = siblingIndex,
                siblingCount = siblingCount,
                grandparentName = grandparentName
            });
        }

        public void RecordTextInput(string targetName, string targetPath, string inputText)
        {
            if (!IsRecording) return;

            ShowActionFeedback("INPUT", inputText, new Vector2(Screen.width / 2f, Screen.height / 2f), new Color(0.2f, 0.6f, 0.9f));

            string screenshotPath = CaptureScreenshot($"step_{stepCounter:D4}_input");

            AddStep(new UITestRecordingStep
            {
                type = UITestRecordingStep.StepType.TextInput,
                timestamp = Time.realtimeSinceStartup - recordingStartTime,
                targetName = targetName,
                targetPath = targetPath,
                inputText = inputText,
                screenshotPath = screenshotPath
            });
        }

        public void RecordNote(string note)
        {
            if (!IsRecording) return;

            ShowActionFeedback("NOTE", note, new Vector2(Screen.width / 2f, Screen.height / 2f), new Color(0.9f, 0.9f, 0.2f));

            string screenshotPath = CaptureScreenshot($"step_{stepCounter:D4}_note");

            AddStep(new UITestRecordingStep
            {
                type = UITestRecordingStep.StepType.Note,
                timestamp = Time.realtimeSinceStartup - recordingStartTime,
                additionalContext = note,
                screenshotPath = screenshotPath
            });
        }

        public void RecordSceneChange(string fromScene, string toScene)
        {
            if (!IsRecording) return;

            ShowActionFeedback("SCENE", $"{fromScene} → {toScene}", new Vector2(Screen.width / 2f, Screen.height / 2f), new Color(0.8f, 0.2f, 0.8f));

            string screenshotPath = CaptureScreenshot($"step_{stepCounter:D4}_scene");

            AddStep(new UITestRecordingStep
            {
                type = UITestRecordingStep.StepType.SceneChange,
                timestamp = Time.realtimeSinceStartup - recordingStartTime,
                previousScene = fromScene,
                newScene = toScene,
                screenshotPath = screenshotPath
            });
        }

        public void RecordKeyPress(KeyCode key, string targetName, string targetPath)
        {
            if (!IsRecording) return;

            ShowActionFeedback("KEY", key.ToString(), new Vector2(Screen.width / 2f, Screen.height / 2f), new Color(0.6f, 0.4f, 0.9f));

            Debug.Log($"[UITestRecorder] KEYPRESS recorded - Key: {key} Target: '{targetName}'");

            string screenshotPath = CaptureScreenshot($"step_{stepCounter:D4}_keypress");

            AddStep(new UITestRecordingStep
            {
                type = UITestRecordingStep.StepType.KeyPress,
                timestamp = Time.realtimeSinceStartup - recordingStartTime,
                keyCode = key.ToString(),
                targetName = targetName,
                targetPath = targetPath,
                screenshotPath = screenshotPath
            });
        }

        public void RecordDrag(string targetName, string targetPath, string targetType, string parentName, Vector2 startPosition, Vector2 endPosition, float duration, string scrollRectName, int siblingIndex, int siblingCount, string grandparentName)
        {
            if (!IsRecording) return;

            Vector2 delta = endPosition - startPosition;
            string direction = GetDragDirection(delta);
            string displayName = !string.IsNullOrEmpty(scrollRectName) ? scrollRectName : targetName;
            ShowActionFeedback($"DRAG {direction}", displayName, startPosition, new Color(0.9f, 0.5f, 0.2f));

            Debug.Log($"[UITestRecorder] DRAG recorded - Start: {startPosition} End: {endPosition} Delta: {delta} Target: '{targetName}' ScrollRect: '{scrollRectName}'");

            string screenshotPath = CaptureScreenshot($"step_{stepCounter:D4}_drag");

            AddStep(new UITestRecordingStep
            {
                type = UITestRecordingStep.StepType.Drag,
                timestamp = Time.realtimeSinceStartup - recordingStartTime,
                duration = duration,
                targetName = targetName,
                targetPath = targetPath,
                targetType = targetType,
                parentName = parentName,
                screenPosition = startPosition,
                dragStartPosition = startPosition,
                dragEndPosition = endPosition,
                dragDelta = delta,
                additionalContext = scrollRectName,
                screenshotPath = screenshotPath,
                siblingIndex = siblingIndex,
                siblingCount = siblingCount,
                grandparentName = grandparentName
            });
        }

        public void RecordScroll(string targetName, string targetPath, string targetType, string parentName, Vector2 position, float scrollDelta, int siblingIndex, int siblingCount, string grandparentName)
        {
            if (!IsRecording) return;

            string direction = scrollDelta > 0 ? "UP" : "DOWN";
            ShowActionFeedback($"SCROLL {direction}", targetName, position, new Color(0.5f, 0.9f, 0.5f));

            Debug.Log($"[UITestRecorder] SCROLL recorded - Position: {position} Delta: {scrollDelta} Target: '{targetName}'");

            string screenshotPath = CaptureScreenshot($"step_{stepCounter:D4}_scroll");

            AddStep(new UITestRecordingStep
            {
                type = UITestRecordingStep.StepType.Scroll,
                timestamp = Time.realtimeSinceStartup - recordingStartTime,
                targetName = targetName,
                targetPath = targetPath,
                targetType = targetType,
                parentName = parentName,
                screenPosition = position,
                dragDelta = new Vector2(0, scrollDelta * 100),
                screenshotPath = screenshotPath,
                siblingIndex = siblingIndex,
                siblingCount = siblingCount,
                grandparentName = grandparentName
            });
        }

        string GetDragDirection(Vector2 delta)
        {
            if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
            {
                return delta.x > 0 ? "RIGHT" : "LEFT";
            }
            return delta.y > 0 ? "UP" : "DOWN";
        }

        string CaptureScreenshot(string filename)
        {
            string relativePath = $"screenshots/{filename}.png";
            string fullPath = Path.Combine(LastRecordingFolder, relativePath);

            ScreenCapture.CaptureScreenshot(fullPath);

            return relativePath;
        }

        void AddStep(UITestRecordingStep step)
        {
            step.timeSinceLastStep = timeSinceLastStep;
            step.avgFpsSinceLastStep = timeSinceLastStep > 0 ? frameCountSinceLastStep / timeSinceLastStep : 0;

            frameCountSinceLastStep = 0;
            timeSinceLastStep = 0;

            stepCounter++;
            CurrentRecording.steps.Add(step);
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Window/UI Test Behaviours/Create Test Recorder")]
        static void CreateRecorder()
        {
            if (Instance != null)
            {
                Debug.Log("[UITestRecorder] Recorder already exists");
                return;
            }

            var go = new GameObject("UITestRecorder");
            go.AddComponent<UITestRecorder>();
            Debug.Log("[UITestRecorder] Created recorder instance");
        }
#endif
    }
}
