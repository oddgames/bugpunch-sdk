using System.Collections.Generic;
using UnityEngine;

namespace ODDGames.UIAutomation
{
    /// <summary>
    /// Runtime input visualizer that draws cursor icons and trails on screen.
    /// Uses OnGUI for rendering which works regardless of camera setup.
    /// Icons are loaded from Resources folder.
    /// </summary>
    public class InputVisualizer : MonoBehaviour
    {
        #region Singleton

        private static InputVisualizer _instance;
        private static bool _enabled = true;

        /// <summary>
        /// Enables or disables input visualization globally.
        /// </summary>
        public static bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                if (value)
                    EnsureInstance();
                else if (_instance != null)
                {
                    Destroy(_instance.gameObject);
                    _instance = null;
                }
            }
        }

        private static void EnsureInstance()
        {
            if (_instance != null) return;

            var go = new GameObject("InputVisualizer");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<InputVisualizer>();
        }

        #endregion

        #region Configuration

        /// <summary>Duration in seconds for click ripple animation.</summary>
        public static float ClickDuration = 0.3f;

        /// <summary>Maximum radius for click ripple in pixels.</summary>
        public static float ClickRadius = 25f;

        /// <summary>Duration in seconds for cursor trail to fade.</summary>
        public static float TrailDuration = 0.4f;

        /// <summary>Maximum number of trail points to keep.</summary>
        public static int MaxTrailPoints = 40;

        /// <summary>Size of the cursor icon in pixels.</summary>
        public static float CursorSize = 48f;

        /// <summary>Color for click ripple effect.</summary>
        public static Color ClickColor = new Color(0.3f, 0.85f, 1f, 0.6f);

        /// <summary>Color for cursor trail.</summary>
        public static Color TrailColor = new Color(1f, 1f, 1f, 0.5f);

        /// <summary>Duration in seconds for scroll indicator.</summary>
        public static float ScrollDuration = 0.3f;

        #endregion

        #region Event Data

        private struct ClickEvent
        {
            public Vector2 Position;
            public float StartTime;
            public int TapCount;
            public bool IsMouse;
        }

        private struct TrailPoint
        {
            public Vector2 Position;
            public float Time;
        }

        private struct ScrollEvent
        {
            public Vector2 Position;
            public Vector2 Direction;
            public float StartTime;
        }

        private struct CursorState
        {
            public Vector2 Position;
            public bool IsActive;
            public bool IsMouse;
            public bool IsPressed;
            public int FingerIndex;
        }

        private readonly List<ClickEvent> _clicks = new();
        private readonly List<TrailPoint> _trail = new();
        private readonly List<ScrollEvent> _scrolls = new();
        private CursorState? _activeCursor;

        // Icon textures loaded from Resources
        private Texture2D _mouseCursorIcon;
        private Texture2D _mouseClickIcon;
        private Texture2D _touchIcon;
        private Texture2D _touchTapIcon;
        private Texture2D _scrollUpIcon;
        private Texture2D _scrollDownIcon;

        // For drawing circles/lines in OnGUI
        private Texture2D _circleTexture;
        private Texture2D _pixelTexture;

        #endregion

        #region Public API

        /// <summary>Records a click/tap event for visualization.</summary>
        public static void RecordClick(Vector2 screenPosition, int tapCount = 1, int pointerIndex = -1)
        {
            if (!_enabled) return;
            EnsureInstance();
            if (_instance == null) return;

            _instance._clicks.Add(new ClickEvent
            {
                Position = screenPosition,
                StartTime = Time.unscaledTime,
                TapCount = tapCount,
                IsMouse = pointerIndex < 0
            });
        }

        /// <summary>Shows a cursor at the specified position and adds to trail.</summary>
        public static void RecordCursorPosition(Vector2 screenPosition, bool isMouse = true, int fingerIndex = 0, bool isPressed = true)
        {
            if (!_enabled) return;
            EnsureInstance();
            if (_instance == null) return;

            float now = Time.unscaledTime;

            _instance._activeCursor = new CursorState
            {
                Position = screenPosition,
                IsActive = true,
                IsMouse = isMouse,
                IsPressed = isPressed,
                FingerIndex = fingerIndex
            };

            // Add to trail
            var trail = _instance._trail;
            if (trail.Count == 0 || Vector2.Distance(trail[trail.Count - 1].Position, screenPosition) > 2f)
            {
                trail.Add(new TrailPoint
                {
                    Position = screenPosition,
                    Time = now
                });

                while (trail.Count > MaxTrailPoints)
                    trail.RemoveAt(0);
            }
        }

        /// <summary>Hides the active cursor.</summary>
        public static void RecordCursorEnd()
        {
            if (!_enabled || _instance == null) return;
            _instance._activeCursor = null;
        }

        /// <summary>Records a scroll event.</summary>
        public static void RecordScroll(Vector2 screenPosition, Vector2 direction)
        {
            if (!_enabled) return;
            EnsureInstance();
            if (_instance == null) return;

            _instance._scrolls.Add(new ScrollEvent
            {
                Position = screenPosition,
                Direction = direction.normalized,
                StartTime = Time.unscaledTime
            });

            RecordCursorPosition(screenPosition, true, 0, false);
        }

        /// <summary>Clears the trail immediately.</summary>
        public static void ClearTrail()
        {
            if (_instance == null) return;
            _instance._trail.Clear();
        }

        // Legacy API stubs for compatibility
        public static void RecordDragStart(Vector2 screenPosition) => RecordCursorPosition(screenPosition, false, 0, true);
        public static void RecordDragPoint(Vector2 screenPosition) => RecordCursorPosition(screenPosition, false, 0, true);
        public static void RecordDragEnd(Vector2 screenPosition) { }
        public static void RecordKeyPress(string keyName) { }
        public static void RecordKeyHoldStart(string keyName) { }
        public static void RecordKeyHoldEnd() { }
        public static void RecordHoldStart(Vector2 screenPosition) => RecordCursorPosition(screenPosition, false, 0, true);
        public static void RecordHoldEnd() => RecordCursorEnd();
        public static void RecordPinchStart(Vector2 center, float fingerDistance) { }
        public static void RecordPinchEnd(float endDistance) { }
        public static void RecordRotateStart(Vector2 center, float radius) { }
        public static void RecordRotateEnd(float degrees) { }
        public static void RecordTwoFingerStart(Vector2 pos1, Vector2 pos2) { }
        public static void RecordTwoFingerEnd(Vector2 end1, Vector2 end2) { }
        public static void RecordText(string text, Vector2 position) { }

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            LoadIcons();
            CreateDrawingTextures();
        }

        private void OnDisable()
        {
            CleanupTextures();
        }

        private void LoadIcons()
        {
            _mouseCursorIcon = Resources.Load<Texture2D>("mouse-cursor");
            _mouseClickIcon = Resources.Load<Texture2D>("mouse-click");
            _touchIcon = Resources.Load<Texture2D>("touch");
            _touchTapIcon = Resources.Load<Texture2D>("touch-tap");
            _scrollUpIcon = Resources.Load<Texture2D>("scroll-up");
            _scrollDownIcon = Resources.Load<Texture2D>("scroll-down");
        }

        private void CreateDrawingTextures()
        {
            // Create a simple white pixel texture for drawing lines
            _pixelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _pixelTexture.SetPixel(0, 0, Color.white);
            _pixelTexture.Apply();
            _pixelTexture.hideFlags = HideFlags.HideAndDontSave;

            // Create a circle texture for ripples
            int size = 64;
            _circleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size / 2f;
            float radius = size / 2f - 2f;
            float thickness = 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float alpha = 0f;

                    // Ring shape
                    if (dist >= radius - thickness && dist <= radius + thickness)
                    {
                        alpha = 1f - Mathf.Abs(dist - radius) / thickness;
                    }

                    _circleTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            _circleTexture.Apply();
            _circleTexture.hideFlags = HideFlags.HideAndDontSave;
        }

        private void CleanupTextures()
        {
            if (_pixelTexture != null)
            {
                DestroyImmediate(_pixelTexture);
                _pixelTexture = null;
            }
            if (_circleTexture != null)
            {
                DestroyImmediate(_circleTexture);
                _circleTexture = null;
            }
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            _clicks.RemoveAll(c => now - c.StartTime > ClickDuration);
            _scrolls.RemoveAll(s => now - s.StartTime > ScrollDuration);
            _trail.RemoveAll(t => now - t.Time > TrailDuration);
        }

        #endregion

        #region OnGUI Rendering

        private void OnGUI()
        {
            if (!_enabled) return;

            bool hasEvents = _clicks.Count > 0 || _trail.Count > 0 ||
                             _activeCursor.HasValue || _scrolls.Count > 0;

            if (!hasEvents) return;

            // Draw in screen space - OnGUI uses top-left origin, so we need to flip Y
            DrawTrail();
            DrawClicks();
            DrawScrollIcons();
            DrawCursor();
        }

        /// <summary>Convert screen position (bottom-left origin) to GUI position (top-left origin).</summary>
        private Vector2 ScreenToGUI(Vector2 screenPos)
        {
            return new Vector2(screenPos.x, Screen.height - screenPos.y);
        }

        private void DrawTrail()
        {
            if (_trail.Count < 2 || _pixelTexture == null) return;

            float now = Time.unscaledTime;

            // Draw trail as connected lines
            for (int i = 0; i < _trail.Count - 1; i++)
            {
                var p1 = _trail[i];
                var p2 = _trail[i + 1];

                float age = now - p2.Time;
                float alpha = Mathf.Clamp01(1f - age / TrailDuration) * TrailColor.a;
                float posAlpha = (float)(i + 1) / _trail.Count;
                alpha *= posAlpha;

                if (alpha > 0.05f)
                {
                    var color = TrailColor;
                    color.a = alpha;
                    float thickness = 1f + posAlpha * 2f;
                    DrawLine(ScreenToGUI(p1.Position), ScreenToGUI(p2.Position), color, thickness);
                }
            }

            // Draw trail dots
            for (int i = 0; i < _trail.Count; i += 2)
            {
                var p = _trail[i];
                float age = now - p.Time;
                float alpha = Mathf.Clamp01(1f - age / TrailDuration) * TrailColor.a;
                float posAlpha = (float)i / _trail.Count;
                alpha *= posAlpha;

                if (alpha > 0.05f)
                {
                    var color = TrailColor;
                    color.a = alpha;
                    float radius = 2f + posAlpha * 4f;
                    DrawFilledCircle(ScreenToGUI(p.Position), radius, color);
                }
            }
        }

        private void DrawClicks()
        {
            if (_circleTexture == null) return;

            float now = Time.unscaledTime;

            foreach (var click in _clicks)
            {
                float t = (now - click.StartTime) / ClickDuration;
                float alpha = Mathf.Clamp01(1f - t) * ClickColor.a;
                float radius = Mathf.Lerp(8f, ClickRadius, t);

                var color = ClickColor;
                color.a = alpha;

                var guiPos = ScreenToGUI(click.Position);

                for (int ring = 0; ring < click.TapCount; ring++)
                {
                    float ringRadius = radius - ring * 6f;
                    if (ringRadius > 0)
                    {
                        DrawRing(guiPos, ringRadius, color);
                    }
                }
            }
        }

        private void DrawScrollIcons()
        {
            float now = Time.unscaledTime;

            foreach (var scroll in _scrolls)
            {
                float t = (now - scroll.StartTime) / ScrollDuration;
                float alpha = 1f - t;
                if (alpha <= 0) continue;

                bool scrollUp = scroll.Direction.y > 0;
                var icon = scrollUp ? _scrollUpIcon : _scrollDownIcon;

                if (icon != null)
                {
                    float size = CursorSize * (1f + t * 0.2f);
                    float yOffset = t * 15f * (scrollUp ? -1f : 1f); // Flip for GUI coords
                    var guiPos = ScreenToGUI(scroll.Position) + new Vector2(0, yOffset);
                    DrawIcon(icon, guiPos, size, alpha);
                }
            }
        }

        private void DrawCursor()
        {
            if (!_activeCursor.HasValue) return;

            var cursor = _activeCursor.Value;
            Texture2D icon;

            if (cursor.IsMouse)
            {
                icon = cursor.IsPressed ? _mouseClickIcon : _mouseCursorIcon;
            }
            else
            {
                icon = cursor.IsPressed ? _touchTapIcon : _touchIcon;
            }

            if (icon != null)
            {
                var guiPos = ScreenToGUI(cursor.Position);
                DrawIcon(icon, guiPos, CursorSize, 1f);
            }
        }

        #endregion

        #region Drawing Primitives

        private void DrawIcon(Texture2D icon, Vector2 guiPos, float size, float alpha)
        {
            if (icon == null) return;

            float aspect = (float)icon.width / icon.height;
            float width = size * aspect;
            float height = size;

            // Position with hotspot at top-left
            var rect = new Rect(guiPos.x, guiPos.y, width, height);

            var oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit, true);
            GUI.color = oldColor;
        }

        private void DrawLine(Vector2 p1, Vector2 p2, Color color, float thickness)
        {
            if (_pixelTexture == null) return;

            var oldColor = GUI.color;
            GUI.color = color;

            var delta = p2 - p1;
            float length = delta.magnitude;
            if (length < 0.1f) return;

            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

            var matrixBackup = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, p1);

            GUI.DrawTexture(new Rect(p1.x, p1.y - thickness / 2f, length, thickness), _pixelTexture);

            GUI.matrix = matrixBackup;
            GUI.color = oldColor;
        }

        private void DrawRing(Vector2 center, float radius, Color color)
        {
            if (_circleTexture == null) return;

            var oldColor = GUI.color;
            GUI.color = color;

            float size = radius * 2f;
            var rect = new Rect(center.x - radius, center.y - radius, size, size);
            GUI.DrawTexture(rect, _circleTexture, ScaleMode.ScaleToFit, true);

            GUI.color = oldColor;
        }

        private void DrawFilledCircle(Vector2 center, float radius, Color color)
        {
            if (_pixelTexture == null) return;

            var oldColor = GUI.color;
            GUI.color = color;

            // Approximate circle with a square texture
            float size = radius * 2f;
            var rect = new Rect(center.x - radius, center.y - radius, size, size);
            GUI.DrawTexture(rect, _pixelTexture, ScaleMode.ScaleToFit, true);

            GUI.color = oldColor;
        }

        #endregion
    }
}
