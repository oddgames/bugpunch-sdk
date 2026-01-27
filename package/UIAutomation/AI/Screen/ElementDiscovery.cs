using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ODDGames.UIAutomation;

namespace ODDGames.UIAutomation.AI
{
    /// <summary>
    /// Discovers all interactable UI elements in the scene.
    /// </summary>
    public static class ElementDiscovery
    {
        /// <summary>
        /// Discovers all interactable UI elements and returns structured data for AI.
        /// Includes both Selectable components AND non-Selectable interactables
        /// (draggables, drop targets, custom clickables).
        /// </summary>
        public static List<ElementInfo> DiscoverElements()
        {
            var elements = new List<ElementInfo>();
            var selectables = Object.FindObjectsByType<Selectable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            int idCounter = 1;

            Debug.Log($"[ElementDiscovery] Found {selectables.Length} Selectable components in scene");

            int skippedInactive = 0, skippedCanvasGroup = 0, skippedBounds = 0;
            foreach (var selectable in selectables)
            {
                if (selectable == null) continue;
                if (!selectable.gameObject.activeInHierarchy)
                {
                    skippedInactive++;
                    continue;
                }

                // Check canvas group visibility
                var canvasGroup = selectable.GetComponentInParent<CanvasGroup>();
                if (canvasGroup != null && (canvasGroup.alpha <= 0 || !canvasGroup.interactable))
                {
                    skippedCanvasGroup++;
                    continue;
                }

                var bounds = InputInjector.GetScreenBounds(selectable.gameObject);
                if (bounds.width <= 0 || bounds.height <= 0)
                {
                    skippedBounds++;
                    Debug.Log($"[ElementDiscovery] Skipped '{selectable.gameObject.name}' - invalid bounds: {bounds}");
                    continue;
                }

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

            if (elements.Count == 0 && selectables.Length > 0)
            {
                Debug.LogWarning($"[ElementDiscovery] All {selectables.Length} selectables filtered out! " +
                    $"Inactive: {skippedInactive}, CanvasGroup: {skippedCanvasGroup}, InvalidBounds: {skippedBounds}");
            }
            else
            {
                Debug.Log($"[ElementDiscovery] Returning {elements.Count} Selectable elements " +
                    $"(filtered: inactive={skippedInactive}, canvasGroup={skippedCanvasGroup}, bounds={skippedBounds})");
            }

            // Also discover non-Selectable interactables (draggables, drop targets, custom clickables)
            var nonSelectables = ElementFinder.FindAllNonSelectableInteractables();
            Debug.Log($"[ElementDiscovery] Found {nonSelectables.Count} non-Selectable interactables");

            foreach (var interactable in nonSelectables)
            {
                if (interactable.GameObject == null) continue;

                var bounds = InputInjector.GetScreenBounds(interactable.GameObject);
                if (bounds.width <= 0 || bounds.height <= 0)
                    continue;

                var element = new ElementInfo
                {
                    id = $"e{idCounter++}",
                    gameObject = interactable.GameObject,
                    name = interactable.GameObject.name,
                    type = interactable.Type,
                    text = GetElementText(interactable.GameObject),
                    bounds = bounds,
                    normalizedBounds = new Rect(
                        bounds.x / Screen.width,
                        bounds.y / Screen.height,
                        bounds.width / Screen.width,
                        bounds.height / Screen.height
                    ),
                    isEnabled = true, // Non-selectables don't have interactable state
                    componentType = interactable.Type,
                    path = GetHierarchyPath(interactable.GameObject.transform),
                    parentName = interactable.GameObject.transform.parent?.name,
                    siblingIndex = interactable.GameObject.transform.GetSiblingIndex(),
                    childCount = interactable.GameObject.transform.childCount,
                    extraInfo = GetNonSelectableExtraInfo(interactable)
                };

                elements.Add(element);
            }

            // Find adjacent labels for elements that typically have them (inputs, sliders, etc.)
            var allTexts = FindAllTextLabels();
            foreach (var element in elements)
            {
                // Only find adjacent labels for certain element types
                if (element.type == "input" || element.type == "slider" || element.type == "dropdown" ||
                    element.type == "toggle (on)" || element.type == "toggle (off)")
                {
                    FindAdjacentLabel(element, allTexts);
                }
            }

            // Sort by screen position (top-to-bottom, left-to-right reading order)
            var sorted = elements
                .OrderByDescending(e => e.bounds.y) // Top first
                .ThenBy(e => e.bounds.x) // Left first
                .ToList();

            // Assign search pattern IDs and detect which elements need disambiguation
            AssignSearchPatternIds(sorted);

            return sorted;
        }

        /// <summary>
        /// Assigns search pattern IDs and marks elements that need disambiguation.
        /// The search pattern is used as the element identifier.
        /// </summary>
        private static void AssignSearchPatternIds(List<ElementInfo> elements)
        {
            // First pass: detect duplicate patterns
            var patternCounts = new Dictionary<string, List<ElementInfo>>();
            foreach (var element in elements)
            {
                var pattern = element.GetSearchPattern(needsDisambiguation: false);
                if (!patternCounts.ContainsKey(pattern))
                    patternCounts[pattern] = new List<ElementInfo>();
                patternCounts[pattern].Add(element);
            }

            // Mark elements that have duplicate patterns - they need disambiguation
            foreach (var kvp in patternCounts)
            {
                if (kvp.Value.Count > 1)
                {
                    foreach (var element in kvp.Value)
                    {
                        element.needsDisambiguation = true;
                    }
                }
            }

            // Second pass: assign IDs using search patterns (with disambiguation if needed)
            foreach (var element in elements)
            {
                element.id = element.GetSearchPattern(element.needsDisambiguation);
            }
        }

        /// <summary>
        /// Finds all text labels in the scene (non-interactable text elements).
        /// </summary>
        private static List<(string text, Rect bounds)> FindAllTextLabels()
        {
            var labels = new List<(string text, Rect bounds)>();

            // Find TMP_Text components
            var tmpTexts = Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var tmp in tmpTexts)
            {
                if (tmp == null || !tmp.gameObject.activeInHierarchy) continue;
                if (string.IsNullOrWhiteSpace(tmp.text)) continue;

                // Skip if it's part of an interactable (button text, input text, etc.)
                var selectable = tmp.GetComponentInParent<Selectable>();
                if (selectable != null) continue;

                var bounds = InputInjector.GetScreenBounds(tmp.gameObject);
                if (bounds.width > 0 && bounds.height > 0)
                {
                    labels.Add((tmp.text.Trim(), bounds));
                }
            }

            // Find legacy Text components
            var legacyTexts = Object.FindObjectsByType<Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var txt in legacyTexts)
            {
                if (txt == null || !txt.gameObject.activeInHierarchy) continue;
                if (string.IsNullOrWhiteSpace(txt.text)) continue;

                // Skip if it's part of an interactable
                var selectable = txt.GetComponentInParent<Selectable>();
                if (selectable != null) continue;

                var bounds = InputInjector.GetScreenBounds(txt.gameObject);
                if (bounds.width > 0 && bounds.height > 0)
                {
                    labels.Add((txt.text.Trim(), bounds));
                }
            }

            return labels;
        }

        /// <summary>
        /// Finds the closest adjacent label for an element.
        /// </summary>
        private static void FindAdjacentLabel(ElementInfo element, List<(string text, Rect bounds)> labels)
        {
            const float maxDistance = 150f; // Max pixels to search for adjacent labels
            float bestScore = float.MaxValue;
            string bestLabel = null;
            string bestDirection = null;

            var elementCenter = new Vector2(
                element.bounds.x + element.bounds.width / 2,
                element.bounds.y + element.bounds.height / 2
            );

            foreach (var (text, labelBounds) in labels)
            {
                var labelCenter = new Vector2(
                    labelBounds.x + labelBounds.width / 2,
                    labelBounds.y + labelBounds.height / 2
                );

                // Calculate horizontal and vertical distances
                float hDist = 0, vDist = 0;
                string direction = null;

                // Check if label is to the left
                if (labelBounds.x + labelBounds.width < element.bounds.x)
                {
                    hDist = element.bounds.x - (labelBounds.x + labelBounds.width);
                    vDist = Mathf.Abs(labelCenter.y - elementCenter.y);
                    if (hDist < maxDistance && vDist < element.bounds.height)
                        direction = "left";
                }
                // Check if label is to the right
                else if (labelBounds.x > element.bounds.x + element.bounds.width)
                {
                    hDist = labelBounds.x - (element.bounds.x + element.bounds.width);
                    vDist = Mathf.Abs(labelCenter.y - elementCenter.y);
                    if (hDist < maxDistance && vDist < element.bounds.height)
                        direction = "right";
                }
                // Check if label is above
                else if (labelBounds.y > element.bounds.y + element.bounds.height)
                {
                    vDist = labelBounds.y - (element.bounds.y + element.bounds.height);
                    hDist = Mathf.Abs(labelCenter.x - elementCenter.x);
                    if (vDist < maxDistance && hDist < element.bounds.width)
                        direction = "above";
                }
                // Check if label is below
                else if (labelBounds.y + labelBounds.height < element.bounds.y)
                {
                    vDist = element.bounds.y - (labelBounds.y + labelBounds.height);
                    hDist = Mathf.Abs(labelCenter.x - elementCenter.x);
                    if (vDist < maxDistance && hDist < element.bounds.width)
                        direction = "below";
                }

                if (direction != null)
                {
                    // Score based on distance (prefer closer labels, prefer left/above for form layouts)
                    float score = hDist + vDist;
                    if (direction == "left" || direction == "above")
                        score *= 0.8f; // Prefer left/above labels (typical form layout)

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestLabel = text;
                        bestDirection = direction;
                    }
                }
            }

            if (bestLabel != null)
            {
                element.adjacentLabel = bestLabel;
                element.adjacentDirection = bestDirection;
            }
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
        /// Gets extra info for non-Selectable interactables.
        /// </summary>
        private static string GetNonSelectableExtraInfo(ElementFinder.InteractableInfo interactable)
        {
            var parts = new List<string>();

            if (interactable.IsDraggable)
                parts.Add("can be dragged");
            if (interactable.IsDropTarget)
                parts.Add("accepts drops");
            if (interactable.IsClickable && !interactable.IsDraggable)
                parts.Add("clickable");

            return parts.Count > 0 ? string.Join(", ", parts) : null;
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
