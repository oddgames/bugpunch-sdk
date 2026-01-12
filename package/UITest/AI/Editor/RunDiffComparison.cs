#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace ODDGames.UITest.AI.Editor
{
    /// <summary>
    /// Window for comparing two test runs side-by-side, particularly useful
    /// for analyzing differences between successful and failed runs.
    /// </summary>
    public class RunDiffComparison : EditorWindow
    {
        private AITestRun leftRun;   // Usually successful run
        private AITestRun rightRun;  // Usually failed run
        private AITestResultStore store;

        private Texture2D leftTexture;
        private Texture2D rightTexture;

        private int divergeIndex = -1;
        private int currentIndex;
        private Vector2 scrollPos;
        private bool syncNavigation = true;
        private float splitPosition = 0.5f;

        [MenuItem("Window/Analysis/UI Automation/Run Diff Comparison")]
        public static void ShowWindow()
        {
            var window = GetWindow<RunDiffComparison>("Run Comparison");
            window.minSize = new Vector2(1000, 600);
            window.Show();
        }

        public static void ShowWindow(AITestRun successRun, AITestRun failedRun)
        {
            var window = GetWindow<RunDiffComparison>("Run Comparison");
            window.minSize = new Vector2(1000, 600);
            window.leftRun = successRun;
            window.rightRun = failedRun;
            window.FindDivergencePoint();
            window.currentIndex = window.divergeIndex >= 0 ? window.divergeIndex : 0;
            window.LoadScreenshots();
            window.Show();
        }

        private void OnEnable()
        {
            store = AITestResultStore.Instance;
        }

        private void OnDisable()
        {
            ClearTextures();
        }

        private void ClearTextures()
        {
            if (leftTexture != null)
            {
                DestroyImmediate(leftTexture);
                leftTexture = null;
            }
            if (rightTexture != null)
            {
                DestroyImmediate(rightTexture);
                rightTexture = null;
            }
        }

        private void LoadScreenshots()
        {
            ClearTextures();

            if (leftRun != null && leftRun.screenshots.Count > currentIndex)
            {
                var screenshot = leftRun.screenshots[currentIndex];
                if (!string.IsNullOrEmpty(screenshot.filePath))
                {
                    var bytes = store.LoadScreenshot(screenshot.filePath);
                    if (bytes != null && bytes.Length > 0)
                    {
                        leftTexture = new Texture2D(2, 2);
                        leftTexture.LoadImage(bytes);
                    }
                }
            }

            if (rightRun != null && rightRun.screenshots.Count > currentIndex)
            {
                var screenshot = rightRun.screenshots[currentIndex];
                if (!string.IsNullOrEmpty(screenshot.filePath))
                {
                    var bytes = store.LoadScreenshot(screenshot.filePath);
                    if (bytes != null && bytes.Length > 0)
                    {
                        rightTexture = new Texture2D(2, 2);
                        rightTexture.LoadImage(bytes);
                    }
                }
            }
        }

        private void FindDivergencePoint()
        {
            divergeIndex = -1;

            if (leftRun == null || rightRun == null)
                return;

            var minCount = Mathf.Min(leftRun.screenshots.Count, rightRun.screenshots.Count);

            for (int i = 0; i < minCount; i++)
            {
                var leftHash = leftRun.screenshots[i].screenHash;
                var rightHash = rightRun.screenshots[i].screenHash;

                if (!ScreenHash.AreSimilar(leftHash, rightHash))
                {
                    divergeIndex = Mathf.Max(0, i - 1);
                    return;
                }
            }

            // No divergence found in overlapping region
            divergeIndex = minCount > 0 ? minCount - 1 : 0;
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (leftRun == null || rightRun == null)
            {
                DrawRunSelector();
                return;
            }

            DrawNavigationBar();

            EditorGUILayout.BeginHorizontal();

            // Left panel
            DrawRunPanel(leftRun, leftTexture, "Left (Reference)", Color.green, splitPosition);

            // Splitter
            DrawSplitter();

            // Right panel
            DrawRunPanel(rightRun, rightTexture, "Right (Compare)", Color.red, 1f - splitPosition);

            EditorGUILayout.EndHorizontal();

            // Differences summary
            DrawDifferencesSummary();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Select Runs", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                leftRun = null;
                rightRun = null;
                ClearTextures();
            }

            GUILayout.Space(10);

            if (leftRun != null && rightRun != null)
            {
                syncNavigation = GUILayout.Toggle(syncNavigation, "Sync Navigation", EditorStyles.toolbarButton);

                GUILayout.Space(10);

                if (divergeIndex >= 0)
                {
                    if (GUILayout.Button("Go to Divergence", EditorStyles.toolbarButton, GUILayout.Width(110)))
                    {
                        currentIndex = divergeIndex;
                        LoadScreenshots();
                    }
                }
            }

            GUILayout.FlexibleSpace();

            if (leftRun != null && rightRun != null)
            {
                EditorGUILayout.LabelField($"Step {currentIndex + 1}", GUILayout.Width(60));
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawRunSelector()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            EditorGUILayout.HelpBox("Select two runs to compare. Typically select a successful run on the left and a failed run on the right.", MessageType.Info);

            EditorGUILayout.Space();

            // Query recent runs
            var query = new ResultQuery { Limit = 50, SortBy = SortBy.Newest };
            var runs = store.QueryRuns(query).ToList();

            EditorGUILayout.BeginHorizontal();

            // Left run selection
            EditorGUILayout.BeginVertical("box", GUILayout.Width(position.width * 0.5f - 10));
            EditorGUILayout.LabelField("Left Run (Reference)", EditorStyles.boldLabel);

            if (leftRun != null)
            {
                DrawSelectedRunInfo(leftRun, Color.green);
                if (GUILayout.Button("Clear"))
                {
                    leftRun = null;
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Select from recent runs:", EditorStyles.miniBoldLabel);

            foreach (var run in runs)
            {
                DrawRunSelectionRow(run, true);
            }

            EditorGUILayout.EndVertical();

            // Right run selection
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Right Run (Compare)", EditorStyles.boldLabel);

            if (rightRun != null)
            {
                DrawSelectedRunInfo(rightRun, Color.red);
                if (GUILayout.Button("Clear"))
                {
                    rightRun = null;
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Select from recent runs:", EditorStyles.miniBoldLabel);

            foreach (var run in runs)
            {
                DrawRunSelectionRow(run, false);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            GUI.enabled = leftRun != null && rightRun != null;
            if (GUILayout.Button("Compare Runs", GUILayout.Height(30)))
            {
                FindDivergencePoint();
                currentIndex = divergeIndex >= 0 ? divergeIndex : 0;
                LoadScreenshots();
            }
            GUI.enabled = true;

            EditorGUILayout.EndScrollView();
        }

        private void DrawSelectedRunInfo(AITestRun run, Color color)
        {
            GUI.color = color;
            EditorGUILayout.BeginVertical("box");
            GUI.color = Color.white;

            EditorGUILayout.LabelField(run.testName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Status: {run.status}");
            EditorGUILayout.LabelField($"Duration: {run.durationSeconds:F1}s");
            EditorGUILayout.LabelField($"Actions: {run.actionsExecuted}");
            EditorGUILayout.LabelField($"Screenshots: {run.screenshots.Count}");

            EditorGUILayout.EndVertical();
        }

        private void DrawRunSelectionRow(RunSummary run, bool isLeft)
        {
            var isSelected = (isLeft && leftRun?.id == run.id) || (!isLeft && rightRun?.id == run.id);

            if (isSelected)
            {
                GUI.color = new Color(0.24f, 0.48f, 0.9f, 0.3f);
            }

            EditorGUILayout.BeginHorizontal("box");

            GUI.color = Color.white;

            // Status icon
            var statusColor = run.status == TestStatus.Passed ? Color.green : Color.red;
            GUI.color = statusColor;
            EditorGUILayout.LabelField(run.status == TestStatus.Passed ? "OK" : "X", GUILayout.Width(25));
            GUI.color = Color.white;

            EditorGUILayout.LabelField(run.testName ?? "Unknown", GUILayout.Width(150));
            EditorGUILayout.LabelField(run.Timestamp.ToString("MM/dd HH:mm"), GUILayout.Width(80));
            EditorGUILayout.LabelField($"{run.duration:F1}s", GUILayout.Width(50));

            if (GUILayout.Button("Select", GUILayout.Width(60)))
            {
                var fullRun = store.LoadRun(run.id);
                if (fullRun != null)
                {
                    if (isLeft)
                        leftRun = fullRun;
                    else
                        rightRun = fullRun;
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawNavigationBar()
        {
            var maxIndex = Mathf.Max(
                leftRun?.screenshots.Count ?? 0,
                rightRun?.screenshots.Count ?? 0
            ) - 1;

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUI.enabled = currentIndex > 0;
            if (GUILayout.Button("◀ Prev", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                currentIndex--;
                LoadScreenshots();
            }
            GUI.enabled = true;

            // Slider
            EditorGUI.BeginChangeCheck();
            currentIndex = EditorGUILayout.IntSlider(currentIndex, 0, Mathf.Max(0, maxIndex));
            if (EditorGUI.EndChangeCheck())
            {
                LoadScreenshots();
            }

            GUI.enabled = currentIndex < maxIndex;
            if (GUILayout.Button("Next ▶", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                currentIndex++;
                LoadScreenshots();
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            // Divergence indicator
            if (divergeIndex >= 0)
            {
                var divergeColor = currentIndex == divergeIndex ? Color.yellow : new Color(1, 0.8f, 0, 0.3f);
                GUI.color = divergeColor;
                EditorGUILayout.HelpBox($"Divergence detected at step {divergeIndex + 1}", MessageType.Warning);
                GUI.color = Color.white;
            }
        }

        private void DrawRunPanel(AITestRun run, Texture2D texture, string title, Color titleColor, float widthFactor)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(position.width * widthFactor - 10));

            // Title
            GUI.color = titleColor;
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            GUI.color = Color.white;

            EditorGUILayout.LabelField($"{run.testName} - {run.status}", EditorStyles.miniLabel);

            // Screenshot
            if (texture != null)
            {
                var aspectRatio = (float)texture.width / texture.height;
                var width = position.width * widthFactor - 20;
                var height = width / aspectRatio;

                var rect = GUILayoutUtility.GetRect(width, Mathf.Min(height, 300));
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
            }
            else
            {
                var rect = GUILayoutUtility.GetRect(100, 200);
                EditorGUI.DrawRect(rect, Color.gray);
                GUI.Label(rect, "No screenshot", EditorStyles.centeredGreyMiniLabel);
            }

            // Action at this step
            if (run.screenshots.Count > currentIndex)
            {
                var screenshot = run.screenshots[currentIndex];
                var action = run.actions.LastOrDefault(a => a.timestamp <= screenshot.timestamp);

                EditorGUILayout.Space();

                if (action != null)
                {
                    var actionColor = action.success ? Color.green : Color.red;
                    GUI.color = actionColor;

                    var icon = action.success ? "OK" : "X";
                    EditorGUILayout.LabelField($"{icon} {action.actionType}({action.target})", EditorStyles.boldLabel);

                    GUI.color = Color.white;

                    if (!string.IsNullOrEmpty(action.reasoning))
                    {
                        EditorGUILayout.LabelField(action.reasoning, EditorStyles.wordWrappedMiniLabel);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("No action at this step", EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSplitter()
        {
            var rect = GUILayoutUtility.GetRect(4, position.height - 100);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
            }

            if (GUIUtility.hotControl != 0 && Event.current.type == EventType.MouseDrag)
            {
                splitPosition = Mathf.Clamp(Event.current.mousePosition.x / position.width, 0.25f, 0.75f);
                Repaint();
            }

            if (Event.current.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
            }
        }

        private void DrawDifferencesSummary()
        {
            if (leftRun == null || rightRun == null)
                return;

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Comparison Summary", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Left Run:", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField($"Status: {leftRun.status}");
            EditorGUILayout.LabelField($"Actions: {leftRun.actionsExecuted}");
            EditorGUILayout.LabelField($"Model: {leftRun.startingModelTier} → {leftRun.finalModelTier}");
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Right Run:", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField($"Status: {rightRun.status}");
            EditorGUILayout.LabelField($"Actions: {rightRun.actionsExecuted}");
            EditorGUILayout.LabelField($"Model: {rightRun.startingModelTier} → {rightRun.finalModelTier}");
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            // Key differences
            var differences = FindKeyDifferences();
            if (differences.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Key Differences:", EditorStyles.miniBoldLabel);
                foreach (var diff in differences)
                {
                    EditorGUILayout.LabelField($"  {diff}");
                }
            }

            EditorGUILayout.EndVertical();
        }

        private List<string> FindKeyDifferences()
        {
            var differences = new List<string>();

            if (leftRun == null || rightRun == null)
                return differences;

            // Status difference
            if (leftRun.status != rightRun.status)
            {
                differences.Add($"Status: {leftRun.status} vs {rightRun.status}");
            }

            // Action count difference
            var actionDiff = Mathf.Abs(leftRun.actionsExecuted - rightRun.actionsExecuted);
            if (actionDiff > 5)
            {
                differences.Add($"Action count differs by {actionDiff}");
            }

            // Divergence point
            if (divergeIndex >= 0 && divergeIndex < Mathf.Min(leftRun.actions.Count, rightRun.actions.Count))
            {
                var leftAction = leftRun.actions.Count > divergeIndex ? leftRun.actions[divergeIndex] : null;
                var rightAction = rightRun.actions.Count > divergeIndex ? rightRun.actions[divergeIndex] : null;

                if (leftAction != null && rightAction != null)
                {
                    if (leftAction.actionType != rightAction.actionType ||
                        leftAction.target != rightAction.target)
                    {
                        differences.Add($"Different actions at step {divergeIndex + 1}: " +
                            $"{leftAction.actionType}({leftAction.target}) vs " +
                            $"{rightAction.actionType}({rightAction.target})");
                    }
                }
            }

            // Model escalation difference
            if (leftRun.modelEscalations != rightRun.modelEscalations)
            {
                differences.Add($"Model escalations: {leftRun.modelEscalations} vs {rightRun.modelEscalations}");
            }

            return differences;
        }
    }
}
#endif
