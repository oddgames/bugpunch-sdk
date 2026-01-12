#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace ODDGames.UITest.AI.Editor
{
    /// <summary>
    /// Window for browsing and filtering AI test results.
    /// </summary>
    public class AIResultsWindow : EditorWindow
    {
        private AITestResultStore store;
        private List<RunSummary> displayedRuns = new List<RunSummary>();
        private Vector2 scrollPosition;

        // Filters
        private TestStatus? statusFilter;
        private string searchText = "";
        private string selectedGroup = "";
        private SortBy sortBy = SortBy.Newest;

        // Selected run
        private string selectedRunId;
        private AITestRun selectedRun;

        // View state
        private bool showStatistics = true;
        private bool showFilters = true;

        [MenuItem("Window/Analysis/UI Automation/AI Test Results")]
        public static void ShowWindow()
        {
            var window = GetWindow<AIResultsWindow>("AI Test Results");
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        private void OnEnable()
        {
            store = AITestResultStore.Instance;
            RefreshResults();
        }

        private void RefreshResults()
        {
            var query = new ResultQuery
            {
                Status = statusFilter,
                TestName = string.IsNullOrEmpty(searchText) ? null : searchText,
                GroupName = string.IsNullOrEmpty(selectedGroup) ? null : selectedGroup,
                SortBy = sortBy,
                Limit = 200
            };

            displayedRuns = store.QueryRuns(query).ToList();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (showStatistics)
            {
                DrawStatisticsSummary();
            }

            EditorGUILayout.BeginHorizontal();

            // Left panel - Results list
            EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.5f));
            DrawResultsList();
            EditorGUILayout.EndVertical();

            // Right panel - Run details
            EditorGUILayout.BeginVertical();
            DrawRunDetails();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Status filter tabs
            DrawStatusTab(null, "All");
            DrawStatusTab(TestStatus.Passed, "Passed");
            DrawStatusTab(TestStatus.Failed, "Failed");
            DrawStatusTab(TestStatus.Error, "Errors");

            GUILayout.FlexibleSpace();

            // Sort dropdown
            EditorGUI.BeginChangeCheck();
            sortBy = (SortBy)EditorGUILayout.EnumPopup(sortBy, EditorStyles.toolbarPopup, GUILayout.Width(80));
            if (EditorGUI.EndChangeCheck())
            {
                RefreshResults();
            }

            // Search
            EditorGUI.BeginChangeCheck();
            searchText = EditorGUILayout.TextField(searchText, EditorStyles.toolbarSearchField, GUILayout.Width(150));
            if (EditorGUI.EndChangeCheck())
            {
                RefreshResults();
            }

            // Refresh button
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
            {
                RefreshResults();
            }

            // Toggle statistics
            showStatistics = GUILayout.Toggle(showStatistics, "Stats", EditorStyles.toolbarButton);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatusTab(TestStatus? status, string label)
        {
            var isSelected = statusFilter == status;
            EditorGUI.BeginChangeCheck();
            GUILayout.Toggle(isSelected, label, EditorStyles.toolbarButton, GUILayout.Width(60));
            if (EditorGUI.EndChangeCheck())
            {
                statusFilter = isSelected ? null : status;
                RefreshResults();
            }
        }

        private void DrawStatisticsSummary()
        {
            var stats = store.GetStatistics(string.IsNullOrEmpty(selectedGroup) ? null : selectedGroup);

            EditorGUILayout.BeginHorizontal("box");

            DrawStatBox("Total", stats.TotalRuns.ToString(), Color.white);
            DrawStatBox("Passed", stats.Passed.ToString(), new Color(0.2f, 0.8f, 0.2f));
            DrawStatBox("Failed", stats.Failed.ToString(), new Color(0.8f, 0.2f, 0.2f));
            DrawStatBox("Pass Rate", $"{stats.PassRate:F1}%", stats.PassRate >= 80 ? Color.green : Color.yellow);
            DrawStatBox("Avg Duration", $"{stats.AverageDuration:F1}s", Color.cyan);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatBox(string label, string value, Color color)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(80));
            EditorGUILayout.LabelField(label, EditorStyles.centeredGreyMiniLabel);

            var oldColor = GUI.color;
            GUI.color = color;
            EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            GUI.color = oldColor;

            EditorGUILayout.EndVertical();
        }

        private void DrawResultsList()
        {
            EditorGUILayout.LabelField($"Results ({displayedRuns.Count})", EditorStyles.boldLabel);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            foreach (var run in displayedRuns)
            {
                DrawRunRow(run);
            }

            if (displayedRuns.Count == 0)
            {
                EditorGUILayout.HelpBox("No results found. Run some AI tests to see results here.", MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawRunRow(RunSummary run)
        {
            var isSelected = selectedRunId == run.id;
            var bgColor = isSelected ? new Color(0.24f, 0.48f, 0.9f, 0.5f) : Color.clear;

            var rect = EditorGUILayout.BeginHorizontal();
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, bgColor);
            }

            // Status icon
            var statusColor = run.status switch
            {
                TestStatus.Passed => new Color(0.2f, 0.8f, 0.2f),
                TestStatus.Failed => new Color(0.8f, 0.2f, 0.2f),
                TestStatus.Error => new Color(0.8f, 0.4f, 0.2f),
                TestStatus.TimedOut => new Color(0.8f, 0.8f, 0.2f),
                _ => Color.gray
            };

            var statusIcon = run.status switch
            {
                TestStatus.Passed => "✓",
                TestStatus.Failed => "✗",
                TestStatus.Error => "⚠",
                TestStatus.TimedOut => "⏱",
                _ => "○"
            };

            GUI.color = statusColor;
            EditorGUILayout.LabelField(statusIcon, GUILayout.Width(20));
            GUI.color = Color.white;

            // Test name
            EditorGUILayout.LabelField(run.testName ?? "Unknown", GUILayout.Width(150));

            // Duration
            EditorGUILayout.LabelField($"{run.duration:F1}s", GUILayout.Width(50));

            // Timestamp
            EditorGUILayout.LabelField(run.Timestamp.ToString("MM/dd HH:mm"), GUILayout.Width(90));

            EditorGUILayout.EndHorizontal();

            // Handle click
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                selectedRunId = run.id;
                selectedRun = store.LoadRun(run.id);
                Repaint();
            }
        }

        private void DrawRunDetails()
        {
            EditorGUILayout.LabelField("Run Details", EditorStyles.boldLabel);

            if (selectedRun == null)
            {
                EditorGUILayout.HelpBox("Select a run to view details.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginVertical("box");

            // Header
            EditorGUILayout.LabelField(selectedRun.testName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Status: {selectedRun.status}");
            EditorGUILayout.LabelField($"Duration: {selectedRun.durationSeconds:F1}s");
            EditorGUILayout.LabelField($"Actions: {selectedRun.actionsExecuted}");
            EditorGUILayout.LabelField($"Model: {selectedRun.startingModelTier} → {selectedRun.finalModelTier}");

            if (selectedRun.modelEscalations > 0)
            {
                EditorGUILayout.LabelField($"Escalations: {selectedRun.modelEscalations}");
            }

            if (!string.IsNullOrEmpty(selectedRun.failureReason))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Failure Reason:", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(selectedRun.failureReason, EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.Space(10);

            // Actions
            EditorGUILayout.LabelField($"Actions ({selectedRun.actions.Count})", EditorStyles.boldLabel);

            if (selectedRun.actions.Count > 0)
            {
                EditorGUI.indentLevel++;
                foreach (var action in selectedRun.actions.Take(20))
                {
                    var icon = action.success ? "✓" : "✗";
                    var color = action.success ? Color.green : Color.red;

                    EditorGUILayout.BeginHorizontal();
                    GUI.color = color;
                    EditorGUILayout.LabelField(icon, GUILayout.Width(15));
                    GUI.color = Color.white;
                    EditorGUILayout.LabelField($"{action.actionType}({action.target})");
                    EditorGUILayout.EndHorizontal();
                }

                if (selectedRun.actions.Count > 20)
                {
                    EditorGUILayout.LabelField($"... and {selectedRun.actions.Count - 20} more");
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(10);

            // Screenshots
            if (selectedRun.screenshots.Count > 0)
            {
                EditorGUILayout.LabelField($"Screenshots ({selectedRun.screenshots.Count})", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("View Timeline", GUILayout.Width(100)))
                {
                    AIScreenshotTimeline.ShowWindow(selectedRun);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(10);

            // Actions
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Delete", GUILayout.Width(60)))
            {
                if (EditorUtility.DisplayDialog("Delete Run",
                    "Are you sure you want to delete this run?", "Delete", "Cancel"))
                {
                    store.DeleteRun(selectedRunId);
                    selectedRunId = null;
                    selectedRun = null;
                    RefreshResults();
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }
    }

    /// <summary>
    /// Simple screenshot timeline viewer.
    /// </summary>
    public class AIScreenshotTimeline : EditorWindow
    {
        private AITestRun run;
        private int currentIndex;
        private Texture2D currentTexture;
        private AITestResultStore store;

        public static void ShowWindow(AITestRun run)
        {
            var window = GetWindow<AIScreenshotTimeline>("Screenshot Timeline");
            window.run = run;
            window.currentIndex = 0;
            window.store = AITestResultStore.Instance;
            window.LoadCurrentScreenshot();
            window.Show();
        }

        private void OnDisable()
        {
            ClearTexture();
        }

        private void LoadCurrentScreenshot()
        {
            ClearTexture();

            if (run == null || run.screenshots.Count == 0)
                return;

            var screenshot = run.screenshots[currentIndex];
            if (!string.IsNullOrEmpty(screenshot.filePath))
            {
                var bytes = store.LoadScreenshot(screenshot.filePath);
                if (bytes != null && bytes.Length > 0)
                {
                    currentTexture = new Texture2D(2, 2);
                    currentTexture.LoadImage(bytes);
                }
            }
        }

        private void ClearTexture()
        {
            if (currentTexture != null)
            {
                DestroyImmediate(currentTexture);
                currentTexture = null;
            }
        }

        private void OnGUI()
        {
            if (run == null || run.screenshots.Count == 0)
            {
                EditorGUILayout.HelpBox("No screenshots available.", MessageType.Info);
                return;
            }

            // Navigation
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUI.enabled = currentIndex > 0;
            if (GUILayout.Button("◀ Prev", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                currentIndex--;
                LoadCurrentScreenshot();
            }
            GUI.enabled = true;

            EditorGUILayout.LabelField($"{currentIndex + 1} / {run.screenshots.Count}",
                EditorStyles.centeredGreyMiniLabel);

            GUI.enabled = currentIndex < run.screenshots.Count - 1;
            if (GUILayout.Button("Next ▶", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                currentIndex++;
                LoadCurrentScreenshot();
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            // Slider
            EditorGUI.BeginChangeCheck();
            currentIndex = EditorGUILayout.IntSlider(currentIndex, 0, run.screenshots.Count - 1);
            if (EditorGUI.EndChangeCheck())
            {
                LoadCurrentScreenshot();
            }

            // Screenshot
            if (currentTexture != null)
            {
                var rect = GUILayoutUtility.GetRect(position.width - 20, position.height - 100);
                GUI.DrawTexture(rect, currentTexture, ScaleMode.ScaleToFit);
            }
            else
            {
                EditorGUILayout.HelpBox("Screenshot not available.", MessageType.Warning);
            }

            // Info
            var screenshot = run.screenshots[currentIndex];
            EditorGUILayout.LabelField($"Time: {screenshot.timestamp:F1}s | Elements: {screenshot.elementCount}");

            // Action at this time
            var actionAtTime = run.actions.FirstOrDefault(a => a.timestamp <= screenshot.timestamp);
            if (actionAtTime != null)
            {
                EditorGUILayout.LabelField($"Last Action: {actionAtTime.actionType}({actionAtTime.target})");
            }
        }
    }
}
#endif
