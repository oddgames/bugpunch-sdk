using System;
using System.Collections.Generic;
using ODDGames.UITest.AI;
using UnityEngine;

namespace ODDGames.UITest.VisualBuilder
{
    /// <summary>
    /// Configuration for selecting a UI element at runtime using the Search API.
    /// Uses JSON-based SearchQuery for full chaining support and strict parameter typing.
    /// </summary>
    [Serializable]
    public class ElementSelector
    {
        /// <summary>
        /// The search query that defines how to find the element.
        /// Supports all Search API methods: text, name, type, adjacent, etc.
        /// with chainable filters like near, hasParent, first, etc.
        /// </summary>
        public SearchQuery query;

        /// <summary>Human-readable display name for the UI</summary>
        public string displayName;

        /// <summary>
        /// Creates a selector from a SearchQuery.
        /// </summary>
        public static ElementSelector FromQuery(SearchQuery query, string displayName = null)
        {
            return new ElementSelector
            {
                query = query,
                displayName = displayName
            };
        }

        /// <summary>
        /// Creates a selector by text pattern.
        /// </summary>
        public static ElementSelector ByText(string textPattern, string displayName = null)
        {
            return new ElementSelector
            {
                query = SearchQuery.Text(textPattern),
                displayName = displayName ?? $"\"{textPattern}\""
            };
        }

        /// <summary>
        /// Creates a selector by name pattern.
        /// </summary>
        public static ElementSelector ByName(string namePattern, string displayName = null)
        {
            return new ElementSelector
            {
                query = SearchQuery.Name(namePattern),
                displayName = displayName ?? namePattern
            };
        }

        /// <summary>
        /// Creates a selector by adjacent label.
        /// </summary>
        public static ElementSelector Adjacent(string labelText, string direction = "right", string displayName = null)
        {
            return new ElementSelector
            {
                query = SearchQuery.Adjacent(labelText, direction),
                displayName = displayName ?? $"near \"{labelText}\""
            };
        }

        /// <summary>
        /// Creates a selector by component type.
        /// </summary>
        public static ElementSelector ByType(string typeName, string displayName = null)
        {
            return new ElementSelector
            {
                query = SearchQuery.Type(typeName),
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
                query = SearchQuery.Path(path),
                displayName = displayName ?? path
            };
        }

        /// <summary>
        /// Creates a selector by texture/sprite name.
        /// </summary>
        public static ElementSelector ByTexture(string textureName, string displayName = null)
        {
            return new ElementSelector
            {
                query = SearchQuery.Texture(textureName),
                displayName = displayName ?? $"Texture:{textureName}"
            };
        }

        /// <summary>
        /// Creates a selector by Unity tag.
        /// </summary>
        public static ElementSelector ByTag(string tag, string displayName = null)
        {
            return new ElementSelector
            {
                query = SearchQuery.Tag(tag),
                displayName = displayName ?? $"Tag:{tag}"
            };
        }

        /// <summary>
        /// Creates a selector that matches text, name, or path.
        /// </summary>
        public static ElementSelector ByAny(string pattern, string displayName = null)
        {
            return new ElementSelector
            {
                query = SearchQuery.Any(pattern),
                displayName = displayName ?? pattern
            };
        }

        /// <summary>
        /// Creates a selector for elements near another element.
        /// </summary>
        public static ElementSelector NearTo(string targetText, string direction = null, string displayName = null)
        {
            return new ElementSelector
            {
                query = SearchQuery.Near(targetText, direction),
                displayName = displayName ?? $"Near:{targetText}"
            };
        }

        /// <summary>
        /// Adds a chain filter to the selector.
        /// </summary>
        public ElementSelector Near(string target, string direction = null)
        {
            query.chain ??= new List<SearchChainItem>();
            query.chain.Add(SearchChainItem.Near(target, direction));
            return this;
        }

        /// <summary>
        /// Adds a hasParent filter to the selector.
        /// </summary>
        public ElementSelector HasParent(string parentName)
        {
            query.chain ??= new List<SearchChainItem>();
            query.chain.Add(SearchChainItem.HasParent(parentName));
            return this;
        }

        /// <summary>
        /// Adds a hasAncestor filter to the selector.
        /// </summary>
        public ElementSelector HasAncestor(string ancestorName)
        {
            query.chain ??= new List<SearchChainItem>();
            query.chain.Add(SearchChainItem.HasAncestor(ancestorName));
            return this;
        }

        /// <summary>
        /// Adds an includeInactive filter to the selector.
        /// </summary>
        public ElementSelector IncludeInactive()
        {
            query.chain ??= new List<SearchChainItem>();
            query.chain.Add(SearchChainItem.IncludeInactive());
            return this;
        }

        /// <summary>
        /// Adds a first filter to the selector.
        /// </summary>
        public ElementSelector First()
        {
            query.chain ??= new List<SearchChainItem>();
            query.chain.Add(SearchChainItem.First());
            return this;
        }

        /// <summary>
        /// Converts this selector to a Search object that can find elements.
        /// </summary>
        public Search ToSearch()
        {
            return query?.ToSearch();
        }

        /// <summary>
        /// Gets a human-readable description of this selector.
        /// </summary>
        public string GetDisplayText()
        {
            if (!string.IsNullOrEmpty(displayName))
                return displayName;

            if (query == null)
                return "(no selector)";

            // Build display from query - use prefixes to distinguish search types
            var display = query.searchBase switch
            {
                "text" => $"Text(\"{query.value}\")",
                "name" => $"Name(\"{query.value}\")",
                "type" => $"Type<{query.value}>",
                "adjacent" => $"Adjacent(\"{query.value}\", {query.direction ?? "?"})",
                "near" => $"Near(\"{query.value}\")",
                "path" => $"Path(\"{query.value}\")",
                "texture" => $"Texture(\"{query.value}\")",
                "tag" => $"Tag(\"{query.value}\")",
                "any" => $"Any(\"{query.value}\")",
                _ => query.value ?? "(unknown)"
            };

            // Add chain info
            if (query.chain != null && query.chain.Count > 0)
            {
                foreach (var item in query.chain)
                {
                    display += item.method switch
                    {
                        "near" => $".Near(\"{item.value}\")",
                        "hasParent" => $".HasParent(\"{item.value}\")",
                        "hasAncestor" => $".HasAncestor(\"{item.value}\")",
                        "hasChild" => $".HasChild(\"{item.value}\")",
                        "hasSibling" => $".HasSibling(\"{item.value}\")",
                        "first" => ".First()",
                        "last" => ".Last()",
                        "skip" => $".Skip({item.count})",
                        "take" => $".Take({item.count})",
                        "visible" => ".Visible()",
                        "interactable" => ".Interactable()",
                        "inRegion" => $".InRegion(\"{item.value}\")",
                        _ => $".{item.method}()"
                    };
                }
            }

            return display;
        }

        /// <summary>
        /// Creates a deep copy of this selector.
        /// </summary>
        public ElementSelector Clone()
        {
            var clone = new ElementSelector
            {
                displayName = displayName
            };

            if (query != null)
            {
                clone.query = new SearchQuery
                {
                    searchBase = query.searchBase,
                    value = query.value,
                    direction = query.direction
                };

                if (query.chain != null)
                {
                    clone.query.chain = new List<SearchChainItem>();
                    foreach (var item in query.chain)
                    {
                        clone.query.chain.Add(new SearchChainItem
                        {
                            method = item.method,
                            value = item.value,
                            direction = item.direction,
                            count = item.count,
                            index = item.index,
                            offset = item.offset,
                            xMin = item.xMin,
                            yMin = item.yMin,
                            xMax = item.xMax,
                            yMax = item.yMax
                        });
                    }
                }
            }

            return clone;
        }

        /// <summary>
        /// Returns true if this selector has enough information to find an element.
        /// </summary>
        public bool IsValid()
        {
            return query != null && !string.IsNullOrEmpty(query.searchBase);
        }

        /// <summary>
        /// Serializes this selector's query to JSON.
        /// </summary>
        public string ToJson()
        {
            return query?.ToJson();
        }

        /// <summary>
        /// Creates a selector from a JSON string.
        /// </summary>
        public static ElementSelector FromJson(string json, string displayName = null)
        {
            var q = SearchQuery.FromJson(json);
            return q != null ? FromQuery(q, displayName) : null;
        }
    }
}
