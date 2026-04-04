#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using ODDGames.UIAutomation;

namespace ODDGames.UIAutomation.AI.Editor
{
    /// <summary>
    /// Custom inspector for AITest ScriptableObjects.
    /// </summary>
    [CustomEditor(typeof(AITest))]
    public class AITestInspector : UnityEditor.Editor
    {
        private AITest test;
        private bool showHistory;
        private Vector2 historyScrollPos;

        private void OnEnable()
        {
            test = (AITest)target;
        }

        private void DrawTestRunnerInfo()
        {
            var hasPrompt = !string.IsNullOrWhiteSpace(test.prompt);

            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();
            var icon = hasPrompt ? EditorGUIUtility.IconContent("TestPassed") : EditorGUIUtility.IconContent("TestIgnored");
            GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));
            EditorGUILayout.LabelField("Test Runner Discovery", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            if (hasPrompt)
            {
                EditorGUILayout.LabelField($"This test will appear as: AITestDiscovery > {test.name}", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField("Add a prompt to enable test discovery", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Test Runner Discovery Info
            DrawTestRunnerInfo();

            EditorGUILayout.Space(5);

            // Test Definition Section
            EditorGUILayout.LabelField("Test Definition", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("prompt"),
                new GUIContent("Prompt", "What should the test do?"));

            // Validation warning
            if (string.IsNullOrWhiteSpace(test.prompt))
            {
                EditorGUILayout.HelpBox("A prompt is required for this test to appear in Test Runner.", MessageType.Warning);
            }

            EditorGUILayout.Space(10);

            // Knowledge Section
            EditorGUILayout.LabelField("Test-Specific Knowledge", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("knowledge"),
                new GUIContent("Knowledge", "Additional context for this specific test"));

            EditorGUILayout.Space(10);

            // Configuration Section
            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("model"),
                new GUIContent("Model", "Gemini model to use (leave empty for project default)"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxActions"),
                new GUIContent("Max Actions", "Maximum actions before timeout"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("timeoutSeconds"),
                new GUIContent("Timeout (s)", "Maximum time in seconds"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("actionDelaySeconds"),
                new GUIContent("Action Delay", "Delay between actions"));

            EditorGUILayout.Space(10);

            // Group Section
            EditorGUILayout.LabelField("Group", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_group"),
                new GUIContent("Test Group", "Group this test belongs to"));

            EditorGUILayout.Space(10);

            // Run Controls
            EditorGUILayout.LabelField("Run Test", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            // Open Test Runner button
            if (GUILayout.Button("Open Test Runner", GUILayout.Height(30)))
            {
                EditorApplication.ExecuteMenuItem("Window/General/Test Runner");
            }

            GUI.enabled = Application.isPlaying && !AITestRunner.IsRunning;
            if (GUILayout.Button("Run Now (Play Mode)", GUILayout.Height(30)))
            {
                RunTest();
            }
            GUI.enabled = true;

            if (AITestRunner.IsRunning)
            {
                if (GUILayout.Button("Cancel", GUILayout.Height(30), GUILayout.Width(80)))
                {
                    AITestRunner.Current?.Cancel();
                }
            }

            EditorGUILayout.EndHorizontal();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Use the Test Runner to run this test, or enter Play Mode to run directly.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(10);

            // History Section
            DrawHistorySection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawHistorySection()
        {
            var successfulRuns = test.SuccessfulRuns;

            showHistory = EditorGUILayout.Foldout(showHistory,
                $"Successful Runs ({successfulRuns.Count})");

            if (showHistory && successfulRuns.Count > 0)
            {
                EditorGUI.indentLevel++;

                historyScrollPos = EditorGUILayout.BeginScrollView(historyScrollPos, GUILayout.MaxHeight(200));

                for (int i = successfulRuns.Count - 1; i >= 0; i--)
                {
                    var run = successfulRuns[i];
                    var timestamp = new System.DateTime(run.timestampTicks);

                    EditorGUILayout.BeginHorizontal("box");
                    EditorGUILayout.LabelField($"Run {i + 1}", GUILayout.Width(50));
                    EditorGUILayout.LabelField($"{run.actions.Count} actions", GUILayout.Width(70));
                    EditorGUILayout.LabelField(timestamp.ToString("MM/dd HH:mm"));

                    if (GUILayout.Button("View", GUILayout.Width(50)))
                    {
                        ShowRunDetails(run);
                    }

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Clear History", GUILayout.Width(100)))
                {
                    if (EditorUtility.DisplayDialog("Clear History",
                        "Are you sure you want to clear all successful run history?", "Clear", "Cancel"))
                    {
                        test.ClearHistory();
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel--;
            }
        }

        private async void RunTest()
        {
            var settings = AITestSettings.Instance;
            var config = settings.CreateRunnerConfig();
            var globalKnowledge = settings.GetGlobalKnowledge();

            var runner = new AITestRunner(test, globalKnowledge, config);

            // Show debug panel if enabled
            if (settings.showDebugPanel)
            {
                AIDebugPanel.ShowWindow();
            }

            var result = await runner.RunAsync();

            // Save result
            var run = AITestRun.FromResult(result, AssetDatabase.GetAssetPath(test));
            AITestResultStore.Instance.SaveRun(run, result);

            // Show result
            EditorUtility.DisplayDialog(
                result.IsSuccess ? "Test Passed" : "Test Failed",
                $"{result.Message}\n\nActions: {result.ActionCount}\nDuration: {result.DurationSeconds:F1}s",
                "OK");
        }

        private void ShowRunDetails(ActionSequence run)
        {
            var message = $"Timestamp: {new System.DateTime(run.timestampTicks)}\n";
            message += $"Actions: {run.actions.Count}\n\n";

            foreach (var action in run.actions)
            {
                message += $"• {action.actionType}({action.target})\n";
            }

            EditorUtility.DisplayDialog("Run Details", message, "OK");
        }
    }
}
#endif
