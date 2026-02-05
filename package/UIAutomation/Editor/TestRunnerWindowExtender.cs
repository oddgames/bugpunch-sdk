using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ODDGames.UIAutomation.AI;

namespace ODDGames.UIAutomation.Editor
{
    /// <summary>
    /// Uses reflection to inject buttons into Unity's Test Runner window.
    /// Adds "Copy Failed", "+ AI Test", and "Edit" buttons.
    /// </summary>
    [InitializeOnLoad]
    public static class TestRunnerWindowExtender
    {
        private static Type _testRunnerWindowType;
        private static EditorWindow _cachedWindow;
        private static bool _initialized;
        private static bool _buttonInjected;
        private static UnityEngine.UIElements.IMGUIContainer _container;

        // Threshold below which we use compact mode
        private const float CompactModeThreshold = 450f;

        static TestRunnerWindowExtender()
        {
            EditorApplication.update += TryInitialize;
        }

        private static void TryInitialize()
        {
            if (_initialized) return;

            // Find the TestRunnerWindow type
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                _testRunnerWindowType = assembly.GetType("UnityEditor.TestTools.TestRunner.TestRunnerWindow");
                if (_testRunnerWindowType != null)
                    break;
            }

            if (_testRunnerWindowType != null)
            {
                _initialized = true;
                EditorApplication.update += CheckForTestRunnerWindow;
            }
        }

        private static void CheckForTestRunnerWindow()
        {
            // Find open Test Runner window
            var windows = Resources.FindObjectsOfTypeAll(_testRunnerWindowType);
            if (windows.Length > 0)
            {
                var window = windows[0] as EditorWindow;
                if (window != null && window != _cachedWindow)
                {
                    _cachedWindow = window;
                    _buttonInjected = false;
                }

                // Draw our button overlay when window is visible
                if (window != null && !_buttonInjected)
                {
                    // Use rootVisualElement to add a floating button
                    var root = window.rootVisualElement;
                    if (root != null && root.childCount == 0)
                    {
                        // The window uses IMGUI, add an overlay container using IMGUIContainer
                        _container = new UnityEngine.UIElements.IMGUIContainer(DrawButtons);
                        _container.name = "uiautomation-buttons-container";
                        _container.style.position = UnityEngine.UIElements.Position.Absolute;
                        _container.style.top = 2;
                        _container.style.right = 5;
                        _container.style.height = 20;
                        root.Add(_container);
                        _buttonInjected = true;
                    }
                }
            }
            else
            {
                _cachedWindow = null;
                _buttonInjected = false;
                _container = null;
            }
        }

        private static void DrawButtons()
        {
            if (_cachedWindow == null) return;

            float windowWidth = _cachedWindow.position.width;
            bool compactMode = windowWidth < CompactModeThreshold;

            GUILayout.BeginHorizontal();

            // Refresh button - always show
            if (GUILayout.Button("↻", GUILayout.Width(22)))
            {
                RefreshTests();
            }

            // Copy Failed button
            if (GUILayout.Button(compactMode ? "Copy" : "Copy Failed", GUILayout.Width(compactMode ? 40 : 70)))
            {
                CopyFailedTestsMenu.CopyFailedTests();
            }

            // New AI Test button - creates a ScriptableObject
            if (GUILayout.Button(compactMode ? "+AI" : "+ AI Test", GUILayout.Width(compactMode ? 32 : 60)))
            {
                CreateNewAITest();
            }

            // Edit button - opens selected AITest in inspector
            var selectedAITest = TryGetSelectedAITest();
            GUI.enabled = selectedAITest != null;
            if (GUILayout.Button("Edit", GUILayout.Width(35)))
            {
                if (selectedAITest != null)
                {
                    Selection.activeObject = selectedAITest;
                    EditorGUIUtility.PingObject(selectedAITest);
                }
            }
            GUI.enabled = true;

            GUILayout.EndHorizontal();
        }

        private static void RefreshTests()
        {
            // NUnit caches TestCaseSource results - the only way to refresh is domain reload
            // Touch the discovery file to trigger recompilation
            var discoveryScript = AssetDatabase.FindAssets("t:MonoScript AITestDiscovery")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.EndsWith("AITestDiscovery.cs"));

            if (!string.IsNullOrEmpty(discoveryScript))
            {
                // Touch the file to trigger recompile
                AssetDatabase.ImportAsset(discoveryScript, ImportAssetOptions.ForceUpdate);
            }

            AssetDatabase.Refresh();
            Debug.Log("[UIAutomation] Triggering domain reload to refresh AI tests...");
        }

        private static void CreateNewAITest()
        {
            // Determine where to create the test
            string folder = "Assets/Tests/AITests";
            if (!Directory.Exists(folder))
            {
                // Try Assets/Tests
                folder = "Assets/Tests";
                if (!Directory.Exists(folder))
                {
                    folder = "Assets";
                }
            }

            // Create the asset
            var test = ScriptableObject.CreateInstance<AITest>();
            test.prompt = "Describe what this test should do...";

            var path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, "NewAITest.asset"));
            AssetDatabase.CreateAsset(test, path);
            AssetDatabase.SaveAssets();

            Selection.activeObject = test;
            EditorGUIUtility.PingObject(test);

            Debug.Log($"[UIAutomation] Created new AI test: {path}\nThis test will appear in Test Runner under 'AITestDiscovery > {test.name}'.");
        }

        private static AITest TryGetSelectedAITest()
        {
            // Check if an AITest asset is currently selected
            if (Selection.activeObject is AITest aiTest)
            {
                return aiTest;
            }
            return null;
        }
    }
}
