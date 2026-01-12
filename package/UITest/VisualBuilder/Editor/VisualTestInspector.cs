using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ODDGames.UITest.VisualBuilder.Editor
{
    /// <summary>
    /// Custom inspector for VisualTest ScriptableObject assets.
    /// Provides a summary view and quick actions for visual tests.
    /// </summary>
    [CustomEditor(typeof(VisualTest))]
    public class VisualTestInspector : UnityEditor.Editor
    {
        private VisualTest visualTest;
        private bool showBlockDetails;
        private bool showAIPrompt;
        private Vector2 blockScrollPosition;

        // Styles
        private GUIStyle headerStyle;
        private GUIStyle subHeaderStyle;
        private GUIStyle blockSummaryStyle;
        private GUIStyle promptStyle;
        private bool stylesInitialized;

        // Block type colors (matching Scratch-inspired palette)
        private static readonly Dictionary<BlockType, Color> BlockColors = new Dictionary<BlockType, Color>
        {
            { BlockType.Click, new Color32(0x4C, 0x97, 0xFF, 0xFF) },  // Blue
            { BlockType.Type, new Color32(0x99, 0x66, 0xFF, 0xFF) },   // Purple
            { BlockType.Drag, new Color32(0xFF, 0x8C, 0x1A, 0xFF) },   // Orange
            { BlockType.Scroll, new Color32(0x59, 0xC0, 0x59, 0xFF) }, // Green
            { BlockType.Wait, new Color32(0xFF, 0xBF, 0x00, 0xFF) },   // Yellow
            { BlockType.Assert, new Color32(0xFF, 0x66, 0x80, 0xFF) }  // Pink/Red
        };

        private void OnEnable()
        {
            visualTest = (VisualTest)target;
        }

        private void InitializeStyles()
        {
            if (stylesInitialized) return;

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                margin = new RectOffset(0, 0, 8, 4)
            };

            subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                margin = new RectOffset(0, 0, 4, 2)
            };

            blockSummaryStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(8, 8, 8, 8),
                margin = new RectOffset(0, 0, 4, 4)
            };

            promptStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                padding = new RectOffset(8, 8, 8, 8)
            };

            stylesInitialized = true;
        }

        public override void OnInspectorGUI()
        {
            InitializeStyles();

            serializedObject.Update();

            DrawHeader();
            EditorGUILayout.Space(8);

            DrawTestInfo();
            EditorGUILayout.Space(8);

            DrawBlockSummary();
            EditorGUILayout.Space(8);

            DrawActionButtons();
            EditorGUILayout.Space(8);

            if (!string.IsNullOrEmpty(visualTest.originalPrompt))
            {
                DrawAIPromptSection();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal();

            var iconContent = EditorGUIUtility.IconContent("TestPassed");
            if (iconContent != null && iconContent.image != null)
            {
                GUILayout.Label(iconContent.image, GUILayout.Width(24), GUILayout.Height(24));
            }

            EditorGUILayout.LabelField("Visual Test", headerStyle);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTestInfo()
        {
            EditorGUILayout.LabelField("Test Information", subHeaderStyle);

            EditorGUI.BeginChangeCheck();

            // Test Name
            var testNameProp = serializedObject.FindProperty("testName");
            EditorGUILayout.PropertyField(testNameProp, new GUIContent("Test Name"));

            // Description
            var descriptionProp = serializedObject.FindProperty("description");
            EditorGUILayout.PropertyField(descriptionProp, new GUIContent("Description"));

            // Start Scene
            var startSceneProp = serializedObject.FindProperty("startScene");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(startSceneProp, new GUIContent("Start Scene"));

            // Scene picker button
            if (GUILayout.Button("...", GUILayout.Width(24)))
            {
                ShowScenePicker(startSceneProp);
            }
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(visualTest);
            }
        }

        private void ShowScenePicker(SerializedProperty sceneProp)
        {
            var menu = new GenericMenu();

            // Find all scenes in build settings
            for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
            {
                var scene = EditorBuildSettings.scenes[i];
                if (!scene.enabled) continue;

                var scenePath = scene.path;
                var sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);

                menu.AddItem(new GUIContent(sceneName), sceneProp.stringValue == sceneName, () =>
                {
                    sceneProp.stringValue = sceneName;
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(visualTest);
                });
            }

            if (menu.GetItemCount() == 0)
            {
                menu.AddDisabledItem(new GUIContent("No scenes in build settings"));
            }

            menu.ShowAsContext();
        }

        private void DrawBlockSummary()
        {
            EditorGUILayout.LabelField("Test Steps", subHeaderStyle);

            var blockCount = visualTest.blocks?.Count ?? 0;

            if (blockCount == 0)
            {
                EditorGUILayout.HelpBox("No blocks defined. Open in Visual Builder to add test steps.", MessageType.Info);
                return;
            }

            // Summary statistics
            var blockTypeCounts = GetBlockTypeCounts();

            EditorGUILayout.BeginVertical(blockSummaryStyle);

            EditorGUILayout.LabelField($"Total Steps: {blockCount}", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // Draw block type summary with colored boxes
            EditorGUILayout.BeginHorizontal();
            foreach (var kvp in blockTypeCounts.OrderByDescending(x => x.Value))
            {
                DrawBlockTypeChip(kvp.Key, kvp.Value);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            // Expandable block list
            showBlockDetails = EditorGUILayout.Foldout(showBlockDetails, $"Block Details ({blockCount})", true);
            if (showBlockDetails)
            {
                DrawBlockList();
            }
        }

        private Dictionary<BlockType, int> GetBlockTypeCounts()
        {
            var counts = new Dictionary<BlockType, int>();

            if (visualTest.blocks == null) return counts;

            foreach (var block in visualTest.blocks)
            {
                if (!counts.ContainsKey(block.type))
                {
                    counts[block.type] = 0;
                }
                counts[block.type]++;
            }

            return counts;
        }

        private void DrawBlockTypeChip(BlockType blockType, int count)
        {
            var color = BlockColors.GetValueOrDefault(blockType, Color.gray);
            var rect = GUILayoutUtility.GetRect(60, 20);

            // Draw colored background
            EditorGUI.DrawRect(rect, color);

            // Draw text
            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = blockType == BlockType.Wait ? Color.black : Color.white },
                fontStyle = FontStyle.Bold
            };
            GUI.Label(rect, $"{blockType} ({count})", labelStyle);

            GUILayout.Space(4);
        }

        private void DrawBlockList()
        {
            if (visualTest.blocks == null || visualTest.blocks.Count == 0) return;

            var maxHeight = Mathf.Min(visualTest.blocks.Count * 24 + 10, 200);
            blockScrollPosition = EditorGUILayout.BeginScrollView(blockScrollPosition, GUILayout.MaxHeight(maxHeight));

            for (int i = 0; i < visualTest.blocks.Count; i++)
            {
                var block = visualTest.blocks[i];
                DrawBlockListItem(i, block);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawBlockListItem(int index, VisualBlock block)
        {
            var color = BlockColors.GetValueOrDefault(block.type, Color.gray);

            EditorGUILayout.BeginHorizontal();

            // Index label
            EditorGUILayout.LabelField($"{index + 1}.", GUILayout.Width(24));

            // Colored block type indicator
            var colorRect = GUILayoutUtility.GetRect(8, 18, GUILayout.Width(8));
            EditorGUI.DrawRect(colorRect, color);

            // Block type
            EditorGUILayout.LabelField(block.type.ToString(), EditorStyles.boldLabel, GUILayout.Width(50));

            // Block description
            EditorGUILayout.LabelField(block.GetDisplayText(), EditorStyles.label);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();

            // Open in Visual Builder button
            if (GUILayout.Button(new GUIContent(" Open in Visual Builder", EditorGUIUtility.IconContent("d_CustomTool").image),
                GUILayout.Height(28)))
            {
                OpenInVisualBuilder();
            }

            // Run Test button
            GUI.enabled = Application.isPlaying && visualTest.blocks != null && visualTest.blocks.Count > 0;
            if (GUILayout.Button(new GUIContent(" Run Test", EditorGUIUtility.IconContent("PlayButton").image),
                GUILayout.Height(28)))
            {
                RunTest();
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            if (!Application.isPlaying && visualTest.blocks != null && visualTest.blocks.Count > 0)
            {
                EditorGUILayout.HelpBox("Enter Play mode to run this test.", MessageType.Info);
            }
        }

        private void DrawAIPromptSection()
        {
            EditorGUILayout.Space(4);

            showAIPrompt = EditorGUILayout.Foldout(showAIPrompt, "AI Recording Information", true);
            if (!showAIPrompt) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("Original Prompt:", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextArea(visualTest.originalPrompt, promptStyle, GUILayout.MinHeight(60));
            EditorGUI.EndDisabledGroup();

            if (!string.IsNullOrEmpty(visualTest.passCondition))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Pass Condition:", EditorStyles.boldLabel);
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField(visualTest.passCondition);
                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.EndVertical();
        }

        private void OpenInVisualBuilder()
        {
            var window = TestBuilder.ShowWindow();
            window.LoadTest(visualTest);
        }

        private void RunTest()
        {
            // TODO: Implement VisualTestRunner integration
            Debug.Log($"[VisualTest] Running test: {visualTest.testName}");
            Debug.LogWarning("[VisualTest] Test execution not yet implemented. Use Visual Builder window to run tests.");
        }

        /// <summary>
        /// Validates the visual test asset.
        /// </summary>
        [MenuItem("CONTEXT/VisualTest/Validate Test")]
        private static void ValidateTest(MenuCommand command)
        {
            var test = command.context as VisualTest;
            if (test == null) return;

            var issues = new List<string>();

            if (string.IsNullOrEmpty(test.testName))
            {
                issues.Add("Test name is empty");
            }

            if (test.blocks == null || test.blocks.Count == 0)
            {
                issues.Add("No blocks defined");
            }
            else
            {
                for (int i = 0; i < test.blocks.Count; i++)
                {
                    var block = test.blocks[i];
                    var blockIssues = ValidateBlock(block, i);
                    issues.AddRange(blockIssues);
                }
            }

            if (issues.Count == 0)
            {
                EditorUtility.DisplayDialog("Validation Passed",
                    $"Visual test '{test.testName}' is valid and ready to run.", "OK");
            }
            else
            {
                var message = string.Join("\n- ", issues.Prepend($"Found {issues.Count} issue(s):"));
                EditorUtility.DisplayDialog("Validation Failed", message, "OK");
            }
        }

        private static List<string> ValidateBlock(VisualBlock block, int index)
        {
            var issues = new List<string>();
            var prefix = $"Block {index + 1} ({block.type})";

            switch (block.type)
            {
                case BlockType.Click:
                case BlockType.Scroll:
                    if (block.target == null || !block.target.IsValid())
                    {
                        issues.Add($"{prefix}: No valid target element specified");
                    }
                    break;

                case BlockType.Type:
                    if (block.target == null || !block.target.IsValid())
                    {
                        issues.Add($"{prefix}: No valid target element specified");
                    }
                    if (string.IsNullOrEmpty(block.text))
                    {
                        issues.Add($"{prefix}: No text to type");
                    }
                    break;

                case BlockType.Drag:
                    if (block.target == null || !block.target.IsValid())
                    {
                        issues.Add($"{prefix}: No valid source element specified");
                    }
                    if (block.dragTarget == null && string.IsNullOrEmpty(block.dragDirection))
                    {
                        issues.Add($"{prefix}: No drag target or direction specified");
                    }
                    break;

                case BlockType.Wait:
                    if (block.waitSeconds <= 0)
                    {
                        issues.Add($"{prefix}: Wait duration must be positive");
                    }
                    break;

                case BlockType.Assert:
                    if (block.target == null || !block.target.IsValid())
                    {
                        issues.Add($"{prefix}: No valid target element specified");
                    }
                    if ((block.assertCondition == AssertCondition.TextEquals ||
                         block.assertCondition == AssertCondition.TextContains) &&
                        string.IsNullOrEmpty(block.assertExpected))
                    {
                        issues.Add($"{prefix}: Expected text value is empty");
                    }
                    break;
            }

            return issues;
        }

        /// <summary>
        /// Duplicates the visual test asset.
        /// </summary>
        [MenuItem("CONTEXT/VisualTest/Duplicate Test")]
        private static void DuplicateTest(MenuCommand command)
        {
            var source = command.context as VisualTest;
            if (source == null) return;

            var path = AssetDatabase.GetAssetPath(source);
            var directory = System.IO.Path.GetDirectoryName(path);
            var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            var extension = System.IO.Path.GetExtension(path);

            var newPath = AssetDatabase.GenerateUniqueAssetPath(
                System.IO.Path.Combine(directory, $"{fileName}_Copy{extension}"));

            var duplicate = Instantiate(source);
            duplicate.testName = source.testName + " (Copy)";

            // Generate new IDs for blocks
            if (duplicate.blocks != null)
            {
                foreach (var block in duplicate.blocks)
                {
                    block.id = Guid.NewGuid().ToString();
                }
            }

            AssetDatabase.CreateAsset(duplicate, newPath);
            AssetDatabase.SaveAssets();

            Selection.activeObject = duplicate;
            EditorGUIUtility.PingObject(duplicate);
        }
    }
}
