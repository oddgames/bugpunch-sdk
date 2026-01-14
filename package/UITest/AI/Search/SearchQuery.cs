using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace ODDGames.UITest.AI
{
    /// <summary>
    /// JSON-serializable search query that can be used by AI tools and visual blocks.
    /// Supports chaining with strict parameter typing.
    ///
    /// Example JSON:
    /// {
    ///   "base": "text",
    ///   "value": "Submit",
    ///   "chain": [
    ///     { "method": "near", "value": "Panel" },
    ///     { "method": "first" }
    ///   ]
    /// }
    /// </summary>
    [Serializable]
    public class SearchQuery
    {
        /// <summary>
        /// Base search method: "text", "name", "type", "path", "adjacent", "any", "sprite", "tag"
        /// Uses [JsonProperty] to map from "base" in JSON while avoiding C# keyword conflict.
        /// </summary>
        [JsonProperty("base")]
        public string searchBase;

        /// <summary>
        /// Value for the base search (text pattern, name pattern, type name, etc.)
        /// </summary>
        public string value;

        /// <summary>
        /// Direction for adjacent searches: "right", "left", "above", "below"
        /// </summary>
        public string direction;

        /// <summary>
        /// Chain of filter methods to apply after base search
        /// </summary>
        public List<SearchChainItem> chain;

        /// <summary>
        /// Converts this query to a Search object that can find elements.
        /// </summary>
        public Search ToSearch()
        {
            var search = CreateBaseSearch();
            if (search == null)
                return null;

            // Apply chain methods
            if (chain != null)
            {
                foreach (var item in chain)
                {
                    search = ApplyChainMethod(search, item);
                    if (search == null)
                        return null;
                }
            }

            return search;
        }

        private Search CreateBaseSearch()
        {
            switch (searchBase?.ToLower())
            {
                case "text":
                    return string.IsNullOrEmpty(value) ? null : new Search().Text(value);

                case "name":
                    return string.IsNullOrEmpty(value) ? null : new Search().Name(value);

                case "path":
                    return string.IsNullOrEmpty(value) ? null : new Search().Path(value);

                case "type":
                    return string.IsNullOrEmpty(value) ? null : new Search().Type(value);

                case "adjacent":
                case "adjacentto":
                    if (string.IsNullOrEmpty(value)) return null;
                    var dir = ParseDirection(direction);
                    return new Search().Adjacent(value, dir);

                case "sprite":
                    return string.IsNullOrEmpty(value) ? null : new Search().Sprite(value);

                case "tag":
                    return string.IsNullOrEmpty(value) ? null : new Search().Tag(value);

                case "any":
                    return string.IsNullOrEmpty(value) ? new Search() : new Search().Any(value);

                case "near":
                case "nearto":
                    if (string.IsNullOrEmpty(value)) return null;
                    var nearDir = !string.IsNullOrEmpty(direction) ? (Direction?)ParseDirection(direction) : null;
                    return nearDir.HasValue
                        ? new Search().Near(value, nearDir)
                        : new Search().Near(value);

                default:
                    Debug.LogWarning($"[SearchQuery] Unknown base method: {searchBase}");
                    return null;
            }
        }

        private static Search ApplyChainMethod(Search search, SearchChainItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.method))
                return search;

            switch (item.method.ToLower())
            {
                case "near":
                case "nearto":
                    if (string.IsNullOrEmpty(item.value)) return search;
                    var nearDir = !string.IsNullOrEmpty(item.direction) ? (Direction?)ParseDirection(item.direction) : null;
                    return nearDir.HasValue
                        ? search.Near(item.value, nearDir)
                        : search.Near(item.value);

                case "hasparent":
                    if (item.search != null)
                        return search.HasParent(item.search.ToSearch());
                    return string.IsNullOrEmpty(item.value) ? search : search.HasParent(item.value);

                case "hasancestor":
                    if (item.search != null)
                        return search.HasAncestor(item.search.ToSearch());
                    return string.IsNullOrEmpty(item.value) ? search : search.HasAncestor(item.value);

                case "haschild":
                    if (item.search != null)
                        return search.HasChild(item.search.ToSearch());
                    return string.IsNullOrEmpty(item.value) ? search : search.HasChild(item.value);

                case "hasdescendant":
                    if (item.search != null)
                        return search.HasDescendant(item.search.ToSearch());
                    return string.IsNullOrEmpty(item.value) ? search : search.HasDescendant(item.value);

                case "hassibling":
                    if (item.search != null)
                        return search.HasSibling(item.search.ToSearch());
                    return string.IsNullOrEmpty(item.value) ? search : search.HasSibling(item.value);

                case "includeinactive":
                    return search.IncludeInactive();

                case "includedisabled":
                    return search.IncludeDisabled();

                case "interactable":
                    return search.Interactable();

                case "visible":
                    return search.Visible();

                case "first":
                    return search.First();

                case "last":
                    return search.Last();

                case "skip":
                    return item.count > 0 ? search.Skip(item.count) : search;

                case "take":
                    return item.count > 0 ? search.Take(item.count) : search;

                case "inregion":
                    // Named region
                    if (!string.IsNullOrEmpty(item.value) && Enum.TryParse<ScreenRegion>(item.value, true, out var region))
                        return search.InRegion(region);
                    // Custom bounds
                    if (item.xMin >= 0 && item.yMin >= 0 && item.xMax > 0 && item.yMax > 0)
                        return search.InRegion(item.xMin, item.yMin, item.xMax, item.yMax);
                    return search;

                case "getparent":
                    return search.GetParent();

                case "getchild":
                    return search.GetChild(item.index);

                case "getsibling":
                    return search.GetSibling(item.offset);

                case "type":
                    return string.IsNullOrEmpty(item.value) ? search : search.Type(item.value);

                case "text":
                    return string.IsNullOrEmpty(item.value) ? search : search.Text(item.value);

                case "name":
                    return string.IsNullOrEmpty(item.value) ? search : search.Name(item.value);

                case "path":
                    return string.IsNullOrEmpty(item.value) ? search : search.Path(item.value);

                case "tag":
                    return string.IsNullOrEmpty(item.value) ? search : search.Tag(item.value);

                case "sprite":
                    return string.IsNullOrEmpty(item.value) ? search : search.Sprite(item.value);

                case "any":
                    return string.IsNullOrEmpty(item.value) ? search : search.Any(item.value);

                case "adjacent":
                case "adjacentto":
                    if (string.IsNullOrEmpty(item.value)) return search;
                    var adjDir = ParseDirection(item.direction);
                    return search.Adjacent(item.value, adjDir);

                default:
                    Debug.LogWarning($"[SearchQuery] Unknown chain method: {item.method}");
                    return search;
            }
        }

        private static Direction ParseDirection(string dir)
        {
            if (string.IsNullOrEmpty(dir))
                return Direction.Right;

            return dir.ToLower() switch
            {
                "right" => Direction.Right,
                "left" => Direction.Left,
                "above" => Direction.Above,
                "up" => Direction.Above,
                "below" => Direction.Below,
                "down" => Direction.Below,
                _ => Direction.Right
            };
        }

        /// <summary>
        /// Creates a SearchQuery from a Search API string pattern.
        /// </summary>
        public static SearchQuery FromString(string pattern)
        {
            // Use the existing parser to build a Search, then we'd need to reverse-engineer it
            // For now, return null - prefer using JSON directly
            Debug.LogWarning("[SearchQuery] FromString not implemented - use JSON format");
            return null;
        }

        /// <summary>
        /// Parses a JSON string into a SearchQuery.
        /// </summary>
        public static SearchQuery FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<SearchQuery>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SearchQuery] Failed to parse JSON: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Serializes this SearchQuery to JSON.
        /// </summary>
        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.None, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
        }

        /// <summary>
        /// Creates a simple text search query.
        /// </summary>
        public static SearchQuery Text(string text)
        {
            return new SearchQuery { searchBase = "text", value = text };
        }

        /// <summary>
        /// Creates a simple name search query.
        /// </summary>
        public static SearchQuery Name(string name)
        {
            return new SearchQuery { searchBase = "name", value = name };
        }

        /// <summary>
        /// Creates an adjacent search query.
        /// </summary>
        public static SearchQuery Adjacent(string labelText, string direction = "right")
        {
            return new SearchQuery { searchBase = "adjacent", value = labelText, direction = direction };
        }

        /// <summary>
        /// Creates a type search query.
        /// </summary>
        public static SearchQuery Type(string typeName)
        {
            return new SearchQuery { searchBase = "type", value = typeName };
        }
    }

    /// <summary>
    /// A single chain method in a search query.
    /// </summary>
    [Serializable]
    public class SearchChainItem
    {
        /// <summary>
        /// Method name: "near", "hasParent", "hasAncestor", "hasChild", "includeInactive", etc.
        /// </summary>
        public string method;

        /// <summary>
        /// String value for methods like near("value"), hasParent("value")
        /// </summary>
        public string value;

        /// <summary>
        /// Nested search query for recursive filtering (alternative to value).
        /// Use for complex hierarchy filters like hasParent with its own filters.
        /// </summary>
        public SearchQuery search;

        /// <summary>
        /// Direction for near with direction: "right", "left", "above", "below"
        /// </summary>
        public string direction;

        /// <summary>
        /// Count for skip(n), take(n)
        /// </summary>
        public int count;

        /// <summary>
        /// Index for getChild(index)
        /// </summary>
        public int index;

        /// <summary>
        /// Offset for getSibling(offset)
        /// </summary>
        public int offset;

        /// <summary>
        /// Region bounds for inRegion(xMin, yMin, xMax, yMax)
        /// </summary>
        public float xMin, yMin, xMax, yMax;

        /// <summary>
        /// Creates a near chain item.
        /// </summary>
        public static SearchChainItem Near(string value, string direction = null)
        {
            return new SearchChainItem { method = "near", value = value, direction = direction };
        }

        /// <summary>
        /// Creates a hasParent chain item.
        /// </summary>
        public static SearchChainItem HasParent(string value)
        {
            return new SearchChainItem { method = "hasParent", value = value };
        }

        /// <summary>
        /// Creates a hasAncestor chain item.
        /// </summary>
        public static SearchChainItem HasAncestor(string value)
        {
            return new SearchChainItem { method = "hasAncestor", value = value };
        }

        /// <summary>
        /// Creates an includeInactive chain item.
        /// </summary>
        public static SearchChainItem IncludeInactive()
        {
            return new SearchChainItem { method = "includeInactive" };
        }

        /// <summary>
        /// Creates a first chain item.
        /// </summary>
        public static SearchChainItem First()
        {
            return new SearchChainItem { method = "first" };
        }

        /// <summary>
        /// Creates a skip chain item.
        /// </summary>
        public static SearchChainItem Skip(int count)
        {
            return new SearchChainItem { method = "skip", count = count };
        }

        /// <summary>
        /// Creates a take chain item.
        /// </summary>
        public static SearchChainItem Take(int count)
        {
            return new SearchChainItem { method = "take", count = count };
        }
    }
}
