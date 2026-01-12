#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace ODDGames.UITest.AI.Editor
{
    /// <summary>
    /// Live debug panel showing AI test execution state.
    /// </summary>
    public class AIDebugPanel : EditorWindow
    {
        private Texture2D currentScreenshot;
        private string currentReasoning;
        private List<ActionInfo> actionHistory = new List<ActionInfo>();
        private Vector2 actionScrollPos;
        private Vector2 logScrollPos;
        private float splitPosition = 0.5f;

        private AITestRunner runner;
        private string lastTestName;
        private int lastActionCount;
        private List<string> logs = new List<string>();

        [MenuItem("Window/Analysis/UI Automation/AI Debug Panel")]
        public static void ShowWindow()
        {
            var window = GetWindow<AIDebugPanel>("AI Debug");
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnUpdate;
            ClearScreenshot();
        }

        private void OnUpdate()
        {
            if (AITestRunner.IsRunning)
            {
                var current = AITestRunner.Current;
                if (current != runner)
                {
                    SubscribeToRunner(current);
                }
                Repaint();
            }
        }

        private void SubscribeToRunner(AITestRunner newRunner)
        {
            runner = newRunner;
            actionHistory.Clear();
            logs.Clear();
            currentReasoning = null;
            ClearScreenshot();

            runner.OnScreenCaptured += OnScreenCaptured;
            runner.OnReasoning += OnReasoning;
            runner.OnActionExecuted += OnActionExecuted;
            runner.OnEscalated += OnEscalated;
            runner.OnTestCompleted += OnTestCompleted;
        }

        private void OnScreenCaptured(ScreenState screen)
        {
            ClearScreenshot();

            if (screen.ScreenshotPng != null && screen.ScreenshotPng.Length > 0)
            {
                currentScreenshot = new Texture2D(2, 2);
                currentScreenshot.LoadImage(screen.ScreenshotPng);
            }
        }

        private void OnReasoning(string reasoning)
        {
            currentReasoning = reasoning;
        }

        private void OnActionExecuted(AIAction action, ActionResult result)
        {
            actionHistory.Add(new ActionInfo
            {
                Type = action.ActionType,
                Target = GetActionTarget(action),
                Success = result.Success,
                Error = result.Error,
                Reasoning = currentReasoning
            });
        }

        private void OnEscalated(ModelTier from, ModelTier to, string reason)
        {
            logs.Add($"[Escalated] {from} → {to}: {reason}");
        }

        private void OnTestCompleted(AITestResult result)
        {
            logs.Add($"[Completed] {result.Status}: {result.Message}");
        }

        private void OnGUI()
        {
            if (!AITestRunner.IsRunning)
            {
                DrawNotRunningState();
                return;
            }

            DrawHeader();

            EditorGUILayout.BeginHorizontal();

            // Left panel - Screenshot
            DrawScreenshotPanel();

            // Right panel - Info
            DrawInfoPanel();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawNotRunningState()
        {
            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox(
                "No AI test is currently running.\n\nRun an AI Test to see live debug information.",
                MessageType.Info);

            if (actionHistory.Count > 0)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Last Run History", EditorStyles.boldLabel);
                DrawActionHistory();
            }
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Current model tier
            var tier = runner?.CurrentTier.ToString() ?? "Unknown";
            EditorGUILayout.LabelField($"Model: {tier}", GUILayout.Width(150));

            // Token usage
            var stats = runner?.ConversationStats;
            if (stats != null)
            {
                var tokenColor = stats.UtilizationPercent > 80 ? Color.yellow : Color.white;
                var oldColor = GUI.color;
                GUI.color = tokenColor;
                EditorGUILayout.LabelField($"Tokens: {stats.EstimatedTokens}/{stats.MaxTokens}", GUILayout.Width(120));
                GUI.color = oldColor;

                if (stats.WasCompacted)
                {
                    EditorGUILayout.LabelField("[Compacted]", EditorStyles.miniLabel, GUILayout.Width(70));
                }
            }

            GUILayout.FlexibleSpace();

            // Action count
            var actionCount = runner?.CurrentAction ?? 0;
            var maxActions = runner?.MaxActions ?? 50;
            EditorGUILayout.LabelField($"Actions: {actionCount}/{maxActions}", GUILayout.Width(100));

            // Stuck level
            var stuckLevel = runner?.StuckLevel ?? 0;
            if (stuckLevel > 0)
            {
                GUI.color = stuckLevel > 1 ? Color.red : Color.yellow;
                EditorGUILayout.LabelField($"[Stuck: {stuckLevel}]", GUILayout.Width(70));
                GUI.color = Color.white;
            }

            // Cancel button
            if (GUILayout.Button("Cancel", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                runner?.Cancel();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawScreenshotPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(position.width * splitPosition));

            EditorGUILayout.LabelField("Current Screen", EditorStyles.boldLabel);

            if (currentScreenshot != null)
            {
                var rect = GUILayoutUtility.GetRect(400, 300, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                GUI.DrawTexture(rect, currentScreenshot, ScaleMode.ScaleToFit);
            }
            else
            {
                EditorGUILayout.HelpBox("Waiting for screenshot...", MessageType.None);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawInfoPanel()
        {
            EditorGUILayout.BeginVertical();

            // AI Reasoning
            EditorGUILayout.LabelField("AI Reasoning", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(currentReasoning ?? "Thinking...",
                GUILayout.Height(80));

            EditorGUILayout.Space(5);

            // Action History
            EditorGUILayout.LabelField("Action History", EditorStyles.boldLabel);
            DrawActionHistory();

            EditorGUILayout.Space(5);

            // Logs
            EditorGUILayout.LabelField("Logs", EditorStyles.boldLabel);
            DrawLogs();

            EditorGUILayout.EndVertical();
        }

        private void DrawActionHistory()
        {
            actionScrollPos = EditorGUILayout.BeginScrollView(actionScrollPos, GUILayout.Height(150));

            for (int i = actionHistory.Count - 1; i >= 0; i--)
            {
                var action = actionHistory[i];
                var color = action.Success ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.8f, 0.2f, 0.2f);
                var icon = action.Success ? "✓" : "✗";

                EditorGUILayout.BeginHorizontal();

                GUI.color = color;
                EditorGUILayout.LabelField($"{icon}", GUILayout.Width(20));
                GUI.color = Color.white;

                EditorGUILayout.LabelField($"{i + 1}. {action.Type}: {action.Target}");
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(action.Reasoning))
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField(action.Reasoning, EditorStyles.miniLabel);
                    EditorGUI.indentLevel--;
                }

                if (!action.Success && !string.IsNullOrEmpty(action.Error))
                {
                    EditorGUI.indentLevel++;
                    GUI.color = new Color(0.8f, 0.2f, 0.2f);
                    EditorGUILayout.LabelField($"Error: {action.Error}", EditorStyles.miniLabel);
                    GUI.color = Color.white;
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawLogs()
        {
            logScrollPos = EditorGUILayout.BeginScrollView(logScrollPos, GUILayout.Height(100));

            foreach (var log in logs)
            {
                EditorGUILayout.LabelField(log, EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        private void ClearScreenshot()
        {
            if (currentScreenshot != null)
            {
                DestroyImmediate(currentScreenshot);
                currentScreenshot = null;
            }
        }

        private string GetActionTarget(AIAction action)
        {
            return action switch
            {
                ClickAction click => click.ElementId ?? $"({click.ScreenPosition?.x:F2},{click.ScreenPosition?.y:F2})",
                TypeAction type => type.ElementId,
                DragAction drag => drag.FromElementId,
                ScrollAction scroll => scroll.ElementId,
                WaitAction wait => $"{wait.Seconds}s",
                PassAction pass => pass.Reason,
                FailAction fail => fail.Reason,
                _ => ""
            };
        }

        private struct ActionInfo
        {
            public string Type;
            public string Target;
            public bool Success;
            public string Error;
            public string Reasoning;
        }
    }
}
#endif
