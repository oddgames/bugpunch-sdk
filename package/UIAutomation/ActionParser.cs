#if UNITY_INCLUDE_TESTS
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ODDGames.UIAutomation
{
    /// <summary>
    /// Result of executing a parsed action.
    /// </summary>
    public class ActionResult
    {
        public bool Success;
        public string Action;
        public string ScreenshotPath;
        public string Error;
        public float ElapsedMs;
    }

    public static partial class ActionExecutor
    {
        /// <summary>
        /// Parse and execute a JSON action. Takes a screenshot after the action.
        /// <para>Examples:</para>
        /// <code>
        /// await Execute("{\"action\":\"click\", \"text\":\"Settings\"}");
        /// await Execute("{\"action\":\"type\", \"name\":\"InputField\", \"value\":\"hello\"}");
        /// await Execute("{\"action\":\"swipe\", \"direction\":\"left\"}");
        /// await Execute("{\"action\":\"wait\", \"seconds\":2}");
        /// </code>
        /// </summary>
        public static async Task<ActionResult> Execute(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new ActionResult { Success = false, Error = "Empty action" };

            var sw = Stopwatch.StartNew();
            var result = new ActionResult { Action = json.Trim() };

            try
            {
                var obj = JObject.Parse(json);
                await ActionJsonExecutor.Execute(obj);
                result.Success = true;
            }
            catch (JsonReaderException ex)
            {
                result.Success = false;
                result.Error = $"Invalid JSON: {ex.Message}";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
            }

            try
            {
                result.ScreenshotPath = await Screenshot();
            }
            catch
            {
                // Screenshot failure shouldn't mask action result
            }

            sw.Stop();
            result.ElapsedMs = (float)sw.Elapsed.TotalMilliseconds;
            return result;
        }
    }

    /// <summary>
    /// Executes UI actions from JSON objects via ActionExecutor.
    /// <para>
    /// JSON format — "action" is required, everything else depends on the verb:
    /// <code>
    /// // Click by text, name, or position
    /// {"action":"click", "text":"Settings"}
    /// {"action":"click", "name":"SettingsBtn"}
    /// {"action":"click", "at":[0.5, 0.5]}
    /// {"action":"doubleclick", "text":"Item"}
    ///
    /// // Type into a field
    /// {"action":"type", "name":"InputField", "value":"hello world"}
    /// {"action":"type", "adjacent":"Username:", "value":"admin", "clear":true, "enter":true}
    ///
    /// // Swipe / scroll
    /// {"action":"swipe", "direction":"left"}
    /// {"action":"swipe", "direction":"up", "name":"Panel", "distance":0.3}
    /// {"action":"scroll", "name":"ListView", "delta":-120}
    /// {"action":"scrollto", "name":"ScrollView", "target":{"text":"TargetItem"}}
    ///
    /// // Drag
    /// {"action":"drag", "from":{"name":"Source"}, "to":{"name":"Target"}}
    ///
    /// // Dropdown
    /// {"action":"dropdown", "name":"Dropdown", "option":2}
    /// {"action":"dropdown", "name":"Dropdown", "option":"Option Label"}
    ///
    /// // Gestures
    /// {"action":"pinch", "scale":2.0}
    /// {"action":"rotate", "degrees":45}
    ///
    /// // Misc
    /// {"action":"hold", "text":"Button", "seconds":2}
    /// {"action":"wait", "seconds":1.5}
    /// {"action":"key", "key":"enter"}
    /// {"action":"screenshot"}
    /// </code>
    /// </para>
    /// </summary>
    internal static class ActionJsonExecutor
    {
        internal static async Task Execute(JObject obj)
        {
            string action = obj.Value<string>("action")?.ToLowerInvariant()
                ?? throw new ArgumentException("Missing 'action' field");

            switch (action)
            {
                case "click":
                    await ExecuteClick(obj);
                    break;
                case "doubleclick":
                    await ExecuteDoubleClick(obj);
                    break;
                case "tripleclick":
                    await ExecuteTripleClick(obj);
                    break;
                case "type":
                    await ExecuteType(obj);
                    break;
                case "textinput":
                    await ExecuteTextInput(obj);
                    break;
                case "swipe":
                    await ExecuteSwipe(obj);
                    break;
                case "scroll":
                    await ExecuteScroll(obj);
                    break;
                case "scrollto":
                    await ExecuteScrollTo(obj);
                    break;
                case "wait":
                    await ActionExecutor.Wait(obj.Value<float?>("seconds") ?? 1f);
                    break;
                case "hold":
                    await ExecuteHold(obj);
                    break;
                case "drag":
                    await ExecuteDrag(obj);
                    break;
                case "dropdown":
                    await ExecuteDropdown(obj);
                    break;
                case "pinch":
                    await ExecutePinch(obj);
                    break;
                case "rotate":
                    await ExecuteRotate(obj);
                    break;
                case "key":
                    await ActionExecutor.PressKey(obj.Value<string>("key") ?? "");
                    break;
                case "screenshot":
                    // No-op — screenshot is taken automatically by Execute()
                    break;
                default:
                    throw new ArgumentException(
                        $"Unknown action: '{action}'. Valid: click, doubleclick, tripleclick, type, textinput, " +
                        "swipe, scroll, scrollto, wait, hold, drag, dropdown, pinch, rotate, key, screenshot");
            }
        }

        private static async Task ExecuteClick(JObject obj)
        {
            var at = obj["at"] as JArray;
            if (at != null && at.Count == 2)
                await ActionExecutor.ClickAt(new Vector2(at[0].Value<float>(), at[1].Value<float>()));
            else
                await ActionExecutor.Click(BuildSearch(obj));
        }

        private static async Task ExecuteDoubleClick(JObject obj)
        {
            var at = obj["at"] as JArray;
            if (at != null && at.Count == 2)
                await ActionExecutor.DoubleClickAt(new Vector2(at[0].Value<float>(), at[1].Value<float>()));
            else
                await ActionExecutor.DoubleClick(BuildSearch(obj));
        }

        private static async Task ExecuteTripleClick(JObject obj)
        {
            var at = obj["at"] as JArray;
            if (at != null && at.Count == 2)
                await ActionExecutor.TripleClickAt(new Vector2(at[0].Value<float>(), at[1].Value<float>()));
            else
                await ActionExecutor.TripleClick(BuildSearch(obj));
        }

        private static async Task ExecuteType(JObject obj)
        {
            var search = BuildSearch(obj);
            string value = obj.Value<string>("value") ?? "";
            bool clear = obj.Value<bool?>("clear") ?? true;
            bool enter = obj.Value<bool?>("enter") ?? false;
            await ActionExecutor.Type(search, value, clear, enter);
        }

        private static async Task ExecuteTextInput(JObject obj)
        {
            var search = BuildSearch(obj);
            string value = obj.Value<string>("value") ?? "";
            await ActionExecutor.TextInput(search, value);
        }

        private static async Task ExecuteSwipe(JObject obj)
        {
            var dir = ParseSwipeDirection(obj.Value<string>("direction") ?? "left");
            float distance = obj.Value<float?>("distance") ?? 0.2f;
            float duration = obj.Value<float?>("duration") ?? 0.15f;

            var search = TryBuildSearch(obj);
            if (search != null)
                await ActionExecutor.Swipe(search, dir, distance, duration);
            else
                await ActionExecutor.Swipe(dir, distance, duration);
        }

        private static async Task ExecuteScroll(JObject obj)
        {
            float delta = obj.Value<float?>("delta") ?? -120f;

            var search = TryBuildSearch(obj);
            if (search != null)
                await ActionExecutor.Scroll(search, delta);
            else
                await ActionExecutor.ScrollAt(new Vector2(0.5f, 0.5f), delta);
        }

        private static async Task ExecuteScrollTo(JObject obj)
        {
            var container = BuildSearch(obj);
            var targetObj = obj["target"] as JObject;
            if (targetObj == null)
                throw new ArgumentException("scrollto requires a 'target' object");

            var target = BuildSearch(targetObj);
            await ActionExecutor.ScrollTo(container, target);
        }

        private static async Task ExecuteHold(JObject obj)
        {
            float seconds = obj.Value<float?>("seconds") ?? 1f;

            var at = obj["at"] as JArray;
            if (at != null && at.Count == 2)
                await ActionExecutor.HoldAt(new Vector2(at[0].Value<float>(), at[1].Value<float>()), seconds);
            else
                await ActionExecutor.Hold(BuildSearch(obj), seconds);
        }

        private static async Task ExecuteDrag(JObject obj)
        {
            var fromObj = obj["from"] as JObject;
            var toObj = obj["to"] as JObject;
            if (fromObj == null || toObj == null)
                throw new ArgumentException("drag requires 'from' and 'to' objects");

            float duration = obj.Value<float?>("duration") ?? 0.15f;
            await ActionExecutor.DragTo(BuildSearch(fromObj), BuildSearch(toObj), duration);
        }

        private static async Task ExecuteDropdown(JObject obj)
        {
            var search = BuildSearch(obj);
            var option = obj["option"];

            if (option == null)
                throw new ArgumentException("dropdown requires 'option' (int index or string label)");

            if (option.Type == JTokenType.Integer)
                await ActionExecutor.ClickDropdown(search, option.Value<int>());
            else
                await ActionExecutor.ClickDropdown(search, option.Value<string>());
        }

        private static async Task ExecutePinch(JObject obj)
        {
            float scale = obj.Value<float?>("scale") ?? 2f;
            float duration = obj.Value<float?>("duration") ?? 0.15f;

            var search = TryBuildSearch(obj);
            if (search != null)
                await ActionExecutor.Pinch(search, scale, duration);
            else
                await ActionExecutor.Pinch(scale, duration);
        }

        private static async Task ExecuteRotate(JObject obj)
        {
            float degrees = obj.Value<float?>("degrees") ?? 90f;
            float duration = obj.Value<float?>("duration") ?? 0.15f;

            var search = TryBuildSearch(obj);
            if (search != null)
                await ActionExecutor.Rotate(search, degrees, duration);
            else
                await ActionExecutor.Rotate(degrees, duration);
        }

        // --- Search builder ---

        /// <summary>
        /// Builds a Search from JSON fields: text, name, type, near, adjacent, tag, path, any.
        /// Multiple fields are chained (AND logic).
        /// Throws if no search fields found.
        /// </summary>
        private static Search BuildSearch(JObject obj)
        {
            var search = TryBuildSearch(obj);
            if (search == null)
                throw new ArgumentException(
                    "No target specified. Use one or more of: text, name, type, near, adjacent, tag, path, any, at");
            return search;
        }

        /// <summary>
        /// Builds a Search from JSON fields. Returns null if no search fields found.
        /// </summary>
        private static Search TryBuildSearch(JObject obj)
        {
            Search search = null;

            string text = obj.Value<string>("text");
            if (text != null)
                search = (search ?? new Search()).Text(text);

            string name = obj.Value<string>("name");
            if (name != null)
                search = (search ?? new Search()).Name(name);

            string type = obj.Value<string>("type");
            if (type != null)
                search = (search ?? new Search()).Type(type);

            string near = obj.Value<string>("near");
            if (near != null)
                search = ActionExecutor.Near(near);

            string adjacent = obj.Value<string>("adjacent");
            if (adjacent != null)
                search = ActionExecutor.Adjacent(adjacent);

            string tag = obj.Value<string>("tag");
            if (tag != null)
                search = (search ?? new Search()).Tag(tag);

            string path = obj.Value<string>("path");
            if (path != null)
                search = (search ?? new Search()).Path(path);

            string any = obj.Value<string>("any");
            if (any != null)
                search = ActionExecutor.Any(any);

            return search;
        }

        private static SwipeDirection ParseSwipeDirection(string dir)
        {
            switch (dir.ToLowerInvariant())
            {
                case "left": return SwipeDirection.Left;
                case "right": return SwipeDirection.Right;
                case "up": return SwipeDirection.Up;
                case "down": return SwipeDirection.Down;
                default: throw new ArgumentException($"Unknown swipe direction: '{dir}'. Valid: left, right, up, down");
            }
        }
    }
}
#endif
