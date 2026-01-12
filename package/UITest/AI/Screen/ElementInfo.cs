using System;
using System.Text;
using UnityEngine;

namespace ODDGames.UITest.AI
{
    /// <summary>
    /// Structured information about a UI element for AI decision making.
    /// </summary>
    [Serializable]
    public class ElementInfo
    {
        /// <summary>Unique identifier for this element in the current screen state (e.g., "e1", "e2")</summary>
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

        /// <summary>
        /// Creates a short annotation string for the element list.
        /// </summary>
        public string ToAnnotation()
        {
            var sb = new StringBuilder();
            sb.Append($"[{id}] {type}");

            if (!string.IsNullOrEmpty(text))
            {
                var truncatedText = text.Length > 30 ? text.Substring(0, 27) + "..." : text;
                sb.Append($": \"{truncatedText}\"");
            }
            else if (!string.IsNullOrEmpty(name))
            {
                sb.Append($": {name}");
            }

            sb.Append($" at ({normalizedBounds.x:F2},{normalizedBounds.y:F2})");

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
    }
}
