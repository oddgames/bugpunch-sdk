using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ODDGames.UIAutomation.Editor
{
    /// <summary>
    /// Centralized settings for UITest framework.
    /// Access via Edit > Project Settings > UITest.
    /// </summary>
    public static class UITestSettings
    {
        // EditorPrefs key prefix - all settings use this prefix for consistency
        private const string KeyPrefix = "ODDGames.UIAutomation.";

        // Settings keys
        private const string GeminiApiKeyPref = KeyPrefix + "GeminiApiKey";
        private const string GeminiApiKeyValidatedPref = KeyPrefix + "GeminiApiKeyValidated";
        private const string GeminiModelPref = KeyPrefix + "GeminiModel";
        private const string LastRecordingFolderPref = KeyPrefix + "LastRecordingFolder";
        private const string TestDataPathPref = KeyPrefix + "TestDataPath";
        private const string TestDataModePref = KeyPrefix + "TestDataMode";

        // Session state keys (not persisted across editor restarts)
        private const string RecordOnNextPlayKey = KeyPrefix + "RecordOnNextPlay";
        private const string WasRecordingKey = KeyPrefix + "WasRecording";
        private const string PendingRecordingNameKey = KeyPrefix + "PendingRecordingName";
        private const string PendingTestDataSourceKey = KeyPrefix + "PendingTestDataSource";
        private const string PendingRecordingFolderKey = KeyPrefix + "PendingRecordingFolder";

        // Default values
        private const string DefaultGeminiModel = "gemini-2.0-flash";

        #region Gemini API Settings

        public static string GeminiApiKey
        {
            get => EditorPrefs.GetString(GeminiApiKeyPref, "");
            set => EditorPrefs.SetString(GeminiApiKeyPref, value);
        }

        public static string GeminiModel
        {
            get => EditorPrefs.GetString(GeminiModelPref, DefaultGeminiModel);
            set => EditorPrefs.SetString(GeminiModelPref, value);
        }

        public static bool IsGeminiApiKeyValid
        {
            get
            {
                string apiKey = GeminiApiKey;
                if (string.IsNullOrEmpty(apiKey)) return false;

                string validatedData = EditorPrefs.GetString(GeminiApiKeyValidatedPref, "");
                if (string.IsNullOrEmpty(validatedData)) return false;

                string[] parts = validatedData.Split('|');
                if (parts.Length != 2 || parts[0] != apiKey) return false;

                if (long.TryParse(parts[1], out long timestamp))
                {
                    var validatedTime = System.DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
                    return (System.DateTime.UtcNow - validatedTime).TotalHours < 24;
                }

                return false;
            }
        }

        public static void SetGeminiApiKeyValidated(bool isValid)
        {
            if (isValid)
            {
                long timestamp = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                EditorPrefs.SetString(GeminiApiKeyValidatedPref, $"{GeminiApiKey}|{timestamp}");
            }
            else
            {
                EditorPrefs.DeleteKey(GeminiApiKeyValidatedPref);
            }
        }

        public static void ClearGeminiApiKeyValidation()
        {
            EditorPrefs.DeleteKey(GeminiApiKeyValidatedPref);
        }

        #endregion

        #region Recording Settings

        public static string LastRecordingFolder
        {
            get => EditorPrefs.GetString(LastRecordingFolderPref, "");
            set => EditorPrefs.SetString(LastRecordingFolderPref, value);
        }

        public static string TestDataPath
        {
            get => EditorPrefs.GetString(TestDataPathPref, "");
            set => EditorPrefs.SetString(TestDataPathPref, value);
        }

        public static int TestDataMode
        {
            get => EditorPrefs.GetInt(TestDataModePref, 0);
            set => EditorPrefs.SetInt(TestDataModePref, value);
        }

        #endregion

        #region Session State (not persisted)

        public static bool RecordOnNextPlay
        {
            get => SessionState.GetBool(RecordOnNextPlayKey, false);
            set => SessionState.SetBool(RecordOnNextPlayKey, value);
        }

        public static bool WasRecording
        {
            get => SessionState.GetBool(WasRecordingKey, false);
            set => SessionState.SetBool(WasRecordingKey, value);
        }

        public static string PendingRecordingName
        {
            get => SessionState.GetString(PendingRecordingNameKey, "");
            set => SessionState.SetString(PendingRecordingNameKey, value);
        }

        public static string PendingTestDataSource
        {
            get => EditorPrefs.GetString(PendingTestDataSourceKey, "");
            set
            {
                if (string.IsNullOrEmpty(value))
                    EditorPrefs.DeleteKey(PendingTestDataSourceKey);
                else
                    EditorPrefs.SetString(PendingTestDataSourceKey, value);
            }
        }

        public static string PendingRecordingFolder
        {
            get => EditorPrefs.GetString(PendingRecordingFolderKey, "");
            set
            {
                if (string.IsNullOrEmpty(value))
                    EditorPrefs.DeleteKey(PendingRecordingFolderKey);
                else
                    EditorPrefs.SetString(PendingRecordingFolderKey, value);
            }
        }

        #endregion

        #region Migration

        /// <summary>
        /// Migrates old TOR.* prefixed settings to new ODDGames.UIAutomation.* prefix.
        /// Called automatically on first access.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void MigrateOldSettings()
        {
            // Only migrate once
            const string migrationKey = KeyPrefix + "SettingsMigrated";
            if (EditorPrefs.GetBool(migrationKey, false)) return;

            // Migrate old keys to new keys
            MigrateKey("TOR.UITestGenerator.GeminiApiKey", GeminiApiKeyPref);
            MigrateKey("TOR.UITestGenerator.GeminiApiKeyValidated", GeminiApiKeyValidatedPref);
            MigrateKey("TOR.UITestRecorder.LastRecordingFolder", LastRecordingFolderPref);
            MigrateKey("TOR.UITestRecorder.TestDataPath", TestDataPathPref);
            MigrateIntKey("TOR.UITestRecorder.TestDataMode", TestDataModePref);

            EditorPrefs.SetBool(migrationKey, true);
        }

        private static void MigrateKey(string oldKey, string newKey)
        {
            if (EditorPrefs.HasKey(oldKey) && !EditorPrefs.HasKey(newKey))
            {
                EditorPrefs.SetString(newKey, EditorPrefs.GetString(oldKey, ""));
                EditorPrefs.DeleteKey(oldKey);
            }
        }

        private static void MigrateIntKey(string oldKey, string newKey)
        {
            if (EditorPrefs.HasKey(oldKey) && !EditorPrefs.HasKey(newKey))
            {
                EditorPrefs.SetInt(newKey, EditorPrefs.GetInt(oldKey, 0));
                EditorPrefs.DeleteKey(oldKey);
            }
        }

        #endregion
    }

    /// <summary>
    /// Settings provider for Project Settings window.
    /// </summary>
    public class UITestSettingsProvider : SettingsProvider
    {
        private static readonly string[] GeminiModels = new[]
        {
            "gemini-2.0-flash",
            "gemini-2.0-flash-lite",
            "gemini-1.5-flash",
            "gemini-1.5-pro"
        };

        private bool isValidatingKey;
        private string validationStatus;

        public UITestSettingsProvider(string path, SettingsScope scope)
            : base(path, scope) { }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new UITestSettingsProvider("Project/UITest", SettingsScope.Project)
            {
                label = "UITest",
                keywords = new HashSet<string>(new[] { "uitest", "ui", "test", "automation", "gemini", "api" })
            };
        }

        public override void OnGUI(string searchContext)
        {
            EditorGUILayout.Space(10);

            // Gemini API Section
            EditorGUILayout.LabelField("Gemini API (Test Generation)", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUI.BeginChangeCheck();
            string apiKey = EditorGUILayout.PasswordField("API Key", UITestSettings.GeminiApiKey);
            if (EditorGUI.EndChangeCheck())
            {
                UITestSettings.GeminiApiKey = apiKey;
                UITestSettings.ClearGeminiApiKeyValidation();
                validationStatus = "";
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15);

            EditorGUI.BeginDisabledGroup(isValidatingKey || string.IsNullOrEmpty(UITestSettings.GeminiApiKey));
            if (GUILayout.Button(isValidatingKey ? "Validating..." : "Validate API Key", GUILayout.Width(120)))
            {
                ValidateApiKey();
            }
            EditorGUI.EndDisabledGroup();

            // Status indicator
            if (UITestSettings.IsGeminiApiKeyValid)
            {
                GUI.color = Color.green;
                GUILayout.Label("Valid", EditorStyles.boldLabel);
                GUI.color = Color.white;
            }
            else if (!string.IsNullOrEmpty(UITestSettings.GeminiApiKey))
            {
                GUI.color = Color.yellow;
                GUILayout.Label("Not validated", EditorStyles.miniLabel);
                GUI.color = Color.white;
            }

            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(validationStatus))
            {
                EditorGUILayout.HelpBox(validationStatus, validationStatus.Contains("success") ? MessageType.Info : MessageType.Warning);
            }

            if (string.IsNullOrEmpty(UITestSettings.GeminiApiKey))
            {
                EditorGUILayout.HelpBox("Get your API key at: https://aistudio.google.com/apikey", MessageType.Info);
            }

            // Model selection
            EditorGUILayout.Space(5);
            int currentModelIndex = System.Array.IndexOf(GeminiModels, UITestSettings.GeminiModel);
            if (currentModelIndex < 0) currentModelIndex = 0;

            EditorGUI.BeginChangeCheck();
            int newModelIndex = EditorGUILayout.Popup("Model", currentModelIndex, GeminiModels);
            if (EditorGUI.EndChangeCheck())
            {
                UITestSettings.GeminiModel = GeminiModels[newModelIndex];
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(15);

            // Recording Section
            EditorGUILayout.LabelField("Recording", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Last Recording", UITestSettings.LastRecordingFolder);
            if (!string.IsNullOrEmpty(UITestSettings.LastRecordingFolder) && GUILayout.Button("Open", GUILayout.Width(60)))
            {
                EditorUtility.RevealInFinder(UITestSettings.LastRecordingFolder);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(15);

            // Links Section
            EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate Test from Recording", GUILayout.Height(25)))
            {
                UITestGeneratorWindow.ShowWindow();
            }
            if (GUILayout.Button("Generate Sample Scene", GUILayout.Height(25)))
            {
                EditorApplication.ExecuteMenuItem("Window/UI Automation/Generate Sample Scene");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }

        private async void ValidateApiKey()
        {
            isValidatingKey = true;
            validationStatus = "Validating...";

            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = System.TimeSpan.FromSeconds(30);

                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{UITestSettings.GeminiModel}:generateContent?key={UITestSettings.GeminiApiKey}";

                string json = @"{
                    ""contents"": [{
                        ""parts"": [{
                            ""text"": ""Say OK""
                        }]
                    }],
                    ""generationConfig"": {
                        ""maxOutputTokens"": 10
                    }
                }";

                var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    UITestSettings.SetGeminiApiKeyValidated(true);
                    validationStatus = "API key validated successfully!";
                }
                else
                {
                    UITestSettings.SetGeminiApiKeyValidated(false);
                    validationStatus = $"Validation failed: {response.StatusCode}";
                }
            }
            catch (System.Exception ex)
            {
                UITestSettings.SetGeminiApiKeyValidated(false);
                validationStatus = $"Validation error: {ex.Message}";
            }
            finally
            {
                isValidatingKey = false;
                // Force repaint
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
        }
    }
}
