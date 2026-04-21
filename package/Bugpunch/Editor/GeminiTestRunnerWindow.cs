using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace ODDGames.Bugpunch.Editor
{
    /// <summary>
    /// Editor window for running Gemini AI-driven UI tests.
    /// Fetches test definitions from the server and runs them interactively.
    /// </summary>
    public class GeminiTestRunnerWindow : EditorWindow
    {
        // Settings keys
        private const string PrefGeminiKey = "UIAutomation.GeminiApiKey";
        private const string PrefGeminiModel = "UIAutomation.GeminiModel";
        private const string PrefServerUrl = "UIAutomation.GeminiServerUrl";
        private const string PrefServerApiKey = "UIAutomation.GeminiServerApiKey";
        private const string PrefMaxSteps = "UIAutomation.GeminiMaxSteps";
        private const string PrefUploadResults = "UIAutomation.GeminiUploadResults";

        // Settings
        private string _geminiApiKey = "";
        private int _modelIndex;
        private string _serverUrl = "http://localhost:5000";
        private string _serverApiKey = "";
        private int _maxSteps = 20;
        private bool _uploadResults = true;

        // Ad-hoc prompt mode
        private string _adHocPrompt = "";

        // State
        private List<GeminiTestDefinition> _tests = new List<GeminiTestDefinition>();
        private readonly List<string> _log = new List<string>();
        private Vector2 _logScroll;
        private Vector2 _testListScroll;
        private bool _isRunning;
        private bool _isFetching;
        private CancellationTokenSource _cts;
        private GeminiTestResult _lastResult;
        private readonly HashSet<int> _selectedTests = new HashSet<int>();
        private bool _selectAll = true;

        private static readonly string[] ModelOptions = {
            "gemini-2.0-flash",
            "gemini-2.5-flash",
            "gemini-2.5-pro"
        };

        [MenuItem("Window/UI Automation/Gemini Test Runner")]
        public static void ShowWindow()
        {
            var window = GetWindow<GeminiTestRunnerWindow>();
            window.titleContent = new GUIContent("Gemini Tests");
            window.minSize = new Vector2(400, 500);
        }

        private void OnEnable()
        {
            _geminiApiKey = EditorPrefs.GetString(PrefGeminiKey, "");
            _modelIndex = Mathf.Max(0, Array.IndexOf(ModelOptions, EditorPrefs.GetString(PrefGeminiModel, "gemini-2.0-flash")));
            _serverUrl = EditorPrefs.GetString(PrefServerUrl, "http://localhost:5000");
            _serverApiKey = EditorPrefs.GetString(PrefServerApiKey, "");
            _maxSteps = EditorPrefs.GetInt(PrefMaxSteps, 20);
            _uploadResults = EditorPrefs.GetBool(PrefUploadResults, true);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);

            DrawSettings();
            EditorGUILayout.Space(8);
            DrawServerTests();
            EditorGUILayout.Space(8);
            DrawAdHocPrompt();
            EditorGUILayout.Space(8);
            DrawLog();
        }

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _geminiApiKey = EditorGUILayout.PasswordField("Gemini API Key", _geminiApiKey);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(PrefGeminiKey, _geminiApiKey);

            EditorGUI.BeginChangeCheck();
            _modelIndex = EditorGUILayout.Popup("Model", _modelIndex, ModelOptions);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(PrefGeminiModel, ModelOptions[_modelIndex]);

            EditorGUI.BeginChangeCheck();
            _maxSteps = EditorGUILayout.IntSlider("Max Steps", _maxSteps, 1, 100);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetInt(PrefMaxSteps, _maxSteps);

            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _serverUrl = EditorGUILayout.TextField("Server URL", _serverUrl);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(PrefServerUrl, _serverUrl);

            EditorGUI.BeginChangeCheck();
            _serverApiKey = EditorGUILayout.PasswordField("Server API Key", _serverApiKey);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(PrefServerApiKey, _serverApiKey);

            EditorGUI.BeginChangeCheck();
            _uploadResults = EditorGUILayout.Toggle("Upload Results", _uploadResults);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetBool(PrefUploadResults, _uploadResults);
        }

        private void DrawServerTests()
        {
            EditorGUILayout.LabelField("Server Tests", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !_isFetching && !_isRunning;
            if (GUILayout.Button(_isFetching ? "Fetching..." : "Fetch Tests", GUILayout.Width(100)))
            {
                FetchTests();
            }

            GUI.enabled = !_isRunning && _tests.Count > 0 && _selectedTests.Count > 0;
            if (GUILayout.Button("Run Selected", GUILayout.Width(100)))
            {
                RunSelectedTests();
            }

            GUI.enabled = _isRunning;
            if (GUILayout.Button("Stop", GUILayout.Width(60)))
            {
                _cts?.Cancel();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (_tests.Count > 0)
            {
                EditorGUILayout.Space(4);

                EditorGUI.BeginChangeCheck();
                _selectAll = EditorGUILayout.ToggleLeft($"Select All ({_tests.Count} tests)", _selectAll);
                if (EditorGUI.EndChangeCheck())
                {
                    _selectedTests.Clear();
                    if (_selectAll)
                        for (int i = 0; i < _tests.Count; i++)
                            _selectedTests.Add(i);
                }

                _testListScroll = EditorGUILayout.BeginScrollView(_testListScroll, GUILayout.MaxHeight(150));
                for (int i = 0; i < _tests.Count; i++)
                {
                    var test = _tests[i];
                    EditorGUILayout.BeginHorizontal();

                    var selected = _selectedTests.Contains(i);
                    var newSelected = EditorGUILayout.ToggleLeft("", selected, GUILayout.Width(16));
                    if (newSelected != selected)
                    {
                        if (newSelected) _selectedTests.Add(i);
                        else _selectedTests.Remove(i);
                    }

                    EditorGUILayout.LabelField(test.Name, GUILayout.MinWidth(120));
                    EditorGUILayout.LabelField(test.Scene ?? "(current)", EditorStyles.miniLabel, GUILayout.Width(80));
                    EditorGUILayout.LabelField($"{test.MaxSteps} steps", EditorStyles.miniLabel, GUILayout.Width(60));
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
            else
            {
                EditorGUILayout.HelpBox("No tests loaded. Click 'Fetch Tests' to load from server.", MessageType.Info);
            }
        }

        private void DrawAdHocPrompt()
        {
            EditorGUILayout.LabelField("Ad-Hoc Test", EditorStyles.boldLabel);
            _adHocPrompt = EditorGUILayout.TextArea(_adHocPrompt, GUILayout.MinHeight(48));

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !_isRunning && !string.IsNullOrWhiteSpace(_adHocPrompt);
            if (GUILayout.Button("Run Prompt", GUILayout.Width(100)))
            {
                RunAdHocPrompt();
            }
            GUI.enabled = _isRunning;
            if (GUILayout.Button("Stop", GUILayout.Width(60)))
            {
                _cts?.Cancel();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLog()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
            if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(40)))
                _log.Clear();
            EditorGUILayout.EndHorizontal();

            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.MinHeight(120));
            var style = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, richText = true };
            foreach (var line in _log)
            {
                EditorGUILayout.LabelField(line, style);
            }
            EditorGUILayout.EndScrollView();

            if (_lastResult != null)
            {
                var status = _lastResult.Passed ? "PASSED" : "FAILED";
                var errorInfo = _lastResult.Errors.Count > 0
                    ? $"\nErrors: {_lastResult.Errors.Count}"
                    : "";
                EditorGUILayout.HelpBox(
                    $"{status}: {_lastResult.TestName}\n" +
                    $"Steps: {_lastResult.StepsExecuted}, Time: {_lastResult.TotalSeconds:F1}s" +
                    (_lastResult.FailReason != null ? $"\nReason: {_lastResult.FailReason}" : "") +
                    errorInfo,
                    _lastResult.Passed ? MessageType.Info : MessageType.Error);
            }
        }

        private void AppendLog(string message)
        {
            _log.Add($"[{DateTime.UtcNow:HH:mm:ss}] {message}");
            // Auto-scroll
            _logScroll.y = float.MaxValue;
            Repaint();
        }

        private async void FetchTests()
        {
            _isFetching = true;
            Repaint();

            try
            {
                var client = CreateClient();
                _tests = await client.FetchTests();
                _selectedTests.Clear();
                _selectAll = true;
                for (int i = 0; i < _tests.Count; i++)
                    _selectedTests.Add(i);
                AppendLog($"Fetched {_tests.Count} test(s) from server");
            }
            catch (Exception ex)
            {
                AppendLog($"<color=red>Fetch failed: {ex.Message}</color>");
            }

            _isFetching = false;
            Repaint();
        }

        private async void RunSelectedTests()
        {
            if (string.IsNullOrEmpty(_geminiApiKey))
            {
                AppendLog("<color=red>Gemini API key is required</color>");
                return;
            }

            _isRunning = true;
            _cts = new CancellationTokenSource();
            _lastResult = null;
            Repaint();

            try
            {
                var client = CreateClient();
                var indices = new List<int>(_selectedTests);
                indices.Sort();

                int passed = 0, total = indices.Count;
                for (int i = 0; i < indices.Count; i++)
                {
                    var test = _tests[indices[i]];
                    AppendLog($"--- [{i + 1}/{total}] {test.Name} ---");

                    var result = await client.RunTest(test, _cts.Token);
                    result.TestName = test.Name;
                    _lastResult = result;

                    if (result.Passed) passed++;

                    AppendLog(result.Passed
                        ? $"<color=green>PASS</color>: {test.Name} ({result.StepsExecuted} steps)"
                        : $"<color=red>FAIL</color>: {test.Name} - {result.FailReason}");
                }

                AppendLog($"=== Results: {passed}/{total} passed ===");
            }
            catch (OperationCanceledException)
            {
                AppendLog("Cancelled.");
            }
            catch (Exception ex)
            {
                AppendLog($"<color=red>Error: {ex.Message}</color>");
            }

            _isRunning = false;
            _cts = null;
            Repaint();
        }

        private async void RunAdHocPrompt()
        {
            if (string.IsNullOrEmpty(_geminiApiKey))
            {
                AppendLog("<color=red>Gemini API key is required</color>");
                return;
            }

            _isRunning = true;
            _cts = new CancellationTokenSource();
            _lastResult = null;
            Repaint();

            try
            {
                var runner = new GeminiTestRunner
                {
                    ApiKey = _geminiApiKey,
                    Model = ModelOptions[_modelIndex],
                    MaxSteps = _maxSteps,
                    OnLog = AppendLog,
                    OnStepComplete = step =>
                    {
                        var icon = step.Success ? "+" : "x";
                        AppendLog($"  [{icon}] {step.ActionJson ?? "(no action)"}");
                    },
                    CancelSource = _cts
                };

                var result = await runner.Run(_adHocPrompt);
                _lastResult = result;

                AppendLog(result.Passed
                    ? $"<color=green>PASS</color> ({result.StepsExecuted} steps, {result.TotalSeconds:F1}s)"
                    : $"<color=red>FAIL</color>: {result.FailReason}");

                if (_uploadResults && !string.IsNullOrEmpty(_serverApiKey))
                {
                    try
                    {
                        var client = CreateClient();
                        await client.UploadAdHocResult(result, _cts.Token);
                        AppendLog("Result uploaded to server.");
                    }
                    catch (Exception uploadEx)
                    {
                        AppendLog($"<color=yellow>Upload failed: {uploadEx.Message}</color>");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                AppendLog("Cancelled.");
            }
            catch (Exception ex)
            {
                AppendLog($"<color=red>Error: {ex.Message}</color>");
            }

            _isRunning = false;
            _cts = null;
            Repaint();
        }

        private GeminiTestClient CreateClient()
        {
            return new GeminiTestClient
            {
                ServerUrl = _serverUrl,
                ApiKey = _serverApiKey,
                GeminiApiKey = _geminiApiKey,
                GeminiModel = ModelOptions[_modelIndex],
                UploadResults = _uploadResults,
                OnLog = AppendLog
            };
        }
    }
}
