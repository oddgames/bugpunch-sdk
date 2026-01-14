using System;
using System.Collections.Generic;
using UnityEngine;

namespace ODDGames.UITest.AI
{
    /// <summary>
    /// Parses JSON-formatted search queries from AI tool calls into Search objects.
    ///
    /// JSON Format:
    /// {
    ///   "method": "Text|Name|Type|Adjacent|Path|Any",
    ///   "value": "pattern",
    ///   "direction": "right|left|above|below",  // for Adjacent only
    ///   "chain": [                               // optional filters
    ///     { "method": "Near|HasParent|HasAncestor|...", "value": "pattern" },
    ///     { "method": "IncludeInactive" },
    ///     { "method": "First" }
    ///   ]
    /// }
    ///
    /// Or simple string format (backwards compatible):
    /// "Text(\"Submit\")"  - parsed as search pattern
    /// </summary>
    public static class SearchQueryParser
    {
        /// <summary>
        /// Parses a search query string into a Search object.
        /// Supports both JSON object format and simple string patterns.
        /// </summary>
        /// <param name="searchQuery">The search query from AI tool call - either JSON object or string pattern</param>
        /// <returns>A Search object, or null if parsing fails</returns>
        public static Search Parse(string searchQuery)
        {
            if (string.IsNullOrEmpty(searchQuery))
                return null;

            searchQuery = searchQuery.Trim();

            // Try JSON object format first
            if (searchQuery.StartsWith("{"))
            {
                return ParseJsonFormat(searchQuery);
            }

            // Try string pattern format: Text("Submit"), Name("Button*"), etc.
            return ParseStringPattern(searchQuery);
        }

        /// <summary>
        /// Parses a JSON object format search query.
        /// </summary>
        private static Search ParseJsonFormat(string json)
        {
            try
            {
                // Use Unity's JsonUtility with a wrapper class, or simple parsing
                var query = JsonUtility.FromJson<SearchQuery>(json);
                return BuildSearchFromQuery(query);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SearchQueryParser] Failed to parse JSON: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parses a string pattern format like Text("Submit").Near("Panel")
        /// </summary>
        private static Search ParseStringPattern(string pattern)
        {
            try
            {
                // Split on ).(  to get method chain
                var parts = SplitMethodChain(pattern);
                if (parts.Count == 0)
                    return null;

                // Parse base method
                var (baseMethod, baseArgs) = ParseMethodCall(parts[0]);
                var search = CreateBaseSearch(baseMethod, baseArgs);
                if (search == null)
                    return null;

                // Apply chain methods
                for (int i = 1; i < parts.Count; i++)
                {
                    var (method, args) = ParseMethodCall(parts[i]);
                    search = ApplyChainMethod(search, method, args);
                    if (search == null)
                        return null;
                }

                return search;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SearchQueryParser] Failed to parse pattern '{pattern}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Splits a method chain like "Text(\"a\").Near(\"b\").First()" into parts.
        /// </summary>
        private static List<string> SplitMethodChain(string pattern)
        {
            var parts = new List<string>();
            int depth = 0;
            int start = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                    continue;

                if (c == '(')
                    depth++;
                else if (c == ')')
                    depth--;
                else if (c == '.' && depth == 0)
                {
                    // Split here
                    if (i > start)
                        parts.Add(pattern.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }

            // Add final part
            if (start < pattern.Length)
                parts.Add(pattern.Substring(start).Trim());

            return parts;
        }

        /// <summary>
        /// Parses a single method call like "Text(\"Submit\")" into method name and arguments.
        /// </summary>
        private static (string method, string[] args) ParseMethodCall(string call)
        {
            int parenStart = call.IndexOf('(');
            if (parenStart < 0)
            {
                // No parentheses, just method name like "First" or "IncludeInactive"
                return (call.Trim(), Array.Empty<string>());
            }

            string method = call.Substring(0, parenStart).Trim();

            // Handle generic types like Type<Button>
            int genericStart = method.IndexOf('<');
            if (genericStart > 0)
            {
                // Extract type from generic: Type<Button> -> method=Type, add type as arg
                int genericEnd = method.IndexOf('>');
                if (genericEnd > genericStart)
                {
                    string genericType = method.Substring(genericStart + 1, genericEnd - genericStart - 1);
                    method = method.Substring(0, genericStart);
                    return (method, new[] { genericType });
                }
            }

            int parenEnd = call.LastIndexOf(')');
            if (parenEnd <= parenStart)
                return (method, Array.Empty<string>());

            string argsStr = call.Substring(parenStart + 1, parenEnd - parenStart - 1).Trim();
            if (string.IsNullOrEmpty(argsStr))
                return (method, Array.Empty<string>());

            // Parse comma-separated arguments, respecting quotes
            var args = ParseArguments(argsStr);
            return (method, args.ToArray());
        }

        /// <summary>
        /// Parses comma-separated arguments, handling quoted strings.
        /// </summary>
        private static List<string> ParseArguments(string argsStr)
        {
            var args = new List<string>();
            bool inString = false;
            bool escaped = false;
            int start = 0;

            for (int i = 0; i < argsStr.Length; i++)
            {
                char c = argsStr[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (c == ',' && !inString)
                {
                    args.Add(ExtractArgValue(argsStr.Substring(start, i - start)));
                    start = i + 1;
                }
            }

            // Add final argument
            if (start < argsStr.Length)
                args.Add(ExtractArgValue(argsStr.Substring(start)));

            return args;
        }

        /// <summary>
        /// Extracts the value from an argument, removing quotes if present.
        /// </summary>
        private static string ExtractArgValue(string arg)
        {
            arg = arg.Trim();

            // Remove surrounding quotes
            if (arg.Length >= 2 && arg.StartsWith("\"") && arg.EndsWith("\""))
            {
                arg = arg.Substring(1, arg.Length - 2);
                // Unescape
                arg = arg.Replace("\\\"", "\"").Replace("\\\\", "\\");
            }

            return arg;
        }

        /// <summary>
        /// Creates the base Search object from a method name and arguments.
        /// </summary>
        private static Search CreateBaseSearch(string method, string[] args)
        {
            switch (method.ToLower())
            {
                case "text":
                    if (args.Length < 1) return null;
                    return new Search().Text(args[0]);

                case "name":
                    if (args.Length < 1) return null;
                    return new Search().Name(args[0]);

                case "path":
                    if (args.Length < 1) return null;
                    return new Search().Path(args[0]);

                case "any":
                    if (args.Length < 1) return new Search();
                    return new Search().Any(args[0]);

                case "type":
                    if (args.Length < 1) return null;
                    return new Search().Type(args[0]);

                case "adjacent":
                case "adjacentto":
                    if (args.Length < 1) return null;
                    var direction = args.Length > 1 ? ParseDirection(args[1]) : Direction.Right;
                    return new Search().Adjacent(args[0], direction);

                case "sprite":
                    if (args.Length < 1) return null;
                    return new Search().Sprite(args[0]);

                case "tag":
                    if (args.Length < 1) return null;
                    return new Search().Tag(args[0]);

                default:
                    Debug.LogWarning($"[SearchQueryParser] Unknown base method: {method}");
                    return null;
            }
        }

        /// <summary>
        /// Applies a chain method to an existing Search object.
        /// </summary>
        private static Search ApplyChainMethod(Search search, string method, string[] args)
        {
            switch (method.ToLower())
            {
                case "near":
                case "nearto":
                    if (args.Length < 1) return search;
                    var dir = args.Length > 1 ? ParseDirection(args[1]) : (Direction?)null;
                    return dir.HasValue
                        ? search.Near(args[0], dir)
                        : search.Near(args[0]);

                case "hasparent":
                    if (args.Length < 1) return search;
                    return search.HasParent(args[0]);

                case "hasancestor":
                    if (args.Length < 1) return search;
                    return search.HasAncestor(args[0]);

                case "haschild":
                    if (args.Length < 1) return search;
                    return search.HasChild(args[0]);

                case "hasdescendant":
                    if (args.Length < 1) return search;
                    return search.HasDescendant(args[0]);

                case "hassibling":
                    if (args.Length < 1) return search;
                    return search.HasSibling(args[0]);

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
                    if (args.Length < 1) return search;
                    if (int.TryParse(args[0], out int skipCount))
                        return search.Skip(skipCount);
                    return search;

                case "take":
                    if (args.Length < 1) return search;
                    if (int.TryParse(args[0], out int takeCount))
                        return search.Take(takeCount);
                    return search;

                case "inregion":
                    if (args.Length == 1)
                    {
                        // Named region
                        if (Enum.TryParse<ScreenRegion>(args[0], true, out var region))
                            return search.InRegion(region);
                    }
                    else if (args.Length >= 4)
                    {
                        // Custom bounds
                        if (float.TryParse(args[0], out float xMin) &&
                            float.TryParse(args[1], out float yMin) &&
                            float.TryParse(args[2], out float xMax) &&
                            float.TryParse(args[3], out float yMax))
                        {
                            return search.InRegion(xMin, yMin, xMax, yMax);
                        }
                    }
                    return search;

                case "getparent":
                    return search.GetParent();

                case "getchild":
                    if (args.Length < 1) return search.GetChild(0);
                    if (int.TryParse(args[0], out int childIndex))
                        return search.GetChild(childIndex);
                    return search;

                case "getsibling":
                    if (args.Length < 1) return search.GetSibling(1);
                    if (int.TryParse(args[0], out int siblingOffset))
                        return search.GetSibling(siblingOffset);
                    return search;

                case "type":
                    if (args.Length < 1) return search;
                    return search.Type(args[0]);

                case "text":
                    if (args.Length < 1) return search;
                    return search.Text(args[0]);

                case "name":
                    if (args.Length < 1) return search;
                    return search.Name(args[0]);

                default:
                    Debug.LogWarning($"[SearchQueryParser] Unknown chain method: {method}");
                    return search;
            }
        }

        /// <summary>
        /// Parses a direction string to Direction enum.
        /// </summary>
        private static Direction ParseDirection(string dir)
        {
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
        /// Builds a Search object from a parsed JSON query.
        /// </summary>
        private static Search BuildSearchFromQuery(SearchQuery query)
        {
            if (query == null || string.IsNullOrEmpty(query.method))
                return null;

            // Create base search
            string[] baseArgs = string.IsNullOrEmpty(query.value)
                ? Array.Empty<string>()
                : new[] { query.value };

            // Add direction for Adjacent
            if (!string.IsNullOrEmpty(query.direction))
            {
                baseArgs = new[] { query.value, query.direction };
            }

            var search = CreateBaseSearch(query.method, baseArgs);
            if (search == null)
                return null;

            // Apply chain methods
            if (query.chain != null)
            {
                foreach (var chainItem in query.chain)
                {
                    string[] chainArgs = string.IsNullOrEmpty(chainItem.value)
                        ? Array.Empty<string>()
                        : new[] { chainItem.value };

                    search = ApplyChainMethod(search, chainItem.method, chainArgs);
                }
            }

            return search;
        }

        /// <summary>
        /// JSON structure for search queries.
        /// </summary>
        [Serializable]
        private class SearchQuery
        {
            public string method;
            public string value;
            public string direction;
            public ChainItem[] chain;
        }

        [Serializable]
        private class ChainItem
        {
            public string method;
            public string value;
        }
    }
}
