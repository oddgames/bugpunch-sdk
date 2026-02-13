using System;
using System.Collections.Generic;
using System.Net.Http;
using UnityEditor;
using UnityEngine;

namespace ODDGames.UIAutomation.Editor
{
    /// <summary>
    /// Registers UI Automation settings in Edit > Project Settings > UI Automation.
    /// Configures server connection, auto-upload, and manual upload.
    /// </summary>
    public class UIAutomationSettingsProvider : SettingsProvider
    {
        private string _serverUrl;
        private string _apiKey;
        private bool _autoUpload;
        private bool _uploadPasses;
        private bool _capturePersistentData;
        private string _connectionStatus;
        private bool _testing;

        public UIAutomationSettingsProvider()
            : base("Project/UI Automation", SettingsScope.Project)
        {
            label = "UI Automation";
            keywords = new HashSet<string> { "ui", "automation", "test", "server", "upload" };
        }

        [SettingsProvider]
        public static SettingsProvider CreateProvider() => new UIAutomationSettingsProvider();

        public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement)
        {
            _serverUrl = ServerSettings.ServerUrl;
            _apiKey = ServerSettings.ApiKey;
            _autoUpload = ServerSettings.AutoUpload;
            _uploadPasses = ServerSettings.UploadPasses;
            _capturePersistentData = ServerSettings.CapturePersistentData;
            _connectionStatus = null;
        }

        public override void OnGUI(string searchContext)
        {
            EditorGUILayout.Space(8);

            // --- Server Connection ---
            EditorGUILayout.LabelField("Test Result Server", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _serverUrl = EditorGUILayout.TextField("Server URL", _serverUrl);
            if (EditorGUI.EndChangeCheck())
                ServerSettings.ServerUrl = _serverUrl;

            EditorGUI.BeginChangeCheck();
            _apiKey = EditorGUILayout.PasswordField("API Key", _apiKey);
            if (EditorGUI.EndChangeCheck())
                ServerSettings.ApiKey = _apiKey;

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !_testing;
            if (GUILayout.Button(_testing ? "Testing..." : "Test Connection", GUILayout.Width(120)))
            {
                TestConnection();
            }
            GUI.enabled = true;

            if (!string.IsNullOrEmpty(_connectionStatus))
            {
                var style = new GUIStyle(EditorStyles.label) { richText = true };
                EditorGUILayout.LabelField(_connectionStatus, style);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(16);

            // --- Auto-Upload ---
            EditorGUILayout.LabelField("Auto-Upload", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _autoUpload = EditorGUILayout.Toggle("Enable Auto-Upload", _autoUpload);
            if (EditorGUI.EndChangeCheck())
                ServerSettings.AutoUpload = _autoUpload;

            GUI.enabled = _autoUpload;
            EditorGUI.BeginChangeCheck();
            _uploadPasses = EditorGUILayout.Toggle("Upload Passed Tests", _uploadPasses);
            if (EditorGUI.EndChangeCheck())
                ServerSettings.UploadPasses = _uploadPasses;
            GUI.enabled = true;

            if (_autoUpload)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.HelpBox(
                    "Test sessions will be automatically uploaded to the server after each test run.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(16);

            // --- Manual Upload ---
            EditorGUILayout.LabelField("Manual Upload", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            var sessions = TestReport.GetDiagnosticsFolders();
            EditorGUILayout.LabelField($"Local sessions: {sessions.Count}", EditorStyles.miniLabel);

            EditorGUILayout.Space(2);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = sessions.Count > 0;
            if (GUILayout.Button("Upload Latest Session", GUILayout.Width(150)))
            {
                UploadLatest();
            }
            if (GUILayout.Button("Upload All Sessions", GUILayout.Width(150)))
            {
                UploadAll();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(16);

            // --- Diagnostics ---
            EditorGUILayout.LabelField("Diagnostics", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _capturePersistentData = EditorGUILayout.Toggle("Capture Persistent Data on Failure", _capturePersistentData);
            if (EditorGUI.EndChangeCheck())
            {
                ServerSettings.CapturePersistentData = _capturePersistentData;
                TestReport.CapturePersistentData = _capturePersistentData;
            }

            EditorGUILayout.HelpBox(
                "When enabled, Application.persistentDataPath contents (save files, player prefs, logs) " +
                "are included in the diagnostic zip when a test fails. Max 50 MB, 3 levels deep.",
                MessageType.None);

            EditorGUILayout.Space(16);

            // --- Metadata ---
            EditorGUILayout.LabelField("Metadata", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Sessions automatically include:\n" +
                "  - Git/Plastic SCM branch & commit (auto-detected)\n" +
                "  - Application.version, platform, machine name\n" +
                "  - Custom metadata via TestReport.MetadataCallback",
                MessageType.None);

            EditorGUI.BeginChangeCheck();
            var autoVcs = EditorGUILayout.Toggle("Auto-Detect VCS", TestReport.AutoDetectVCS);
            if (EditorGUI.EndChangeCheck())
                TestReport.AutoDetectVCS = autoVcs;
        }

        private async void TestConnection()
        {
            _testing = true;
            _connectionStatus = "";

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);

                var url = _serverUrl.TrimEnd('/') + "/api/auth/validate-key";
                var json = $"{{\"key\":\"{_apiKey}\"}}";
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    // Parse minimal JSON: {"valid":true,"projectName":"..."}
                    if (body.Contains("\"valid\":true") || body.Contains("\"valid\": true"))
                    {
                        var nameStart = body.IndexOf("\"projectName\":\"", StringComparison.Ordinal);
                        if (nameStart >= 0)
                        {
                            nameStart += "\"projectName\":\"".Length;
                            var nameEnd = body.IndexOf("\"", nameStart, StringComparison.Ordinal);
                            var projectName = body.Substring(nameStart, nameEnd - nameStart);
                            _connectionStatus = $"<color=#4DCC4D>Connected (Project: {projectName})</color>";
                        }
                        else
                            _connectionStatus = "<color=#4DCC4D>Connected</color>";
                    }
                    else
                        _connectionStatus = "<color=#FF4D4D>Invalid API key</color>";
                }
                else
                    _connectionStatus = $"<color=#FF4D4D>HTTP {(int)response.StatusCode}</color>";
            }
            catch (Exception ex)
            {
                _connectionStatus = $"<color=#FF4D4D>Failed: {ex.Message}</color>";
            }

            _testing = false;
        }

        private async void UploadLatest()
        {
            var sessions = TestReport.GetDiagnosticsFolders();
            if (sessions.Count == 0) return;

            var latest = sessions[0];
            Debug.Log($"[UIAutomation] Uploading: {System.IO.Path.GetFileName(latest)}");
            var apiKey = string.IsNullOrEmpty(_apiKey) ? null : _apiKey;
            await TestReport.UploadSession(latest, _serverUrl, apiKey);
        }

        private async void UploadAll()
        {
            var sessions = TestReport.GetDiagnosticsFolders();
            if (sessions.Count == 0) return;

            Debug.Log($"[UIAutomation] Uploading {sessions.Count} session(s)...");
            var apiKey = string.IsNullOrEmpty(_apiKey) ? null : _apiKey;
            var uploaded = 0;
            foreach (var zip in sessions)
            {
                try
                {
                    await TestReport.UploadSession(zip, _serverUrl, apiKey);
                    uploaded++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[UIAutomation] Upload failed: {System.IO.Path.GetFileName(zip)}: {ex.Message}");
                }
            }
            Debug.Log($"[UIAutomation] Uploaded {uploaded}/{sessions.Count} session(s).");
        }
    }
}
