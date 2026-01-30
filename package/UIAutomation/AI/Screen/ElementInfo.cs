using System;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ODDGames.UIAutomation.AI
{
    /// <summary>
    /// Structured information about a UI element for AI decision making.
    /// </summary>
    [Serializable]
    public class ElementInfo
    {
        /// <summary>Unique identifier for this element - descriptive like "Submit_button" or with disambiguation suffix</summary>
        public string id;

        /// <summary>Reference to the actual GameObject (for runtime click calculations)</summary>
        [NonSerialized]
        public GameObject gameObject;

        /// <summary>GameObject name</summary>
        public string name;

        /// <summary>Type of UI element: "button", "toggle", "slider", "input", "dropdown", "scrollview", "selectable"</summary>
        public string type;

        /// <summary>Visible text content (from Text, TMP_Text, or placeholder)</summary>
        public string text;

        /// <summary>Screen-space bounding box (in pixels)</summary>
        public Rect bounds;

        /// <summary>Normalized bounding box (0-1 range for both axes)</summary>
        public Rect normalizedBounds;

        /// <summary>Whether the element is currently interactable</summary>
        public bool isEnabled;

        /// <summary>Full component type name (e.g., "UnityEngine.UI.Button")</summary>
        public string componentType;

        /// <summary>Hierarchy path to the element</summary>
        public string path;

        /// <summary>Parent GameObject name</summary>
        public string parentName;

        /// <summary>Sibling index in parent</summary>
        public int siblingIndex;

        /// <summary>Number of children</summary>
        public int childCount;

        /// <summary>Additional info for specific element types (e.g., slider value, toggle state)</summary>
        public string extraInfo;

        /// <summary>Adjacent label text (e.g., "Username:" label next to an input field)</summary>
        public string adjacentLabel;

        /// <summary>Direction of the adjacent label relative to this element (left, right, above, below)</summary>
        public string adjacentDirection;

        /// <summary>Whether this element needs disambiguation (multiple elements with same search pattern)</summary>
        public bool needsDisambiguation;

        /// <summary>
        /// Creates a short annotation string for the element list.
        /// Shows the Search API pattern to find the element, plus type and state info.
        /// Coordinates are normalized (0.0-1.0) for use with click x/y parameters.
        /// </summary>
        public string ToAnnotation()
        {
            var sb = new StringBuilder();

            // Search pattern is the primary identifier - AI uses this in the 'search' parameter
            var searchPattern = GetSearchPattern(needsDisambiguation);
            sb.Append($"{searchPattern}");

            // Type info
            sb.Append($" [{type}]");

            // Add normalized coordinates (0.0-1.0 range)
            // Center of the element, Y flipped (0=top for intuitive coordinates)
            var centerX = normalizedBounds.x + normalizedBounds.width / 2;
            var centerY = 1f - (normalizedBounds.y + normalizedBounds.height / 2);  // Flip Y (0=top)
            sb.Append($" at ({centerX:F2},{centerY:F2})");

            // Extra info (slider value, toggle state, etc.)
            if (!string.IsNullOrEmpty(extraInfo))
            {
                sb.Append($" [{extraInfo}]");
            }

            if (!isEnabled)
            {
                sb.Append(" (disabled)");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Serializes the element to a JSON-like string.
        /// </summary>
        public string ToJson()
        {
            return $"{{\"id\":\"{id}\",\"type\":\"{type}\",\"name\":\"{EscapeString(name)}\",\"text\":\"{EscapeString(text ?? "")}\",\"x\":{normalizedBounds.x:F3},\"y\":{normalizedBounds.y:F3},\"width\":{normalizedBounds.width:F3},\"height\":{normalizedBounds.height:F3},\"enabled\":{(isEnabled ? "true" : "false")}}}";
        }

        private static string EscapeString(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }

        /// <summary>
        /// Generates a Search API pattern showing how to find this element in code.
        /// Used in the element annotation to teach the AI proper Search API usage.
        /// </summary>
        /// <param name="needsDisambiguation">If true, includes parent context (.Near) for disambiguation</param>
        public string GetSearchPattern(bool needsDisambiguation = false)
        {
            string basePattern;

            // Priority 1: Descriptive unique name (e.g., "VolumeSlider", "SubmitButton")
            // These are most reliable - prefer over adjacent labels
            if (!string.IsNullOrEmpty(name) && IsDescriptiveName(name))
            {
                var n = SanitizeForId(name);
                basePattern = $"Name(\"{n}\")";
            }
            // Priority 2: Unique text content (buttons with text like "Submit", "Cancel")
            else if (!string.IsNullOrEmpty(text) && !text.StartsWith("(placeholder"))
            {
                var txt = SanitizeForId(text);
                basePattern = $"Text(\"{txt}\")";
            }
            // Priority 3: Adjacent label (for generic-named form fields)
            else if (!string.IsNullOrEmpty(adjacentLabel) && !string.IsNullOrEmpty(adjacentDirection))
            {
                var label = SanitizeForId(adjacentLabel);
                basePattern = $"Adjacent(\"{label}\", {adjacentDirection})";
            }
            // Priority 4: Any name (even generic ones)
            else if (!string.IsNullOrEmpty(name))
            {
                var n = SanitizeForId(name);
                basePattern = $"Name(\"{n}\")";
            }
            // Fallback: Type with position hint
            else
            {
                var posHint = GetPositionHint();
                basePattern = $"Type<{type}>({posHint})";
            }

            // Add parent context for disambiguation if needed
            if (needsDisambiguation && !string.IsNullOrEmpty(parentName))
            {
                var parent = SanitizeForId(parentName);
                return $"{basePattern}.Near(\"{parent}\")";
            }

            return basePattern;
        }

        /// <summary>
        /// Checks if a name is descriptive (contains meaningful words, not just generic like "Slider" or "Button").
        /// </summary>
        private static bool IsDescriptiveName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            // Generic names that don't help identify the element
            var genericNames = new[] { "Slider", "Button", "Toggle", "Input", "InputField", "Dropdown", "ScrollView", "Image", "Text", "Panel" };

            // Check if the name is just a generic type name
            foreach (var generic in genericNames)
            {
                if (name.Equals(generic, System.StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // Name has more context (e.g., "VolumeSlider", "SubmitButton", "UsernameInput")
            return true;
        }

        /// <summary>
        /// Gets a position hint based on screen quadrant.
        /// </summary>
        private string GetPositionHint()
        {
            var x = normalizedBounds.x + normalizedBounds.width / 2;
            var y = normalizedBounds.y + normalizedBounds.height / 2;

            string hPos = x < 0.33f ? "left" : x > 0.66f ? "right" : "center";
            string vPos = y < 0.33f ? "bottom" : y > 0.66f ? "top" : "middle";

            return $"{vPos}_{hPos}";
        }

        /// <summary>
        /// Sanitizes a string for use in an ID (removes special chars, truncates).
        /// </summary>
        private static string SanitizeForId(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";

            // Truncate long strings
            if (str.Length > 25)
                str = str.Substring(0, 22) + "...";

            // Escape quotes
            str = str.Replace("\"", "\\\"");

            return str;
        }
    }
}
