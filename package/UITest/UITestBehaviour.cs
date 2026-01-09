using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ODDGames.UITest
{
    public abstract class UITestBehaviour : MonoBehaviour
    {
        #region Inlined Utilities

        sealed class TimeYielder
        {
            private readonly long m_yieldThreshold;
            private long m_lastYield;

            public bool WantsToYield => Environment.TickCount - m_lastYield > m_yieldThreshold;

            public TimeYielder(TimeSpan threshold)
            {
                m_yieldThreshold = (long)threshold.TotalMilliseconds;
                m_lastYield = Environment.TickCount;
            }

            public async UniTask<bool> Yield(PlayerLoopTiming timing = PlayerLoopTiming.Update)
            {
                await UniTask.Yield(timing);
                m_lastYield = Environment.TickCount;
                return true;
            }

            public UniTask<bool> YieldOptional(PlayerLoopTiming timing = PlayerLoopTiming.Update)
            {
                return WantsToYield ? Yield(timing) : UniTask.FromResult(false);
            }
        }

        static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            var path = new StringBuilder();
            while (transform != null)
            {
                if (path.Length > 0)
                    path.Insert(0, "/");
                path.Insert(0, transform.name);
                transform = transform.parent;
            }
            return "/" + path.ToString();
        }

        static bool TryGetComponentInChildren<T>(GameObject obj, out T component) where T : class
        {
            if (obj == null)
            {
                component = default;
                return false;
            }
            component = obj.GetComponentInChildren<T>();
            return component != null;
        }

        #endregion

        #region Search Query Builder

        /// <summary>
        /// Fluent builder for searching GameObjects. Supports wildcards (*) in all patterns.
        ///
        /// Examples:
        ///   await Click(Search.ByText("Play"));
        ///   await Click(Search.BySprite("icon_*"));
        ///   await Click(Search.ByType&lt;Button&gt;().Text("Start"));
        ///   await Click(Search.ByName("btn_*").Not.Tag("Disabled"));
        ///   await Click(Search.ByPath("*/Panel/Buttons/*"));
        ///   await Click(Search.ByType("Toggle").Sprite("*checkbox*"));
        ///   await Click(Search.ByAny("*play*"));  // matches name, text, sprite, or path
        ///
        /// Hierarchy queries:
        ///   await Click(Search.ByName("Button").HasParent("Panel"));
        ///   await Click(Search.ByType&lt;Button&gt;().HasAncestor(Search.ByName("*Settings*")));
        ///   await Click(Search.ByName("Panel").HasChild(Search.ByText("Submit")));
        ///   await Click(Search.ByName("Root").HasDescendant(Search.ByType&lt;Toggle&gt;()));
        /// </summary>
        /// <summary>
        /// Direction for adjacent element searches.
        /// </summary>
        public enum Adjacent
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

        public class Search
        {
            readonly List<Func<GameObject, bool>> _conditions = new();
            bool _nextNegate;
            bool _includeInactive;
            bool _includeDisabled;

            Search() { }

            /// <summary>Gets whether this search should include inactive GameObjects.</summary>
            public bool ShouldIncludeInactive => _includeInactive;

            /// <summary>Gets whether this search should include disabled (non-interactable) components.</summary>
            public bool ShouldIncludeDisabled => _includeDisabled;

            /// <summary>
            /// Include inactive (SetActive(false)) GameObjects in the search.
            /// By default, only active GameObjects are searched.
            /// </summary>
            public Search IncludeInactive()
            {
                _includeInactive = true;
                return this;
            }

            /// <summary>
            /// Include disabled (interactable=false) components in the search.
            /// By default, only enabled/interactable components are searched.
            /// </summary>
            public Search IncludeDisabled()
            {
                _includeDisabled = true;
                return this;
            }

            // Static factory methods - start a new search

            /// <summary>Search by GameObject name (supports * wildcards).</summary>
            public static Search ByName(string pattern) => new Search().Name(pattern);

            /// <summary>Search by component type.</summary>
            public static Search ByType<T>() where T : Component => new Search().Type<T>();

            /// <summary>Search by component type name (supports * wildcards).</summary>
            public static Search ByType(string typeName) => new Search().Type(typeName);

            /// <summary>Search by text content in Text/TMP_Text (supports * wildcards).</summary>
            public static Search ByText(string pattern) => new Search().Text(pattern);

            /// <summary>Search by sprite name in Image/SpriteRenderer (supports * wildcards).</summary>
            public static Search BySprite(string pattern) => new Search().Sprite(pattern);

            /// <summary>Search by hierarchy path (supports * wildcards).</summary>
            public static Search ByPath(string pattern) => new Search().Path(pattern);

            /// <summary>Search by GameObject tag.</summary>
            public static Search ByTag(string tag) => new Search().Tag(tag);

            /// <summary>Search by any: name, text, sprite, or path (supports * wildcards).</summary>
            public static Search ByAny(string pattern) => new Search().Any(pattern);

            /// <summary>
            /// Search for an interactable element (InputField, Dropdown, Slider, Toggle) that is
            /// spatially adjacent to a Text/TMP_Text matching the pattern in the specified direction.
            /// Example: Search.ByAdjacent("Username:", Adjacent.Right) finds the input field to the right of "Username:" text.
            /// </summary>
            public static Search ByAdjacent(string textPattern, Adjacent direction = Adjacent.Right) => new Search().AdjacentTo(textPattern, direction);

            /// <summary>Search by custom predicate on the GameObject.</summary>
            public static Search Where(Func<GameObject, bool> predicate) => new Search().With(predicate);

            // Chainable instance methods

            /// <summary>Negates the next condition.</summary>
            public Search Not { get { _nextNegate = true; return this; } }

            /// <summary>
            /// Check a property on a component. Returns false if component doesn't exist.
            /// Example: Search.Type&lt;Slider&gt;().With&lt;Slider&gt;(s => s.value > 0.5f)
            /// </summary>
            public Search With<T>(Func<T, bool> predicate) where T : Component
            {
                bool negate = _nextNegate;
                _nextNegate = false;
                _conditions.Add(go =>
                {
                    var comp = go.GetComponent<T>();
                    if (comp == null) return negate; // No component = no match (or match if negated)
                    bool match = predicate(comp);
                    return negate != match;
                });
                return this;
            }

            /// <summary>Add a custom GameObject predicate (chainable version).</summary>
            public Search With(Func<GameObject, bool> predicate)
            {
                bool negate = _nextNegate;
                _nextNegate = false;
                _conditions.Add(go => negate ? !predicate(go) : predicate(go));
                return this;
            }

            /// <summary>
            /// Match if the immediate parent matches the given Search query.
            /// Example: Search.ByName("Button").HasParent(Search.ByName("Panel"))
            /// </summary>
            public Search HasParent(Search parentSearch)
            {
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
            /// Match if the immediate parent's name matches the pattern (supports * wildcards).
            /// Shorthand for HasParent(Search.ByName(pattern)).
            /// </summary>
            public Search HasParent(string parentNamePattern)
            {
                return HasParent(ByName(parentNamePattern));
            }

            /// <summary>
            /// Match if any ancestor (parent, grandparent, etc.) matches the given Search query.
            /// Example: Search.ByType&lt;Button&gt;().HasAncestor(Search.ByName("*Settings*"))
            /// </summary>
            public Search HasAncestor(Search ancestorSearch)
            {
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
            /// Match if any ancestor's name matches the pattern (supports * wildcards).
            /// Shorthand for HasAncestor(Search.ByName(pattern)).
            /// </summary>
            public Search HasAncestor(string ancestorNamePattern)
            {
                return HasAncestor(ByName(ancestorNamePattern));
            }

            /// <summary>
            /// Match if any immediate child matches the given Search query.
            /// Example: Search.ByName("Panel").HasChild(Search.ByText("Submit"))
            /// </summary>
            public Search HasChild(Search childSearch)
            {
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
            /// Match if any immediate child's name matches the pattern (supports * wildcards).
            /// Shorthand for HasChild(Search.ByName(pattern)).
            /// </summary>
            public Search HasChild(string childNamePattern)
            {
                return HasChild(ByName(childNamePattern));
            }

            /// <summary>
            /// Match if any descendant (child, grandchild, etc.) matches the given Search query.
            /// Example: Search.ByName("Panel").HasDescendant(Search.ByType&lt;Button&gt;())
            /// </summary>
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
            /// Match if any descendant's name matches the pattern (supports * wildcards).
            /// Shorthand for HasDescendant(Search.ByName(pattern)).
            /// </summary>
            public Search HasDescendant(string descendantNamePattern)
            {
                return HasDescendant(ByName(descendantNamePattern));
            }

            /// <summary>
            /// Match if a component of type T exists in the parent chain (using GetComponentInParent).
            /// Example: Search.ByType&lt;Button&gt;().GetParent&lt;CanvasGroup&gt;()
            /// </summary>
            public Search GetParent<T>() where T : Component
            {
                bool negate = _nextNegate;
                _nextNegate = false;
                _conditions.Add(go =>
                {
                    var comp = go.GetComponentInParent<T>();
                    // GetComponentInParent also returns components on self, so check parent chain only
                    if (comp != null && comp.gameObject == go)
                        comp = go.transform.parent?.GetComponentInParent<T>();
                    bool match = comp != null;
                    return negate != match;
                });
                return this;
            }

            /// <summary>
            /// Match if a component of type T exists in the parent chain that satisfies the predicate.
            /// Example: Search.ByType&lt;Button&gt;().GetParent&lt;CanvasGroup&gt;(cg => cg.alpha > 0)
            /// </summary>
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
            /// Match if a component of type T exists in children (using GetComponentInChildren).
            /// Example: Search.ByName("Slot*").GetChild&lt;Image&gt;()
            /// </summary>
            public Search GetChild<T>() where T : Component
            {
                bool negate = _nextNegate;
                _nextNegate = false;
                _conditions.Add(go =>
                {
                    var comp = go.GetComponentInChildren<T>();
                    // GetComponentInChildren also returns components on self, so check children only
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
            /// Match if a component of type T exists in children that satisfies the predicate.
            /// Example: Search.ByName("Slot*").GetChild&lt;Image&gt;(img => img.sprite != null)
            /// </summary>
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

            static bool FindMatchingChildComponent<T>(Transform parent, Func<T, bool> predicate, bool skipSelf) where T : Component
            {
                if (!skipSelf)
                {
                    var comp = parent.GetComponent<T>();
                    if (comp != null && predicate(comp))
                        return true;
                }
                foreach (Transform child in parent)
                {
                    if (FindMatchingChildComponent(child, predicate, skipSelf: false))
                        return true;
                }
                return false;
            }

            /// <summary>
            /// Filter to elements whose screen position is within the specified region.
            /// Screen is divided into a 3x3 grid.
            /// Example: Search.ByType&lt;Button&gt;().InRegion(ScreenRegion.TopRight)
            /// </summary>
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
            /// Filter to elements whose screen position is within the specified custom bounds (normalized 0-1).
            /// Example: Search.ByType&lt;Button&gt;().InRegion(0.5f, 0.5f, 1f, 1f) // Right-top quadrant
            /// </summary>
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

            /// <summary>
            /// Gets the screen position of a GameObject (works with UI RectTransform, Renderer bounds, or transform position).
            /// </summary>
            static Vector2 GetScreenPositionForSearch(GameObject go)
            {
                // Try RectTransform first (UI elements)
                if (go.TryGetComponent<RectTransform>(out var rect))
                {
                    Vector3[] corners = new Vector3[4];
                    rect.GetWorldCorners(corners);
                    Vector3 center = (corners[0] + corners[2]) / 2f;

                    var canvas = go.GetComponentInParent<Canvas>();
                    if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                    {
                        return center;
                    }
                    else
                    {
                        Camera cam = canvas?.worldCamera ?? Camera.main;
                        return cam != null ? RectTransformUtility.WorldToScreenPoint(cam, center) : (Vector2)center;
                    }
                }

                // Try Renderer bounds (3D objects)
                if (go.TryGetComponent<Renderer>(out var renderer))
                {
                    Camera cam = Camera.main;
                    if (cam != null)
                    {
                        return cam.WorldToScreenPoint(renderer.bounds.center);
                    }
                }

                // Fallback to transform position
                {
                    Camera cam = Camera.main;
                    return cam != null ? (Vector2)cam.WorldToScreenPoint(go.transform.position) : Vector2.zero;
                }
            }

            static bool IsInScreenRegion(GameObject go, ScreenRegion region)
            {
                Vector2 screenPos = GetScreenPositionForSearch(go);

                // Normalize to 0-1 range
                float normalizedX = screenPos.x / Screen.width;
                float normalizedY = screenPos.y / Screen.height;

                // Determine region bounds (3x3 grid)
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
                Vector2 screenPos = GetScreenPositionForSearch(go);

                float normalizedX = screenPos.x / Screen.width;
                float normalizedY = screenPos.y / Screen.height;

                return normalizedX >= xMin && normalizedX <= xMax && normalizedY >= yMin && normalizedY <= yMax;
            }

            // Ordering and filtering - these are stored as post-processing operations
            int _skipCount;
            int _takeCount = -1; // -1 means take all
            bool _takeLast;
            Func<IEnumerable<GameObject>, IEnumerable<GameObject>> _orderBy;

            // Target transformation - applied to each matched object after all filtering
            Func<GameObject, GameObject> _targetTransform;

            /// <summary>
            /// Take only the first matching element (by screen position, top-left to bottom-right).
            /// Example: Search.ByName("ListItem*").First()
            /// </summary>
            public Search First()
            {
                _takeCount = 1;
                _takeLast = false;
                _orderBy ??= OrderByScreenPosition;
                return this;
            }

            /// <summary>
            /// Take only the last matching element (by screen position, top-left to bottom-right).
            /// Example: Search.ByName("ListItem*").Last()
            /// </summary>
            public Search Last()
            {
                _takeCount = 1;
                _takeLast = true;
                _orderBy ??= OrderByScreenPosition;
                return this;
            }

            /// <summary>
            /// Skip the first N matching elements.
            /// Example: Search.ByName("Tab*").Skip(2).First() // Get the 3rd tab
            /// </summary>
            public Search Skip(int count)
            {
                _skipCount = count;
                return this;
            }

            /// <summary>
            /// Order matches by a component property value.
            /// Example: Search.ByType&lt;Slider&gt;().OrderBy&lt;Slider&gt;(s => s.value).First()
            /// </summary>
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
            /// Order matches by a component property value (descending).
            /// Example: Search.ByType&lt;Slider&gt;().OrderByDescending&lt;Slider&gt;(s => s.value).First()
            /// </summary>
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
            /// Explicitly order by screen position (top-left to bottom-right reading order).
            /// This is the default order for First() and Last().
            /// Example: Search.ByName("Item*").OrderByPosition().Skip(3).First()
            /// </summary>
            public Search OrderByPosition()
            {
                _orderBy = OrderByScreenPosition;
                return this;
            }

            static IEnumerable<GameObject> OrderByScreenPosition(IEnumerable<GameObject> objects)
            {
                var objectsList = objects.ToList();
                Debug.Log($"[UITEST] OrderByScreenPosition: Ordering {objectsList.Count} objects");
                foreach (var go in objectsList)
                {
                    Vector2 screenPos = GetScreenPositionForSearch(go);
                    float sortKey = -screenPos.y * 10000 + screenPos.x;
                    Debug.Log($"[UITEST] OrderByScreenPosition: {go.name} at screenPos={screenPos}, sortKey={sortKey}");
                }

                return objectsList.OrderBy(go =>
                {
                    Vector2 screenPos = GetScreenPositionForSearch(go);

                    // Top-left to bottom-right: prioritize Y (descending), then X (ascending)
                    // Higher Y = top, so negate it. Lower X = left.
                    return -screenPos.y * 10000 + screenPos.x;
                });
            }

            /// <summary>
            /// After finding the matching element, return its parent instead.
            /// Example: Search.ByText("Title").Parent() - finds element with text, returns its parent
            /// </summary>
            public Search Parent()
            {
                var previous = _targetTransform;
                _targetTransform = go =>
                {
                    var target = previous != null ? previous(go) : go;
                    return target?.transform.parent?.gameObject;
                };
                return this;
            }

            /// <summary>
            /// After finding the matching element, return a child by index.
            /// Example: Search.ByName("Container").Child(0) - finds Container, returns its first child
            /// </summary>
            public Search Child(int index)
            {
                var previous = _targetTransform;
                _targetTransform = go =>
                {
                    var target = previous != null ? previous(go) : go;
                    if (target == null) return null;
                    var transform = target.transform;
                    if (index < 0 || index >= transform.childCount) return null;
                    return transform.GetChild(index).gameObject;
                };
                return this;
            }

            /// <summary>
            /// After finding the matching element, return a sibling by offset.
            /// Example: Search.ByText("Label").Sibling(1) - finds Label, returns the next sibling
            /// Example: Search.ByText("Label").Sibling(-1) - finds Label, returns the previous sibling
            /// </summary>
            public Search Sibling(int offset)
            {
                var previous = _targetTransform;
                _targetTransform = go =>
                {
                    var target = previous != null ? previous(go) : go;
                    if (target == null) return null;
                    var transform = target.transform;
                    var parent = transform.parent;
                    if (parent == null) return null;

                    int currentIndex = transform.GetSiblingIndex();
                    int newIndex = currentIndex + offset;
                    if (newIndex < 0 || newIndex >= parent.childCount) return null;
                    return parent.GetChild(newIndex).gameObject;
                };
                return this;
            }

            /// <summary>
            /// Gets whether this search has any post-processing (Skip, First, Last, OrderBy, target transform).
            /// </summary>
            public bool HasPostProcessing => _skipCount > 0 || _takeCount >= 0 || _orderBy != null || _targetTransform != null;

            /// <summary>
            /// Gets whether this search has a target transformation (Parent, Child, Sibling).
            /// When true, Find should collect matches regardless of component type T, then check T after transformation.
            /// </summary>
            public bool HasTargetTransform => _targetTransform != null;

            /// <summary>
            /// Apply post-processing (ordering, skip, take, target transform) to a list of matching GameObjects.
            /// Returns the filtered/ordered/transformed results.
            /// </summary>
            public IEnumerable<GameObject> ApplyPostProcessing(IEnumerable<GameObject> matches)
            {
                // Materialize input to avoid multiple enumeration issues
                var result = matches.ToList().AsEnumerable();

                Debug.Log($"[UITEST] ApplyPostProcessing: Input count={result.Count()}, _skipCount={_skipCount}, _takeCount={_takeCount}, _takeLast={_takeLast}, _orderBy={(_orderBy != null ? "set" : "null")}, _targetTransform={(_targetTransform != null ? "set" : "null")}");
                Debug.Log($"[UITEST] ApplyPostProcessing: Input objects: {string.Join(", ", result.Select(go => go.name))}");

                if (_orderBy != null)
                {
                    result = _orderBy(result).ToList(); // Materialize after ordering
                    Debug.Log($"[UITEST] ApplyPostProcessing: After OrderBy: {string.Join(", ", result.Select(go => go.name))}");
                }

                if (_skipCount > 0)
                {
                    result = result.Skip(_skipCount).ToList(); // Materialize after skip
                    Debug.Log($"[UITEST] ApplyPostProcessing: After Skip({_skipCount}): {string.Join(", ", result.Select(go => go.name))}");
                }

                if (_takeCount >= 0)
                {
                    result = _takeLast ? result.TakeLast(_takeCount).ToList() : result.Take(_takeCount).ToList(); // Materialize after take
                    Debug.Log($"[UITEST] ApplyPostProcessing: After Take({_takeCount}, takeLast={_takeLast}): {string.Join(", ", result.Select(go => go.name))}");
                }

                // Apply target transformation (Parent, Child, Sibling) after filtering
                if (_targetTransform != null)
                {
                    result = result.Select(_targetTransform).Where(go => go != null).ToList(); // Materialize after transform
                    Debug.Log($"[UITEST] ApplyPostProcessing: After TargetTransform: {string.Join(", ", result.Select(go => go.name))}");
                }

                Debug.Log($"[UITEST] ApplyPostProcessing: Final result count={result.Count()}");
                return result;
            }

            static bool HasMatchingDescendant(Transform parent, Search search)
            {
                foreach (Transform child in parent)
                {
                    if (search.Matches(child.gameObject))
                        return true;
                    if (HasMatchingDescendant(child, search))
                        return true;
                }
                return false;
            }

            /// <summary>Evaluates if a GameObject matches all conditions.</summary>
            public bool Matches(GameObject go)
            {
                if (go == null) return false;
                foreach (var condition in _conditions)
                {
                    if (!condition(go))
                        return false;
                }
                return true;
            }

            /// <summary>
            /// Implicit conversion from string to Search.ByText().
            /// Allows: await Click((Search)"Play") or passing strings where Search is expected.
            /// </summary>
            public static implicit operator Search(string text) => ByText(text);

            // Chainable Add methods (can also be used for chaining after static factory)

            /// <summary>Also match by GameObject name (supports * wildcards).</summary>
            public Search Name(string pattern)
            {
                bool negate = _nextNegate;
                _nextNegate = false;
                _conditions.Add(go => negate != WildcardMatch(go.name, pattern));
                return this;
            }

            /// <summary>Also match by component type.</summary>
            public Search Type<T>() where T : Component
            {
                bool negate = _nextNegate;
                _nextNegate = false;
                _conditions.Add(go => negate != (go.GetComponent<T>() != null));
                return this;
            }

            /// <summary>Also match by component type name (supports * wildcards).</summary>
            public Search Type(string typeName)
            {
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

            /// <summary>Also match by text content (supports * wildcards).</summary>
            public Search Text(string pattern)
            {
                bool negate = _nextNegate;
                _nextNegate = false;
                _conditions.Add(go => negate != MatchText(go, pattern));
                return this;
            }

            /// <summary>Also match by sprite name (supports * wildcards).</summary>
            public Search Sprite(string pattern)
            {
                bool negate = _nextNegate;
                _nextNegate = false;
                _conditions.Add(go => negate != MatchSprite(go, pattern));
                return this;
            }

            /// <summary>Also match by hierarchy path (supports * wildcards).</summary>
            public Search Path(string pattern)
            {
                bool negate = _nextNegate;
                _nextNegate = false;
                _conditions.Add(go => negate != WildcardMatch(GetHierarchyPath(go.transform), pattern));
                return this;
            }

            /// <summary>Also match by GameObject tag.</summary>
            public Search Tag(string tag)
            {
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

            /// <summary>Also match by any: name, text, sprite, or path (supports * wildcards).</summary>
            public Search Any(string pattern)
            {
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
            /// Match if this element is an interactable (InputField, Dropdown, Slider, Toggle) that is
            /// spatially adjacent to a Text/TMP_Text matching the pattern in the specified direction.
            /// Uses world position to find the nearest interactable.
            /// Example: Search.ByAdjacent("Username:", Adjacent.Right) finds the input field next to "Username:" text.
            /// </summary>
            public Search AdjacentTo(string textPattern, Adjacent direction = Adjacent.Right)
            {
                bool negate = _nextNegate;
                _nextNegate = false;
                _conditions.Add(go =>
                {
                    bool match = IsNearestInteractableToText(go, textPattern, direction);
                    return negate != match;
                });
                return this;
            }

            /// <summary>
            /// Checks if this interactable is the nearest one to any text matching the pattern in the scene.
            /// Uses world/screen position for spatial proximity, not hierarchy.
            /// </summary>
            static bool IsNearestInteractableToText(GameObject interactable, string textPattern, Adjacent direction)
            {
                // Must have an interactable component
                if (!HasInteractableComponent(interactable))
                    return false;

                var interactableRect = interactable.GetComponent<RectTransform>();
                if (interactableRect == null)
                    return false;

                // Find all text elements in the scene matching the pattern
                var matchingTexts = FindTextsMatchingPattern(textPattern);
                if (matchingTexts.Count == 0)
                    return false;

                // For each matching text, check if this interactable is the nearest one in the specified direction
                foreach (var text in matchingTexts)
                {
                    var nearest = FindNearestInteractableInScene(text, direction);
                    if (nearest == interactable)
                        return true;
                }

                return false;
            }

            /// <summary>Find all Text/TMP_Text GameObjects in the scene matching the pattern.</summary>
            static List<GameObject> FindTextsMatchingPattern(string pattern)
            {
                var result = new List<GameObject>();

                // Check legacy Text components
                foreach (var text in GameObject.FindObjectsByType<Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (text != null && !string.IsNullOrEmpty(text.text) && WildcardMatch(text.text, pattern))
                        result.Add(text.gameObject);
                }

                // Check TMP_Text components
                foreach (var tmpText in GameObject.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (tmpText != null && !string.IsNullOrEmpty(tmpText.text) && WildcardMatch(tmpText.text, pattern))
                        result.Add(tmpText.gameObject);
                }

                return result;
            }

            /// <summary>Check if the GameObject has any Selectable component (Button, Toggle, Slider, InputField, Dropdown, Scrollbar, or custom).</summary>
            static bool HasInteractableComponent(GameObject go)
            {
                return go.GetComponent<Selectable>() != null;
            }

            /// <summary>Find the nearest interactable in the entire scene to the text in the specified direction.</summary>
            static GameObject FindNearestInteractableInScene(GameObject textObj, Adjacent direction)
            {
                var textRect = textObj.GetComponent<RectTransform>();
                if (textRect == null)
                    return null;

                Vector3 textPos = textRect.position;
                GameObject nearest = null;
                float nearestScore = float.MaxValue;

                // Search all Selectable components in the scene (Button, Toggle, Slider, InputField, Dropdown, Scrollbar, etc.)
                var selectables = GameObject.FindObjectsByType<Selectable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

                foreach (var selectable in selectables)
                {
                    if (selectable.gameObject == textObj)
                        continue;

                    var interactableRect = selectable.GetComponent<RectTransform>();
                    if (interactableRect == null)
                        continue;

                    // Skip if not active
                    if (!selectable.gameObject.activeInHierarchy)
                        continue;

                    Vector3 interactablePos = interactableRect.position;

                    // Calculate offset from text to interactable
                    float dx = interactablePos.x - textPos.x;
                    float dy = textPos.y - interactablePos.y; // Positive when interactable is below text

                    float score = CalculateAdjacentScore(dx, dy, direction);
                    if (score < nearestScore)
                    {
                        nearestScore = score;
                        nearest = selectable.gameObject;
                    }
                }

                return nearest;
            }

            /// <summary>Calculate a score for how well an interactable matches the desired direction from the text.</summary>
            static float CalculateAdjacentScore(float dx, float dy, Adjacent direction)
            {
                const float rowTolerance = 30f; // Elements within 30 units vertically are on "same row"
                const float colTolerance = 100f; // Elements within 100 units horizontally are "same column"

                switch (direction)
                {
                    case Adjacent.Right:
                        // Must be to the right, prefer same row
                        if (dx <= 0) return float.MaxValue;
                        return Mathf.Abs(dy) < rowTolerance ? dx : dx + Mathf.Abs(dy) * 10;

                    case Adjacent.Left:
                        // Must be to the left, prefer same row
                        if (dx >= 0) return float.MaxValue;
                        return Mathf.Abs(dy) < rowTolerance ? -dx : -dx + Mathf.Abs(dy) * 10;

                    case Adjacent.Below:
                        // Must be below, prefer same column
                        if (dy <= 0) return float.MaxValue;
                        return Mathf.Abs(dx) < colTolerance ? dy : dy + Mathf.Abs(dx) * 10;

                    case Adjacent.Above:
                        // Must be above, prefer same column
                        if (dy >= 0) return float.MaxValue;
                        return Mathf.Abs(dx) < colTolerance ? -dy : -dy + Mathf.Abs(dx) * 10;

                    default:
                        return float.MaxValue;
                }
            }

            static bool WildcardMatch(string subject, string pattern)
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

            static bool MatchText(GameObject go, string pattern)
            {
                // Check all legacy Text components
                foreach (var text in go.GetComponentsInChildren<Text>())
                {
                    if (text != null && !string.IsNullOrEmpty(text.text) && WildcardMatch(text.text, pattern))
                        return true;
                }

                // Check all TMP_Text components
                foreach (var tmpText in go.GetComponentsInChildren<TMP_Text>())
                {
                    if (tmpText != null && !string.IsNullOrEmpty(tmpText.text) && WildcardMatch(tmpText.text, pattern))
                        return true;
                }

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
        }

        #endregion

        public class TestException : Exception
        {
            public TestException(string message) : base(message) { }
        }
        /// <summary>
        /// Direction for swipe gestures.
        /// </summary>
        public enum SwipeDirection
        {
            Left,
            Right,
            Up,
            Down
        }

        public static int Interval { get; set; } = 100;

        /// <summary>
        /// When true, increases all intervals and enables verbose logging for debugging tests.
        /// </summary>
        public static bool DebugMode { get; set; } = false;

        /// <summary>
        /// Multiplier for all intervals when DebugMode is enabled. Default is 3x.
        /// </summary>
        public static float DebugIntervalMultiplier { get; set; } = 3f;

        /// <summary>
        /// Gets the effective interval, accounting for debug mode multiplier.
        /// </summary>
        static int EffectiveInterval => DebugMode ? (int)(Interval * DebugIntervalMultiplier) : Interval;

        static void LogDebug(string message)
        {
            if (DebugMode)
                Debug.Log($"[UITEST:DEBUG] {message}");
        }

        static readonly List<Type> clickablesList = new() { typeof(Selectable) };
        static Type[] clickablesArray = { typeof(Selectable) };
        public static Type[] Clickables => clickablesArray;

        static CancellationTokenSource testCts;
        protected static CancellationToken TestCancellationToken => testCts != null ? testCts.Token : CancellationToken.None;

        public static void RegisterClickable(Type type)
        {
            if (type != null && !clickablesList.Contains(type))
            {
                clickablesList.Add(type);
                clickablesArray = clickablesList.ToArray();
            }
        }

        public static void RegisterClickable<T>() => RegisterClickable(typeof(T));

        public static void UnregisterClickable(Type type)
        {
            if (clickablesList.Remove(type))
                clickablesArray = clickablesList.ToArray();
        }

        public static void UnregisterClickable<T>() => UnregisterClickable(typeof(T));

        /// <summary>
        /// Delegate for custom key press handlers (e.g., EzGUI).
        /// Returns true if the key was handled, false to fall back to Unity UI handling.
        /// </summary>
        public delegate bool KeyPressHandler(GameObject target, KeyCode key);

        static readonly List<KeyPressHandler> keyPressHandlers = new();

        /// <summary>
        /// Register a custom key press handler (e.g., for EzGUI support).
        /// Handlers are tried in order until one returns true.
        /// </summary>
        public static void RegisterKeyPressHandler(KeyPressHandler handler)
        {
            if (handler != null && !keyPressHandlers.Contains(handler))
                keyPressHandlers.Add(handler);
        }

        /// <summary>
        /// Unregister a custom key press handler.
        /// </summary>
        public static void UnregisterKeyPressHandler(KeyPressHandler handler)
        {
            keyPressHandlers.Remove(handler);
        }

        public static void StopTest()
        {
            if (testCts != null)
                testCts.Cancel();
        }

        protected virtual void OnDestroy()
        {
            if (testCts != null && Scenario == testScenario)
            {
                testCts.Cancel();
                testCts.Dispose();
                testCts = null;
            }
        }

        static float lastActionTime;

        /// <summary>
        /// Called at the end of each action to wait for the interval before continuing.
        /// </summary>
        static async UniTask ActionComplete()
        {
            lastActionTime = Time.realtimeSinceStartup;

            // Always yield at least one frame to allow Unity to process events
            await UniTask.Yield(PlayerLoopTiming.Update, TestCancellationToken);

            // Then wait for the remaining interval time (uses EffectiveInterval for debug mode)
            int delay = EffectiveInterval;
            LogDebug($"ActionComplete: waiting {delay}ms");
            await UniTask.Delay(delay, true, PlayerLoopTiming.Update, TestCancellationToken);
        }

        /// <summary>
        /// Waits until the editor is fully in play mode and ready for testing.
        /// </summary>
        static async UniTask WaitForPlayModeReady()
        {
#if UNITY_EDITOR
            // Wait until EditorApplication.isPlaying is true and isPlayingOrWillChangePlaymode settles
            while (!UnityEditor.EditorApplication.isPlaying || UnityEditor.EditorApplication.isCompiling)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, TestCancellationToken);
            }
#endif
            // Wait for Time.timeScale to be non-zero (game not paused)
            while (Time.timeScale == 0)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, TestCancellationToken);
            }

            // Yield a few frames to let Unity fully settle
            for (int i = 0; i < 3; i++)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, TestCancellationToken);
            }
        }

        /// <summary>
        /// Checks if an object meets the availability requirements specified by a Search query.
        /// By default (no IncludeInactive/IncludeDisabled), requires active and enabled.
        /// </summary>
        static bool CheckAvailability(UnityEngine.Object obj, Search search)
        {
            if (obj == null)
                return false;

            GameObject go = null;
            Behaviour behaviour = null;

            if (obj is GameObject g)
                go = g;
            else if (obj is UnityEngine.Component c)
            {
                go = c.gameObject;
                behaviour = c as Behaviour;
            }

            if (go == null)
                return false;

            // Check active state (unless IncludeInactive is set)
            if (search == null || !search.ShouldIncludeInactive)
            {
                if (!go.activeInHierarchy)
                    return false;
            }

            // Check enabled/interactable state (unless IncludeDisabled is set)
            if (search == null || !search.ShouldIncludeDisabled)
            {
                if (behaviour != null && !behaviour.enabled)
                    return false;

                var canvasGroup = go.GetComponentInParent<CanvasGroup>();
                if (canvasGroup != null && (canvasGroup.alpha <= 0 || !canvasGroup.interactable))
                    return false;
            }

            return true;
        }

        public int Scenario
        {
            get
            {
                var attr = GetType().GetCustomAttribute<UITestAttribute>();
                if (attr == null)
                    throw new InvalidOperationException($"{GetType().Name} missing [UITest] attribute");
                if (attr.Scenario <= 0)
                    throw new InvalidOperationException($"{GetType().Name} [UITest] Scenario must be > 0");
                return attr.Scenario;
            }
        }

        public static bool Active => testScenario != 0;

        static int testScenario = 0;
        static string lastKnownScene;
        static float lastSceneChangeTime;

        protected void CaptureScreenshot(string name = "screenshot")
        {
            string path = Path.Combine(Application.temporaryCachePath, $"{name}_{DateTime.Now.Ticks}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[UITEST] Screenshot: {path}");
        }

        protected void AttachJson(string name, object data)
        {
            string json = JsonUtility.ToJson(data, true);
            Debug.Log($"[UITEST] Attach JSON '{name}': {json}");
        }

        protected void AttachText(string name, string content)
        {
            Debug.Log($"[UITEST] Attach Text '{name}': {content}");
        }

        protected void AttachFile(string name, string filePath, string mimeType)
        {
            if (File.Exists(filePath))
            {
                Debug.Log($"[UITEST] Attach File '{name}': {filePath} ({mimeType})");
            }
        }

        protected void AttachFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                Debug.Log($"[UITEST] Attach File: {filePath}");
            }
        }

        protected void AddParameter(string name, string value)
        {
            Debug.Log($"[UITEST] Parameter: {name}={value}");
        }

        protected void PauseRecording()
        {
        }

        protected void ResumeRecording()
        {
        }


        public static string TestRunName { get; private set; } = "";

#if UNITY_EDITOR
        [ContextMenu("Run Test")]
        void RunTest()
        {
            StartTest(clearData: false, testDataPath: null, debugMode: false);
        }

        [ContextMenu("Run Test (Debug)")]
        void RunTestDebug()
        {
            StartTest(clearData: false, testDataPath: null, debugMode: true);
        }

        [ContextMenu("Run Test (Clear Data)")]
        void RunTestClearData()
        {
            StartTest(clearData: true, testDataPath: null, debugMode: false);
        }

        [ContextMenu("Run Test with Data Folder...")]
        void RunTestWithDataFolder()
        {
            string folder = UnityEditor.EditorUtility.OpenFolderPanel("Select Test Data Folder", Application.persistentDataPath, "data");
            if (!string.IsNullOrEmpty(folder))
            {
                StartTest(clearData: false, testDataPath: folder, debugMode: false);
            }
        }

        [ContextMenu("Run Test with Data Zip...")]
        void RunTestWithDataZip()
        {
            string zipPath = UnityEditor.EditorUtility.OpenFilePanel("Select Test Data Zip", Application.persistentDataPath, "zip");
            if (!string.IsNullOrEmpty(zipPath))
            {
                StartTest(clearData: false, testDataPath: zipPath, debugMode: false);
            }
        }

        void StartTest(bool clearData, string testDataPath, bool debugMode)
        {
            int scenario = Scenario;
            TestRunName = GetType().Name + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            if (clearData)
            {
                try
                {
                    string folder = Path.Combine(Application.persistentDataPath, "data");
                    if (Directory.Exists(folder))
                        Directory.Delete(folder, true);
                    PlayerPrefs.DeleteAll();
                    Debug.Log("[UITEST] Cleared test data");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[UITEST] Failed to clear: {ex.Message}");
                }
            }

            UnityEditor.SessionState.SetBool("GAME_LOOP_TEST", true);
            UnityEditor.SessionState.SetInt("GAME_LOOP_TEST_SCENARIO", scenario);
            UnityEditor.SessionState.SetString("GAME_LOOP_TEST_NAME", TestRunName);
            UnityEditor.SessionState.SetString("GAME_LOOP_TEST_DATA_PATH", testDataPath ?? "");
            UnityEditor.SessionState.SetBool("GAME_LOOP_TEST_DEBUG", debugMode);
            UnityEditor.EditorApplication.isPlaying = true;
        }

        string GetTestDataPath()
        {
            string testFolder = Path.Combine(Application.dataPath, "UITestBehaviours", "GeneratedTests");
            string testName = GetType().Name;

            if (!Directory.Exists(testFolder))
                return null;

            var directories = Directory.GetDirectories(testFolder, "*", SearchOption.TopDirectoryOnly);
            foreach (var dir in directories)
            {
                string zipPath = Path.Combine(dir, "testdata.zip");
                if (File.Exists(zipPath))
                {
                    string scriptPath = Path.Combine(dir, $"{testName}.cs");
                    if (File.Exists(scriptPath))
                        return zipPath;
                }
            }

            return null;
        }
#endif

        void PrepareTestData()
        {
            var attr = GetType().GetCustomAttribute<UITestAttribute>();
            var dataMode = attr != null ? attr.DataMode : TestDataMode.Ask;

            if (dataMode == TestDataMode.UseCurrent)
            {
                Debug.Log("[UITEST] DataMode=UseCurrent, using existing data");
                return;
            }

            string testDataPath = FindTestData();
            if (string.IsNullOrEmpty(testDataPath))
            {
                Debug.Log("[UITEST] No test data found, using existing data");
                return;
            }

            string targetPath = Path.Combine(Application.persistentDataPath, "data");

            try
            {
                if (Directory.Exists(targetPath))
                    Directory.Delete(targetPath, true);

                if (testDataPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    System.IO.Compression.ZipFile.ExtractToDirectory(testDataPath, Application.persistentDataPath);
                    Debug.Log($"[UITEST] Extracted test data from: {testDataPath}");
                }
                else if (Directory.Exists(testDataPath))
                {
                    CopyDirectoryRuntime(testDataPath, targetPath);
                    Debug.Log($"[UITEST] Copied test data from: {testDataPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UITEST] Failed to prepare test data: {ex.Message}");
            }
        }

        string FindTestData()
        {
#if UNITY_EDITOR
            // Check if a custom data path was specified via context menu
            string customPath = UnityEditor.SessionState.GetString("GAME_LOOP_TEST_DATA_PATH", "");
            if (!string.IsNullOrEmpty(customPath))
            {
                UnityEditor.SessionState.EraseString("GAME_LOOP_TEST_DATA_PATH");
                if (File.Exists(customPath) || Directory.Exists(customPath))
                    return customPath;
            }

            string editorPath = GetTestDataPath();
            if (!string.IsNullOrEmpty(editorPath))
                return editorPath;
#endif
            return FindTestDataInStreamingAssets();
        }

        string FindTestDataInStreamingAssets()
        {
            string testName = GetType().Name;
            string streamingPath = Application.streamingAssetsPath;

            string zipPath = Path.Combine(streamingPath, "UITestData", $"{testName}.zip");
            if (File.Exists(zipPath))
                return zipPath;

            string folderPath = Path.Combine(streamingPath, "UITestData", testName);
            if (Directory.Exists(folderPath))
                return folderPath;

            return null;
        }

        static void CopyDirectoryRuntime(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string targetFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, targetFile, true);
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string targetSubDir = Path.Combine(targetDir, Path.GetFileName(dir));
                CopyDirectoryRuntime(dir, targetSubDir);
            }
        }


        protected virtual void LateUpdate()
        {
        }

        async void Start()
        {
            await UniTask.Yield();

#if UNITY_EDITOR
            if (UnityEditor.SessionState.GetBool("GAME_LOOP_TEST", false))
            {
                testScenario = UnityEditor.SessionState.GetInt("GAME_LOOP_TEST_SCENARIO", 0);
                TestRunName = UnityEditor.SessionState.GetString("GAME_LOOP_TEST_NAME", "");
                DebugMode = UnityEditor.SessionState.GetBool("GAME_LOOP_TEST_DEBUG", false);
            }

            UnityEditor.SessionState.SetBool("GAME_LOOP_TEST", false);
            UnityEditor.SessionState.SetBool("GAME_LOOP_TEST_DEBUG", false);
#endif

            if (Scenario != 0 && Scenario == testScenario)
            {
                GameObject.DontDestroyOnLoad(this.gameObject);
                EnsureSceneCallbackRegistered();

                PrepareTestData();

                if (testCts != null)
                {
                    testCts.Cancel();
                    testCts.Dispose();
                }
                testCts = new CancellationTokenSource();

                if (DebugMode)
                {
                    Debug.Log($"[UITEST] Test Start (DEBUG MODE): {GetType().Name}");
                    Debug.Log($"[UITEST:DEBUG] Interval: {Interval}ms x {DebugIntervalMultiplier} = {EffectiveInterval}ms");
                }
                else
                {
                    Debug.Log($"[UITEST] Test Start: {GetType().Name}");
                }

                // Wait for editor to be fully in play mode and scene to initialize
                await WaitForPlayModeReady();
                await UniTask.Delay(1000, true, PlayerLoopTiming.Update, TestCancellationToken);

                float testStartTime = Time.realtimeSinceStartup;
                try
                {
                    await Test();
                    float duration = Time.realtimeSinceStartup - testStartTime;
                    Debug.Log($"[UITEST] Test PASSED: {GetType().Name} Duration: {duration:F2}s");
                }
                catch (OperationCanceledException)
                {
                    float duration = Time.realtimeSinceStartup - testStartTime;
                    Debug.Log($"[UITEST] Test CANCELLED: {GetType().Name} Duration: {duration:F2}s");
                }
                catch (Exception ex)
                {
                    float duration = Time.realtimeSinceStartup - testStartTime;
                    Debug.LogException(ex);
                    Debug.Log($"[UITEST] Test FAILED: {GetType().Name} Duration: {duration:F2}s");
                }
                finally
                {
                    Debug.Log($"[UITEST] Test End: {GetType().Name}");

                    if (testCts != null)
                    {
                        testCts.Cancel();
                        testCts.Dispose();
                        testCts = null;
                    }

                    await UniTask.Yield();

#if UNITY_EDITOR
                    Debug.Log($"[UITEST] Stopping play mode...");
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit(0);
#endif
                }
            }
            else
            {
                GameObject.Destroy(this.gameObject);
            }
        }
        static bool sceneCallbackRegistered;

        static void EnsureSceneCallbackRegistered()
        {
            if (sceneCallbackRegistered)
                return;
            sceneCallbackRegistered = true;
            lastKnownScene = SceneManager.GetActiveScene().name;
            lastSceneChangeTime = Time.realtimeSinceStartup;
            SceneManager.sceneLoaded += OnSceneLoadedStatic;
        }

        static void OnSceneLoadedStatic(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != lastKnownScene)
            {
                Debug.Log($"[UITEST] Scene changed: {lastKnownScene} -> {scene.name}");
                lastKnownScene = scene.name;
                lastSceneChangeTime = Time.realtimeSinceStartup;
            }
        }

        public static bool GetScenario(out int test, out string logFile)
        {

            test = 0;
            logFile = "";

#if UNITY_ANDROID && !UNITY_EDITOR

            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            var intent = activity.Call<AndroidJavaObject>("getIntent");
            string action = intent.Call<string>("getAction");

            if (action.Equals("com.google.intent.action.TEST_LOOP"))
            {
                test = intent.Call<int>("getIntExtra", "scenario", 0);
                logFile = intent.Call<string>("getDataString");
                return true;

            }

#endif

            return false;

        }
        protected abstract UniTask Test();

        /// <summary>
        /// Assert a condition is true. Throws TestException if false.
        /// </summary>
        protected void Assert(bool condition, string message = "Assertion failed")
        {
            if (!condition)
            {
                throw new TestException(message);
            }
        }

        protected async UniTask Wait(float seconds = 1f)
        {
            LogDebug($"Wait: {seconds}s");
            await UniTask.Delay((int)(seconds * 1000), true, PlayerLoopTiming.Update, TestCancellationToken);
            await ActionComplete();
        }

        protected async UniTask Wait(Search search, int seconds = 10)
        {
            Debug.Log($"[UITEST] Wait ({seconds}s)");
            LogDebug($"Wait: with {seconds}s timeout");
            await Find<MonoBehaviour>(search, true, seconds);
        }

        protected async UniTask WaitFor(Func<bool> condition, float seconds = 60, string description = "condition")
        {
            Debug.Log($"[UITEST] WaitFor ({seconds}) [{description}]");
            LogDebug($"WaitFor: '{description}' with {seconds}s timeout");

            var startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying && !TestCancellationToken.IsCancellationRequested)
            {
                if (condition())
                {
                    LogDebug($"WaitFor: '{description}' satisfied after {Time.realtimeSinceStartup - startTime:F2}s");
                    await ActionComplete();
                    return;
                }

                await UniTask.Delay(EffectiveInterval, true, PlayerLoopTiming.Update, TestCancellationToken);
            }

            TestCancellationToken.ThrowIfCancellationRequested();
            throw new System.TimeoutException($"Condition '{description}' not met within {seconds} seconds");
        }

        protected async UniTask SceneChange(float seconds = 30, float recentThreshold = 1f)
        {
            string startScene = SceneManager.GetActiveScene().name;
            float startTime = Time.realtimeSinceStartup;

            Debug.Log($"[UITEST] SceneChange - waiting for scene change from '{startScene}' (timeout: {seconds}s)");

            float timeSinceLastChange = startTime - lastSceneChangeTime;
            if (timeSinceLastChange < recentThreshold && lastKnownScene != startScene)
            {
                Debug.Log($"[UITEST] SceneChange - scene recently changed ({timeSinceLastChange:F2}s ago) to '{lastKnownScene}'");
                await ActionComplete();
                return;
            }

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying && !TestCancellationToken.IsCancellationRequested)
            {
                string currentScene = SceneManager.GetActiveScene().name;
                if (currentScene != startScene)
                {
                    Debug.Log($"[UITEST] SceneChange - scene changed to '{currentScene}'");
                    await ActionComplete();
                    return;
                }

                await UniTask.Delay(EffectiveInterval, true, PlayerLoopTiming.Update, TestCancellationToken);
            }

            TestCancellationToken.ThrowIfCancellationRequested();
            throw new System.TimeoutException($"Scene did not change from '{startScene}' within {seconds} seconds");
        }

        protected async UniTask WaitFramerate(int averageFps, float sampleDuration = 2f, float timeout = 60f)
        {
            Debug.Log($"[UITEST] WaitFramerate - waiting for {averageFps} FPS (sample: {sampleDuration}s, timeout: {timeout}s)");

            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying && !TestCancellationToken.IsCancellationRequested)
            {
                float sampleStart = Time.realtimeSinceStartup;
                int frameCount = 0;

                while ((Time.realtimeSinceStartup - sampleStart) < sampleDuration && Application.isPlaying && !TestCancellationToken.IsCancellationRequested)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, TestCancellationToken);
                    frameCount++;
                }

                float elapsed = Time.realtimeSinceStartup - sampleStart;
                float currentFps = frameCount / elapsed;

                if (currentFps >= averageFps)
                {
                    Debug.Log($"[UITEST] WaitFramerate - achieved {currentFps:F1} FPS (target: {averageFps})");
                    await ActionComplete();
                    return;
                }

                Debug.Log($"[UITEST] WaitFramerate - current {currentFps:F1} FPS, waiting for {averageFps}...");
            }

            TestCancellationToken.ThrowIfCancellationRequested();
            throw new System.TimeoutException($"Framerate did not reach {averageFps} FPS within {timeout} seconds");
        }

        /// <summary>
        /// Enters text into an input field using Input System injection (click to focus, type characters).
        /// </summary>
        /// <param name="search">Search query for the input field</param>
        /// <param name="input">Text to enter</param>
        /// <param name="seconds">Timeout for finding the input field</param>
        /// <param name="pressEnter">Whether to press Enter after typing</param>
        protected async UniTask TextInput(Search search, string input, float seconds = 10, bool pressEnter = false)
        {
            Debug.Log($"[UITEST] TextInput ({seconds}) '{input}' pressEnter={pressEnter}");

            // Try to find TMP_InputField or legacy InputField - quick check first (single iteration)
            var findStart = Time.realtimeSinceStartup;
            var tmpInput = await Find<TMP_InputField>(search, false, 0.1f);
            var legacyInputQuick = tmpInput == null ? await Find<InputField>(search, false, 0.1f) : null;

            // If neither found on quick check, do full timeout search for TMP first, then legacy
            if (tmpInput == null && legacyInputQuick == null)
            {
                Debug.Log($"[UITEST] Quick check failed, doing full {seconds}s search...");
                tmpInput = await Find<TMP_InputField>(search, false, seconds);
            }

            Debug.Log($"[UITEST] Find took {(Time.realtimeSinceStartup - findStart) * 1000:F0}ms, TMP={tmpInput != null}, Legacy={legacyInputQuick != null}");

            if (tmpInput != null)
            {
                // Click to focus
                Vector2 screenPosition = GetScreenPosition(tmpInput.gameObject);
                await InjectPointerTap(screenPosition);
                await UniTask.Yield();

                // Type characters using ProcessEvent (adds to text) + ForceLabelUpdate (updates display)
                // TMP_InputField uses IMGUI Event.PopEvent() internally which we can't inject into,
                // so we must call ProcessEvent directly and then force the label update
                if (!string.IsNullOrEmpty(input))
                {
                    foreach (char c in input)
                    {
                        var keyEvent = new Event
                        {
                            type = EventType.KeyDown,
                            character = c,
                            keyCode = CharToKeyCode(c)
                        };
                        tmpInput.ProcessEvent(keyEvent);
                        tmpInput.ForceLabelUpdate();
                        await UniTask.Yield();
                    }
                }

                if (pressEnter)
                {
                    var enterEvent = new Event
                    {
                        type = EventType.KeyDown,
                        character = '\n',
                        keyCode = KeyCode.Return
                    };
                    tmpInput.ProcessEvent(enterEvent);
                }

                await ActionComplete();
                return;
            }

            // Fall back to legacy InputField (use quick result if found, otherwise full search)
            var legacyInput = legacyInputQuick ?? await Find<InputField>(search, true, seconds);

            // Click to focus the input field
            Vector2 legacyScreenPosition = GetScreenPosition(legacyInput.gameObject);
            await InjectPointerTap(legacyScreenPosition);

            // Type characters using ProcessEvent + ForceLabelUpdate
            if (!string.IsNullOrEmpty(input))
            {
                foreach (char c in input)
                {
                    var keyEvent = new Event
                    {
                        type = EventType.KeyDown,
                        character = c,
                        keyCode = CharToKeyCode(c)
                    };
                    legacyInput.ProcessEvent(keyEvent);
                    legacyInput.ForceLabelUpdate();
                    await UniTask.Yield();
                }
            }

            if (pressEnter)
            {
                var enterEvent = new Event
                {
                    type = EventType.KeyDown,
                    character = '\n',
                    keyCode = KeyCode.Return
                };
                legacyInput.ProcessEvent(enterEvent);
            }

            await ActionComplete();
        }

        /// <summary>
        /// Delegate to get the currently focused object from custom UI systems (e.g., EzGUI UIManager.FocusObject).
        /// </summary>
        public delegate GameObject FocusedObjectGetter();

        static readonly List<FocusedObjectGetter> focusedObjectGetters = new();

        /// <summary>
        /// Register a getter for the currently focused object in a custom UI system.
        /// </summary>
        public static void RegisterFocusedObjectGetter(FocusedObjectGetter getter)
        {
            if (getter != null && !focusedObjectGetters.Contains(getter))
                focusedObjectGetters.Add(getter);
        }

        /// <summary>
        /// Simulates a key press. Sends to the currently focused/selected object.
        /// Works with Unity UI (EventSystem.currentSelectedGameObject) and custom UI systems.
        /// </summary>
        protected async UniTask PressKey(KeyCode key)
        {
            GameObject target = GetFocusedObject();
            Debug.Log($"[UITEST] PressKey [{key}] target='{(target != null ? target.name : "none")}'");

            if (target != null && TryCustomKeyHandlers(target, key))
            {
                await ActionComplete();
                return;
            }

            await PressKeyUnityUI(target, key);
            await ActionComplete();
        }

        /// <summary>
        /// Simulates a key press on a specific target found by search query.
        /// </summary>
        protected async UniTask PressKey(KeyCode key, Search search, float seconds = 10)
        {
            var component = await Find<Component>(search, true, seconds);
            GameObject target = component?.gameObject;
            Debug.Log($"[UITEST] PressKey [{key}] target='{(target != null ? target.name : "none")}'");

            if (target != null && TryCustomKeyHandlers(target, key))
            {
                await ActionComplete();
                return;
            }

            await PressKeyUnityUI(target, key);
            await ActionComplete();
        }

        /// <summary>
        /// Simulates a key press using a character (e.g., 'a', '1', ' ').
        /// </summary>
        protected async UniTask PressKey(char c)
        {
            var keyCode = CharToKeyCode(c);
            if (keyCode == KeyCode.None)
            {
                Debug.LogWarning($"[UITEST] PressKey - Unable to map character '{c}' to KeyCode");
                return;
            }
            await PressKey(keyCode);
        }

        /// <summary>
        /// Simulates a key press using a key name string (e.g., "Enter", "Space", "A", "Escape").
        /// </summary>
        protected async UniTask PressKey(string keyName)
        {
            // Try single character first
            if (keyName.Length == 1)
            {
                await PressKey(keyName[0]);
                return;
            }

            // Try parsing as KeyCode name
            if (System.Enum.TryParse<KeyCode>(keyName, true, out var keyCode))
            {
                await PressKey(keyCode);
                return;
            }

            // Handle common aliases
            var mappedKey = keyName.ToLowerInvariant() switch
            {
                "enter" => KeyCode.Return,
                "esc" => KeyCode.Escape,
                "up" => KeyCode.UpArrow,
                "down" => KeyCode.DownArrow,
                "left" => KeyCode.LeftArrow,
                "right" => KeyCode.RightArrow,
                "bs" or "backspace" => KeyCode.Backspace,
                "del" => KeyCode.Delete,
                _ => KeyCode.None
            };

            if (mappedKey != KeyCode.None)
            {
                await PressKey(mappedKey);
                return;
            }

            Debug.LogWarning($"[UITEST] PressKey - Unable to map key name '{keyName}' to KeyCode");
        }

        /// <summary>
        /// Types a sequence of characters by injecting keyboard events.
        /// Uses Input System TextEvent for focused input fields.
        /// </summary>
        protected async UniTask PressKeys(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            Debug.Log($"[UITEST] PressKeys - Typing '{text}' ({text.Length} characters)");

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                Debug.LogWarning("[UITEST] PressKeys - No keyboard device found");
                return;
            }

            // Inject each character as a text event via Input System
            foreach (char c in text)
            {
                var textEvent = TextEvent.Create(keyboard.deviceId, c);
                InputSystem.QueueEvent(ref textEvent);
                await UniTask.Yield();
            }
        }

        /// <summary>
        /// Maps a character to its corresponding KeyCode.
        /// </summary>
        private static KeyCode CharToKeyCode(char c)
        {
            // Letters (case-insensitive)
            if (c >= 'a' && c <= 'z')
                return KeyCode.A + (c - 'a');
            if (c >= 'A' && c <= 'Z')
                return KeyCode.A + (c - 'A');

            // Numbers
            if (c >= '0' && c <= '9')
                return KeyCode.Alpha0 + (c - '0');

            // Common symbols
            return c switch
            {
                ' ' => KeyCode.Space,
                '\n' or '\r' => KeyCode.Return,
                '\t' => KeyCode.Tab,
                '\b' => KeyCode.Backspace,
                '`' or '~' => KeyCode.BackQuote,
                '-' or '_' => KeyCode.Minus,
                '=' or '+' => KeyCode.Equals,
                '[' or '{' => KeyCode.LeftBracket,
                ']' or '}' => KeyCode.RightBracket,
                '\\' or '|' => KeyCode.Backslash,
                ';' or ':' => KeyCode.Semicolon,
                '\'' or '"' => KeyCode.Quote,
                ',' or '<' => KeyCode.Comma,
                '.' or '>' => KeyCode.Period,
                '/' or '?' => KeyCode.Slash,
                _ => KeyCode.None
            };
        }

        private static GameObject GetFocusedObject()
        {
            // Try custom UI systems first (e.g., EzGUI)
            foreach (var getter in focusedObjectGetters)
            {
                var focused = getter();
                if (focused != null)
                    return focused;
            }
            // Fall back to Unity UI EventSystem
            return EventSystem.current?.currentSelectedGameObject;
        }

        private static bool TryCustomKeyHandlers(GameObject target, KeyCode key)
        {
            foreach (var handler in keyPressHandlers)
            {
                if (handler(target, key))
                    return true;
            }
            return false;
        }

        private async UniTask PressKeyUnityUI(GameObject target, KeyCode key)
        {
            // Use the new Input System to inject true key press events
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                Debug.LogWarning("[UITEST] PressKey - No keyboard device found, cannot inject key");
                return;
            }

            var inputKey = KeyCodeToKey(key);
            if (inputKey == Key.None)
            {
                Debug.LogWarning($"[UITEST] PressKey - Unable to map KeyCode.{key} to Input System Key");
                return;
            }

            // Get the character for this key (for text input)
            char? textChar = KeyCodeToChar(key);

            // Inject key down
            using (StateEvent.From(keyboard, out var eventPtr))
            {
                keyboard[inputKey].WriteValueIntoEvent(1f, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }

            // Also inject a text event if this is a printable character
            // This is required for InputField to receive the character
            if (textChar.HasValue)
            {
                var textEvent = TextEvent.Create(keyboard.deviceId, textChar.Value);
                InputSystem.QueueEvent(ref textEvent);
            }

            // Wait a frame for the input to be processed
            await UniTask.Yield();

            // Inject key up
            using (StateEvent.From(keyboard, out var eventPtr))
            {
                keyboard[inputKey].WriteValueIntoEvent(0f, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }
        }

        /// <summary>
        /// Maps a KeyCode to its printable character (if applicable).
        /// </summary>
        private static char? KeyCodeToChar(KeyCode keyCode)
        {
            // Letters (lowercase)
            if (keyCode >= KeyCode.A && keyCode <= KeyCode.Z)
                return (char)('a' + (keyCode - KeyCode.A));

            // Numbers
            if (keyCode >= KeyCode.Alpha0 && keyCode <= KeyCode.Alpha9)
                return (char)('0' + (keyCode - KeyCode.Alpha0));

            // Common symbols
            return keyCode switch
            {
                KeyCode.Space => ' ',
                KeyCode.BackQuote => '`',
                KeyCode.Minus => '-',
                KeyCode.Equals => '=',
                KeyCode.LeftBracket => '[',
                KeyCode.RightBracket => ']',
                KeyCode.Backslash => '\\',
                KeyCode.Semicolon => ';',
                KeyCode.Quote => '\'',
                KeyCode.Comma => ',',
                KeyCode.Period => '.',
                KeyCode.Slash => '/',
                _ => null
            };
        }

        /// <summary>
        /// Maps a legacy KeyCode to the new Input System Key.
        /// </summary>
        private static Key KeyCodeToKey(KeyCode keyCode)
        {
            return keyCode switch
            {
                // Letters
                KeyCode.A => Key.A, KeyCode.B => Key.B, KeyCode.C => Key.C, KeyCode.D => Key.D,
                KeyCode.E => Key.E, KeyCode.F => Key.F, KeyCode.G => Key.G, KeyCode.H => Key.H,
                KeyCode.I => Key.I, KeyCode.J => Key.J, KeyCode.K => Key.K, KeyCode.L => Key.L,
                KeyCode.M => Key.M, KeyCode.N => Key.N, KeyCode.O => Key.O, KeyCode.P => Key.P,
                KeyCode.Q => Key.Q, KeyCode.R => Key.R, KeyCode.S => Key.S, KeyCode.T => Key.T,
                KeyCode.U => Key.U, KeyCode.V => Key.V, KeyCode.W => Key.W, KeyCode.X => Key.X,
                KeyCode.Y => Key.Y, KeyCode.Z => Key.Z,

                // Numbers
                KeyCode.Alpha0 => Key.Digit0, KeyCode.Alpha1 => Key.Digit1, KeyCode.Alpha2 => Key.Digit2,
                KeyCode.Alpha3 => Key.Digit3, KeyCode.Alpha4 => Key.Digit4, KeyCode.Alpha5 => Key.Digit5,
                KeyCode.Alpha6 => Key.Digit6, KeyCode.Alpha7 => Key.Digit7, KeyCode.Alpha8 => Key.Digit8,
                KeyCode.Alpha9 => Key.Digit9,

                // Numpad
                KeyCode.Keypad0 => Key.Numpad0, KeyCode.Keypad1 => Key.Numpad1, KeyCode.Keypad2 => Key.Numpad2,
                KeyCode.Keypad3 => Key.Numpad3, KeyCode.Keypad4 => Key.Numpad4, KeyCode.Keypad5 => Key.Numpad5,
                KeyCode.Keypad6 => Key.Numpad6, KeyCode.Keypad7 => Key.Numpad7, KeyCode.Keypad8 => Key.Numpad8,
                KeyCode.Keypad9 => Key.Numpad9,
                KeyCode.KeypadDivide => Key.NumpadDivide, KeyCode.KeypadMultiply => Key.NumpadMultiply,
                KeyCode.KeypadMinus => Key.NumpadMinus, KeyCode.KeypadPlus => Key.NumpadPlus,
                KeyCode.KeypadEnter => Key.NumpadEnter, KeyCode.KeypadPeriod => Key.NumpadPeriod,

                // Function keys
                KeyCode.F1 => Key.F1, KeyCode.F2 => Key.F2, KeyCode.F3 => Key.F3, KeyCode.F4 => Key.F4,
                KeyCode.F5 => Key.F5, KeyCode.F6 => Key.F6, KeyCode.F7 => Key.F7, KeyCode.F8 => Key.F8,
                KeyCode.F9 => Key.F9, KeyCode.F10 => Key.F10, KeyCode.F11 => Key.F11, KeyCode.F12 => Key.F12,

                // Arrow keys
                KeyCode.UpArrow => Key.UpArrow, KeyCode.DownArrow => Key.DownArrow,
                KeyCode.LeftArrow => Key.LeftArrow, KeyCode.RightArrow => Key.RightArrow,

                // Special keys
                KeyCode.Space => Key.Space, KeyCode.Return => Key.Enter, KeyCode.Escape => Key.Escape,
                KeyCode.Tab => Key.Tab, KeyCode.Backspace => Key.Backspace, KeyCode.Delete => Key.Delete,
                KeyCode.Insert => Key.Insert, KeyCode.Home => Key.Home, KeyCode.End => Key.End,
                KeyCode.PageUp => Key.PageUp, KeyCode.PageDown => Key.PageDown,

                // Modifiers
                KeyCode.LeftShift => Key.LeftShift, KeyCode.RightShift => Key.RightShift,
                KeyCode.LeftControl => Key.LeftCtrl, KeyCode.RightControl => Key.RightCtrl,
                KeyCode.LeftAlt => Key.LeftAlt, KeyCode.RightAlt => Key.RightAlt,
                KeyCode.LeftCommand => Key.LeftMeta, KeyCode.RightCommand => Key.RightMeta,
                KeyCode.LeftWindows => Key.LeftWindows, KeyCode.RightWindows => Key.RightWindows,

                // Symbols
                KeyCode.BackQuote => Key.Backquote, KeyCode.Minus => Key.Minus, KeyCode.Equals => Key.Equals,
                KeyCode.LeftBracket => Key.LeftBracket, KeyCode.RightBracket => Key.RightBracket,
                KeyCode.Backslash => Key.Backslash, KeyCode.Semicolon => Key.Semicolon, KeyCode.Quote => Key.Quote,
                KeyCode.Comma => Key.Comma, KeyCode.Period => Key.Period, KeyCode.Slash => Key.Slash,

                // Other
                KeyCode.CapsLock => Key.CapsLock, KeyCode.Numlock => Key.NumLock,
                KeyCode.ScrollLock => Key.ScrollLock, KeyCode.Pause => Key.Pause,
                KeyCode.Print => Key.PrintScreen,

                _ => Key.None
            };
        }


        protected async UniTask ClickAny(params string[] searches)
        {
            await ClickAny(searches, throwIfMissing: true, seconds: 5);
        }

        protected async UniTask ClickAny(string[] searches, bool throwIfMissing = true, float seconds = 5)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying)
            {
                // Try each search pattern
                foreach (var searchPattern in searches)
                {
                    var b = await Find<IPointerClickHandler>(Search.ByAny(searchPattern), throwIfMissing: false, seconds: 0.1f);

                    if (b != null)
                    {
                        await SimulateClick(b);
                        await ActionComplete();
                        return;
                    }
                }

                await UniTask.Delay(100, true);
            }

            if (throwIfMissing)
            {
                throw new TestException($"ClickAny on '{string.Join(", ", searches)}' could not find any matching target within {seconds}s");
            }
        }

        private async UniTask SimulateClick(object target)
        {
            if (target is UnityEngine.Component component)
            {
                string path = GetHierarchyPath(component.transform);
                string textContent = "";
                if (TryGetComponentInChildren(component.gameObject, out TMP_Text tmpText) && tmpText != null)
                    textContent = tmpText.text;
                else if (TryGetComponentInChildren(component.gameObject, out Text uiText) && uiText != null)
                    textContent = uiText.text;

                Debug.Log($"[UITEST] CLICK executing - Name: '{component.name}' Path: '{path}' Text: '{textContent}'");

                // Get screen position of target
                Vector2 screenPosition = GetScreenPosition(component.gameObject);
                LogDebug($"SimulateClick: screen position ({screenPosition.x:F0}, {screenPosition.y:F0})");

                // Use Input System to inject mouse click
                await InjectPointerTap(screenPosition);
            }
        }

        /// <summary>
        /// Gets the screen position of a GameObject (works with both UI and world-space objects).
        /// </summary>
        private static Vector2 GetScreenPosition(GameObject go)
        {
            if (go.TryGetComponent<RectTransform>(out var rt))
            {
                // UI element - get center of rect in screen space
                Vector3[] corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                Vector3 center = (corners[0] + corners[2]) / 2f;

                // Find the canvas to determine if it's screen space or world space
                var canvas = go.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    return center;
                }
                else
                {
                    // World space or camera space canvas
                    Camera cam = canvas?.worldCamera ?? Camera.main;
                    return cam != null ? RectTransformUtility.WorldToScreenPoint(cam, center) : (Vector2)center;
                }
            }
            else
            {
                // World-space object
                Camera cam = Camera.main;
                return cam != null ? cam.WorldToScreenPoint(go.transform.position) : Vector2.zero;
            }
        }

        /// <summary>
        /// Injects a click/tap at the specified screen position.
        /// Uses touch on mobile (iOS/Android), mouse on desktop.
        /// </summary>
        private static async UniTask InjectPointerTap(Vector2 screenPosition)
        {
            if (ShouldUseTouchInput())
            {
                await InjectTouchTap(screenPosition);
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                Debug.LogWarning("[UITEST] Click - No mouse device found, cannot inject click");
                return;
            }

            // Use MouseState struct for complete state control
            var mouseState = new MouseState { position = screenPosition, delta = Vector2.zero };

            // Move mouse to position
            InputSystem.QueueStateEvent(mouse, mouseState);
            InputSystem.Update(); // Force event processing
            await UniTask.Yield();

            // Mouse button down
            mouseState = mouseState.WithButton(MouseButton.Left, true);
            InputSystem.QueueStateEvent(mouse, mouseState);
            InputSystem.Update(); // Force event processing
            await UniTask.Yield();

            // Mouse button up
            mouseState = mouseState.WithButton(MouseButton.Left, false);
            InputSystem.QueueStateEvent(mouse, mouseState);
            InputSystem.Update(); // Force event processing
            await UniTask.Yield();
        }

        /// <summary>
        /// Injects a single-finger tap gesture using the Input System.
        /// Used on mobile platforms (iOS/Android).
        /// </summary>
        private static async UniTask InjectTouchTap(Vector2 screenPosition)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                touchscreen = InputSystem.AddDevice<Touchscreen>();
                if (touchscreen == null)
                {
                    Debug.LogWarning("[UITEST] TouchTap - Could not create touchscreen device");
                    return;
                }
            }

            const int touchId = 1; // Touch IDs must be non-zero

            // Touch began
            using (StateEvent.From(touchscreen, out var beginPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId, beginPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(screenPosition, beginPtr);
                touchscreen.touches[0].delta.WriteValueIntoEvent(Vector2.zero, beginPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, beginPtr);
                InputSystem.QueueEvent(beginPtr);
            }
            InputSystem.Update(); // Force event processing

            await UniTask.Yield();

            // Touch ended (tap is just began + ended at same position)
            using (StateEvent.From(touchscreen, out var endPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId, endPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(screenPosition, endPtr);
                touchscreen.touches[0].delta.WriteValueIntoEvent(Vector2.zero, endPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(0f, endPtr);
                InputSystem.QueueEvent(endPtr);
            }
            InputSystem.Update(); // Force event processing

            await UniTask.Yield();
        }


        protected async UniTask Hold(Search search, float seconds, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] Hold ({searchTime}) for {seconds}s");

            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var b = await Find<IPointerDownHandler>(search, false, 0.5f);

                if (b != null && b is UnityEngine.Component c1)
                {
                    Vector2 screenPosition = GetScreenPosition(c1.gameObject);
                    await InjectPointerHold(screenPosition, seconds);
                    await ActionComplete();
                    return;
                }

                await UniTask.Delay(100, true);
            }

            if (throwIfMissing)
            {
                throw new TestException($"Hold could not find any matching target within {searchTime}s");
            }
        }

        /// <summary>
        /// Holds on a component directly for the specified duration.
        /// </summary>
        protected async UniTask Hold(Component component, float seconds)
        {
            if (component == null)
                throw new ArgumentNullException(nameof(component), "Cannot hold null component");

            var screenPos = GetScreenPosition(component.gameObject);
            Debug.Log($"[UITEST] Hold (Component) '{component.gameObject.name}' for {seconds}s at ({screenPos.x:F0}, {screenPos.y:F0})");

            await InjectPointerHold(screenPos, seconds);
            await ActionComplete();
        }

        /// <summary>
        /// Injects a hold/long-press at the specified screen position.
        /// Uses touch on mobile (iOS/Android), mouse on desktop.
        /// </summary>
        private static async UniTask InjectPointerHold(Vector2 screenPosition, float holdSeconds)
        {
            if (ShouldUseTouchInput())
            {
                await InjectTouchHold(screenPosition, holdSeconds);
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                Debug.LogWarning("[UITEST] Hold - No mouse device found, cannot inject hold");
                return;
            }

            // Use MouseState struct for complete state control
            var mouseState = new MouseState { position = screenPosition, delta = Vector2.zero };

            // Move mouse to position
            InputSystem.QueueStateEvent(mouse, mouseState);
            await UniTask.Yield();

            // Mouse button down
            mouseState = mouseState.WithButton(MouseButton.Left, true);
            InputSystem.QueueStateEvent(mouse, mouseState);

            // Hold for specified duration
            await UniTask.Delay(TimeSpan.FromSeconds(holdSeconds), true);

            // Mouse button up
            mouseState = mouseState.WithButton(MouseButton.Left, false);
            InputSystem.QueueStateEvent(mouse, mouseState);
            await UniTask.Yield();
        }

        /// <summary>
        /// Injects a touch hold/long-press gesture using the Input System.
        /// Used on mobile platforms (iOS/Android).
        /// </summary>
        private static async UniTask InjectTouchHold(Vector2 screenPosition, float holdSeconds)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                touchscreen = InputSystem.AddDevice<Touchscreen>();
                if (touchscreen == null)
                {
                    Debug.LogWarning("[UITEST] TouchHold - Could not create touchscreen device");
                    return;
                }
            }

            const int touchId = 1; // Touch IDs must be non-zero

            // Touch began
            using (StateEvent.From(touchscreen, out var beginPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId, beginPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(screenPosition, beginPtr);
                touchscreen.touches[0].delta.WriteValueIntoEvent(Vector2.zero, beginPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, beginPtr);
                InputSystem.QueueEvent(beginPtr);
            }

            await UniTask.Yield();

            // Hold for specified duration (touch stays in Stationary phase)
            float elapsed = 0f;
            while (elapsed < holdSeconds)
            {
                using (StateEvent.From(touchscreen, out var stationaryPtr))
                {
                    touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId, stationaryPtr);
                    touchscreen.touches[0].position.WriteValueIntoEvent(screenPosition, stationaryPtr);
                    touchscreen.touches[0].delta.WriteValueIntoEvent(Vector2.zero, stationaryPtr);
                    touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Stationary, stationaryPtr);
                    touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, stationaryPtr);
                    InputSystem.QueueEvent(stationaryPtr);
                }

                await UniTask.Yield();
                elapsed += Time.deltaTime;
            }

            // Touch ended
            using (StateEvent.From(touchscreen, out var endPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId, endPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(screenPosition, endPtr);
                touchscreen.touches[0].delta.WriteValueIntoEvent(Vector2.zero, endPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(0f, endPtr);
                InputSystem.QueueEvent(endPtr);
            }

            await UniTask.Yield();
        }

        /// <summary>
        /// Clicks on a UI element matching the Search query.
        /// Supports implicit string conversion: Click("ButtonName") becomes Click(Search.ByText("ButtonName"))
        /// Use search.IncludeInactive() to find inactive GameObjects.
        /// Use search.IncludeDisabled() to find disabled/non-interactable components.
        /// Supports post-processing: First(), Last(), Skip(), OrderBy().
        /// </summary>
        protected async UniTask Click(Search search, bool throwIfMissing = true, float searchTime = 10, int repeat = 0, int index = 0)
        {
            do
            {
                string indexInfo = index > 0 ? $" index={index}" : "";
                Debug.Log($"[UITEST] Click (Search) timeout={searchTime}s{indexInfo}");
                LogDebug($"Click: using Search query with {searchTime}s timeout{indexInfo}");

                float startTime = Time.realtimeSinceStartup;
                int searchIterations = 0;
                var findMode = search.ShouldIncludeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;

                while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
                {
                    searchIterations++;

                    // Find all clickable objects and filter by Search query
                    // Use GroupBy to deduplicate by GameObject (multiple MonoBehaviours on same object)
                    var allClickables = GameObject.FindObjectsByType<MonoBehaviour>(findMode, FindObjectsSortMode.None)
                        .Where(b => b != null && b is IPointerClickHandler && CheckAvailability(b, search) && search.Matches(b.gameObject))
                        .GroupBy(b => b.gameObject)
                        .Select(g => g.First())
                        .ToList();

                    // Apply post-processing (ordering, skip, take) if specified
                    IEnumerable<MonoBehaviour> processed = allClickables;
                    if (search.HasPostProcessing)
                    {
                        processed = search.ApplyPostProcessing(allClickables.Select(b => b.gameObject))
                            .Select(go => go.GetComponent<MonoBehaviour>())
                            .Where(b => b != null && b is IPointerClickHandler);
                    }

                    var finalList = processed.ToList();
                    LogDebug($"Click: iteration {searchIterations}, found {allClickables.Count} matching clickables, {finalList.Count} after post-processing");

                    if (finalList.Count > index)
                    {
                        var target = finalList[index];
                        LogDebug($"Click: found target at index {index} after {searchIterations} iterations ({Time.realtimeSinceStartup - startTime:F2}s): {GetHierarchyPath(target.transform)}");
                        await SimulateClick(target);
                        await ActionComplete();
                        goto nextRepeat;
                    }

                    await UniTask.Delay(100, true);
                }

                LogDebug($"Click: target not found after {searchIterations} iterations ({Time.realtimeSinceStartup - startTime:F2}s)");

                if (throwIfMissing)
                {
                    string indexMsg = index > 0 ? $" at index {index}" : "";
                    throw new TestException($"Click (Search){indexMsg} could not find any matching target within {searchTime}s");
                }

                nextRepeat:
                repeat--;
            }
            while (repeat > 0);
        }

        private bool IsWildcardMatch(string subject, string wildcardPattern)
        {
            if (string.IsNullOrEmpty(subject) || string.IsNullOrWhiteSpace(wildcardPattern))
                return false;

            if (string.CompareOrdinal(subject, wildcardPattern) == 0)
                return true;

            // No wildcards - exact match (case insensitive)
            if (!wildcardPattern.Contains('*'))
            {
                return subject.Equals(wildcardPattern, StringComparison.CurrentCultureIgnoreCase);
            }

            // Convert glob pattern to regex-like matching
            // Split by * and match each part in sequence
            string[] parts = wildcardPattern.Split('*');
            int subjectIndex = 0;
            string subjectLower = subject.ToLowerInvariant();

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].ToLowerInvariant();

                if (string.IsNullOrEmpty(part))
                    continue;

                int foundIndex = subjectLower.IndexOf(part, subjectIndex, StringComparison.Ordinal);

                if (foundIndex < 0)
                    return false;

                // First part must match at start if pattern doesn't start with *
                if (i == 0 && !wildcardPattern.StartsWith("*") && foundIndex != 0)
                    return false;

                // Last part must match at end if pattern doesn't end with *
                if (i == parts.Length - 1 && !wildcardPattern.EndsWith("*"))
                {
                    if (foundIndex + part.Length != subjectLower.Length)
                        return false;
                }

                subjectIndex = foundIndex + part.Length;
            }

            return true;
        }

        protected async UniTask Click(bool throwIfMissing = true)
        {
            await ClickAt(0.5f, 0.5f);
        }

        /// <summary>
        /// Clicks on a component directly (uses its screen position).
        /// Useful for iterating over FindAll results.
        /// </summary>
        /// <example>
        /// foreach (var btn in await FindAll&lt;Button&gt;(Search.ByName("Tab*")))
        /// {
        ///     await Click(btn);
        /// }
        /// </example>
        protected async UniTask Click(Component component)
        {
            if (component == null)
                throw new ArgumentNullException(nameof(component), "Cannot click null component");

            var screenPos = GetScreenPosition(component.gameObject);
            Debug.Log($"[UITEST] Click (Component) '{component.gameObject.name}' at ({screenPos.x:F0}, {screenPos.y:F0})");

            await InjectPointerTap(screenPos);
            await ActionComplete();
        }

        /// <summary>
        /// Clicks at a screen position specified as percentages (0-1).
        /// </summary>
        /// <param name="xPercent">X position as percentage of screen width (0 = left, 1 = right)</param>
        /// <param name="yPercent">Y position as percentage of screen height (0 = bottom, 1 = top)</param>
        protected async UniTask ClickAt(float xPercent, float yPercent)
        {
            Vector2 pos = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            Debug.Log($"[UITEST] ClickAt ({xPercent:P0}, {yPercent:P0}) at ({pos.x:F0}, {pos.y:F0})");

            await InjectPointerTap(pos);
            await ActionComplete();
        }

        /// <summary>
        /// Double-clicks at a screen position specified as percentages (0-1).
        /// </summary>
        /// <param name="xPercent">X position as percentage of screen width (0 = left, 1 = right)</param>
        /// <param name="yPercent">Y position as percentage of screen height (0 = bottom, 1 = top)</param>
        protected async UniTask DoubleClickAt(float xPercent, float yPercent)
        {
            Vector2 pos = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            Debug.Log($"[UITEST] DoubleClickAt ({xPercent:P0}, {yPercent:P0}) at ({pos.x:F0}, {pos.y:F0})");

            await InjectMouseDoubleClick(pos);
            await ActionComplete();
        }

        /// <summary>
        /// Double-clicks on a UI element.
        /// </summary>
        /// <param name="search">Search query for the element</param>
        /// <param name="throwIfMissing">Whether to throw if element not found</param>
        /// <param name="searchTime">Timeout for finding the element</param>
        protected async UniTask DoubleClick(Search search, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] DoubleClick");

            var target = await Find<IPointerClickHandler>(search, throwIfMissing, searchTime);
            if (target == null) return;

            if (target is UnityEngine.Component c)
            {
                Vector2 screenPosition = GetScreenPosition(c.gameObject);
                await InjectMouseDoubleClick(screenPosition);
                await ActionComplete();
            }
        }

        /// <summary>
        /// Double-clicks at screen center.
        /// </summary>
        protected async UniTask DoubleClick()
        {
            Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
            Debug.Log($"[UITEST] DoubleClick (screen center) at ({screenCenter.x:F0}, {screenCenter.y:F0})");

            await InjectMouseDoubleClick(screenCenter);
            await ActionComplete();
        }

        /// <summary>
        /// Double-clicks on a component directly.
        /// </summary>
        protected async UniTask DoubleClick(Component component)
        {
            if (component == null)
                throw new ArgumentNullException(nameof(component), "Cannot double-click null component");

            var screenPos = GetScreenPosition(component.gameObject);
            Debug.Log($"[UITEST] DoubleClick (Component) '{component.gameObject.name}' at ({screenPos.x:F0}, {screenPos.y:F0})");

            await InjectMouseDoubleClick(screenPos);
            await ActionComplete();
        }

        /// <summary>
        /// Injects a mouse double-click at the specified screen position.
        /// </summary>
        private static async UniTask InjectMouseDoubleClick(Vector2 screenPosition)
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                Debug.LogWarning("[UITEST] DoubleClick - No mouse device found");
                return;
            }

            // First click
            await InjectPointerTap(screenPosition);

            // Short delay between clicks (typical double-click threshold is ~500ms)
            await UniTask.Delay(50, true);

            // Second click
            await InjectPointerTap(screenPosition);
        }

        /// <summary>
        /// Scrolls the mouse wheel at the specified element or screen center.
        /// </summary>
        /// <param name="search">Search query for the element to scroll on</param>
        /// <param name="delta">Scroll delta (positive = up, negative = down)</param>
        /// <param name="throwIfMissing">Whether to throw if element not found</param>
        /// <param name="searchTime">Timeout for finding the element</param>
        protected async UniTask Scroll(Search search, float delta, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] Scroll delta={delta}");

            var target = await Find<RectTransform>(search, throwIfMissing, searchTime);
            if (target == null) return;

            Vector2 screenPos = GetScreenPosition(target.gameObject);
            await InjectMouseScroll(screenPos, delta);
            await ActionComplete();
        }

        /// <summary>
        /// Scrolls the mouse wheel at screen center.
        /// </summary>
        /// <param name="delta">Scroll delta (positive = up, negative = down)</param>
        protected async UniTask Scroll(float delta)
        {
            Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
            Debug.Log($"[UITEST] Scroll (screen center) delta={delta}");

            await InjectMouseScroll(screenCenter, delta);
            await ActionComplete();
        }

        /// <summary>
        /// Scrolls the mouse wheel at a screen position specified as percentages (0-1).
        /// </summary>
        /// <param name="xPercent">X position as percentage of screen width (0 = left, 1 = right)</param>
        /// <param name="yPercent">Y position as percentage of screen height (0 = bottom, 1 = top)</param>
        /// <param name="delta">Scroll delta (positive = up, negative = down)</param>
        protected async UniTask ScrollAt(float xPercent, float yPercent, float delta)
        {
            Vector2 pos = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            Debug.Log($"[UITEST] ScrollAt ({xPercent:P0}, {yPercent:P0}) delta={delta} at ({pos.x:F0}, {pos.y:F0})");

            await InjectMouseScroll(pos, delta);
            await ActionComplete();
        }

        /// <summary>
        /// Injects a mouse scroll event at the specified position.
        /// </summary>
        private static async UniTask InjectMouseScroll(Vector2 screenPosition, float delta)
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                Debug.LogWarning("[UITEST] Scroll - No mouse device found");
                return;
            }

            // Move mouse to position first
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, posPtr);
                InputSystem.QueueEvent(posPtr);
            }
            InputSystem.Update(); // Force event processing

            await UniTask.Yield();

            // Send scroll event
            using (StateEvent.From(mouse, out var scrollPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, scrollPtr);
                mouse.scroll.WriteValueIntoEvent(new Vector2(0, delta * 120), scrollPtr); // 120 is standard scroll delta unit
                InputSystem.QueueEvent(scrollPtr);
            }
            InputSystem.Update(); // Force event processing

            await UniTask.Yield();
        }

        /// <summary>
        /// Swipes on an element in the specified direction.
        /// </summary>
        /// <param name="search">Search query for the element</param>
        /// <param name="direction">Swipe direction (Left, Right, Up, Down)</param>
        /// <param name="distance">Swipe distance as percentage of screen height (0.2 = 20%)</param>
        /// <param name="duration">Duration of the swipe</param>
        /// <param name="throwIfMissing">Whether to throw if element not found</param>
        /// <param name="searchTime">Timeout for finding the element</param>
        protected async UniTask Swipe(Search search, SwipeDirection direction, float distance = 0.2f, float duration = 0.3f, bool throwIfMissing = true, float searchTime = 10)
        {
            float distancePixels = distance * Screen.height;
            Vector2 delta = direction switch
            {
                SwipeDirection.Left => new Vector2(-distancePixels, 0),
                SwipeDirection.Right => new Vector2(distancePixels, 0),
                SwipeDirection.Up => new Vector2(0, distancePixels),
                SwipeDirection.Down => new Vector2(0, -distancePixels),
                _ => Vector2.zero
            };

            Debug.Log($"[UITEST] Swipe {direction} duration={duration}s distance={distance:P0} ({distancePixels:F0}px)");
            LogDebug($"Swipe: delta=({delta.x:F0}, {delta.y:F0}), Screen.height={Screen.height}");

            await Drag(search, delta, duration, throwIfMissing, searchTime);
        }

        /// <summary>
        /// Swipes at screen center in the specified direction.
        /// </summary>
        /// <param name="direction">Swipe direction (Left, Right, Up, Down)</param>
        /// <param name="distance">Swipe distance as percentage of screen height (0.2 = 20%)</param>
        /// <param name="duration">Duration of the swipe</param>
        protected async UniTask Swipe(SwipeDirection direction, float distance = 0.2f, float duration = 0.3f)
        {
            await SwipeAt(0.5f, 0.5f, direction, distance, duration);
        }

        /// <summary>
        /// Swipes on a component in the specified direction.
        /// </summary>
        protected async UniTask Swipe(Component component, SwipeDirection direction, float distance = 0.2f, float duration = 0.3f)
        {
            if (component == null)
                throw new ArgumentNullException(nameof(component), "Cannot swipe null component");

            float distancePixels = distance * Screen.height;
            Vector2 delta = direction switch
            {
                SwipeDirection.Left => new Vector2(-distancePixels, 0),
                SwipeDirection.Right => new Vector2(distancePixels, 0),
                SwipeDirection.Up => new Vector2(0, distancePixels),
                SwipeDirection.Down => new Vector2(0, -distancePixels),
                _ => Vector2.zero
            };

            Debug.Log($"[UITEST] Swipe (Component) '{component.gameObject.name}' {direction}");
            await Drag(component, delta, duration);
        }

        /// <summary>
        /// Swipes at a screen position specified as percentages (0-1).
        /// </summary>
        /// <param name="xPercent">X position as percentage of screen width (0 = left, 1 = right)</param>
        /// <param name="yPercent">Y position as percentage of screen height (0 = bottom, 1 = top)</param>
        /// <param name="direction">Swipe direction</param>
        /// <param name="distance">Swipe distance as percentage of screen height (0.2 = 20%)</param>
        /// <param name="duration">Duration of the swipe</param>
        protected async UniTask SwipeAt(float xPercent, float yPercent, SwipeDirection direction, float distance = 0.2f, float duration = 0.3f)
        {
            float distancePixels = distance * Screen.height;
            Vector2 delta = direction switch
            {
                SwipeDirection.Left => new Vector2(-distancePixels, 0),
                SwipeDirection.Right => new Vector2(distancePixels, 0),
                SwipeDirection.Up => new Vector2(0, distancePixels),
                SwipeDirection.Down => new Vector2(0, -distancePixels),
                _ => Vector2.zero
            };

            Debug.Log($"[UITEST] SwipeAt ({xPercent:P0}, {yPercent:P0}) {direction} distance={distance:P0} ({distancePixels:F0}px)");

            await DragAt(xPercent, yPercent, delta, duration);
        }

        /// <summary>
        /// Performs a pinch gesture on an element.
        /// </summary>
        /// <param name="search">Search query for the element</param>
        /// <param name="scale">Scale factor: &gt;1 = zoom in (fingers spread), &lt;1 = zoom out (fingers pinch)</param>
        /// <param name="duration">Duration of the gesture</param>
        /// <param name="fingerDistance">Starting distance of each finger from center as percentage of screen height (0.05 = 5%)</param>
        /// <param name="throwIfMissing">Whether to throw if element not found</param>
        /// <param name="searchTime">Timeout for finding the element</param>
        protected async UniTask Pinch(Search search, float scale, float duration = 0.5f, float fingerDistance = 0.05f, bool throwIfMissing = true, float searchTime = 10)
        {
            float distancePixels = fingerDistance * Screen.height;
            Debug.Log($"[UITEST] Pinch scale={scale} duration={duration}s fingerDistance={fingerDistance:P0} ({distancePixels:F0}px)");

            var target = await Find<Transform>(search, throwIfMissing, searchTime);
            if (target == null) return;

            Vector2 center = GetScreenPosition(target.gameObject);
            await PinchAt(center, scale, duration, distancePixels);
        }

        /// <summary>
        /// Performs a pinch gesture at screen center.
        /// </summary>
        /// <param name="scale">Scale factor: &gt;1 = zoom in (fingers spread), &lt;1 = zoom out (fingers pinch)</param>
        /// <param name="duration">Duration of the gesture</param>
        /// <param name="fingerDistance">Starting distance of each finger from center as percentage of screen height (0.05 = 5%)</param>
        protected async UniTask Pinch(float scale, float duration = 0.5f, float fingerDistance = 0.05f)
        {
            await PinchAt(0.5f, 0.5f, scale, duration, fingerDistance);
        }

        /// <summary>
        /// Performs a pinch gesture on a component.
        /// </summary>
        protected async UniTask Pinch(Component component, float scale, float duration = 0.5f, float fingerDistance = 0.05f)
        {
            if (component == null)
                throw new ArgumentNullException(nameof(component), "Cannot pinch null component");

            Vector2 center = GetScreenPosition(component.gameObject);
            float distancePixels = fingerDistance * Screen.height;
            Debug.Log($"[UITEST] Pinch (Component) '{component.gameObject.name}' scale={scale}");

            await PinchAt(center, scale, duration, distancePixels);
        }

        /// <summary>
        /// Performs a pinch gesture at a screen position specified as percentages (0-1).
        /// </summary>
        /// <param name="xPercent">X position as percentage of screen width (0 = left, 1 = right)</param>
        /// <param name="yPercent">Y position as percentage of screen height (0 = bottom, 1 = top)</param>
        /// <param name="scale">Scale factor: &gt;1 = zoom in (fingers spread), &lt;1 = zoom out (fingers pinch)</param>
        /// <param name="duration">Duration of the gesture</param>
        /// <param name="fingerDistance">Starting distance of each finger from center as percentage of screen height (0.05 = 5%)</param>
        protected async UniTask PinchAt(float xPercent, float yPercent, float scale, float duration = 0.5f, float fingerDistance = 0.05f)
        {
            Vector2 center = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            float distancePixels = fingerDistance * Screen.height;
            Debug.Log($"[UITEST] PinchAt ({xPercent:P0}, {yPercent:P0}) scale={scale} fingerDistance={fingerDistance:P0} ({distancePixels:F0}px)");

            await PinchAt(center, scale, duration, distancePixels);
        }

        /// <summary>
        /// Performs a pinch gesture at the specified screen position.
        /// </summary>
        private async UniTask PinchAt(Vector2 center, float scale, float duration, float fingerDistancePixels)
        {
            float endDistance = fingerDistancePixels * scale;

            Vector2 finger1Start = center + new Vector2(-fingerDistancePixels, 0);
            Vector2 finger2Start = center + new Vector2(fingerDistancePixels, 0);
            Vector2 finger1End = center + new Vector2(-endDistance, 0);
            Vector2 finger2End = center + new Vector2(endDistance, 0);

            await InjectTwoFingerGesture(finger1Start, finger1End, finger2Start, finger2End, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Performs a two-finger swipe gesture on an element.
        /// </summary>
        /// <param name="search">Search query for the element</param>
        /// <param name="direction">Swipe direction</param>
        /// <param name="distance">Swipe distance as percentage of screen height (0.2 = 20%)</param>
        /// <param name="duration">Duration of the gesture</param>
        /// <param name="fingerSpacing">Distance between the two fingers as percentage of screen height (0.03 = 3%)</param>
        /// <param name="throwIfMissing">Whether to throw if element not found</param>
        /// <param name="searchTime">Timeout for finding the element</param>
        protected async UniTask TwoFingerSwipe(Search search, SwipeDirection direction, float distance = 0.2f, float duration = 0.3f, float fingerSpacing = 0.03f, bool throwIfMissing = true, float searchTime = 10)
        {
            float distancePixels = distance * Screen.height;
            float spacingPixels = fingerSpacing * Screen.height;
            Debug.Log($"[UITEST] TwoFingerSwipe {direction} duration={duration}s distance={distance:P0} ({distancePixels:F0}px) spacing={fingerSpacing:P0} ({spacingPixels:F0}px)");

            var target = await Find<Transform>(search, throwIfMissing, searchTime);
            if (target == null)
            {
                Debug.LogWarning($"[UITEST] TwoFingerSwipe - target not found");
                return;
            }

            Vector2 center = GetScreenPosition(target.gameObject);
            await TwoFingerSwipeAt(center, direction, distancePixels, duration, spacingPixels);
        }

        /// <summary>
        /// Performs a two-finger swipe gesture at screen center.
        /// </summary>
        /// <param name="direction">Swipe direction</param>
        /// <param name="distance">Swipe distance as percentage of screen height (0.2 = 20%)</param>
        /// <param name="duration">Duration of the gesture</param>
        /// <param name="fingerSpacing">Distance between the two fingers as percentage of screen height (0.03 = 3%)</param>
        protected async UniTask TwoFingerSwipe(SwipeDirection direction, float distance = 0.2f, float duration = 0.3f, float fingerSpacing = 0.03f)
        {
            await TwoFingerSwipeAt(0.5f, 0.5f, direction, distance, duration, fingerSpacing);
        }

        /// <summary>
        /// Performs a two-finger swipe gesture on a component.
        /// </summary>
        protected async UniTask TwoFingerSwipe(Component component, SwipeDirection direction, float distance = 0.2f, float duration = 0.3f, float fingerSpacing = 0.03f)
        {
            if (component == null)
                throw new ArgumentNullException(nameof(component), "Cannot two-finger swipe null component");

            float distancePixels = distance * Screen.height;
            float spacingPixels = fingerSpacing * Screen.height;
            Debug.Log($"[UITEST] TwoFingerSwipe (Component) '{component.gameObject.name}' {direction}");

            Vector2 center = GetScreenPosition(component.gameObject);
            await TwoFingerSwipeAt(center, direction, distancePixels, duration, spacingPixels);
        }

        /// <summary>
        /// Performs a two-finger swipe gesture at a screen position specified as percentages (0-1).
        /// </summary>
        /// <param name="xPercent">X position as percentage of screen width (0 = left, 1 = right)</param>
        /// <param name="yPercent">Y position as percentage of screen height (0 = bottom, 1 = top)</param>
        /// <param name="direction">Swipe direction</param>
        /// <param name="distance">Swipe distance as percentage of screen height (0.2 = 20%)</param>
        /// <param name="duration">Duration of the gesture</param>
        /// <param name="fingerSpacing">Distance between the two fingers as percentage of screen height (0.03 = 3%)</param>
        protected async UniTask TwoFingerSwipeAt(float xPercent, float yPercent, SwipeDirection direction, float distance = 0.2f, float duration = 0.3f, float fingerSpacing = 0.03f)
        {
            Vector2 center = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            float distancePixels = distance * Screen.height;
            float spacingPixels = fingerSpacing * Screen.height;
            Debug.Log($"[UITEST] TwoFingerSwipeAt ({xPercent:P0}, {yPercent:P0}) {direction} distance={distance:P0} ({distancePixels:F0}px)");

            await TwoFingerSwipeAt(center, direction, distancePixels, duration, spacingPixels);
        }

        /// <summary>
        /// Performs a two-finger swipe at the specified screen position.
        /// </summary>
        private async UniTask TwoFingerSwipeAt(Vector2 center, SwipeDirection direction, float distancePixels, float duration, float spacingPixels)
        {
            Vector2 delta = direction switch
            {
                SwipeDirection.Left => new Vector2(-distancePixels, 0),
                SwipeDirection.Right => new Vector2(distancePixels, 0),
                SwipeDirection.Up => new Vector2(0, distancePixels),
                SwipeDirection.Down => new Vector2(0, -distancePixels),
                _ => Vector2.zero
            };

            float halfSpacing = spacingPixels / 2f;
            Vector2 finger1Start = center + new Vector2(-halfSpacing, 0);
            Vector2 finger2Start = center + new Vector2(halfSpacing, 0);
            Vector2 finger1End = finger1Start + delta;
            Vector2 finger2End = finger2Start + delta;

            await InjectTwoFingerGesture(finger1Start, finger1End, finger2Start, finger2End, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Performs a two-finger rotation gesture on an element.
        /// </summary>
        /// <param name="search">Search query for the element</param>
        /// <param name="degrees">Rotation angle in degrees (positive = clockwise)</param>
        /// <param name="duration">Duration of the gesture</param>
        /// <param name="fingerDistance">Distance of each finger from center as percentage of screen height (0.05 = 5%)</param>
        /// <param name="throwIfMissing">Whether to throw if element not found</param>
        /// <param name="searchTime">Timeout for finding the element</param>
        protected async UniTask Rotate(Search search, float degrees, float duration = 0.5f, float fingerDistance = 0.05f, bool throwIfMissing = true, float searchTime = 10)
        {
            float radiusPixels = fingerDistance * Screen.height;
            Debug.Log($"[UITEST] Rotate {degrees} degrees duration={duration}s fingerDistance={fingerDistance:P0} ({radiusPixels:F0}px)");

            var target = await Find<Transform>(search, throwIfMissing, searchTime);
            if (target == null)
            {
                Debug.LogWarning($"[UITEST] Rotate - target not found");
                return;
            }

            var center = GetScreenPosition(target.gameObject);
            await RotateAt(center, degrees, duration, radiusPixels);
        }

        /// <summary>
        /// Performs a two-finger rotation gesture at the center of the screen.
        /// </summary>
        /// <param name="degrees">Rotation angle in degrees (positive = clockwise)</param>
        /// <param name="duration">Duration of the gesture</param>
        /// <param name="fingerDistance">Distance of each finger from center as percentage of screen height (0.05 = 5%)</param>
        protected async UniTask Rotate(float degrees, float duration = 0.5f, float fingerDistance = 0.05f)
        {
            await RotateAt(0.5f, 0.5f, degrees, duration, fingerDistance);
        }

        /// <summary>
        /// Performs a two-finger rotation gesture on a component.
        /// </summary>
        protected async UniTask Rotate(Component component, float degrees, float duration = 0.5f, float fingerDistance = 0.05f)
        {
            if (component == null)
                throw new ArgumentNullException(nameof(component), "Cannot rotate null component");

            float radiusPixels = fingerDistance * Screen.height;
            Debug.Log($"[UITEST] Rotate (Component) '{component.gameObject.name}' {degrees} degrees");

            var center = GetScreenPosition(component.gameObject);
            await RotateAt(center, degrees, duration, radiusPixels);
        }

        /// <summary>
        /// Performs a two-finger rotation gesture at a screen position specified as percentages (0-1).
        /// </summary>
        /// <param name="xPercent">X position as percentage of screen width (0 = left, 1 = right)</param>
        /// <param name="yPercent">Y position as percentage of screen height (0 = bottom, 1 = top)</param>
        /// <param name="degrees">Rotation angle in degrees (positive = clockwise)</param>
        /// <param name="duration">Duration of the gesture</param>
        /// <param name="fingerDistance">Distance of each finger from center as percentage of screen height (0.05 = 5%)</param>
        protected async UniTask RotateAt(float xPercent, float yPercent, float degrees, float duration = 0.5f, float fingerDistance = 0.05f)
        {
            var center = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            float radiusPixels = fingerDistance * Screen.height;
            Debug.Log($"[UITEST] RotateAt ({xPercent:P0}, {yPercent:P0}) {degrees} degrees fingerDistance={fingerDistance:P0} ({radiusPixels:F0}px)");

            await RotateAt(center, degrees, duration, radiusPixels);
        }

        /// <summary>
        /// Performs a rotation gesture at the specified screen position.
        /// </summary>
        private static async UniTask RotateAt(Vector2 center, float degrees, float duration, float radiusPixels)
        {
            float startAngle = 0f;
            float endAngle = degrees * Mathf.Deg2Rad;

            await InjectTwoFingerRotation(center, radiusPixels, startAngle, endAngle, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Injects a two-finger rotation gesture. Uses circular interpolation for smooth rotation.
        /// </summary>
        private static async UniTask InjectTwoFingerRotation(Vector2 center, float radius, float startAngle, float endAngle, float duration)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                touchscreen = InputSystem.AddDevice<Touchscreen>();
                if (touchscreen == null)
                {
                    Debug.LogWarning("[UITEST] Rotate - Could not create touchscreen device");
                    return;
                }
            }

            int steps = Mathf.Max(10, (int)(duration * 60));
            int delayPerStep = Mathf.Max(1, (int)(duration * 1000 / steps));

            // Calculate initial positions
            Vector2 finger1Start = center + new Vector2(Mathf.Cos(startAngle) * radius, Mathf.Sin(startAngle) * radius);
            Vector2 finger2Start = center + new Vector2(Mathf.Cos(startAngle + Mathf.PI) * radius, Mathf.Sin(startAngle + Mathf.PI) * radius);
            Vector2 prev1 = finger1Start;
            Vector2 prev2 = finger2Start;

            // Begin touches
            using (StateEvent.From(touchscreen, out var beginPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(1, beginPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(finger1Start, beginPtr);
                touchscreen.touches[0].delta.WriteValueIntoEvent(Vector2.zero, beginPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, beginPtr);

                touchscreen.touches[1].touchId.WriteValueIntoEvent(2, beginPtr);
                touchscreen.touches[1].position.WriteValueIntoEvent(finger2Start, beginPtr);
                touchscreen.touches[1].delta.WriteValueIntoEvent(Vector2.zero, beginPtr);
                touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);
                touchscreen.touches[1].pressure.WriteValueIntoEvent(1f, beginPtr);

                InputSystem.QueueEvent(beginPtr);
            }
            InputSystem.Update();

            await UniTask.Yield();

            // Move touches in a circular path
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                float currentAngle = Mathf.Lerp(startAngle, endAngle, t);

                Vector2 pos1 = center + new Vector2(Mathf.Cos(currentAngle) * radius, Mathf.Sin(currentAngle) * radius);
                Vector2 pos2 = center + new Vector2(Mathf.Cos(currentAngle + Mathf.PI) * radius, Mathf.Sin(currentAngle + Mathf.PI) * radius);
                Vector2 delta1 = pos1 - prev1;
                Vector2 delta2 = pos2 - prev2;

                using (StateEvent.From(touchscreen, out var movePtr))
                {
                    touchscreen.touches[0].touchId.WriteValueIntoEvent(1, movePtr);
                    touchscreen.touches[0].position.WriteValueIntoEvent(pos1, movePtr);
                    touchscreen.touches[0].delta.WriteValueIntoEvent(delta1, movePtr);
                    touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, movePtr);
                    touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, movePtr);

                    touchscreen.touches[1].touchId.WriteValueIntoEvent(2, movePtr);
                    touchscreen.touches[1].position.WriteValueIntoEvent(pos2, movePtr);
                    touchscreen.touches[1].delta.WriteValueIntoEvent(delta2, movePtr);
                    touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, movePtr);
                    touchscreen.touches[1].pressure.WriteValueIntoEvent(1f, movePtr);

                    InputSystem.QueueEvent(movePtr);
                }
                InputSystem.Update();

                prev1 = pos1;
                prev2 = pos2;
                await UniTask.Delay(delayPerStep, true);
            }

            // End touches
            Vector2 finger1End = center + new Vector2(Mathf.Cos(endAngle) * radius, Mathf.Sin(endAngle) * radius);
            Vector2 finger2End = center + new Vector2(Mathf.Cos(endAngle + Mathf.PI) * radius, Mathf.Sin(endAngle + Mathf.PI) * radius);

            using (StateEvent.From(touchscreen, out var endPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(1, endPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(finger1End, endPtr);
                touchscreen.touches[0].delta.WriteValueIntoEvent(Vector2.zero, endPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(0f, endPtr);

                touchscreen.touches[1].touchId.WriteValueIntoEvent(2, endPtr);
                touchscreen.touches[1].position.WriteValueIntoEvent(finger2End, endPtr);
                touchscreen.touches[1].delta.WriteValueIntoEvent(Vector2.zero, endPtr);
                touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);
                touchscreen.touches[1].pressure.WriteValueIntoEvent(0f, endPtr);

                InputSystem.QueueEvent(endPtr);
            }
            InputSystem.Update();

            await UniTask.Yield();
        }

        /// <summary>
        /// Injects a two-finger touch gesture using the Input System.
        /// </summary>
        private static async UniTask InjectTwoFingerGesture(Vector2 finger1Start, Vector2 finger1End, Vector2 finger2Start, Vector2 finger2End, float duration)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                // Add a touchscreen if none exists
                touchscreen = InputSystem.AddDevice<Touchscreen>();
                if (touchscreen == null)
                {
                    Debug.LogWarning("[UITEST] TwoFingerGesture - Could not create touchscreen device");
                    return;
                }
            }

            int steps = Mathf.Max(10, (int)(duration * 60));
            int delayPerStep = Mathf.Max(1, (int)(duration * 1000 / steps));
            Vector2 prev1 = finger1Start;
            Vector2 prev2 = finger2Start;

            // Begin touches
            using (StateEvent.From(touchscreen, out var beginPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(1, beginPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(finger1Start, beginPtr);
                touchscreen.touches[0].delta.WriteValueIntoEvent(Vector2.zero, beginPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, beginPtr);

                touchscreen.touches[1].touchId.WriteValueIntoEvent(2, beginPtr);
                touchscreen.touches[1].position.WriteValueIntoEvent(finger2Start, beginPtr);
                touchscreen.touches[1].delta.WriteValueIntoEvent(Vector2.zero, beginPtr);
                touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);
                touchscreen.touches[1].pressure.WriteValueIntoEvent(1f, beginPtr);

                InputSystem.QueueEvent(beginPtr);
            }
            InputSystem.Update();

            await UniTask.Yield();

            // Move touches
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                Vector2 pos1 = Vector2.Lerp(finger1Start, finger1End, t);
                Vector2 pos2 = Vector2.Lerp(finger2Start, finger2End, t);
                Vector2 delta1 = pos1 - prev1;
                Vector2 delta2 = pos2 - prev2;

                using (StateEvent.From(touchscreen, out var movePtr))
                {
                    touchscreen.touches[0].touchId.WriteValueIntoEvent(1, movePtr);
                    touchscreen.touches[0].position.WriteValueIntoEvent(pos1, movePtr);
                    touchscreen.touches[0].delta.WriteValueIntoEvent(delta1, movePtr);
                    touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, movePtr);
                    touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, movePtr);

                    touchscreen.touches[1].touchId.WriteValueIntoEvent(2, movePtr);
                    touchscreen.touches[1].position.WriteValueIntoEvent(pos2, movePtr);
                    touchscreen.touches[1].delta.WriteValueIntoEvent(delta2, movePtr);
                    touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, movePtr);
                    touchscreen.touches[1].pressure.WriteValueIntoEvent(1f, movePtr);

                    InputSystem.QueueEvent(movePtr);
                }
                InputSystem.Update();

                prev1 = pos1;
                prev2 = pos2;
                await UniTask.Delay(delayPerStep, true);
            }

            // End touches
            using (StateEvent.From(touchscreen, out var endPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(1, endPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(finger1End, endPtr);
                touchscreen.touches[0].delta.WriteValueIntoEvent(Vector2.zero, endPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(0f, endPtr);

                touchscreen.touches[1].touchId.WriteValueIntoEvent(2, endPtr);
                touchscreen.touches[1].position.WriteValueIntoEvent(finger2End, endPtr);
                touchscreen.touches[1].delta.WriteValueIntoEvent(Vector2.zero, endPtr);
                touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);
                touchscreen.touches[1].pressure.WriteValueIntoEvent(0f, endPtr);

                InputSystem.QueueEvent(endPtr);
            }
            InputSystem.Update();

            await UniTask.Yield();
        }

        protected async UniTask Drag(Vector2 direction, float duration = 0.5f)
        {
            Vector2 startPos = new Vector2(Screen.width / 2f, Screen.height / 2f);
            await DragFromTo(startPos, startPos + direction, duration);
        }

        /// <summary>
        /// Drags from a screen position specified as percentages (0-1).
        /// </summary>
        /// <param name="xPercent">X position as percentage of screen width (0 = left, 1 = right)</param>
        /// <param name="yPercent">Y position as percentage of screen height (0 = bottom, 1 = top)</param>
        /// <param name="direction">Drag direction in pixels</param>
        /// <param name="duration">Duration of the drag</param>
        protected async UniTask DragAt(float xPercent, float yPercent, Vector2 direction, float duration = 0.5f)
        {
            Vector2 startPos = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            Debug.Log($"[UITEST] DragAt ({xPercent:P0}, {yPercent:P0}) delta=({direction.x:F0},{direction.y:F0})");
            await DragFromTo(startPos, startPos + direction, duration);
        }

        protected async UniTask Drag(Search search, Vector2 direction, float duration = 0.5f, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] Drag ({duration}s) delta=({direction.x:F0},{direction.y:F0})");

            var target = await Find<RectTransform>(search, throwIfMissing, searchTime);
            if (target == null) return;

            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector2 center = (corners[0] + corners[2]) / 2f;
            Vector2 screenCenter = RectTransformUtility.WorldToScreenPoint(null, center);

            await DragFromTo(screenCenter, screenCenter + direction, duration);
        }

        /// <summary>
        /// Drags from a component in a direction.
        /// </summary>
        protected async UniTask Drag(Component component, Vector2 direction, float duration = 0.5f)
        {
            if (component == null)
                throw new ArgumentNullException(nameof(component), "Cannot drag null component");

            var screenPos = GetScreenPosition(component.gameObject);
            Debug.Log($"[UITEST] Drag (Component) '{component.gameObject.name}' delta=({direction.x:F0},{direction.y:F0})");

            await DragFromTo(screenPos, screenPos + direction, duration);
        }

        protected async UniTask DragFromTo(Vector2 startPos, Vector2 endPos, float duration = 0.5f)
        {
            Debug.Log($"[UITEST] DragFromTo ({duration}s) from ({startPos.x:F0},{startPos.y:F0}) to ({endPos.x:F0},{endPos.y:F0})");

            await InjectPointerDrag(startPos, endPos, duration);

            await ActionComplete();
        }

        /// <summary>
        /// Drags one element to another element (drag and drop).
        /// </summary>
        /// <param name="sourceSearch">Search query for the element to drag</param>
        /// <param name="targetSearch">Search query for the drop target</param>
        /// <param name="duration">Duration of the drag animation</param>
        /// <param name="throwIfMissing">Whether to throw if elements not found</param>
        /// <param name="searchTime">Timeout for finding the elements</param>
        protected async UniTask DragTo(Search sourceSearch, Search targetSearch, float duration = 0.5f, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] DragTo ({duration}s)");

            var source = await Find<RectTransform>(sourceSearch, throwIfMissing, searchTime);
            if (source == null) return;

            var target = await Find<RectTransform>(targetSearch, throwIfMissing, searchTime);
            if (target == null) return;

            Vector2 sourcePos = GetScreenPosition(source.gameObject);
            Vector2 targetPos = GetScreenPosition(target.gameObject);

            Debug.Log($"[UITEST] DragTo - dragging from ({sourcePos.x:F0},{sourcePos.y:F0}) to ({targetPos.x:F0},{targetPos.y:F0})");

            await InjectPointerDrag(sourcePos, targetPos, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Drags one component to another component (drag and drop).
        /// </summary>
        protected async UniTask DragTo(Component source, Component target, float duration = 0.5f)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source), "Cannot drag null source component");
            if (target == null)
                throw new ArgumentNullException(nameof(target), "Cannot drag to null target component");

            Vector2 sourcePos = GetScreenPosition(source.gameObject);
            Vector2 targetPos = GetScreenPosition(target.gameObject);

            Debug.Log($"[UITEST] DragTo (Component) '{source.gameObject.name}' -> '{target.gameObject.name}'");

            await InjectPointerDrag(sourcePos, targetPos, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Drags a component to a screen position specified as percentages (0-1).
        /// </summary>
        /// <param name="source">Component to drag</param>
        /// <param name="targetPercent">Target position as screen percentage (0,0 = bottom-left, 1,1 = top-right)</param>
        /// <param name="duration">Duration of the drag animation</param>
        protected async UniTask DragTo(Component source, Vector2 targetPercent, float duration = 0.5f)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source), "Cannot drag null source component");

            Vector2 sourcePos = GetScreenPosition(source.gameObject);
            Vector2 targetPos = new Vector2(targetPercent.x * Screen.width, targetPercent.y * Screen.height);

            Debug.Log($"[UITEST] DragTo (Component) '{source.gameObject.name}' -> ({targetPercent.x:P0}, {targetPercent.y:P0})");

            await InjectPointerDrag(sourcePos, targetPos, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Drags an element found by search to a screen position specified as percentages (0-1).
        /// </summary>
        /// <param name="sourceSearch">Search query for the element to drag</param>
        /// <param name="targetPercent">Target position as screen percentage (0,0 = bottom-left, 1,1 = top-right)</param>
        /// <param name="duration">Duration of the drag animation</param>
        /// <param name="throwIfMissing">Whether to throw if element not found</param>
        /// <param name="searchTime">Timeout for finding the element</param>
        protected async UniTask DragTo(Search sourceSearch, Vector2 targetPercent, float duration = 0.5f, bool throwIfMissing = true, float searchTime = 10)
        {
            var source = await Find<RectTransform>(sourceSearch, throwIfMissing, searchTime);
            if (source == null) return;

            Vector2 sourcePos = GetScreenPosition(source.gameObject);
            Vector2 targetPos = new Vector2(targetPercent.x * Screen.width, targetPercent.y * Screen.height);

            Debug.Log($"[UITEST] DragTo '{source.name}' -> ({targetPercent.x:P0}, {targetPercent.y:P0})");

            await InjectPointerDrag(sourcePos, targetPos, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Drags an element found by name to a screen position specified as percentages (0-1).
        /// </summary>
        protected async UniTask DragTo(string sourceName, Vector2 targetPercent, float duration = 0.5f, bool throwIfMissing = true, float searchTime = 10)
        {
            await DragTo(Search.ByName(sourceName), targetPercent, duration, throwIfMissing, searchTime);
        }

        /// <summary>
        /// Clicks on a slider at a percentage position (0-1) of its visible area.
        /// </summary>
        /// <param name="search">Search query for the slider</param>
        /// <param name="percent">Position to click as percentage (0 = left/bottom, 1 = right/top)</param>
        /// <param name="throwIfMissing">Whether to throw if slider not found</param>
        /// <param name="searchTime">Timeout for finding the slider</param>
        protected async UniTask ClickSlider(Search search, float percent, bool throwIfMissing = true, float searchTime = 10)
        {
            percent = Mathf.Clamp01(percent);
            Debug.Log($"[UITEST] ClickSlider ({searchTime}s) at {percent:P0}");

            var slider = await Find<Slider>(search, throwIfMissing, searchTime);
            if (slider == null) return;

            Vector2 clickPos = GetSliderPositionAtPercent(slider, percent);
            Debug.Log($"[UITEST] ClickSlider - clicking at ({clickPos.x:F0},{clickPos.y:F0})");

            await InjectPointerTap(clickPos);
            await ActionComplete();
        }

        /// <summary>
        /// Drags on a slider from one percentage position to another.
        /// </summary>
        /// <param name="search">Search query for the slider</param>
        /// <param name="fromPercent">Start position as percentage (0-1)</param>
        /// <param name="toPercent">End position as percentage (0-1)</param>
        /// <param name="throwIfMissing">Whether to throw if slider not found</param>
        /// <param name="searchTime">Timeout for finding the slider</param>
        /// <param name="duration">Duration of the drag animation</param>
        protected async UniTask DragSlider(Search search, float fromPercent, float toPercent, bool throwIfMissing = true, float searchTime = 10, float duration = 0.3f)
        {
            fromPercent = Mathf.Clamp01(fromPercent);
            toPercent = Mathf.Clamp01(toPercent);
            Debug.Log($"[UITEST] DragSlider ({searchTime}s) from {fromPercent:P0} to {toPercent:P0}");

            var slider = await Find<Slider>(search, throwIfMissing, searchTime);
            if (slider == null) return;

            Vector2 startPos = GetSliderPositionAtPercent(slider, fromPercent);
            Vector2 endPos = GetSliderPositionAtPercent(slider, toPercent);

            Debug.Log($"[UITEST] DragSlider - dragging from ({startPos.x:F0},{startPos.y:F0}) to ({endPos.x:F0},{endPos.y:F0})");

            await InjectPointerDrag(startPos, endPos, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Gets the screen position at a percentage along the slider's visible area.
        /// </summary>
        private static Vector2 GetSliderPositionAtPercent(Slider slider, float percent)
        {
            var sliderRT = slider.GetComponent<RectTransform>();

            Vector3[] corners = new Vector3[4];
            sliderRT.GetWorldCorners(corners);

            // corners: 0=bottom-left, 1=top-left, 2=top-right, 3=bottom-right
            Vector2 bottomLeft = corners[0];
            Vector2 topRight = corners[2];

            var canvas = slider.GetComponentInParent<Canvas>();
            Camera cam = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                cam = canvas.worldCamera ?? Camera.main;
            }

            // Determine direction - most sliders are horizontal left-to-right
            bool isHorizontal = slider.direction == Slider.Direction.LeftToRight ||
                               slider.direction == Slider.Direction.RightToLeft;
            bool isReversed = slider.direction == Slider.Direction.RightToLeft ||
                             slider.direction == Slider.Direction.TopToBottom;

            if (isReversed)
                percent = 1f - percent;

            Vector3 worldPos;
            if (isHorizontal)
            {
                float x = Mathf.Lerp(bottomLeft.x, topRight.x, percent);
                float y = (bottomLeft.y + topRight.y) / 2f;
                worldPos = new Vector3(x, y, 0);
            }
            else
            {
                float x = (bottomLeft.x + topRight.x) / 2f;
                float y = Mathf.Lerp(bottomLeft.y, topRight.y, percent);
                worldPos = new Vector3(x, y, 0);
            }

            return cam != null
                ? RectTransformUtility.WorldToScreenPoint(cam, worldPos)
                : (Vector2)worldPos;
        }

        /// <summary>
        /// Scrolls a ScrollRect using drag gestures until the target element becomes visible on screen, then returns it.
        /// Uses realistic input injection (drag gestures) - does not manipulate scroll position directly.
        /// </summary>
        /// <param name="scrollViewSearch">Search query for the ScrollRect</param>
        /// <param name="targetSearch">Search query for the target element inside the scroll view</param>
        /// <param name="maxScrollAttempts">Maximum number of scroll attempts before giving up</param>
        /// <param name="throwIfMissing">Whether to throw if elements not found</param>
        /// <param name="searchTime">Initial timeout for finding elements</param>
        /// <returns>The target GameObject once visible, or null if not found</returns>
        protected async UniTask<GameObject> ScrollTo(Search scrollViewSearch, Search targetSearch, int maxScrollAttempts = 20, bool throwIfMissing = true, float searchTime = 5)
        {
            Debug.Log($"[UITEST] ScrollTo - searching for scroll view and target");

            // Find the ScrollRect
            var scrollRect = await Find<ScrollRect>(scrollViewSearch, throwIfMissing, searchTime);
            if (scrollRect == null) return null;

            var viewport = scrollRect.viewport ?? scrollRect.GetComponent<RectTransform>();
            var content = scrollRect.content;

            if (content == null)
            {
                if (throwIfMissing)
                    throw new TestException("ScrollTo - ScrollRect has no content RectTransform assigned");
                return null;
            }

            // Get viewport bounds for visibility checks
            var canvas = scrollRect.GetComponentInParent<Canvas>();
            Camera cam = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = canvas.worldCamera ?? Camera.main;

            // Calculate scroll distances based on viewport size
            Vector3[] viewportCorners = new Vector3[4];
            viewport.GetWorldCorners(viewportCorners);

            // For ScreenSpaceOverlay, world corners ARE screen coordinates
            // For other render modes, convert using the camera
            Vector2 viewportMin, viewportMax;
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                viewportMin = viewportCorners[0];
                viewportMax = viewportCorners[2];
            }
            else
            {
                viewportMin = RectTransformUtility.WorldToScreenPoint(cam, viewportCorners[0]);
                viewportMax = RectTransformUtility.WorldToScreenPoint(cam, viewportCorners[2]);
            }

            float viewportHeight = Mathf.Abs(viewportMax.y - viewportMin.y);
            float viewportWidth = Mathf.Abs(viewportMax.x - viewportMin.x);
            // Use 40% of the smaller dimension to ensure drag stays within viewport bounds
            float scrollDistance = Mathf.Min(viewportHeight, viewportWidth) * 0.4f;

            Debug.Log($"[UITEST] ScrollTo - viewport: {viewport.name}, renderMode={canvas?.renderMode}, cam={cam}");
            Debug.Log($"[UITEST] ScrollTo - viewportMin={viewportMin}, viewportMax={viewportMax}, size=({viewportWidth}, {viewportHeight})");
            Debug.Log($"[UITEST] ScrollTo - Screen.width={Screen.width}, Screen.height={Screen.height}");

            Vector2 scrollCenter = (viewportMin + viewportMax) / 2f;
            Debug.Log($"[UITEST] ScrollTo - scrollCenter=({scrollCenter.x:F0},{scrollCenter.y:F0})");

            // Try to find target - first check if already visible
            var findMode = targetSearch.ShouldIncludeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
            int scrollAttempts = 0;

            while (scrollAttempts < maxScrollAttempts && Application.isPlaying)
            {
                await UniTask.Yield(); // Ensure layout is updated

                // Search for target element within the content
                var allTargets = GameObject.FindObjectsByType<MonoBehaviour>(findMode, FindObjectsSortMode.None)
                    .Where(b => b != null && targetSearch.Matches(b.gameObject) && IsDescendantOf(b.transform, content))
                    .Select(b => b.gameObject)
                    .Distinct()
                    .ToList();

                // Check if any target is visible in viewport
                foreach (var target in allTargets)
                {
                    if (IsVisibleInViewport(target, viewport, cam))
                    {
                        Debug.Log($"[UITEST] ScrollTo - found visible target: {GetHierarchyPath(target.transform)}");
                        return target;
                    }
                }

                Vector2 dragDirection = Vector2.zero;
                bool canScrollVertical = scrollRect.vertical;
                bool canScrollHorizontal = scrollRect.horizontal;

                if (allTargets.Count > 0)
                {
                    // Target exists but not visible - determine drag direction based on target position
                    var target = allTargets[0];
                    var targetRect = target.GetComponent<RectTransform>();

                    if (targetRect != null)
                    {
                        Vector3[] targetCorners = new Vector3[4];
                        targetRect.GetWorldCorners(targetCorners);

                        // Get current viewport corners (fresh read)
                        Vector3[] currentViewportCorners = new Vector3[4];
                        viewport.GetWorldCorners(currentViewportCorners);

                        float targetCenterY = (targetCorners[0].y + targetCorners[2].y) / 2f;
                        float viewportCenterY = (currentViewportCorners[0].y + currentViewportCorners[2].y) / 2f;
                        float targetCenterX = (targetCorners[0].x + targetCorners[2].x) / 2f;
                        float viewportCenterX = (currentViewportCorners[0].x + currentViewportCorners[2].x) / 2f;

                        if (canScrollVertical)
                        {
                            if (targetCenterY < viewportCenterY)
                            {
                                // Target is below viewport (lower Y) - drag UP to bring lower content into view
                                // (dragging up on screen scrolls content upward, revealing lower content)
                                dragDirection.y = scrollDistance;
                            }
                            else
                            {
                                // Target is above viewport (higher Y) - drag DOWN to bring upper content into view
                                dragDirection.y = -scrollDistance;
                            }
                        }

                        if (canScrollHorizontal)
                        {
                            if (targetCenterX > viewportCenterX)
                            {
                                // Target is to the right - drag left to scroll content right
                                dragDirection.x = -scrollDistance;
                            }
                            else
                            {
                                // Target is to the left - drag right to scroll content left
                                dragDirection.x = scrollDistance;
                            }
                        }

                        Debug.Log($"[UITEST] ScrollTo - target found but not visible, scroll attempt {scrollAttempts + 1}, drag=({dragDirection.x:F0},{dragDirection.y:F0})");
                    }
                }
                else
                {
                    // No targets found yet - do a sequential search by scrolling through content
                    Debug.Log($"[UITEST] ScrollTo - no targets found yet, scroll attempt {scrollAttempts + 1}");

                    // Scroll down/right to search through content
                    // Drag UP to bring lower content into view (scroll down through list)
                    if (canScrollVertical)
                    {
                        dragDirection.y = scrollDistance; // Drag UP to scroll down through content
                    }
                    if (canScrollHorizontal)
                    {
                        dragDirection.x = -scrollDistance; // Drag LEFT to scroll right through content
                    }
                }

                if (dragDirection == Vector2.zero)
                {
                    Debug.LogWarning("[UITEST] ScrollTo - ScrollRect has no scroll direction enabled");
                    break;
                }

                // Perform drag gesture using realistic input injection
                float posBefore = scrollRect.vertical ? scrollRect.verticalNormalizedPosition : scrollRect.horizontalNormalizedPosition;

                // Debug: Log what the raycast would hit at the drag start position
                var pointerData = new PointerEventData(EventSystem.current) { position = scrollCenter };
                var raycastResults = new List<RaycastResult>();
                EventSystem.current.RaycastAll(pointerData, raycastResults);
                Debug.Log($"[UITEST] ScrollTo - drag start ({scrollCenter.x:F0},{scrollCenter.y:F0}), raycast hits: {raycastResults.Count}");
                foreach (var hit in raycastResults.Take(3))
                {
                    Debug.Log($"[UITEST] ScrollTo - raycast hit: {hit.gameObject.name} at depth {hit.depth}");
                }

                await InjectPointerDrag(scrollCenter, scrollCenter + dragDirection, 0.15f);
                await UniTask.Delay(100, true); // Wait for scroll to settle
                float posAfter = scrollRect.vertical ? scrollRect.verticalNormalizedPosition : scrollRect.horizontalNormalizedPosition;
                Debug.Log($"[UITEST] ScrollTo - scroll position changed: {posBefore:F3} -> {posAfter:F3}");

                scrollAttempts++;
            }

            if (throwIfMissing)
                throw new TestException($"ScrollTo - Could not find visible target after {maxScrollAttempts} scroll attempts");

            return null;
        }

        /// <summary>
        /// Scrolls a ScrollRect until the target element becomes visible, then clicks on it.
        /// </summary>
        /// <param name="scrollViewSearch">Search query for the ScrollRect</param>
        /// <param name="targetSearch">Search query for the target element to click</param>
        /// <param name="maxScrollAttempts">Maximum number of scroll attempts</param>
        /// <param name="throwIfMissing">Whether to throw if not found</param>
        /// <param name="searchTime">Initial timeout for finding scroll view</param>
        protected async UniTask ScrollToAndClick(Search scrollViewSearch, Search targetSearch, int maxScrollAttempts = 20, bool throwIfMissing = true, float searchTime = 5)
        {
            var target = await ScrollTo(scrollViewSearch, targetSearch, maxScrollAttempts, throwIfMissing, searchTime);
            if (target != null)
            {
                Vector2 screenPos = GetScreenPosition(target);
                await InjectPointerTap(screenPos);
                await ActionComplete();
            }
        }

        /// <summary>
        /// Checks if a GameObject is a descendant of the given parent transform.
        /// </summary>
        private static bool IsDescendantOf(Transform child, Transform parent)
        {
            Transform current = child;
            while (current != null)
            {
                if (current == parent)
                    return true;
                current = current.parent;
            }
            return false;
        }

        /// <summary>
        /// Checks if a GameObject is visible within a viewport RectTransform.
        /// Uses RectTransformUtility for accurate visibility detection regardless of canvas mode.
        /// </summary>
        private static bool IsVisibleInViewport(GameObject go, RectTransform viewport, Camera cam)
        {
            var rect = go.GetComponent<RectTransform>();
            if (rect == null)
            {
                Debug.Log($"[UITEST] IsVisibleInViewport: {go.name} has no RectTransform");
                return false;
            }

            // Get element's world corners
            Vector3[] elementCorners = new Vector3[4];
            rect.GetWorldCorners(elementCorners);

            // Get viewport's world corners
            Vector3[] viewportCorners = new Vector3[4];
            viewport.GetWorldCorners(viewportCorners);

            // Calculate bounds in world space
            float elementMinX = Mathf.Min(elementCorners[0].x, elementCorners[1].x, elementCorners[2].x, elementCorners[3].x);
            float elementMaxX = Mathf.Max(elementCorners[0].x, elementCorners[1].x, elementCorners[2].x, elementCorners[3].x);
            float elementMinY = Mathf.Min(elementCorners[0].y, elementCorners[1].y, elementCorners[2].y, elementCorners[3].y);
            float elementMaxY = Mathf.Max(elementCorners[0].y, elementCorners[1].y, elementCorners[2].y, elementCorners[3].y);

            float viewportMinX = Mathf.Min(viewportCorners[0].x, viewportCorners[1].x, viewportCorners[2].x, viewportCorners[3].x);
            float viewportMaxX = Mathf.Max(viewportCorners[0].x, viewportCorners[1].x, viewportCorners[2].x, viewportCorners[3].x);
            float viewportMinY = Mathf.Min(viewportCorners[0].y, viewportCorners[1].y, viewportCorners[2].y, viewportCorners[3].y);
            float viewportMaxY = Mathf.Max(viewportCorners[0].y, viewportCorners[1].y, viewportCorners[2].y, viewportCorners[3].y);

            // Check for overlap (element is at least partially visible)
            // Using 20% visibility threshold - element must have at least 20% overlap
            float elementWidth = elementMaxX - elementMinX;
            float elementHeight = elementMaxY - elementMinY;

            float overlapX = Mathf.Max(0, Mathf.Min(elementMaxX, viewportMaxX) - Mathf.Max(elementMinX, viewportMinX));
            float overlapY = Mathf.Max(0, Mathf.Min(elementMaxY, viewportMaxY) - Mathf.Max(elementMinY, viewportMinY));

            float overlapArea = overlapX * overlapY;
            float elementArea = elementWidth * elementHeight;
            float overlapPercent = elementArea > 0 ? overlapArea / elementArea : 0;

            bool isVisible = overlapPercent >= 0.2f; // 20% visibility threshold

            Debug.Log($"[UITEST] IsVisibleInViewport: {go.name} element=({elementMinX:F1},{elementMinY:F1})-({elementMaxX:F1},{elementMaxY:F1}), viewport=({viewportMinX:F1},{viewportMinY:F1})-({viewportMaxX:F1},{viewportMaxY:F1}), overlap={overlapPercent:P0}, visible={isVisible}");

            return isVisible;
        }

        /// <summary>
        /// Legacy overload for compatibility - converts min/max to viewport rect check.
        /// </summary>
        private static bool IsVisibleInViewport(GameObject go, Vector2 viewportMin, Vector2 viewportMax, Camera cam)
        {
            var rect = go.GetComponent<RectTransform>();
            if (rect == null) return false;

            // Get element's world corners
            Vector3[] elementCorners = new Vector3[4];
            rect.GetWorldCorners(elementCorners);

            float elementMinX = Mathf.Min(elementCorners[0].x, elementCorners[1].x, elementCorners[2].x, elementCorners[3].x);
            float elementMaxX = Mathf.Max(elementCorners[0].x, elementCorners[1].x, elementCorners[2].x, elementCorners[3].x);
            float elementMinY = Mathf.Min(elementCorners[0].y, elementCorners[1].y, elementCorners[2].y, elementCorners[3].y);
            float elementMaxY = Mathf.Max(elementCorners[0].y, elementCorners[1].y, elementCorners[2].y, elementCorners[3].y);

            float elementWidth = elementMaxX - elementMinX;
            float elementHeight = elementMaxY - elementMinY;

            float overlapX = Mathf.Max(0, Mathf.Min(elementMaxX, viewportMax.x) - Mathf.Max(elementMinX, viewportMin.x));
            float overlapY = Mathf.Max(0, Mathf.Min(elementMaxY, viewportMax.y) - Mathf.Max(elementMinY, viewportMin.y));

            float overlapArea = overlapX * overlapY;
            float elementArea = elementWidth * elementHeight;
            float overlapPercent = elementArea > 0 ? overlapArea / elementArea : 0;

            bool isVisible = overlapPercent >= 0.2f;

            Debug.Log($"[UITEST] IsVisibleInViewport: {go.name} element=({elementMinX:F1},{elementMinY:F1})-({elementMaxX:F1},{elementMaxY:F1}), viewport=({viewportMin.x:F1},{viewportMin.y:F1})-({viewportMax.x:F1},{viewportMax.y:F1}), overlap={overlapPercent:P0}, visible={isVisible}");

            return isVisible;
        }

        /// <summary>
        /// Selects a dropdown option by index using actual clicks.
        /// Clicks the dropdown to open it, then clicks the option at the specified index.
        /// Supports both legacy Dropdown and TMP_Dropdown.
        /// </summary>
        /// <param name="search">Search query for the dropdown</param>
        /// <param name="optionIndex">Index of the option to select (0-based)</param>
        /// <param name="throwIfMissing">Whether to throw if dropdown not found</param>
        /// <param name="searchTime">Timeout for finding the dropdown</param>
        protected async UniTask ClickDropdown(Search search, int optionIndex, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] ClickDropdown ({searchTime}s) option index={optionIndex}");

            // Try to find legacy Dropdown first, then TMP_Dropdown
            var legacyDropdown = await Find<Dropdown>(search, false, searchTime / 2);
            var tmpDropdown = legacyDropdown == null ? await Find<TMP_Dropdown>(search, false, searchTime / 2) : null;

            GameObject dropdownGO = null;
            RectTransform template = null;

            if (legacyDropdown != null)
            {
                dropdownGO = legacyDropdown.gameObject;
                template = legacyDropdown.template;
            }
            else if (tmpDropdown != null)
            {
                dropdownGO = tmpDropdown.gameObject;
                template = tmpDropdown.template;
            }

            if (dropdownGO == null)
            {
                if (throwIfMissing)
                    throw new TestException($"ClickDropdown - Could not find Dropdown or TMP_Dropdown");
                return;
            }

            await ClickDropdownItem(dropdownGO, template, optionIndex);
        }

        /// <summary>
        /// Selects a dropdown option by label text using actual clicks.
        /// Supports both legacy Dropdown and TMP_Dropdown.
        /// </summary>
        /// <param name="search">Search query for the dropdown</param>
        /// <param name="optionLabel">The text label of the option to select</param>
        /// <param name="throwIfMissing">Whether to throw if dropdown not found</param>
        /// <param name="searchTime">Timeout for finding the dropdown</param>
        protected async UniTask ClickDropdown(Search search, string optionLabel, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] ClickDropdown ({searchTime}s) option='{optionLabel}'");

            // Try to find legacy Dropdown first, then TMP_Dropdown
            var legacyDropdown = await Find<Dropdown>(search, false, searchTime / 2);
            var tmpDropdown = legacyDropdown == null ? await Find<TMP_Dropdown>(search, false, searchTime / 2) : null;

            int optionIndex = -1;
            GameObject dropdownGO = null;
            RectTransform template = null;

            if (legacyDropdown != null)
            {
                dropdownGO = legacyDropdown.gameObject;
                template = legacyDropdown.template;
                for (int i = 0; i < legacyDropdown.options.Count; i++)
                {
                    if (legacyDropdown.options[i].text == optionLabel)
                    {
                        optionIndex = i;
                        break;
                    }
                }
            }
            else if (tmpDropdown != null)
            {
                dropdownGO = tmpDropdown.gameObject;
                template = tmpDropdown.template;
                for (int i = 0; i < tmpDropdown.options.Count; i++)
                {
                    if (tmpDropdown.options[i].text == optionLabel)
                    {
                        optionIndex = i;
                        break;
                    }
                }
            }
            else
            {
                if (throwIfMissing)
                    throw new TestException($"ClickDropdown - Could not find Dropdown or TMP_Dropdown '{search}'");
                return;
            }

            if (optionIndex < 0)
            {
                Debug.LogWarning($"[UITEST] ClickDropdown - Option '{optionLabel}' not found in dropdown");
                return;
            }

            // Use the already-found dropdown directly
            await ClickDropdownItem(dropdownGO, template, optionIndex);
        }

        /// <summary>
        /// Internal method to click a dropdown item after the dropdown has been found.
        /// </summary>
        private async UniTask ClickDropdownItem(GameObject dropdownGO, RectTransform template, int optionIndex)
        {
            // Capture existing toggles before opening dropdown
            var existingToggles = new HashSet<Toggle>(
                GameObject.FindObjectsByType<Toggle>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));

            // Click the dropdown to open it
            Vector2 dropdownPos = GetScreenPosition(dropdownGO);
            await InjectPointerTap(dropdownPos);

            // Wait for new toggles to appear (the dropdown items)
            Toggle[] newToggles = null;
            float waitTime = 0f;
            const float maxWaitTime = 0.5f;

            while (waitTime < maxWaitTime)
            {
                await UniTask.DelayFrame(1);

                var allToggles = GameObject.FindObjectsByType<Toggle>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

                // Find toggles that are new (created by opening the dropdown) and not part of the template
                newToggles = allToggles
                    .Where(t => !existingToggles.Contains(t))
                    .Where(t => t.gameObject.activeInHierarchy)
                    .Where(t => template == null || (!t.transform.IsChildOf(template) && t.transform != template))
                    .OrderBy(t => t.transform.GetSiblingIndex())
                    .ToArray();

                if (newToggles.Length > optionIndex)
                {
                    var targetToggle = newToggles[optionIndex];
                    Vector2 itemPos = GetScreenPosition(targetToggle.gameObject);
                    await InjectPointerTap(itemPos);
                    await ActionComplete();
                    return;
                }

                await UniTask.Delay(50, true);
                waitTime += 0.05f;
            }

            Debug.LogWarning($"[UITEST] ClickDropdown - Item at index {optionIndex} not found (found {newToggles?.Length ?? 0} new toggles)");
        }

        /// <summary>
        /// Returns true if we should use touch input instead of mouse.
        /// On mobile platforms or when no mouse is available but touchscreen is.
        /// </summary>
        private static bool ShouldUseTouchInput()
        {
#if UNITY_IOS || UNITY_ANDROID
            return true;
#else
            // On desktop, use mouse if available, otherwise fall back to touch
            return Mouse.current == null && Touchscreen.current != null;
#endif
        }

        /// <summary>
        /// Injects a drag gesture using the appropriate input method for the platform.
        /// Uses touch on mobile (iOS/Android), mouse on desktop.
        /// </summary>
        private static async UniTask InjectPointerDrag(Vector2 startPos, Vector2 endPos, float duration)
        {
            if (ShouldUseTouchInput())
            {
                await InjectTouchDrag(startPos, endPos, duration);
                return;
            }

            await InjectMouseDrag(startPos, endPos, duration);
        }

        /// <summary>
        /// Injects a mouse drag from start to end position using the Input System.
        /// Uses frame-based yields to ensure Unity processes events each frame (matching touch behavior).
        /// </summary>
        private static async UniTask InjectMouseDrag(Vector2 startPos, Vector2 endPos, float duration)
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                Debug.LogWarning("[UITEST] MouseDrag - No mouse device found, cannot inject drag");
                return;
            }

            Debug.Log($"[UITEST] MouseDrag - start=({startPos.x:F0},{startPos.y:F0}) end=({endPos.x:F0},{endPos.y:F0}) duration={duration}s");

            int totalFrames = Mathf.Max(5, Mathf.RoundToInt(duration * 60)); // ~60fps
            Vector2 previousPos = startPos;

            // Move mouse to start position
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(startPos, posPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, posPtr);
                InputSystem.QueueEvent(posPtr);
            }
            InputSystem.Update(); // Force event processing
            await UniTask.Yield();

            // Mouse button down at start
            using (StateEvent.From(mouse, out var downPtr))
            {
                mouse.position.WriteValueIntoEvent(startPos, downPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, downPtr);
                mouse.leftButton.WriteValueIntoEvent(1f, downPtr);
                InputSystem.QueueEvent(downPtr);
            }
            InputSystem.Update(); // Force event processing
            await UniTask.Yield(); // Allow PointerDown to register

            Debug.Log($"[UITEST] MouseDrag - mouse down at ({startPos.x:F0},{startPos.y:F0})");

            // Interpolate mouse position over duration with frame-based yields (like touch)
            for (int i = 1; i <= totalFrames; i++)
            {
                float t = (float)i / totalFrames;
                Vector2 currentPos = Vector2.Lerp(startPos, endPos, t);
                Vector2 delta = currentPos - previousPos;

                using (StateEvent.From(mouse, out var movePtr))
                {
                    mouse.position.WriteValueIntoEvent(currentPos, movePtr);
                    mouse.delta.WriteValueIntoEvent(delta, movePtr);
                    mouse.leftButton.WriteValueIntoEvent(1f, movePtr);
                    InputSystem.QueueEvent(movePtr);
                }
                InputSystem.Update(); // Force event processing each frame

                previousPos = currentPos;
                await UniTask.Yield(); // Frame-based to ensure event processing
            }

            // Mouse button up at end
            using (StateEvent.From(mouse, out var upPtr))
            {
                mouse.position.WriteValueIntoEvent(endPos, upPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, upPtr);
                mouse.leftButton.WriteValueIntoEvent(0f, upPtr);
                InputSystem.QueueEvent(upPtr);
            }
            InputSystem.Update(); // Force event processing

            Debug.Log($"[UITEST] MouseDrag - mouse up at ({endPos.x:F0},{endPos.y:F0})");

            await UniTask.Yield();
        }

        /// <summary>
        /// Injects a single-finger touch drag gesture using the Input System.
        /// Used on mobile platforms (iOS/Android).
        /// </summary>
        private static async UniTask InjectTouchDrag(Vector2 startPos, Vector2 endPos, float duration)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                touchscreen = InputSystem.AddDevice<Touchscreen>();
                if (touchscreen == null)
                {
                    Debug.LogWarning("[UITEST] TouchDrag - Could not create touchscreen device");
                    return;
                }
            }

            Debug.Log($"[UITEST] InjectTouchDrag - start=({startPos.x:F0},{startPos.y:F0}) end=({endPos.x:F0},{endPos.y:F0}) duration={duration}s");

            int totalFrames = Mathf.Max(5, Mathf.RoundToInt(duration * 60)); // ~60fps
            Vector2 previousPos = startPos;
            const int touchId = 1; // Touch IDs must be non-zero

            // Touch began at start position
            using (StateEvent.From(touchscreen, out var beginPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId, beginPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(startPos, beginPtr);
                touchscreen.touches[0].delta.WriteValueIntoEvent(Vector2.zero, beginPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, beginPtr);
                InputSystem.QueueEvent(beginPtr);
            }
            InputSystem.Update(); // Force event processing
            await UniTask.Yield();

            Debug.Log($"[UITEST] InjectTouchDrag - touch began at ({startPos.x:F0},{startPos.y:F0})");

            // Move touch through interpolated positions
            for (int i = 1; i <= totalFrames; i++)
            {
                float t = (float)i / totalFrames;
                Vector2 currentPos = Vector2.Lerp(startPos, endPos, t);
                Vector2 delta = currentPos - previousPos;

                using (StateEvent.From(touchscreen, out var movePtr))
                {
                    touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId, movePtr);
                    touchscreen.touches[0].position.WriteValueIntoEvent(currentPos, movePtr);
                    touchscreen.touches[0].delta.WriteValueIntoEvent(delta, movePtr);
                    touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, movePtr);
                    touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, movePtr);
                    InputSystem.QueueEvent(movePtr);
                }
                InputSystem.Update(); // Force event processing each frame

                previousPos = currentPos;
                await UniTask.Yield();
            }

            Debug.Log($"[UITEST] InjectTouchDrag - touch ended at ({endPos.x:F0},{endPos.y:F0})");

            // Touch ended at end position
            using (StateEvent.From(touchscreen, out var endPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId, endPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(endPos, endPtr);
                touchscreen.touches[0].delta.WriteValueIntoEvent(Vector2.zero, endPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(0f, endPtr);
                InputSystem.QueueEvent(endPtr);
            }
            InputSystem.Update(); // Force event processing

            await UniTask.Yield();
        }

        protected async UniTask SimulatePlay(int seconds = 20, params string[] targets)
        {

            Debug.Log($"[UITEST] SimulatePlay ({seconds}) [{string.Join(',', targets)}]");

            await UniTask.Delay(EffectiveInterval, true);

            var startTime = Time.realtimeSinceStartup;

            foreach (var t in targets)
            {
                SimulatePlayTarget(t, startTime, seconds).Forget();
            }

            await UniTask.Delay(TimeSpan.FromSeconds(seconds), true);

        }

        private async UniTaskVoid SimulatePlayTarget(string t, float startTime, int seconds)
        {
            var target = await Find<IPointerDownHandler>(Search.ByAny(t), throwIfMissing: true, seconds: seconds);
            if (target == null || !(target is UnityEngine.Component component))
                return;

            Vector2 screenPosition = GetScreenPosition(component.gameObject);

            while (Time.realtimeSinceStartup - startTime < seconds && Application.isPlaying)
            {
                // Random hold duration
                int holdDuration = UnityEngine.Random.Range(300, Mathf.Min(3000, seconds * 1000));
                float holdSeconds = holdDuration / 1000f;

                await InjectPointerHold(screenPosition, holdSeconds);

                await UniTask.Delay(UnityEngine.Random.Range(10, 100), true);
            }
        }



        protected async UniTask ClickAny(Search search, float seconds = 10, bool throwIfMissing = true)
        {
            Debug.Log($"[UITEST] ClickAny ({seconds})");

            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying)
            {
                try
                {
                    var list = await FindAll<IPointerClickHandler>(search, 0.5f);
                    var rnd = new System.Random((int)DateTime.Now.Millisecond);
                    var clicktargets = list.OrderBy(i => rnd.Next());

                    foreach (var item in clicktargets)
                    {
                        if (item != null)
                        {
                            await SimulateClick(item);
                            await ActionComplete();
                            return;
                        }
                    }
                }
                catch (TimeoutException) { }

                await UniTask.Delay(100, true);
            }

            if (throwIfMissing)
            {
                throw new TestException($"ClickAny could not find any matching target within {seconds}s");
            }
        }


        /// <summary>
        /// Finds a component by type. By default only active and enabled components are returned.
        /// </summary>
        /// <param name="throwIfMissing">Whether to throw if not found</param>
        /// <param name="seconds">Timeout in seconds</param>
        /// <param name="includeInactive">Include inactive GameObjects</param>
        /// <param name="includeDisabled">Include disabled/non-interactable components</param>
        protected async UniTask<T> Find<T>(bool throwIfMissing = true, float seconds = 10, bool includeInactive = false, bool includeDisabled = false)
            where T : MonoBehaviour
        {
            var startTime = Time.realtimeSinceStartup;
            var findMode = includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;

            while ((Time.realtimeSinceStartup - startTime) < seconds)
            {
                await UniTask.Delay(EffectiveInterval, true);

                var result = GameObject.FindAnyObjectByType<T>(findMode);

                if (result == null)
                    continue;

                // Check availability manually for the non-Search version
                if (!includeInactive && !result.gameObject.activeInHierarchy)
                    continue;

                if (!includeDisabled)
                {
                    if (!result.enabled)
                        continue;

                    var canvasGroup = result.GetComponentInParent<CanvasGroup>();
                    if (canvasGroup != null && (canvasGroup.alpha <= 0 || !canvasGroup.interactable))
                        continue;
                }

                return result;
            }

            if (throwIfMissing)
                throw new System.TimeoutException($"Unable to locate {typeof(T).Name} in {seconds} seconds");

            return default;
        }

        /// <summary>
        /// Finds a component matching the Search query.
        /// Supports implicit string conversion: Find&lt;Button&gt;("Play") becomes Find&lt;Button&gt;(Search.ByText("Play"))
        /// Use search.IncludeInactive() to find inactive GameObjects.
        /// Use search.IncludeDisabled() to find disabled/non-interactable components.
        /// </summary>
        protected async UniTask<T> Find<T>(Search search, bool throwIfMissing = true, float seconds = 10)
        {
            var startTime = Time.realtimeSinceStartup;
            LogDebug($"Find<{typeof(T).Name}>: using Search query with {seconds}s timeout, includeInactive={search.ShouldIncludeInactive}, includeDisabled={search.ShouldIncludeDisabled}");

            int iteration = 0;
            var findMode = search.ShouldIncludeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying && !TestCancellationToken.IsCancellationRequested)
            {
                iteration++;

                // When the search has a target transform (Parent/Child/Sibling), we need to search all GameObjects
                // because the source object may not have a MonoBehaviour (e.g., a Panel with only Image component)
                if (search.HasTargetTransform)
                {
                    var allTransforms = GameObject.FindObjectsByType<Transform>(findMode, FindObjectsSortMode.None);
                    var matchingGameObjects = new List<GameObject>();

                    foreach (var transform in allTransforms)
                    {
                        if (transform == null) continue;
                        var go = transform.gameObject;
                        if (!search.Matches(go)) continue;
                        matchingGameObjects.Add(go);
                    }

                    if (matchingGameObjects.Count > 0)
                    {
                        var transformedGos = search.ApplyPostProcessing(matchingGameObjects).ToList();
                        foreach (var go in transformedGos)
                        {
                            if (go == null) continue;

                            // Check availability on the transformed target
                            var anyComponent = go.GetComponent<Component>();
                            if (anyComponent != null && !CheckAvailability(anyComponent, search)) continue;

                            var component = go.GetComponent<T>();
                            if (component != null)
                            {
                                Debug.Log($"[UITEST] Find<{typeof(T).Name}> (Search with transform) found on iteration {iteration} after {(Time.realtimeSinceStartup - startTime) * 1000:F0}ms");
                                await ActionComplete();
                                return component;
                            }
                        }
                    }
                }
                else
                {
                    // Standard path: find MonoBehaviours that match and have component T
                    var allObjects = GameObject.FindObjectsByType<MonoBehaviour>(findMode, FindObjectsSortMode.None);
                    var matches = new List<(MonoBehaviour obj, T result)>();

                    // Track which GameObjects we've already processed to avoid duplicates
                    var processedGameObjects = new HashSet<GameObject>();

                    foreach (var obj in allObjects)
                    {
                        if (obj == null) continue;

                        // Skip if we've already processed this GameObject
                        if (!processedGameObjects.Add(obj.gameObject)) continue;

                        if (!CheckAvailability(obj, search)) continue;
                        if (!search.Matches(obj.gameObject)) continue;

                        // Check if this object or its components match type T
                        if (obj is T match)
                        {
                            matches.Add((obj, match));
                        }
                        else
                        {
                            var component = obj.GetComponent<T>();
                            if (component != null)
                                matches.Add((obj, component));
                        }
                    }

                    // Apply post-processing if specified (ordering, skip, take, etc.)
                    if (matches.Count > 0)
                    {
                        if (search.HasPostProcessing)
                        {
                            var orderedGos = search.ApplyPostProcessing(matches.Select(m => m.obj.gameObject)).ToList();
                            foreach (var go in orderedGos)
                            {
                                if (go == null) continue;

                                var component = go.GetComponent<T>();
                                if (component != null)
                                {
                                    Debug.Log($"[UITEST] Find<{typeof(T).Name}> (Search) found on iteration {iteration} after {(Time.realtimeSinceStartup - startTime) * 1000:F0}ms");
                                    await ActionComplete();
                                    return component;
                                }
                            }
                        }
                        else
                        {
                            var first = matches.FirstOrDefault();
                            if (first.result != null)
                            {
                                Debug.Log($"[UITEST] Find<{typeof(T).Name}> (Search) found on iteration {iteration} after {(Time.realtimeSinceStartup - startTime) * 1000:F0}ms");
                                await ActionComplete();
                                return first.result;
                            }
                        }
                    }
                }

                await UniTask.Delay(EffectiveInterval, true, PlayerLoopTiming.Update, TestCancellationToken);
            }

            TestCancellationToken.ThrowIfCancellationRequested();

            if (throwIfMissing)
                throw new TimeoutException($"Unable to locate {typeof(T).Name} matching Search query in {seconds} seconds after {iteration} iterations");

            return default;
        }


        /// <summary>
        /// Finds all components matching the Search query.
        /// Supports implicit string conversion.
        /// Use search.IncludeInactive() to find inactive GameObjects.
        /// Use search.IncludeDisabled() to find disabled/non-interactable components.
        /// Supports post-processing: First(), Last(), Skip(), OrderBy().
        /// </summary>
        protected async UniTask<IEnumerable<T>> FindAll<T>(Search search, float seconds = 10)
        {
            var startTime = Time.realtimeSinceStartup;
            var findMode = search.ShouldIncludeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;

            while (Application.isPlaying && !TestCancellationToken.IsCancellationRequested)
            {
                if ((Time.realtimeSinceStartup - startTime) > seconds)
                    break;

                // When the search has a target transform (Parent/Child/Sibling), we need to search all GameObjects
                // because the source object may not have a MonoBehaviour (e.g., a Panel with only Image component)
                if (search.HasTargetTransform)
                {
                    var allTransforms = GameObject.FindObjectsByType<Transform>(findMode, FindObjectsSortMode.None);
                    var matchingGameObjects = new List<GameObject>();

                    foreach (var transform in allTransforms)
                    {
                        if (transform == null) continue;
                        var go = transform.gameObject;
                        if (!search.Matches(go)) continue;
                        matchingGameObjects.Add(go);
                    }

                    if (matchingGameObjects.Count > 0)
                    {
                        var transformedGos = search.ApplyPostProcessing(matchingGameObjects).ToList();
                        return transformedGos
                            .Where(go => go != null)
                            .Select(go => go.GetComponent<T>())
                            .Where(c => c != null);
                    }
                }
                else
                {
                    // Standard path: find MonoBehaviours that match and have component T
                    var allObjects = GameObject.FindObjectsByType<MonoBehaviour>(findMode, FindObjectsSortMode.None);
                    var matches = new List<(MonoBehaviour obj, T result)>();

                    // Track which GameObjects we've already processed to avoid duplicates
                    var processedGameObjects = new HashSet<GameObject>();

                    foreach (var obj in allObjects)
                    {
                        if (obj == null) continue;

                        // Skip if we've already processed this GameObject
                        if (!processedGameObjects.Add(obj.gameObject)) continue;

                        if (!CheckAvailability(obj, search)) continue;
                        if (!search.Matches(obj.gameObject)) continue;

                        if (obj is T match)
                        {
                            matches.Add((obj, match));
                        }
                        else
                        {
                            var component = obj.GetComponent<T>();
                            if (component != null)
                                matches.Add((obj, component));
                        }
                    }

                    if (matches.Count > 0)
                    {
                        // Apply post-processing if specified (ordering, skip, take, etc.)
                        if (search.HasPostProcessing)
                        {
                            var orderedGos = search.ApplyPostProcessing(matches.Select(m => m.obj.gameObject)).ToList();
                            return orderedGos
                                .Where(go => go != null)
                                .Select(go => go.GetComponent<T>())
                                .Where(c => c != null);
                        }
                        return matches.Select(m => m.result);
                    }
                }

                await UniTask.Delay(EffectiveInterval, true, PlayerLoopTiming.Update, TestCancellationToken);
            }

            TestCancellationToken.ThrowIfCancellationRequested();
            return Enumerable.Empty<T>();
        }

        /// <summary>
        /// Represents a container with its child items for iteration.
        /// Use with foreach to iterate over items while having access to the container for ScrollTo.
        /// </summary>
        public class ItemContainer
        {
            public Component Container { get; }
            public ScrollRect ScrollRect => Container as ScrollRect;
            public IEnumerable<RectTransform> Items { get; }

            public ItemContainer(Component container, IEnumerable<RectTransform> items)
            {
                Container = container;
                Items = items;
            }

            public IEnumerator<(Component Container, RectTransform Item)> GetEnumerator()
            {
                foreach (var item in Items)
                {
                    yield return (Container, item);
                }
            }
        }

        /// <summary>
        /// Finds a container (ScrollRect, Dropdown, LayoutGroup) and its child items.
        /// Returns pairs of (Container, Item) for use with ScrollTo and Click.
        /// Supports: ScrollRect, TMP_Dropdown, Dropdown, LayoutGroup (Horizontal/Vertical/Grid).
        /// </summary>
        /// <param name="containerSearch">Search for the container</param>
        /// <param name="itemSearch">Optional additional search criteria for items</param>
        /// <example>
        /// // ScrollRect - scroll to each item
        /// foreach (var (list, item) in await FindItems("InventoryList"))
        /// {
        ///     await ScrollTo(list, item);
        ///     await Click(item);
        /// }
        ///
        /// // Dropdown options
        /// foreach (var (dropdown, option) in await FindItems("CategoryDropdown"))
        /// {
        ///     await Click(option);
        /// }
        /// </example>
        protected async UniTask<ItemContainer> FindItems(Search containerSearch, Search itemSearch = null)
        {
            // Try each supported container type in order of most common
            Component container = await Find<ScrollRect>(containerSearch, throwIfMissing: false, seconds: 2);
            container ??= await Find<VerticalLayoutGroup>(containerSearch, throwIfMissing: false, seconds: 1);
            container ??= await Find<HorizontalLayoutGroup>(containerSearch, throwIfMissing: false, seconds: 1);
            container ??= await Find<GridLayoutGroup>(containerSearch, throwIfMissing: false, seconds: 1);
            container ??= await Find<TMP_Dropdown>(containerSearch, throwIfMissing: false, seconds: 1);
            container ??= await Find<Dropdown>(containerSearch, throwIfMissing: false, seconds: 1);

            if (container == null)
                throw new TestException($"FindItems could not find a supported container (ScrollRect, Dropdown, LayoutGroup) matching: {containerSearch}");

            var items = GetContainerItems(container);

            if (itemSearch != null)
            {
                items = items.Where(item => itemSearch.Matches(item.gameObject));
            }

            return new ItemContainer(container, items);
        }

        /// <summary>
        /// Finds a container by name and its child items.
        /// </summary>
        protected async UniTask<ItemContainer> FindItems(string containerName, Search itemSearch = null)
        {
            return await FindItems(Search.ByName(containerName), itemSearch);
        }

        private IEnumerable<RectTransform> GetContainerItems(Component container)
        {
            switch (container)
            {
                case ScrollRect scrollRect:
                {
                    var content = scrollRect.content ?? scrollRect.GetComponent<RectTransform>();
                    var items = new List<RectTransform>();
                    foreach (Transform child in content)
                    {
                        var rect = child.GetComponent<RectTransform>();
                        if (rect != null && child.gameObject.activeInHierarchy)
                            items.Add(rect);
                    }
                    // Order by position
                    return items.OrderBy(item => scrollRect.vertical ? -item.anchoredPosition.y : item.anchoredPosition.x);
                }

                case TMP_Dropdown tmpDropdown:
                {
                    // Return option data as conceptual items - caller will use ClickDropdown
                    // For now, return the template items if dropdown is open
                    var template = tmpDropdown.template;
                    if (template != null && template.gameObject.activeInHierarchy)
                    {
                        var content = template.GetComponentInChildren<ToggleGroup>()?.transform ?? template;
                        return content.GetComponentsInChildren<RectTransform>()
                            .Where(r => r.GetComponent<Toggle>() != null)
                            .OrderBy(r => -r.anchoredPosition.y);
                    }
                    return Enumerable.Empty<RectTransform>();
                }

                case Dropdown dropdown:
                {
                    var template = dropdown.template;
                    if (template != null && template.gameObject.activeInHierarchy)
                    {
                        var content = template.GetComponentInChildren<ToggleGroup>()?.transform ?? template;
                        return content.GetComponentsInChildren<RectTransform>()
                            .Where(r => r.GetComponent<Toggle>() != null)
                            .OrderBy(r => -r.anchoredPosition.y);
                    }
                    return Enumerable.Empty<RectTransform>();
                }

                case HorizontalLayoutGroup hlg:
                {
                    var items = new List<RectTransform>();
                    foreach (Transform child in hlg.transform)
                    {
                        var rect = child.GetComponent<RectTransform>();
                        if (rect != null && child.gameObject.activeInHierarchy)
                            items.Add(rect);
                    }
                    return items.OrderBy(r => r.anchoredPosition.x);
                }

                case VerticalLayoutGroup vlg:
                {
                    var items = new List<RectTransform>();
                    foreach (Transform child in vlg.transform)
                    {
                        var rect = child.GetComponent<RectTransform>();
                        if (rect != null && child.gameObject.activeInHierarchy)
                            items.Add(rect);
                    }
                    return items.OrderBy(r => -r.anchoredPosition.y);
                }

                case GridLayoutGroup glg:
                {
                    var items = new List<RectTransform>();
                    foreach (Transform child in glg.transform)
                    {
                        var rect = child.GetComponent<RectTransform>();
                        if (rect != null && child.gameObject.activeInHierarchy)
                            items.Add(rect);
                    }
                    // Order top-to-bottom, left-to-right (reading order)
                    return items.OrderBy(r => -r.anchoredPosition.y).ThenBy(r => r.anchoredPosition.x);
                }

                default:
                    return Enumerable.Empty<RectTransform>();
            }
        }
    }
}
