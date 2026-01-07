using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ODDGames.UITest
{
    /// <summary>
    /// Helper class for reporting UI events to the UITestRecorder.
    /// This can be called from any input module implementation.
    /// </summary>
    public static class UITestInputEvents
    {
        /// <summary>
        /// Report a click event to the recorder.
        /// Call this from your input module when a click is executed.
        /// </summary>
        public static void ReportClick(PointerEventData pointerEvent)
        {
            if (pointerEvent?.pointerPress == null) return;

            var recorder = UITestRecorder.Instance;
            if (recorder == null || !recorder.IsRecording) return;

            try
            {
                var target = pointerEvent.pointerPress;
                string targetName = target.name ?? "Unnamed";
                string parentName = target.transform.parent?.name ?? "Root";
                string grandparentName = target.transform.parent?.parent?.name;
                Vector2 clickPos = pointerEvent.position;

                string componentType = "Unknown";
                var selectable = target.GetComponent<Selectable>();
                if (selectable != null)
                {
                    componentType = selectable.GetType().Name;
                }

                string targetPath = GetHierarchyPath(target.transform);
                string textContent = GetTextContent(target);

                // Calculate sibling info
                int siblingIndex = 0;
                int siblingCount = 1;
                if (target.transform.parent != null)
                {
                    siblingIndex = target.transform.GetSiblingIndex();
                    siblingCount = target.transform.parent.childCount;
                }

                recorder.RecordClick(targetName, targetPath, componentType, parentName, clickPos, textContent, siblingIndex, siblingCount, grandparentName);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UITest] ReportClick error: {ex.Message}");
            }
        }

        /// <summary>
        /// Report a drag event to the recorder.
        /// Call this from your input module when dragging completes or during drag.
        /// </summary>
        public static void ReportDrag(PointerEventData pointerEvent)
        {
            if (pointerEvent?.pointerDrag == null) return;

            var recorder = UITestRecorder.Instance;
            if (recorder == null || !recorder.IsRecording) return;

            try
            {
                var dragTarget = pointerEvent.pointerDrag;
                string targetName = dragTarget.name ?? "Unknown";
                string targetPath = GetHierarchyPath(dragTarget.transform);
                string targetType = "Unknown";
                string parentName = dragTarget.transform.parent?.name ?? "Root";
                string grandparentName = dragTarget.transform.parent?.parent?.name;

                var selectable = dragTarget.GetComponent<Selectable>();
                if (selectable != null)
                {
                    targetType = selectable.GetType().Name;
                }

                // Find ScrollRect if this is a scroll operation
                var scrollRect = dragTarget.GetComponentInParent<ScrollRect>();
                string scrollRectName = scrollRect?.name;

                // Calculate sibling info
                int siblingIndex = 0;
                int siblingCount = 1;
                if (dragTarget.transform.parent != null)
                {
                    siblingIndex = dragTarget.transform.GetSiblingIndex();
                    siblingCount = dragTarget.transform.parent.childCount;
                }

                recorder.RecordDrag(
                    targetName,
                    targetPath,
                    targetType,
                    parentName,
                    pointerEvent.pressPosition,
                    pointerEvent.position,
                    0f, // duration - calculate if needed
                    scrollRectName,
                    siblingIndex,
                    siblingCount,
                    grandparentName
                );
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UITest] ReportDrag error: {ex.Message}");
            }
        }

        /// <summary>
        /// Report a scroll event to the recorder.
        /// </summary>
        public static void ReportScroll(PointerEventData pointerEvent, float scrollDelta)
        {
            var recorder = UITestRecorder.Instance;
            if (recorder == null || !recorder.IsRecording) return;

            var target = pointerEvent?.pointerCurrentRaycast.gameObject;
            if (target == null) return;

            try
            {
                string targetName = target.name ?? "Unknown";
                string targetPath = GetHierarchyPath(target.transform);
                string targetType = "Unknown";
                string parentName = target.transform.parent?.name ?? "Root";
                string grandparentName = target.transform.parent?.parent?.name;

                var selectable = target.GetComponent<Selectable>();
                if (selectable != null)
                {
                    targetType = selectable.GetType().Name;
                }

                int siblingIndex = 0;
                int siblingCount = 1;
                if (target.transform.parent != null)
                {
                    siblingIndex = target.transform.GetSiblingIndex();
                    siblingCount = target.transform.parent.childCount;
                }

                recorder.RecordScroll(
                    targetName,
                    targetPath,
                    targetType,
                    parentName,
                    pointerEvent.position,
                    scrollDelta,
                    siblingIndex,
                    siblingCount,
                    grandparentName
                );
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UITest] ReportScroll error: {ex.Message}");
            }
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null) return "";

            var parts = new List<string>();
            var current = transform;
            while (current != null)
            {
                parts.Insert(0, current.name);
                current = current.parent;
            }
            return string.Join("/", parts);
        }

        private static string GetTextContent(GameObject go)
        {
            if (go == null) return "";

            var text = go.GetComponent<Text>();
            if (text != null) return text.text ?? "";

            var tmpText = go.GetComponent<TMP_Text>();
            if (tmpText != null) return tmpText.text ?? "";

            var childText = go.GetComponentInChildren<Text>();
            if (childText != null) return childText.text ?? "";

            var childTmpText = go.GetComponentInChildren<TMP_Text>();
            if (childTmpText != null) return childTmpText.text ?? "";

            return "";
        }
    }
}
