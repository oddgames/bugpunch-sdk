using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// Shared utility for finding UI elements in the scene.
    /// Used by UIAutomation and AI testing.
    /// </summary>
    internal static class ElementFinder
    {
        /// <summary>
        /// Finds all active, visible, interactable Selectables in the scene.
        /// Filters out elements hidden by CanvasGroup or with zero bounds.
        /// </summary>
        public static List<Selectable> FindAllSelectables(bool includeInactive = false, bool includeDisabled = false)
        {
            var findMode = includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
            var selectables = UnityEngine.Object.FindObjectsByType<Selectable>(findMode, FindObjectsSortMode.None);
            var result = new List<Selectable>();

            foreach (var selectable in selectables)
            {
                if (selectable == null) continue;

                // Check active state
                if (!includeInactive && !selectable.gameObject.activeInHierarchy)
                    continue;

                // Check interactability
                if (!includeDisabled && !selectable.interactable)
                    continue;

                // Check canvas group visibility
                var canvasGroup = selectable.GetComponentInParent<CanvasGroup>();
                if (canvasGroup != null && (canvasGroup.alpha <= 0 || (!includeDisabled && !canvasGroup.interactable)))
                    continue;

                // Check bounds using InputInjector
                var bounds = InputInjector.GetScreenBounds(selectable.gameObject);
                if (bounds.width <= 0 || bounds.height <= 0)
                    continue;

                result.Add(selectable);
            }

            return result;
        }

        /// <summary>
        /// Finds a Selectable by its GameObject's instance ID.
        /// </summary>
        public static Selectable FindByInstanceId(int instanceId)
        {
            var selectables = UnityEngine.Object.FindObjectsByType<Selectable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            return selectables.FirstOrDefault(s => s != null && s.gameObject.GetInstanceID() == instanceId);
        }

        /// <summary>
        /// Finds a Selectable by its GameObject's instance ID (string version).
        /// </summary>
        public static Selectable FindByInstanceId(string instanceIdStr)
        {
            if (string.IsNullOrEmpty(instanceIdStr))
                return null;

            if (int.TryParse(instanceIdStr, out int instanceId))
                return FindByInstanceId(instanceId);

            return null;
        }

        /// <summary>
        /// Finds the first Selectable whose visible text contains the specified text (case-insensitive).
        /// </summary>
        public static Selectable FindByText(string text, bool includeInactive = false)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            var lowerText = text.ToLowerInvariant();
            var selectables = FindAllSelectables(includeInactive);

            // First try exact text match
            foreach (var selectable in selectables)
            {
                var elementText = GetVisibleText(selectable.gameObject);
                if (!string.IsNullOrEmpty(elementText) && elementText.ToLowerInvariant() == lowerText)
                    return selectable;
            }

            // Then try text contains
            foreach (var selectable in selectables)
            {
                var elementText = GetVisibleText(selectable.gameObject);
                if (!string.IsNullOrEmpty(elementText) && elementText.ToLowerInvariant().Contains(lowerText))
                    return selectable;
            }

            // Finally try name contains
            foreach (var selectable in selectables)
            {
                if (selectable.gameObject.name.ToLowerInvariant().Contains(lowerText))
                    return selectable;
            }

            return null;
        }

        /// <summary>
        /// Finds the first Selectable whose name matches the pattern (supports * wildcards).
        /// </summary>
        public static Selectable FindByName(string pattern, bool includeInactive = false)
        {
            if (string.IsNullOrEmpty(pattern))
                return null;

            var selectables = FindAllSelectables(includeInactive);
            return selectables.FirstOrDefault(s => WildcardMatch(s.gameObject.name, pattern));
        }

        /// <summary>
        /// Gets the visible text content of a GameObject (from Text, TMP_Text, or child components).
        /// </summary>
        public static string GetVisibleText(GameObject go)
        {
            if (go == null) return null;

            // Check TMP_Text first (more common in modern projects)
            var tmpText = go.GetComponentInChildren<TMP_Text>();
            if (tmpText != null && !string.IsNullOrEmpty(tmpText.text))
                return tmpText.text;

            // Check legacy Text
            var text = go.GetComponentInChildren<Text>();
            if (text != null && !string.IsNullOrEmpty(text.text))
                return text.text;

            // Check InputField placeholder/text
            var inputField = go.GetComponent<InputField>();
            if (inputField != null)
            {
                if (!string.IsNullOrEmpty(inputField.text))
                    return inputField.text;
                if (inputField.placeholder != null)
                {
                    var placeholderText = inputField.placeholder.GetComponent<Text>();
                    if (placeholderText != null)
                        return placeholderText.text;
                }
            }

            // Check TMP_InputField placeholder/text
            var tmpInputField = go.GetComponent<TMP_InputField>();
            if (tmpInputField != null)
            {
                if (!string.IsNullOrEmpty(tmpInputField.text))
                    return tmpInputField.text;
                if (tmpInputField.placeholder != null)
                {
                    var placeholderTmp = tmpInputField.placeholder.GetComponent<TMP_Text>();
                    if (placeholderTmp != null)
                        return placeholderTmp.text;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the element type string for a Selectable.
        /// </summary>
        public static string GetElementType(Selectable selectable)
        {
            if (selectable == null) return "unknown";

            // Check for ScrollRect on the same GameObject (ScrollRect is not a Selectable)
            if (selectable.GetComponent<ScrollRect>() != null)
                return "scrollview";

            return selectable switch
            {
                Button => "button",
                Toggle toggle => toggle.isOn ? "toggle-on" : "toggle-off",
                Slider => "slider",
                Scrollbar => "scrollbar",
                InputField => "input",
                TMP_InputField => "input",
                TMP_Dropdown => "dropdown",
                Dropdown => "dropdown",
                _ => "selectable"
            };
        }

        /// <summary>
        /// Gets the hierarchy path of a Transform.
        /// </summary>
        public static string GetHierarchyPath(Transform transform)
        {
            if (transform == null) return "";

            var path = new System.Text.StringBuilder(transform.name);
            Transform current = transform.parent;

            while (current != null)
            {
                path.Insert(0, "/");
                path.Insert(0, current.name);
                current = current.parent;
            }

            return path.ToString();
        }

        /// <summary>
        /// Information about a non-Selectable interactable element.
        /// </summary>
        internal class InteractableInfo
        {
            public GameObject GameObject { get; set; }
            public string Type { get; set; } // "draggable", "droptarget", "clickable"
            public bool IsDraggable { get; set; }
            public bool IsDropTarget { get; set; }
            public bool IsClickable { get; set; }
        }

        /// <summary>
        /// Finds all active, visible GameObjects that implement EventSystem interfaces
        /// but are NOT Selectables. This catches custom interactable elements like:
        /// - Draggables (IDragHandler, IBeginDragHandler, IEndDragHandler)
        /// - Drop targets (IDropHandler)
        /// - Custom clickables (IPointerClickHandler, IPointerDownHandler)
        /// </summary>
        public static List<InteractableInfo> FindAllNonSelectableInteractables(bool includeInactive = false)
        {
            var result = new List<InteractableInfo>();
            var selectableSet = new HashSet<GameObject>();
            var processedSet = new HashSet<GameObject>();

            // Build set of selectable GameObjects to exclude (they're already discovered)
            var selectables = FindAllSelectables(includeInactive);
            foreach (var s in selectables)
            {
                if (s != null)
                    selectableSet.Add(s.gameObject);
            }

            // Find all MonoBehaviours and check for EventSystem interfaces
            var findMode = includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
            var allBehaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(findMode, FindObjectsSortMode.None);

            foreach (var behaviour in allBehaviours)
            {
                if (behaviour == null) continue;

                var go = behaviour.gameObject;

                // Skip if already a Selectable
                if (selectableSet.Contains(go))
                    continue;

                // Skip if already processed (may have multiple behaviours)
                if (processedSet.Contains(go))
                {
                    // But update existing entry with additional interface info
                    var existing = result.Find(i => i.GameObject == go);
                    if (existing != null)
                    {
                        if (behaviour is IDragHandler || behaviour is IBeginDragHandler || behaviour is IEndDragHandler)
                            existing.IsDraggable = true;
                        if (behaviour is IDropHandler)
                            existing.IsDropTarget = true;
                        if (behaviour is IPointerClickHandler || behaviour is IPointerDownHandler)
                            existing.IsClickable = true;
                    }
                    continue;
                }

                // Check if implements any interactable interface
                bool isDraggable = behaviour is IDragHandler || behaviour is IBeginDragHandler || behaviour is IEndDragHandler;
                bool isDropTarget = behaviour is IDropHandler;
                bool isClickable = behaviour is IPointerClickHandler || behaviour is IPointerDownHandler;

                if (!isDraggable && !isDropTarget && !isClickable)
                    continue;

                // Check active state
                if (!includeInactive && !go.activeInHierarchy)
                    continue;

                // Check canvas group visibility
                var canvasGroup = go.GetComponentInParent<CanvasGroup>();
                if (canvasGroup != null && canvasGroup.alpha <= 0)
                    continue;

                // Check bounds
                var bounds = InputInjector.GetScreenBounds(go);
                if (bounds.width <= 0 || bounds.height <= 0)
                    continue;

                processedSet.Add(go);

                // Determine primary type
                string type;
                if (isDraggable && isDropTarget)
                    type = "draggable-droptarget";
                else if (isDraggable)
                    type = "draggable";
                else if (isDropTarget)
                    type = "droptarget";
                else
                    type = "clickable";

                result.Add(new InteractableInfo
                {
                    GameObject = go,
                    Type = type,
                    IsDraggable = isDraggable,
                    IsDropTarget = isDropTarget,
                    IsClickable = isClickable
                });
            }

            return result;
        }

        /// <summary>
        /// Finds all active, visible GameObjects that implement drag interfaces
        /// (IDragHandler, IBeginDragHandler, IEndDragHandler) but are NOT Selectables.
        /// This catches custom draggable elements like drag-and-drop items.
        /// </summary>
        public static List<GameObject> FindAllDraggables(bool includeInactive = false)
        {
            return FindAllNonSelectableInteractables(includeInactive)
                .Where(i => i.IsDraggable)
                .Select(i => i.GameObject)
                .ToList();
        }

        /// <summary>
        /// Finds all active, visible GameObjects that implement drop interfaces (IDropHandler).
        /// These are drop targets for drag-and-drop operations.
        /// </summary>
        public static List<GameObject> FindAllDropTargets(bool includeInactive = false)
        {
            return FindAllNonSelectableInteractables(includeInactive)
                .Where(i => i.IsDropTarget)
                .Select(i => i.GameObject)
                .ToList();
        }

        /// <summary>
        /// Finds all active, visible GameObjects that implement click interfaces
        /// (IPointerClickHandler, IPointerDownHandler) but are NOT Selectables.
        /// </summary>
        public static List<GameObject> FindAllClickables(bool includeInactive = false)
        {
            return FindAllNonSelectableInteractables(includeInactive)
                .Where(i => i.IsClickable)
                .Select(i => i.GameObject)
                .ToList();
        }

        /// <summary>
        /// Finds a GameObject by its instance ID.
        /// Works for both Selectables and draggable elements.
        /// </summary>
        public static GameObject FindGameObjectByInstanceId(int instanceId)
        {
            // First check selectables
            var selectable = FindByInstanceId(instanceId);
            if (selectable != null)
                return selectable.gameObject;

            // Then check draggables
            var draggables = FindAllDraggables();
            var draggable = draggables.FirstOrDefault(go => go.GetInstanceID() == instanceId);
            if (draggable != null)
                return draggable;

            // Then check drop targets
            var dropTargets = FindAllDropTargets();
            return dropTargets.FirstOrDefault(go => go.GetInstanceID() == instanceId);
        }

        /// <summary>
        /// Finds a GameObject by its instance ID (string version).
        /// </summary>
        public static GameObject FindGameObjectByInstanceId(string instanceIdStr)
        {
            if (string.IsNullOrEmpty(instanceIdStr))
                return null;

            if (int.TryParse(instanceIdStr, out int instanceId))
                return FindGameObjectByInstanceId(instanceId);

            return null;
        }

        /// <summary>
        /// Performs wildcard pattern matching (supports * wildcards).
        /// </summary>
        public static bool WildcardMatch(string subject, string pattern)
        {
            if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(pattern))
                return false;

            if (!pattern.Contains('*'))
                return subject.Equals(pattern, StringComparison.OrdinalIgnoreCase);

            string[] parts = pattern.Split('*');
            int subjectIndex = 0;
            string subjectLower = subject.ToLowerInvariant();
            string patternLower = pattern.ToLowerInvariant();

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].ToLowerInvariant();
                if (string.IsNullOrEmpty(part)) continue;

                int foundIndex = subjectLower.IndexOf(part, subjectIndex, StringComparison.Ordinal);
                if (foundIndex < 0) return false;

                if (i == 0 && !patternLower.StartsWith("*") && foundIndex != 0)
                    return false;

                if (i == parts.Length - 1 && !patternLower.EndsWith("*"))
                {
                    if (foundIndex + part.Length != subjectLower.Length)
                        return false;
                }

                subjectIndex = foundIndex + part.Length;
            }

            return true;
        }
    }
}
