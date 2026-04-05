#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// Screen-space utility methods for UI and world-space GameObjects.
    /// </summary>
    public static class UIUtility
    {
        /// <summary>
        /// Gets the screen position of a GameObject (works with both UI and world-space objects).
        /// </summary>
        public static Vector2 GetScreenPosition(GameObject go)
        {
            if (go == null) return Vector2.zero;

            if (go.TryGetComponent<RectTransform>(out var rt))
            {
                Vector3[] corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                Vector3 center = (corners[0] + corners[2]) / 2f;

                var canvas = go.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                    return center;

                Camera cam = (canvas != null) ? canvas.worldCamera : null;
                if (cam == null) cam = Camera.main;
                return cam != null ? RectTransformUtility.WorldToScreenPoint(cam, center) : (Vector2)center;
            }

            if (go.TryGetComponent<Renderer>(out var renderer) && renderer.enabled)
            {
                Camera cam = Camera.main;
                if (cam != null)
                    return cam.WorldToScreenPoint(renderer.bounds.center);
            }
            else if (go.TryGetComponent<Collider>(out var collider) && collider.enabled)
            {
                Camera cam = Camera.main;
                if (cam != null)
                    return cam.WorldToScreenPoint(collider.bounds.center);
            }

            {
                Camera cam = Camera.main;
                return cam != null ? (Vector2)cam.WorldToScreenPoint(go.transform.position) : Vector2.zero;
            }
        }

        /// <summary>
        /// Gets the screen position of a Component's GameObject.
        /// </summary>
        public static Vector2 GetScreenPosition(Component component)
        {
            return component != null ? GetScreenPosition(component.gameObject) : Vector2.zero;
        }

        /// <summary>
        /// Gets the screen-space bounding box of a GameObject.
        /// </summary>
        public static Rect GetScreenBounds(GameObject go)
        {
            if (go == null) return Rect.zero;

            if (go.TryGetComponent<RectTransform>(out var rt))
            {
                Vector3[] corners = new Vector3[4];
                rt.GetWorldCorners(corners);

                var canvas = go.GetComponentInParent<Canvas>();
                Camera cam = null;
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    cam = canvas.worldCamera ?? Camera.main;

                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;

                for (int i = 0; i < 4; i++)
                {
                    Vector2 screenPos;
                    if (cam != null)
                        screenPos = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
                    else
                        screenPos = corners[i];

                    minX = Mathf.Min(minX, screenPos.x);
                    maxX = Mathf.Max(maxX, screenPos.x);
                    minY = Mathf.Min(minY, screenPos.y);
                    maxY = Mathf.Max(maxY, screenPos.y);
                }

                return new Rect(minX, minY, maxX - minX, maxY - minY);
            }

            Bounds? worldBounds = null;
            if (go.TryGetComponent<Renderer>(out var renderer) && renderer.enabled)
                worldBounds = renderer.bounds;
            else if (go.TryGetComponent<Collider>(out var collider) && collider.enabled)
                worldBounds = collider.bounds;

            if (worldBounds.HasValue)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector2 screenMin = cam.WorldToScreenPoint(worldBounds.Value.min);
                    Vector2 screenMax = cam.WorldToScreenPoint(worldBounds.Value.max);

                    return new Rect(
                        Mathf.Min(screenMin.x, screenMax.x),
                        Mathf.Min(screenMin.y, screenMax.y),
                        Mathf.Abs(screenMax.x - screenMin.x),
                        Mathf.Abs(screenMax.y - screenMin.y)
                    );
                }
            }

            return Rect.zero;
        }

        /// <summary>
        /// Gets all raycast hits at the given screen position using EventSystem.RaycastAll.
        /// </summary>
        public static List<RaycastResult> GetHitsAtPosition(Vector2 screenPosition)
        {
            var results = new List<RaycastResult>();
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return results;

            var pointerData = new PointerEventData(eventSystem) { position = screenPosition };
            eventSystem.RaycastAll(pointerData, results);
            return results;
        }

        /// <summary>
        /// Gets all GameObjects at position that have any of the specified handler interface types.
        /// </summary>
        public static List<GameObject> GetReceiversAtPosition(Vector2 screenPosition, params Type[] handlerTypes)
        {
            var receivers = new List<GameObject>();
            var hits = GetHitsAtPosition(screenPosition);

            foreach (var hit in hits)
            {
                foreach (var handlerType in handlerTypes)
                {
                    if (hit.gameObject.GetComponent(handlerType) != null)
                    {
                        receivers.Add(hit.gameObject);
                        break;
                    }
                }
            }

            return receivers;
        }
    }
}
#endif
