using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

using TMPro;
using UnityEngine;
using Debug = UnityEngine.Debug;
using UnityEngine.UI;

namespace ODDGames.UIAutomation
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
    /// With 'using static ODDGames.UIAutomation.ActionExecutor':
    ///   await Click(Text("Play"));
    ///   await Click(Name("btn_*").Type&lt;Button&gt;());
    ///   await Click(Text("OK|Okay|Confirm"));
    ///
    /// With new Search():
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
        bool _includeOffScreen;

        // Static path support
        string _staticPath;
        object _staticValue;
        bool _isStaticPath;

        // For SetValue support - tracks the parent object and member name
        object _parentObject;
        string _memberName;
        MemberInfo _memberInfo;

        // For RequiresReceiver - stores receivers found at position
        List<GameObject> _receivers = new();

        /// <summary>
        /// Gets the receivers found by the last RequiresReceiver() check.
        /// </summary>
        public IReadOnlyList<GameObject> Receivers => _receivers;

        /// <summary>
        /// Whether this search uses RequiresReceiver() filter.
        /// </summary>
        public bool UsesReceiverFilter => _descriptionParts.Any(p => p.Contains("RequiresReceiver"));

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
        /// <summary>
        /// Include off-screen GameObjects in the search.
        /// By default, only on-screen GameObjects with visible bounds are returned.
        /// </summary>
        /// <returns>This Search instance for chaining.</returns>
        public Search IncludeOffScreen()
        {
            _includeOffScreen = true;
            return this;
        }

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
        ///
        /// // Case-sensitive match
        /// new Search().Name("PlayButton", ignoreCase: false)
        /// </code>
        /// </example>
        public Search Name(string pattern, bool ignoreCase = true)
        {
            AddDescription($"Name(\"{pattern}\")");
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go => negate != WildcardMatch(go.name, pattern, ignoreCase));
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
        ///
        /// // Case-sensitive match
        /// new Search().Text("PLAY", ignoreCase: false)
        /// </code>
        /// </example>
        public Search Text(string pattern, bool ignoreCase = true)
        {
            AddDescription($"Text(\"{pattern}\")");
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go => negate != MatchText(go, pattern, ignoreCase));
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
            if (xMin < 0f || xMin > 1f) throw new ArgumentOutOfRangeException(nameof(xMin), xMin, "xMin must be between 0 and 1");
            if (yMin < 0f || yMin > 1f) throw new ArgumentOutOfRangeException(nameof(yMin), yMin, "yMin must be between 0 and 1");
            if (xMax < 0f || xMax > 1f) throw new ArgumentOutOfRangeException(nameof(xMax), xMax, "xMax must be between 0 and 1");
            if (yMax < 0f || yMax > 1f) throw new ArgumentOutOfRangeException(nameof(yMax), yMax, "yMax must be between 0 and 1");
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
        /// Order matches alphabetically by GameObject name (A-Z).
        /// </summary>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Get buttons sorted alphabetically by name
        /// new Search().Type&lt;Button&gt;().OrderByName()
        /// </code>
        /// </example>
        public Search OrderByName()
        {
            _orderBy = objects => objects.OrderBy(go => go.name, StringComparer.Ordinal);
            return this;
        }

        /// <summary>
        /// Order matches alphabetically by GameObject name in reverse (Z-A).
        /// </summary>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Get buttons sorted reverse-alphabetically by name
        /// new Search().Type&lt;Button&gt;().OrderByNameDescending()
        /// </code>
        /// </example>
        public Search OrderByNameDescending()
        {
            _orderBy = objects => objects.OrderByDescending(go => go.name, StringComparer.Ordinal);
            return this;
        }

        /// <summary>
        /// Order matches by Unity instance ID (ascending).
        /// Instance IDs are generally stable within a session and reflect creation order.
        /// </summary>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Get elements in creation order
        /// new Search().Name("Item*").OrderByInstanceId()
        /// </code>
        /// </example>
        public Search OrderByInstanceId()
        {
            _orderBy = objects => objects.OrderBy(go => go.GetInstanceID());
            return this;
        }

        /// <summary>
        /// Order matches by Unity instance ID (descending).
        /// Instance IDs are generally stable within a session and reflect creation order.
        /// </summary>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Get elements in reverse creation order (newest first)
        /// new Search().Name("Item*").OrderByInstanceIdDescending()
        /// </code>
        /// </example>
        public Search OrderByInstanceIdDescending()
        {
            _orderBy = objects => objects.OrderByDescending(go => go.GetInstanceID());
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
        /// Filters to elements where something with ANY of the specified handler types exists at the element's screen position.
        /// Uses EventSystem.RaycastAll to check all registered raycasters (GraphicRaycaster, PhysicsRaycaster, etc.).
        /// All matching receivers are stored in the Receivers property for logging/debugging.
        /// </summary>
        /// <param name="handlerTypes">Handler interface types to look for (e.g., typeof(IPointerClickHandler), typeof(IDragHandler))</param>
        /// <returns>This Search instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Find buttons that have a click receiver at their position
        /// new Search().Type&lt;Button&gt;().RequiresReceiver(typeof(IPointerClickHandler))
        ///
        /// // Find elements with either click or drag handlers
        /// new Search().Name("*Item*").RequiresReceiver(typeof(IPointerClickHandler), typeof(IDragHandler))
        /// </code>
        /// </example>
        public Search RequiresReceiver(params Type[] handlerTypes)
        {
            bool negate = _nextNegate;
            _nextNegate = false;
            _conditions.Add(go =>
            {
                var screenPos = InputInjector.GetScreenPosition(go);
                _receivers = InputInjector.GetReceiversAtPosition(screenPos, handlerTypes);
                return negate != (_receivers.Count > 0);
            });

            var typeNames = string.Join(", ", handlerTypes.Select(t => t.Name));
            AddDescription($"RequiresReceiver({typeNames})");
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
            combined._includeOffScreen = _includeOffScreen || other._includeOffScreen;
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
        internal IEnumerable<GameObject> ApplyPostProcessing(IEnumerable<GameObject> results)
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
        /// Asynchronously finds the first GameObject matching this search query.
        /// Waits until element is stable (found for 3 consecutive frames) or timeout.
        /// </summary>
        /// <param name="timeout">Maximum time to wait for the element (default 10 seconds).</param>
        /// <param name="index">Index of the element when multiple match (0-based).</param>
        /// <returns>The found GameObject, or null if not found within timeout.</returns>
        /// <example>
        /// <code>
        /// var button = await Name("Submit").Find();
        /// if (button != null) await Click(button);
        /// </code>
        /// </example>
        public async Task<GameObject> Find(float timeout = 10f, int index = 0)
        {
            float startTime = (float)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
            var findMode = ShouldIncludeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;

            // Stability tracking - track all matching elements, not just one candidate
            List<GameObject> stableResults = null;
            int stableFrameCount = 0;
            const int requiredStableFrames = 3;
            bool loggedOnce = false;

            while (((float)Stopwatch.GetTimestamp() / Stopwatch.Frequency - startTime) < timeout && Application.isPlaying)
            {

                // Search all Transform objects (includes RectTransform) to support both UI and non-UI objects
                var allMatching = UnityEngine.Object.FindObjectsByType<Transform>(findMode, FindObjectsSortMode.None)
                    .Where(t => t != null && Matches(t.gameObject))
                    .Select(t => t.gameObject)
                    .ToList();

                // Filter to on-screen objects only (off-screen can't be interacted with)
                // using corner-based bounds so partially visible objects are included
                if (!_includeOffScreen)
                {
                    var screenRect = new Rect(0, 0, Screen.width, Screen.height);
                    allMatching = allMatching
                        .Where(go =>
                        {
                            var bounds = InputInjector.GetScreenBounds(go);
                            return bounds.width > 0 && bounds.height > 0 && bounds.Overlaps(screenRect);
                        })
                        .ToList();
                }

                if (HasPostProcessing)
                {
                    // Apply user-specified ordering (Near, OrderBy, OrderByPosition, etc.)
                    allMatching = ApplyPostProcessing(allMatching).ToList();
                }
                else
                {
                    // Default: order by depth (closest first), with UI elements as tiebreaker
                    allMatching = allMatching
                        .OrderBy(go => GetDepth(go))
                        .ThenBy(go => go.GetComponentInParent<Canvas>() != null ? 0 : 1)
                        .ToList();
                }

                // Debug logging on first match
                if (allMatching.Count > 0 && !loggedOnce)
                {
                    loggedOnce = true;
                    var matchInfo = string.Join(", ", allMatching.Take(5).Select(go =>
                    {
                        var pos = InputInjector.GetScreenPosition(go);
                        var path = GetHierarchyPath(go.transform);
                        return $"'{go.name}' at ({pos.x:F0},{pos.y:F0}) path={path}";
                    }));
                    if (allMatching.Count > 5)
                        matchInfo += $" ... and {allMatching.Count - 5} more";
                    Debug.Log($"[Search] {ToString()} matched {allMatching.Count}: {matchInfo}");
                }

                if (allMatching.Count > index)
                {
                    // Check if results are the same as previous frame (same objects in same order)
                    bool resultsMatch = stableResults != null
                        && stableResults.Count == allMatching.Count
                        && stableResults.SequenceEqual(allMatching);

                    if (resultsMatch)
                    {
                        stableFrameCount++;
                        if (stableFrameCount >= requiredStableFrames)
                            return allMatching[index];
                    }
                    else
                    {
                        // Results changed - reset stability counter but keep new results
                        stableResults = allMatching;
                        stableFrameCount = 1;
                    }

                    // Wait one frame and check again
                    await Async.DelayFrames(1);
                    continue;
                }
                else
                {
                    // Not enough elements found - reset stability
                    stableResults = null;
                    stableFrameCount = 0;
                }

                await Task.Delay(100);
            }

            return null;
        }

        /// <summary>
        /// Asynchronously finds all GameObjects matching this search query.
        /// Waits until results are stable (same for 3 consecutive frames) or timeout.
        /// </summary>
        /// <param name="timeout">Maximum time to wait for elements (default 10 seconds).</param>
        /// <returns>List of matching GameObjects, or empty list if none found within timeout.</returns>
        public async Task<List<GameObject>> FindAll(float timeout = 10f)
        {
            float startTime = (float)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
            var findMode = ShouldIncludeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;

            // Stability tracking - results must be consistent for consecutive frames
            List<GameObject> stableResults = null;
            int stableFrameCount = 0;
            const int requiredStableFrames = 3;

            while (((float)Stopwatch.GetTimestamp() / Stopwatch.Frequency - startTime) < timeout && Application.isPlaying)
            {
                // Search all Transform objects (includes RectTransform) to support both UI and non-UI objects
                var results = UnityEngine.Object.FindObjectsByType<Transform>(findMode, FindObjectsSortMode.None)
                    .Where(t => t != null && Matches(t.gameObject))
                    .Select(t => t.gameObject)
                    .ToList();

                // Filter to on-screen objects only (off-screen can't be interacted with)
                // using corner-based bounds so partially visible objects are included
                if (!_includeOffScreen)
                {
                    var screenRect = new Rect(0, 0, Screen.width, Screen.height);
                    results = results
                        .Where(go =>
                        {
                            var bounds = InputInjector.GetScreenBounds(go);
                            return bounds.width > 0 && bounds.height > 0 && bounds.Overlaps(screenRect);
                        })
                        .ToList();
                }

                if (HasPostProcessing)
                {
                    // Apply user-specified ordering (Near, OrderBy, OrderByPosition, etc.)
                    results = ApplyPostProcessing(results).ToList();
                }
                else
                {
                    // Default: order by depth (closest first), with UI elements as tiebreaker
                    results = results
                        .OrderBy(go => GetDepth(go))
                        .ThenBy(go => go.GetComponentInParent<Canvas>() != null ? 0 : 1)
                        .ToList();
                }

                if (results.Count > 0)
                {
                    // Check if results are the same as previous frame (same objects in same order)
                    bool resultsMatch = stableResults != null
                        && stableResults.Count == results.Count
                        && stableResults.SequenceEqual(results);

                    if (resultsMatch)
                    {
                        stableFrameCount++;
                        if (stableFrameCount >= requiredStableFrames)
                            return results;
                    }
                    else
                    {
                        stableResults = results;
                        stableFrameCount = 1;
                    }

                    await Async.DelayFrames(1);
                    continue;
                }
                else
                {
                    stableResults = null;
                    stableFrameCount = 0;
                }

                await Task.Delay(100);
            }

            return new List<GameObject>();
        }


        /// <summary>
        /// Validates this search query and returns details about what was found.
        /// Useful for AI feedback - call this before performing actions to verify the target exists.
        /// </summary>
        /// <returns>Validation result with count and description of matches.</returns>
        public async Task<SearchValidation> Validate(float timeout = 10f)
        {
            var results = await FindAll(timeout);
            return new SearchValidation
            {
                Success = results.Count > 0,
                Count = results.Count,
                Query = ToString(),
                MatchedNames = results.Take(5).Select(go => go.name).ToList(),
                Message = results.Count == 0
                    ? $"No elements found matching '{ToString()}'"
                    : results.Count == 1
                        ? $"Found '{results[0].name}'"
                        : $"Found {results.Count} elements: {string.Join(", ", results.Take(3).Select(go => go.name))}{(results.Count > 3 ? "..." : "")}"
            };
        }

        /// <summary>
        /// Checks if any element matches this search asynchronously with stability waiting.
        /// </summary>
        public async Task<bool> Exists(float timeout = 10f)
        {
            var result = await Find(timeout);
            return result != null;
        }

        /// <summary>
        /// Gets the screen position of the element asynchronously.
        /// </summary>
        /// <param name="searchTime">Timeout in seconds</param>
        /// <returns>Screen position of the element center</returns>
        public async Task<Vector2> GetScreenPosition(float searchTime = 10f)
        {
            var go = await Find(searchTime);
            if (go == null)
                throw new TimeoutException($"No element found matching '{this}' within {searchTime}s");
            return UIUtility.GetScreenPosition(go);
        }

        #endregion

        #region Spatial Helpers

        /// <summary>
        /// Gets the screen-space center position of a GameObject.
        /// </summary>
        public static Vector2 GetScreenCenter(GameObject go) => UIUtility.GetScreenPosition(go);

        /// <summary>
        /// Gets the screen-space bounds of a GameObject as a Rect.
        /// </summary>
        public static Rect GetScreenBounds(GameObject go) => UIUtility.GetScreenBounds(go);

        /// <summary>
        /// Gets the world-space bounds of a GameObject from Renderer or Collider.
        /// </summary>
        public static Bounds GetWorldBounds(GameObject go)
        {
            if (go == null)
                throw new ArgumentNullException(nameof(go));

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) return renderer.bounds;

            var collider = go.GetComponent<Collider>();
            if (collider != null) return collider.bounds;

            // Fall back to position with zero size
            return new Bounds(go.transform.position, Vector3.zero);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Returns a depth value for ordering: lower values are closer to the viewer.
        /// UI elements use negative canvas sort order (higher sort order = on top = lower depth value),
        /// with sibling index as secondary. 3D objects use camera distance.
        /// </summary>
        static float GetDepth(GameObject go)
        {
            var canvas = go.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                // UI: use negative sort order so higher sort order = lower depth value (closer)
                // Subtract a small sibling fraction so later siblings (drawn on top) sort first
                var rootCanvas = canvas.rootCanvas;
                float siblingFraction = 1f - (go.transform.GetSiblingIndex() / 10000f);
                return -rootCanvas.sortingOrder + siblingFraction;
            }

            // 3D: use camera distance (closer = lower value)
            Camera cam = Camera.main;
            if (cam != null)
                return Vector3.Distance(cam.transform.position, go.transform.position);

            return float.MaxValue;
        }

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

        static bool WildcardMatch(string text, string pattern) => WildcardMatch(text, pattern, ignoreCase: true);

        static bool WildcardMatch(string text, string pattern, bool ignoreCase)
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
                    if (WildcardMatchSingle(text, alt.Trim(), ignoreCase))
                        return true;
                }
                return false;
            }

            return WildcardMatchSingle(text, pattern, ignoreCase);
        }

        static bool WildcardMatchSingle(string text, string pattern, bool ignoreCase)
        {
            if (string.IsNullOrEmpty(pattern)) return true;
            if (string.IsNullOrEmpty(text)) return false;

            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            if (!pattern.Contains("*"))
                return text.Equals(pattern, comparison);

            string[] parts = pattern.Split('*');
            int textIndex = 0;

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (string.IsNullOrEmpty(part)) continue;

                int foundIndex = text.IndexOf(part, textIndex, comparison);
                if (foundIndex < 0) return false;

                if (i == 0 && !pattern.StartsWith("*") && foundIndex != 0)
                    return false;

                textIndex = foundIndex + part.Length;
            }

            if (!pattern.EndsWith("*") && parts.Length > 0)
            {
                string lastPart = parts[parts.Length - 1];
                if (!string.IsNullOrEmpty(lastPart))
                    return text.EndsWith(lastPart, comparison);
            }

            return true;
        }

        static bool MatchText(GameObject go, string pattern) => MatchText(go, pattern, ignoreCase: true);

        static bool MatchText(GameObject go, string pattern, bool ignoreCase)
        {
            // Check TMP_Text on this object only (not children)
            var tmpText = go.GetComponent<TMP_Text>();
            if (tmpText != null && WildcardMatch(tmpText.text, pattern, ignoreCase))
                return true;

            // Check legacy Text on this object only (not children)
            var legacyText = go.GetComponent<Text>();
            if (legacyText != null && WildcardMatch(legacyText.text, pattern, ignoreCase))
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

        static Vector2 GetScreenPosition(GameObject go) => UIUtility.GetScreenPosition(go);

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
                        // Use RectTransform world corners for consistent bounds calculation
                        // This handles canvas scaling correctly (unlike TransformVector)
                        Vector3[] corners = new Vector3[4];
                        rect.GetWorldCorners(corners);
                        var bounds = new Rect(
                            corners[0].x,
                            corners[0].y,
                            corners[2].x - corners[0].x,
                            corners[2].y - corners[0].y);
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
                // Skip if the element is the anchor text itself or contains it
                if (element == textGo || textGo.transform.IsChildOf(element.transform) || element.transform.IsChildOf(textGo.transform))
                    continue;
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

                    // Check element center is in the correct direction from text bounds edge
                    // Use the text bounds edge (not center) as the reference point for direction checks
                    // This ensures elements must be truly beyond the text to match
                    bool isInDirection = direction.Value switch
                    {
                        Direction.Right => elementCenter.x > textBounds.xMax,
                        Direction.Left => elementCenter.x < textBounds.xMin,
                        Direction.Below => elementCenter.y < textBounds.yMin,
                        Direction.Above => elementCenter.y > textBounds.yMax,
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
        /// var score = Search.Reflect("GameManager.Instance.Score").GetValue&lt;int&gt;();
        ///
        /// // Iterate over arrays
        /// foreach (var player in Search.Reflect("GameManager.Players"))
        ///     Debug.Log(player.Property("Name").GetValue&lt;string&gt;());
        ///
        /// // Use with Assert
        /// ActionExecutor.Assert(Search.Reflect("GameManager.IsReady").Property("Value"));
        /// </example>
        public static Search Reflect(string path)
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

            // For UI elements, use async Find() first, then call Property on the result
            throw new InvalidOperationException($"Property() is only for Reflect paths. For UI elements use: var go = await {context}.Find(); then access components directly.");
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
                // For UI element searches, use async Find() first
                throw new InvalidOperationException($"Component() is only for Reflect paths. For UI elements use: var go = await {context}.Find(); then access components directly.");
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
        /// Only works for static/Reflect paths.
        /// </summary>
        /// <param name="index">The integer index to access</param>
        /// <returns>A new Search representing the indexed value</returns>
        /// <example>
        /// <code>
        /// // Access array element from static path
        /// var item = Search.Reflect("GameManager.Instance").Property("Players").Index(0);
        ///
        /// // Access list element
        /// var weapon = Search.Reflect("Inventory.Items").Index(2).Property("Name").GetValue&lt;string&gt;();
        ///
        /// // Chain with other accessors
        /// var name = Search.Reflect("Config.Settings").Property("Profiles").Index(0).Property("Name").GetValue&lt;string&gt;();
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
        /// Only works for static/Reflect paths.
        /// </summary>
        /// <param name="key">The string key to access</param>
        /// <returns>A new Search representing the indexed value</returns>
        /// <example>
        /// <code>
        /// // Access dictionary element
        /// var player = Search.Reflect("GameManager.Instance").Property("PlayersByName").Index("Player1");
        ///
        /// // Chain with other accessors
        /// var score = Search.Reflect("Leaderboard.Scores").Index("TopPlayer").GetValue&lt;int&gt;();
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
        /// Equivalent to calling Index(int). Only works for static/Reflect paths.
        /// </summary>
        /// <param name="index">The integer index to access</param>
        /// <returns>A new Search representing the indexed value</returns>
        /// <example>
        /// <code>
        /// // Access array element using indexer syntax
        /// var item = Search.Reflect("GameManager.Instance").Property("Players")[0];
        ///
        /// // Chain with other accessors
        /// var name = Search.Reflect("Config.Settings").Property("Profiles")[0].Property("Name").GetValue&lt;string&gt;();
        /// </code>
        /// </example>
        public Search this[int index] => Index(index);

        /// <summary>
        /// Indexer for accessing elements by string key.
        /// Equivalent to calling Index(string). Only works for static/Reflect paths.
        /// </summary>
        /// <param name="key">The string key to access</param>
        /// <returns>A new Search representing the indexed value</returns>
        /// <example>
        /// <code>
        /// // Access dictionary element using indexer syntax
        /// var player = Search.Reflect("GameManager.Instance").Property("PlayersByName")["Player1"];
        ///
        /// // Get typed value
        /// var volume = Search.Reflect("Config.Settings")["volume"].GetValue&lt;float&gt;();
        /// </code>
        /// </example>
        public Search this[string key] => Index(key);

        /// <summary>
        /// Invokes a method on the current object and returns the result as a new Search.
        /// Only works for static/Reflect paths.
        /// </summary>
        /// <param name="methodName">The name of the method to invoke</param>
        /// <param name="args">Optional arguments to pass to the method</param>
        /// <returns>A new Search representing the method's return value</returns>
        /// <example>
        /// <code>
        /// // Call a parameterless method
        /// var result = Search.Reflect("GameManager.Instance").Invoke("GetScore").GetValue&lt;int&gt;();
        ///
        /// // Call a method with arguments
        /// var player = Search.Reflect("GameManager.Instance").Invoke("GetPlayer", "Player1");
        ///
        /// // Use typed Invoke
        /// int score = Search.Reflect("GameManager.Instance").Invoke&lt;int&gt;("GetScore");
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
                // For UI element searches, use async Find() first
                throw new InvalidOperationException($"Invoke() is only for Reflect paths. For UI elements use: var go = await {context}.Find(); then call methods directly.");
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
        /// int score = Search.Reflect("GameManager.Instance").Invoke&lt;int&gt;("GetScore");
        /// string name = Search.Reflect("Player.Instance").Invoke&lt;string&gt;("GetDisplayName");
        /// bool valid = Search.Reflect("Validator").Invoke&lt;bool&gt;("IsValid", inputValue);
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
                    Debug.Log($"[UIAutomation] Restored '{_go.name}' active={_wasActive}");
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
                Debug.Log($"[UIAutomation] Restored {Count} rigidbodies");
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
                Debug.Log($"[UIAutomation] Restored {Count} colliders");
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
                    Debug.Log($"[UIAutomation] Restored '{_transform.name}' to {_originalPosition}");
                }
                _disposed = true;
            }

            public void Dispose() => Restore();
        }

        /// <summary>
        /// Disables the found GameObject (sets active to false).
        /// Waits up to searchTime for the element to appear.
        /// </summary>
        /// <param name="searchTime">Maximum time to wait for the element (default 10s)</param>
        /// <returns>Restoration token - call .Restore() to re-enable</returns>
        /// <example>
        /// var state = await Name("TutorialPanel").Disable();
        /// // ... test code ...
        /// state.Restore(); // Re-enables the panel
        /// </example>
        public async Task<ActiveState> Disable(float searchTime = 10f)
        {
            var go = await FindGameObjectFromSearchOrStatic(searchTime);
            var state = new ActiveState(go, go.activeSelf);
            go.SetActive(false);
            Debug.Log($"[UIAutomation] Disable '{go.name}'");
            return state;
        }

        /// <summary>
        /// Enables the found GameObject (sets active to true).
        /// Waits up to searchTime for the element to appear.
        /// </summary>
        /// <param name="searchTime">Maximum time to wait for the element (default 10s)</param>
        /// <returns>Restoration token - call .Restore() to return to original state</returns>
        /// <example>
        /// var state = await Name("SecretDoor").Enable();
        /// // ... test code ...
        /// state.Restore(); // Disables it again if it was originally disabled
        /// </example>
        public async Task<ActiveState> Enable(float searchTime = 10f)
        {
            var go = await FindGameObjectFromSearchOrStatic(searchTime, includeInactive: true);
            var state = new ActiveState(go, go.activeSelf);
            go.SetActive(true);
            Debug.Log($"[UIAutomation] Enable '{go.name}'");
            return state;
        }

        /// <summary>
        /// Freezes all rigidbodies by setting isKinematic=true and velocity to zero.
        /// Waits up to searchTime for the element to appear.
        /// </summary>
        /// <param name="includeChildren">Whether to also freeze children (default true)</param>
        /// <param name="searchTime">Maximum time to wait for the element (default 10s)</param>
        /// <returns>Restoration token - call .Restore() to unfreeze with original velocities</returns>
        /// <example>
        /// var state = await Name("AITruck").Freeze();
        /// // ... test code ...
        /// state.Restore(); // Restores original isKinematic and velocities
        /// </example>
        public async Task<FreezeState> Freeze(bool includeChildren = true, float searchTime = 10f)
        {
            var go = await FindGameObjectFromSearchOrStatic(searchTime);
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

            Debug.Log($"[UIAutomation] Freeze '{go.name}' - affected {state.Count} rigidbodies");
            return state;
        }

        /// <summary>
        /// Teleports the found GameObject to a world position instantly.
        /// Waits up to searchTime for the element to appear.
        /// </summary>
        /// <param name="worldPosition">Target world position</param>
        /// <param name="searchTime">Maximum time to wait for the element (default 10s)</param>
        /// <returns>Restoration token - call .Restore() to return to original position</returns>
        /// <example>
        /// var state = await Name("Player").Teleport(new Vector3(100, 0, 50));
        /// // ... test code ...
        /// state.Restore(); // Returns player to original position
        /// </example>
        public async Task<PositionState> Teleport(Vector3 worldPosition, float searchTime = 10f)
        {
            var go = await FindGameObjectFromSearchOrStatic(searchTime);
            var state = new PositionState(go.transform);
            go.transform.position = worldPosition;
            Debug.Log($"[UIAutomation] Teleport '{go.name}' to {worldPosition}");
            return state;
        }

        /// <summary>
        /// Disables all colliders (noclip mode - objects pass through other colliders).
        /// Waits up to searchTime for the element to appear.
        /// </summary>
        /// <param name="includeChildren">Whether to also disable children's colliders (default true)</param>
        /// <param name="searchTime">Maximum time to wait for the element (default 10s)</param>
        /// <returns>Restoration token - call .Restore() to re-enable colliders</returns>
        /// <example>
        /// var state = await Name("Player").NoClip();
        /// // ... test code ...
        /// state.Restore(); // Re-enables colliders that were originally enabled
        /// </example>
        public async Task<ColliderState> NoClip(bool includeChildren = true, float searchTime = 10f)
        {
            var go = await FindGameObjectFromSearchOrStatic(searchTime);
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

            Debug.Log($"[UIAutomation] NoClip '{go.name}' - disabled {state.Count} colliders");
            return state;
        }

        /// <summary>
        /// Enables all colliders (restore from noclip mode).
        /// Waits up to searchTime for the element to appear.
        /// </summary>
        /// <param name="includeChildren">Whether to also enable children's colliders (default true)</param>
        /// <param name="searchTime">Maximum time to wait for the element (default 10s)</param>
        /// <returns>Restoration token - call .Restore() to return to original state</returns>
        /// <example>
        /// var state = await Name("Player").Clip();
        /// // ... test code ...
        /// state.Restore(); // Disables colliders that were originally disabled
        /// </example>
        public async Task<ColliderState> Clip(bool includeChildren = true, float searchTime = 10f)
        {
            var go = await FindGameObjectFromSearchOrStatic(searchTime);
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

            Debug.Log($"[UIAutomation] Clip '{go.name}' - enabled {state.Count} colliders");
            return state;
        }

        /// <summary>
        /// Helper to get a GameObject from either a static path or UI search (async with timeout).
        /// </summary>
        private async Task<GameObject> FindGameObjectFromSearchOrStatic(float searchTime = 10f, bool includeInactive = false)
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
                // Manipulation methods work on any GameObject, not just on-screen UI
                IncludeOffScreen();

                if (includeInactive)
                    IncludeInactive();

                var result = await Find(searchTime);
                if (result == null)
                    throw new TimeoutException($"No GameObject found matching '{this}' within {searchTime}s");
                return result;
            }
        }

        /// <summary>
        /// Gets a typed value from this Search (static/Reflect paths only).
        /// For UI elements, use ActionExecutor.GetValue&lt;T&gt;(search) instead.
        /// </summary>
        public T GetValue<T>(string subPath = null)
        {
            string context = _staticPath ?? ToString();

            if (!_isStaticPath)
                throw new InvalidOperationException($"GetValue<{typeof(T).Name}> is only for Reflect paths. For UI elements use: await ActionExecutor.GetValue<{typeof(T).Name}>({context})");

            if (_staticValue == null)
                throw new InvalidOperationException($"GetValue<{typeof(T).Name}> failed: Static path '{_staticPath}' resolved to null");

            var value = string.IsNullOrEmpty(subPath) ? _staticValue : NavigateProperty(_staticValue, subPath);
            if (value == null && !string.IsNullOrEmpty(subPath))
                throw new InvalidOperationException($"GetValue<{typeof(T).Name}> failed: Sub-path '{subPath}' not found on '{_staticPath}' (type: {_staticValue.GetType().Name})");

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
        /// Gets the raw value for static path searches only.
        /// For UI elements, use Find() to get the GameObject asynchronously.
        /// </summary>
        public object Value
        {
            get
            {
                if (!_isStaticPath)
                    throw new InvalidOperationException($"Value is only for Reflect paths. For UI elements use: await {ToString()}.Find()");
                return _staticValue;
            }
        }

        /// <summary>
        /// Iterates over array/list elements for static paths only.
        /// For UI elements, use FindAll() asynchronously instead.
        /// </summary>
        public IEnumerator<Search> GetEnumerator()
        {
            if (!_isStaticPath)
                throw new InvalidOperationException($"Enumeration is only for Reflect paths. For UI elements use: await {ToString()}.FindAll()");

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

    /// <summary>
    /// Result of a Search.Validate() call. Provides quick feedback on whether a search query would succeed.
    /// </summary>
    public class SearchValidation
    {
        /// <summary>True if at least one element was found.</summary>
        public bool Success { get; set; }

        /// <summary>Number of matching elements found.</summary>
        public int Count { get; set; }

        /// <summary>The search query that was validated.</summary>
        public string Query { get; set; }

        /// <summary>Names of the first few matching GameObjects (up to 5).</summary>
        public List<string> MatchedNames { get; set; }

        /// <summary>Human-readable message describing the validation result.</summary>
        public string Message { get; set; }

        /// <summary>Implicit conversion to bool for easy conditionals.</summary>
        public static implicit operator bool(SearchValidation v) => v.Success;

        public override string ToString() => Message;
    }

    /// <summary>
    /// Result of a Search.Resolve() call. Contains the found element and metadata about how it was found.
    /// </summary>
    public class ResolveResult
    {
        /// <summary>True if at least one element was found.</summary>
        public bool Found { get; set; }

        /// <summary>The first matching GameObject (or at the specified index), or null if not found.</summary>
        public GameObject Element { get; set; }

        /// <summary>All matching GameObjects found.</summary>
        public List<GameObject> AllElements { get; set; }

        /// <summary>The search query that was resolved.</summary>
        public string SearchQuery { get; set; }

        /// <summary>Implicit conversion to bool for easy conditionals.</summary>
        public static implicit operator bool(ResolveResult r) => r.Found;

        /// <summary>Implicit conversion to GameObject for seamless use with existing Click methods.</summary>
        public static implicit operator GameObject(ResolveResult r) => r.Element;

        public override string ToString() =>
            Found ? $"Found: {Element?.name ?? "null"}" : $"Not found: {SearchQuery}";
    }
}
