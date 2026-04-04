using UnityEngine;

namespace ODDGames.UIAutomation.AI
{
    /// <summary>
    /// Project settings for AI Testing configuration.
    /// Works at runtime - assign via Resources.Load or direct reference.
    /// In Editor, automatically loads from Assets/Editor/AITestSettings.asset.
    /// </summary>
    public class AITestSettings : ScriptableObject
    {
        [Header("Gemini")]
        [Tooltip("Gemini API key from Google AI Studio")]
        public string geminiApiKey = "";

        [Tooltip("Default model to use for AI tests")]
        public string defaultModel = "gemini-2.0-flash";

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
        public bool sendScreenshotsToAI = true;

        [Tooltip("Use text-only mode by default (faster, no vision). The AI relies solely on the element list.")]
        public bool preferTextOnlyMode = false;

        [Header("Debug")]
        [Tooltip("Show debug panel during test execution")]
        public bool showDebugPanel = true;

        [Tooltip("Save screenshots during tests (for debugging, independent of sending to AI)")]
        public bool saveScreenshots = true;

        [Tooltip("Verbose logging")]
        public bool verboseLogging = false;

        [Tooltip("Log all AI data (screenshots, prompts, responses) to AIDebug folder")]
        public bool enableDebugLogging = true;

        private static AITestSettings instance;

        /// <summary>
        /// Optional editor-provided loader. Set by Editor assembly to load/create from AssetDatabase.
        /// </summary>
        internal static System.Func<AITestSettings> EditorLoader;

        /// <summary>
        /// Gets the singleton settings instance.
        /// In Editor: uses EditorLoader callback (set by Editor assembly).
        /// At Runtime: loads from Resources or set via SetInstance().
        /// </summary>
        public static AITestSettings Instance
        {
            get
            {
                if (instance == null)
                {
                    // Try editor loader first (set by Editor assembly)
                    if (EditorLoader != null)
                        instance = EditorLoader();

                    // Fallback: load from Resources
                    if (instance == null)
                        instance = Resources.Load<AITestSettings>("AITestSettings");
                }
                return instance;
            }
        }

        /// <summary>
        /// Sets the singleton instance. Use this at runtime to provide settings.
        /// </summary>
        public static void SetInstance(AITestSettings settings)
        {
            instance = settings;
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

            return "gemini-2.0-flash";
        }

        /// <summary>
        /// Creates a runner config for a specific model.
        /// </summary>
        public AITestRunnerConfig CreateRunnerConfig(string modelId = null)
        {
            var effectiveModel = modelId ?? GetEffectiveModel();
            // Screenshots are on-demand now - AI requests them via screenshot action when needed
            var shouldSendScreenshots = false;

            Debug.Log($"[AITest] Creating runner config: model={effectiveModel}, SendScreenshots={shouldSendScreenshots}, Debug={enableDebugLogging}");

            var config = new AITestRunnerConfig
            {
                EnableHistoryReplay = true,
                SendScreenshots = shouldSendScreenshots,
                EnableDebugLogging = enableDebugLogging
            };

            // Create provider if we have an API key and model
            if (!string.IsNullOrEmpty(geminiApiKey) && !string.IsNullOrEmpty(effectiveModel))
            {
                config.ModelProvider = CreateProviderForModel(effectiveModel);
            }

            return config;
        }

        /// <summary>
        /// Creates a model provider from current settings.
        /// </summary>
        public IModelProvider CreateModelProvider(string modelId = null)
        {
            var effectiveModel = modelId ?? GetEffectiveModel();

            if (string.IsNullOrEmpty(geminiApiKey))
            {
                Debug.LogWarning("[AITestSettings] No API key configured.");
                return null;
            }

            return CreateProviderForModel(effectiveModel);
        }

        /// <summary>
        /// Creates the appropriate provider for the given model.
        /// Uses GeminiToolProvider with structured JSON output for reliable action parsing.
        /// </summary>
        private IModelProvider CreateProviderForModel(string modelId)
        {
            // Check if this is a computer-use-specific model (which we don't support)
            var isComputerUseModel = modelId.ToLowerInvariant().Contains("computer-use");

            if (isComputerUseModel)
            {
                // Computer Use models require special built-in tools - use a standard model instead
                var fallbackModel = "gemini-2.0-flash";
                Debug.LogWarning($"[AITestSettings] Model '{modelId}' is a Computer Use model. " +
                    $"Using '{fallbackModel}' with structured output instead.");
                return new GeminiToolProvider(geminiApiKey, fallbackModel);
            }

            Debug.Log($"[AITestSettings] Using GeminiToolProvider for model: {modelId}");
            return new GeminiToolProvider(geminiApiKey, modelId);
        }

    }
}
