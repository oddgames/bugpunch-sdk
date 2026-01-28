using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace ODDGames.UIAutomation.Editor
{
    public class UITestGeneratorWindow : EditorWindow
    {
        string lastRecordingPath;
        string statusMessage;
        bool isGenerating;
        bool isValidatingKey;
        Vector2 scrollPosition;

        string generatedCode;
        string suggestedClassName;

        enum ApiKeyStatus { Unknown, Valid, Invalid }
        ApiKeyStatus apiKeyStatus = ApiKeyStatus.Unknown;

        [MenuItem("Window/Analysis/UI Automation/Generate Test from Recording")]
        public static void ShowWindow()
        {
            var window = GetWindow<UITestGeneratorWindow>("UI Test Generator");
            window.minSize = new Vector2(500, 400);
            window.Show();
        }

        public static void ShowWindowWithLastRecording()
        {
            var window = GetWindow<UITestGeneratorWindow>("UI Test Generator");
            window.minSize = new Vector2(500, 400);

            string lastFolder = UITestSettings.LastRecordingFolder;
            if (!string.IsNullOrEmpty(lastFolder) && Directory.Exists(lastFolder))
            {
                window.lastRecordingPath = lastFolder;
            }

            window.Show();
        }

        void OnEnable()
        {
            if (UITestRecorder.Instance != null && !string.IsNullOrEmpty(UITestRecorder.Instance.LastRecordingFolder))
            {
                lastRecordingPath = UITestRecorder.Instance.LastRecordingFolder;
            }
            else if (string.IsNullOrEmpty(lastRecordingPath))
            {
                lastRecordingPath = UITestSettings.LastRecordingFolder;
            }

            CheckApiKeyValidation();
        }

        void CheckApiKeyValidation()
        {
            if (string.IsNullOrEmpty(UITestSettings.GeminiApiKey))
            {
                apiKeyStatus = ApiKeyStatus.Unknown;
                return;
            }

            apiKeyStatus = UITestSettings.IsGeminiApiKeyValid ? ApiKeyStatus.Valid : ApiKeyStatus.Unknown;
        }

        void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            DrawApiKeySection();
            EditorGUILayout.Space(10);

            DrawRecordingSection();
            EditorGUILayout.Space(10);

            DrawOutputSection();
            EditorGUILayout.Space(10);

            DrawGenerateSection();
            EditorGUILayout.Space(10);

            if (!string.IsNullOrEmpty(generatedCode))
            {
                DrawResultSection();
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawApiKeySection()
        {
            EditorGUILayout.LabelField("Gemini API", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            string apiKey = UITestSettings.GeminiApiKey;
            EditorGUI.BeginChangeCheck();
            apiKey = EditorGUILayout.PasswordField("API Key", apiKey);
            if (EditorGUI.EndChangeCheck())
            {
                UITestSettings.GeminiApiKey = apiKey;
                UITestSettings.ClearGeminiApiKeyValidation();
                apiKeyStatus = ApiKeyStatus.Unknown;
            }

            EditorGUI.BeginDisabledGroup(isValidatingKey || string.IsNullOrEmpty(apiKey));
            if (GUILayout.Button(isValidatingKey ? "Testing..." : "Test Key", GUILayout.Width(80)))
            {
                ValidateApiKeyAsync();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            // Model selection
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Model", GUILayout.Width(EditorGUIUtility.labelWidth));
            string[] models = { "gemini-2.0-flash", "gemini-2.0-flash-lite", "gemini-1.5-flash", "gemini-1.5-pro" };
            int currentIndex = Array.IndexOf(models, UITestSettings.GeminiModel);
            if (currentIndex < 0) currentIndex = 0;
            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUILayout.Popup(currentIndex, models);
            if (EditorGUI.EndChangeCheck())
            {
                UITestSettings.GeminiModel = models[newIndex];
            }
            EditorGUILayout.EndHorizontal();

            if (string.IsNullOrEmpty(apiKey))
            {
                EditorGUILayout.HelpBox("Enter your Gemini API key. Get one at: https://aistudio.google.com/apikey\nOr configure in Edit > Project Settings > UITest", MessageType.Info);
            }
            else
            {
                switch (apiKeyStatus)
                {
                    case ApiKeyStatus.Valid:
                        EditorGUILayout.HelpBox("API key is valid (checked within 24h)", MessageType.None);
                        break;
                    case ApiKeyStatus.Invalid:
                        EditorGUILayout.HelpBox("API key is invalid. Please check and try again.", MessageType.Error);
                        break;
                    case ApiKeyStatus.Unknown:
                        EditorGUILayout.HelpBox("API key not validated. Click 'Test Key' to verify.", MessageType.Warning);
                        break;
                }
            }
        }

        async void ValidateApiKeyAsync()
        {
            isValidatingKey = true;
            Repaint();

            try
            {
                bool isValid = await TestGeminiApiKey();
                UITestSettings.SetGeminiApiKeyValidated(isValid);
                apiKeyStatus = isValid ? ApiKeyStatus.Valid : ApiKeyStatus.Invalid;

                statusMessage = isValid ? "API key validated successfully!" : "API key validation failed.";
            }
            catch (Exception ex)
            {
                UITestSettings.SetGeminiApiKeyValidated(false);
                apiKeyStatus = ApiKeyStatus.Invalid;
                statusMessage = $"Validation error: {ex.Message}";
            }
            finally
            {
                isValidatingKey = false;
                Repaint();
            }
        }

        async Task<bool> TestGeminiApiKey()
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);

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

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);

            return response.IsSuccessStatusCode;
        }

        void DrawRecordingSection()
        {
            EditorGUILayout.LabelField("Recording", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            lastRecordingPath = EditorGUILayout.TextField("Recording Folder", lastRecordingPath);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string path = EditorUtility.OpenFolderPanel("Select Recording Folder", Path.GetTempPath(), "");
                if (!string.IsNullOrEmpty(path))
                {
                    lastRecordingPath = path;
                }
            }
            EditorGUILayout.EndHorizontal();

            if (UITestRecorder.Instance != null && !string.IsNullOrEmpty(UITestRecorder.Instance.LastRecordingFolder))
            {
                if (GUILayout.Button("Use Last Recording"))
                {
                    lastRecordingPath = UITestRecorder.Instance.LastRecordingFolder;
                }
            }

            if (!string.IsNullOrEmpty(lastRecordingPath) && Directory.Exists(lastRecordingPath))
            {
                bool hasSteps = File.Exists(Path.Combine(lastRecordingPath, "steps.log"));
                bool hasLog = File.Exists(Path.Combine(lastRecordingPath, "log.txt"));
                bool hasPrompt = File.Exists(Path.Combine(lastRecordingPath, "prompt.md"));

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Files:", GUILayout.Width(50));
                GUI.color = hasSteps ? Color.green : Color.red;
                EditorGUILayout.LabelField("steps.log", GUILayout.Width(80));
                GUI.color = hasLog ? Color.green : Color.red;
                EditorGUILayout.LabelField("log.txt", GUILayout.Width(60));
                GUI.color = hasPrompt ? Color.green : Color.red;
                EditorGUILayout.LabelField("prompt.md", GUILayout.Width(80));
                GUI.color = Color.white;
                EditorGUILayout.EndHorizontal();
            }
        }

        void DrawOutputSection()
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

            if (!string.IsNullOrEmpty(lastRecordingPath) && Directory.Exists(lastRecordingPath))
            {
                string relativePath = lastRecordingPath;
                if (lastRecordingPath.StartsWith(Application.dataPath))
                {
                    relativePath = "Assets" + lastRecordingPath.Substring(Application.dataPath.Length);
                }
                EditorGUILayout.LabelField("Output:", relativePath);
                EditorGUILayout.HelpBox("Script will be saved in the recording folder alongside screenshots and test data.", MessageType.None);
            }
        }

        void DrawGenerateSection()
        {
            string apiKey = UITestSettings.GeminiApiKey;
            bool canGenerate = !isGenerating &&
                              !string.IsNullOrEmpty(apiKey) &&
                              !string.IsNullOrEmpty(lastRecordingPath) &&
                              apiKeyStatus != ApiKeyStatus.Invalid;

            EditorGUI.BeginDisabledGroup(!canGenerate);

            if (GUILayout.Button(isGenerating ? "Generating..." : "Generate Test with Gemini", GUILayout.Height(30)))
            {
                GenerateTestAsync();
            }

            EditorGUI.EndDisabledGroup();

            if (apiKeyStatus == ApiKeyStatus.Unknown && !string.IsNullOrEmpty(apiKey))
            {
                EditorGUILayout.HelpBox("Please validate your API key before generating.", MessageType.Warning);
            }

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, isGenerating ? MessageType.Info : MessageType.None);
            }
        }

        void DrawResultSection()
        {
            EditorGUILayout.LabelField("Generated Test", EditorStyles.boldLabel);

            if (!string.IsNullOrEmpty(suggestedClassName))
            {
                EditorGUILayout.LabelField($"Suggested Name: {suggestedClassName}");
            }

            EditorGUILayout.TextArea(generatedCode, GUILayout.Height(200));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Test Script"))
            {
                SaveTestScript();
            }
            if (GUILayout.Button("Copy to Clipboard"))
            {
                EditorGUIUtility.systemCopyBuffer = generatedCode;
                statusMessage = "Copied to clipboard!";
            }
            EditorGUILayout.EndHorizontal();
        }

        async void GenerateTestAsync()
        {
            if (apiKeyStatus == ApiKeyStatus.Unknown)
            {
                isValidatingKey = true;
                statusMessage = "Validating API key...";
                Repaint();

                try
                {
                    bool isValid = await TestGeminiApiKey();
                    UITestSettings.SetGeminiApiKeyValidated(isValid);
                    apiKeyStatus = isValid ? ApiKeyStatus.Valid : ApiKeyStatus.Invalid;

                    if (!isValid)
                    {
                        statusMessage = "API key is invalid. Please check and try again.";
                        isValidatingKey = false;
                        Repaint();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    UITestSettings.SetGeminiApiKeyValidated(false);
                    apiKeyStatus = ApiKeyStatus.Invalid;
                    statusMessage = $"API key validation failed: {ex.Message}";
                    isValidatingKey = false;
                    Repaint();
                    return;
                }
                finally
                {
                    isValidatingKey = false;
                }
            }

            isGenerating = true;
            statusMessage = "Reading recording files...";
            Repaint();

            try
            {
                string promptContent = File.ReadAllText(Path.Combine(lastRecordingPath, "prompt.md"));
                string stepsContent = File.ReadAllText(Path.Combine(lastRecordingPath, "steps.log"));
                string logContent = File.ReadAllText(Path.Combine(lastRecordingPath, "log.txt"));

                string fullPrompt = BuildGeminiPrompt(promptContent, stepsContent, logContent);

                statusMessage = "Sending to Gemini...";
                Repaint();

                string response = await CallGeminiApi(fullPrompt);

                ParseGeminiResponse(response);

                statusMessage = "Generation complete!";
            }
            catch (Exception ex)
            {
                statusMessage = $"Error: {ex.Message}";
                Debug.LogError($"[UITestGenerator] {ex}");
            }
            finally
            {
                isGenerating = false;
                Repaint();
            }
        }

        string BuildGeminiPrompt(string promptMd, string stepsJson, string logTxt)
        {
            var sb = new StringBuilder();

            sb.AppendLine("You are a Unity C# test generation assistant. Generate an NUnit PlayMode test based on the following recorded user session.");
            sb.AppendLine();
            sb.AppendLine("IMPORTANT: Your response must be in this exact format:");
            sb.AppendLine("1. First line: CLASS_NAME: YourTestClassName");
            sb.AppendLine("2. Then the complete C# code wrapped in ```csharp and ```");
            sb.AppendLine();
            sb.AppendLine("Requirements:");
            sb.AppendLine("- Generate a descriptive PascalCase class name based on what the test does (e.g., GarageToFreestyleTest, BuyTruckTest)");
            sb.AppendLine("- Use [TestFixture] attribute on the class");
            sb.AppendLine("- Use [Test] attribute on async test methods returning Task");
            sb.AppendLine("- Add 'using static ODDGames.UIAutomation.ActionExecutor;' for static access to all test methods");
            sb.AppendLine("- Use await Click(Name(\"pattern\")) for clicks - PREFER text content over element names when available");
            sb.AppendLine("- Use await Wait(seconds) for delays between actions");
            sb.AppendLine("- Use await WaitFor(() => condition, timeout, \"description\") for waiting on state changes");
            sb.AppendLine("- Analyze the log.txt to understand game state transitions and add appropriate waits");
            sb.AppendLine("- Add  and using NUnit.Framework; at the top");
            sb.AppendLine("- Make sure to handle popups or dialogs that might appear (use throwIfMissing: false for optional elements)");
            sb.AppendLine("- Steps marked with '** DELIBERATE WAIT DETECTED **' indicate the user intentionally waited (gap >= 2s with stable 30+ fps)");
            sb.AppendLine("- For deliberate waits, add an explicit await Wait(seconds) call BEFORE the action to replicate the user's behavior");
            sb.AppendLine("- IMPORTANT: Add a comment above EACH action referencing the step number and describing what is being replicated");
            sb.AppendLine("  Example:");
            sb.AppendLine("  // Step 1: Click 'Play' button to start the game");
            sb.AppendLine("  await Click(\"Play\");");
            sb.AppendLine("  // Step 2: Click 'Garage' to enter garage mode");
            sb.AppendLine("  await Click(\"Garage\");");
            sb.AppendLine();
            sb.AppendLine("=== FRAMEWORK DOCUMENTATION ===");
            sb.AppendLine(UITestPromptGenerator.GetDocumentation());
            sb.AppendLine();
            sb.AppendLine("=== RECORDING SUMMARY (prompt.md) ===");
            sb.AppendLine(promptMd);
            sb.AppendLine();
            sb.AppendLine("=== RECORDED STEPS (steps.log) ===");
            sb.AppendLine(stepsJson);
            sb.AppendLine();
            sb.AppendLine("=== UNITY LOG DURING RECORDING (log.txt) ===");
            sb.AppendLine(logTxt);

            return sb.ToString();
        }

        async Task<string> CallGeminiApi(string prompt)
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(2);

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{UITestSettings.GeminiModel}:generateContent?key={UITestSettings.GeminiApiKey}";

            string escapedPrompt = EscapeJsonString(prompt);

            string json = $@"{{
                ""contents"": [{{
                    ""parts"": [{{
                        ""text"": ""{escapedPrompt}""
                    }}]
                }}],
                ""generationConfig"": {{
                    ""temperature"": 0.2,
                    ""maxOutputTokens"": 8192
                }}
            }}";

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);

            string responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Gemini API error: {response.StatusCode} - {responseText}");
            }

            return responseText;
        }

        string EscapeJsonString(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 32)
                            sb.Append($"\\u{(int)c:X4}");
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        void ParseGeminiResponse(string response)
        {
            try
            {
                int textStart = response.IndexOf("\"text\":");
                if (textStart < 0) throw new Exception("Could not find text in response");

                textStart = response.IndexOf("\"", textStart + 7) + 1;
                int textEnd = response.LastIndexOf("\"");

                string text = response.Substring(textStart, textEnd - textStart);
                text = text.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\t", "\t");

                int classNameIndex = text.IndexOf("CLASS_NAME:");
                if (classNameIndex >= 0)
                {
                    int lineEnd = text.IndexOf("\n", classNameIndex);
                    suggestedClassName = text.Substring(classNameIndex + 11, lineEnd - classNameIndex - 11).Trim();
                }

                int codeStart = text.IndexOf("```csharp");
                int codeEnd = text.LastIndexOf("```");

                if (codeStart >= 0 && codeEnd > codeStart)
                {
                    generatedCode = text.Substring(codeStart + 9, codeEnd - codeStart - 9).Trim();
                }
                else
                {
                    generatedCode = text;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to parse response: {ex.Message}");
                Debug.Log($"Raw response: {response}");
                generatedCode = response;
            }
        }

        void SaveTestScript()
        {
            if (string.IsNullOrEmpty(generatedCode)) return;
            if (string.IsNullOrEmpty(lastRecordingPath)) return;

            string className = suggestedClassName;
            if (string.IsNullOrEmpty(className))
            {
                className = "UITestGenerated_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            }

            string filePath = Path.Combine(lastRecordingPath, $"{className}.cs");
            File.WriteAllText(filePath, generatedCode);

            string relativePath = filePath;
            if (filePath.StartsWith(Application.dataPath))
            {
                relativePath = "Assets" + filePath.Substring(Application.dataPath.Length);
            }

            AssetDatabase.Refresh();

            statusMessage = $"Saved to: {relativePath}";
            Debug.Log($"[UITestGenerator] Test saved to: {filePath}");

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(relativePath);
            if (script != null)
            {
                EditorGUIUtility.PingObject(script);
            }
        }
    }
}
