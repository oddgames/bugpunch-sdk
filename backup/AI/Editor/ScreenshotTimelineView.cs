#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace ODDGames.UIAutomation.AI.Editor
{
    /// <summary>
    /// Standalone window for viewing screenshot timelines from AI test runs.
    /// Provides a scrubbable timeline with action overlay information.
    /// </summary>
    public class ScreenshotTimelineView : EditorWindow
    {
        private AITestRun run;
        private int currentIndex;
        private Texture2D currentTexture;
        private Texture2D[] thumbnails;
        private AITestResultStore store;
        private Vector2 thumbnailScrollPos;
        private Vector2 actionScrollPos;
        private float thumbnailSize = 80f;
        private bool showActionOverlay = true;
        private bool autoPlay;
        private float autoPlaySpeed = 1f;
        private double lastAutoPlayTime;

        public static void ShowWindow(AITestRun run)
        {
            var window = GetWindow<ScreenshotTimelineView>("Screenshot Timeline");
            window.minSize = new Vector2(800, 600);
            window.run = run;
            window.currentIndex = 0;
            window.store = AITestResultStore.Instance;
            window.LoadThumbnails();
            window.LoadCurrentScreenshot();
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnUpdate;
            ClearTextures();
        }

        private void OnUpdate()
        {
            if (autoPlay && run != null && run.screenshots.Count > 1)
            {
                var now = EditorApplication.timeSinceStartup;
                var interval = 1.0 / autoPlaySpeed;

                if (now - lastAutoPlayTime > interval)
                {
                    lastAutoPlayTime = now;
                    currentIndex = (currentIndex + 1) % run.screenshots.Count;
                    LoadCurrentScreenshot();
                    Repaint();
                }
            }
        }

        private void LoadThumbnails()
        {
            ClearThumbnails();

            if (run == null || run.screenshots.Count == 0)
                return;

            thumbnails = new Texture2D[run.screenshots.Count];

            for (int i = 0; i < run.screenshots.Count; i++)
            {
                var screenshot = run.screenshots[i];
                if (!string.IsNullOrEmpty(screenshot.filePath))
                {
                    var bytes = store.LoadScreenshot(screenshot.filePath);
                    if (bytes != null && bytes.Length > 0)
                    {
                        var tex = new Texture2D(2, 2);
                        tex.LoadImage(bytes);

                        // Create thumbnail
                        thumbnails[i] = CreateThumbnail(tex, (int)thumbnailSize);
                        DestroyImmediate(tex);
                    }
                }
            }
        }

        private Texture2D CreateThumbnail(Texture2D source, int size)
        {
            var ratio = (float)source.width / source.height;
            int width = ratio > 1 ? size : (int)(size * ratio);
            int height = ratio > 1 ? (int)(size / ratio) : size;

            var rt = RenderTexture.GetTemporary(width, height);
            Graphics.Blit(source, rt);

            var previous = RenderTexture.active;
            RenderTexture.active = rt;

            var thumb = new Texture2D(width, height);
            thumb.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            thumb.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return thumb;
        }

        private void LoadCurrentScreenshot()
        {
            ClearCurrentTexture();

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

        private void ClearCurrentTexture()
        {
            if (currentTexture != null)
            {
                DestroyImmediate(currentTexture);
                currentTexture = null;
            }
        }

        private void ClearThumbnails()
        {
            if (thumbnails != null)
            {
                foreach (var thumb in thumbnails)
                {
                    if (thumb != null)
                        DestroyImmediate(thumb);
                }
                thumbnails = null;
            }
        }

        private void ClearTextures()
        {
            ClearCurrentTexture();
            ClearThumbnails();
        }

        private void OnGUI()
        {
            if (run == null || run.screenshots.Count == 0)
            {
                EditorGUILayout.HelpBox("No screenshots available for this run.", MessageType.Info);
                return;
            }

            DrawToolbar();
            DrawThumbnailStrip();

            EditorGUILayout.BeginHorizontal();

            // Left side - Main screenshot
            DrawMainScreenshot();

            // Right side - Action info
            DrawActionPanel();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Test name
            EditorGUILayout.LabelField(run.testName ?? "Unknown Test", EditorStyles.boldLabel, GUILayout.Width(200));

            GUILayout.FlexibleSpace();

            // Navigation buttons
            GUI.enabled = currentIndex > 0;
            if (GUILayout.Button("◀ Prev", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                currentIndex--;
                LoadCurrentScreenshot();
            }
            GUI.enabled = true;

            // Current position
            EditorGUILayout.LabelField($"{currentIndex + 1} / {run.screenshots.Count}",
                EditorStyles.centeredGreyMiniLabel, GUILayout.Width(60));

            GUI.enabled = currentIndex < run.screenshots.Count - 1;
            if (GUILayout.Button("Next ▶", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                currentIndex++;
                LoadCurrentScreenshot();
            }
            GUI.enabled = true;

            GUILayout.Space(10);

            // Auto-play toggle
            autoPlay = GUILayout.Toggle(autoPlay, "Auto-Play", EditorStyles.toolbarButton);

            if (autoPlay)
            {
                EditorGUILayout.LabelField("Speed:", GUILayout.Width(40));
                autoPlaySpeed = EditorGUILayout.Slider(autoPlaySpeed, 0.5f, 5f, GUILayout.Width(100));
            }

            GUILayout.Space(10);

            // Show action overlay
            showActionOverlay = GUILayout.Toggle(showActionOverlay, "Show Actions", EditorStyles.toolbarButton);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawThumbnailStrip()
        {
            if (thumbnails == null || thumbnails.Length == 0)
                return;

            var stripHeight = thumbnailSize + 25;

            EditorGUILayout.BeginHorizontal("box", GUILayout.Height(stripHeight));

            thumbnailScrollPos = EditorGUILayout.BeginScrollView(thumbnailScrollPos,
                GUILayout.Height(stripHeight));

            EditorGUILayout.BeginHorizontal();

            for (int i = 0; i < thumbnails.Length; i++)
            {
                var isSelected = i == currentIndex;
                var thumb = thumbnails[i];

                EditorGUILayout.BeginVertical(GUILayout.Width(thumbnailSize + 4));

                // Highlight selected
                if (isSelected)
                {
                    var rect = GUILayoutUtility.GetRect(thumbnailSize + 4, thumbnailSize + 4);
                    EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.9f, 0.5f));
                    GUI.SetNextControlName($"thumb_{i}");
                }

                // Draw thumbnail
                var thumbRect = GUILayoutUtility.GetRect(thumbnailSize, thumbnailSize);
                if (thumb != null)
                {
                    GUI.DrawTexture(thumbRect, thumb, ScaleMode.ScaleToFit);
                }
                else
                {
                    EditorGUI.DrawRect(thumbRect, Color.gray);
                }

                // Click to select
                if (Event.current.type == EventType.MouseDown && thumbRect.Contains(Event.current.mousePosition))
                {
                    currentIndex = i;
                    LoadCurrentScreenshot();
                    Repaint();
                }

                // Index label
                EditorGUILayout.LabelField($"{i + 1}", EditorStyles.centeredGreyMiniLabel,
                    GUILayout.Width(thumbnailSize));

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndHorizontal();

            // Slider for quick navigation
            EditorGUI.BeginChangeCheck();
            currentIndex = EditorGUILayout.IntSlider(currentIndex, 0, run.screenshots.Count - 1);
            if (EditorGUI.EndChangeCheck())
            {
                LoadCurrentScreenshot();
            }
        }

        private void DrawMainScreenshot()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.65f));

            var screenshot = run.screenshots[currentIndex];

            // Screenshot info
            EditorGUILayout.LabelField($"Time: {screenshot.timestamp:F2}s | Elements: {screenshot.elementCount}",
                EditorStyles.miniLabel);

            // Main image
            if (currentTexture != null)
            {
                var aspectRatio = (float)currentTexture.width / currentTexture.height;
                var availableWidth = position.width * 0.65f - 20;
                var availableHeight = position.height - 200;

                float width, height;
                if (availableWidth / aspectRatio <= availableHeight)
                {
                    width = availableWidth;
                    height = availableWidth / aspectRatio;
                }
                else
                {
                    height = availableHeight;
                    width = availableHeight * aspectRatio;
                }

                var rect = GUILayoutUtility.GetRect(width, height);
                GUI.DrawTexture(rect, currentTexture, ScaleMode.ScaleToFit);

                // Draw action overlay if enabled
                if (showActionOverlay)
                {
                    DrawActionOverlay(rect, screenshot);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Screenshot not available.", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawActionOverlay(Rect imageRect, StoredScreenshotRecord screenshot)
        {
            // Find action at this timestamp
            var action = run.actions.LastOrDefault(a => a.timestamp <= screenshot.timestamp);
            if (action == null)
                return;

            // Draw action indicator (simplified - would need element position data for accurate overlay)
            var overlayStyle = new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 10,
                normal = { textColor = Color.white }
            };

            var overlayRect = new Rect(imageRect.x + 5, imageRect.y + 5, 200, 40);

            var color = action.success ? new Color(0, 0.7f, 0, 0.8f) : new Color(0.7f, 0, 0, 0.8f);
            EditorGUI.DrawRect(overlayRect, color);

            var icon = action.success ? "OK" : "X";
            GUI.Label(overlayRect, $" {icon} {action.actionType}({action.target})", overlayStyle);
        }

        private void DrawActionPanel()
        {
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.LabelField("Action at this time", EditorStyles.boldLabel);

            var screenshot = run.screenshots[currentIndex];
            var action = run.actions.LastOrDefault(a => a.timestamp <= screenshot.timestamp);

            if (action != null)
            {
                EditorGUILayout.Space();

                // Action type
                EditorGUILayout.LabelField("Action:", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField($"{action.actionType}({action.target})");

                // Success indicator
                var statusColor = action.success ? Color.green : Color.red;
                GUI.color = statusColor;
                EditorGUILayout.LabelField(action.success ? "Success" : "Failed", EditorStyles.boldLabel);
                GUI.color = Color.white;

                if (!action.success && !string.IsNullOrEmpty(action.error))
                {
                    EditorGUILayout.LabelField("Error:", EditorStyles.miniBoldLabel);
                    EditorGUILayout.LabelField(action.error, EditorStyles.wordWrappedLabel);
                }

                EditorGUILayout.Space();

                // Reasoning
                if (!string.IsNullOrEmpty(action.reasoning))
                {
                    EditorGUILayout.LabelField("AI Reasoning:", EditorStyles.miniBoldLabel);
                    EditorGUILayout.LabelField(action.reasoning, EditorStyles.wordWrappedLabel);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No action at this timestamp.", MessageType.Info);
            }

            EditorGUILayout.Space();

            // All actions list
            EditorGUILayout.LabelField("All Actions", EditorStyles.boldLabel);

            actionScrollPos = EditorGUILayout.BeginScrollView(actionScrollPos);

            for (int i = 0; i < run.actions.Count; i++)
            {
                var a = run.actions[i];
                var isCurrentAction = action != null && a.timestamp == action.timestamp;

                if (isCurrentAction)
                {
                    GUI.color = new Color(0.24f, 0.48f, 0.9f, 0.5f);
                }

                EditorGUILayout.BeginHorizontal("box");

                var icon = a.success ? "OK" : "X";
                var iconColor = a.success ? Color.green : Color.red;

                GUI.color = iconColor;
                EditorGUILayout.LabelField(icon, GUILayout.Width(20));
                GUI.color = Color.white;

                EditorGUILayout.LabelField($"{i + 1}. {a.actionType}({a.target})");
                EditorGUILayout.LabelField($"{a.timestamp:F1}s", GUILayout.Width(50));

                // Jump to this action's screenshot
                if (GUILayout.Button("Go", GUILayout.Width(30)))
                {
                    // Find screenshot closest to this action
                    var closest = run.screenshots
                        .Select((s, idx) => new { Screenshot = s, Index = idx })
                        .OrderBy(x => Mathf.Abs(x.Screenshot.timestamp - a.timestamp))
                        .FirstOrDefault();

                    if (closest != null)
                    {
                        currentIndex = closest.Index;
                        LoadCurrentScreenshot();
                    }
                }

                EditorGUILayout.EndHorizontal();

                if (isCurrentAction)
                {
                    GUI.color = Color.white;
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }
    }
}
#endif
