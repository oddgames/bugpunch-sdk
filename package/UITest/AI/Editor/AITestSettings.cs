#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

namespace ODDGames.UITest.AI.Editor
{
    /// <summary>
    /// Project settings for AI Testing configuration.
    /// </summary>
    public class AITestSettings : ScriptableObject
    {
        private const string SettingsPath = "Assets/Editor/AITestSettings.asset";

        [Header("Local Models")]
        [Tooltip("Enable LM Studio for local model inference")]
        public bool lmStudioEnabled = true;

        [Tooltip("LM Studio endpoint (typically localhost:1234)")]
        public string lmStudioEndpoint = "http://localhost:1234";

        [Tooltip("Model name to use in LM Studio")]
        public string lmStudioModel = "";

        [Header("Gemini (Cloud)")]
        [Tooltip("Gemini API key from Google AI Studio")]
        public string geminiApiKey = "";

        [Header("Defaults")]
        [Tooltip("Default starting model tier for new tests")]
        public ModelTier defaultModelTier = ModelTier.LocalFast;

        [Tooltip("Default timeout in seconds")]
        public float defaultTimeout = 180f;

        [Tooltip("Default maximum actions per test")]
        public int defaultMaxActions = 50;

        [Tooltip("Default delay between actions")]
        public float defaultActionDelay = 0.3f;

        [Header("Global Knowledge")]
        [TextArea(5, 15)]
        [Tooltip("Context sent to AI for ALL tests. Describe your app's general UI patterns.")]
        public string globalKnowledge = "";

        [Tooltip("Common patterns that apply to most tests")]
        public string[] commonPatterns = new string[0];

        [Header("Performance")]
        [Tooltip("Send screenshots to AI for visual reasoning. Disable for faster text-only mode.")]
        public bool sendScreenshotsToAI = false;

        [Tooltip("Use text-only mode by default (faster, no vision). The AI relies solely on the element list.")]
        public bool preferTextOnlyMode = false;

        [Header("Debug")]
        [Tooltip("Show debug panel during test execution")]
        public bool showDebugPanel = true;

        [Tooltip("Save screenshots during tests (for debugging, independent of sending to AI)")]
        public bool saveScreenshots = true;

        [Tooltip("Verbose logging")]
        public bool verboseLogging = false;

        private static AITestSettings instance;

        /// <summary>
        /// Gets the singleton settings instance.
        /// </summary>
        public static AITestSettings Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = LoadOrCreate();
                }
                return instance;
            }
        }

        /// <summary>
        /// Gets the global knowledge configuration.
        /// </summary>
        public GlobalKnowledge GetGlobalKnowledge()
        {
            var knowledge = new GlobalKnowledge
            {
                context = globalKnowledge,
                defaultModelTier = defaultModelTier,
                defaultTimeoutSeconds = defaultTimeout,
                defaultMaxActions = defaultMaxActions
            };

            if (commonPatterns != null)
            {
                knowledge.commonPatterns.AddRange(commonPatterns);
            }

            return knowledge;
        }

        /// <summary>
        /// Creates model providers based on settings.
        /// </summary>
        public AITestRunnerConfig CreateRunnerConfig()
        {
            var shouldSendScreenshots = sendScreenshotsToAI && !preferTextOnlyMode;
            Debug.Log($"[AITest] Creating runner config: sendScreenshotsToAI={sendScreenshotsToAI}, preferTextOnlyMode={preferTextOnlyMode}, SendScreenshots={shouldSendScreenshots}");

            var config = new AITestRunnerConfig
            {
                EnableHistoryReplay = true,
                SendScreenshots = shouldSendScreenshots
            };

            // LM Studio provider
            if (lmStudioEnabled && !string.IsNullOrEmpty(lmStudioEndpoint))
            {
                config.LMStudioProvider = new LMStudioProvider(lmStudioEndpoint, lmStudioModel);
            }

            // Gemini providers
            if (!string.IsNullOrEmpty(geminiApiKey))
            {
                config.GeminiFlashLiteProvider = new GeminiProvider(geminiApiKey, ModelTier.GeminiFlashLite);
                config.GeminiFlashProvider = new GeminiProvider(geminiApiKey, ModelTier.GeminiFlash);
                config.GeminiProProvider = new GeminiProvider(geminiApiKey, ModelTier.GeminiPro);
            }

            return config;
        }

        private static AITestSettings LoadOrCreate()
        {
            var settings = AssetDatabase.LoadAssetAtPath<AITestSettings>(SettingsPath);

            if (settings == null)
            {
                settings = CreateInstance<AITestSettings>();

                // Ensure directory exists
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                AssetDatabase.CreateAsset(settings, SettingsPath);
                AssetDatabase.SaveAssets();
            }

            return settings;
        }

        public void Save()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }
    }

    /// <summary>
    /// Settings provider for Project Settings window.
    /// </summary>
    public class AITestSettingsProvider : SettingsProvider
    {
        private SerializedObject serializedSettings;
        private AITestSettings settings;

        public AITestSettingsProvider(string path, SettingsScope scope)
            : base(path, scope) { }

        public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement)
        {
            settings = AITestSettings.Instance;
            serializedSettings = new SerializedObject(settings);
        }

        public override void OnGUI(string searchContext)
        {
            if (serializedSettings == null || settings == null)
            {
                settings = AITestSettings.Instance;
                serializedSettings = new SerializedObject(settings);
            }

            serializedSettings.Update();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("AI Testing Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Local Models Section
            EditorGUILayout.LabelField("Local Models", EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(serializedSettings.FindProperty("lmStudioEnabled"),
                new GUIContent("Enable LM Studio"));

            if (settings.lmStudioEnabled)
            {
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("lmStudioEndpoint"),
                    new GUIContent("Endpoint"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("lmStudioModel"),
                    new GUIContent("Model Name"));

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(20);
                if (GUILayout.Button("Test Connection", GUILayout.Width(120)))
                {
                    TestLMStudioConnection();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(10);

            // Gemini Section
            EditorGUILayout.LabelField("Gemini (Cloud)", EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(serializedSettings.FindProperty("geminiApiKey"),
                new GUIContent("API Key"));

            if (!string.IsNullOrEmpty(settings.geminiApiKey))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(20);
                if (GUILayout.Button("Test Connection", GUILayout.Width(120)))
                {
                    TestGeminiConnection();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(10);

            // Defaults Section
            EditorGUILayout.LabelField("Defaults", EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(serializedSettings.FindProperty("defaultModelTier"),
                new GUIContent("Starting Tier"));
            EditorGUILayout.PropertyField(serializedSettings.FindProperty("defaultTimeout"),
                new GUIContent("Timeout (seconds)"));
            EditorGUILayout.PropertyField(serializedSettings.FindProperty("defaultMaxActions"),
                new GUIContent("Max Actions"));
            EditorGUILayout.PropertyField(serializedSettings.FindProperty("defaultActionDelay"),
                new GUIContent("Action Delay"));

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(10);

            // Global Knowledge Section
            EditorGUILayout.LabelField("Global Knowledge", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(
                "This context is sent to the AI for ALL tests. Describe your app's general UI patterns, navigation structure, and any important information.",
                MessageType.Info);

            EditorGUILayout.PropertyField(serializedSettings.FindProperty("globalKnowledge"),
                new GUIContent("Context"));
            EditorGUILayout.PropertyField(serializedSettings.FindProperty("commonPatterns"),
                new GUIContent("Common Patterns"));

            EditorGUILayout.Space(10);

            // Performance Section
            EditorGUILayout.LabelField("Performance", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(
                "Text-only mode uses just the element list (faster). Enable screenshots for visual reasoning when needed.",
                MessageType.Info);
            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(serializedSettings.FindProperty("preferTextOnlyMode"),
                new GUIContent("Prefer Text-Only Mode", "Use text-only by default (faster, no vision)"));
            EditorGUILayout.PropertyField(serializedSettings.FindProperty("sendScreenshotsToAI"),
                new GUIContent("Send Screenshots", "Send screenshots to AI for visual reasoning"));

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(10);

            // Debug Section
            EditorGUILayout.LabelField("Debug", EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(serializedSettings.FindProperty("showDebugPanel"),
                new GUIContent("Show Debug Panel"));
            EditorGUILayout.PropertyField(serializedSettings.FindProperty("saveScreenshots"),
                new GUIContent("Save Screenshots", "Save screenshots for debugging (independent of AI)"));
            EditorGUILayout.PropertyField(serializedSettings.FindProperty("verboseLogging"),
                new GUIContent("Verbose Logging"));

            EditorGUI.indentLevel--;

            if (serializedSettings.hasModifiedProperties)
            {
                serializedSettings.ApplyModifiedProperties();
                settings.Save();
            }
        }

        private async void TestLMStudioConnection()
        {
            var provider = new LMStudioProvider(settings.lmStudioEndpoint, settings.lmStudioModel);
            var result = await provider.TestConnectionAsync();

            if (result)
            {
                EditorUtility.DisplayDialog("LM Studio", "Connection successful!", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("LM Studio", "Connection failed. Check endpoint and ensure LM Studio is running.", "OK");
            }
        }

        private async void TestGeminiConnection()
        {
            var provider = new GeminiProvider(settings.geminiApiKey, ModelTier.GeminiFlashLite);
            var result = await provider.TestConnectionAsync();

            if (result)
            {
                EditorUtility.DisplayDialog("Gemini", "Connection successful!", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Gemini", "Connection failed. Check your API key.", "OK");
            }
        }

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            var provider = new AITestSettingsProvider("Project/UI Test/AI Testing", SettingsScope.Project)
            {
                keywords = new[] { "AI", "Test", "LM Studio", "Gemini", "Automation" }
            };
            return provider;
        }
    }
}
#endif
