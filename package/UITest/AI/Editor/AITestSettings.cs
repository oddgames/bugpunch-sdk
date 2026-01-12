#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ODDGames.UITest.AI.Editor
{
    /// <summary>
    /// Project settings for AI Testing configuration.
    /// </summary>
    public class AITestSettings : ScriptableObject
    {
        private const string SettingsPath = "Assets/Editor/AITestSettings.asset";

        [Header("Gemini")]
        [Tooltip("Gemini API key from Google AI Studio")]
        public string geminiApiKey = "";

        [Tooltip("Default model to use for AI tests")]
        public string defaultModel = "";

        [Header("Defaults")]
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
                defaultModel = GetEffectiveModel(),
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
        /// Gets the effective model to use (settings default or first available).
        /// </summary>
        public string GetEffectiveModel()
        {
            if (!string.IsNullOrEmpty(defaultModel))
                return defaultModel;

            // Fall back to cached models
            var cached = GeminiModels.CachedModels;
            if (cached != null && cached.Count > 0)
                return cached[0].ModelId;

            return null;
        }

        /// <summary>
        /// Creates a runner config for a specific model.
        /// </summary>
        public AITestRunnerConfig CreateRunnerConfig(string modelId = null)
        {
            var effectiveModel = modelId ?? GetEffectiveModel();
            var shouldSendScreenshots = sendScreenshotsToAI && !preferTextOnlyMode;

            Debug.Log($"[AITest] Creating runner config: model={effectiveModel}, SendScreenshots={shouldSendScreenshots}");

            var config = new AITestRunnerConfig
            {
                EnableHistoryReplay = true,
                SendScreenshots = shouldSendScreenshots
            };

            // Create provider if we have an API key and model
            if (!string.IsNullOrEmpty(geminiApiKey) && !string.IsNullOrEmpty(effectiveModel))
            {
                // Get context window size from cached model info if available
                var modelInfo = GeminiModels.GetCachedModel(effectiveModel);
                var contextSize = modelInfo?.inputTokenLimit ?? 1048576;

                config.ModelProvider = new GeminiProvider(geminiApiKey, effectiveModel, contextWindowSize: contextSize);
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
        private List<GeminiModelInfo> availableModels;
        private bool isLoadingModels;
        private string[] modelOptions;
        private int selectedModelIndex;

        public AITestSettingsProvider(string path, SettingsScope scope)
            : base(path, scope) { }

        public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement)
        {
            settings = AITestSettings.Instance;
            serializedSettings = new SerializedObject(settings);
            RefreshModelsAsync().Forget();
        }

        private async UniTaskVoid RefreshModelsAsync()
        {
            if (string.IsNullOrEmpty(settings.geminiApiKey))
            {
                availableModels = null;
                modelOptions = new[] { "(Enter API key first)" };
                return;
            }

            isLoadingModels = true;
            availableModels = await GeminiModels.ListModelsAsync(settings.geminiApiKey, CancellationToken.None);
            isLoadingModels = false;

            UpdateModelOptions();
        }

        private void UpdateModelOptions()
        {
            if (availableModels == null || availableModels.Count == 0)
            {
                modelOptions = new[] { "(No models available)" };
                selectedModelIndex = 0;
                return;
            }

            var options = new List<string>();
            selectedModelIndex = 0;

            for (int i = 0; i < availableModels.Count; i++)
            {
                var model = availableModels[i];
                options.Add($"{model.displayName} ({model.ModelId})");

                if (model.ModelId == settings.defaultModel)
                {
                    selectedModelIndex = i;
                }
            }

            modelOptions = options.ToArray();
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

            // Gemini Section
            EditorGUILayout.LabelField("Gemini", EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedSettings.FindProperty("geminiApiKey"),
                new GUIContent("API Key"));
            if (EditorGUI.EndChangeCheck())
            {
                serializedSettings.ApplyModifiedProperties();
                GeminiModels.ClearCache();
                RefreshModelsAsync().Forget();
            }

            if (!string.IsNullOrEmpty(settings.geminiApiKey))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(20);

                if (isLoadingModels)
                {
                    EditorGUILayout.LabelField("Loading models...");
                }
                else
                {
                    if (GUILayout.Button("Refresh Models", GUILayout.Width(120)))
                    {
                        GeminiModels.ClearCache();
                        RefreshModelsAsync().Forget();
                    }
                }

                EditorGUILayout.EndHorizontal();

                // Model Selection
                EditorGUILayout.Space(5);
                if (modelOptions != null && modelOptions.Length > 0)
                {
                    EditorGUI.BeginChangeCheck();
                    selectedModelIndex = EditorGUILayout.Popup("Default Model", selectedModelIndex, modelOptions);
                    if (EditorGUI.EndChangeCheck() && availableModels != null && selectedModelIndex < availableModels.Count)
                    {
                        settings.defaultModel = availableModels[selectedModelIndex].ModelId;
                        GeminiModels.DefaultModel = settings.defaultModel;
                        EditorUtility.SetDirty(settings);
                    }
                }

                // Show model info
                if (availableModels != null && selectedModelIndex < availableModels.Count)
                {
                    var model = availableModels[selectedModelIndex];
                    var capabilities = new List<string>();
                    if (model.SupportsVision) capabilities.Add("Vision");
                    if (model.SupportsTools) capabilities.Add("Tools");
                    if (model.SupportsStructuredOutput) capabilities.Add("JSON");
                    if (model.thinking) capabilities.Add("Thinking");

                    EditorGUILayout.HelpBox(
                        $"Context: {model.inputTokenLimit:N0} tokens | Capabilities: {string.Join(", ", capabilities)}",
                        MessageType.None);
                }
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(10);

            // Defaults Section
            EditorGUILayout.LabelField("Defaults", EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;

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

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            var provider = new AITestSettingsProvider("Project/UI Test/AI Testing", SettingsScope.Project)
            {
                keywords = new[] { "AI", "Test", "Gemini", "Automation", "Model" }
            };
            return provider;
        }
    }
}
#endif
