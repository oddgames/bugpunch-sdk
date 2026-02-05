using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ODDGames.UIAutomation
{
    /// <summary>
    /// Runtime input visualizer that draws input events directly on screen using GL.
    /// Works over entire screen regardless of canvas setup, UI layers, or camera configuration.
    /// Automatically attaches to main camera and draws after all rendering.
    /// </summary>
    public class InputVisualizer : MonoBehaviour
    {
        #region Singleton

        private static InputVisualizer _instance;
        private static bool _enabled = true;

        /// <summary>
        /// Enables or disables input visualization globally.
        /// When enabled, creates a visualizer attached to the main camera.
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
        public static float ClickDuration = 0.4f;

        /// <summary>Maximum radius for click ripple in pixels.</summary>
        public static float ClickRadius = 30f;

        /// <summary>Duration in seconds for drag trail to fade.</summary>
        public static float DragFadeDuration = 0.5f;

        /// <summary>Duration in seconds for scroll indicator.</summary>
        public static float ScrollDuration = 0.3f;

        /// <summary>Duration in seconds for key press indicator.</summary>
        public static float KeyDuration = 0.5f;

        /// <summary>Color for click events (cyan, semi-transparent).</summary>
        public static Color ClickColor = new Color(0.2f, 0.8f, 1f, 0.7f);

        /// <summary>Color for drag events (orange, semi-transparent).</summary>
        public static Color DragColor = new Color(1f, 0.6f, 0.2f, 0.7f);

        /// <summary>Color for scroll events (green, semi-transparent).</summary>
        public static Color ScrollColor = new Color(0.5f, 1f, 0.5f, 0.7f);

        /// <summary>Color for key press events (yellow, semi-transparent).</summary>
        public static Color KeyColor = new Color(1f, 1f, 0.4f, 0.7f);

        /// <summary>Color for hold events (magenta, semi-transparent).</summary>
        public static Color HoldColor = new Color(1f, 0.4f, 1f, 0.7f);

        /// <summary>Color for pinch events (purple, semi-transparent).</summary>
        public static Color PinchColor = new Color(0.7f, 0.4f, 1f, 0.7f);

        /// <summary>Color for rotate events (teal, semi-transparent).</summary>
        public static Color RotateColor = new Color(0.3f, 0.9f, 0.9f, 0.7f);

        /// <summary>Color for two-finger events (pink, semi-transparent).</summary>
        public static Color TwoFingerColor = new Color(1f, 0.5f, 0.7f, 0.7f);

        /// <summary>Color for text typing events (white, semi-transparent).</summary>
        public static Color TextColor = new Color(1f, 1f, 1f, 0.7f);

        /// <summary>Duration in seconds for hold indicator.</summary>
        public static float HoldDuration = 0.5f;

        /// <summary>Duration in seconds for pinch indicator.</summary>
        public static float PinchDuration = 0.5f;

        /// <summary>Duration in seconds for rotate indicator.</summary>
        public static float RotateDuration = 0.5f;

        /// <summary>Duration in seconds for two-finger gesture indicator.</summary>
        public static float TwoFingerDuration = 0.5f;

        /// <summary>Duration in seconds for text display.</summary>
        public static float TextDuration = 1.0f;

        #endregion

        #region Event Data

        private struct ClickEvent
        {
            public Vector2 Position;
            public float StartTime;
            public int TapCount; // 1 = single, 2 = double, 3 = triple
            public int PointerIndex; // Touch finger index (0 = first finger, -1 = mouse)
        }

        private struct DragEvent
        {
            public List<Vector2> Points;
            public float EndTime;
            public bool IsComplete;
        }

        private struct ScrollEvent
        {
            public Vector2 Position;
            public Vector2 Direction;
            public float StartTime;
        }

        private struct KeyEvent
        {
            public string KeyName;
            public float StartTime;
            public Vector2 Position; // Bottom center of screen
            public bool IsHold;
            public float HoldEndTime;
        }

        private struct HoldEvent
        {
            public Vector2 Position;
            public float StartTime;
            public float EndTime;
            public bool IsComplete;
        }

        private struct PinchEvent
        {
            public Vector2 Center;
            public float StartDistance;
            public float EndDistance;
            public float StartTime;
            public float EndTime;
            public bool IsComplete;
        }

        private struct RotateEvent
        {
            public Vector2 Center;
            public float Degrees;
            public float Radius;
            public float StartTime;
            public float EndTime;
            public bool IsComplete;
        }

        private struct TwoFingerEvent
        {
            public Vector2 Start1;
            public Vector2 End1;
            public Vector2 Start2;
            public Vector2 End2;
            public float StartTime;
            public float EndTime;
            public bool IsComplete;
        }

        private struct TextEvent
        {
            public string Text;
            public Vector2 Position;
            public float StartTime;
        }

        private readonly List<ClickEvent> _clicks = new();
        private readonly List<DragEvent> _drags = new();
        private readonly List<ScrollEvent> _scrolls = new();
        private readonly List<KeyEvent> _keys = new();
        private readonly List<HoldEvent> _holds = new();
        private readonly List<PinchEvent> _pinches = new();
        private readonly List<RotateEvent> _rotates = new();
        private readonly List<TwoFingerEvent> _twoFingers = new();
        private readonly List<TextEvent> _texts = new();
        private DragEvent? _activeDrag;
        private HoldEvent? _activeHold;
        private PinchEvent? _activePinch;
        private RotateEvent? _activeRotate;
        private TwoFingerEvent? _activeTwoFinger;
        private KeyEvent? _activeKeyHold;

        private Material _glMaterial;
        private Camera _attachedCamera;

        #endregion

        #region Public API - Called by InputInjector

        /// <summary>Records a click/tap event for visualization.</summary>
        /// <param name="screenPosition">Screen position of the click</param>
        /// <param name="tapCount">1 = single, 2 = double, 3 = triple click</param>
        /// <param name="pointerIndex">Touch finger index (0-based), or -1 for mouse</param>
        public static void RecordClick(Vector2 screenPosition, int tapCount = 1, int pointerIndex = -1)
        {
            if (!_enabled) return;
            EnsureInstance();
            if (_instance == null) return;


            _instance._clicks.Add(new ClickEvent
            {
                Position = screenPosition,
                StartTime = Time.time,
                TapCount = tapCount,
                PointerIndex = pointerIndex
            });
        }

        /// <summary>Records the start of a drag event.</summary>
        public static void RecordDragStart(Vector2 screenPosition)
        {
            if (!_enabled) return;
            EnsureInstance();
            if (_instance == null) return;
            _instance._activeDrag = new DragEvent
            {
                Points = new List<Vector2> { screenPosition },
                IsComplete = false
            };
        }

        /// <summary>Records a point during an ongoing drag.</summary>
        public static void RecordDragPoint(Vector2 screenPosition)
        {
            if (!_enabled || _instance == null || !_instance._activeDrag.HasValue) return;
            var drag = _instance._activeDrag.Value;
            drag.Points.Add(screenPosition);
            _instance._activeDrag = drag;
        }

        /// <summary>Records the end of a drag event.</summary>
        public static void RecordDragEnd(Vector2 screenPosition)
        {
            if (!_enabled || _instance == null || !_instance._activeDrag.HasValue) return;
            var drag = _instance._activeDrag.Value;
            drag.Points.Add(screenPosition);
            drag.EndTime = Time.time;
            drag.IsComplete = true;
            _instance._drags.Add(drag);
            _instance._activeDrag = null;
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
                StartTime = Time.time
            });
        }

        /// <summary>Records a key press event.</summary>
        public static void RecordKeyPress(string keyName)
        {
            if (!_enabled) return;
            EnsureInstance();
            if (_instance == null) return;

            // Stack keys horizontally at bottom of screen
            float xOffset = (_instance._keys.Count % 8) * 70f;
            _instance._keys.Add(new KeyEvent
            {
                KeyName = keyName,
                StartTime = Time.time,
                Position = new Vector2(Screen.width / 2f - 245f + xOffset, 50f),
                IsHold = false
            });
        }

        /// <summary>Records a key hold start event.</summary>
        public static void RecordKeyHoldStart(string keyName)
        {
            if (!_enabled) return;
            EnsureInstance();
            if (_instance == null) return;

            float xOffset = (_instance._keys.Count % 8) * 70f;
            _instance._activeKeyHold = new KeyEvent
            {
                KeyName = keyName,
                StartTime = Time.time,
                Position = new Vector2(Screen.width / 2f - 245f + xOffset, 50f),
                IsHold = true
            };
        }

        /// <summary>Records a key hold end event.</summary>
        public static void RecordKeyHoldEnd()
        {
            if (!_enabled || _instance == null || !_instance._activeKeyHold.HasValue) return;
            var keyHold = _instance._activeKeyHold.Value;
            keyHold.HoldEndTime = Time.time;
            _instance._keys.Add(keyHold);
            _instance._activeKeyHold = null;
        }

        /// <summary>Records a pointer/touch hold start event.</summary>
        public static void RecordHoldStart(Vector2 screenPosition)
        {
            if (!_enabled) return;
            EnsureInstance();
            if (_instance == null) return;
            _instance._activeHold = new HoldEvent
            {
                Position = screenPosition,
                StartTime = Time.time,
                IsComplete = false
            };
        }

        /// <summary>Records a pointer/touch hold end event.</summary>
        public static void RecordHoldEnd()
        {
            if (!_enabled || _instance == null || !_instance._activeHold.HasValue) return;
            var hold = _instance._activeHold.Value;
            hold.EndTime = Time.time;
            hold.IsComplete = true;
            _instance._holds.Add(hold);
            _instance._activeHold = null;
        }

        /// <summary>Records a pinch gesture start.</summary>
        public static void RecordPinchStart(Vector2 center, float fingerDistance)
        {
            if (!_enabled) return;
            EnsureInstance();
            if (_instance == null) return;
            _instance._activePinch = new PinchEvent
            {
                Center = center,
                StartDistance = fingerDistance,
                EndDistance = fingerDistance,
                StartTime = Time.time,
                IsComplete = false
            };
        }

        /// <summary>Records a pinch gesture end.</summary>
        public static void RecordPinchEnd(float endDistance)
        {
            if (!_enabled || _instance == null || !_instance._activePinch.HasValue) return;
            var pinch = _instance._activePinch.Value;
            pinch.EndDistance = endDistance;
            pinch.EndTime = Time.time;
            pinch.IsComplete = true;
            _instance._pinches.Add(pinch);
            _instance._activePinch = null;
        }

        /// <summary>Records a rotate gesture start.</summary>
        public static void RecordRotateStart(Vector2 center, float radius)
        {
            if (!_enabled) return;
            EnsureInstance();
            if (_instance == null) return;
            _instance._activeRotate = new RotateEvent
            {
                Center = center,
                Degrees = 0,
                Radius = radius,
                StartTime = Time.time,
                IsComplete = false
            };
        }

        /// <summary>Records a rotate gesture end.</summary>
        public static void RecordRotateEnd(float degrees)
        {
            if (!_enabled || _instance == null || !_instance._activeRotate.HasValue) return;
            var rotate = _instance._activeRotate.Value;
            rotate.Degrees = degrees;
            rotate.EndTime = Time.time;
            rotate.IsComplete = true;
            _instance._rotates.Add(rotate);
            _instance._activeRotate = null;
        }

        /// <summary>Records a two-finger swipe/drag start.</summary>
        public static void RecordTwoFingerStart(Vector2 pos1, Vector2 pos2)
        {
            if (!_enabled) return;
            EnsureInstance();
            if (_instance == null) return;
            _instance._activeTwoFinger = new TwoFingerEvent
            {
                Start1 = pos1,
                End1 = pos1,
                Start2 = pos2,
                End2 = pos2,
                StartTime = Time.time,
                IsComplete = false
            };
        }

        /// <summary>Records a two-finger swipe/drag end.</summary>
        public static void RecordTwoFingerEnd(Vector2 end1, Vector2 end2)
        {
            if (!_enabled || _instance == null || !_instance._activeTwoFinger.HasValue) return;
            var twoFinger = _instance._activeTwoFinger.Value;
            twoFinger.End1 = end1;
            twoFinger.End2 = end2;
            twoFinger.EndTime = Time.time;
            twoFinger.IsComplete = true;
            _instance._twoFingers.Add(twoFinger);
            _instance._activeTwoFinger = null;
        }

        /// <summary>Records text being typed.</summary>
        public static void RecordText(string text, Vector2 position)
        {
            if (!_enabled) return;
            EnsureInstance();
            if (_instance == null) return;
            _instance._texts.Add(new TextEvent
            {
                Text = text,
                Position = position,
                StartTime = Time.time
            });
        }

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            // Built-in render pipeline
            Camera.onPostRender += OnCameraPostRender;
            // Scriptable render pipeline (URP/HDRP)
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        private void OnDisable()
        {
            Camera.onPostRender -= OnCameraPostRender;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            if (_glMaterial != null)
            {
                DestroyImmediate(_glMaterial);
                _glMaterial = null;
            }
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            OnCameraPostRender(cam);
        }

        private void Update()
        {
            // Cleanup expired events
            float now = Time.time;

            _clicks.RemoveAll(c => now - c.StartTime > ClickDuration);
            _drags.RemoveAll(d => d.IsComplete && now - d.EndTime > DragFadeDuration);
            _scrolls.RemoveAll(s => now - s.StartTime > ScrollDuration);
            _keys.RemoveAll(k => !k.IsHold && now - k.StartTime > KeyDuration);
            _keys.RemoveAll(k => k.IsHold && k.HoldEndTime > 0 && now - k.HoldEndTime > KeyDuration);
            _holds.RemoveAll(h => h.IsComplete && now - h.EndTime > HoldDuration);
            _pinches.RemoveAll(p => p.IsComplete && now - p.EndTime > PinchDuration);
            _rotates.RemoveAll(r => r.IsComplete && now - r.EndTime > RotateDuration);
            _twoFingers.RemoveAll(t => t.IsComplete && now - t.EndTime > TwoFingerDuration);
            _texts.RemoveAll(t => now - t.StartTime > TextDuration);
        }

        #endregion

        #region GL Rendering

        private Material GetMaterial()
        {
            if (_glMaterial == null)
            {
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

        private void OnCameraPostRender(Camera cam)
        {
            if (cam.cameraType != CameraType.Game) return;

            // Skip if no events to draw
            bool hasEvents = _clicks.Count > 0 || _drags.Count > 0 || _activeDrag.HasValue ||
                _scrolls.Count > 0 || _keys.Count > 0 || _activeKeyHold.HasValue ||
                _holds.Count > 0 || _activeHold.HasValue ||
                _pinches.Count > 0 || _activePinch.HasValue ||
                _rotates.Count > 0 || _activeRotate.HasValue ||
                _twoFingers.Count > 0 || _activeTwoFinger.HasValue ||
                _texts.Count > 0;

            if (!hasEvents) return;

            var mat = GetMaterial();
            if (mat == null)
            {
                Debug.LogWarning("[InputVisualizer] Material is null!");
                return;
            }

            mat.SetPass(0);

            GL.PushMatrix();
            GL.LoadPixelMatrix();

            DrawClicks();
            DrawDrags();
            DrawScrolls();
            DrawKeys();
            DrawHolds();
            DrawPinches();
            DrawRotates();
            DrawTwoFingers();
            DrawTexts();

            GL.PopMatrix();
        }

        private void DrawClicks()
        {
            float now = Time.time;

            foreach (var click in _clicks)
            {
                float t = (now - click.StartTime) / ClickDuration;
                float alpha = Mathf.Clamp01(1f - t) * ClickColor.a;
                float radius = Mathf.Lerp(5f, ClickRadius, t);

                var color = ClickColor;
                color.a = alpha;

                // Draw expanding ring(s) - one per tap
                for (int ring = 0; ring < click.TapCount; ring++)
                {
                    float ringRadius = radius - ring * 8f;
                    if (ringRadius > 0)
                        DrawCircle(click.Position.x, click.Position.y, ringRadius, color, 2f);
                }

                // Draw center with pointer info
                if (alpha > 0.2f)
                {
                    color.a = alpha;

                    // For touch, show finger number; for mouse, show filled dot
                    if (click.PointerIndex >= 0)
                    {
                        // Touch - draw finger index indicator
                        DrawFilledCircle(click.Position.x, click.Position.y, 12f, color);

                        // Draw dots for finger number (1 dot = finger 0, 2 dots = finger 1, etc.)
                        var dotColor = new Color(0, 0, 0, alpha);
                        int fingerNum = click.PointerIndex + 1;
                        float dotSpacing = 6f;
                        float startX = click.Position.x - (fingerNum - 1) * dotSpacing / 2f;
                        for (int i = 0; i < fingerNum && i < 5; i++)
                        {
                            DrawFilledCircle(startX + i * dotSpacing, click.Position.y, 2f, dotColor);
                        }
                    }
                    else
                    {
                        // Mouse - simple filled dot
                        DrawFilledCircle(click.Position.x, click.Position.y, 6f, color);
                    }
                }
            }
        }

        private void DrawDrags()
        {
            float now = Time.time;

            // Draw active drag
            if (_activeDrag.HasValue)
            {
                DrawDragTrail(_activeDrag.Value.Points, DragColor, 1f);
            }

            // Draw completed drags (fading)
            foreach (var drag in _drags)
            {
                if (!drag.IsComplete) continue;
                float t = (now - drag.EndTime) / DragFadeDuration;
                float alpha = 1f - t;
                if (alpha > 0)
                    DrawDragTrail(drag.Points, DragColor, alpha);
            }
        }

        private void DrawDragTrail(List<Vector2> points, Color color, float alpha)
        {
            if (points == null || points.Count < 2) return;

            color.a = alpha;

            GL.Begin(GL.LINES);
            GL.Color(color);

            for (int i = 0; i < points.Count - 1; i++)
            {
                GL.Vertex3(points[i].x, points[i].y, 0);
                GL.Vertex3(points[i + 1].x, points[i + 1].y, 0);
            }

            GL.End();

            // Draw arrow at end
            if (points.Count >= 2)
            {
                var end = points[points.Count - 1];
                var prev = points[points.Count - 2];
                var dir = (end - prev).normalized;
                DrawArrowHead(end, dir, 12f, color);
            }

            // Draw start circle
            DrawFilledCircle(points[0].x, points[0].y, 5f, color);
        }

        private void DrawScrolls()
        {
            float now = Time.time;

            foreach (var scroll in _scrolls)
            {
                float t = (now - scroll.StartTime) / ScrollDuration;
                float alpha = 1f - t;

                var color = ScrollColor;
                color.a = alpha;

                // Draw arrow in scroll direction
                float arrowLength = 30f + t * 20f;
                var arrowEnd = scroll.Position + scroll.Direction * arrowLength;

                GL.Begin(GL.LINES);
                GL.Color(color);
                GL.Vertex3(scroll.Position.x, scroll.Position.y, 0);
                GL.Vertex3(arrowEnd.x, arrowEnd.y, 0);
                GL.End();

                DrawArrowHead(arrowEnd, scroll.Direction, 10f, color);
            }
        }

        private void DrawKeys()
        {
            float now = Time.time;

            // Draw active key hold
            if (_activeKeyHold.HasValue)
            {
                var key = _activeKeyHold.Value;
                float holdTime = now - key.StartTime;
                float pulse = 0.7f + Mathf.Sin(holdTime * 8f) * 0.3f; // Pulsing effect

                var color = KeyColor;
                color.a = pulse;

                DrawKeyBadge(key.Position.x, key.Position.y, key.KeyName, color, true, holdTime);
            }

            // Draw completed key events
            foreach (var key in _keys)
            {
                float duration = key.IsHold ? KeyDuration : KeyDuration;
                float endTime = key.IsHold ? key.HoldEndTime : key.StartTime;
                float t = (now - endTime) / duration;
                float alpha = Mathf.Clamp01(1f - t);
                float yOffset = key.IsHold ? 0 : t * 30f; // Only float up for taps

                var color = KeyColor;
                color.a = alpha * 0.7f;

                float holdDuration = key.IsHold ? (key.HoldEndTime - key.StartTime) : 0;
                DrawKeyBadge(key.Position.x, key.Position.y + yOffset, key.KeyName, color, key.IsHold, holdDuration);
            }
        }

        private void DrawKeyBadge(float x, float y, string keyName, Color color, bool isHold, float holdDuration)
        {
            // Format the key name nicely
            string displayName = FormatKeyName(keyName);
            if (isHold && holdDuration > 0)
                displayName += $" ({holdDuration:F1}s)";

            float charWidth = 8f;
            float width = displayName.Length * charWidth + 20f;
            float height = 28f;

            // Background
            var bgColor = new Color(0.1f, 0.1f, 0.1f, color.a * 0.85f);
            DrawFilledRect(x - width / 2f, y - height / 2f, width, height, bgColor);

            // Border (thicker for holds)
            float borderThickness = isHold ? 3f : 2f;
            DrawRect(x - width / 2f, y - height / 2f, width, height, color, borderThickness);

            // Inner glow for holds
            if (isHold)
            {
                var glowColor = color;
                glowColor.a *= 0.3f;
                DrawFilledRect(x - width / 2f + 3f, y - height / 2f + 3f, width - 6f, height - 6f, glowColor);
            }
        }

        private string FormatKeyName(string keyName)
        {
            // Make key names more readable
            return keyName
                .Replace("Left", "L-")
                .Replace("Right", "R-")
                .Replace("Digit", "")
                .Replace("Numpad", "Num")
                .Replace("Arrow", "");
        }

        private void DrawHolds()
        {
            float now = Time.time;

            // Draw active hold
            if (_activeHold.HasValue)
            {
                var hold = _activeHold.Value;
                float holdTime = now - hold.StartTime;
                float pulse = 0.5f + Mathf.Sin(holdTime * 6f) * 0.3f;
                float radius = 20f + holdTime * 10f; // Grows over time

                var color = HoldColor;
                color.a = pulse;

                // Pulsing circles
                DrawCircle(hold.Position.x, hold.Position.y, radius, color, 3f);
                DrawCircle(hold.Position.x, hold.Position.y, radius * 0.6f, color, 2f);
                DrawFilledCircle(hold.Position.x, hold.Position.y, 8f, color);

                // Duration text indicator position
                DrawHoldTimer(hold.Position, holdTime, color);
            }

            // Draw completed holds (fading)
            foreach (var hold in _holds)
            {
                if (!hold.IsComplete) continue;
                float t = (now - hold.EndTime) / HoldDuration;
                float alpha = 1f - t;

                var color = HoldColor;
                color.a = alpha * 0.7f;

                float holdDuration = hold.EndTime - hold.StartTime;
                float radius = 20f + holdDuration * 10f;
                DrawCircle(hold.Position.x, hold.Position.y, radius * (1f + t * 0.3f), color, 2f);
            }
        }

        private void DrawHoldTimer(Vector2 pos, float seconds, Color color)
        {
            // Draw a small timer indicator above the hold
            float y = pos.y + 40f;
            string text = $"{seconds:F1}s";
            float width = text.Length * 8f + 12f;

            var bgColor = new Color(0, 0, 0, color.a * 0.7f);
            DrawFilledRect(pos.x - width / 2f, y - 10f, width, 20f, bgColor);
            DrawRect(pos.x - width / 2f, y - 10f, width, 20f, color, 1f);
        }

        private void DrawPinches()
        {
            float now = Time.time;

            // Draw active pinch
            if (_activePinch.HasValue)
            {
                var pinch = _activePinch.Value;
                DrawPinchIndicator(pinch.Center, pinch.StartDistance, pinch.EndDistance, PinchColor, 1f);
            }

            // Draw completed pinches (fading)
            foreach (var pinch in _pinches)
            {
                if (!pinch.IsComplete) continue;
                float t = (now - pinch.EndTime) / PinchDuration;
                float alpha = 1f - t;

                var color = PinchColor;
                color.a = alpha * 0.7f;

                DrawPinchIndicator(pinch.Center, pinch.StartDistance, pinch.EndDistance, color, alpha);
            }
        }

        private void DrawPinchIndicator(Vector2 center, float startDist, float endDist, Color color, float alpha)
        {
            color.a *= alpha;

            // Draw two finger circles
            float currentDist = endDist / 2f;
            var finger1 = center + Vector2.left * currentDist;
            var finger2 = center + Vector2.right * currentDist;

            // Finger indicators with "1" and "2" implied by position
            DrawFilledCircle(finger1.x, finger1.y, 12f, color);
            DrawFilledCircle(finger2.x, finger2.y, 12f, color);
            DrawCircle(finger1.x, finger1.y, 15f, color, 2f);
            DrawCircle(finger2.x, finger2.y, 15f, color, 2f);

            // Draw start distance (dotted line effect with gaps)
            var startColor = color;
            startColor.a *= 0.4f;
            DrawDashedLine(center + Vector2.left * (startDist / 2f), center + Vector2.right * (startDist / 2f), startColor);

            // Arrow indicating direction (zoom in or out)
            bool zoomIn = endDist < startDist;
            var arrowColor = color;
            if (zoomIn)
            {
                // Arrows pointing inward
                DrawArrowHead(finger1 + Vector2.right * 20f, Vector2.right, 10f, arrowColor);
                DrawArrowHead(finger2 + Vector2.left * 20f, Vector2.left, 10f, arrowColor);
            }
            else
            {
                // Arrows pointing outward
                DrawArrowHead(finger1 + Vector2.left * 5f, Vector2.left, 10f, arrowColor);
                DrawArrowHead(finger2 + Vector2.right * 5f, Vector2.right, 10f, arrowColor);
            }

            // Scale indicator
            float scale = endDist / Mathf.Max(startDist, 1f);
            string scaleText = scale < 1f ? $"{scale:F1}x" : $"{scale:F1}x";
            float textWidth = scaleText.Length * 8f + 12f;
            var bgColor = new Color(0, 0, 0, color.a * 0.7f);
            DrawFilledRect(center.x - textWidth / 2f, center.y - 30f, textWidth, 20f, bgColor);
            DrawRect(center.x - textWidth / 2f, center.y - 30f, textWidth, 20f, color, 1f);
        }

        private void DrawRotates()
        {
            float now = Time.time;

            // Draw active rotate
            if (_activeRotate.HasValue)
            {
                var rotate = _activeRotate.Value;
                DrawRotateIndicator(rotate.Center, rotate.Radius, rotate.Degrees, RotateColor, 1f);
            }

            // Draw completed rotates (fading)
            foreach (var rotate in _rotates)
            {
                if (!rotate.IsComplete) continue;
                float t = (now - rotate.EndTime) / RotateDuration;
                float alpha = 1f - t;

                var color = RotateColor;
                color.a = alpha * 0.7f;

                DrawRotateIndicator(rotate.Center, rotate.Radius, rotate.Degrees, color, alpha);
            }
        }

        private void DrawRotateIndicator(Vector2 center, float radius, float degrees, Color color, float alpha)
        {
            color.a *= alpha;

            // Draw the arc
            float startAngle = 0;
            float endAngle = degrees * Mathf.Deg2Rad;
            DrawArc(center, radius, startAngle, endAngle, color);

            // Draw finger positions at start and end of arc
            var finger1Start = center + new Vector2(Mathf.Cos(startAngle), Mathf.Sin(startAngle)) * radius;
            var finger1End = center + new Vector2(Mathf.Cos(endAngle), Mathf.Sin(endAngle)) * radius;

            DrawFilledCircle(finger1Start.x, finger1Start.y, 10f, color);
            DrawFilledCircle(finger1End.x, finger1End.y, 12f, color);

            // Arrow at end showing direction
            var tangent = new Vector2(-Mathf.Sin(endAngle), Mathf.Cos(endAngle));
            if (degrees < 0) tangent = -tangent;
            DrawArrowHead(finger1End, tangent, 12f, color);

            // Degrees indicator
            string degText = $"{degrees:F0}°";
            float textWidth = degText.Length * 8f + 12f;
            var bgColor = new Color(0, 0, 0, color.a * 0.7f);
            DrawFilledRect(center.x - textWidth / 2f, center.y - 10f, textWidth, 20f, bgColor);
            DrawRect(center.x - textWidth / 2f, center.y - 10f, textWidth, 20f, color, 1f);
        }

        private void DrawTwoFingers()
        {
            float now = Time.time;

            // Draw active two-finger gesture
            if (_activeTwoFinger.HasValue)
            {
                var tf = _activeTwoFinger.Value;
                DrawTwoFingerIndicator(tf.Start1, tf.End1, tf.Start2, tf.End2, TwoFingerColor, 1f);
            }

            // Draw completed two-finger gestures (fading)
            foreach (var tf in _twoFingers)
            {
                if (!tf.IsComplete) continue;
                float t = (now - tf.EndTime) / TwoFingerDuration;
                float alpha = 1f - t;

                var color = TwoFingerColor;
                color.a = alpha * 0.7f;

                DrawTwoFingerIndicator(tf.Start1, tf.End1, tf.Start2, tf.End2, color, alpha);
            }
        }

        private void DrawTwoFingerIndicator(Vector2 start1, Vector2 end1, Vector2 start2, Vector2 end2, Color color, float alpha)
        {
            color.a *= alpha;

            // Draw trails for both fingers
            GL.Begin(GL.LINES);
            GL.Color(color);
            GL.Vertex3(start1.x, start1.y, 0);
            GL.Vertex3(end1.x, end1.y, 0);
            GL.Vertex3(start2.x, start2.y, 0);
            GL.Vertex3(end2.x, end2.y, 0);
            GL.End();

            // Start positions with finger numbers
            DrawFingerCircle(start1, 1, color, 0.5f);
            DrawFingerCircle(start2, 2, color, 0.5f);

            // End positions (current)
            DrawFingerCircle(end1, 1, color, 1f);
            DrawFingerCircle(end2, 2, color, 1f);

            // Arrows at end
            var dir1 = (end1 - start1).normalized;
            var dir2 = (end2 - start2).normalized;
            if (dir1.magnitude > 0.1f) DrawArrowHead(end1, dir1, 10f, color);
            if (dir2.magnitude > 0.1f) DrawArrowHead(end2, dir2, 10f, color);
        }

        private void DrawFingerCircle(Vector2 pos, int fingerIndex, Color color, float alphaMultiplier)
        {
            var c = color;
            c.a *= alphaMultiplier;

            DrawFilledCircle(pos.x, pos.y, 14f, c);
            DrawCircle(pos.x, pos.y, 16f, color, 2f);

            // Draw finger number indicator (small filled circle offset)
            var numColor = new Color(1, 1, 1, c.a);
            float offsetX = fingerIndex == 1 ? -4f : 4f;
            DrawFilledCircle(pos.x + offsetX, pos.y + 8f, 4f, numColor);
            if (fingerIndex == 2)
            {
                DrawFilledCircle(pos.x - 4f, pos.y + 8f, 4f, numColor);
            }
        }

        private void DrawTexts()
        {
            float now = Time.time;

            foreach (var text in _texts)
            {
                float t = (now - text.StartTime) / TextDuration;
                float alpha = Mathf.Clamp01(1f - t);
                float yOffset = t * 20f;

                var color = TextColor;
                color.a = alpha * 0.7f;

                // Draw text box above the input field
                float charWidth = 7f;
                float width = Mathf.Min(text.Text.Length * charWidth + 16f, 300f);
                float height = 24f;
                float x = text.Position.x;
                float y = text.Position.y + 30f + yOffset;

                var bgColor = new Color(0.1f, 0.1f, 0.2f, color.a * 0.85f);
                DrawFilledRect(x - width / 2f, y - height / 2f, width, height, bgColor);
                DrawRect(x - width / 2f, y - height / 2f, width, height, color, 2f);

                // Typing indicator (blinking cursor effect)
                float blink = Mathf.Sin(now * 10f) > 0 ? 1f : 0.3f;
                var cursorColor = color;
                cursorColor.a *= blink;
                DrawFilledRect(x + width / 2f - 8f, y - 8f, 3f, 16f, cursorColor);
            }
        }

        #endregion

        #region GL Primitives

        private void DrawCircle(float cx, float cy, float radius, Color color, float thickness)
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

        private void DrawFilledCircle(float cx, float cy, float radius, Color color)
        {
            GL.Begin(GL.TRIANGLES);
            GL.Color(color);

            int segments = 16;
            for (int i = 0; i < segments; i++)
            {
                float angle1 = (float)i / segments * Mathf.PI * 2f;
                float angle2 = (float)(i + 1) / segments * Mathf.PI * 2f;

                GL.Vertex3(cx, cy, 0);
                GL.Vertex3(cx + Mathf.Cos(angle1) * radius, cy + Mathf.Sin(angle1) * radius, 0);
                GL.Vertex3(cx + Mathf.Cos(angle2) * radius, cy + Mathf.Sin(angle2) * radius, 0);
            }

            GL.End();
        }

        private void DrawFilledRect(float x, float y, float width, float height, Color color)
        {
            GL.Begin(GL.QUADS);
            GL.Color(color);
            GL.Vertex3(x, y, 0);
            GL.Vertex3(x + width, y, 0);
            GL.Vertex3(x + width, y + height, 0);
            GL.Vertex3(x, y + height, 0);
            GL.End();
        }

        private void DrawRect(float x, float y, float width, float height, Color color, float thickness)
        {
            GL.Begin(GL.QUADS);
            GL.Color(color);

            // Top
            GL.Vertex3(x, y + height - thickness, 0);
            GL.Vertex3(x + width, y + height - thickness, 0);
            GL.Vertex3(x + width, y + height, 0);
            GL.Vertex3(x, y + height, 0);

            // Bottom
            GL.Vertex3(x, y, 0);
            GL.Vertex3(x + width, y, 0);
            GL.Vertex3(x + width, y + thickness, 0);
            GL.Vertex3(x, y + thickness, 0);

            // Left
            GL.Vertex3(x, y, 0);
            GL.Vertex3(x + thickness, y, 0);
            GL.Vertex3(x + thickness, y + height, 0);
            GL.Vertex3(x, y + height, 0);

            // Right
            GL.Vertex3(x + width - thickness, y, 0);
            GL.Vertex3(x + width, y, 0);
            GL.Vertex3(x + width, y + height, 0);
            GL.Vertex3(x + width - thickness, y + height, 0);

            GL.End();
        }

        private void DrawArrowHead(Vector2 tip, Vector2 direction, float size, Color color)
        {
            // Calculate perpendicular
            var perp = new Vector2(-direction.y, direction.x);
            var back = tip - direction * size;
            var left = back + perp * size * 0.5f;
            var right = back - perp * size * 0.5f;

            GL.Begin(GL.TRIANGLES);
            GL.Color(color);
            GL.Vertex3(tip.x, tip.y, 0);
            GL.Vertex3(left.x, left.y, 0);
            GL.Vertex3(right.x, right.y, 0);
            GL.End();
        }

        private void DrawArc(Vector2 center, float radius, float startAngle, float endAngle, Color color)
        {
            GL.Begin(GL.LINES);
            GL.Color(color);

            int segments = Mathf.Max(8, Mathf.Abs((int)((endAngle - startAngle) * 16f / Mathf.PI)));
            float angleStep = (endAngle - startAngle) / segments;

            for (int i = 0; i < segments; i++)
            {
                float a1 = startAngle + angleStep * i;
                float a2 = startAngle + angleStep * (i + 1);

                GL.Vertex3(center.x + Mathf.Cos(a1) * radius, center.y + Mathf.Sin(a1) * radius, 0);
                GL.Vertex3(center.x + Mathf.Cos(a2) * radius, center.y + Mathf.Sin(a2) * radius, 0);
            }

            GL.End();
        }

        private void DrawDashedLine(Vector2 start, Vector2 end, Color color)
        {
            GL.Begin(GL.LINES);
            GL.Color(color);

            Vector2 dir = (end - start);
            float length = dir.magnitude;
            dir.Normalize();

            float dashLength = 8f;
            float gapLength = 6f;
            float pos = 0;

            while (pos < length)
            {
                float dashEnd = Mathf.Min(pos + dashLength, length);
                var p1 = start + dir * pos;
                var p2 = start + dir * dashEnd;

                GL.Vertex3(p1.x, p1.y, 0);
                GL.Vertex3(p2.x, p2.y, 0);

                pos = dashEnd + gapLength;
            }

            GL.End();
        }

        #endregion
    }
}
