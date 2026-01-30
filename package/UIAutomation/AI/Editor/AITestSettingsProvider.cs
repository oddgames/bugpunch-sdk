#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Threading;

namespace ODDGames.UIAutomation.AI.Editor
{
    /// <summary>
    /// Settings provider for Project Settings window.
    /// </summary>
    public class AITestSettingsProvider : SettingsProvider
    {
        private SerializedObject serializedSettings;
        private AITestSettings settings;
        private List<GeminiModelInfo> availableModels;
        private bool isLoadingModels;
        private bool isValidatingModel;
        private string modelValidationError;
        private string[] modelOptions;
        private int selectedModelIndex;

        public AITestSettingsProvider(string path, SettingsScope scope)
            : base(path, scope) { }

        public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement)
        {
            settings = AITestSettings.Instance;
            serializedSettings = new SerializedObject(settings);
            RefreshModelsAsync();
        }

        private async void RefreshModelsAsync()
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

        private async void ValidateAndSelectModelAsync(int newIndex)
        {
            if (availableModels == null || newIndex >= availableModels.Count)
                return;

            var model = availableModels[newIndex];
            isValidatingModel = true;
            modelValidationError = null;

            var error = await GeminiModels.ValidateModelCapabilitiesAsync(
                settings.geminiApiKey,
                model.ModelId,
                CancellationToken.None);

            isValidatingModel = false;

            if (error != null)
            {
                modelValidationError = error;
                Debug.LogWarning($"[AITestSettings] {error}");
            }
            else
            {
                // Model validated successfully - apply the selection
                selectedModelIndex = newIndex;
                settings.defaultModel = model.ModelId;
                GeminiModels.DefaultModel = settings.defaultModel;
                EditorUtility.SetDirty(settings);
                modelValidationError = null;
                Debug.Log($"[AITestSettings] Model '{model.ModelId}' validated - supports vision, tools, and structured output");
            }
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
                RefreshModelsAsync();
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
                        RefreshModelsAsync();
                    }
                }

                EditorGUILayout.EndHorizontal();

                // Model Selection
                EditorGUILayout.Space(5);
                if (modelOptions != null && modelOptions.Length > 0)
                {
                    EditorGUI.BeginChangeCheck();
                    var newIndex = EditorGUILayout.Popup("Default Model", selectedModelIndex, modelOptions);
                    if (EditorGUI.EndChangeCheck() && availableModels != null && newIndex < availableModels.Count && newIndex != selectedModelIndex)
                    {
                        ValidateAndSelectModelAsync(newIndex);
                    }
                }

                // Show validation status
                if (isValidatingModel)
                {
                    EditorGUILayout.HelpBox("Validating model capabilities (vision, tools, JSON)...", MessageType.Info);
                }
                else if (!string.IsNullOrEmpty(modelValidationError))
                {
                    EditorGUILayout.HelpBox(modelValidationError, MessageType.Error);
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
            EditorGUILayout.PropertyField(serializedSettings.FindProperty("enableDebugLogging"),
                new GUIContent("Debug Logging", "Log all AI data (screenshots, prompts, responses) to AIDebug folder"));

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
            var provider = new AITestSettingsProvider("Project/UI Automation/AI Testing", SettingsScope.Project)
            {
                keywords = new[] { "AI", "Test", "Gemini", "Automation", "Model" }
            };
            return provider;
        }
    }
}
#endif
