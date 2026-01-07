using UnityEngine;
using UnityEngine.InputSystem;

namespace ODDGames.UITest.Samples
{
    /// <summary>
    /// 3D cube that responds to touch gestures for visual demonstration.
    /// - Pinch: Scale the cube
    /// - Rotate: Rotate the cube around Y axis
    /// - Two-finger pan: Move the cube
    /// </summary>
    public class GestureCube : MonoBehaviour
    {
        [Header("Sensitivity")]
        [SerializeField] private float scaleMultiplier = 1f;
        [SerializeField] private float rotationMultiplier = 1f;
        [SerializeField] private float moveMultiplier = 0.01f;

        private Vector3 baseScale;
        private Vector3 baseRotation;
        private Vector3 basePosition;

        // Touch tracking
        private bool wasGesturing;
        private Vector2 touch1Start;
        private Vector2 touch2Start;
        private float initialDistance;
        private float initialAngle;

        private void Start()
        {
            SaveBaseState();
        }

        private void SaveBaseState()
        {
            baseScale = transform.localScale;
            baseRotation = transform.localEulerAngles;
            basePosition = transform.position;
        }

        private void Update()
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null) return;

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
                    // Gesture just started
                    initialDistance = currentDistance;
                    initialAngle = currentAngle;
                    touch1Start = p1;
                    touch2Start = p2;
                    SaveBaseState();
                    Debug.Log($"[GestureCube] Gesture started");
                }
                else
                {
                    // Apply pinch scale
                    if (initialDistance > 10f)
                    {
                        float scale = currentDistance / initialDistance;
                        transform.localScale = baseScale * Mathf.Lerp(1f, scale, scaleMultiplier);
                    }

                    // Apply rotation (around Y axis)
                    float angleDelta = Mathf.DeltaAngle(initialAngle, currentAngle);
                    transform.localEulerAngles = baseRotation + new Vector3(0, -angleDelta * rotationMultiplier, 0);

                    // Apply two-finger pan (move in XY plane)
                    Vector2 initialCenter = (touch1Start + touch2Start) / 2f;
                    Vector2 currentCenter = (p1 + p2) / 2f;
                    Vector2 panDelta = currentCenter - initialCenter;
                    transform.position = basePosition + new Vector3(panDelta.x, panDelta.y, 0) * moveMultiplier;
                }
            }
            else if (wasGesturing)
            {
                // Gesture ended - save current state
                SaveBaseState();
                Debug.Log("[GestureCube] Gesture ended");
            }

            wasGesturing = isGesturing;
        }

        [ContextMenu("Reset Transform")]
        public void ResetTransform()
        {
            transform.localScale = Vector3.one;
            transform.localEulerAngles = Vector3.zero;
            transform.position = Vector3.zero;
            SaveBaseState();
        }
    }
}
