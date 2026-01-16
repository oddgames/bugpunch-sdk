using UnityEngine;
using UnityEditor;

namespace ODDGames.UITest.VisualBuilder.Editor
{
    /// <summary>
    /// Editor-only overlay that draws a target indicator in the Game view using GL.
    /// Uses Camera.onPostRender to draw without requiring runtime scripts.
    /// </summary>
    public static class TargetOverlay
    {
        private static bool _isVisible;
        private static Vector2 _targetPosition;
        private static string _targetLabel;
        private static Rect _targetBounds;
        private static bool _hasBounds;
        private static Material _glMaterial;
        private static bool _isSubscribed;

        // Visual settings
        private static readonly Color CrosshairColor = new Color(0f, 1f, 0.5f, 0.9f);
        private static readonly Color BoundsColor = new Color(0f, 1f, 0.5f, 0.4f);
        private static readonly Color LabelBgColor = new Color(0f, 0f, 0f, 0.85f);
        private const float CrosshairSize = 24f;
        private const float CrosshairThickness = 3f;

        /// <summary>
        /// Shows the target indicator at the specified screen position.
        /// </summary>
        public static void Show(Vector2 screenPosition, string label, Rect? bounds = null)
        {
            _targetPosition = screenPosition;
            _targetLabel = label ?? "";
            _hasBounds = bounds.HasValue;
            if (bounds.HasValue)
                _targetBounds = bounds.Value;
            _isVisible = true;

            EnsureSubscribed();
        }

        /// <summary>
        /// Hides the target indicator.
        /// </summary>
        public static void Hide()
        {
            _isVisible = false;
        }

        private static void EnsureSubscribed()
        {
            if (_isSubscribed) return;

            Camera.onPostRender += OnPostRender;
            EditorApplication.update += OnEditorUpdate;
            _isSubscribed = true;
        }

        private static void Unsubscribe()
        {
            if (!_isSubscribed) return;

            Camera.onPostRender -= OnPostRender;
            EditorApplication.update -= OnEditorUpdate;
            _isSubscribed = false;

            if (_glMaterial != null)
            {
                Object.DestroyImmediate(_glMaterial);
                _glMaterial = null;
            }
        }

        private static void OnEditorUpdate()
        {
            // Unsubscribe when not in play mode or not visible
            if (!EditorApplication.isPlaying || !_isVisible)
            {
                if (!_isVisible)
                    Unsubscribe();
            }
        }

        private static Material GetMaterial()
        {
            if (_glMaterial == null)
            {
                // Unity built-in shader for colored rendering
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null)
                    shader = Shader.Find("UI/Default");

                _glMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                _glMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _glMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _glMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                _glMaterial.SetInt("_ZWrite", 0);
                _glMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
            return _glMaterial;
        }

        private static void OnPostRender(Camera cam)
        {
            if (!_isVisible) return;

            // Only draw on the main camera or game view camera
            if (cam.cameraType != CameraType.Game) return;

            var mat = GetMaterial();
            mat.SetPass(0);

            GL.PushMatrix();
            GL.LoadPixelMatrix();

            // GL coordinates: origin at bottom-left, Y goes up
            // Screen coordinates: origin at bottom-left, Y goes up
            float x = _targetPosition.x;
            float y = _targetPosition.y;

            // Draw bounds if available
            if (_hasBounds)
            {
                DrawRectOutline(_targetBounds.x, _targetBounds.y, _targetBounds.width, _targetBounds.height, BoundsColor, 2f);
            }

            // Draw crosshair
            DrawCrosshair(x, y, CrosshairSize, CrosshairThickness, CrosshairColor);

            // Draw label background and text position marker
            if (!string.IsNullOrEmpty(_targetLabel))
            {
                DrawLabelBackground(x, y);
            }

            GL.PopMatrix();
        }

        private static void DrawCrosshair(float x, float y, float size, float thickness, Color color)
        {
            float half = size / 2f;

            GL.Begin(GL.QUADS);
            GL.Color(color);

            // Horizontal line
            GL.Vertex3(x - half, y - thickness / 2f, 0);
            GL.Vertex3(x + half, y - thickness / 2f, 0);
            GL.Vertex3(x + half, y + thickness / 2f, 0);
            GL.Vertex3(x - half, y + thickness / 2f, 0);

            // Vertical line
            GL.Vertex3(x - thickness / 2f, y - half, 0);
            GL.Vertex3(x + thickness / 2f, y - half, 0);
            GL.Vertex3(x + thickness / 2f, y + half, 0);
            GL.Vertex3(x - thickness / 2f, y + half, 0);

            // Center dot (larger)
            float dotSize = thickness * 1.5f;
            GL.Vertex3(x - dotSize, y - dotSize, 0);
            GL.Vertex3(x + dotSize, y - dotSize, 0);
            GL.Vertex3(x + dotSize, y + dotSize, 0);
            GL.Vertex3(x - dotSize, y + dotSize, 0);

            GL.End();

            // Outer ring for visibility
            DrawCircleOutline(x, y, half, color, 2f);
        }

        private static void DrawCircleOutline(float cx, float cy, float radius, Color color, float thickness)
        {
            GL.Begin(GL.LINES);
            GL.Color(color);

            int segments = 32;
            for (int i = 0; i < segments; i++)
            {
                float angle1 = (float)i / segments * Mathf.PI * 2f;
                float angle2 = (float)(i + 1) / segments * Mathf.PI * 2f;

                GL.Vertex3(cx + Mathf.Cos(angle1) * radius, cy + Mathf.Sin(angle1) * radius, 0);
                GL.Vertex3(cx + Mathf.Cos(angle2) * radius, cy + Mathf.Sin(angle2) * radius, 0);
            }

            GL.End();
        }

        private static void DrawRectOutline(float x, float y, float width, float height, Color color, float thickness)
        {
            GL.Begin(GL.QUADS);
            GL.Color(color);

            // Top edge
            GL.Vertex3(x, y + height - thickness, 0);
            GL.Vertex3(x + width, y + height - thickness, 0);
            GL.Vertex3(x + width, y + height, 0);
            GL.Vertex3(x, y + height, 0);

            // Bottom edge
            GL.Vertex3(x, y, 0);
            GL.Vertex3(x + width, y, 0);
            GL.Vertex3(x + width, y + thickness, 0);
            GL.Vertex3(x, y + thickness, 0);

            // Left edge
            GL.Vertex3(x, y, 0);
            GL.Vertex3(x + thickness, y, 0);
            GL.Vertex3(x + thickness, y + height, 0);
            GL.Vertex3(x, y + height, 0);

            // Right edge
            GL.Vertex3(x + width - thickness, y, 0);
            GL.Vertex3(x + width, y, 0);
            GL.Vertex3(x + width, y + height, 0);
            GL.Vertex3(x + width - thickness, y + height, 0);

            GL.End();
        }

        private static void DrawLabelBackground(float x, float y)
        {
            // Estimate label size (rough approximation since we can't measure text in GL)
            float charWidth = 8f;
            float padding = 8f;
            float labelWidth = _targetLabel.Length * charWidth + padding * 2f;
            float labelHeight = 20f;

            // Position above crosshair
            float labelX = x - labelWidth / 2f;
            float labelY = y + CrosshairSize + 8f;

            // Keep on screen
            labelX = Mathf.Clamp(labelX, 4f, Screen.width - labelWidth - 4f);
            labelY = Mathf.Clamp(labelY, 4f, Screen.height - labelHeight - 4f);

            // Draw background
            GL.Begin(GL.QUADS);
            GL.Color(LabelBgColor);
            GL.Vertex3(labelX, labelY, 0);
            GL.Vertex3(labelX + labelWidth, labelY, 0);
            GL.Vertex3(labelX + labelWidth, labelY + labelHeight, 0);
            GL.Vertex3(labelX, labelY + labelHeight, 0);
            GL.End();

            // Draw border
            DrawRectOutline(labelX, labelY, labelWidth, labelHeight, CrosshairColor, 1f);
        }
    }
}
