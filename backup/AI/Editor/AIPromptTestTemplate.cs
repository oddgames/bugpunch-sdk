#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

namespace ODDGames.UIAutomation.AI.Editor
{
    /// <summary>
    /// Creates new AI prompt test scripts from the Project window.
    /// Right-click in Project > Create > UI Automation > AI Prompt Test
    /// </summary>
    public static class AIPromptTestTemplate
    {
        [MenuItem("Assets/Create/UI Automation/AI Prompt Test", false, 80)]
        public static void CreateAIPromptTest()
        {
            string path = GetSelectedPath();
            string className = "NewAIPromptTest";

            // Find unique name
            int counter = 1;
            while (File.Exists(Path.Combine(path, $"{className}.cs")))
            {
                className = $"NewAIPromptTest{counter++}";
            }

            string filePath = Path.Combine(path, $"{className}.cs");
            string template = GenerateTemplate(className);

            File.WriteAllText(filePath, template);
            AssetDatabase.Refresh();

            // Select the new file
            var asset = AssetDatabase.LoadAssetAtPath<MonoScript>(filePath.Replace(Application.dataPath, "Assets"));
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }

            Debug.Log($"[AIPromptTest] Created new test at: {filePath}");
        }

        private static string GetSelectedPath()
        {
            string path = "Assets";

            foreach (Object obj in Selection.GetFiltered(typeof(Object), SelectionMode.Assets))
            {
                path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    path = Path.GetDirectoryName(path);
                }
                break;
            }

            return path.Replace("Assets", Application.dataPath);
        }

        private static string GenerateTemplate(string className)
        {
            return $@"using ODDGames.UIAutomation.AI;

/// <summary>
/// AI-driven test: Describe what this test does here.
/// Edit the Prompt property to define the test behavior.
/// </summary>
public class {className} : AIPromptTest
{{
    /// <summary>
    /// The test instruction in natural language.
    /// Describe what the AI should do step by step.
    /// </summary>
    protected override string Prompt => @""
Navigate to the main menu and:
1. Click the Settings button
2. Toggle the 'Enable Notifications' option
3. Click Save
4. Verify the success message appears
"";

    /// <summary>
    /// Scene to load before running the test.
    /// Leave null to use the currently loaded scene.
    /// </summary>
    protected override string SceneName => null;

    /// <summary>
    /// Additional context for the AI (optional).
    /// Describe app-specific patterns or hints.
    /// </summary>
    protected override string SystemPrompt => @""
The Settings button has a gear icon.
The save confirmation shows 'Settings saved!' text.
"";

    // Optional overrides:
    // protected override int MaxActions => 50;
    // protected override float TimeoutSeconds => 180f;
    // protected override float ActionDelay => 0.3f;
    // protected override bool SendScreenshots => true;
    // protected override string ModelId => ""gemini-2.5-flash"";
}}
";
        }
    }
}
#endif
