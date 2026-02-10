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
            public float FadeStartTime; // When cursor started fading (0 = not fading)
        }

        private struct GestureState
        {
            public enum GestureType { Pinch, Rotate, TwoFingerDrag }
            public GestureType Type;
            public Vector2 Center;
            public Vector2 Finger1;
            public Vector2 Finger2;
            public float StartTime;
            public bool IsActive;
            public float FadeStartTime;
        }

        private struct KeyEvent
        {
            public string KeyName;
            public float StartTime;
            public bool IsHold;
            public bool IsActive; // For holds - true while held
        }

        private struct TextEvent
        {
            public string Text;
            public Vector2 Position;
            public float StartTime;
        }

        /// <summary>Duration in seconds for cursor to fade out after release.</summary>
        public static float CursorFadeDuration = 1.5f;

        /// <summary>Duration in seconds for gesture visualization to fade after completion.</summary>
        public static float GestureFadeDuration = 0.5f;

        /// <summary>Duration in seconds for key press label to display.</summary>
        public static float KeyDisplayDuration = 0.8f;

        /// <summary>Duration in seconds for text input label to display.</summary>
        public static float TextDisplayDuration = 1.0f;

        /// <summary>Color for gesture finger dots and lines.</summary>
        public static Color GestureColor = new Color(1f, 0.6f, 0.2f, 0.7f);

        /// <summary>Color for key press labels.</summary>
        public static Color KeyColor = new Color(1f, 1f, 0.4f, 0.9f);

        private readonly List<ClickEvent> _clicks = new();
        private readonly List<TrailPoint> _trail = new();
        private readonly List<ScrollEvent> _scrolls = new();
        private readonly List<KeyEvent> _keys = new();
        private readonly List<TextEvent> _texts = new();
        private CursorState? _activeCursor;
        private GestureState? _activeGesture;

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

        /// <summary>Starts fading out the active cursor.</summary>
        public static void RecordCursorEnd()
        {
            if (!_enabled || _instance == null) return;
            if (!_instance._activeCursor.HasValue) return;

            // Start fading instead of immediately hiding
            var cursor = _instance._activeCursor.Value;
            cursor.IsActive = false;
            cursor.FadeStartTime = Time.unscaledTime;
            _instance._activeCursor = cursor;
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

        // Drag visualization
        public static void RecordDragStart(Vector2 screenPosition) => RecordCursorPosition(screenPosition, false, 0, true);
        public static void RecordDragPoint(Vector2 screenPosition) => RecordCursorPosition(screenPosition, false, 0, true);
        public static void RecordDragEnd(Vector2 screenPosition) => RecordCursorEnd();

        // Hold visualization
        public static void RecordHoldStart(Vector2 screenPosition) => RecordCursorPosition(screenPosition, false, 0, true);
        public static void RecordHoldEnd() => RecordCursorEnd();

        /// <summary>Records a key press for visualization.</summary>
        public static void RecordKeyPress(string keyName)
        {
            if (!_enabled) return;
            EnsureInstance();
            if (_instance == null) return;

            _instance._keys.Add(new KeyEvent
            {
                KeyName = keyName,
                StartTime = Time.unscaledTime,
                IsHold = false,
                IsActive = false
            });
        }

        /// <summary>Records the start of a key hold for visualization.</summary>
        public static void RecordKeyHoldStart(string keyName)
        {
            if (!_enabled) return;
            EnsureInstance();
            if (_instance == null) return;

            _instance._keys.Add(new KeyEvent
            {
                KeyName = keyName,
                StartTime = Time.unscaledTime,
                IsHold = true,
                IsActive = true
            });
        }

        /// <summary>Marks the active key hold as ended.</summary>
        public static void RecordKeyHoldEnd()
        {
            if (!_enabled || _instance == null) return;

            for (int i = _instance._keys.Count - 1; i >= 0; i--)
            {
                var key = _instance._keys[i];
                if (key.IsHold && key.IsActive)
                {
                    key.IsActive = false;
                    key.StartTime = Time.unscaledTime; // Reset timer for fade-out
                    _instance._keys[i] = key;
                    break;
                }
            }
        }

        /// <summary>Records a pinch gesture start with two finger positions.</summary>
        public static void RecordPinchStart(Vector2 center, float fingerDistance)
        {
            if (!_enabled) return;
            EnsureInstance();
            if (_instance == null) return;

            _instance._activeGesture = new GestureState
            {
                Type = GestureState.GestureType.Pinch,
                Center = center,
                Finger1 = center + new Vector2(-fingerDistance / 2f, 0),
                Finger2 = center + new Vector2(fingerDistance / 2f, 0),
                StartTime = Time.unscaledTime,
                IsActive = true
            };
        }

        /// <summary>Updates the active pinch gesture finger positions.</summary>
        public static void RecordPinchUpdate(Vector2 finger1, Vector2 finger2)
        {
            if (!_enabled || _instance == null || !_instance._activeGesture.HasValue) return;

            var gesture = _instance._activeGesture.Value;
            gesture.Finger1 = finger1;
            gesture.Finger2 = finger2;
            gesture.Center = (finger1 + finger2) / 2f;
            _instance._activeGesture = gesture;
        }

        /// <summary>Ends the active pinch gesture.</summary>
        public static void RecordPinchEnd(float endDistance)
        {
            EndActiveGesture();
        }

        /// <summary>Records a rotate gesture start.</summary>
        public static void RecordRotateStart(Vector2 center, float radius)
        {
            if (!_enabled) return;
            EnsureInstance();
            if (_instance == null) return;

            _instance._activeGesture = new GestureState
            {
                Type = GestureState.GestureType.Rotate,
                Center = center,
                Finger1 = center + new Vector2(-radius, 0),
                Finger2 = center + new Vector2(radius, 0),
                StartTime = Time.unscaledTime,
                IsActive = true
            };
        }

        /// <summary>Updates the active rotate gesture finger positions.</summary>
        public static void RecordRotateUpdate(Vector2 finger1, Vector2 finger2)
        {
            if (!_enabled || _instance == null || !_instance._activeGesture.HasValue) return;

            var gesture = _instance._activeGesture.Value;
            gesture.Finger1 = finger1;
            gesture.Finger2 = finger2;
            _instance._activeGesture = gesture;
        }

        /// <summary>Ends the active rotate gesture.</summary>
        public static void RecordRotateEnd(float degrees)
        {
            EndActiveGesture();
        }

        /// <summary>Records a two-finger drag start.</summary>
        public static void RecordTwoFingerStart(Vector2 pos1, Vector2 pos2)
        {
            if (!_enabled) return;
            EnsureInstance();
            if (_instance == null) return;

            _instance._activeGesture = new GestureState
            {
                Type = GestureState.GestureType.TwoFingerDrag,
                Center = (pos1 + pos2) / 2f,
                Finger1 = pos1,
                Finger2 = pos2,
                StartTime = Time.unscaledTime,
                IsActive = true
            };
        }

        /// <summary>Updates the active two-finger drag positions.</summary>
        public static void RecordTwoFingerUpdate(Vector2 pos1, Vector2 pos2)
        {
            if (!_enabled || _instance == null || !_instance._activeGesture.HasValue) return;

            var gesture = _instance._activeGesture.Value;
            gesture.Finger1 = pos1;
            gesture.Finger2 = pos2;
            gesture.Center = (pos1 + pos2) / 2f;
            _instance._activeGesture = gesture;
        }

        /// <summary>Ends the active two-finger drag.</summary>
        public static void RecordTwoFingerEnd(Vector2 end1, Vector2 end2)
        {
            EndActiveGesture();
        }

        /// <summary>Records text input for visualization.</summary>
        public static void RecordText(string text, Vector2 position)
        {
            if (!_enabled) return;
            EnsureInstance();
            if (_instance == null) return;

            _instance._texts.Add(new TextEvent
            {
                Text = text,
                Position = position,
                StartTime = Time.unscaledTime
            });
        }

        private static void EndActiveGesture()
        {
            if (_instance == null || !_instance._activeGesture.HasValue) return;

            var gesture = _instance._activeGesture.Value;
            gesture.IsActive = false;
            gesture.FadeStartTime = Time.unscaledTime;
            _instance._activeGesture = gesture;
        }

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
            _keys.RemoveAll(k => !k.IsActive && now - k.StartTime > KeyDisplayDuration);
            _texts.RemoveAll(t => now - t.StartTime > TextDisplayDuration);

            // Clear cursor when fade completes
            if (_activeCursor.HasValue && !_activeCursor.Value.IsActive)
            {
                if (now - _activeCursor.Value.FadeStartTime > CursorFadeDuration)
                    _activeCursor = null;
            }

            // Clear gesture when fade completes
            if (_activeGesture.HasValue && !_activeGesture.Value.IsActive)
            {
                if (now - _activeGesture.Value.FadeStartTime > GestureFadeDuration)
                    _activeGesture = null;
            }
        }

        #endregion

        #region OnGUI Rendering

        private void OnGUI()
        {
            if (!_enabled) return;

            bool hasEvents = _clicks.Count > 0 || _trail.Count > 0 ||
                             _activeCursor.HasValue || _scrolls.Count > 0 ||
                             _activeGesture.HasValue || _keys.Count > 0 || _texts.Count > 0;

            if (!hasEvents) return;

            // Draw in screen space - OnGUI uses top-left origin, so we need to flip Y
            DrawTrail();
            DrawClicks();
            DrawScrollIcons();
            DrawGesture();
            DrawKeys();
            DrawTexts();
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
                    DrawIcon(icon, guiPos, size, alpha, new Vector2(0.5f, 0.5f));
                }
            }
        }

        private void DrawCursor()
        {
            if (!_activeCursor.HasValue) return;

            var cursor = _activeCursor.Value;
            Texture2D icon;
            Vector2 hotspot;

            if (cursor.IsMouse)
            {
                if (cursor.IsPressed)
                {
                    icon = _mouseClickIcon;
                    hotspot = new Vector2(0.5f, 0.5f); // Starburst centered on click point
                }
                else
                {
                    icon = _mouseCursorIcon;
                    hotspot = new Vector2(0.15f, 0.05f); // Arrow tip near top-left
                }
            }
            else
            {
                icon = cursor.IsPressed ? _touchTapIcon : _touchIcon;
                hotspot = new Vector2(0.42f, 0.08f); // Fingertip near top-center
            }

            if (icon != null)
            {
                var guiPos = ScreenToGUI(cursor.Position);

                // Calculate alpha based on fade state
                float alpha = 1f;
                if (!cursor.IsActive && cursor.FadeStartTime > 0)
                {
                    float fadeProgress = (Time.unscaledTime - cursor.FadeStartTime) / CursorFadeDuration;
                    alpha = Mathf.Clamp01(1f - fadeProgress);
                }

                DrawIcon(icon, guiPos, CursorSize, alpha, hotspot);
            }
        }

        private void DrawGesture()
        {
            if (!_activeGesture.HasValue || _pixelTexture == null) return;

            var gesture = _activeGesture.Value;
            float alpha = 1f;

            if (!gesture.IsActive && gesture.FadeStartTime > 0)
            {
                float fadeProgress = (Time.unscaledTime - gesture.FadeStartTime) / GestureFadeDuration;
                alpha = Mathf.Clamp01(1f - fadeProgress);
            }

            if (alpha <= 0) return;

            var color = GestureColor;
            color.a *= alpha;

            var guiF1 = ScreenToGUI(gesture.Finger1);
            var guiF2 = ScreenToGUI(gesture.Finger2);
            var guiCenter = ScreenToGUI(gesture.Center);

            // Draw finger dots
            float dotRadius = 8f;
            DrawFilledCircle(guiF1, dotRadius, color);
            DrawFilledCircle(guiF2, dotRadius, color);

            // Draw lines from fingers to center
            var lineColor = color;
            lineColor.a *= 0.5f;
            DrawLine(guiF1, guiCenter, lineColor, 2f);
            DrawLine(guiF2, guiCenter, lineColor, 2f);

            // Draw center marker
            DrawRing(guiCenter, 6f, lineColor);

            // Draw gesture type label
            string label = gesture.Type switch
            {
                GestureState.GestureType.Pinch => "PINCH",
                GestureState.GestureType.Rotate => "ROTATE",
                GestureState.GestureType.TwoFingerDrag => "2-FINGER",
                _ => ""
            };

            if (label.Length > 0)
            {
                var labelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold
                };
                labelStyle.normal.textColor = new Color(1f, 1f, 1f, alpha * 0.8f);

                var labelRect = new Rect(guiCenter.x - 40, guiCenter.y - 25, 80, 20);
                GUI.Label(labelRect, label, labelStyle);
            }
        }

        private void DrawKeys()
        {
            if (_keys.Count == 0) return;

            float now = Time.unscaledTime;
            float yOffset = 10f;

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };

            foreach (var key in _keys)
            {
                float alpha;
                if (key.IsActive)
                {
                    // Currently held - pulse effect
                    float pulse = 0.7f + 0.3f * Mathf.Sin((now - key.StartTime) * 4f);
                    alpha = pulse;
                }
                else
                {
                    float t = (now - key.StartTime) / KeyDisplayDuration;
                    alpha = Mathf.Clamp01(1f - t);
                }

                if (alpha <= 0) continue;

                string label = key.IsHold && key.IsActive
                    ? $"[{key.KeyName}] (hold)"
                    : $"[{key.KeyName}]";

                style.normal.textColor = new Color(KeyColor.r, KeyColor.g, KeyColor.b, alpha);
                var rect = new Rect(10, yOffset, 200, 22);
                GUI.Label(rect, label, style);
                yOffset += 24f;
            }
        }

        private void DrawTexts()
        {
            if (_texts.Count == 0) return;

            float now = Time.unscaledTime;

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };

            foreach (var textEvent in _texts)
            {
                float t = (now - textEvent.StartTime) / TextDisplayDuration;
                float alpha = Mathf.Clamp01(1f - t);
                if (alpha <= 0) continue;

                var guiPos = ScreenToGUI(textEvent.Position);
                style.normal.textColor = new Color(1f, 1f, 1f, alpha);

                var rect = new Rect(guiPos.x - 100, guiPos.y - 30, 200, 20);
                GUI.Label(rect, $"\"{textEvent.Text}\"", style);
            }
        }

        #endregion

        #region Drawing Primitives

        /// <summary>
        /// Draws an icon at the given GUI position.
        /// hotspotNormalized defines where the "point" of the icon is in normalized coords (0,0 = top-left, 0.5,0.5 = center).
        /// </summary>
        private void DrawIcon(Texture2D icon, Vector2 guiPos, float size, float alpha, Vector2 hotspotNormalized = default)
        {
            if (icon == null) return;

            float aspect = (float)icon.width / icon.height;
            float width = size * aspect;
            float height = size;

            // Offset so the hotspot aligns with guiPos
            var rect = new Rect(
                guiPos.x - width * hotspotNormalized.x,
                guiPos.y - height * hotspotNormalized.y,
                width, height);

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
