using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using ODDGames.UITest;
using ODDGames.UITest.VisualBuilder;
using ODDGames.UITest.VisualBuilder.Editor;
using Cysharp.Threading.Tasks;
#if UITEST_AI
using ODDGames.UITest.AI;
using ODDGames.UITest.AI.Editor;
#endif

namespace ODDGames.UITest.Editor
{
    public class UITestExplorerWindow : EditorWindow
    {
        private const string PREFS_PREFIX = "UITestExplorer_";
        private const string PREFS_RESULTS = PREFS_PREFIX + "Results";
        private const string PREFS_SEARCH = PREFS_PREFIX + "Search";
        private const string PREFS_FILTER = PREFS_PREFIX + "Filter";

        [SerializeField] private TreeViewState treeViewState;
        [SerializeField] private string searchString = "";
        [SerializeField] private TestStatusFilter statusFilter = TestStatusFilter.All;
        [SerializeField] private TestSeverity? severityFilter = null;
        [SerializeField] private float splitterPosition = 0.6f;

        private UITestTreeView treeView;
        private SearchField searchField;
        private List<UITestItem> allTests = new List<UITestItem>();
        private Dictionary<string, TestResult> testResults = new Dictionary<string, TestResult>();
        private UITestItem selectedTest;
        private Vector2 detailsScrollPosition;
        private bool isResizingSplitter;
        private bool isDraggingSplitter;

        // Styles
        private GUIStyle toolbarStyle;
        private GUIStyle headerStyle;
        private GUIStyle detailsLabelStyle;
        private GUIStyle detailsValueStyle;
        private GUIStyle logStyle;
        private bool stylesInitialized;

        [MenuItem("Window/Analysis/UI Automation/Test Explorer")]
        public static void ShowWindow()
        {
            var window = GetWindow<UITestExplorerWindow>();
            window.titleContent = new GUIContent("UI Test Explorer", EditorGUIUtility.IconContent("TestPassed").image);
            window.minSize = new Vector2(300, 200);
            window.Show();
        }

        private void OnEnable()
        {
            if (treeViewState == null)
                treeViewState = new TreeViewState();

            searchField = new SearchField();
            if (treeView != null)
                searchField.downOrUpArrowKeyPressed += treeView.SetFocusAndEnsureSelectedItem;

            LoadPersistedResults();
            RefreshTests();

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            Application.logMessageReceived += OnLogMessage;
        }

        private void OnDisable()
        {
            SavePersistedResults();
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            Application.logMessageReceived -= OnLogMessage;
        }

        private void InitializeStyles()
        {
            if (stylesInitialized) return;

            toolbarStyle = new GUIStyle(EditorStyles.toolbar);

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                margin = new RectOffset(4, 4, 4, 4)
            };

            detailsLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Bold,
                fixedWidth = 80
            };

            detailsValueStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true
            };

            logStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 11,
                richText = true,
                wordWrap = true,
                padding = new RectOffset(8, 8, 8, 8)
            };

            stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitializeStyles();

            DrawToolbar();

            var contentRect = new Rect(0, EditorStyles.toolbar.fixedHeight, position.width, position.height - EditorStyles.toolbar.fixedHeight);

            // Split view: tree on left, details on right
            var treeWidth = position.width * splitterPosition;
            var detailsWidth = position.width * (1 - splitterPosition);

            var treeRect = new Rect(contentRect.x, contentRect.y, treeWidth - 2, contentRect.height);
            var splitterRect = new Rect(treeWidth - 2, contentRect.y, 4, contentRect.height);
            var detailsRect = new Rect(treeWidth + 2, contentRect.y, detailsWidth - 4, contentRect.height);

            DrawTreeView(treeRect);
            DrawSplitter(splitterRect);
            DrawDetailsPanel(detailsRect);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Run All button
            if (GUILayout.Button(new GUIContent("Run All", EditorGUIUtility.IconContent("PlayButton").image, "Run all tests"), EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                RunAllTests();
            }

            // Run Selected button
            GUI.enabled = selectedTest != null;
            if (GUILayout.Button(new GUIContent("Run", EditorGUIUtility.IconContent("PlayButton").image, "Run selected test"), EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                RunSelectedTest();
            }
            GUI.enabled = true;

            GUILayout.Space(5);

            // Auto-Explore dropdown
            GUI.enabled = !AutoExplorer.IsExploring;
            if (EditorGUILayout.DropdownButton(new GUIContent("Auto-Explore", "Run automated UI exploration"), FocusType.Keyboard, EditorStyles.toolbarDropDown, GUILayout.Width(90)))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("30 seconds"), false, () => StartAutoExplore(30f, 0));
                menu.AddItem(new GUIContent("60 seconds"), false, () => StartAutoExplore(60f, 0));
                menu.AddItem(new GUIContent("5 minutes"), false, () => StartAutoExplore(300f, 0));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("20 actions"), false, () => StartAutoExplore(0f, 20));
                menu.AddItem(new GUIContent("100 actions"), false, () => StartAutoExplore(0f, 100));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Until dead end"), false, () => StartAutoExploreDeadEnd());
                menu.ShowAsContext();
            }
            GUI.enabled = true;

            // Stop button (only visible when exploring)
            if (AutoExplorer.IsExploring)
            {
                if (GUILayout.Button(new GUIContent("Stop", EditorGUIUtility.IconContent("PauseButton").image, "Stop auto-exploration"), EditorStyles.toolbarButton, GUILayout.Width(50)))
                {
                    AutoExplorer.StopExploration();
                }
            }

            GUILayout.Space(5);

            // Refresh button
            if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("Refresh").image, "Refresh test list"), EditorStyles.toolbarButton, GUILayout.Width(28)))
            {
                RefreshTests();
            }

            // Clear Results button
            if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("TreeEditor.Trash").image, "Clear all results"), EditorStyles.toolbarButton, GUILayout.Width(28)))
            {
                ClearResults();
            }

            GUILayout.FlexibleSpace();

            // Status filter dropdown
            EditorGUI.BeginChangeCheck();
            statusFilter = (TestStatusFilter)EditorGUILayout.EnumPopup(statusFilter, EditorStyles.toolbarDropDown, GUILayout.Width(80));
            if (EditorGUI.EndChangeCheck())
            {
                ApplyFilters();
            }

            // Search field
            EditorGUI.BeginChangeCheck();
            searchString = searchField.OnToolbarGUI(searchString, GUILayout.Width(150));
            if (EditorGUI.EndChangeCheck())
            {
                ApplyFilters();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void StartAutoExplore(float seconds, int actions)
        {
            if (!EditorApplication.isPlaying)
            {
                EditorPrefs.SetFloat("AutoExplorer_Duration", seconds);
                EditorPrefs.SetInt("AutoExplorer_Actions", actions);
                EditorPrefs.SetBool("AutoExplorer_DeadEnd", false);
                EditorPrefs.SetBool("AutoExplorer_Pending", true);

                EditorApplication.playModeStateChanged += OnAutoExplorePlayModeChanged;
                EditorApplication.EnterPlaymode();
            }
            else
            {
                RunAutoExploreAsync(seconds, actions, false).Forget();
            }
        }

        private void StartAutoExploreDeadEnd()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorPrefs.SetFloat("AutoExplorer_Duration", 0);
                EditorPrefs.SetInt("AutoExplorer_Actions", 0);
                EditorPrefs.SetBool("AutoExplorer_DeadEnd", true);
                EditorPrefs.SetBool("AutoExplorer_Pending", true);

                EditorApplication.playModeStateChanged += OnAutoExplorePlayModeChanged;
                EditorApplication.EnterPlaymode();
            }
            else
            {
                RunAutoExploreAsync(0, 0, true).Forget();
            }
        }

        private void OnAutoExplorePlayModeChanged(PlayModeStateChange state)
        {
            // Just unsubscribe - the actual exploration is started by AutoExplorer.CheckPendingExploration()
            // which uses [RuntimeInitializeOnLoadMethod] for reliable startup
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.playModeStateChanged -= OnAutoExplorePlayModeChanged;
            }
        }

        private async UniTaskVoid RunAutoExploreAsync(float seconds, int actions, bool deadEnd)
        {
            await UniTask.DelayFrame(30);

            var settings = new ExploreSettings
            {
                DurationSeconds = seconds,
                MaxActions = actions,
                StopOnDeadEnd = deadEnd,
                Seed = null,
                DelayBetweenActions = 0.5f,
                TryBackOnStuck = true
            };

            Debug.Log($"[AutoExplorer] Starting - Duration: {seconds}s, Actions: {actions}, DeadEnd: {deadEnd}");
            var result = await AutoExplorer.StartExploration(settings);
            Debug.Log($"[AutoExplorer] Completed - {result.ActionsPerformed} actions in {result.DurationSeconds:F1}s. Reason: {result.StopReason}");
        }

        private void DrawTreeView(Rect rect)
        {
            if (treeView != null)
            {
                treeView.OnGUI(rect);

                // Handle selection change
                var selection = treeView.GetSelection();
                if (selection.Count > 0)
                {
                    var item = treeView.FindItem(selection[0]);
                    if (item != selectedTest)
                    {
                        selectedTest = item;
                        Repaint();
                    }
                }
            }
        }

        private void DrawSplitter(Rect rect)
        {
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                isDraggingSplitter = true;
                Event.current.Use();
            }

            if (isDraggingSplitter)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    splitterPosition = Mathf.Clamp(Event.current.mousePosition.x / position.width, 0.2f, 0.8f);
                    Repaint();
                }
                else if (Event.current.type == EventType.MouseUp)
                {
                    isDraggingSplitter = false;
                }
            }

            // Draw splitter line
            EditorGUI.DrawRect(new Rect(rect.x + 1, rect.y, 1, rect.height), new Color(0.1f, 0.1f, 0.1f, 0.5f));
        }

        private void DrawDetailsPanel(Rect rect)
        {
            GUILayout.BeginArea(rect);
            detailsScrollPosition = EditorGUILayout.BeginScrollView(detailsScrollPosition);

            if (selectedTest == null)
            {
                EditorGUILayout.HelpBox("Select a test to view details", MessageType.Info);
#if UITEST_AI
                DrawCreateAITestButtons();
#endif
                DrawCreateVisualTestButton();
            }
            else if (selectedTest.IsGroup)
            {
                if (selectedTest.IsVisualTestGroup)
                {
                    DrawVisualTestGroupDetails(selectedTest);
                }
#if UITEST_AI
                else if (selectedTest.IsAITestGroup)
                {
                    DrawAITestGroupDetails(selectedTest);
                }
#endif
                else
                {
                    DrawGroupDetails(selectedTest);
                }
            }
            else
            {
                if (selectedTest.IsVisualTest)
                {
                    DrawVisualTestDetails(selectedTest);
                }
#if UITEST_AI
                else if (selectedTest.IsAITest)
                {
                    DrawAITestDetails(selectedTest);
                }
#endif
                else
                {
                    DrawTestDetails(selectedTest);
                }
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawGroupDetails(UITestItem group)
        {
            EditorGUILayout.LabelField(group.DisplayName, headerStyle);
            EditorGUILayout.Space();

            var children = GetAllTestsInGroup(group);
            var passed = children.Count(t => GetTestStatus(t) == TestStatus.Passed);
            var failed = children.Count(t => GetTestStatus(t) == TestStatus.Failed);
            var notRun = children.Count(t => GetTestStatus(t) == TestStatus.NotRun);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Tests:", detailsLabelStyle);
            EditorGUILayout.LabelField($"{children.Count} total", detailsValueStyle);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Status:", detailsLabelStyle);
            var statusText = $"<color=#4CAF50>{passed} passed</color>, <color=#F44336>{failed} failed</color>, {notRun} not run";
            EditorGUILayout.LabelField(statusText, new GUIStyle(detailsValueStyle) { richText = true });
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            if (GUILayout.Button("Run All in Group", GUILayout.Height(24)))
            {
                RunTestGroup(group);
            }
        }

        // === Visual Test Support ===

        private void DrawCreateVisualTestButton()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Create New Test", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("New Test Script", GUILayout.Height(30)))
            {
                CreateTestScript();
            }
            if (GUILayout.Button("New Visual Test", GUILayout.Height(30)))
            {
                CreateVisualTest();
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Open Test Builder", GUILayout.Height(24)))
            {
                TestBuilder.ShowWindow();
            }
        }

        private void CreateTestScript()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "Create Test Script",
                "NewUITest",
                "cs",
                "Create a new UITest script");

            if (string.IsNullOrEmpty(path)) return;

            var className = Path.GetFileNameWithoutExtension(path);
            // Sanitize class name (remove invalid characters)
            className = System.Text.RegularExpressions.Regex.Replace(className, @"[^a-zA-Z0-9_]", "");
            if (char.IsDigit(className[0]))
                className = "_" + className;

            // Get next available scenario number
            var nextScenario = GetNextAvailableScenario();

            var template = $@"using Cysharp.Threading.Tasks;
using ODDGames.UITest;
using UnityEngine;

/// <summary>
/// UI Test: {className}
/// </summary>
[UITest(
    scenario: {nextScenario},
    Feature = ""Feature Name"",
    Story = ""Story Description"",
    Severity = TestSeverity.Normal,
    Description = ""Test description goes here""
)]
public class {className} : UITestBehaviour
{{
    protected override async UniTask RunTest()
    {{
        // Arrange
        await Wait(0.5f);

        // Act
        // await Click(""ButtonName"");

        // Assert
        // await AssertExists(""ExpectedElement"");

        Pass(""Test completed successfully"");
    }}
}}
";

            File.WriteAllText(path, template);
            AssetDatabase.Refresh();

            // Open the script in the editor
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script != null)
            {
                Selection.activeObject = script;
                EditorGUIUtility.PingObject(script);
                AssetDatabase.OpenAsset(script);
            }

            RefreshTests();
        }

        private int GetNextAvailableScenario()
        {
            var usedScenarios = new HashSet<int>();
            foreach (var item in allTests)
            {
                CollectScenarios(item, usedScenarios);
            }

            // Start from 1 and find the first unused scenario
            int scenario = 1;
            while (usedScenarios.Contains(scenario))
            {
                scenario++;
            }
            return scenario;
        }

        private void CollectScenarios(UITestItem item, HashSet<int> scenarios)
        {
            if (!item.IsGroup && item.Attribute != null && item.Attribute.Scenario > 0)
            {
                scenarios.Add(item.Attribute.Scenario);
            }

            if (item.Children != null)
            {
                foreach (var child in item.Children)
                {
                    CollectScenarios(child, scenarios);
                }
            }
        }

        private void CreateVisualTest()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "Create Visual Test",
                "NewVisualTest",
                "asset",
                "Create a new visual test asset");

            if (string.IsNullOrEmpty(path)) return;

            var test = ScriptableObject.CreateInstance<VisualTest>();
            test.testName = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(test, path);
            AssetDatabase.SaveAssets();

            Selection.activeObject = test;
            EditorGUIUtility.PingObject(test);

            RefreshTests();

            // Open in Visual Builder
            var window = TestBuilder.ShowWindow();
            window.LoadTest(test);
        }

        private void DrawVisualTestGroupDetails(UITestItem group)
        {
            EditorGUILayout.LabelField(group.DisplayName, headerStyle);
            EditorGUILayout.Space();

            var children = GetAllTestsInGroup(group);
            var passed = children.Count(t => GetTestStatus(t) == TestStatus.Passed);
            var failed = children.Count(t => GetTestStatus(t) == TestStatus.Failed);
            var notRun = children.Count(t => GetTestStatus(t) == TestStatus.NotRun);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Tests:", detailsLabelStyle);
            EditorGUILayout.LabelField($"{children.Count} total", detailsValueStyle);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Status:", detailsLabelStyle);
            var statusText = $"<color=#4CAF50>{passed} passed</color>, <color=#F44336>{failed} failed</color>, {notRun} not run";
            EditorGUILayout.LabelField(statusText, new GUIStyle(detailsValueStyle) { richText = true });
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Run all visual tests in group
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = Application.isPlaying;
            if (GUILayout.Button("Run All in Group", GUILayout.Height(30)))
            {
                RunVisualTestGroup(group);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode to run visual tests.", MessageType.Info);
            }

            EditorGUILayout.Space();

            // Create new test button
            if (GUILayout.Button("Create New Visual Test", GUILayout.Height(24)))
            {
                CreateVisualTest();
            }
        }

        private void DrawVisualTestDetails(UITestItem test)
        {
            var visualTest = test.VisualTest;
            if (visualTest == null)
            {
                EditorGUILayout.HelpBox("Visual Test asset not found.", MessageType.Error);
                return;
            }

            // Header with status icon
            EditorGUILayout.BeginHorizontal();
            var status = GetTestStatus(test);
            var statusIcon = GetStatusIcon(status);
            GUILayout.Label(statusIcon, GUILayout.Width(20), GUILayout.Height(20));
            EditorGUILayout.LabelField(test.DisplayName, headerStyle);

            // Open in Inspector button
            if (GUILayout.Button("Inspector", GUILayout.Width(70)))
            {
                Selection.activeObject = visualTest;
                EditorGUIUtility.PingObject(visualTest);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Test metadata
            if (!string.IsNullOrEmpty(visualTest.description))
            {
                EditorGUILayout.LabelField("Description:", detailsLabelStyle);
                EditorGUILayout.LabelField(visualTest.description, EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space();
            }

            DrawDetailRow("Blocks:", $"{visualTest.blocks?.Count ?? 0} steps");

            if (!string.IsNullOrEmpty(visualTest.startScene))
            {
                DrawDetailRow("Start Scene:", visualTest.startScene);
            }

            // Block summary
            if (visualTest.blocks != null && visualTest.blocks.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Block Types:", detailsLabelStyle);

                var blockTypeCounts = visualTest.blocks
                    .GroupBy(b => b.type)
                    .ToDictionary(g => g.Key, g => g.Count());

                EditorGUILayout.BeginHorizontal();
                foreach (var kvp in blockTypeCounts.OrderByDescending(x => x.Value))
                {
                    GUILayout.Label($"{kvp.Key}: {kvp.Value}", EditorStyles.miniLabel);
                    GUILayout.Space(10);
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            // AI prompt if generated
            if (!string.IsNullOrEmpty(visualTest.originalPrompt))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Generated from AI:", detailsLabelStyle);
                EditorGUILayout.LabelField(visualTest.originalPrompt, EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.Space(10);

            // Action buttons
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Open in Visual Builder", GUILayout.Height(28)))
            {
                var window = TestBuilder.ShowWindow();
                window.LoadTest(visualTest);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            GUI.enabled = Application.isPlaying;
            if (GUILayout.Button("Run", GUILayout.Height(28)))
            {
                RunVisualTest(test);
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode to run this test.", MessageType.Info);
            }

            // Last run results
            if (testResults.TryGetValue(test.FullName, out var result))
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Last Run", headerStyle);

                DrawDetailRow("Status:", result.Status.ToString());
                DrawDetailRow("Duration:", $"{result.Duration:F2}s");
                DrawDetailRow("Time:", result.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));

                if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Error:", detailsLabelStyle);
                    EditorGUILayout.HelpBox(result.ErrorMessage, MessageType.Error);
                }
            }
        }

        private void RunVisualTest(UITestItem test)
        {
            if (test.VisualTest == null) return;

            SetTestStatus(test, TestStatus.Running);

            // TODO: Integrate with VisualTestRunner when implemented
            Debug.Log($"[VisualTest] Running: {test.DisplayName}");
            Debug.LogWarning("[VisualTest] Test execution not yet implemented. Visual test runner coming soon.");

            // For now, mark as not run since we can't actually run it yet
            SetTestStatus(test, TestStatus.NotRun);
        }

        private void RunVisualTestGroup(UITestItem group)
        {
            var tests = GetAllTestsInGroup(group).Where(t => t.IsVisualTest).ToList();
            foreach (var test in tests)
            {
                RunVisualTest(test);
            }
        }

        // === End Visual Test Support ===

        private void DrawTestDetails(UITestItem test)
        {
            // Header with status icon
            EditorGUILayout.BeginHorizontal();
            var status = GetTestStatus(test);
            var statusIcon = GetStatusIcon(status);
            GUILayout.Label(statusIcon, GUILayout.Width(20), GUILayout.Height(20));
            EditorGUILayout.LabelField(test.DisplayName, headerStyle);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Test metadata
            if (test.Attribute != null)
            {
                DrawDetailRow("Scenario:", test.Attribute.Scenario.ToString());
                if (!string.IsNullOrEmpty(test.Attribute.Feature))
                    DrawDetailRow("Feature:", test.Attribute.Feature);
                if (!string.IsNullOrEmpty(test.Attribute.Story))
                    DrawDetailRow("Story:", test.Attribute.Story);
                DrawDetailRow("Severity:", test.Attribute.Severity.ToString());
                if (!string.IsNullOrEmpty(test.Attribute.Owner))
                    DrawDetailRow("Owner:", test.Attribute.Owner);
                DrawDetailRow("Timeout:", $"{test.Attribute.TimeoutSeconds}s");
                if (test.Attribute.Tags != null && test.Attribute.Tags.Length > 0)
                    DrawDetailRow("Tags:", string.Join(", ", test.Attribute.Tags));
                if (!string.IsNullOrEmpty(test.Attribute.Description))
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Description:", detailsLabelStyle);
                    EditorGUILayout.LabelField(test.Attribute.Description, detailsValueStyle);
                }
            }

            EditorGUILayout.Space();

            // Run buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Run", GUILayout.Height(24)))
            {
                RunTest(test, false, false);
            }
            if (GUILayout.Button("Run (Debug)", GUILayout.Height(24)))
            {
                RunTest(test, true, false);
            }
            if (GUILayout.Button("Run (Clear Data)", GUILayout.Height(24)))
            {
                RunTest(test, false, true);
            }
            EditorGUILayout.EndHorizontal();

            // Test result
            if (testResults.TryGetValue(test.FullName, out var result))
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Last Run", headerStyle);

                DrawDetailRow("Status:", result.Status.ToString());
                DrawDetailRow("Duration:", $"{result.Duration:F2}s");
                DrawDetailRow("Time:", result.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));

                // Logs
                if (!string.IsNullOrEmpty(result.Logs))
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Output:", detailsLabelStyle);

                    var logHeight = Mathf.Min(200, logStyle.CalcHeight(new GUIContent(result.Logs), position.width * (1 - splitterPosition) - 20));
                    EditorGUILayout.SelectableLabel(result.Logs, logStyle, GUILayout.Height(logHeight));
                }

                // Error message
                if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Error:", detailsLabelStyle);
                    EditorGUILayout.HelpBox(result.ErrorMessage, MessageType.Error);
                }

                // Attachments
                if (result.Attachments != null && result.Attachments.Count > 0)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Attachments:", detailsLabelStyle);
                    foreach (var attachment in result.Attachments)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(Path.GetFileName(attachment), GUILayout.ExpandWidth(true));
                        if (GUILayout.Button("Open", GUILayout.Width(50)))
                        {
                            if (File.Exists(attachment))
                            {
                                System.Diagnostics.Process.Start(attachment);
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
        }

        private void DrawDetailRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, detailsLabelStyle);
            EditorGUILayout.LabelField(value, detailsValueStyle);
            EditorGUILayout.EndHorizontal();
        }

#if UITEST_AI
        // Editing state for AI tests
        private bool isEditingAITest;
        private string editPrompt;
        private string editPassCondition;
        private string editFailCondition;
        private string editKnowledge;
        private Vector2 aiTestScrollPos;

        private void DrawCreateAITestButtons()
        {
            EditorGUILayout.Space(20);
            EditorGUILayout.LabelField("Create New AI Test", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("New AI Test", GUILayout.Height(30)))
            {
                AI.Editor.AITestExplorerIntegration.CreateAITest();
                RefreshTests();
            }
            if (GUILayout.Button("New AI Test Group", GUILayout.Height(30)))
            {
                AI.Editor.AITestExplorerIntegration.CreateAITestGroup();
                RefreshTests();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAITestGroupDetails(UITestItem group)
        {
            EditorGUILayout.LabelField(group.DisplayName, headerStyle);

            // Show group knowledge if available
            if (group.AITestGroup != null)
            {
                EditorGUILayout.Space();

                if (!string.IsNullOrEmpty(group.AITestGroup.description))
                {
                    EditorGUILayout.LabelField(group.AITestGroup.description, EditorStyles.wordWrappedLabel);
                }

                EditorGUILayout.Space();

                if (!string.IsNullOrEmpty(group.AITestGroup.knowledge))
                {
                    EditorGUILayout.LabelField("Group Knowledge:", detailsLabelStyle);
                    EditorGUILayout.LabelField(group.AITestGroup.knowledge, EditorStyles.wordWrappedLabel);
                }

                EditorGUILayout.Space();

                // Edit group button
                if (GUILayout.Button("Edit Group", GUILayout.Height(24)))
                {
                    Selection.activeObject = group.AITestGroup;
                    EditorGUIUtility.PingObject(group.AITestGroup);
                }
            }

            EditorGUILayout.Space();

            var children = GetAllTestsInGroup(group);
            EditorGUILayout.LabelField($"Tests: {children.Count}", detailsLabelStyle);

            EditorGUILayout.Space();

            // Run all AI tests in group
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = Application.isPlaying && !AI.AITestRunner.IsRunning;
            if (GUILayout.Button("Run All in Group", GUILayout.Height(30)))
            {
                RunAITestGroup(group).Forget();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode to run AI tests.", MessageType.Info);
            }

            EditorGUILayout.Space();

            // Create new test in this group
            if (group.AITestGroup != null)
            {
                if (GUILayout.Button("Create Test in Group", GUILayout.Height(24)))
                {
                    var folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(group.AITestGroup));
                    AI.Editor.AITestExplorerIntegration.CreateAITest(folder, group.AITestGroup);
                    RefreshTests();
                }
            }
        }

        private void DrawAITestDetails(UITestItem test)
        {
            var aiTest = test.AITest;
            if (aiTest == null)
            {
                EditorGUILayout.HelpBox("AI Test asset not found.", MessageType.Error);
                return;
            }

            // Header
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(test.DisplayName, headerStyle);

            // Open in Inspector button
            if (GUILayout.Button("Inspector", GUILayout.Width(70)))
            {
                Selection.activeObject = aiTest;
                EditorGUIUtility.PingObject(aiTest);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Inline editing toggle
            isEditingAITest = EditorGUILayout.Toggle("Edit Mode", isEditingAITest);

            EditorGUILayout.Space();

            if (isEditingAITest)
            {
                DrawAITestEditor(aiTest);
            }
            else
            {
                DrawAITestViewer(aiTest);
            }

            EditorGUILayout.Space(10);

            // Run controls
            DrawAITestRunControls(test);

            EditorGUILayout.Space(10);

            // Last run results
            DrawAITestLastRun(test);
        }

        private void DrawAITestViewer(AI.AITest aiTest)
        {
            // Prompt
            EditorGUILayout.LabelField("Prompt:", detailsLabelStyle);
            EditorGUILayout.LabelField(aiTest.prompt, EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space();

            // Pass Condition
            EditorGUILayout.LabelField("Pass Condition:", detailsLabelStyle);
            EditorGUILayout.LabelField(aiTest.passCondition, EditorStyles.wordWrappedLabel);

            if (!string.IsNullOrEmpty(aiTest.failCondition))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Fail Condition:", detailsLabelStyle);
                EditorGUILayout.LabelField(aiTest.failCondition, EditorStyles.wordWrappedLabel);
            }

            if (!string.IsNullOrEmpty(aiTest.knowledge))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Knowledge:", detailsLabelStyle);
                EditorGUILayout.LabelField(aiTest.knowledge, EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.Space();

            // Configuration
            EditorGUILayout.LabelField("Configuration:", detailsLabelStyle);
            DrawDetailRow("Starting Tier:", aiTest.startingTier.ToString());
            DrawDetailRow("Max Actions:", aiTest.maxActions.ToString());
            DrawDetailRow("Timeout:", $"{aiTest.timeoutSeconds}s");

            if (aiTest.Group != null)
            {
                DrawDetailRow("Group:", aiTest.Group.displayName ?? aiTest.Group.name);
            }
        }

        private void DrawAITestEditor(AI.AITest aiTest)
        {
            // Initialize edit values if needed
            if (editPrompt == null || Selection.activeObject != aiTest)
            {
                editPrompt = aiTest.prompt;
                editPassCondition = aiTest.passCondition;
                editFailCondition = aiTest.failCondition;
                editKnowledge = aiTest.knowledge;
            }

            EditorGUI.BeginChangeCheck();

            // Prompt
            EditorGUILayout.LabelField("Prompt:", detailsLabelStyle);
            editPrompt = EditorGUILayout.TextArea(editPrompt, GUILayout.Height(60));

            EditorGUILayout.Space();

            // Pass Condition
            EditorGUILayout.LabelField("Pass Condition:", detailsLabelStyle);
            editPassCondition = EditorGUILayout.TextArea(editPassCondition, GUILayout.Height(40));

            EditorGUILayout.Space();

            // Fail Condition
            EditorGUILayout.LabelField("Fail Condition (Optional):", detailsLabelStyle);
            editFailCondition = EditorGUILayout.TextArea(editFailCondition, GUILayout.Height(40));

            EditorGUILayout.Space();

            // Knowledge
            EditorGUILayout.LabelField("Test Knowledge:", detailsLabelStyle);
            editKnowledge = EditorGUILayout.TextArea(editKnowledge, GUILayout.Height(60));

            if (EditorGUI.EndChangeCheck())
            {
                // Mark dirty for save
            }

            EditorGUILayout.Space();

            // Save/Revert buttons
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Save Changes", GUILayout.Height(24)))
            {
                Undo.RecordObject(aiTest, "Edit AI Test");
                aiTest.prompt = editPrompt;
                aiTest.passCondition = editPassCondition;
                aiTest.failCondition = editFailCondition;
                aiTest.knowledge = editKnowledge;
                EditorUtility.SetDirty(aiTest);
                AssetDatabase.SaveAssets();
                isEditingAITest = false;
            }

            if (GUILayout.Button("Revert", GUILayout.Height(24)))
            {
                editPrompt = aiTest.prompt;
                editPassCondition = aiTest.passCondition;
                editFailCondition = aiTest.failCondition;
                editKnowledge = aiTest.knowledge;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawAITestRunControls(UITestItem test)
        {
            EditorGUILayout.LabelField("Run Test", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            GUI.enabled = Application.isPlaying && !AI.AITestRunner.IsRunning;

            if (GUILayout.Button("Run", GUILayout.Height(30)))
            {
                RunAITest(test).Forget();
            }

            if (GUILayout.Button("Run with Debug Panel", GUILayout.Height(30)))
            {
                AI.Editor.AIDebugPanel.ShowWindow();
                RunAITest(test).Forget();
            }

            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            // Cancel button if running
            if (AI.AITestRunner.IsRunning)
            {
                if (GUILayout.Button("Cancel", GUILayout.Height(24)))
                {
                    AI.AITestRunner.Current?.Cancel();
                }
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode to run AI tests.", MessageType.Info);
            }
        }

        private void DrawAITestLastRun(UITestItem test)
        {
            var lastRun = AI.Editor.AITestExplorerIntegration.GetLastRunStatus(test.AITest);

            if (lastRun.Status == AI.Editor.AITestStatus.NotRun)
            {
                EditorGUILayout.HelpBox("This test has not been run yet.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Last Run", EditorStyles.boldLabel);

            var statusColor = lastRun.Status switch
            {
                AI.Editor.AITestStatus.Passed => new Color(0.2f, 0.8f, 0.2f),
                AI.Editor.AITestStatus.Failed => new Color(0.8f, 0.2f, 0.2f),
                AI.Editor.AITestStatus.Error => new Color(0.8f, 0.4f, 0.2f),
                _ => Color.gray
            };

            GUI.color = statusColor;
            EditorGUILayout.LabelField($"Status: {lastRun.Status}", EditorStyles.boldLabel);
            GUI.color = Color.white;

            DrawDetailRow("Duration:", $"{lastRun.Duration:F1}s");
            DrawDetailRow("Time:", lastRun.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("View Details", GUILayout.Height(24)))
            {
                AI.Editor.AIResultsWindow.ShowWindow();
            }
            if (GUILayout.Button("View Screenshots", GUILayout.Height(24)))
            {
                ShowScreenshotTimeline(lastRun.RunId);
            }
            EditorGUILayout.EndHorizontal();
        }

        private async UniTask RunAITest(UITestItem test)
        {
            SetTestStatus(test, TestStatus.Running);

            try
            {
                var result = await AI.Editor.AITestExplorerIntegration.RunAITestAsync(test.AITest);

                // Check if we're still valid after await
                if (!Application.isPlaying)
                    return;

                if (result != null)
                {
                    var status = result.IsSuccess ? TestStatus.Passed : TestStatus.Failed;
                    UpdateTestResult(test.DisplayName, status, result.Message, result.DurationSeconds);
                }
                else
                {
                    SetTestStatus(test, TestStatus.NotRun); // Cancelled/failed to run
                }
            }
            catch (OperationCanceledException)
            {
                SetTestStatus(test, TestStatus.NotRun);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AITest] RunAITest exception: {ex}");
                SetTestStatus(test, TestStatus.Failed);
            }

            if (this != null)
            {
                Repaint();
            }
        }

        private async UniTaskVoid RunAITestGroup(UITestItem group)
        {
            try
            {
                var tests = GetAllTestsInGroup(group).Where(t => t.IsAITest).ToList();

                foreach (var test in tests)
                {
                    if (!Application.isPlaying)
                        break;

                    await RunAITest(test);
                }
            }
            catch (OperationCanceledException)
            {
                // Group run was cancelled
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AITest] RunAITestGroup exception: {ex}");
            }
        }

        private void ShowScreenshotTimeline(string runId)
        {
            if (string.IsNullOrEmpty(runId))
                return;

            var run = AI.AITestResultStore.Instance.LoadRun(runId);
            if (run != null)
            {
                AI.Editor.ScreenshotTimelineView.ShowWindow(run);
            }
        }
#endif

        private void RefreshTests()
        {
            allTests.Clear();

            // 1. Find traditional UITestBehaviour tests
            var testInfos = FindAllUITestBehaviours();

            // Group by namespace/assembly
            var groupedByNamespace = testInfos
                .GroupBy(t => t.TestType.Namespace ?? "(No Namespace)")
                .OrderBy(g => g.Key);

            foreach (var namespaceGroup in groupedByNamespace)
            {
                var namespaceItem = new UITestItem
                {
                    DisplayName = namespaceGroup.Key,
                    FullName = namespaceGroup.Key,
                    IsGroup = true,
                    Children = new List<UITestItem>()
                };

                // Group by assembly within namespace
                var groupedByAssembly = namespaceGroup
                    .GroupBy(t => t.TestType.Assembly.GetName().Name)
                    .OrderBy(g => g.Key);

                foreach (var assemblyGroup in groupedByAssembly)
                {
                    UITestItem parentItem;

                    // If there's only one assembly, don't create an extra level
                    if (groupedByAssembly.Count() == 1)
                    {
                        parentItem = namespaceItem;
                    }
                    else
                    {
                        var assemblyItem = new UITestItem
                        {
                            DisplayName = assemblyGroup.Key,
                            FullName = $"{namespaceGroup.Key}.{assemblyGroup.Key}",
                            IsGroup = true,
                            Children = new List<UITestItem>()
                        };
                        namespaceItem.Children.Add(assemblyItem);
                        parentItem = assemblyItem;
                    }

                    foreach (var test in assemblyGroup.OrderBy(t => t.Scenario))
                    {
                        var attr = test.TestType.GetCustomAttribute<UITestAttribute>();
                        parentItem.Children.Add(new UITestItem
                        {
                            DisplayName = test.Name,
                            FullName = test.TestType.FullName,
                            TestType = test.TestType,
                            Attribute = attr,
                            IsGroup = false
                        });
                    }
                }

                allTests.Add(namespaceItem);
            }

#if UITEST_AI
            // 2. Find AI Tests and add them
            AddAITests();
#endif

            // 3. Find Visual Tests and add them
            AddVisualTests();

            // Rebuild tree view
            treeView = new UITestTreeView(treeViewState, allTests, testResults, this);
            ApplyFilters();
        }

        private void AddVisualTests()
        {
            var visualTestGuids = AssetDatabase.FindAssets("t:VisualTest");
            if (visualTestGuids.Length == 0)
                return;

            // Group visual tests by folder
            var testsByFolder = new Dictionary<string, List<(VisualTest test, string path)>>();

            foreach (var guid in visualTestGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var test = AssetDatabase.LoadAssetAtPath<VisualTest>(path);
                if (test == null) continue;

                var folder = Path.GetDirectoryName(path)?.Replace("\\", "/") ?? "Assets";
                var folderName = Path.GetFileName(folder);
                if (string.IsNullOrEmpty(folderName)) folderName = folder;

                if (!testsByFolder.ContainsKey(folderName))
                {
                    testsByFolder[folderName] = new List<(VisualTest, string)>();
                }
                testsByFolder[folderName].Add((test, path));
            }

            // Create Visual Tests root
            var visualTestsRoot = new UITestItem
            {
                DisplayName = "Visual Tests",
                FullName = "Visual Tests",
                IsGroup = true,
                IsVisualTestGroup = true,
                Children = new List<UITestItem>()
            };

            foreach (var kvp in testsByFolder.OrderBy(x => x.Key))
            {
                UITestItem parentItem;

                // If there's only one folder, add tests directly under root
                if (testsByFolder.Count == 1)
                {
                    parentItem = visualTestsRoot;
                }
                else
                {
                    var folderItem = new UITestItem
                    {
                        DisplayName = kvp.Key,
                        FullName = $"Visual Tests/{kvp.Key}",
                        IsGroup = true,
                        IsVisualTestGroup = true,
                        Children = new List<UITestItem>()
                    };
                    visualTestsRoot.Children.Add(folderItem);
                    parentItem = folderItem;
                }

                foreach (var (test, path) in kvp.Value.OrderBy(x => x.test.testName ?? x.test.name))
                {
                    parentItem.Children.Add(new UITestItem
                    {
                        DisplayName = test.testName ?? test.name,
                        FullName = path,
                        IsGroup = false,
                        IsVisualTest = true,
                        VisualTest = test,
                        VisualTestAssetPath = path
                    });
                }
            }

            // Only add if there are tests
            if (visualTestsRoot.Children.Count > 0 ||
                (testsByFolder.Count == 1 && testsByFolder.Values.First().Count > 0))
            {
                allTests.Add(visualTestsRoot);
            }
        }

#if UITEST_AI
        private void AddAITests()
        {
            var aiTests = AI.Editor.AITestExplorerIntegration.FindAllAITests();
            if (aiTests.Count == 0)
                return;

            // Group AI tests by their group
            var grouped = aiTests.GroupBy(t => t.GroupName ?? "(Ungrouped AI Tests)");

            // Create AI Tests root
            var aiTestsRoot = new UITestItem
            {
                DisplayName = "AI Tests",
                FullName = "AI Tests",
                IsGroup = true,
                IsAITestGroup = true,
                Children = new List<UITestItem>()
            };

            foreach (var group in grouped.OrderBy(g => g.Key))
            {
                var groupItem = new UITestItem
                {
                    DisplayName = group.Key,
                    FullName = $"AI Tests/{group.Key}",
                    IsGroup = true,
                    IsAITestGroup = true,
                    Children = new List<UITestItem>()
                };

                // Try to find the actual AITestGroup asset
                var firstTest = group.FirstOrDefault()?.AITest;
                if (firstTest?.Group != null)
                {
                    groupItem.AITestGroup = firstTest.Group;
                }

                foreach (var test in group.OrderBy(t => t.DisplayName))
                {
                    groupItem.Children.Add(new UITestItem
                    {
                        DisplayName = test.DisplayName,
                        FullName = test.FullName,
                        IsGroup = false,
                        IsAITest = true,
                        AITest = test.AITest,
                        AITestAssetPath = test.AssetPath
                    });
                }

                aiTestsRoot.Children.Add(groupItem);
            }

            allTests.Add(aiTestsRoot);
        }
#endif

        private void ApplyFilters()
        {
            if (treeView != null)
            {
                treeView.ApplyFilter(searchString, statusFilter, severityFilter);
            }
        }

        private void RunAllTests()
        {
            UITestRunner.RunUITestsFromCommandLine();
        }

        private void RunSelectedTest()
        {
            if (selectedTest != null)
            {
                if (selectedTest.IsGroup)
                {
                    RunTestGroup(selectedTest);
                }
                else
                {
                    RunTest(selectedTest, false, false);
                }
            }
        }

        private void RunTestGroup(UITestItem group)
        {
            var tests = GetAllTestsInGroup(group);
            if (tests.Count > 0)
            {
                // Queue all tests in the group
                foreach (var test in tests)
                {
                    SetTestStatus(test, TestStatus.Running);
                }

                // Run first test - the runner will handle the queue
                RunTest(tests[0], false, false);
            }
        }

        private void RunTest(UITestItem test, bool debug, bool clearData)
        {
            if (test.TestType == null) return;

            var attr = test.Attribute;
            if (attr == null || attr.Scenario <= 0) return;

            SetTestStatus(test, TestStatus.Running);

            // Set up session state for the test
            SessionState.SetBool("GAME_LOOP_TEST", true);
            SessionState.SetInt("GAME_LOOP_TEST_SCENARIO", attr.Scenario);
            SessionState.SetString("GAME_LOOP_TEST_TYPE", test.TestType.AssemblyQualifiedName);
            SessionState.SetString("GAME_LOOP_TEST_NAME", test.DisplayName);
            SessionState.SetBool("GAME_LOOP_TEST_DEBUG", debug);

            if (clearData)
            {
                SessionState.SetString("GAME_LOOP_TEST_DATA_PATH", "");
            }

            // Enter play mode to run the test
            EditorApplication.EnterPlaymode();
        }

        private List<UITestItem> GetAllTestsInGroup(UITestItem group)
        {
            var tests = new List<UITestItem>();
            CollectTests(group, tests);
            return tests;
        }

        private void CollectTests(UITestItem item, List<UITestItem> tests)
        {
            if (!item.IsGroup)
            {
                tests.Add(item);
            }
            else if (item.Children != null)
            {
                foreach (var child in item.Children)
                {
                    CollectTests(child, tests);
                }
            }
        }

        private TestStatus GetTestStatus(UITestItem test)
        {
            if (testResults.TryGetValue(test.FullName, out var result))
            {
                return result.Status;
            }
            return TestStatus.NotRun;
        }

        private void SetTestStatus(UITestItem test, TestStatus status)
        {
            if (!testResults.TryGetValue(test.FullName, out var result))
            {
                result = new TestResult { TestName = test.FullName };
                testResults[test.FullName] = result;
            }
            result.Status = status;
            result.Timestamp = DateTime.Now;

            treeView?.Reload();
            Repaint();
        }

        private void ClearResults()
        {
            testResults.Clear();
            SavePersistedResults();
            treeView?.Reload();
            Repaint();
        }

        private GUIContent GetStatusIcon(TestStatus status)
        {
            switch (status)
            {
                case TestStatus.Passed:
                    return EditorGUIUtility.IconContent("TestPassed");
                case TestStatus.Failed:
                    return EditorGUIUtility.IconContent("TestFailed");
                case TestStatus.Running:
                    return EditorGUIUtility.IconContent("TestInconclusive");
                default:
                    return EditorGUIUtility.IconContent("TestNormal");
            }
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                // Test completed - refresh results
                EditorApplication.delayCall += () =>
                {
                    RefreshTestResults();
                    Repaint();
                };
            }
        }

        private void OnLogMessage(string logString, string stackTrace, LogType type)
        {
            // Parse test result logs with duration
            // Format: "[UITEST] Test PASSED: ClassName Duration: 1.23s"
            if (logString.StartsWith("[UITEST] Test PASSED:"))
            {
                ParseTestResult(logString, "[UITEST] Test PASSED:", TestStatus.Passed, null);
            }
            else if (logString.StartsWith("[UITEST] Test FAILED:"))
            {
                ParseTestResult(logString, "[UITEST] Test FAILED:", TestStatus.Failed, stackTrace);
            }
            else if (logString.StartsWith("[UITEST] Test CANCELLED:"))
            {
                ParseTestResult(logString, "[UITEST] Test CANCELLED:", TestStatus.Cancelled, null);
            }
        }

        private void ParseTestResult(string logString, string prefix, TestStatus status, string error)
        {
            var content = logString.Substring(prefix.Length).Trim();
            string testName;
            float duration = 0f;

            // Parse duration if present: "ClassName Duration: 1.23s"
            var durationIndex = content.IndexOf(" Duration:");
            if (durationIndex > 0)
            {
                testName = content.Substring(0, durationIndex).Trim();
                var durationStr = content.Substring(durationIndex + " Duration:".Length).Trim().TrimEnd('s');
                float.TryParse(durationStr, out duration);
            }
            else
            {
                testName = content;
            }

            UpdateTestResult(testName, status, error, duration);
        }

        private void UpdateTestResult(string testClassName, TestStatus status, string error, float duration = 0f)
        {
            // Find test by class name
            var test = FindTestByClassName(testClassName);
            if (test != null)
            {
                if (!testResults.TryGetValue(test.FullName, out var result))
                {
                    result = new TestResult { TestName = test.FullName };
                    testResults[test.FullName] = result;
                }
                result.Status = status;
                result.ErrorMessage = error;
                result.Duration = duration;
                result.Timestamp = DateTime.Now;

                SavePersistedResults();
                treeView?.Reload();
                Repaint();
            }
        }

        private UITestItem FindTestByClassName(string className)
        {
            foreach (var item in allTests)
            {
                var found = FindTestInItem(item, className);
                if (found != null) return found;
            }
            return null;
        }

        private UITestItem FindTestInItem(UITestItem item, string className)
        {
            if (!item.IsGroup && item.TestType?.Name == className)
            {
                return item;
            }

            if (item.Children != null)
            {
                foreach (var child in item.Children)
                {
                    var found = FindTestInItem(child, className);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private void RefreshTestResults()
        {
            // Results are updated via log messages
            treeView?.Reload();
        }

        private void LoadPersistedResults()
        {
            var json = EditorPrefs.GetString(PREFS_RESULTS, "{}");
            try
            {
                var data = JsonUtility.FromJson<TestResultsData>(json);
                if (data?.Results != null)
                {
                    testResults = data.Results.ToDictionary(r => r.TestName, r => r);
                }
            }
            catch
            {
                testResults = new Dictionary<string, TestResult>();
            }
        }

        private void SavePersistedResults()
        {
            var data = new TestResultsData { Results = testResults.Values.ToList() };
            var json = JsonUtility.ToJson(data);
            EditorPrefs.SetString(PREFS_RESULTS, json);
        }

        private List<TestInfo> FindAllUITestBehaviours()
        {
            var testInfos = new List<TestInfo>();

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes();
                    foreach (var type in types)
                    {
                        if (type.IsAbstract || !type.IsSubclassOf(typeof(UITestBehaviour)))
                            continue;

                        var attr = type.GetCustomAttribute<UITestAttribute>();
                        if (attr != null && attr.Scenario > 0)
                        {
                            testInfos.Add(new TestInfo
                            {
                                Name = type.Name,
                                TestType = type,
                                Scenario = attr.Scenario
                            });
                        }
                    }
                }
                catch (Exception)
                {
                    // Skip assemblies that can't be processed
                }
            }

            return testInfos.OrderBy(t => t.Scenario).ToList();
        }

        private class TestInfo
        {
            public string Name { get; set; }
            public Type TestType { get; set; }
            public int Scenario { get; set; }
        }
    }

    public enum TestStatus
    {
        NotRun,
        Running,
        Passed,
        Failed,
        Cancelled
    }

    public enum TestStatusFilter
    {
        All,
        Passed,
        Failed,
        NotRun
    }

    public class UITestItem
    {
        public string DisplayName { get; set; }
        public string FullName { get; set; }
        public Type TestType { get; set; }
        public UITestAttribute Attribute { get; set; }
        public bool IsGroup { get; set; }
        public List<UITestItem> Children { get; set; }

        // AI Test support
        public bool IsAITest { get; set; }
        public bool IsAITestGroup { get; set; }
#if UITEST_AI
        public ODDGames.UITest.AI.AITest AITest { get; set; }
        public ODDGames.UITest.AI.AITestGroup AITestGroup { get; set; }
        public string AITestAssetPath { get; set; }
#endif

        // Visual Test support
        public bool IsVisualTest { get; set; }
        public bool IsVisualTestGroup { get; set; }
        public VisualTest VisualTest { get; set; }
        public string VisualTestAssetPath { get; set; }
    }

    [Serializable]
    public class TestResult
    {
        public string TestName;
        public TestStatus Status;
        public float Duration;
        public string Logs;
        public string ErrorMessage;
        public List<string> Attachments = new List<string>();
        public DateTime Timestamp;
    }

    [Serializable]
    public class TestResultsData
    {
        public List<TestResult> Results = new List<TestResult>();
    }

    public class UITestTreeView : TreeView
    {
        private List<UITestItem> allItems;
        private List<UITestItem> filteredItems;
        private Dictionary<string, TestResult> testResults;
        private UITestExplorerWindow window;
        private Dictionary<int, UITestItem> idToItem = new Dictionary<int, UITestItem>();

        public UITestTreeView(TreeViewState state, List<UITestItem> items, Dictionary<string, TestResult> results, UITestExplorerWindow window)
            : base(state)
        {
            this.allItems = items;
            this.filteredItems = items;
            this.testResults = results;
            this.window = window;
            showAlternatingRowBackgrounds = true;
            showBorder = true;
            Reload();
        }

        public void ApplyFilter(string search, TestStatusFilter statusFilter, TestSeverity? severityFilter)
        {
            if (string.IsNullOrEmpty(search) && statusFilter == TestStatusFilter.All && !severityFilter.HasValue)
            {
                filteredItems = allItems;
            }
            else
            {
                filteredItems = FilterItems(allItems, search?.ToLower(), statusFilter, severityFilter);
            }
            Reload();
        }

        private List<UITestItem> FilterItems(List<UITestItem> items, string search, TestStatusFilter statusFilter, TestSeverity? severityFilter)
        {
            var result = new List<UITestItem>();

            foreach (var item in items)
            {
                if (item.IsGroup)
                {
                    var filteredChildren = FilterItems(item.Children, search, statusFilter, severityFilter);
                    if (filteredChildren.Count > 0)
                    {
                        result.Add(new UITestItem
                        {
                            DisplayName = item.DisplayName,
                            FullName = item.FullName,
                            IsGroup = true,
                            Children = filteredChildren
                        });
                    }
                }
                else
                {
                    var matchesSearch = string.IsNullOrEmpty(search) ||
                                       item.DisplayName.ToLower().Contains(search) ||
                                       item.FullName.ToLower().Contains(search);

                    var matchesStatus = statusFilter == TestStatusFilter.All || MatchesStatusFilter(item, statusFilter);

                    var matchesSeverity = !severityFilter.HasValue ||
                                         (item.Attribute != null && item.Attribute.Severity == severityFilter.Value);

                    if (matchesSearch && matchesStatus && matchesSeverity)
                    {
                        result.Add(item);
                    }
                }
            }

            return result;
        }

        private bool MatchesStatusFilter(UITestItem item, TestStatusFilter filter)
        {
            if (!testResults.TryGetValue(item.FullName, out var result))
            {
                return filter == TestStatusFilter.NotRun;
            }

            return filter switch
            {
                TestStatusFilter.Passed => result.Status == TestStatus.Passed,
                TestStatusFilter.Failed => result.Status == TestStatus.Failed,
                TestStatusFilter.NotRun => result.Status == TestStatus.NotRun,
                _ => true
            };
        }

        protected override TreeViewItem BuildRoot()
        {
            idToItem.Clear();
            var root = new TreeViewItem(0, -1, "Root");
            int id = 1;

            foreach (var item in filteredItems)
            {
                var treeItem = BuildTreeItem(item, ref id, 0);
                root.AddChild(treeItem);
            }

            if (!root.hasChildren)
            {
                root.AddChild(new TreeViewItem(id++, 0, "No tests found"));
            }

            SetupDepthsFromParentsAndChildren(root);
            return root;
        }

        private TreeViewItem BuildTreeItem(UITestItem item, ref int id, int depth)
        {
            var treeItem = new UITestTreeViewItem(id, depth, item.DisplayName, item);
            idToItem[id] = item;
            id++;

            if (item.Children != null)
            {
                foreach (var child in item.Children)
                {
                    var childItem = BuildTreeItem(child, ref id, depth + 1);
                    treeItem.AddChild(childItem);
                }
            }

            return treeItem;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var item = args.item as UITestTreeViewItem;
            if (item == null)
            {
                base.RowGUI(args);
                return;
            }

            var testItem = item.TestItem;
            var rect = args.rowRect;

            // Draw status icon
            var iconRect = new Rect(rect.x + GetContentIndent(item), rect.y, 16, rect.height);
            var statusIcon = GetStatusIcon(testItem);
            if (statusIcon != null)
            {
                GUI.Label(iconRect, statusIcon);
            }

            // Draw label
            var labelRect = new Rect(iconRect.xMax + 2, rect.y, rect.width - iconRect.xMax - 60, rect.height);
            GUI.Label(labelRect, item.displayName);

            // Draw run button for tests (or Edit for Visual Tests)
            if (!testItem.IsGroup)
            {
                var buttonRect = new Rect(rect.xMax - 50, rect.y + 2, 45, rect.height - 4);

                if (testItem.IsVisualTest)
                {
                    if (GUI.Button(buttonRect, "Edit"))
                    {
                        var builderWindow = TestBuilder.ShowWindow();
                        builderWindow.LoadTest(testItem.VisualTest);
                    }
                }
                else
                {
                    if (GUI.Button(buttonRect, "Run"))
                    {
                        window.GetType().GetMethod("RunTest", BindingFlags.NonPublic | BindingFlags.Instance)
                            ?.Invoke(window, new object[] { testItem, false, false });
                    }
                }
            }
        }

        protected override void ContextClickedItem(int id)
        {
            if (!idToItem.TryGetValue(id, out var item))
                return;

            var menu = new GenericMenu();

            if (item.IsVisualTest)
            {
                menu.AddItem(new GUIContent("Open in Visual Builder"), false, () =>
                {
                    var builderWindow = TestBuilder.ShowWindow();
                    builderWindow.LoadTest(item.VisualTest);
                });

                menu.AddItem(new GUIContent("Show in Inspector"), false, () =>
                {
                    Selection.activeObject = item.VisualTest;
                    EditorGUIUtility.PingObject(item.VisualTest);
                });

                menu.AddSeparator("");

                menu.AddItem(new GUIContent("Run Test"), false, () =>
                {
                    window.GetType().GetMethod("RunVisualTest", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.Invoke(window, new object[] { item });
                });

                menu.AddSeparator("");

                menu.AddItem(new GUIContent("Duplicate"), false, () =>
                {
                    DuplicateVisualTest(item);
                });

                menu.AddItem(new GUIContent("Delete"), false, () =>
                {
                    if (EditorUtility.DisplayDialog("Delete Visual Test",
                        $"Are you sure you want to delete '{item.DisplayName}'?", "Delete", "Cancel"))
                    {
                        AssetDatabase.DeleteAsset(item.VisualTestAssetPath);
                        window.GetType().GetMethod("RefreshTests", BindingFlags.NonPublic | BindingFlags.Instance)
                            ?.Invoke(window, null);
                    }
                });
            }
            else if (item.IsVisualTestGroup)
            {
                menu.AddItem(new GUIContent("Create New Visual Test"), false, () =>
                {
                    window.GetType().GetMethod("CreateVisualTest", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.Invoke(window, null);
                });

                menu.AddItem(new GUIContent("Open Visual Builder"), false, () =>
                {
                    TestBuilder.ShowWindow();
                });
            }
            else if (!item.IsGroup)
            {
                // Regular test context menu
                menu.AddItem(new GUIContent("Run"), false, () =>
                {
                    window.GetType().GetMethod("RunTest", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.Invoke(window, new object[] { item, false, false });
                });

                menu.AddItem(new GUIContent("Run (Debug)"), false, () =>
                {
                    window.GetType().GetMethod("RunTest", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.Invoke(window, new object[] { item, true, false });
                });
            }

            menu.ShowAsContext();
        }

        private void DuplicateVisualTest(UITestItem item)
        {
            if (item.VisualTest == null || string.IsNullOrEmpty(item.VisualTestAssetPath))
                return;

            var sourcePath = item.VisualTestAssetPath;
            var directory = Path.GetDirectoryName(sourcePath);
            var fileName = Path.GetFileNameWithoutExtension(sourcePath);
            var extension = Path.GetExtension(sourcePath);

            var newPath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(directory, $"{fileName}_Copy{extension}"));

            var duplicate = ScriptableObject.Instantiate(item.VisualTest);
            duplicate.testName = item.VisualTest.testName + " (Copy)";

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

            window.GetType().GetMethod("RefreshTests", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(window, null);
        }

        private GUIContent GetStatusIcon(UITestItem item)
        {
            if (item.IsGroup)
            {
                // Calculate group status from children
                var hasFailure = HasFailureInGroup(item);
                var allPassed = AllPassedInGroup(item);

                if (hasFailure)
                    return EditorGUIUtility.IconContent("TestFailed");
                if (allPassed)
                    return EditorGUIUtility.IconContent("TestPassed");
                return EditorGUIUtility.IconContent("TestNormal");
            }

            if (testResults.TryGetValue(item.FullName, out var result))
            {
                return result.Status switch
                {
                    TestStatus.Passed => EditorGUIUtility.IconContent("TestPassed"),
                    TestStatus.Failed => EditorGUIUtility.IconContent("TestFailed"),
                    TestStatus.Running => EditorGUIUtility.IconContent("TestInconclusive"),
                    TestStatus.Cancelled => EditorGUIUtility.IconContent("TestStopwatch"),
                    _ => EditorGUIUtility.IconContent("TestNormal")
                };
            }

            return EditorGUIUtility.IconContent("TestNormal");
        }

        private bool HasFailureInGroup(UITestItem group)
        {
            if (!group.IsGroup)
            {
                return testResults.TryGetValue(group.FullName, out var r) && r.Status == TestStatus.Failed;
            }

            return group.Children?.Any(HasFailureInGroup) ?? false;
        }

        private bool AllPassedInGroup(UITestItem group)
        {
            if (!group.IsGroup)
            {
                return testResults.TryGetValue(group.FullName, out var r) && r.Status == TestStatus.Passed;
            }

            return group.Children?.All(AllPassedInGroup) ?? false;
        }

        public UITestItem FindItem(int id)
        {
            idToItem.TryGetValue(id, out var item);
            return item;
        }

        protected override void DoubleClickedItem(int id)
        {
            if (idToItem.TryGetValue(id, out var item) && !item.IsGroup)
            {
                if (item.IsVisualTest)
                {
                    // Open Visual Test in builder on double-click
                    var builderWindow = TestBuilder.ShowWindow();
                    builderWindow.LoadTest(item.VisualTest);
                }
                else
                {
                    // Run test on double-click
                    window.GetType().GetMethod("RunTest", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.Invoke(window, new object[] { item, false, false });
                }
            }
        }
    }

    public class UITestTreeViewItem : TreeViewItem
    {
        public UITestItem TestItem { get; }

        public UITestTreeViewItem(int id, int depth, string displayName, UITestItem testItem)
            : base(id, depth, displayName)
        {
            TestItem = testItem;
        }
    }
}
