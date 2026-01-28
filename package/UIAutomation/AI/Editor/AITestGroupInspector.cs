#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Linq;
using ODDGames.UIAutomation;

namespace ODDGames.UIAutomation.AI.Editor
{
    /// <summary>
    /// Custom inspector for AITestGroup ScriptableObjects.
    /// </summary>
    [CustomEditor(typeof(AITestGroup))]
    public class AITestGroupInspector : UnityEditor.Editor
    {
        private AITestGroup group;
        private bool showTests = true;
        private Vector2 testsScrollPos;

        private void OnEnable()
        {
            group = (AITestGroup)target;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Group Info Section
            EditorGUILayout.LabelField("Group Info", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"),
                new GUIContent("Display Name"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("description"),
                new GUIContent("Description"));

            EditorGUILayout.Space(10);

            // Knowledge Section
            EditorGUILayout.LabelField("Group Knowledge", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This knowledge is sent to the AI for all tests in this group. Describe shared UI patterns, navigation, and context.",
                MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("knowledge"),
                new GUIContent("Knowledge"));

            EditorGUILayout.Space(10);

            // Default Configuration Section
            EditorGUILayout.LabelField("Default Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultTier"),
                new GUIContent("Default Tier"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultTimeout"),
                new GUIContent("Default Timeout (s)"));

            EditorGUILayout.Space(10);

            // Tests Section
            DrawTestsSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTestsSection()
        {
            var tests = group.Tests;

            showTests = EditorGUILayout.Foldout(showTests,
                $"Tests in Group ({tests.Count})");

            if (!showTests)
                return;

            EditorGUI.indentLevel++;

            if (tests.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No tests in this group. Create AI Tests and assign this group to them.",
                    MessageType.Info);
            }
            else
            {
                testsScrollPos = EditorGUILayout.BeginScrollView(testsScrollPos, GUILayout.MaxHeight(200));

                for (int i = 0; i < tests.Count; i++)
                {
                    var test = tests[i];
                    if (test == null)
                        continue;

                    EditorGUILayout.BeginHorizontal("box");

                    // Test name
                    EditorGUILayout.LabelField(test.name, GUILayout.ExpandWidth(true));

                    // Select button
                    if (GUILayout.Button("Select", GUILayout.Width(50)))
                    {
                        Selection.activeObject = test;
                    }

                    // Run button
                    GUI.enabled = Application.isPlaying && !AITestRunner.IsRunning;
                    if (GUILayout.Button("Run", GUILayout.Width(40)))
                    {
                        RunTest(test);
                    }
                    GUI.enabled = true;

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.Space(5);

            // Run All button
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            GUI.enabled = Application.isPlaying && !AITestRunner.IsRunning && tests.Count > 0;
            if (GUILayout.Button("Run All Tests", GUILayout.Width(120)))
            {
                RunAllTests();
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode to run tests.", MessageType.Info);
            }

            EditorGUI.indentLevel--;
        }

        private async void RunTest(AITest test)
        {
            var settings = AITestSettings.Instance;
            var config = settings.CreateRunnerConfig();
            var globalKnowledge = settings.GetGlobalKnowledge();

            var runner = new AITestRunner(test, globalKnowledge, config);

            if (settings.showDebugPanel)
            {
                AIDebugPanel.ShowWindow();
            }

            var result = await runner.RunAsync();

            var run = AITestRun.FromResult(result, AssetDatabase.GetAssetPath(test));
            AITestResultStore.Instance.SaveRun(run, result);

            Debug.Log($"[AITest] {test.name}: {result.Status} - {result.Message}");
        }

        private async void RunAllTests()
        {
            var settings = AITestSettings.Instance;
            var config = settings.CreateRunnerConfig();
            var globalKnowledge = settings.GetGlobalKnowledge();

            if (settings.showDebugPanel)
            {
                AIDebugPanel.ShowWindow();
            }

            int passed = 0;
            int failed = 0;

            foreach (var test in group.Tests)
            {
                if (test == null)
                    continue;

                Debug.Log($"[AITest] Running: {test.name}");

                var runner = new AITestRunner(test, globalKnowledge, config);
                var result = await runner.RunAsync();

                var run = AITestRun.FromResult(result, AssetDatabase.GetAssetPath(test));
                AITestResultStore.Instance.SaveRun(run, result);

                if (result.IsSuccess)
                    passed++;
                else
                    failed++;

                Debug.Log($"[AITest] {test.name}: {result.Status}");
            }

            EditorUtility.DisplayDialog("Group Run Complete",
                $"Passed: {passed}\nFailed: {failed}\nTotal: {passed + failed}",
                "OK");
        }
    }
}
#endif
