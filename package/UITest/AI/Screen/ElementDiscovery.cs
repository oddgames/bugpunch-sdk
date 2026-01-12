using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ODDGames.UITest;

namespace ODDGames.UITest.AI
{
    /// <summary>
    /// Discovers all interactable UI elements in the scene.
    /// </summary>
    public static class ElementDiscovery
    {
        /// <summary>
        /// Discovers all interactable UI elements and returns structured data for AI.
        /// </summary>
        public static List<ElementInfo> DiscoverElements()
        {
            var elements = new List<ElementInfo>();
            var selectables = Object.FindObjectsByType<Selectable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            int idCounter = 1;

            foreach (var selectable in selectables)
            {
                if (selectable == null) continue;
                if (!selectable.gameObject.activeInHierarchy) continue;

                // Check canvas group visibility
                var canvasGroup = selectable.GetComponentInParent<CanvasGroup>();
                if (canvasGroup != null && (canvasGroup.alpha <= 0 || !canvasGroup.interactable))
                    continue;

                var bounds = InputInjector.GetScreenBounds(selectable.gameObject);
                if (bounds.width <= 0 || bounds.height <= 0)
                    continue;

                var element = new ElementInfo
                {
                    id = $"e{idCounter++}",
                    gameObject = selectable.gameObject,
                    name = selectable.gameObject.name,
                    type = GetElementType(selectable),
                    text = GetElementText(selectable.gameObject),
                    bounds = bounds,
                    normalizedBounds = new Rect(
                        bounds.x / Screen.width,
                        bounds.y / Screen.height,
                        bounds.width / Screen.width,
                        bounds.height / Screen.height
                    ),
                    isEnabled = selectable.interactable,
                    componentType = selectable.GetType().FullName,
                    path = GetHierarchyPath(selectable.transform),
                    parentName = selectable.transform.parent?.name,
                    siblingIndex = selectable.transform.GetSiblingIndex(),
                    childCount = selectable.transform.childCount,
                    extraInfo = GetExtraInfo(selectable)
                };

                elements.Add(element);
            }

            // Sort by screen position (top-to-bottom, left-to-right reading order)
            return elements
                .OrderByDescending(e => e.bounds.y) // Top first
                .ThenBy(e => e.bounds.x) // Left first
                .ToList();
        }

        /// <summary>
        /// Builds a formatted element list string for the AI prompt.
        /// </summary>
        public static string BuildElementListPrompt(List<ElementInfo> elements)
        {
            if (elements == null || elements.Count == 0)
                return "No interactable UI elements found on screen.";

            var sb = new StringBuilder();
            sb.AppendLine($"Available UI elements ({elements.Count} total):");
            sb.AppendLine();

            foreach (var element in elements)
            {
                sb.AppendLine(element.ToAnnotation());
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets the element type string for an element.
        /// </summary>
        private static string GetElementType(Selectable selectable)
        {
            // Check for ScrollRect on the same GameObject (ScrollRect is not a Selectable)
            var scrollRect = selectable.GetComponent<ScrollRect>();
            if (scrollRect != null)
                return "scrollview";

            return selectable switch
            {
                Button => "button",
                Toggle toggle => toggle.isOn ? "toggle (on)" : "toggle (off)",
                Slider => "slider",
                TMP_InputField => "input",
                InputField => "input",
                TMP_Dropdown => "dropdown",
                Dropdown => "dropdown",
                Scrollbar => "scrollbar",
                _ => "selectable"
            };
        }

        /// <summary>
        /// Gets the visible text content of an element.
        /// </summary>
        private static string GetElementText(GameObject go)
        {
            // Check TMP_Text first (more common in modern Unity)
            var tmpText = go.GetComponentInChildren<TMP_Text>();
            if (tmpText != null && !string.IsNullOrWhiteSpace(tmpText.text))
                return tmpText.text.Trim();

            // Check legacy Text
            var legacyText = go.GetComponentInChildren<Text>();
            if (legacyText != null && !string.IsNullOrWhiteSpace(legacyText.text))
                return legacyText.text.Trim();

            // Check input field placeholder
            var tmpInput = go.GetComponent<TMP_InputField>();
            if (tmpInput != null)
            {
                if (!string.IsNullOrWhiteSpace(tmpInput.text))
                    return tmpInput.text.Trim();
                if (tmpInput.placeholder != null)
                {
                    var placeholderText = tmpInput.placeholder.GetComponent<TMP_Text>();
                    if (placeholderText != null && !string.IsNullOrWhiteSpace(placeholderText.text))
                        return $"(placeholder: {placeholderText.text.Trim()})";
                }
            }

            var legacyInput = go.GetComponent<InputField>();
            if (legacyInput != null)
            {
                if (!string.IsNullOrWhiteSpace(legacyInput.text))
                    return legacyInput.text.Trim();
                if (legacyInput.placeholder != null)
                {
                    var placeholderText = legacyInput.placeholder.GetComponent<Text>();
                    if (placeholderText != null && !string.IsNullOrWhiteSpace(placeholderText.text))
                        return $"(placeholder: {placeholderText.text.Trim()})";
                }
            }

            return null;
        }

        /// <summary>
        /// Gets extra info for specific element types.
        /// </summary>
        private static string GetExtraInfo(Selectable selectable)
        {
            // Check for ScrollRect on the same GameObject
            var scrollRectComponent = selectable.GetComponent<ScrollRect>();
            if (scrollRectComponent != null)
            {
                return $"scroll: ({scrollRectComponent.horizontalNormalizedPosition:F2}, {scrollRectComponent.verticalNormalizedPosition:F2})";
            }

            switch (selectable)
            {
                case Slider slider:
                    return $"value: {slider.normalizedValue:P0}";

                case Toggle toggle:
                    return toggle.isOn ? "checked" : "unchecked";

                case TMP_Dropdown dropdown:
                    if (dropdown.options.Count > 0 && dropdown.value < dropdown.options.Count)
                        return $"selected: {dropdown.options[dropdown.value].text}";
                    break;

                case Dropdown legacyDropdown:
                    if (legacyDropdown.options.Count > 0 && legacyDropdown.value < legacyDropdown.options.Count)
                        return $"selected: {legacyDropdown.options[legacyDropdown.value].text}";
                    break;
            }

            return null;
        }

        /// <summary>
        /// Gets the hierarchy path of a transform.
        /// </summary>
        private static string GetHierarchyPath(Transform transform)
        {
            var path = new StringBuilder();
            var current = transform;

            while (current != null)
            {
                if (path.Length > 0)
                    path.Insert(0, "/");
                path.Insert(0, current.name);
                current = current.parent;

                // Limit depth to avoid very long paths
                if (path.Length > 100)
                {
                    path.Insert(0, ".../");
                    break;
                }
            }

            return path.ToString();
        }

        /// <summary>
        /// Finds an element by its ID.
        /// </summary>
        public static ElementInfo FindElementById(List<ElementInfo> elements, string id)
        {
            return elements?.FirstOrDefault(e => e.id == id);
        }

        /// <summary>
        /// Finds elements by type.
        /// </summary>
        public static List<ElementInfo> FindElementsByType(List<ElementInfo> elements, string type)
        {
            return elements?.Where(e => e.type.StartsWith(type)).ToList() ?? new List<ElementInfo>();
        }

        /// <summary>
        /// Finds elements containing specific text.
        /// </summary>
        public static List<ElementInfo> FindElementsByText(List<ElementInfo> elements, string text)
        {
            var lowerText = text.ToLowerInvariant();
            return elements?.Where(e =>
                (!string.IsNullOrEmpty(e.text) && e.text.ToLowerInvariant().Contains(lowerText)) ||
                (!string.IsNullOrEmpty(e.name) && e.name.ToLowerInvariant().Contains(lowerText))
            ).ToList() ?? new List<ElementInfo>();
        }
    }
}
