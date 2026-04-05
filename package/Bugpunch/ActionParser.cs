#if UNITY_INCLUDE_TESTS
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ODDGames.Bugpunch
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
                case "keys":
                    await ActionExecutor.PressKeys(obj.Value<string>("text") ?? obj.Value<string>("value") ?? "");
                    break;
                case "holdkey":
                    await ExecuteHoldKey(obj);
                    break;
                case "holdkeys":
                    await ExecuteHoldKeys(obj);
                    break;
                case "typetext":
                    await ActionExecutor.TypeText(obj.Value<string>("text") ?? obj.Value<string>("value") ?? "");
                    break;
                case "twofingerswipe":
                    await ExecuteTwoFingerSwipe(obj);
                    break;
                case "waitfps":
                    await ActionExecutor.WaitForStableFrameRate(
                        obj.Value<float?>("minFps") ?? 20f,
                        obj.Value<int?>("stableFrames") ?? 5,
                        obj.Value<float?>("timeout") ?? 10f);
                    break;
                case "waitframerate":
                    await ActionExecutor.WaitFramerate(
                        obj.Value<int?>("fps") ?? 30,
                        obj.Value<float?>("sampleDuration") ?? 2f,
                        obj.Value<float?>("timeout") ?? 60f);
                    break;
                case "scenechange":
                    await ActionExecutor.SceneChange(obj.Value<float?>("seconds") ?? 30f);
                    break;
                case "slider":
                    await ExecuteSlider(obj);
                    break;
                case "scrollbar":
                    await ExecuteScrollbar(obj);
                    break;
                case "waitfor":
                    await ExecuteWaitFor(obj);
                    break;
                case "waitfornot":
                    await ActionExecutor.WaitForNot(BuildSearch(obj), obj.Value<float?>("seconds") ?? 10f);
                    break;
                case "snapshot":
                    await ActionExecutor.Snapshot();
                    break;
                case "screenshot":
                    // No-op — screenshot is taken automatically by Execute()
                    break;
                case "clickslider":
                    await ExecuteClickSlider(obj);
                    break;
                case "scrolltoandclick":
                    await ExecuteScrollToAndClick(obj);
                    break;
                case "randomclick":
                    await ExecuteRandomClick(obj);
                    break;
                case "autoexplore":
                    await ExecuteAutoExplore(obj);
                    break;
                case "enable":
                    await ActionExecutor.Enable(BuildSearch(obj));
                    break;
                case "disable":
                    await ActionExecutor.Disable(BuildSearch(obj));
                    break;
                case "freeze":
                    await ActionExecutor.Freeze(BuildSearch(obj), obj.Value<bool?>("includeChildren") ?? true);
                    break;
                case "teleport":
                    await ExecuteTeleport(obj);
                    break;
                case "noclip":
                    await ActionExecutor.NoClip(BuildSearch(obj), obj.Value<bool?>("includeChildren") ?? true);
                    break;
                case "clip":
                    await ActionExecutor.Clip(BuildSearch(obj), obj.Value<bool?>("includeChildren") ?? true);
                    break;
                case "getvalue":
                    await ExecuteGetValue(obj);
                    break;
                case "exists":
                    await ExecuteExists(obj);
                    break;
                default:
                    throw new ArgumentException(
                        $"Unknown action: '{action}'. Valid: click, doubleclick, tripleclick, type, textinput, typetext, " +
                        "swipe, twofingerswipe, scroll, scrollto, scrolltoandclick, wait, hold, drag, dropdown, pinch, rotate, " +
                        "key, keys, holdkey, holdkeys, slider, clickslider, scrollbar, waitfor, waitfornot, waitfps, " +
                        "waitframerate, scenechange, snapshot, screenshot, randomclick, autoexplore, " +
                        "enable, disable, freeze, teleport, noclip, clip, getvalue, exists");
            }
        }

        private static async Task ExecuteClick(JObject obj)
        {
            var at = obj["at"] as JArray;
            if (at != null && at.Count == 2)
                await ActionExecutor.ClickAt(new Vector2(at[0].Value<float>(), at[1].Value<float>()));
            else
                await ActionExecutor.Click(BuildSearch(obj), index: obj.Value<int?>("index") ?? 0);
        }

        private static async Task ExecuteDoubleClick(JObject obj)
        {
            var at = obj["at"] as JArray;
            if (at != null && at.Count == 2)
                await ActionExecutor.DoubleClickAt(new Vector2(at[0].Value<float>(), at[1].Value<float>()));
            else
                await ActionExecutor.DoubleClick(BuildSearch(obj), index: obj.Value<int?>("index") ?? 0);
        }

        private static async Task ExecuteTripleClick(JObject obj)
        {
            var at = obj["at"] as JArray;
            if (at != null && at.Count == 2)
                await ActionExecutor.TripleClickAt(new Vector2(at[0].Value<float>(), at[1].Value<float>()));
            else
                await ActionExecutor.TripleClick(BuildSearch(obj), index: obj.Value<int?>("index") ?? 0);
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

            var at = obj["at"] as JArray;
            if (at != null && at.Count == 2)
            {
                await ActionExecutor.SwipeAt(at[0].Value<float>(), at[1].Value<float>(), dir, distance, duration);
            }
            else
            {
                var search = TryBuildSearch(obj);
                if (search != null)
                    await ActionExecutor.Swipe(search, dir, distance, duration);
                else
                    await ActionExecutor.Swipe(dir, distance, duration);
            }
        }

        private static async Task ExecuteScroll(JObject obj)
        {
            var search = TryBuildSearch(obj);
            string direction = obj.Value<string>("direction");

            if (direction != null && search != null)
            {
                float amount = obj.Value<float?>("amount") ?? 0.3f;
                await ActionExecutor.Scroll(search, direction, amount);
            }
            else
            {
                float delta = obj.Value<float?>("delta") ?? -120f;
                var at = obj["at"] as JArray;
                if (at != null && at.Count == 2)
                    await ActionExecutor.ScrollAt(new Vector2(at[0].Value<float>(), at[1].Value<float>()), delta);
                else if (search != null)
                    await ActionExecutor.Scroll(search, delta);
                else
                    await ActionExecutor.ScrollAt(new Vector2(0.5f, 0.5f), delta);
            }
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
                await ActionExecutor.Hold(BuildSearch(obj), seconds, index: obj.Value<int?>("index") ?? 0);
        }

        private static async Task ExecuteDrag(JObject obj)
        {
            float duration = obj.Value<float?>("duration") ?? 0.15f;
            float holdTime = obj.Value<float?>("holdTime") ?? 0.05f;
            var button = ParsePointerButton(obj.Value<string>("button"));

            // Direction-based drag on a search target
            var dirArray = obj["direction"] as JArray;
            if (dirArray != null && dirArray.Count == 2)
            {
                var direction = new Vector2(dirArray[0].Value<float>(), dirArray[1].Value<float>());
                var search = TryBuildSearch(obj);
                if (search != null)
                    await ActionExecutor.Drag(search, direction, duration, holdTime: holdTime, button: button);
                else
                    await ActionExecutor.Drag(direction, duration, holdTime);
                return;
            }

            var fromObj = obj["from"] as JObject;
            var toObj = obj["to"] as JObject;
            if (fromObj == null || toObj == null)
                throw new ArgumentException("drag requires 'from'+'to' objects, or 'direction':[x,y]");

            // Position-based drag (from.at / to.at)
            var fromAt = fromObj["at"] as JArray;
            var toAt = toObj["at"] as JArray;
            if (fromAt != null && fromAt.Count == 2 && toAt != null && toAt.Count == 2)
            {
                var start = new Vector2(fromAt[0].Value<float>(), fromAt[1].Value<float>());
                var end = new Vector2(toAt[0].Value<float>(), toAt[1].Value<float>());
                await ActionExecutor.DragFromTo(start, end, duration, holdTime, button);
            }
            else
            {
                await ActionExecutor.DragTo(BuildSearch(fromObj), BuildSearch(toObj), duration, holdTime: holdTime, button: button);
            }
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

            var at = obj["at"] as JArray;
            if (at != null && at.Count == 2)
            {
                await ActionExecutor.PinchAt(at[0].Value<float>(), at[1].Value<float>(), scale, duration);
            }
            else
            {
                var search = TryBuildSearch(obj);
                if (search != null)
                    await ActionExecutor.Pinch(search, scale, duration);
                else
                    await ActionExecutor.Pinch(scale, duration);
            }
        }

        private static async Task ExecuteRotate(JObject obj)
        {
            float degrees = obj.Value<float?>("degrees") ?? 90f;
            float duration = obj.Value<float?>("duration") ?? 0.15f;

            var at = obj["at"] as JArray;
            if (at != null && at.Count == 2)
            {
                float fingerDistance = obj.Value<float?>("fingerDistance") ?? 0.05f;
                await ActionExecutor.RotateAt(at[0].Value<float>(), at[1].Value<float>(), degrees, duration, fingerDistance);
            }
            else
            {
                var search = TryBuildSearch(obj);
                if (search != null)
                    await ActionExecutor.Rotate(search, degrees, duration);
                else
                    await ActionExecutor.Rotate(degrees, duration);
            }
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
                search = ActionExecutor.Near(near, ParseDirection(obj.Value<string>("direction")));

            string adjacent = obj.Value<string>("adjacent");
            if (adjacent != null)
                search = ActionExecutor.Adjacent(adjacent, ParseDirection(obj.Value<string>("direction")) ?? Direction.Right);

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

        private static async Task ExecuteSlider(JObject obj)
        {
            var search = BuildSearch(obj);
            float value = obj.Value<float?>("value")
                ?? throw new ArgumentException("slider requires 'value' (0-1 normalized)");

            // If from is specified, drag from->to; otherwise click at position
            var from = obj.Value<float?>("from");
            if (from.HasValue)
                await ActionExecutor.DragSlider(search, from.Value, value);
            else
                await ActionExecutor.SetSlider(search, value);
        }

        private static async Task ExecuteScrollbar(JObject obj)
        {
            var search = BuildSearch(obj);
            float value = obj.Value<float?>("value")
                ?? throw new ArgumentException("scrollbar requires 'value' (0-1 normalized)");
            await ActionExecutor.SetScrollbar(search, value);
        }

        private static async Task ExecuteWaitFor(JObject obj)
        {
            var search = BuildSearch(obj);
            float seconds = obj.Value<float?>("seconds") ?? 10f;
            string expectedText = obj.Value<string>("expected");

            if (expectedText != null)
                await ActionExecutor.WaitFor(search, expectedText, seconds);
            else
                await ActionExecutor.WaitFor(search, seconds);
        }

        private static async Task ExecuteClickSlider(JObject obj)
        {
            var search = BuildSearch(obj);
            float value = obj.Value<float?>("value")
                ?? throw new ArgumentException("clickslider requires 'value' (0-1 normalized)");
            await ActionExecutor.ClickSlider(search, value);
        }

        private static async Task ExecuteScrollToAndClick(JObject obj)
        {
            var container = BuildSearch(obj);
            var targetObj = obj["target"] as JObject
                ?? throw new ArgumentException("scrolltoandclick requires a 'target' object");
            var target = BuildSearch(targetObj);
            await ActionExecutor.ScrollToAndClick(container, target);
        }

        private static async Task ExecuteRandomClick(JObject obj)
        {
            var filter = TryBuildSearch(obj);
            await ActionExecutor.RandomClick(filter);
        }

        private static async Task ExecuteAutoExplore(JObject obj)
        {
            string mode = obj.Value<string>("mode")?.ToLowerInvariant() ?? "time";
            int? seed = obj.Value<int?>("seed");
            float delay = obj.Value<float?>("delay") ?? 0.5f;

            switch (mode)
            {
                case "time":
                    float seconds = obj.Value<float?>("seconds") ?? obj.Value<float?>("value") ?? 10f;
                    await ActionExecutor.AutoExploreForSeconds(seconds, seed, delay);
                    break;
                case "actions":
                    int count = obj.Value<int?>("count") ?? obj.Value<int?>("value") ?? 10;
                    await ActionExecutor.AutoExploreForActions(count, seed, delay);
                    break;
                case "deadend":
                    bool tryBack = obj.Value<bool?>("tryBack") ?? false;
                    await ActionExecutor.AutoExploreUntilDeadEnd(seed, delay, tryBack);
                    break;
                default:
                    throw new ArgumentException($"Unknown autoexplore mode: '{mode}'. Valid: time, actions, deadend");
            }
        }

        private static async Task ExecuteTeleport(JObject obj)
        {
            var search = BuildSearch(obj);
            var pos = obj["position"] as JArray
                ?? throw new ArgumentException("teleport requires 'position' array [x, y, z]");
            if (pos.Count < 3)
                throw new ArgumentException("teleport 'position' requires 3 values [x, y, z]");
            var worldPos = new Vector3(pos[0].Value<float>(), pos[1].Value<float>(), pos[2].Value<float>());
            await ActionExecutor.Teleport(search, worldPos);
        }

        private static Task ExecuteGetValue(JObject obj)
        {
            // Static path access: {"action":"getvalue", "path":"GameManager.Instance.Score"}
            string path = obj.Value<string>("path");
            if (path != null)
            {
                var value = ActionExecutor.GetValue<object>(path);
                // Store result as action metadata — the bridge response carries it
                UnityEngine.Debug.Log($"[GetValue] {path} = {value}");
                return Task.CompletedTask;
            }

            throw new ArgumentException("getvalue requires 'path' (static reflection path)");
        }

        private static async Task ExecuteExists(JObject obj)
        {
            var search = BuildSearch(obj);
            float timeout = obj.Value<float?>("seconds") ?? 1f;
            bool found = await search.Exists(timeout);
            if (!found && (obj.Value<bool?>("required") ?? false))
                throw new InvalidOperationException($"exists: element not found — {search}");
        }

        private static PointerButton ParsePointerButton(string button)
        {
            if (button == null) return PointerButton.Left;
            return button.ToLowerInvariant() switch
            {
                "left" => PointerButton.Left,
                "right" => PointerButton.Right,
                "middle" => PointerButton.Middle,
                _ => PointerButton.Left
            };
        }

        private static Direction? ParseDirection(string dir)
        {
            if (dir == null) return null;
            return dir.ToLowerInvariant() switch
            {
                "right" => Direction.Right,
                "left" => Direction.Left,
                "below" or "down" => Direction.Below,
                "above" or "up" => Direction.Above,
                _ => null
            };
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

        private static KeyCode ParseKeyCode(string keyName)
        {
            if (string.IsNullOrEmpty(keyName))
                throw new ArgumentException("Key name is required");

            // Try common aliases first
            var mapped = keyName.ToLowerInvariant() switch
            {
                "enter" => KeyCode.Return,
                "esc" => KeyCode.Escape,
                "up" => KeyCode.UpArrow,
                "down" => KeyCode.DownArrow,
                "left" => KeyCode.LeftArrow,
                "right" => KeyCode.RightArrow,
                "bs" or "backspace" => KeyCode.Backspace,
                "del" => KeyCode.Delete,
                "space" => KeyCode.Space,
                "tab" => KeyCode.Tab,
                "shift" => KeyCode.LeftShift,
                "ctrl" or "control" => KeyCode.LeftControl,
                "alt" => KeyCode.LeftAlt,
                _ => KeyCode.None
            };
            if (mapped != KeyCode.None)
                return mapped;

            // Single character
            if (keyName.Length == 1)
            {
                char c = char.ToUpper(keyName[0]);
                if (c >= 'A' && c <= 'Z')
                    return (KeyCode)c;
                if (c >= '0' && c <= '9')
                    return KeyCode.Alpha0 + (c - '0');
            }

            // Try direct enum parse
            if (Enum.TryParse<KeyCode>(keyName, true, out var keyCode))
                return keyCode;

            throw new ArgumentException($"Unknown key: '{keyName}'");
        }

        private static async Task ExecuteHoldKey(JObject obj)
        {
            string keyName = obj.Value<string>("key")
                ?? throw new ArgumentException("holdkey requires 'key'");
            float duration = obj.Value<float?>("duration") ?? obj.Value<float?>("seconds") ?? 1f;

            var keyCode = ParseKeyCode(keyName);
            var key = ActionExecutor.KeyCodeToKey(keyCode);
            if (key != UnityEngine.InputSystem.Key.None)
                await ActionExecutor.HoldKey(key, duration);
        }

        private static async Task ExecuteHoldKeys(JObject obj)
        {
            var keysArray = obj["keys"] as JArray
                ?? throw new ArgumentException("holdkeys requires 'keys' array");
            float duration = obj.Value<float?>("duration") ?? obj.Value<float?>("seconds") ?? 1f;

            var keys = new System.Collections.Generic.List<UnityEngine.InputSystem.Key>();
            foreach (var k in keysArray)
            {
                var keyCode = ParseKeyCode(k.Value<string>());
                var key = ActionExecutor.KeyCodeToKey(keyCode);
                if (key != UnityEngine.InputSystem.Key.None)
                    keys.Add(key);
            }

            if (keys.Count > 0)
                await ActionExecutor.HoldKeys(duration, keys.ToArray());
        }

        private static async Task ExecuteTwoFingerSwipe(JObject obj)
        {
            var dir = ParseSwipeDirection(obj.Value<string>("direction") ?? "up");
            float distance = obj.Value<float?>("distance") ?? 0.2f;
            float duration = obj.Value<float?>("duration") ?? 0.15f;
            float fingerSpacing = obj.Value<float?>("fingerSpacing") ?? 0.03f;

            var at = obj["at"] as JArray;
            if (at != null && at.Count == 2)
            {
                await ActionExecutor.TwoFingerSwipeAt(at[0].Value<float>(), at[1].Value<float>(), dir, distance, duration, fingerSpacing);
            }
            else
            {
                var search = TryBuildSearch(obj);
                if (search != null)
                    await ActionExecutor.TwoFingerSwipe(search, dir.ToString().ToLower(), distance, duration, fingerSpacing);
                else
                    await ActionExecutor.TwoFingerSwipe(dir, distance, duration, fingerSpacing);
            }
        }
    }
}
#endif
