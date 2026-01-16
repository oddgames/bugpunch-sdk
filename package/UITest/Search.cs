using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        // For SetValue support - tracks the parent object and member name
        object _parentObject;
        string _memberName;
        MemberInfo _memberInfo;

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
        private Search(object staticValue, string path, object parentObject = null, string memberName = null, MemberInfo memberInfo = null)
        {
            _isStaticPath = true;
            _staticPath = path;
            _staticValue = staticValue;
            _parentObject = parentObject;
            _memberName = memberName;
            _memberInfo = memberInfo;
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
        /// Match elements by their texture or sprite name. Supports wildcards (*) and OR (|).
        /// Searches all visual components: Image.sprite, RawImage.texture, SpriteRenderer.sprite,
        /// Renderer material textures (main texture, normal map, emission), and UI Toolkit backgrounds.
        /// </summary>
        /// <param name="pattern">The texture/sprite name pattern to match. Use * for wildcards.</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find UI elements with specific icon
        /// new Search().Texture("icon_settings")
        ///
        /// // Find RawImage with texture
        /// new Search().Texture("avatar_*")
        ///
        /// // Find 3D object with material texture
        /// new Search().Texture("wood_diffuse")
        ///
        /// // Find any visual with texture pattern
        /// new Search().Texture("*player*|*avatar*")
        /// </code>
        /// </example>
        public Search Texture(string pattern)
        {
            AddDescription($"Texture(\"{pattern}\")");
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go => negate != MatchTexture(go, pattern));
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
                          || MatchTexture(go, pattern)
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
        /// new Search().HasChild(new Search().Texture("icon_star"))
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
        /// new Search().Type&lt;Button&gt;().HasSibling(new Search().Texture("icon_*"))
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

            // Search all Transform objects (includes RectTransform) to support both UI and non-UI objects
            var allMatching = UnityEngine.Object.FindObjectsByType<Transform>(findMode, FindObjectsSortMode.None)
                .Where(t => t != null && Matches(t.gameObject))
                .Select(t => t.gameObject)
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
                {
                    if (_staticValue == null)
                        throw new InvalidOperationException($"StringValue failed: Static path '{_staticPath}' resolved to null");
                    return _staticValue as string ?? _staticValue.ToString();
                }

                var go = FindFirst();
                if (go == null)
                    throw new InvalidOperationException($"StringValue failed: No UI element found matching '{ToString()}'");

                var tmp = go.GetComponent<TMP_Text>();
                if (tmp != null) return tmp.text;

                var legacy = go.GetComponent<Text>();
                if (legacy != null) return legacy.text;

                var inputField = go.GetComponent<TMP_InputField>();
                if (inputField != null) return inputField.text;

                var legacyInput = go.GetComponent<InputField>();
                if (legacyInput != null) return legacyInput.text;

                throw new InvalidOperationException($"StringValue failed: Element '{go.name}' has no text component (TMP_Text, Text, TMP_InputField, InputField)");
            }
        }

        /// <summary>
        /// Gets the toggle state of the first matching element.
        /// For static paths, returns the bool value.
        /// </summary>
        public bool BoolValue
        {
            get
            {
                if (_isStaticPath)
                {
                    if (_staticValue == null)
                        throw new InvalidOperationException($"BoolValue failed: Static path '{_staticPath}' resolved to null");
                    if (_staticValue is bool b) return b;
                    throw new InvalidOperationException($"BoolValue failed: Static path '{_staticPath}' is not a bool (type: {_staticValue.GetType().Name})");
                }

                var go = FindFirst();
                if (go == null)
                    throw new InvalidOperationException($"BoolValue failed: No UI element found matching '{ToString()}'");

                var toggle = go.GetComponent<Toggle>();
                if (toggle != null) return toggle.isOn;

                throw new InvalidOperationException($"BoolValue failed: Element '{go.name}' has no Toggle component");
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
                    if (_staticValue == null)
                        throw new InvalidOperationException($"FloatValue failed: Static path '{_staticPath}' resolved to null");
                    try { return Convert.ToSingle(_staticValue); }
                    catch (Exception ex) { throw new InvalidOperationException($"FloatValue failed: Cannot convert '{_staticValue}' (type: {_staticValue.GetType().Name}) to float. {ex.Message}"); }
                }

                var go = FindFirst();
                if (go == null)
                    throw new InvalidOperationException($"FloatValue failed: No UI element found matching '{ToString()}'");

                var slider = go.GetComponent<Slider>();
                if (slider != null) return slider.value;

                var scrollbar = go.GetComponent<Scrollbar>();
                if (scrollbar != null) return scrollbar.value;

                throw new InvalidOperationException($"FloatValue failed: Element '{go.name}' has no Slider or Scrollbar component");
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
                    if (_staticValue == null)
                        throw new InvalidOperationException($"IntValue failed: Static path '{_staticPath}' resolved to null");
                    try { return Convert.ToInt32(_staticValue); }
                    catch (Exception ex) { throw new InvalidOperationException($"IntValue failed: Cannot convert '{_staticValue}' (type: {_staticValue.GetType().Name}) to int. {ex.Message}"); }
                }

                var go = FindFirst();
                if (go == null)
                    throw new InvalidOperationException($"IntValue failed: No UI element found matching '{ToString()}'");

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

                throw new InvalidOperationException($"IntValue failed: Element '{go.name}' has no Slider, Dropdown, or parseable text component");
            }
        }

        /// <summary>
        /// Gets a Vector3 value from the current search.
        /// For static paths, returns the value cast to Vector3.
        /// For UI elements, gets RectTransform position.
        /// </summary>
        public Vector3 Vector3Value
        {
            get
            {
                if (_isStaticPath)
                {
                    if (_staticValue == null)
                        throw new InvalidOperationException($"Vector3Value failed: Static path '{_staticPath}' resolved to null");
                    if (_staticValue is Vector3 v3) return v3;
                    if (_staticValue is Vector2 v2) return v2;
                    throw new InvalidOperationException($"Vector3Value failed: Static path '{_staticPath}' is not a Vector3 (type: {_staticValue.GetType().Name})");
                }

                var go = FindFirst();
                if (go == null)
                    throw new InvalidOperationException($"Vector3Value failed: No UI element found matching '{ToString()}'");

                var rt = go.GetComponent<RectTransform>();
                if (rt != null) return rt.anchoredPosition3D;

                return go.transform.position;
            }
        }

        /// <summary>
        /// Gets a Vector2 value from the current search.
        /// For static paths, returns the value cast to Vector2.
        /// For UI elements, gets RectTransform anchoredPosition.
        /// </summary>
        public Vector2 Vector2Value
        {
            get
            {
                if (_isStaticPath)
                {
                    if (_staticValue == null)
                        throw new InvalidOperationException($"Vector2Value failed: Static path '{_staticPath}' resolved to null");
                    if (_staticValue is Vector2 v2) return v2;
                    if (_staticValue is Vector3 v3) return new Vector2(v3.x, v3.y);
                    throw new InvalidOperationException($"Vector2Value failed: Static path '{_staticPath}' is not a Vector2 (type: {_staticValue.GetType().Name})");
                }

                var go = FindFirst();
                if (go == null)
                    throw new InvalidOperationException($"Vector2Value failed: No UI element found matching '{ToString()}'");

                var rt = go.GetComponent<RectTransform>();
                if (rt != null) return rt.anchoredPosition;

                return go.transform.position;
            }
        }

        /// <summary>
        /// Gets a Color value from the current search.
        /// For static paths, returns the value cast to Color.
        /// For UI elements, gets Image or Text color.
        /// </summary>
        public Color ColorValue
        {
            get
            {
                if (_isStaticPath)
                {
                    if (_staticValue == null)
                        throw new InvalidOperationException($"ColorValue failed: Static path '{_staticPath}' resolved to null");
                    if (_staticValue is Color c) return c;
                    if (_staticValue is Color32 c32) return c32;
                    throw new InvalidOperationException($"ColorValue failed: Static path '{_staticPath}' is not a Color (type: {_staticValue.GetType().Name})");
                }

                var go = FindFirst();
                if (go == null)
                    throw new InvalidOperationException($"ColorValue failed: No UI element found matching '{ToString()}'");

                var image = go.GetComponent<Image>();
                if (image != null) return image.color;

                var rawImage = go.GetComponent<RawImage>();
                if (rawImage != null) return rawImage.color;

                var tmp = go.GetComponent<TMP_Text>();
                if (tmp != null) return tmp.color;

                var text = go.GetComponent<Text>();
                if (text != null) return text.color;

                throw new InvalidOperationException($"ColorValue failed: Element '{go.name}' has no Image, RawImage, or Text component");
            }
        }

        /// <summary>
        /// Gets a Quaternion value from the current search.
        /// For static paths, returns the value cast to Quaternion.
        /// For UI elements, gets Transform rotation.
        /// </summary>
        public Quaternion QuaternionValue
        {
            get
            {
                if (_isStaticPath)
                {
                    if (_staticValue == null)
                        throw new InvalidOperationException($"QuaternionValue failed: Static path '{_staticPath}' resolved to null");
                    if (_staticValue is Quaternion q) return q;
                    throw new InvalidOperationException($"QuaternionValue failed: Static path '{_staticPath}' is not a Quaternion (type: {_staticValue.GetType().Name})");
                }

                var go = FindFirst();
                if (go == null)
                    throw new InvalidOperationException($"QuaternionValue failed: No UI element found matching '{ToString()}'");

                return go.transform.rotation;
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

        #region Spatial Helpers

        /// <summary>
        /// Gets the screen-space center position of the UI element.
        /// </summary>
        public Vector2 ScreenCenter
        {
            get
            {
                var go = FindFirst();
                if (go == null)
                    throw new InvalidOperationException($"ScreenCenter failed: No UI element found matching '{ToString()}'");

                var rt = go.GetComponent<RectTransform>();
                if (rt != null)
                {
                    var corners = new Vector3[4];
                    rt.GetWorldCorners(corners);
                    var center = (corners[0] + corners[2]) / 2f;
                    var canvas = rt.GetComponentInParent<Canvas>();
                    if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.worldCamera != null)
                        return RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, center);
                    return center;
                }

                // For 3D objects, project to screen
                var cam = Camera.main;
                if (cam != null)
                    return cam.WorldToScreenPoint(go.transform.position);

                return go.transform.position;
            }
        }

        /// <summary>
        /// Gets the screen-space bounds of the UI element as a Rect.
        /// </summary>
        public Rect ScreenBounds
        {
            get
            {
                var go = FindFirst();
                if (go == null)
                    throw new InvalidOperationException($"ScreenBounds failed: No UI element found matching '{ToString()}'");

                var rt = go.GetComponent<RectTransform>();
                if (rt != null)
                {
                    var corners = new Vector3[4];
                    rt.GetWorldCorners(corners);
                    var canvas = rt.GetComponentInParent<Canvas>();
                    if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.worldCamera != null)
                    {
                        for (int i = 0; i < 4; i++)
                            corners[i] = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, corners[i]);
                    }
                    float minX = Mathf.Min(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
                    float maxX = Mathf.Max(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
                    float minY = Mathf.Min(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
                    float maxY = Mathf.Max(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
                    return new Rect(minX, minY, maxX - minX, maxY - minY);
                }

                // For 3D objects, use renderer bounds projected to screen
                var renderer = go.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var cam = Camera.main;
                    if (cam != null)
                    {
                        var bounds = renderer.bounds;
                        var min = cam.WorldToScreenPoint(bounds.min);
                        var max = cam.WorldToScreenPoint(bounds.max);
                        return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
                    }
                }

                return new Rect(go.transform.position.x, go.transform.position.y, 0, 0);
            }
        }

        /// <summary>
        /// Gets the world-space position (3D).
        /// </summary>
        public Vector3 WorldPosition
        {
            get
            {
                if (_isStaticPath)
                {
                    if (_staticValue is Vector3 v3) return v3;
                    throw new InvalidOperationException($"WorldPosition failed: Static path '{_staticPath}' is not a Vector3");
                }

                var go = FindFirst();
                if (go == null)
                    throw new InvalidOperationException($"WorldPosition failed: No element found matching '{ToString()}'");

                return go.transform.position;
            }
        }

        /// <summary>
        /// Gets the world-space bounds (3D) from Renderer or Collider.
        /// </summary>
        public Bounds WorldBounds
        {
            get
            {
                if (_isStaticPath && _staticValue is Bounds b)
                    return b;

                var go = FindFirst();
                if (go == null)
                    throw new InvalidOperationException($"WorldBounds failed: No element found matching '{ToString()}'");

                var renderer = go.GetComponent<Renderer>();
                if (renderer != null) return renderer.bounds;

                var collider = go.GetComponent<Collider>();
                if (collider != null) return collider.bounds;

                // Fall back to position with zero size
                return new Bounds(go.transform.position, Vector3.zero);
            }
        }

        /// <summary>
        /// Returns true if this element is above another element in screen space.
        /// </summary>
        public bool IsAbove(Search other) => ScreenCenter.y > other.ScreenCenter.y;

        /// <summary>
        /// Returns true if this element is below another element in screen space.
        /// </summary>
        public bool IsBelow(Search other) => ScreenCenter.y < other.ScreenCenter.y;

        /// <summary>
        /// Returns true if this element is to the left of another element in screen space.
        /// </summary>
        public bool IsLeftOf(Search other) => ScreenCenter.x < other.ScreenCenter.x;

        /// <summary>
        /// Returns true if this element is to the right of another element in screen space.
        /// </summary>
        public bool IsRightOf(Search other) => ScreenCenter.x > other.ScreenCenter.x;

        /// <summary>
        /// Returns the screen-space distance between the centers of two elements.
        /// </summary>
        public float DistanceTo(Search other) => Vector2.Distance(ScreenCenter, other.ScreenCenter);

        /// <summary>
        /// Returns the world-space distance between two elements.
        /// </summary>
        public float WorldDistanceTo(Search other) => Vector3.Distance(WorldPosition, other.WorldPosition);

        /// <summary>
        /// Returns true if this element's screen bounds overlap with another element's bounds.
        /// </summary>
        public bool Overlaps(Search other) => ScreenBounds.Overlaps(other.ScreenBounds);

        /// <summary>
        /// Returns true if this element's screen bounds fully contain another element's bounds.
        /// </summary>
        public bool Contains(Search other)
        {
            var thisBounds = ScreenBounds;
            var otherBounds = other.ScreenBounds;
            return thisBounds.Contains(new Vector2(otherBounds.xMin, otherBounds.yMin)) &&
                   thisBounds.Contains(new Vector2(otherBounds.xMax, otherBounds.yMax));
        }

        /// <summary>
        /// Returns true if two elements are horizontally aligned (same Y center within tolerance).
        /// </summary>
        public bool IsHorizontallyAligned(Search other, float tolerance = 10f)
            => Mathf.Abs(ScreenCenter.y - other.ScreenCenter.y) <= tolerance;

        /// <summary>
        /// Returns true if two elements are vertically aligned (same X center within tolerance).
        /// </summary>
        public bool IsVerticallyAligned(Search other, float tolerance = 10f)
            => Mathf.Abs(ScreenCenter.x - other.ScreenCenter.x) <= tolerance;

        /// <summary>
        /// Returns true if this element is in front of another in world space (closer to camera).
        /// </summary>
        public bool IsInFrontOf(Search other)
        {
            var cam = Camera.main;
            if (cam == null) return false;
            var thisDistance = Vector3.Distance(cam.transform.position, WorldPosition);
            var otherDistance = Vector3.Distance(cam.transform.position, other.WorldPosition);
            return thisDistance < otherDistance;
        }

        /// <summary>
        /// Returns true if this element is behind another in world space (further from camera).
        /// </summary>
        public bool IsBehind(Search other) => !IsInFrontOf(other);

        /// <summary>
        /// Returns true if this element's world bounds intersect with another element's bounds.
        /// </summary>
        public bool WorldIntersects(Search other) => WorldBounds.Intersects(other.WorldBounds);

        /// <summary>
        /// Returns true if this element's world bounds fully contain another element's bounds.
        /// </summary>
        public bool WorldContains(Search other)
        {
            var thisBounds = WorldBounds;
            var otherBounds = other.WorldBounds;
            return thisBounds.Contains(otherBounds.min) && thisBounds.Contains(otherBounds.max);
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

        // Cached shader property IDs for texture lookups (initialized once)
        static readonly int[] TexturePropertyIds = new[]
        {
            Shader.PropertyToID("_MainTex"),
            Shader.PropertyToID("_BaseMap"),
            Shader.PropertyToID("_BaseColorMap"),
            Shader.PropertyToID("_BumpMap"),
            Shader.PropertyToID("_NormalMap"),
            Shader.PropertyToID("_DetailNormalMap"),
            Shader.PropertyToID("_EmissionMap"),
            Shader.PropertyToID("_EmissiveColorMap"),
            Shader.PropertyToID("_MetallicGlossMap"),
            Shader.PropertyToID("_MaskMap"),
            Shader.PropertyToID("_OcclusionMap"),
            Shader.PropertyToID("_ParallaxMap"),
            Shader.PropertyToID("_HeightMap"),
            Shader.PropertyToID("_DetailAlbedoMap"),
            Shader.PropertyToID("_DetailMask"),
            Shader.PropertyToID("_SpecGlossMap"),
        };

        static bool MatchTexture(GameObject go, string pattern)
        {
            // Unity UI Image (uses Sprite)
            var image = go.GetComponentInChildren<Image>();
            if (image != null && image.sprite != null && WildcardMatch(image.sprite.name, pattern))
                return true;

            // Unity UI RawImage (uses Texture)
            var rawImage = go.GetComponentInChildren<RawImage>();
            if (rawImage != null && rawImage.texture != null && WildcardMatch(rawImage.texture.name, pattern))
                return true;

            // 2D SpriteRenderer (uses Sprite) - check sprite name directly
            var sr = go.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.sprite != null && WildcardMatch(sr.sprite.name, pattern))
                return true;

            // All Renderer types (MeshRenderer, SkinnedMeshRenderer, ParticleSystemRenderer,
            // LineRenderer, TrailRenderer, SpriteRenderer, BillboardRenderer, etc.)
            var renderers = go.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;

                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null) continue;

                    // Check main texture (uses cached property internally)
                    if (mat.mainTexture != null && WildcardMatch(mat.mainTexture.name, pattern))
                        return true;

                    // Check common texture properties using cached property IDs
                    foreach (var propId in TexturePropertyIds)
                    {
                        if (mat.HasProperty(propId))
                        {
                            var tex = mat.GetTexture(propId);
                            if (tex != null && WildcardMatch(tex.name, pattern))
                                return true;
                        }
                    }
                }
            }

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

            // Adjacent() is for UI form fields, so we search RectTransform (uses GetWorldCorners)
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
            string context = _staticPath ?? ToString();
            const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            if (_isStaticPath)
            {
                if (_staticValue == null)
                    throw new InvalidOperationException($"Property failed: Static path '{_staticPath}' resolved to null, cannot access property '{name}'");

                // If _staticValue is a Type, access static members of that type
                if (_staticValue is Type typeRef)
                {
                    var value = NavigatePropertyOnType(typeRef, null, name);
                    if (value == null)
                        throw new InvalidOperationException($"Property failed: Static property/field '{name}' not found on type '{typeRef.Name}'");
                    return new Search(value, $"{_staticPath}.{name}");
                }

                // Get the value and track parent/member for SetValue support
                var parentType = _staticValue.GetType();
                var propInfo = parentType.GetProperty(name, bindingFlags);
                if (propInfo != null)
                {
                    var value = propInfo.GetValue(_staticValue);
                    return new Search(value, $"{_staticPath}.{name}", _staticValue, name, propInfo);
                }

                var fieldInfo = parentType.GetField(name, bindingFlags);
                if (fieldInfo != null)
                {
                    var value = fieldInfo.GetValue(_staticValue);
                    return new Search(value, $"{_staticPath}.{name}", _staticValue, name, fieldInfo);
                }

                throw new InvalidOperationException($"Property failed: Property '{name}' not found on '{_staticPath}' (type: {_staticValue.GetType().Name})");
            }

            // For UI elements, get the first match and navigate its component properties
            var go = FindFirst();
            if (go == null)
                throw new InvalidOperationException($"Property failed: No UI element found matching '{context}', cannot access property '{name}'");

            // Try to get property from any component
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                var type = comp.GetType();
                var prop = type.GetProperty(name, bindingFlags);
                if (prop != null)
                    return new Search(prop.GetValue(comp), name, comp, name, prop);

                var field = type.GetField(name, bindingFlags);
                if (field != null)
                    return new Search(field.GetValue(comp), name, comp, name, field);
            }

            var componentNames = string.Join(", ", go.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name));
            throw new InvalidOperationException($"Property failed: Property/field '{name}' not found on any component of '{go.name}'. Components: [{componentNames}]");
        }

        /// <summary>
        /// Gets a component of the specified type from the matched GameObject and returns it as a Search.
        /// This allows chaining Property() and SetValue() on the component.
        /// </summary>
        /// <typeparam name="T">The component type to get</typeparam>
        /// <returns>A new Search wrapping the component</returns>
        /// <example>
        /// <code>
        /// // Get Rigidbody and set isKinematic
        /// Search("CarController").Component&lt;Rigidbody&gt;().Property("isKinematic").SetValue(true);
        ///
        /// // Get from static path result
        /// Static("GameModeDrag.Instance.competitorControllers[1]")
        ///     .Component&lt;Rigidbody&gt;().Property("isKinematic").SetValue(true);
        /// </code>
        /// </example>
        public Search Component<T>() where T : Component
        {
            return Component(typeof(T).Name);
        }

        /// <summary>
        /// Gets a component by type name from the matched GameObject and returns it as a Search.
        /// This allows chaining Property() and SetValue() on the component.
        /// </summary>
        /// <param name="typeName">The component type name (e.g., "Rigidbody", "BoxCollider")</param>
        /// <returns>A new Search wrapping the component</returns>
        /// <example>
        /// <code>
        /// // Get Rigidbody by name and set isKinematic
        /// Search("CarController").Component("Rigidbody").Property("isKinematic").SetValue(true);
        ///
        /// // Works with custom component types
        /// Search("Player").Component("PlayerStats").Property("health").SetValue(100f);
        /// </code>
        /// </example>
        public Search Component(string typeName)
        {
            string context = _staticPath ?? ToString();
            GameObject go = null;

            // If this is a static path, try to get GameObject from the resolved value
            if (_isStaticPath)
            {
                if (_staticValue == null)
                    throw new InvalidOperationException($"Component failed: Static path '{_staticPath}' resolved to null, cannot get component '{typeName}'");

                // If _staticValue is already a Component, get its GameObject
                if (_staticValue is Component comp)
                    go = comp.gameObject;
                else if (_staticValue is GameObject gameObj)
                    go = gameObj;
                else
                    throw new InvalidOperationException($"Component failed: Static path '{_staticPath}' resolved to '{_staticValue.GetType().Name}', not a Component or GameObject. Cannot get component '{typeName}'");
            }
            else
            {
                // For UI element searches, find the first match
                go = FindFirst();
                if (go == null)
                    throw new InvalidOperationException($"Component failed: No UI element found matching '{context}', cannot get component '{typeName}'");
            }

            // Find the component by type name
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                var cType = c.GetType();
                // Match by exact name or by full name ending with the type name
                if (cType.Name == typeName || cType.FullName == typeName || cType.Name.EndsWith(typeName))
                {
                    var newPath = _isStaticPath ? $"{_staticPath}.GetComponent<{typeName}>()" : $"{context}.Component(\"{typeName}\")";
                    return new Search(c, newPath);
                }
            }

            var componentNames = string.Join(", ", go.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name));
            throw new InvalidOperationException($"Component failed: Component '{typeName}' not found on '{go.name}'. Available components: [{componentNames}]");
        }

        /// <summary>
        /// Accesses an element by index (for arrays, lists, dictionaries, or types with indexers).
        /// Works for static paths and values obtained via Property() from UI elements.
        /// </summary>
        /// <param name="index">The integer index to access</param>
        /// <returns>A new Search representing the indexed value</returns>
        /// <example>
        /// <code>
        /// // Access array element from static path
        /// var item = Search.Static("GameManager.Instance").Property("Players").Index(0);
        ///
        /// // Access list element
        /// var weapon = Search.Static("Inventory.Items").Index(2).Property("Name").StringValue;
        ///
        /// // Access array property from UI element component
        /// var firstItem = new Search().Name("Inventory").Property("Items").Index(0).StringValue;
        ///
        /// // Chain with other accessors
        /// var name = Search.Static("Config.Settings").Property("Profiles").Index(0).Property("Name").StringValue;
        /// </code>
        /// </example>
        public Search Index(int index)
        {
            string context = _staticPath ?? ToString();

            if (!_isStaticPath)
                throw new InvalidOperationException($"Index failed: Index access is only supported for static paths, not UI element searches ('{context}')");

            if (_staticValue == null)
                throw new InvalidOperationException($"Index failed: Static path '{_staticPath}' resolved to null, cannot access index [{index}]");

            var value = AccessIndexer(_staticValue, index);
            return new Search(value, $"{_staticPath}[{index}]");
        }

        /// <summary>
        /// Accesses an element by string key (for dictionaries or types with string indexers).
        /// Works for static paths and values obtained via Property() from UI elements.
        /// </summary>
        /// <param name="key">The string key to access</param>
        /// <returns>A new Search representing the indexed value</returns>
        /// <example>
        /// <code>
        /// // Access dictionary element
        /// var player = Search.Static("GameManager.Instance").Property("PlayersByName").Index("Player1");
        ///
        /// // Access dictionary property from UI element component
        /// var value = new Search().Name("Config").Property("Settings").Index("volume").FloatValue;
        ///
        /// // Chain with other accessors
        /// var score = Search.Static("Leaderboard.Scores").Index("TopPlayer").IntValue;
        /// </code>
        /// </example>
        public Search Index(string key)
        {
            string context = _staticPath ?? ToString();

            if (!_isStaticPath)
                throw new InvalidOperationException($"Index failed: Index access is only supported for static paths, not UI element searches ('{context}')");

            if (_staticValue == null)
                throw new InvalidOperationException($"Index failed: Static path '{_staticPath}' resolved to null, cannot access index [\"{key}\"]");

            var value = AccessIndexer(_staticValue, key);
            return new Search(value, $"{_staticPath}[\"{key}\"]");
        }

        /// <summary>
        /// Indexer for accessing elements by integer index.
        /// Equivalent to calling Index(int).
        /// </summary>
        /// <param name="index">The integer index to access</param>
        /// <returns>A new Search representing the indexed value</returns>
        /// <example>
        /// <code>
        /// // Access array element using indexer syntax
        /// var item = Search.Static("GameManager.Instance").Property("Players")[0];
        ///
        /// // Chain with other accessors
        /// var name = Search.Static("Config.Settings").Property("Profiles")[0].Property("Name").StringValue;
        ///
        /// // From UI element property
        /// var firstItem = new Search().Name("Inventory").Property("Items")[0].StringValue;
        /// </code>
        /// </example>
        public Search this[int index] => Index(index);

        /// <summary>
        /// Indexer for accessing elements by string key.
        /// Equivalent to calling Index(string).
        /// </summary>
        /// <param name="key">The string key to access</param>
        /// <returns>A new Search representing the indexed value</returns>
        /// <example>
        /// <code>
        /// // Access dictionary element using indexer syntax
        /// var player = Search.Static("GameManager.Instance").Property("PlayersByName")["Player1"];
        ///
        /// // From UI element property
        /// var value = new Search().Name("Config").Property("Settings")["volume"].FloatValue;
        /// </code>
        /// </example>
        public Search this[string key] => Index(key);

        /// <summary>
        /// Invokes a method on the current object and returns the result as a new Search.
        /// Works for both static paths and UI element components.
        /// </summary>
        /// <param name="methodName">The name of the method to invoke</param>
        /// <param name="args">Optional arguments to pass to the method</param>
        /// <returns>A new Search representing the method's return value</returns>
        /// <example>
        /// <code>
        /// // Call a parameterless method
        /// var result = Search.Static("GameManager.Instance").Invoke("GetScore").IntValue;
        ///
        /// // Call a method with arguments
        /// var player = Search.Static("GameManager.Instance").Invoke("GetPlayer", "Player1");
        ///
        /// // Call method on UI element component
        /// new Search().Name("MyButton").Invoke("IsInteractable").BoolValue;
        /// </code>
        /// </example>
        public Search Invoke(string methodName, params object[] args)
        {
            object target = null;
            Type targetType = null;
            string context = _staticPath ?? ToString();

            if (_isStaticPath)
            {
                if (_staticValue == null)
                    throw new InvalidOperationException($"Invoke failed: Static path '{_staticPath}' resolved to null, cannot invoke '{methodName}'");

                target = _staticValue;
                targetType = target.GetType();
            }
            else
            {
                // For UI elements, get the first match and try to find method on any component
                var go = FindFirst();
                if (go == null)
                    throw new InvalidOperationException($"Invoke failed: No UI element found matching '{context}', cannot invoke '{methodName}'");

                foreach (var comp in go.GetComponents<Component>())
                {
                    if (comp == null) continue;
                    var type = comp.GetType();
                    var method = type.GetMethod(methodName,
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (method != null)
                    {
                        target = comp;
                        targetType = type;
                        break;
                    }
                }

                if (target == null)
                {
                    var componentNames = string.Join(", ", go.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name));
                    throw new InvalidOperationException($"Invoke failed: Method '{methodName}' not found on any component of '{go.name}'. Components: [{componentNames}]");
                }
            }

            // Find and invoke the method
            var argTypes = args?.Select(a => a?.GetType() ?? typeof(object)).ToArray() ?? System.Type.EmptyTypes;
            var methodInfo = targetType.GetMethod(methodName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null, argTypes, null);

            // Try without specific arg types if exact match not found
            methodInfo ??= targetType.GetMethod(methodName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (methodInfo == null)
            {
                var argTypesStr = args?.Length > 0 ? string.Join(", ", argTypes.Select(t => t.Name)) : "none";
                throw new InvalidOperationException($"Invoke failed: Method '{methodName}' not found on type '{targetType.Name}'. Arg types: [{argTypesStr}]");
            }

            try
            {
                var result = methodInfo.Invoke(target, args);
                return new Search(result, $"{_staticPath ?? "element"}.{methodName}()");
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException?.Message ?? ex.Message;
                throw new InvalidOperationException($"Invoke failed: Method '{targetType.Name}.{methodName}' threw exception: {innerMsg}", ex);
            }
        }

        /// <summary>
        /// Invokes a method and returns the result as a typed value.
        /// </summary>
        /// <typeparam name="T">The expected return type</typeparam>
        /// <param name="methodName">The name of the method to invoke</param>
        /// <param name="args">Optional arguments to pass to the method</param>
        /// <returns>The method's return value cast to type T</returns>
        /// <example>
        /// <code>
        /// // Call method and get typed result
        /// int score = Search.Static("GameManager.Instance").Invoke&lt;int&gt;("GetScore");
        /// string name = Search.Static("Player.Instance").Invoke&lt;string&gt;("GetDisplayName");
        /// bool valid = Search.Static("Validator").Invoke&lt;bool&gt;("IsValid", inputValue);
        /// </code>
        /// </example>
        public T Invoke<T>(string methodName, params object[] args)
        {
            var result = Invoke(methodName, args);
            var value = result.Value;

            if (value == null) return default;
            if (value is T typedValue) return typedValue;

            try { return (T)Convert.ChangeType(value, typeof(T)); }
            catch { return default; }
        }

        /// <summary>
        /// Sets the value of the property or field accessed via Property().
        /// </summary>
        /// <param name="value">The value to set</param>
        /// <example>
        /// <code>
        /// // Set property via reflection
        /// Static("GameManager.Instance.competitorControllers[0]")
        ///     .Property("Rigidbody").Property("isKinematic").SetValue(true);
        ///
        /// // Set field value
        /// Static("Player.Instance").Property("health").SetValue(100f);
        ///
        /// // Set UI element property
        /// new Search().Name("VolumeSlider").Property("value").SetValue(0.5f);
        /// </code>
        /// </example>
        public void SetValue(object value)
        {
            if (_memberInfo == null || _parentObject == null)
            {
                throw new InvalidOperationException(
                    $"SetValue failed: Cannot set value on '{_staticPath ?? "search"}'. " +
                    "SetValue only works on properties accessed via .Property(). " +
                    "Example: Static(\"Obj.Instance\").Property(\"field\").SetValue(value)");
            }

            try
            {
                if (_memberInfo is PropertyInfo prop)
                {
                    if (!prop.CanWrite)
                        throw new InvalidOperationException($"SetValue failed: Property '{_memberName}' is read-only");
                    prop.SetValue(_parentObject, value);
                }
                else if (_memberInfo is FieldInfo field)
                {
                    if (field.IsInitOnly)
                        throw new InvalidOperationException($"SetValue failed: Field '{_memberName}' is read-only (init-only)");
                    field.SetValue(_parentObject, value);
                }
                else
                {
                    throw new InvalidOperationException($"SetValue failed: Unknown member type for '{_memberName}'");
                }
            }
            catch (ArgumentException ex)
            {
                var expectedType = _memberInfo is PropertyInfo pi ? pi.PropertyType : (_memberInfo as FieldInfo)?.FieldType;
                throw new InvalidOperationException(
                    $"SetValue failed: Cannot set '{_memberName}' to value of type '{value?.GetType().Name ?? "null"}'. " +
                    $"Expected type: {expectedType?.Name ?? "unknown"}", ex);
            }
        }

        #endregion

        #region GameObject Manipulation

        /// <summary>
        /// Restoration token for Disable/Enable operations. Implements IDisposable for using() pattern.
        /// </summary>
        public class ActiveState : IDisposable
        {
            private readonly GameObject _go;
            private readonly bool _wasActive;
            private bool _disposed;

            internal ActiveState(GameObject go, bool wasActive)
            {
                _go = go;
                _wasActive = wasActive;
            }

            /// <summary>Number of GameObjects affected (always 1).</summary>
            public int Count => 1;

            /// <summary>Restores the GameObject to its original active state.</summary>
            public void Restore()
            {
                if (!_disposed && _go != null)
                {
                    _go.SetActive(_wasActive);
                    Debug.Log($"[UITEST] Restored '{_go.name}' active={_wasActive}");
                }
                _disposed = true;
            }

            public void Dispose() => Restore();
        }

        /// <summary>
        /// Restoration token for Freeze operations. Implements IDisposable for using() pattern.
        /// </summary>
        public class FreezeState : IDisposable
        {
            private readonly List<(Rigidbody rb, bool wasKinematic, Vector3 velocity, Vector3 angularVelocity)> _rigidbodies;
            private readonly List<(Rigidbody2D rb, RigidbodyType2D bodyType, Vector2 velocity, float angularVelocity)> _rigidbodies2D;
            private bool _disposed;

            internal FreezeState()
            {
                _rigidbodies = new List<(Rigidbody, bool, Vector3, Vector3)>();
                _rigidbodies2D = new List<(Rigidbody2D, RigidbodyType2D, Vector2, float)>();
            }

            internal void Add(Rigidbody rb)
            {
                _rigidbodies.Add((rb, rb.isKinematic, rb.linearVelocity, rb.angularVelocity));
            }

            internal void Add(Rigidbody2D rb)
            {
                _rigidbodies2D.Add((rb, rb.bodyType, rb.linearVelocity, rb.angularVelocity));
            }

            /// <summary>Number of rigidbodies affected.</summary>
            public int Count => _rigidbodies.Count + _rigidbodies2D.Count;

            /// <summary>Restores all rigidbodies to their original state.</summary>
            public void Restore()
            {
                if (_disposed) return;
                foreach (var (rb, wasKinematic, velocity, angularVelocity) in _rigidbodies)
                {
                    if (rb != null)
                    {
                        rb.isKinematic = wasKinematic;
                        if (!wasKinematic)
                        {
                            rb.linearVelocity = velocity;
                            rb.angularVelocity = angularVelocity;
                        }
                    }
                }
                foreach (var (rb, bodyType, velocity, angularVelocity) in _rigidbodies2D)
                {
                    if (rb != null)
                    {
                        rb.bodyType = bodyType;
                        if (bodyType == RigidbodyType2D.Dynamic)
                        {
                            rb.linearVelocity = velocity;
                            rb.angularVelocity = angularVelocity;
                        }
                    }
                }
                Debug.Log($"[UITEST] Restored {Count} rigidbodies");
                _disposed = true;
            }

            public void Dispose() => Restore();
        }

        /// <summary>
        /// Restoration token for NoClip/Clip operations. Implements IDisposable for using() pattern.
        /// </summary>
        public class ColliderState : IDisposable
        {
            private readonly List<(Collider col, bool wasEnabled)> _colliders;
            private readonly List<(Collider2D col, bool wasEnabled)> _colliders2D;
            private bool _disposed;

            internal ColliderState()
            {
                _colliders = new List<(Collider, bool)>();
                _colliders2D = new List<(Collider2D, bool)>();
            }

            internal void Add(Collider col)
            {
                _colliders.Add((col, col.enabled));
            }

            internal void Add(Collider2D col)
            {
                _colliders2D.Add((col, col.enabled));
            }

            /// <summary>Number of colliders affected.</summary>
            public int Count => _colliders.Count + _colliders2D.Count;

            /// <summary>Restores all colliders to their original enabled state.</summary>
            public void Restore()
            {
                if (_disposed) return;
                foreach (var (col, wasEnabled) in _colliders)
                {
                    if (col != null)
                        col.enabled = wasEnabled;
                }
                foreach (var (col, wasEnabled) in _colliders2D)
                {
                    if (col != null)
                        col.enabled = wasEnabled;
                }
                Debug.Log($"[UITEST] Restored {Count} colliders");
                _disposed = true;
            }

            public void Dispose() => Restore();
        }

        /// <summary>
        /// Restoration token for Teleport operations. Implements IDisposable for using() pattern.
        /// </summary>
        public class PositionState : IDisposable
        {
            private readonly Transform _transform;
            private readonly Vector3 _originalPosition;
            private bool _disposed;

            internal PositionState(Transform t)
            {
                _transform = t;
                _originalPosition = t.position;
            }

            /// <summary>Restores the GameObject to its original position.</summary>
            public void Restore()
            {
                if (!_disposed && _transform != null)
                {
                    _transform.position = _originalPosition;
                    Debug.Log($"[UITEST] Restored '{_transform.name}' to {_originalPosition}");
                }
                _disposed = true;
            }

            public void Dispose() => Restore();
        }

        /// <summary>
        /// Disables the found GameObject (sets active to false).
        /// </summary>
        /// <returns>Restoration token - call .Restore() to re-enable</returns>
        /// <example>
        /// var state = Name("TutorialPanel").Disable();
        /// // ... test code ...
        /// state.Restore(); // Re-enables the panel
        /// </example>
        public ActiveState Disable()
        {
            var go = FindGameObjectFromSearchOrStatic();
            var state = new ActiveState(go, go.activeSelf);
            go.SetActive(false);
            Debug.Log($"[UITEST] Disable '{go.name}'");
            return state;
        }

        /// <summary>
        /// Enables the found GameObject (sets active to true).
        /// </summary>
        /// <returns>Restoration token - call .Restore() to return to original state</returns>
        /// <example>
        /// var state = Name("SecretDoor").Enable();
        /// // ... test code ...
        /// state.Restore(); // Disables it again if it was originally disabled
        /// </example>
        public ActiveState Enable()
        {
            var go = FindGameObjectFromSearchOrStatic(includeInactive: true);
            var state = new ActiveState(go, go.activeSelf);
            go.SetActive(true);
            Debug.Log($"[UITEST] Enable '{go.name}'");
            return state;
        }

        /// <summary>
        /// Freezes all rigidbodies by setting isKinematic=true and velocity to zero.
        /// </summary>
        /// <param name="includeChildren">Whether to also freeze children (default true)</param>
        /// <returns>Restoration token - call .Restore() to unfreeze with original velocities</returns>
        /// <example>
        /// var state = Name("AITruck").Freeze();
        /// // ... test code ...
        /// state.Restore(); // Restores original isKinematic and velocities
        /// </example>
        public FreezeState Freeze(bool includeChildren = true)
        {
            var go = FindGameObjectFromSearchOrStatic();
            var state = new FreezeState();

            if (includeChildren)
            {
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
                {
                    state.Add(rb);
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = true;
                }
                foreach (var rb2d in go.GetComponentsInChildren<Rigidbody2D>(true))
                {
                    state.Add(rb2d);
                    rb2d.linearVelocity = Vector2.zero;
                    rb2d.angularVelocity = 0f;
                    rb2d.bodyType = RigidbodyType2D.Kinematic;
                }
            }
            else
            {
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    state.Add(rb);
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = true;
                }
                var rb2d = go.GetComponent<Rigidbody2D>();
                if (rb2d != null)
                {
                    state.Add(rb2d);
                    rb2d.linearVelocity = Vector2.zero;
                    rb2d.angularVelocity = 0f;
                    rb2d.bodyType = RigidbodyType2D.Kinematic;
                }
            }

            Debug.Log($"[UITEST] Freeze '{go.name}' - affected {state.Count} rigidbodies");
            return state;
        }

        /// <summary>
        /// Teleports the found GameObject to a world position instantly.
        /// </summary>
        /// <param name="worldPosition">Target world position</param>
        /// <returns>Restoration token - call .Restore() to return to original position</returns>
        /// <example>
        /// var state = Name("Player").Teleport(new Vector3(100, 0, 50));
        /// // ... test code ...
        /// state.Restore(); // Returns player to original position
        /// </example>
        public PositionState Teleport(Vector3 worldPosition)
        {
            var go = FindGameObjectFromSearchOrStatic();
            var state = new PositionState(go.transform);
            go.transform.position = worldPosition;
            Debug.Log($"[UITEST] Teleport '{go.name}' to {worldPosition}");
            return state;
        }

        /// <summary>
        /// Disables all colliders (noclip mode - objects pass through other colliders).
        /// </summary>
        /// <param name="includeChildren">Whether to also disable children's colliders (default true)</param>
        /// <returns>Restoration token - call .Restore() to re-enable colliders</returns>
        /// <example>
        /// var state = Name("Player").NoClip();
        /// // ... test code ...
        /// state.Restore(); // Re-enables colliders that were originally enabled
        /// </example>
        public ColliderState NoClip(bool includeChildren = true)
        {
            var go = FindGameObjectFromSearchOrStatic();
            var state = new ColliderState();

            if (includeChildren)
            {
                foreach (var col in go.GetComponentsInChildren<Collider>(true))
                {
                    state.Add(col);
                    col.enabled = false;
                }
                foreach (var col2d in go.GetComponentsInChildren<Collider2D>(true))
                {
                    state.Add(col2d);
                    col2d.enabled = false;
                }
            }
            else
            {
                foreach (var col in go.GetComponents<Collider>())
                {
                    state.Add(col);
                    col.enabled = false;
                }
                foreach (var col2d in go.GetComponents<Collider2D>())
                {
                    state.Add(col2d);
                    col2d.enabled = false;
                }
            }

            Debug.Log($"[UITEST] NoClip '{go.name}' - disabled {state.Count} colliders");
            return state;
        }

        /// <summary>
        /// Enables all colliders (restore from noclip mode).
        /// </summary>
        /// <param name="includeChildren">Whether to also enable children's colliders (default true)</param>
        /// <returns>Restoration token - call .Restore() to return to original state</returns>
        /// <example>
        /// var state = Name("Player").Clip();
        /// // ... test code ...
        /// state.Restore(); // Disables colliders that were originally disabled
        /// </example>
        public ColliderState Clip(bool includeChildren = true)
        {
            var go = FindGameObjectFromSearchOrStatic();
            var state = new ColliderState();

            if (includeChildren)
            {
                foreach (var col in go.GetComponentsInChildren<Collider>(true))
                {
                    state.Add(col);
                    col.enabled = true;
                }
                foreach (var col2d in go.GetComponentsInChildren<Collider2D>(true))
                {
                    state.Add(col2d);
                    col2d.enabled = true;
                }
            }
            else
            {
                foreach (var col in go.GetComponents<Collider>())
                {
                    state.Add(col);
                    col.enabled = true;
                }
                foreach (var col2d in go.GetComponents<Collider2D>())
                {
                    state.Add(col2d);
                    col2d.enabled = true;
                }
            }

            Debug.Log($"[UITEST] Clip '{go.name}' - enabled {state.Count} colliders");
            return state;
        }

        /// <summary>
        /// Helper to get a GameObject from either a static path or UI search.
        /// </summary>
        private GameObject FindGameObjectFromSearchOrStatic(bool includeInactive = false)
        {
            if (_isStaticPath)
            {
                // Static path - the value should be or contain a GameObject
                if (_staticValue is GameObject go)
                    return go;
                if (_staticValue is Component comp)
                    return comp.gameObject;
                if (_staticValue is Transform t)
                    return t.gameObject;

                throw new InvalidOperationException(
                    $"Cannot manipulate GameObject: Static path '{_staticPath}' resolved to '{_staticValue?.GetType().Name ?? "null"}', " +
                    "expected GameObject, Component, or Transform");
            }
            else
            {
                // UI search
                if (includeInactive)
                    IncludeInactive();

                var result = FindFirst();
                if (result == null)
                    throw new InvalidOperationException($"No GameObject found matching '{this}'");
                return result;
            }
        }

        /// <summary>
        /// Gets a typed value from this Search.
        /// For static paths, returns the resolved value.
        /// For UI elements, returns component values (text, toggle state, slider value, etc.)
        /// </summary>
        public T GetValue<T>(string subPath = null)
        {
            string context = _staticPath ?? ToString();
            object value;

            if (_isStaticPath)
            {
                if (_staticValue == null)
                    throw new InvalidOperationException($"GetValue<{typeof(T).Name}> failed: Static path '{_staticPath}' resolved to null");

                value = string.IsNullOrEmpty(subPath) ? _staticValue : NavigateProperty(_staticValue, subPath);
                if (value == null && !string.IsNullOrEmpty(subPath))
                    throw new InvalidOperationException($"GetValue<{typeof(T).Name}> failed: Sub-path '{subPath}' not found on '{_staticPath}' (type: {_staticValue.GetType().Name})");
            }
            else
            {
                // For UI elements, use the appropriate value property based on type
                var go = FindFirst();
                if (go == null)
                    throw new InvalidOperationException($"GetValue<{typeof(T).Name}> failed: No UI element found matching '{context}'");

                value = typeof(T) switch
                {
                    Type t when t == typeof(string) => StringValue,
                    Type t when t == typeof(bool) => BoolValue,
                    Type t when t == typeof(float) => FloatValue,
                    Type t when t == typeof(int) => IntValue,
                    _ => null
                };

                if (value == null)
                    throw new InvalidOperationException($"GetValue<{typeof(T).Name}> failed: Unsupported type for UI element '{go.name}'. Supported types: string, bool, float, int");
            }

            if (value == null)
                throw new InvalidOperationException($"GetValue<{typeof(T).Name}> failed: Value resolved to null");

            if (value is T typedValue) return typedValue;

            // For Unity types and other non-convertible types, try direct cast
            try { return (T)value; }
            catch (InvalidCastException)
            {
                // Fall back to Convert.ChangeType for primitive conversions
                try { return (T)Convert.ChangeType(value, typeof(T)); }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"GetValue<{typeof(T).Name}> failed: Cannot convert value '{value}' (type: {value.GetType().Name}) to {typeof(T).Name}. {ex.Message}", ex);
                }
            }
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

                // Check for indexer syntax: PropertyName[index] or PropertyName["key"]
                var bracketStart = part.IndexOf('[');
                string memberName = bracketStart >= 0 ? part.Substring(0, bracketStart) : part;
                string indexerPart = bracketStart >= 0 ? part.Substring(bracketStart) : null;

                // Navigate to property/field first (if member name exists)
                if (!string.IsNullOrEmpty(memberName))
                {
                    var type = current.GetType();
                    var prop = type.GetProperty(memberName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (prop != null)
                    {
                        current = prop.GetValue(current);
                    }
                    else
                    {
                        var field = type.GetField(memberName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (field != null)
                        {
                            current = field.GetValue(current);
                        }
                        else
                        {
                            return null;
                        }
                    }
                }

                // Apply indexer if present
                if (indexerPart != null && current != null)
                {
                    current = ApplyIndexer(current, indexerPart);
                    if (current == null) return null;
                }
            }
            return current;
        }

        /// <summary>
        /// Parses and applies an indexer expression like [0], [123], ["key"], or ['key'].
        /// Supports chained indexers like [0][1] or [0]["key"].
        /// </summary>
        private static object ApplyIndexer(object target, string indexerExpr)
        {
            if (target == null || string.IsNullOrEmpty(indexerExpr))
                return target;

            int pos = 0;
            while (pos < indexerExpr.Length && target != null)
            {
                if (indexerExpr[pos] != '[')
                    break;

                int closePos = FindMatchingBracket(indexerExpr, pos);
                if (closePos < 0)
                    throw new InvalidOperationException($"Index failed: Unmatched '[' in indexer expression '{indexerExpr}'");

                var indexContent = indexerExpr.Substring(pos + 1, closePos - pos - 1).Trim();

                // Check if it's a string key (quoted)
                if ((indexContent.StartsWith("\"") && indexContent.EndsWith("\"")) ||
                    (indexContent.StartsWith("'") && indexContent.EndsWith("'")))
                {
                    var key = indexContent.Substring(1, indexContent.Length - 2);
                    target = AccessIndexer(target, key);
                }
                else if (int.TryParse(indexContent, out int index))
                {
                    target = AccessIndexer(target, index);
                }
                else
                {
                    throw new InvalidOperationException($"Index failed: Invalid indexer '{indexContent}' - must be an integer or quoted string");
                }

                pos = closePos + 1;
            }

            return target;
        }

        /// <summary>
        /// Navigates to a property or field on a type, with optional instance.
        /// If instance is null, only static members are accessed.
        /// </summary>
        private static object NavigatePropertyOnType(Type type, object instance, string name)
        {
            if (type == null || string.IsNullOrEmpty(name))
                return null;

            // Check for indexer syntax
            var bracketStart = name.IndexOf('[');
            string memberName = bracketStart >= 0 ? name.Substring(0, bracketStart) : name;
            string indexerPart = bracketStart >= 0 ? name.Substring(bracketStart) : null;

            object current = null;
            var flags = instance == null
                ? System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
                : System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            // Navigate to property/field
            var prop = type.GetProperty(memberName, flags);
            if (prop != null)
            {
                current = prop.GetValue(instance);
            }
            else
            {
                var field = type.GetField(memberName, flags);
                if (field != null)
                {
                    current = field.GetValue(instance);
                }
                else
                {
                    return null;
                }
            }

            // Apply indexer if present
            if (indexerPart != null && current != null)
            {
                current = ApplyIndexer(current, indexerPart);
            }

            return current;
        }

        /// <summary>
        /// Finds the matching closing bracket for an opening bracket at the given position.
        /// </summary>
        private static int FindMatchingBracket(string str, int openPos)
        {
            int depth = 0;
            bool inString = false;
            char stringChar = '\0';

            for (int i = openPos; i < str.Length; i++)
            {
                char c = str[i];

                if (inString)
                {
                    if (c == stringChar && (i == 0 || str[i - 1] != '\\'))
                        inString = false;
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inString = true;
                    stringChar = c;
                }
                else if (c == '[')
                {
                    depth++;
                }
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Accesses an indexer on the given object with an integer index.
        /// Supports arrays, lists, and any type with an int indexer.
        /// </summary>
        private static object AccessIndexer(object target, int index)
        {
            if (target == null)
                throw new InvalidOperationException($"Index failed: Cannot access index [{index}] on null");

            var type = target.GetType();

            // Handle arrays directly
            if (type.IsArray)
            {
                var array = (Array)target;
                if (index < 0 || index >= array.Length)
                    throw new IndexOutOfRangeException($"Index [{index}] is out of range for array of length {array.Length}");
                return array.GetValue(index);
            }

            // Handle IList (List<T>, etc.)
            if (target is System.Collections.IList list)
            {
                if (index < 0 || index >= list.Count)
                    throw new IndexOutOfRangeException($"Index [{index}] is out of range for list of count {list.Count}");
                return list[index];
            }

            // Try to find an indexer property with int parameter
            var indexer = type.GetProperty("Item", new[] { typeof(int) });
            if (indexer != null)
            {
                return indexer.GetValue(target, new object[] { index });
            }

            throw new InvalidOperationException($"Index failed: Type '{type.Name}' does not support integer indexer access");
        }

        /// <summary>
        /// Accesses an indexer on the given object with a string key.
        /// Supports dictionaries and any type with a string indexer.
        /// </summary>
        private static object AccessIndexer(object target, string key)
        {
            if (target == null)
                throw new InvalidOperationException($"Index failed: Cannot access index [\"{key}\"] on null");

            var type = target.GetType();

            // Handle IDictionary
            if (target is System.Collections.IDictionary dict)
            {
                if (!dict.Contains(key))
                    throw new KeyNotFoundException($"Key \"{key}\" not found in dictionary");
                return dict[key];
            }

            // Try to find an indexer property with string parameter
            var indexer = type.GetProperty("Item", new[] { typeof(string) });
            if (indexer != null)
            {
                return indexer.GetValue(target, new object[] { key });
            }

            throw new InvalidOperationException($"Index failed: Type '{type.Name}' does not support string indexer access");
        }

        #endregion
    }
}
