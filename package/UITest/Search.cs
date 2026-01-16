using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ODDGames.UITest
{
    /// <summary>
    /// Direction for adjacent/near element searches.
    /// </summary>
    public enum Direction
    {
        /// <summary>Find the nearest interactable to the right of the text (same row preferred).</summary>
        Right,
        /// <summary>Find the nearest interactable below the text.</summary>
        Below,
        /// <summary>Find the nearest interactable to the left of the text.</summary>
        Left,
        /// <summary>Find the nearest interactable above the text.</summary>
        Above
    }

    /// <summary>
    /// Screen regions for positional filtering. Screen is divided into a 3x3 grid.
    /// </summary>
    public enum ScreenRegion
    {
        /// <summary>Top-left third of the screen.</summary>
        TopLeft,
        /// <summary>Top-center third of the screen.</summary>
        TopCenter,
        /// <summary>Top-right third of the screen.</summary>
        TopRight,
        /// <summary>Middle-left third of the screen.</summary>
        MiddleLeft,
        /// <summary>Center of the screen.</summary>
        Center,
        /// <summary>Middle-right third of the screen.</summary>
        MiddleRight,
        /// <summary>Bottom-left third of the screen.</summary>
        BottomLeft,
        /// <summary>Bottom-center third of the screen.</summary>
        BottomCenter,
        /// <summary>Bottom-right third of the screen.</summary>
        BottomRight
    }

    /// <summary>
    /// Fluent builder for searching GameObjects. Supports wildcards (*) and OR patterns (|) in all string patterns.
    ///
    /// Patterns:
    ///   - Wildcard (*): "btn_*" matches btn_play, btn_settings; "*Button" matches PlayButton, SubmitButton
    ///   - OR (|): "OK|Okay|Confirm" matches any of those values; combinable with wildcards: "*Yes*|*OK*"
    ///
    /// From UITestBehaviour (using protected helpers):
    ///   await Click(Text("Play"));
    ///   await Click(Name("btn_*").Type&lt;Button&gt;());
    ///   await Click(Text("OK|Okay|Confirm"));
    ///
    /// From anywhere (using new Search()):
    ///   await Click(new Search().Name("btn_*").Type&lt;Button&gt;());
    ///   await Click(new Search().Type&lt;Button&gt;().Text("Start"));
    ///
    /// Hierarchy queries:
    ///   await Click(Name("Button").HasParent("Panel"));
    ///   await Click(Type&lt;Button&gt;().HasAncestor(Name("*Settings*")));
    /// </summary>
    public class Search : System.Collections.Generic.IEnumerable<Search>
    {
        readonly List<Func<GameObject, bool>> _conditions = new();
        readonly List<string> _descriptionParts = new();
        bool _nextNegate;
        bool _includeInactive;
        bool _includeDisabled;

        // Static path support
        string _staticPath;
        object _staticValue;
        bool _isStaticPath;

        /// <summary>
        /// Creates a new Search instance.
        /// </summary>
        /// <param name="textPattern">Optional text pattern to match. If provided, searches for visible text content.</param>
        /// <example>new Search("Play") - finds element with "Play" text</example>
        /// <example>new Search().Name("Button*").Type&lt;Button&gt;() - chain conditions</example>
        public Search(string textPattern = null)
        {
            if (!string.IsNullOrEmpty(textPattern))
            {
                Text(textPattern);
            }
        }

        /// <summary>
        /// Private constructor for static path searches.
        /// </summary>
        private Search(object staticValue, string path)
        {
            _isStaticPath = true;
            _staticPath = path;
            _staticValue = staticValue;
        }

        /// <summary>
        /// Gets whether this search should include inactive GameObjects.
        /// </summary>
        /// <value>True if inactive GameObjects will be included in search results; otherwise, false.</value>
        /// <example>
        /// <code>
        /// var search = new Search().Name("Panel").IncludeInactive();
        /// bool includesInactive = search.ShouldIncludeInactive; // true
        /// </code>
        /// </example>
        public bool ShouldIncludeInactive => _includeInactive;

        /// <summary>
        /// Gets whether this search should include disabled (non-interactable) components.
        /// </summary>
        /// <value>True if disabled components will be included in search results; otherwise, false.</value>
        /// <example>
        /// <code>
        /// var search = new Search().Type&lt;Button&gt;().IncludeDisabled();
        /// bool includesDisabled = search.ShouldIncludeDisabled; // true
        /// </code>
        /// </example>
        public bool ShouldIncludeDisabled => _includeDisabled;

        /// <summary>
        /// Returns a human-readable description of the search query.
        /// </summary>
        public override string ToString()
        {
            if (_descriptionParts.Count == 0)
                return "Search()";

            var result = string.Join(".", _descriptionParts);
            if (_includeInactive)
                result += ".IncludeInactive()";
            if (_includeDisabled)
                result += ".IncludeDisabled()";
            return result;
        }

        void AddDescription(string part)
        {
            if (_nextNegate)
                _descriptionParts.Add($"Not.{part}");
            else
                _descriptionParts.Add(part);
        }

        /// <summary>
        /// Include inactive (SetActive(false)) GameObjects in the search.
        /// By default, only active GameObjects are searched.
        /// </summary>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>Search.Name("HiddenPanel").IncludeInactive()</example>
        public Search IncludeInactive()
        {
            _includeInactive = true;
            return this;
        }

        /// <summary>
        /// Include disabled (interactable=false) components in the search.
        /// By default, only enabled/interactable components are searched.
        /// </summary>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>Search.Type&lt;Button&gt;().IncludeDisabled()</example>
        public Search IncludeDisabled()
        {
            _includeDisabled = true;
            return this;
        }

        #region Chainable Instance Methods

        /// <summary>
        /// Negates the next condition added to the search.
        /// </summary>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>Search.Type&lt;Button&gt;().Not.HasParent("DisabledPanel")</example>
        public Search Not { get { _nextNegate = true; return this; } }

        /// <summary>
        /// Match elements by GameObject name. Supports wildcards (*).
        /// </summary>
        /// <param name="pattern">The name pattern to match. Use * for wildcards.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Exact name match
        /// new Search().Name("PlayButton")
        ///
        /// // Wildcard prefix match
        /// new Search().Name("btn_*").Type&lt;Button&gt;()
        ///
        /// // Wildcard contains match
        /// new Search().Name("*Settings*")
        /// </code>
        /// </example>
        public Search Name(string pattern)
        {
            AddDescription($"Name(\"{pattern}\")");
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go => negate != WildcardMatch(go.name, pattern));
            return this;
        }

        /// <summary>
        /// Match elements by visible text content (TMP_Text or legacy Text) directly on the element. Supports wildcards (*).
        /// Only matches elements that have a text component directly attached - does not check children.
        /// Use HasChild(Text(...)) or HasDescendant(Text(...)) to find parents containing text.
        /// </summary>
        /// <param name="pattern">The text pattern to match. Use * for wildcards.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Exact text match on text element
        /// new Search().Text("Play")
        ///
        /// // Find button that has text child
        /// new Search().Type&lt;Button&gt;().HasChild(new Search().Text("Submit"))
        ///
        /// // Wildcard suffix match
        /// new Search().Text("Level *")
        /// </code>
        /// </example>
        public Search Text(string pattern)
        {
            AddDescription($"Text(\"{pattern}\")");
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go => negate != MatchText(go, pattern));
            return this;
        }

        /// <summary>
        /// Match elements that have a component of the specified type.
        /// </summary>
        /// <typeparam name="T">The component type to match (must derive from Component).</typeparam>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find all buttons
        /// new Search().Type&lt;Button&gt;()
        ///
        /// // Find buttons with specific name
        /// new Search().Type&lt;Button&gt;().Name("Submit")
        ///
        /// // Find sliders in a panel
        /// new Search().Type&lt;Slider&gt;().HasAncestor("SettingsPanel")
        /// </code>
        /// </example>
        public Search Type<T>() where T : Component
        {
            AddDescription($"Type<{typeof(T).Name}>()");
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go => negate != (go.GetComponent<T>() != null));
            return this;
        }

        /// <summary>
        /// Match elements by component type name (string-based). Supports wildcards (*).
        /// </summary>
        /// <param name="typeName">The component type name pattern to match. Use * for wildcards.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Match any component with "Button" in its type name
        /// new Search().Type("*Button*")
        ///
        /// // Match TMP components
        /// new Search().Type("TMP_*")
        /// </code>
        /// </example>
        public Search Type(string typeName)
        {
            AddDescription($"Type(\"{typeName}\")");
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go =>
            {
                var components = go.GetComponents<Component>();
                bool match = components.Any(c => c != null && WildcardMatch(c.GetType().Name, typeName));
                return negate != match;
            });
            return this;
        }

        /// <summary>
        /// Match elements by sprite name (Image or SpriteRenderer). Supports wildcards (*).
        /// </summary>
        /// <param name="pattern">The sprite name pattern to match. Use * for wildcards.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find elements with specific icon
        /// new Search().Sprite("icon_settings")
        ///
        /// // Find elements with icon prefix
        /// new Search().Sprite("icon_*")
        ///
        /// // Find button with specific sprite
        /// new Search().Type&lt;Button&gt;().Sprite("btn_primary")
        /// </code>
        /// </example>
        public Search Sprite(string pattern)
        {
            AddDescription($"Sprite(\"{pattern}\")");
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go => negate != MatchSprite(go, pattern));
            return this;
        }

        /// <summary>
        /// Match elements by their full hierarchy path. Supports wildcards (*).
        /// The path starts with "/" and includes all parent names separated by "/".
        /// </summary>
        /// <param name="pattern">The hierarchy path pattern to match. Use * for wildcards.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Match exact path
        /// new Search().Path("/Canvas/MainMenu/PlayButton")
        ///
        /// // Match any button under any Panel
        /// new Search().Path("*/Panel/Button*")
        ///
        /// // Match elements in Settings hierarchy
        /// new Search().Path("*Settings*")
        /// </code>
        /// </example>
        public Search Path(string pattern)
        {
            AddDescription($"Path(\"{pattern}\")");
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go => negate != WildcardMatch(GetHierarchyPath(go.transform), pattern));
            return this;
        }

        /// <summary>
        /// Match elements by their Unity tag.
        /// </summary>
        /// <param name="tag">The Unity tag to match (case-insensitive).</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find player tagged objects
        /// new Search().Tag("Player")
        ///
        /// // Find UI elements with custom tag
        /// new Search().Tag("Interactable").Type&lt;Button&gt;()
        /// </code>
        /// </example>
        public Search Tag(string tag)
        {
            AddDescription($"Tag(\"{tag}\")");
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go =>
            {
                bool match;
                try { match = go.CompareTag(tag); }
                catch { match = go.tag.Equals(tag, StringComparison.OrdinalIgnoreCase); }
                return negate != match;
            });
            return this;
        }

        /// <summary>
        /// Match elements by name, text content, sprite name, or hierarchy path.
        /// This is a broad search that checks multiple properties. Supports wildcards (*).
        /// </summary>
        /// <param name="pattern">The pattern to match against name, text, sprite, or path. Use * for wildcards.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find anything with "Settings" in name, text, sprite, or path
        /// new Search().Any("Settings*")
        ///
        /// // Broad search for play-related elements
        /// new Search().Any("*Play*")
        /// </code>
        /// </example>
        public Search Any(string pattern)
        {
            AddDescription($"Any(\"{pattern}\")");
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go =>
            {
                bool match = WildcardMatch(go.name, pattern)
                          || MatchText(go, pattern)
                          || MatchSprite(go, pattern)
                          || WildcardMatch(GetHierarchyPath(go.transform), pattern);
                return negate != match;
            });
            return this;
        }

        /// <summary>
        /// Add a condition based on a component property. Returns false if component doesn't exist.
        /// </summary>
        /// <typeparam name="T">The component type to check.</typeparam>
        /// <param name="predicate">Function that returns true if the component matches the condition.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>Search.Type&lt;Slider&gt;().With&lt;Slider&gt;(s => s.value > 0.5f)</example>
        public Search With<T>(Func<T, bool> predicate) where T : Component
        {
            AddDescription($"With<{typeof(T).Name}>(...)");
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go =>
            {
                var comp = go.GetComponent<T>();
                if (comp == null) return negate;
                bool match = predicate(comp);
                return negate != match;
            });
            return this;
        }

        /// <summary>
        /// Add a custom condition based on the GameObject.
        /// </summary>
        /// <param name="predicate">Function that returns true for matching GameObjects.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>new Search().Name("Item*").Where(go => go.activeInHierarchy)</example>
        public Search Where(Func<GameObject, bool> predicate)
        {
            AddDescription("Where(...)");
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go => negate ? !predicate(go) : predicate(go));
            return this;
        }

        /// <summary>
        /// Match elements whose immediate parent matches the given Search query.
        /// </summary>
        /// <param name="parentSearch">A Search query that the immediate parent must match.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find buttons whose parent is named "Toolbar"
        /// new Search().Type&lt;Button&gt;().HasParent(new Search().Name("Toolbar"))
        ///
        /// // Find elements whose parent has a LayoutGroup
        /// new Search().HasParent(new Search().Type&lt;LayoutGroup&gt;())
        /// </code>
        /// </example>
        public Search HasParent(Search parentSearch)
        {
            AddDescription($"HasParent({parentSearch})");
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go =>
            {
                if (go.transform.parent == null) return negate;
                bool match = parentSearch.Matches(go.transform.parent.gameObject);
                return negate != match;
            });
            return this;
        }

        /// <summary>
        /// Match elements whose immediate parent's name matches the pattern.
        /// This is a convenience overload for HasParent(new Search().Name(pattern)).
        /// </summary>
        /// <param name="parentNamePattern">The name pattern to match against the parent. Supports wildcards (*).</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find buttons directly under "Toolbar"
        /// new Search().Type&lt;Button&gt;().HasParent("Toolbar")
        ///
        /// // Find elements under any panel
        /// new Search().HasParent("*Panel*")
        /// </code>
        /// </example>
        public Search HasParent(string parentNamePattern)
        {
            return HasParent(new Search().Name(parentNamePattern));
        }

        /// <summary>
        /// Match elements that have any ancestor (parent, grandparent, etc.) matching the given Search query.
        /// Searches up the entire hierarchy chain.
        /// </summary>
        /// <param name="ancestorSearch">A Search query that any ancestor must match.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find buttons anywhere under a ScrollRect
        /// new Search().Type&lt;Button&gt;().HasAncestor(new Search().Type&lt;ScrollRect&gt;())
        ///
        /// // Find inputs under Settings panel
        /// new Search().Type&lt;TMP_InputField&gt;().HasAncestor(new Search().Name("*Settings*"))
        /// </code>
        /// </example>
        public Search HasAncestor(Search ancestorSearch)
        {
            AddDescription($"HasAncestor({ancestorSearch})");
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go =>
            {
                Transform current = go.transform.parent;
                while (current != null)
                {
                    if (ancestorSearch.Matches(current.gameObject))
                        return !negate;
                    current = current.parent;
                }
                return negate;
            });
            return this;
        }

        /// <summary>
        /// Match elements that have any ancestor with a name matching the pattern.
        /// This is a convenience overload for HasAncestor(new Search().Name(pattern)).
        /// </summary>
        /// <param name="ancestorNamePattern">The name pattern to match against ancestors. Supports wildcards (*).</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find buttons anywhere under SettingsPanel
        /// new Search().Type&lt;Button&gt;().HasAncestor("SettingsPanel")
        ///
        /// // Find elements somewhere in the menu hierarchy
        /// new Search().HasAncestor("*Menu*")
        /// </code>
        /// </example>
        public Search HasAncestor(string ancestorNamePattern)
        {
            return HasAncestor(new Search().Name(ancestorNamePattern));
        }

        /// <summary>
        /// Match elements that have any immediate (direct) child matching the given Search query.
        /// Only checks direct children, not descendants.
        /// </summary>
        /// <param name="childSearch">A Search query that any immediate child must match.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find panels that contain a Button as direct child
        /// new Search().Name("*Panel*").HasChild(new Search().Type&lt;Button&gt;())
        ///
        /// // Find containers with a specific icon child
        /// new Search().HasChild(new Search().Sprite("icon_star"))
        /// </code>
        /// </example>
        public Search HasChild(Search childSearch)
        {
            AddDescription($"HasChild({childSearch})");
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go =>
            {
                foreach (Transform child in go.transform)
                {
                    if (childSearch.Matches(child.gameObject))
                        return !negate;
                }
                return negate;
            });
            return this;
        }

        /// <summary>
        /// Match elements that have any immediate child with a name matching the pattern.
        /// This is a convenience overload for HasChild(new Search().Name(pattern)).
        /// </summary>
        /// <param name="childNamePattern">The name pattern to match against children. Supports wildcards (*).</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find panels that have an "Icon" child
        /// new Search().Name("*Panel*").HasChild("Icon")
        ///
        /// // Find containers with label children
        /// new Search().HasChild("*Label*")
        /// </code>
        /// </example>
        public Search HasChild(string childNamePattern)
        {
            return HasChild(new Search().Name(childNamePattern));
        }

        /// <summary>
        /// Match elements that have any descendant (child, grandchild, etc.) matching the given Search query.
        /// Recursively searches all children in the hierarchy.
        /// </summary>
        /// <param name="descendantSearch">A Search query that any descendant must match.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find panels that contain a Button anywhere in their hierarchy
        /// new Search().Name("*Panel*").HasDescendant(new Search().Type&lt;Button&gt;())
        ///
        /// // Find scroll views containing specific text
        /// new Search().Type&lt;ScrollRect&gt;().HasDescendant(new Search().Text("Load More"))
        /// </code>
        /// </example>
        public Search HasDescendant(Search descendantSearch)
        {
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go =>
            {
                bool found = HasMatchingDescendant(go.transform, descendantSearch);
                return negate != found;
            });
            return this;
        }

        /// <summary>
        /// Match elements that have any descendant with a name matching the pattern.
        /// This is a convenience overload for HasDescendant(new Search().Name(pattern)).
        /// </summary>
        /// <param name="descendantNamePattern">The name pattern to match against descendants. Supports wildcards (*).</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find panels that contain an element named "SubmitButton" somewhere
        /// new Search().Name("*Panel*").HasDescendant("SubmitButton")
        ///
        /// // Find containers with any icon descendant
        /// new Search().HasDescendant("*Icon*")
        /// </code>
        /// </example>
        public Search HasDescendant(string descendantNamePattern)
        {
            return HasDescendant(new Search().Name(descendantNamePattern));
        }

        /// <summary>
        /// Match elements that have a sibling (another child of the same parent) matching the given Search query.
        /// </summary>
        /// <param name="siblingSearch">A Search query that any sibling must match.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find input fields that have a sibling label
        /// new Search().Type&lt;TMP_InputField&gt;().HasSibling(new Search().Type&lt;TMP_Text&gt;())
        ///
        /// // Find buttons that have a sibling icon
        /// new Search().Type&lt;Button&gt;().HasSibling(new Search().Sprite("icon_*"))
        /// </code>
        /// </example>
        public Search HasSibling(Search siblingSearch)
        {
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go =>
            {
                var parent = go.transform.parent;
                if (parent == null) return negate;

                for (int i = 0; i < parent.childCount; i++)
                {
                    var sibling = parent.GetChild(i).gameObject;
                    if (sibling == go) continue;
                    if (siblingSearch.Matches(sibling))
                        return !negate;
                }
                return negate;
            });
            return this;
        }

        /// <summary>
        /// Match elements that have a sibling with a name matching the pattern.
        /// This is a convenience overload for HasSibling(new Search().Name(pattern)).
        /// </summary>
        /// <param name="siblingNamePattern">The name pattern to match against siblings. Supports wildcards (*).</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find elements that have a sibling named "Label"
        /// new Search().HasSibling("Label")
        ///
        /// // Find buttons that have a sibling with "Icon" in the name
        /// new Search().Type&lt;Button&gt;().HasSibling("*Icon*")
        /// </code>
        /// </example>
        public Search HasSibling(string siblingNamePattern)
        {
            return HasSibling(new Search().Name(siblingNamePattern));
        }

        /// <summary>
        /// Match elements that have a component of type T somewhere in their parent hierarchy.
        /// Searches up from the parent (excludes the element itself).
        /// </summary>
        /// <typeparam name="T">The component type to search for in ancestors.</typeparam>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find elements that are inside a ScrollRect
        /// new Search().Type&lt;Button&gt;().GetParent&lt;ScrollRect&gt;()
        ///
        /// // Find elements inside a LayoutGroup
        /// new Search().GetParent&lt;LayoutGroup&gt;()
        /// </code>
        /// </example>
        public Search GetParent<T>() where T : Component
        {
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go =>
            {
                var comp = go.GetComponentInParent<T>();
                if (comp != null && comp.gameObject == go)
                    comp = go.transform.parent?.GetComponentInParent<T>();
                bool match = comp != null;
                return negate != match;
            });
            return this;
        }

        /// <summary>
        /// Match elements that have a component of type T in the parent chain satisfying the predicate.
        /// Searches up from the parent (excludes the element itself).
        /// </summary>
        /// <typeparam name="T">The component type to search for in ancestors.</typeparam>
        /// <param name="predicate">A function that returns true for matching components.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find elements inside a vertical ScrollRect
        /// new Search().Type&lt;Button&gt;().GetParent&lt;ScrollRect&gt;(sr => sr.vertical)
        ///
        /// // Find elements in active canvases
        /// new Search().GetParent&lt;Canvas&gt;(c => c.enabled)
        /// </code>
        /// </example>
        public Search GetParent<T>(Func<T, bool> predicate) where T : Component
        {
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go =>
            {
                Transform current = go.transform.parent;
                while (current != null)
                {
                    var comp = current.GetComponent<T>();
                    if (comp != null && predicate(comp))
                        return !negate;
                    current = current.parent;
                }
                return negate;
            });
            return this;
        }

        /// <summary>
        /// Match elements that have a component of type T in any descendant (child, grandchild, etc.).
        /// Searches down through the entire child hierarchy (excludes the element itself).
        /// </summary>
        /// <typeparam name="T">The component type to search for in descendants.</typeparam>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find panels that contain a Button somewhere
        /// new Search().Name("*Panel*").GetChild&lt;Button&gt;()
        ///
        /// // Find containers with images
        /// new Search().GetChild&lt;Image&gt;()
        /// </code>
        /// </example>
        public Search GetChild<T>() where T : Component
        {
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go =>
            {
                var comp = go.GetComponentInChildren<T>();
                if (comp != null && comp.gameObject == go)
                {
                    comp = null;
                    foreach (Transform child in go.transform)
                    {
                        comp = child.GetComponentInChildren<T>();
                        if (comp != null) break;
                    }
                }
                bool match = comp != null;
                return negate != match;
            });
            return this;
        }

        /// <summary>
        /// Match elements that have a component of type T in any descendant satisfying the predicate.
        /// Searches down through the entire child hierarchy (excludes the element itself).
        /// </summary>
        /// <typeparam name="T">The component type to search for in descendants.</typeparam>
        /// <param name="predicate">A function that returns true for matching components.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find panels that contain an interactable button
        /// new Search().Name("*Panel*").GetChild&lt;Button&gt;(b => b.interactable)
        ///
        /// // Find containers with non-empty text
        /// new Search().GetChild&lt;TMP_Text&gt;(t => !string.IsNullOrEmpty(t.text))
        /// </code>
        /// </example>
        public Search GetChild<T>(Func<T, bool> predicate) where T : Component
        {
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go =>
            {
                bool found = FindMatchingChildComponent(go.transform, predicate, skipSelf: true);
                return negate != found;
            });
            return this;
        }

        /// <summary>
        /// Filter to elements located in the specified screen region.
        /// The screen is divided into a 3x3 grid for region matching.
        /// </summary>
        /// <param name="region">The screen region to filter by (TopLeft, Center, BottomRight, etc.).</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find buttons in the top-right corner
        /// new Search().Type&lt;Button&gt;().InRegion(ScreenRegion.TopRight)
        ///
        /// // Find center screen elements
        /// new Search().InRegion(ScreenRegion.Center)
        /// </code>
        /// </example>
        public Search InRegion(ScreenRegion region)
        {
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go =>
            {
                bool match = IsInScreenRegion(go, region);
                return negate != match;
            });
            return this;
        }

        /// <summary>
        /// Filter to elements within custom normalized screen bounds.
        /// Uses normalized coordinates where (0,0) is bottom-left and (1,1) is top-right.
        /// </summary>
        /// <param name="xMin">Minimum normalized X position (0-1, left to right).</param>
        /// <param name="yMin">Minimum normalized Y position (0-1, bottom to top).</param>
        /// <param name="xMax">Maximum normalized X position (0-1, left to right).</param>
        /// <param name="yMax">Maximum normalized Y position (0-1, bottom to top).</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find elements in the left half of the screen
        /// new Search().InRegion(0f, 0f, 0.5f, 1f)
        ///
        /// // Find elements in a custom region (bottom 25%)
        /// new Search().InRegion(0f, 0f, 1f, 0.25f)
        /// </code>
        /// </example>
        public Search InRegion(float xMin, float yMin, float xMax, float yMax)
        {
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go =>
            {
                bool match = IsInScreenBounds(go, xMin, yMin, xMax, yMax);
                return negate != match;
            });
            return this;
        }

        #endregion

        #region Ordering and Filtering

        int _skipCount;
        int _takeCount = -1;
        bool _takeLast;
        Func<IEnumerable<GameObject>, IEnumerable<GameObject>> _orderBy;
        Func<GameObject, GameObject> _targetTransform;

        /// <summary>
        /// Returns true if any post-processing (ordering, skip, take, target transform) is applied to this search.
        /// Post-processing methods include First(), Last(), Skip(), OrderBy(), OrderByDescending(), GetParent(), GetChild(), GetSibling().
        /// </summary>
        /// <value>True if post-processing will be applied to results; otherwise, false.</value>
        /// <example>
        /// <code>
        /// var search = new Search().Type&lt;Button&gt;().First();
        /// bool hasProcessing = search.HasPostProcessing; // true
        /// </code>
        /// </example>
        public bool HasPostProcessing => _orderBy != null || _skipCount > 0 || _takeCount > 0 || _targetTransform != null;

        /// <summary>
        /// Returns true if a target transform (GetParent(), GetChild(index), GetSibling(offset)) is applied.
        /// When a target transform is set, the matched element is transformed to a related element.
        /// </summary>
        /// <value>True if results will be transformed to related elements; otherwise, false.</value>
        /// <example>
        /// <code>
        /// var search = new Search().Name("Item").GetParent();
        /// bool hasTransform = search.HasTargetTransform; // true
        /// </code>
        /// </example>
        public bool HasTargetTransform => _targetTransform != null;

        /// <summary>
        /// Take only the first matching element by screen position (top-left to bottom-right).
        /// Automatically applies screen position ordering if no custom ordering is set.
        /// </summary>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Get the first (top-most, left-most) button
        /// new Search().Type&lt;Button&gt;().First()
        ///
        /// // Get the first item in a list
        /// new Search().Name("ListItem*").First()
        /// </code>
        /// </example>
        public Search First()
        {
            _takeCount = 1;
            _takeLast = false;
            _orderBy ??= OrderByScreenPosition;
            return this;
        }

        /// <summary>
        /// Take only the last matching element by screen position (bottom-right to top-left).
        /// Automatically applies screen position ordering if no custom ordering is set.
        /// </summary>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Get the last (bottom-most, right-most) button
        /// new Search().Type&lt;Button&gt;().Last()
        ///
        /// // Get the last item in a list
        /// new Search().Name("ListItem*").Last()
        /// </code>
        /// </example>
        public Search Last()
        {
            _takeCount = 1;
            _takeLast = true;
            _orderBy ??= OrderByScreenPosition;
            return this;
        }

        /// <summary>
        /// Skip the first N matching elements after ordering.
        /// Useful for pagination or selecting nth element.
        /// </summary>
        /// <param name="count">The number of elements to skip.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Get the second button (skip 1, take 1)
        /// new Search().Type&lt;Button&gt;().Skip(1).First()
        ///
        /// // Skip first 5 list items
        /// new Search().Name("ListItem*").Skip(5)
        /// </code>
        /// </example>
        public Search Skip(int count)
        {
            _skipCount = count;
            return this;
        }

        /// <summary>
        /// Take only the first N matching elements after ordering.
        /// Useful for limiting results or pagination.
        /// </summary>
        /// <param name="count">The number of elements to take.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Get first 3 buttons
        /// new Search().Type&lt;Button&gt;().Take(3)
        ///
        /// // Get first 5 list items after skipping 10
        /// new Search().Name("ListItem*").Skip(10).Take(5)
        /// </code>
        /// </example>
        public Search Take(int count)
        {
            _takeCount = count;
            _takeLast = false;
            return this;
        }

        /// <summary>
        /// Order matches by a component property value in ascending order.
        /// Elements without the component are placed at the end.
        /// </summary>
        /// <typeparam name="T">The component type to read the ordering value from.</typeparam>
        /// <param name="selector">A function that extracts a float value from the component for ordering.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Order sliders by their value (ascending)
        /// new Search().Type&lt;Slider&gt;().OrderBy&lt;Slider&gt;(s => s.value)
        ///
        /// // Order by RectTransform width
        /// new Search().OrderBy&lt;RectTransform&gt;(rt => rt.rect.width)
        /// </code>
        /// </example>
        public Search OrderBy<T>(Func<T, float> selector) where T : Component
        {
            _orderBy = objects => objects.OrderBy(go =>
            {
                var comp = go.GetComponent<T>();
                return comp != null ? selector(comp) : float.MaxValue;
            });
            return this;
        }

        /// <summary>
        /// Order matches by a component property value in descending order.
        /// Elements without the component are placed at the end.
        /// </summary>
        /// <typeparam name="T">The component type to read the ordering value from.</typeparam>
        /// <param name="selector">A function that extracts a float value from the component for ordering.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Order sliders by their value (descending)
        /// new Search().Type&lt;Slider&gt;().OrderByDescending&lt;Slider&gt;(s => s.value)
        ///
        /// // Order by RectTransform height (largest first)
        /// new Search().OrderByDescending&lt;RectTransform&gt;(rt => rt.rect.height)
        /// </code>
        /// </example>
        public Search OrderByDescending<T>(Func<T, float> selector) where T : Component
        {
            _orderBy = objects => objects.OrderByDescending(go =>
            {
                var comp = go.GetComponent<T>();
                return comp != null ? selector(comp) : float.MinValue;
            });
            return this;
        }

        /// <summary>
        /// Randomize the order of matching elements.
        /// Useful for testing different UI paths or selecting a random element from multiple matches.
        /// </summary>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Click a random button from matching set
        /// new Search().Type&lt;Button&gt;().Name("Option*").Randomize().First()
        ///
        /// // Select random list item
        /// new Search().Name("ListItem").Randomize().First()
        /// </code>
        /// </example>
        public Search Randomize()
        {
            _orderBy = objects => objects.OrderBy(_ => UnityEngine.Random.value);
            return this;
        }

        /// <summary>
        /// Order matches by screen position (top-to-bottom, left-to-right).
        /// This is the default ordering used by First() and Last() if no other ordering is specified.
        /// </summary>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Get all buttons sorted by screen position
        /// new Search().Type&lt;Button&gt;().OrderByPosition()
        ///
        /// // Get topmost button
        /// new Search().Type&lt;Button&gt;().OrderByPosition().First()
        /// </code>
        /// </example>
        public Search OrderByPosition()
        {
            _orderBy = OrderByScreenPosition;
            return this;
        }

        /// <summary>
        /// Filter to elements that are currently visible in the viewport.
        /// Elements that are scrolled off-screen or outside camera view are excluded.
        /// </summary>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Only buttons currently visible on screen
        /// new Search().Type&lt;Button&gt;().Visible()
        ///
        /// // First visible list item
        /// new Search().Name("ListItem*").Visible().First()
        /// </code>
        /// </example>
        public Search Visible()
        {
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go =>
            {
                bool match = IsVisibleInViewport(go);
                return negate != match;
            });
            return this;
        }

        /// <summary>
        /// Filter to elements that have an interactable Selectable component.
        /// Shorthand for .With&lt;Selectable&gt;(s => s.interactable).
        /// </summary>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Only interactable buttons
        /// new Search().Type&lt;Button&gt;().Interactable()
        ///
        /// // Interactable toggles that are off
        /// new Search().Type&lt;Toggle&gt;().Interactable().With&lt;Toggle&gt;(t => !t.isOn)
        /// </code>
        /// </example>
        public Search Interactable()
        {
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go =>
            {
                var selectable = go.GetComponent<Selectable>();
                bool match = selectable != null && selectable.interactable;
                return negate != match;
            });
            return this;
        }

        /// <summary>
        /// Combine this search with another using OR logic.
        /// An element matches if it satisfies either this search OR the other search.
        /// </summary>
        /// <param name="other">The other search to combine with.</param>
        /// <returns>A new Search instance representing the OR combination.</returns>
        /// <example>
        /// <code>
        /// // Match elements named "OK" OR with text "Confirm"
        /// new Search().Name("OK").Or(new Search().Text("Confirm"))
        ///
        /// // Match any of multiple button types
        /// new Search().Name("Submit").Or(Name("Save")).Or(Name("Apply"))
        /// </code>
        /// </example>
        public Search Or(Search other)
        {
            var combined = new Search();
            var thisSearch = this;
            combined._conditions.Add(go => thisSearch.Matches(go) || other.Matches(go));
            combined._includeInactive = _includeInactive || other._includeInactive;
            combined._includeDisabled = _includeDisabled || other._includeDisabled;
            // Copy post-processing from this search
            combined._orderBy = _orderBy;
            combined._skipCount = _skipCount;
            combined._takeCount = _takeCount;
            combined._takeLast = _takeLast;
            combined._targetTransform = _targetTransform;
            return combined;
        }

        /// <summary>
        /// Transform the matched element to its immediate parent.
        /// The final result will be the parent GameObject instead of the matched element.
        /// Returns null if the element has no parent.
        /// </summary>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find "Icon" elements, return their parents
        /// new Search().Name("Icon").GetParent()
        ///
        /// // Find buttons in a list, return their container
        /// new Search().Type&lt;Button&gt;().HasAncestor("List").GetParent()
        /// </code>
        /// </example>
        public Search GetParent()
        {
            _targetTransform = go => go.transform.parent?.gameObject;
            return this;
        }

        /// <summary>
        /// Transform the matched element to a child at the specified index.
        /// The final result will be the child GameObject instead of the matched element.
        /// Returns null if the index is out of range.
        /// </summary>
        /// <param name="index">The zero-based index of the child to select.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find "Container" and return its first child
        /// new Search().Name("Container").GetChild(0)
        ///
        /// // Find list items and return their third child
        /// new Search().Name("ListItem*").GetChild(2)
        /// </code>
        /// </example>
        public Search GetChild(int index)
        {
            _targetTransform = go =>
            {
                if (go.transform.childCount <= index || index < 0) return null;
                return go.transform.GetChild(index).gameObject;
            };
            return this;
        }

        /// <summary>
        /// Transform the matched element to a sibling at the specified offset.
        /// The final result will be the sibling GameObject instead of the matched element.
        /// Returns null if the offset results in an invalid index.
        /// </summary>
        /// <param name="offset">The offset from the current sibling index (positive = next siblings, negative = previous siblings).</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find "Label" elements and return the next sibling
        /// new Search().Name("Label").GetSibling(1)
        ///
        /// // Find "Button" and return the previous sibling
        /// new Search().Name("Button").GetSibling(-1)
        /// </code>
        /// </example>
        public Search GetSibling(int offset)
        {
            _targetTransform = go =>
            {
                var parent = go.transform.parent;
                if (parent == null) return null;
                int newIndex = go.transform.GetSiblingIndex() + offset;
                if (newIndex < 0 || newIndex >= parent.childCount) return null;
                return parent.GetChild(newIndex).gameObject;
            };
            return this;
        }

        #endregion

        #region Instance Chainable Methods

        /// <summary>
        /// Match elements that are the nearest interactable adjacent to a text label in the specified direction.
        /// Uses spatial proximity scoring to find the closest interactable (Button, InputField, Dropdown, Slider, Toggle).
        /// Prioritizes elements that are aligned with the text (same row for horizontal, same column for vertical).
        /// </summary>
        /// <param name="textPattern">The text pattern to find the label by. Supports wildcards (*).</param>
        /// <param name="direction">The direction from the text to search for interactables (default: Right).</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find the input field to the right of "Username:" label
        /// new Search().Adjacent("Username:", Direction.Right)
        ///
        /// // Find the button below "Actions" label
        /// new Search().Adjacent("Actions", Direction.Below)
        ///
        /// // Find slider to the right of "Volume:" (default direction)
        /// new Search().Adjacent("Volume:")
        /// </code>
        /// </example>
        public Search Adjacent(string textPattern, Direction direction = Direction.Right)
        {
            AddDescription($"Adjacent(\"{textPattern}\", {direction})");
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go =>
            {
                bool match = IsAdjacentToText(go, textPattern, direction);
                return negate != match;
            });
            return this;
        }

        /// <summary>
        /// Match elements that are the nearest interactable to a text label by pure distance.
        /// Unlike Adjacent, this uses simple Euclidean distance without alignment scoring.
        /// Optionally filters to a specific direction.
        /// </summary>
        /// <param name="textPattern">The text pattern to find the label by. Supports wildcards (*).</param>
        /// <param name="direction">Optional direction filter. If null, searches in all directions.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find the nearest interactable to "Settings" label (any direction)
        /// new Search().Near("Settings")
        ///
        /// // Find the nearest interactable below "Options" label
        /// new Search().Near("Options", Direction.Below)
        /// </code>
        /// </example>
        public Search Near(string textPattern, Direction? direction = null)
        {
            AddDescription(direction.HasValue ? $"Near(\"{textPattern}\", {direction.Value})" : $"Near(\"{textPattern}\")");
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go =>
            {
                bool match = IsNearElement(go, textPattern, direction);
                return negate != match;
            });

            // Order results by distance to the anchor text (closest first)
            if (!negate)
            {
                _orderBy = objects => objects.OrderBy(go => GetDistanceToNearestText(go, textPattern, direction));
            }

            return this;
        }

        #endregion

        #region Matching Logic

        /// <summary>
        /// Checks if the specified GameObject matches all conditions in this search.
        /// This evaluates all added conditions (Name, Type, HasParent, etc.) against the GameObject.
        /// </summary>
        /// <param name="go">The GameObject to test against the search conditions.</param>
        /// <returns>True if the GameObject matches all conditions; false if any condition fails or if go is null.</returns>
        /// <example>
        /// <code>
        /// var search = new Search().Name("Button*").Type&lt;Button&gt;();
        /// bool isMatch = search.Matches(someGameObject);
        /// </code>
        /// </example>
        public bool Matches(GameObject go)
        {
            if (go == null) return false;
            foreach (var condition in _conditions)
            {
                if (!condition(go)) return false;
            }
            return true;
        }

        /// <summary>
        /// Applies ordering, skip, take, and target transform to a collection of results.
        /// This is called internally after finding matching elements to apply post-processing operations.
        /// </summary>
        /// <param name="results">The collection of matching GameObjects to process.</param>
        /// <returns>The processed collection with ordering, skip, take, and transforms applied.</returns>
        /// <example>
        /// <code>
        /// var search = new Search().Type&lt;Button&gt;().First();
        /// var allButtons = FindAllMatching(search);
        /// var processed = search.ApplyPostProcessing(allButtons); // Returns only the first button
        /// </code>
        /// </example>
        public IEnumerable<GameObject> ApplyPostProcessing(IEnumerable<GameObject> results)
        {
            var processed = results;

            if (_orderBy != null)
                processed = _orderBy(processed);

            if (_skipCount > 0)
                processed = processed.Skip(_skipCount);

            if (_takeCount > 0)
            {
                processed = _takeLast
                    ? processed.TakeLast(_takeCount)
                    : processed.Take(_takeCount);
            }

            if (_targetTransform != null)
                processed = processed.Select(_targetTransform).Where(go => go != null);

            return processed;
        }

        /// <summary>
        /// Finds all GameObjects in the scene matching this search query.
        /// </summary>
        /// <param name="includeInactive">Whether to include inactive GameObjects. If null, uses IncludeInactive() setting.</param>
        /// <returns>List of matching GameObjects with post-processing applied.</returns>
        public List<GameObject> FindAll(bool? includeInactive = null)
        {
            bool searchInactive = includeInactive ?? ShouldIncludeInactive;
            var findMode = searchInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;

            var allMatching = UnityEngine.Object.FindObjectsByType<RectTransform>(findMode, FindObjectsSortMode.None)
                .Where(rt => rt != null && Matches(rt.gameObject))
                .Select(rt => rt.gameObject)
                .ToList();

            if (HasPostProcessing)
            {
                allMatching = ApplyPostProcessing(allMatching).ToList();
            }

            return allMatching;
        }

        /// <summary>
        /// Finds the first GameObject matching this search query.
        /// </summary>
        /// <param name="includeInactive">Whether to include inactive GameObjects. If null, uses IncludeInactive() setting.</param>
        /// <returns>The first matching GameObject, or null if none found.</returns>
        public GameObject FindFirst(bool? includeInactive = null)
        {
            var results = FindAll(includeInactive);
            return results.Count > 0 ? results[0] : null;
        }

        /// <summary>
        /// Gets the screen position of the first matching element.
        /// </summary>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <returns>Screen position of the element center, or null if not found.</returns>
        public Vector2? GetScreenPosition(int index = 0)
        {
            var results = FindAll();
            if (results.Count <= index) return null;

            return GetScreenPosition(results[index]);
        }

        /// <summary>Implicit conversion from string to Search.Text().</summary>
        public static implicit operator Search(string text) => new Search(text);

        #endregion

        #region Value Properties

        /// <summary>
        /// Gets the text content of the first matching element (TMP_Text, Text, or InputField).
        /// For static paths, returns ToString() of the value.
        /// </summary>
        public string StringValue
        {
            get
            {
                if (_isStaticPath)
                    return _staticValue as string ?? _staticValue?.ToString();

                var go = FindFirst();
                if (go == null) return null;

                var tmp = go.GetComponent<TMP_Text>();
                if (tmp != null) return tmp.text;

                var legacy = go.GetComponent<Text>();
                if (legacy != null) return legacy.text;

                var inputField = go.GetComponent<TMP_InputField>();
                if (inputField != null) return inputField.text;

                var legacyInput = go.GetComponent<InputField>();
                if (legacyInput != null) return legacyInput.text;

                return null;
            }
        }

        /// <summary>
        /// Gets the toggle state of the first matching element.
        /// For static paths, returns the bool value or false.
        /// </summary>
        public bool BoolValue
        {
            get
            {
                if (_isStaticPath)
                    return _staticValue is bool b && b;

                var go = FindFirst();
                if (go == null) return false;

                var toggle = go.GetComponent<Toggle>();
                return toggle != null && toggle.isOn;
            }
        }

        /// <summary>
        /// Gets the float value of the first matching element (Slider or Scrollbar).
        /// For static paths, converts the value to float.
        /// </summary>
        public float FloatValue
        {
            get
            {
                if (_isStaticPath)
                {
                    if (_staticValue == null) return 0f;
                    try { return Convert.ToSingle(_staticValue); }
                    catch { return 0f; }
                }

                var go = FindFirst();
                if (go == null) return 0f;

                var slider = go.GetComponent<Slider>();
                if (slider != null) return slider.value;

                var scrollbar = go.GetComponent<Scrollbar>();
                if (scrollbar != null) return scrollbar.value;

                return 0f;
            }
        }

        /// <summary>
        /// Gets the int value of the first matching element (Slider as int, Dropdown index, or text parsed as int).
        /// For static paths, converts the value to int.
        /// </summary>
        public int IntValue
        {
            get
            {
                if (_isStaticPath)
                {
                    if (_staticValue == null) return 0;
                    try { return Convert.ToInt32(_staticValue); }
                    catch { return 0; }
                }

                var go = FindFirst();
                if (go == null) return 0;

                var slider = go.GetComponent<Slider>();
                if (slider != null) return (int)slider.value;

                var dropdown = go.GetComponent<Dropdown>();
                if (dropdown != null) return dropdown.value;

                var tmpDropdown = go.GetComponent<TMP_Dropdown>();
                if (tmpDropdown != null) return tmpDropdown.value;

                // Try parsing text
                var tmp = go.GetComponent<TMP_Text>();
                if (tmp != null && int.TryParse(tmp.text, out var intVal))
                    return intVal;

                var legacy = go.GetComponent<Text>();
                if (legacy != null && int.TryParse(legacy.text, out intVal))
                    return intVal;

                return 0;
            }
        }

        /// <summary>
        /// Gets the dropdown options as a string array.
        /// For static paths, returns the array if it's a string[].
        /// </summary>
        public string[] ArrayValue
        {
            get
            {
                if (_isStaticPath)
                    return _staticValue as string[] ?? Array.Empty<string>();

                var go = FindFirst();
                if (go == null) return Array.Empty<string>();

                var dropdown = go.GetComponent<Dropdown>();
                if (dropdown != null)
                    return dropdown.options.Select(o => o.text).ToArray();

                var tmpDropdown = go.GetComponent<TMP_Dropdown>();
                if (tmpDropdown != null)
                    return tmpDropdown.options.Select(o => o.text).ToArray();

                return Array.Empty<string>();
            }
        }

        #endregion

        #region Helper Methods

        static string GetHierarchyPath(Transform transform)
        {
            if (transform == null) return string.Empty;
            var path = new StringBuilder();
            while (transform != null)
            {
                if (path.Length > 0) path.Insert(0, "/");
                path.Insert(0, transform.name);
                transform = transform.parent;
            }
            return "/" + path.ToString();
        }

        static bool WildcardMatch(string text, string pattern)
        {
            // Empty string pattern should never match (invalid search)
            if (pattern != null && pattern.Length == 0)
                return false;
            if (string.IsNullOrEmpty(pattern)) return true;
            if (string.IsNullOrEmpty(text)) return false;

            // Support OR logic with pipe separator: "OK|Okay|Confirm"
            if (pattern.Contains("|"))
            {
                string[] alternatives = pattern.Split('|');
                foreach (string alt in alternatives)
                {
                    if (WildcardMatchSingle(text, alt.Trim()))
                        return true;
                }
                return false;
            }

            return WildcardMatchSingle(text, pattern);
        }

        static bool WildcardMatchSingle(string text, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return true;
            if (string.IsNullOrEmpty(text)) return false;

            if (!pattern.Contains("*"))
                return text.Equals(pattern, StringComparison.OrdinalIgnoreCase);

            string[] parts = pattern.Split('*');
            int textIndex = 0;

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (string.IsNullOrEmpty(part)) continue;

                int foundIndex = text.IndexOf(part, textIndex, StringComparison.OrdinalIgnoreCase);
                if (foundIndex < 0) return false;

                if (i == 0 && !pattern.StartsWith("*") && foundIndex != 0)
                    return false;

                textIndex = foundIndex + part.Length;
            }

            if (!pattern.EndsWith("*") && parts.Length > 0)
            {
                string lastPart = parts[parts.Length - 1];
                if (!string.IsNullOrEmpty(lastPart))
                    return text.EndsWith(lastPart, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        static bool MatchText(GameObject go, string pattern)
        {
            // Check TMP_Text on this object only (not children)
            var tmpText = go.GetComponent<TMP_Text>();
            if (tmpText != null && WildcardMatch(tmpText.text, pattern))
                return true;

            // Check legacy Text on this object only (not children)
            var legacyText = go.GetComponent<Text>();
            if (legacyText != null && WildcardMatch(legacyText.text, pattern))
                return true;

            return false;
        }

        static bool MatchSprite(GameObject go, string pattern)
        {
            var image = go.GetComponentInChildren<Image>();
            if (image != null && image.sprite != null && WildcardMatch(image.sprite.name, pattern))
                return true;

            var sr = go.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.sprite != null && WildcardMatch(sr.sprite.name, pattern))
                return true;

            return false;
        }

        static bool HasMatchingDescendant(Transform parent, Search search)
        {
            foreach (Transform child in parent)
            {
                if (search.Matches(child.gameObject)) return true;
                if (HasMatchingDescendant(child, search)) return true;
            }
            return false;
        }

        static bool FindMatchingChildComponent<T>(Transform parent, Func<T, bool> predicate, bool skipSelf) where T : Component
        {
            if (!skipSelf)
            {
                var comp = parent.GetComponent<T>();
                if (comp != null && predicate(comp)) return true;
            }
            foreach (Transform child in parent)
            {
                if (FindMatchingChildComponent(child, predicate, skipSelf: false)) return true;
            }
            return false;
        }

        static Vector2 GetScreenPosition(GameObject go)
        {
            if (go.TryGetComponent<RectTransform>(out var rect))
            {
                Vector3[] corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                Vector3 center = (corners[0] + corners[2]) / 2f;

                var canvas = go.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                    return center;

                Camera cam = canvas?.worldCamera ?? Camera.main;
                return cam != null ? RectTransformUtility.WorldToScreenPoint(cam, center) : (Vector2)center;
            }

            if (go.TryGetComponent<Renderer>(out var renderer))
            {
                Camera cam = Camera.main;
                if (cam != null) return cam.WorldToScreenPoint(renderer.bounds.center);
            }

            {
                Camera cam = Camera.main;
                return cam != null ? (Vector2)cam.WorldToScreenPoint(go.transform.position) : Vector2.zero;
            }
        }

        static bool IsInScreenRegion(GameObject go, ScreenRegion region)
        {
            Vector2 screenPos = GetScreenPosition(go);
            float normalizedX = screenPos.x / Screen.width;
            float normalizedY = screenPos.y / Screen.height;

            return region switch
            {
                ScreenRegion.TopLeft => normalizedX < 0.333f && normalizedY > 0.666f,
                ScreenRegion.TopCenter => normalizedX >= 0.333f && normalizedX < 0.666f && normalizedY > 0.666f,
                ScreenRegion.TopRight => normalizedX >= 0.666f && normalizedY > 0.666f,
                ScreenRegion.MiddleLeft => normalizedX < 0.333f && normalizedY >= 0.333f && normalizedY <= 0.666f,
                ScreenRegion.Center => normalizedX >= 0.333f && normalizedX < 0.666f && normalizedY >= 0.333f && normalizedY <= 0.666f,
                ScreenRegion.MiddleRight => normalizedX >= 0.666f && normalizedY >= 0.333f && normalizedY <= 0.666f,
                ScreenRegion.BottomLeft => normalizedX < 0.333f && normalizedY < 0.333f,
                ScreenRegion.BottomCenter => normalizedX >= 0.333f && normalizedX < 0.666f && normalizedY < 0.333f,
                ScreenRegion.BottomRight => normalizedX >= 0.666f && normalizedY < 0.333f,
                _ => false
            };
        }

        static bool IsInScreenBounds(GameObject go, float xMin, float yMin, float xMax, float yMax)
        {
            Vector2 screenPos = GetScreenPosition(go);
            float normalizedX = screenPos.x / Screen.width;
            float normalizedY = screenPos.y / Screen.height;
            return normalizedX >= xMin && normalizedX <= xMax && normalizedY >= yMin && normalizedY <= yMax;
        }

        static bool IsVisibleInViewport(GameObject go)
        {
            Vector2 screenPos = GetScreenPosition(go);

            // Check if within screen bounds
            if (screenPos.x < 0 || screenPos.x > Screen.width ||
                screenPos.y < 0 || screenPos.y > Screen.height)
                return false;

            // For UI elements, check if within parent scroll rect viewport
            if (go.TryGetComponent<RectTransform>(out var rect))
            {
                var scrollRect = go.GetComponentInParent<ScrollRect>();
                if (scrollRect != null && scrollRect.viewport != null)
                {
                    Vector3[] elementCorners = new Vector3[4];
                    Vector3[] viewportCorners = new Vector3[4];
                    rect.GetWorldCorners(elementCorners);
                    scrollRect.viewport.GetWorldCorners(viewportCorners);

                    Rect elementRect = new Rect(elementCorners[0].x, elementCorners[0].y,
                        elementCorners[2].x - elementCorners[0].x, elementCorners[2].y - elementCorners[0].y);
                    Rect viewportRect = new Rect(viewportCorners[0].x, viewportCorners[0].y,
                        viewportCorners[2].x - viewportCorners[0].x, viewportCorners[2].y - viewportCorners[0].y);

                    // Element must overlap with viewport
                    if (!elementRect.Overlaps(viewportRect))
                        return false;
                }
            }

            return true;
        }

        static IEnumerable<GameObject> OrderByScreenPosition(IEnumerable<GameObject> objects)
        {
            return objects.OrderByDescending(go => GetScreenPosition(go).y)
                          .ThenBy(go => GetScreenPosition(go).x);
        }

        static List<(GameObject go, Rect bounds)> FindTextsMatchingPattern(string pattern)
        {
            var results = new List<(GameObject, Rect)>();

            var allTmpTexts = UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None);

            foreach (var tmp in allTmpTexts)
            {
                if (WildcardMatch(tmp.text, pattern))
                {
                    var rect = tmp.GetComponent<RectTransform>();
                    if (rect != null)
                    {
                        // Use actual text bounds, not RectTransform bounds
                        // textBounds is in local space, need to convert to world space
                        var textBounds = tmp.textBounds;
                        Vector3 worldCenter = tmp.transform.TransformPoint(textBounds.center);
                        Vector3 worldSize = tmp.transform.TransformVector(textBounds.size);
                        var bounds = new Rect(
                            worldCenter.x - Mathf.Abs(worldSize.x) / 2,
                            worldCenter.y - Mathf.Abs(worldSize.y) / 2,
                            Mathf.Abs(worldSize.x),
                            Mathf.Abs(worldSize.y));
                        results.Add((tmp.gameObject, bounds));
                    }
                }
            }

            var allLegacyTexts = UnityEngine.Object.FindObjectsByType<Text>(FindObjectsSortMode.None);
            foreach (var text in allLegacyTexts)
            {
                if (WildcardMatch(text.text, pattern))
                {
                    var rect = text.GetComponent<RectTransform>();
                    if (rect != null)
                    {
                        // For legacy Text, use preferred width/height for actual text size
                        var generator = text.cachedTextGenerator;
                        float width = generator.GetPreferredWidth(text.text, text.GetGenerationSettings(rect.rect.size));
                        float height = generator.GetPreferredHeight(text.text, text.GetGenerationSettings(rect.rect.size));

                        // Get position based on alignment and pivot
                        Vector3[] corners = new Vector3[4];
                        rect.GetWorldCorners(corners);
                        Vector2 rectCenter = new Vector2(
                            (corners[0].x + corners[2].x) / 2,
                            (corners[0].y + corners[2].y) / 2);

                        // Adjust center based on text alignment
                        float xOffset = 0;
                        if (text.alignment == TextAnchor.UpperLeft || text.alignment == TextAnchor.MiddleLeft || text.alignment == TextAnchor.LowerLeft)
                            xOffset = (rect.rect.width - width) / 2 * rect.lossyScale.x;
                        else if (text.alignment == TextAnchor.UpperRight || text.alignment == TextAnchor.MiddleRight || text.alignment == TextAnchor.LowerRight)
                            xOffset = -(rect.rect.width - width) / 2 * rect.lossyScale.x;

                        var bounds = new Rect(
                            rectCenter.x - width * rect.lossyScale.x / 2 - xOffset,
                            rectCenter.y - height * rect.lossyScale.y / 2,
                            width * rect.lossyScale.x,
                            height * rect.lossyScale.y);
                        results.Add((text.gameObject, bounds));
                    }
                }
            }

            return results;
        }

        static bool IsAdjacentToText(GameObject element, string textPattern, Direction direction)
        {
            var matchingTexts = FindTextsMatchingPattern(textPattern);
            if (matchingTexts.Count == 0)
                return false;

            // Find the best adjacent element for each matching text
            foreach (var (textGo, textBounds) in matchingTexts)
            {
                var bestElement = FindBestAdjacentElement(textGo, textBounds, direction);
                if (bestElement == element)
                    return true;
            }

            return false;
        }

        static GameObject FindBestAdjacentElement(GameObject sourceTextGo, Rect textBounds, Direction direction)
        {
            GameObject bestElement = null;
            float bestScore = float.MaxValue;

            var allRects = UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None);
            Vector2 textCenter = textBounds.center;

            foreach (var rect in allRects)
            {
                if (rect == null) continue;

                // Skip the source text and its parent hierarchy
                if (rect.gameObject == sourceTextGo || sourceTextGo.transform.IsChildOf(rect.transform))
                    continue;

                Vector3[] corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                var elementBounds = new Rect(corners[0].x, corners[0].y,
                    corners[2].x - corners[0].x, corners[2].y - corners[0].y);
                Vector2 elementCenter = elementBounds.center;

                // Check if element center is in the correct direction from text center
                bool isInDirection = direction switch
                {
                    Direction.Right => elementCenter.x >= textCenter.x,
                    Direction.Left => elementCenter.x <= textCenter.x,
                    Direction.Below => elementCenter.y <= textCenter.y,
                    Direction.Above => elementCenter.y >= textCenter.y,
                    _ => false
                };

                if (!isInDirection) continue;

                // Calculate edge-center to edge-center distance based on direction
                Vector2 elementEdgeCenter;
                Vector2 textEdgeCenter;
                switch (direction)
                {
                    case Direction.Below:
                        elementEdgeCenter = new Vector2(elementCenter.x, elementBounds.yMax); // top edge center
                        textEdgeCenter = new Vector2(textCenter.x, textBounds.yMin);          // bottom edge center
                        break;
                    case Direction.Above:
                        elementEdgeCenter = new Vector2(elementCenter.x, elementBounds.yMin); // bottom edge center
                        textEdgeCenter = new Vector2(textCenter.x, textBounds.yMax);          // top edge center
                        break;
                    case Direction.Right:
                        elementEdgeCenter = new Vector2(elementBounds.xMin, elementCenter.y); // left edge center
                        textEdgeCenter = new Vector2(textBounds.xMax, textCenter.y);          // right edge center
                        break;
                    case Direction.Left:
                        elementEdgeCenter = new Vector2(elementBounds.xMax, elementCenter.y); // right edge center
                        textEdgeCenter = new Vector2(textBounds.xMin, textCenter.y);          // left edge center
                        break;
                    default:
                        elementEdgeCenter = elementCenter;
                        textEdgeCenter = textCenter;
                        break;
                }
                float distance = Vector2.Distance(elementEdgeCenter, textEdgeCenter);

                // Calculate alignment penalty (misalignment in perpendicular axis)
                float alignmentPenalty = direction switch
                {
                    Direction.Right or Direction.Left => Mathf.Abs(elementCenter.y - textCenter.y),
                    Direction.Above or Direction.Below => Mathf.Abs(elementCenter.x - textCenter.x),
                    _ => 0
                };

                // Score: lower is better. Distance + alignment penalty weighted heavily
                float score = distance + alignmentPenalty * 3;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestElement = rect.gameObject;
                }
            }

            // Walk up hierarchy to find interactable parent if needed
            if (bestElement != null)
            {
                var current = bestElement.transform;
                while (current != null)
                {
                    var go = current.gameObject;
                    if (go.GetComponent<TMP_InputField>() != null ||
                        go.GetComponent<InputField>() != null ||
                        go.GetComponent<Button>() != null ||
                        go.GetComponent<Toggle>() != null ||
                        go.GetComponent<Slider>() != null ||
                        go.GetComponent<TMP_Dropdown>() != null ||
                        go.GetComponent<Dropdown>() != null ||
                        go.GetComponent<Selectable>() != null)
                    {
                        return go;
                    }
                    current = current.parent;
                }
            }

            return bestElement;
        }

        static bool IsNearElement(GameObject element, string textPattern, Direction? direction)
        {
            float distance = GetDistanceToNearestText(element, textPattern, direction);
            return distance < float.MaxValue;
        }

        static float GetDistanceToNearestText(GameObject element, string textPattern, Direction? direction)
        {
            var elementRect = element.GetComponent<RectTransform>();
            if (elementRect == null)
                return float.MaxValue;

            var matchingTexts = FindTextsMatchingPattern(textPattern);
            if (matchingTexts.Count == 0)
                return float.MaxValue;

            Vector3[] elementCorners = new Vector3[4];
            elementRect.GetWorldCorners(elementCorners);
            Vector2 elementCenter = new Vector2(
                (elementCorners[0].x + elementCorners[2].x) / 2,
                (elementCorners[0].y + elementCorners[2].y) / 2);
            Rect elementBounds = new Rect(
                elementCorners[0].x,
                elementCorners[0].y,
                elementCorners[2].x - elementCorners[0].x,
                elementCorners[2].y - elementCorners[0].y);

            // Find the closest matching anchor text to this element that satisfies direction constraint
            float closestDistance = float.MaxValue;
            foreach (var (textGo, textBounds) in matchingTexts)
            {
                // Calculate distance from element's edge center to text's edge center based on direction
                // For Below: element top edge center to text bottom edge center
                // For Above: element bottom edge center to text top edge center
                // For Right: element left edge center to text right edge center
                // For Left: element right edge center to text left edge center
                float distance;
                if (direction.HasValue)
                {
                    Vector2 elementEdgeCenter;
                    Vector2 textEdgeCenter;
                    switch (direction.Value)
                    {
                        case Direction.Below:
                            elementEdgeCenter = new Vector2(elementCenter.x, elementBounds.yMax); // top edge center
                            textEdgeCenter = new Vector2(textBounds.center.x, textBounds.yMin);   // bottom edge center
                            break;
                        case Direction.Above:
                            elementEdgeCenter = new Vector2(elementCenter.x, elementBounds.yMin); // bottom edge center
                            textEdgeCenter = new Vector2(textBounds.center.x, textBounds.yMax);   // top edge center
                            break;
                        case Direction.Right:
                            elementEdgeCenter = new Vector2(elementBounds.xMin, elementCenter.y); // left edge center
                            textEdgeCenter = new Vector2(textBounds.xMax, textBounds.center.y);   // right edge center
                            break;
                        case Direction.Left:
                            elementEdgeCenter = new Vector2(elementBounds.xMax, elementCenter.y); // right edge center
                            textEdgeCenter = new Vector2(textBounds.xMin, textBounds.center.y);   // left edge center
                            break;
                        default:
                            elementEdgeCenter = elementCenter;
                            textEdgeCenter = textBounds.center;
                            break;
                    }
                    distance = Vector2.Distance(elementEdgeCenter, textEdgeCenter);

                    // Check element center is in the correct direction from text center
                    bool isInDirection = direction.Value switch
                    {
                        Direction.Right => elementCenter.x >= textBounds.center.x,
                        Direction.Left => elementCenter.x <= textBounds.center.x,
                        Direction.Below => elementCenter.y <= textBounds.center.y,
                        Direction.Above => elementCenter.y >= textBounds.center.y,
                        _ => true
                    };
                    if (!isInDirection) continue;
                }
                else
                {
                    // No direction - use center to center distance
                    distance = Vector2.Distance(elementCenter, textBounds.center);
                }

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                }
            }

            return closestDistance;
        }

        #endregion

        #region Static Path Support

        /// <summary>
        /// Creates a Search that resolves a static path instead of searching for UI elements.
        /// Supports short type names if unique, or full namespace paths.
        /// </summary>
        /// <param name="path">Dot-separated path (e.g., "GameManager.Instance.Score" or "MyNamespace.GameManager.Instance.Score")</param>
        /// <returns>A Search representing the resolved value</returns>
        /// <example>
        /// // Access static values
        /// var score = Search.Static("GameManager.Instance.Score").IntValue;
        ///
        /// // Iterate over arrays
        /// foreach (var player in Search.Static("GameManager.Players"))
        ///     Debug.Log(player.Property("Name").StringValue);
        ///
        /// // Use with Assert
        /// ActionExecutor.Assert(Search.Static("GameManager.IsReady").Property("Value"), true);
        /// </example>
        public static Search Static(string path)
        {
            var value = ActionExecutor.ResolveStaticPathPublic(path);
            return new Search(value, path);
        }

        /// <summary>
        /// Navigates to a property or field and returns a new Search.
        /// Works for both static paths and UI element component properties.
        /// </summary>
        /// <param name="name">Property or field name</param>
        /// <returns>A new Search representing the property value</returns>
        public Search Property(string name)
        {
            if (_isStaticPath)
            {
                var value = NavigateProperty(_staticValue, name);
                return new Search(value, $"{_staticPath}.{name}");
            }

            // For UI elements, get the first match and navigate its component properties
            var go = FindFirst();
            if (go == null) return new Search(null, name);

            // Try to get property from any component
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                var type = comp.GetType();
                var prop = type.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (prop != null)
                    return new Search(prop.GetValue(comp), name);

                var field = type.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                    return new Search(field.GetValue(comp), name);
            }

            return new Search(null, name);
        }

        /// <summary>
        /// Gets a typed value from this Search.
        /// For static paths, returns the resolved value.
        /// For UI elements, returns component values (text, toggle state, slider value, etc.)
        /// </summary>
        public T GetValue<T>(string subPath = null)
        {
            object value;
            if (_isStaticPath)
            {
                value = string.IsNullOrEmpty(subPath) ? _staticValue : NavigateProperty(_staticValue, subPath);
            }
            else
            {
                // For UI elements, use the appropriate value property based on type
                var go = FindFirst();
                if (go == null) return default;

                value = typeof(T) switch
                {
                    Type t when t == typeof(string) => StringValue,
                    Type t when t == typeof(bool) => BoolValue,
                    Type t when t == typeof(float) => FloatValue,
                    Type t when t == typeof(int) => IntValue,
                    _ => null
                };
            }

            if (value == null) return default;
            if (value is T typedValue) return typedValue;

            try { return (T)Convert.ChangeType(value, typeof(T)); }
            catch { return default; }
        }

        /// <summary>
        /// Gets the raw value for static path searches.
        /// </summary>
        public object Value => _isStaticPath ? _staticValue : FindFirst();

        /// <summary>
        /// Iterates over array/list elements for static paths, or all matching elements for UI searches.
        /// </summary>
        public IEnumerator<Search> GetEnumerator()
        {
            if (_isStaticPath)
            {
                if (_staticValue == null)
                    yield break;

                if (_staticValue is System.Collections.IEnumerable enumerable && !(_staticValue is string))
                {
                    int idx = 0;
                    foreach (var item in enumerable)
                    {
                        if (item != null)
                            yield return new Search(item, $"{_staticPath}[{idx}]");
                        idx++;
                    }
                }
                else
                {
                    yield return this;
                }
            }
            else
            {
                // For UI searches, iterate over all matches
                foreach (var go in FindAll())
                {
                    var search = new Search();
                    search._conditions.Add(g => g == go);
                    search._descriptionParts.Add($"GameObject({go.name})");
                    yield return search;
                }
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private static object NavigateProperty(object current, string path)
        {
            if (current == null || string.IsNullOrEmpty(path))
                return current;

            var parts = path.Split('.');
            foreach (var part in parts)
            {
                if (current == null) return null;

                var type = current.GetType();
                var prop = type.GetProperty(part, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (prop != null)
                {
                    current = prop.GetValue(current);
                    continue;
                }

                var field = type.GetField(part, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    current = field.GetValue(current);
                    continue;
                }

                return null;
            }
            return current;
        }

        #endregion
    }
}
