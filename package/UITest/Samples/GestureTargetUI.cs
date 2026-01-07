using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ODDGames.UITest.Samples
{
    /// <summary>
    /// Sample component that responds to touch gestures (pinch, rotate, two-finger swipe).
    /// Attach to a UI element to make it interactive with touch input.
    /// </summary>
    public class GestureTargetUI : MonoBehaviour
    {
        [Header("Visual Feedback")]
        [SerializeField] private Text statusText;

        private RectTransform rectTransform;
        private Vector3 baseScale;
        private float baseRotation;
        private Vector2 basePosition;

        // Touch tracking
        private bool wasGesturing;
        private Vector2 touch1Start;
        private Vector2 touch2Start;
        private float initialDistance;
        private float initialAngle;

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            SaveBaseState();

            // Find status text if not assigned
            if (statusText == null)
            {
                var statusGO = GameObject.Find("GestureStatus");
                if (statusGO != null)
                    statusText = statusGO.GetComponent<Text>();
            }
        }

        private void SaveBaseState()
        {
            baseScale = rectTransform.localScale;
            baseRotation = rectTransform.localEulerAngles.z;
            basePosition = rectTransform.anchoredPosition;
        }

        private void Update()
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                return;
            }

            // Read active touches
            Vector2? pos1 = null;
            Vector2? pos2 = null;

            foreach (var touch in touchscreen.touches)
            {
                var phase = touch.phase.ReadValue();
                if (phase == UnityEngine.InputSystem.TouchPhase.Began ||
                    phase == UnityEngine.InputSystem.TouchPhase.Moved ||
                    phase == UnityEngine.InputSystem.TouchPhase.Stationary)
                {
                    var position = touch.position.ReadValue();
                    if (pos1 == null)
                        pos1 = position;
                    else if (pos2 == null)
                        pos2 = position;
                }
            }

            bool isGesturing = pos1.HasValue && pos2.HasValue;

            if (isGesturing)
            {
                Vector2 p1 = pos1.Value;
                Vector2 p2 = pos2.Value;

                float currentDistance = Vector2.Distance(p1, p2);
                Vector2 delta = p2 - p1;
                float currentAngle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

                if (!wasGesturing)
                {
                    // Gesture just started - save initial state
                    initialDistance = currentDistance;
                    initialAngle = currentAngle;
                    touch1Start = p1;
                    touch2Start = p2;
                    SaveBaseState();
                    UpdateStatus("Gesture: Started");
                    Debug.Log($"[GestureTarget] Gesture started - distance={initialDistance:F0} angle={initialAngle:F1}");
                }
                else
                {
                    // Apply pinch scale
                    if (initialDistance > 10f)
                    {
                        float scale = currentDistance / initialDistance;
                        rectTransform.localScale = baseScale * scale;

                        if (Mathf.Abs(scale - 1f) > 0.05f)
                        {
                            UpdateStatus($"Pinch: {scale:F2}x");
                        }
                    }

                    // Apply rotation
                    float angleDelta = Mathf.DeltaAngle(initialAngle, currentAngle);
                    if (Mathf.Abs(angleDelta) > 2f)
                    {
                        rectTransform.localEulerAngles = new Vector3(0, 0, baseRotation - angleDelta);
                        UpdateStatus($"Rotate: {-angleDelta:F1}°");
                    }

                    // Apply two-finger pan
                    Vector2 initialCenter = (touch1Start + touch2Start) / 2f;
                    Vector2 currentCenter = (p1 + p2) / 2f;
                    Vector2 panDelta = currentCenter - initialCenter;

                    if (panDelta.magnitude > 5f)
                    {
                        rectTransform.anchoredPosition = basePosition + panDelta * 0.5f;
                        UpdateStatus($"Pan: {panDelta.x:F0}, {panDelta.y:F0}");
                    }
                }
            }
            else if (wasGesturing)
            {
                // Gesture ended
                SaveBaseState();
                UpdateStatus("Gesture: Ended");
                Debug.Log("[GestureTarget] Gesture ended");
            }

            wasGesturing = isGesturing;
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }

        /// <summary>
        /// Reset to original state.
        /// </summary>
        [ContextMenu("Reset Transform")]
        public void ResetTransform()
        {
            if (rectTransform != null)
            {
                rectTransform.localScale = Vector3.one;
                rectTransform.localEulerAngles = Vector3.zero;
                rectTransform.anchoredPosition = Vector2.zero;
                baseScale = Vector3.one;
                baseRotation = 0;
                basePosition = Vector2.zero;
            }
            UpdateStatus("Gesture: Reset");
        }
    }
}
