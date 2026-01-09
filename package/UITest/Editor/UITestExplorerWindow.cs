using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using ODDGames.UITest;

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

            GUILayout.Space(10);

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
            }
            else if (selectedTest.IsGroup)
            {
                DrawGroupDetails(selectedTest);
            }
            else
            {
                DrawTestDetails(selectedTest);
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

        private void RefreshTests()
        {
            allTests.Clear();

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

            // Rebuild tree view
            treeView = new UITestTreeView(treeViewState, allTests, testResults, this);
            ApplyFilters();
        }

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

            // Draw run button for tests
            if (!testItem.IsGroup)
            {
                var buttonRect = new Rect(rect.xMax - 50, rect.y + 2, 45, rect.height - 4);
                if (GUI.Button(buttonRect, "Run"))
                {
                    window.GetType().GetMethod("RunTest", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.Invoke(window, new object[] { testItem, false, false });
                }
            }
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
                // Run test on double-click
                window.GetType().GetMethod("RunTest", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(window, new object[] { item, false, false });
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
