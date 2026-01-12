using System;
using UnityEngine;

namespace ODDGames.UITest.VisualBuilder
{
    /// <summary>
    /// How to identify/select a UI element.
    /// </summary>
    public enum SelectorType
    {
        /// <summary>Match by GameObject name (supports wildcards)</summary>
        ByName,
        /// <summary>Match by visible text content</summary>
        ByText,
        /// <summary>Match by component type (Button, Toggle, etc.)</summary>
        ByType,
        /// <summary>Match by runtime element ID (e.g., "e1")</summary>
        ById,
        /// <summary>Match by hierarchy path</summary>
        ByPath
    }

    /// <summary>
    /// Configuration for selecting a UI element at runtime.
    /// Can be resolved to an actual element via ElementDiscovery.
    /// </summary>
    [Serializable]
    public class ElementSelector
    {
        /// <summary>How to identify the element</summary>
        public SelectorType type = SelectorType.ByName;

        /// <summary>Pattern to match (name, text, path, or type name)</summary>
        public string pattern;

        /// <summary>Runtime element ID from ElementDiscovery (e.g., "e1", "e2")</summary>
        public string elementId;

        /// <summary>Human-readable display name for the UI</summary>
        public string displayName;

        /// <summary>
        /// Creates a selector by element ID.
        /// </summary>
        public static ElementSelector ById(string id, string displayName = null)
        {
            return new ElementSelector
            {
                type = SelectorType.ById,
                elementId = id,
                displayName = displayName ?? id
            };
        }

        /// <summary>
        /// Creates a selector by name pattern.
        /// </summary>
        public static ElementSelector ByName(string namePattern, string displayName = null)
        {
            return new ElementSelector
            {
                type = SelectorType.ByName,
                pattern = namePattern,
                displayName = displayName ?? namePattern
            };
        }

        /// <summary>
        /// Creates a selector by text content.
        /// </summary>
        public static ElementSelector ByText(string textPattern, string displayName = null)
        {
            return new ElementSelector
            {
                type = SelectorType.ByText,
                pattern = textPattern,
                displayName = displayName ?? $"\"{textPattern}\""
            };
        }

        /// <summary>
        /// Creates a selector by component type.
        /// </summary>
        public static ElementSelector ByType(string typeName, string displayName = null)
        {
            return new ElementSelector
            {
                type = SelectorType.ByType,
                pattern = typeName,
                displayName = displayName ?? typeName
            };
        }

        /// <summary>
        /// Creates a selector by hierarchy path.
        /// </summary>
        public static ElementSelector ByPath(string path, string displayName = null)
        {
            return new ElementSelector
            {
                type = SelectorType.ByPath,
                pattern = path,
                displayName = displayName ?? path
            };
        }

        /// <summary>
        /// Gets a human-readable description of this selector.
        /// </summary>
        public string GetDisplayText()
        {
            if (!string.IsNullOrEmpty(displayName))
                return displayName;

            return type switch
            {
                SelectorType.ById => elementId ?? "(no id)",
                SelectorType.ByName => pattern ?? "(no name)",
                SelectorType.ByText => $"\"{pattern ?? ""}\"",
                SelectorType.ByType => pattern ?? "(no type)",
                SelectorType.ByPath => pattern ?? "(no path)",
                _ => "(unknown)"
            };
        }

        /// <summary>
        /// Creates a deep copy of this selector.
        /// </summary>
        public ElementSelector Clone()
        {
            return new ElementSelector
            {
                type = type,
                pattern = pattern,
                elementId = elementId,
                displayName = displayName
            };
        }

        /// <summary>
        /// Returns true if this selector has enough information to find an element.
        /// </summary>
        public bool IsValid()
        {
            return type switch
            {
                SelectorType.ById => !string.IsNullOrEmpty(elementId),
                SelectorType.ByName => !string.IsNullOrEmpty(pattern),
                SelectorType.ByText => !string.IsNullOrEmpty(pattern),
                SelectorType.ByType => !string.IsNullOrEmpty(pattern),
                SelectorType.ByPath => !string.IsNullOrEmpty(pattern),
                _ => false
            };
        }
    }
}
