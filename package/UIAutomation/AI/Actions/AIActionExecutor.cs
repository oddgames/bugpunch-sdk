using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;

using UnityEngine;
using UnityEngine.InputSystem;

namespace ODDGames.UIAutomation.AI
{
    using ODDGames.UIAutomation; // For InputInjector, Search, ActionExecutor

    /// <summary>
    /// Parses and executes AI tool calls as UI actions.
    /// Uses Search-based ActionExecutor methods (same as code tests).
    /// </summary>
    public static class AIActionExecutor
    {
        /// <summary>
        /// Default search timeout for AI actions.
        /// </summary>
        public static float DefaultSearchTime { get; set; } = 5f;

        /// <summary>
        /// Parses a tool call into an executable action.
        /// </summary>
        public static AIAction Parse(ToolCall call, ScreenState screen)
        {
            if (call == null)
                throw new ArgumentNullException(nameof(call));

            return call.Name switch
            {
                "click" => ParseClickAction(call),
                "double_click" => ParseDoubleClickAction(call),
                "triple_click" => ParseTripleClickAction(call),
                "hold" => ParseHoldAction(call),
                "type" => ParseTypeAction(call),
                "drag" => ParseDragAction(call),
                "scroll" => ParseScrollAction(call),
                "swipe" => ParseSwipeAction(call),
                "two_finger_swipe" => ParseTwoFingerSwipeAction(call),
                "pinch" => ParsePinchAction(call),
                "rotate" => ParseRotateAction(call),
                "set_slider" => ParseSetSliderAction(call),
                "set_scrollbar" => ParseSetScrollbarAction(call),
                "click_dropdown" => ParseClickDropdownAction(call),
                "key_press" => ParseKeyPressAction(call),
                "key_hold" => ParseKeyHoldAction(call),
                "wait" => ParseWaitAction(call),
                "pass" => new PassAction { Reason = call.GetString("reason") },
                "fail" => new FailAction { Reason = call.GetString("reason", "Test failed") },
                "screenshot" => new ScreenshotAction { Reason = call.GetString("reason", "Need visual clarification") },
                "get_hierarchy" => ParseGetHierarchyAction(call),
                _ => throw new ArgumentException($"Unknown action type: {call.Name}")
            };
        }

        /// <summary>
        /// Gets a SearchQuery from a tool call, throwing if there's a parse error.
        /// Returns null if the key doesn't exist (allows fallback to x/y coordinates).
        /// </summary>
        private static SearchQuery GetSearchQueryOrThrow(ToolCall call, string key)
        {
            // If no search key provided, return null (allow x/y fallback)
            if (!call.Arguments.ContainsKey(key))
            {
                return null;
            }

            var query = call.GetSearchQuery(key, out var error);
            if (error != null)
            {
                throw new ArgumentException($"Invalid '{key}' search query: {error}");
            }
            return query;
        }

        private static ClickAction ParseClickAction(ToolCall call)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            var action = new ClickAction { Search = searchQuery };

            // Check for screen position as fallback
            // x/y can be percentages (0-100) or normalized (0-1)
            var x = call.GetFloat("x", -1f);
            var y = call.GetFloat("y", -1f);

            if (searchQuery == null && x >= 0 && y >= 0)
            {
                // Coordinates are normalized 0-1 with Y=0 at top (visual convention)
                // Unity's input system has Y=0 at bottom, so we flip Y
                y = 1f - y;

                Debug.Log($"[AIActionExecutor] Click at normalized ({x:F3}, {y:F3}) -> screen ({x * Screen.width:F0}, {y * Screen.height:F0})");
                action.ScreenPosition = new Vector2(x, y);
            }

            return action;
        }

        private static TypeAction ParseTypeAction(ToolCall call)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            return new TypeAction
            {
                Search = searchQuery,
                Text = call.GetString("text", ""),
                ClearFirst = call.Arguments.TryGetValue("clear_first", out var clear) ? Convert.ToBoolean(clear) : true,
                PressEnter = call.Arguments.TryGetValue("press_enter", out var enter) && Convert.ToBoolean(enter)
            };
        }

        private static DragAction ParseDragAction(ToolCall call)
        {
            var fromSearch = GetSearchQueryOrThrow(call, "from");
            var toSearch = GetSearchQueryOrThrow(call, "to");
            return new DragAction
            {
                FromSearch = fromSearch,
                ToSearch = toSearch,
                Direction = call.GetString("direction"),
                Distance = call.GetFloat("distance", 200f),
                Duration = call.GetFloat("duration", 0.3f)
            };
        }

        private static ScrollAction ParseScrollAction(ToolCall call)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            return new ScrollAction
            {
                Search = searchQuery,
                Direction = call.GetString("direction", "down"),
                Amount = call.GetFloat("amount", 0.3f)
            };
        }

        private static WaitAction ParseWaitAction(ToolCall call)
        {
            return new WaitAction
            {
                Seconds = Mathf.Clamp(call.GetFloat("seconds", 1f), 0.1f, 10f)
            };
        }

        private static DoubleClickAction ParseDoubleClickAction(ToolCall call)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            var action = new DoubleClickAction { Search = searchQuery };

            var x = call.GetFloat("x", -1f);
            var y = call.GetFloat("y", -1f);

            if (searchQuery == null && x >= 0 && y >= 0)
            {
                // Convert percentages (0-100) to normalized (0-1) if needed
                if (x > 1f || y > 1f)
                {
                    x = x / 100f;
                    y = y / 100f;
                }

                // Flip Y axis: Computer Use has Y=0 at top, Unity has Y=0 at bottom
                y = 1f - y;

                action.ScreenPosition = new Vector2(x, y);
            }

            return action;
        }

        private static TripleClickAction ParseTripleClickAction(ToolCall call)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            var action = new TripleClickAction { Search = searchQuery };

            var x = call.GetFloat("x", -1f);
            var y = call.GetFloat("y", -1f);

            if (searchQuery == null && x >= 0 && y >= 0)
            {
                // Convert percentages (0-100) to normalized (0-1) if needed
                if (x > 1f || y > 1f)
                {
                    x = x / 100f;
                    y = y / 100f;
                }

                // Flip Y axis: Computer Use has Y=0 at top, Unity has Y=0 at bottom
                y = 1f - y;

                action.ScreenPosition = new Vector2(x, y);
            }

            return action;
        }

        private static HoldAction ParseHoldAction(ToolCall call)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            return new HoldAction
            {
                Search = searchQuery,
                Duration = call.GetFloat("duration", 1f)
            };
        }

        private static SwipeAction ParseSwipeAction(ToolCall call)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            return new SwipeAction
            {
                Search = searchQuery,
                Direction = call.GetString("direction", "up"),
                Distance = call.GetFloat("distance", 0.2f),
                Duration = call.GetFloat("duration", 0.3f)
            };
        }

        private static PinchAction ParsePinchAction(ToolCall call)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            return new PinchAction
            {
                Search = searchQuery,
                Scale = call.GetFloat("scale", 1.5f),
                Duration = call.GetFloat("duration", 0.5f)
            };
        }

        private static TwoFingerSwipeAction ParseTwoFingerSwipeAction(ToolCall call)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            return new TwoFingerSwipeAction
            {
                Search = searchQuery,
                Direction = call.GetString("direction", "up"),
                Distance = call.GetFloat("distance", 0.2f),
                Duration = call.GetFloat("duration", 0.3f),
                FingerSpacing = call.GetFloat("finger_spacing", 0.03f)
            };
        }

        private static RotateAction ParseRotateAction(ToolCall call)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            return new RotateAction
            {
                Search = searchQuery,
                Degrees = call.GetFloat("degrees", 90f),
                Duration = call.GetFloat("duration", 0.5f),
                FingerDistance = call.GetFloat("finger_distance", 0.05f)
            };
        }

        private static SetSliderAction ParseSetSliderAction(ToolCall call)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            return new SetSliderAction
            {
                Search = searchQuery,
                Value = Mathf.Clamp01(call.GetFloat("value", 0.5f))
            };
        }

        private static SetScrollbarAction ParseSetScrollbarAction(ToolCall call)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            return new SetScrollbarAction
            {
                Search = searchQuery,
                Value = Mathf.Clamp01(call.GetFloat("value", 0.5f))
            };
        }

        private static ClickDropdownAction ParseClickDropdownAction(ToolCall call)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            return new ClickDropdownAction
            {
                Search = searchQuery,
                OptionIndex = call.GetInt("index", -1),
                OptionLabel = call.GetString("label", null)
            };
        }

        private static KeyPressAction ParseKeyPressAction(ToolCall call)
        {
            return new KeyPressAction
            {
                Key = call.GetString("key", "Enter")
            };
        }

        private static KeyHoldAction ParseKeyHoldAction(ToolCall call)
        {
            var keysArg = call.Arguments.TryGetValue("keys", out var keysObj) ? keysObj : null;
            string[] keys;

            if (keysArg is System.Collections.IList list)
            {
                keys = new string[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    keys[i] = list[i]?.ToString() ?? "";
                }
            }
            else if (keysArg is string s)
            {
                keys = s.Split(',');
            }
            else
            {
                keys = new[] { "W" };
            }

            return new KeyHoldAction
            {
                Keys = keys,
                Duration = call.GetFloat("duration", 0.5f)
            };
        }

        private static GetHierarchyAction ParseGetHierarchyAction(ToolCall call)
        {
            string[] typeFilter = null;
            if (call.Arguments.TryGetValue("type_filter", out var filterObj))
            {
                if (filterObj is System.Collections.IList list)
                {
                    typeFilter = new string[list.Count];
                    for (int i = 0; i < list.Count; i++)
                    {
                        typeFilter[i] = list[i]?.ToString() ?? "";
                    }
                }
                else if (filterObj is string s)
                {
                    typeFilter = s.Split(',');
                }
            }

            return new GetHierarchyAction
            {
                RootName = call.GetString("root_name"),
                MaxDepth = Math.Min(call.GetInt("max_depth", 10), 20),
                IncludeInactive = call.Arguments.TryGetValue("include_inactive", out var inactive) && Convert.ToBoolean(inactive),
                TypeFilter = typeFilter
            };
        }

        /// <summary>
        /// Executes an AI action using Search-based ActionExecutor methods.
        /// Same approach as code-based tests.
        /// </summary>
        public static async Task<ActionResult> ExecuteAsync(AIAction action, CancellationToken ct = default)
        {
            var startTime = Time.realtimeSinceStartup;

            // Capture element state hash before action for change detection
            string hashBefore = null;
            try
            {
                hashBefore = ScreenHash.ComputeElementStateHash(ElementDiscovery.DiscoverElements());
            }
            catch
            {
                // Non-critical - just won't have before hash
            }

            try
            {
                switch (action)
                {
                    case ClickAction click:
                        await ExecuteClickAsync(click, ct);
                        break;

                    case TypeAction type:
                        await ExecuteTypeAsync(type, ct);
                        break;

                    case DragAction drag:
                        await ExecuteDragAsync(drag, ct);
                        break;

                    case ScrollAction scroll:
                        await ExecuteScrollAsync(scroll, ct);
                        break;

                    case WaitAction wait:
                        await DelaySafe((int)(wait.Seconds * 1000), ct);
                        break;

                    case DoubleClickAction doubleClick:
                        await ExecuteDoubleClickAsync(doubleClick, ct);
                        break;

                    case TripleClickAction tripleClick:
                        await ExecuteTripleClickAsync(tripleClick, ct);
                        break;

                    case HoldAction hold:
                        await ExecuteHoldAsync(hold, ct);
                        break;

                    case SwipeAction swipe:
                        await ExecuteSwipeAsync(swipe, ct);
                        break;

                    case PinchAction pinch:
                        await ExecutePinchAsync(pinch, ct);
                        break;

                    case TwoFingerSwipeAction twoFingerSwipe:
                        await ExecuteTwoFingerSwipeAsync(twoFingerSwipe, ct);
                        break;

                    case RotateAction rotate:
                        await ExecuteRotateAsync(rotate, ct);
                        break;

                    case SetSliderAction setSlider:
                        await ExecuteSetSliderAsync(setSlider, ct);
                        break;

                    case SetScrollbarAction setScrollbar:
                        await ExecuteSetScrollbarAsync(setScrollbar, ct);
                        break;

                    case ClickDropdownAction clickDropdown:
                        await ExecuteClickDropdownAsync(clickDropdown, ct);
                        break;

                    case KeyPressAction keyPress:
                        await ExecuteKeyPressAsync(keyPress, ct);
                        break;

                    case KeyHoldAction keyHold:
                        await ExecuteKeyHoldAsync(keyHold, ct);
                        break;

                    case PassAction:
                    case FailAction:
                    case ScreenshotAction:
                        // These are special actions handled by the runner, no execution needed here
                        break;

                    default:
                        return ActionResult.Failed($"Unknown action type: {action?.GetType().Name}");
                }

                // Brief delay after action for UI to update
                await DelaySafe(50, ct);

                // Capture element state hash after action
                string hashAfter = null;
                bool screenChanged = false;
                try
                {
                    hashAfter = ScreenHash.ComputeElementStateHash(ElementDiscovery.DiscoverElements());
                    screenChanged = hashBefore != null && hashAfter != null && hashBefore != hashAfter;
                }
                catch
                {
                    // Non-critical
                }

                return new ActionResult
                {
                    Success = true,
                    ExecutionTimeMs = (Time.realtimeSinceStartup - startTime) * 1000f,
                    ScreenHashAfter = hashAfter,
                    ScreenChanged = screenChanged
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ActionResult.Failed(ex.Message);
            }
        }

        private static async Task ExecuteClickAsync(ClickAction action, CancellationToken ct)
        {
            if (action.ScreenPosition.HasValue)
            {
                // ScreenPosition is normalized (0-1)
                await ActionExecutor.ClickAt(action.ScreenPosition.Value);
            }
            else if (action.Search != null)
            {
                // Use Search-based Click - same as code tests
                var search = action.Search.ToSearch();
                if (search == null)
                    throw new InvalidOperationException("Invalid search query");
                await ActionExecutor.Click(search, searchTime: DefaultSearchTime);
            }
            else
            {
                throw new InvalidOperationException("Click action has no search or screen position");
            }
        }

        private static async Task ExecuteTypeAsync(TypeAction action, CancellationToken ct)
        {
            if (action.Search != null)
            {
                // Use Search-based Type - same as code tests
                var search = action.Search.ToSearch();
                if (search == null)
                    throw new InvalidOperationException("Invalid search query");

                await ActionExecutor.Type(
                    search,
                    action.Text,
                    clearFirst: action.ClearFirst,
                    searchTime: DefaultSearchTime);

                if (action.PressEnter)
                {
                    await DelaySafe(50, ct);
                    await ActionExecutor.PressKey(Key.Enter);
                }
            }
            else
            {
                // No target, just type text directly
                await ActionExecutor.TypeText(action.Text);
                if (action.PressEnter)
                {
                    await DelaySafe(50, ct);
                    await ActionExecutor.PressKey(Key.Enter);
                }
            }
        }

        private static async Task ExecuteDragAsync(DragAction action, CancellationToken ct)
        {
            if (action.FromSearch == null)
                throw new InvalidOperationException("Drag action has no 'from' search");

            var fromSearch = action.FromSearch.ToSearch();
            if (fromSearch == null)
                throw new InvalidOperationException("Invalid 'from' search query");

            // Resolve the from element
            var fromResult = await fromSearch.Resolve(DefaultSearchTime);
            if (!fromResult.Found)
                throw new InvalidOperationException($"Could not find element for 'from' search");

            var startPos = InputInjector.GetScreenPosition(fromResult.Element);

            if (action.ToSearch != null)
            {
                var toSearch = action.ToSearch.ToSearch();
                if (toSearch == null)
                    throw new InvalidOperationException("Invalid 'to' search query");

                var toResult = await toSearch.Resolve(DefaultSearchTime);
                if (!toResult.Found)
                    throw new InvalidOperationException($"Could not find element for 'to' search");

                var endPos = InputInjector.GetScreenPosition(toResult.Element);
                await ActionExecutor.DragFromTo(startPos, endPos, action.Duration);
            }
            else if (!string.IsNullOrEmpty(action.Direction))
            {
                var offset = InputInjector.GetDirectionOffset(action.Direction, action.Distance / Screen.height);
                var endPos = startPos + offset;
                await ActionExecutor.DragFromTo(startPos, endPos, action.Duration);
            }
            else
            {
                throw new InvalidOperationException("Drag action has no 'to' search or direction");
            }
        }

        private static async Task ExecuteScrollAsync(ScrollAction action, CancellationToken ct)
        {
            if (action.Search == null)
                throw new InvalidOperationException("Scroll action has no search");

            var search = action.Search.ToSearch();
            if (search == null)
                throw new InvalidOperationException("Invalid search query");

            var result = await search.Resolve(DefaultSearchTime);
            if (!result.Found)
                throw new InvalidOperationException($"Could not find element for scroll");

            var screenPos = InputInjector.GetScreenPosition(result.Element);
            float delta = action.Direction.ToLower() switch
            {
                "up" => action.Amount,
                "down" => -action.Amount,
                _ => 0
            };
            await ActionExecutor.ScrollAt(screenPos, delta);
        }

        private static async Task ExecuteDoubleClickAsync(DoubleClickAction action, CancellationToken ct)
        {
            if (action.ScreenPosition.HasValue)
            {
                var screenPos = new Vector2(
                    action.ScreenPosition.Value.x * Screen.width,
                    action.ScreenPosition.Value.y * Screen.height
                );
                await ActionExecutor.DoubleClickAt(screenPos);
            }
            else if (action.Search != null)
            {
                var search = action.Search.ToSearch();
                if (search == null)
                    throw new InvalidOperationException("Invalid search query");
                await ActionExecutor.DoubleClick(search, searchTime: DefaultSearchTime);
            }
            else
            {
                throw new InvalidOperationException("Double-click action has no search or screen position");
            }
        }

        private static async Task ExecuteTripleClickAsync(TripleClickAction action, CancellationToken ct)
        {
            if (action.ScreenPosition.HasValue)
            {
                var screenPos = new Vector2(
                    action.ScreenPosition.Value.x * Screen.width,
                    action.ScreenPosition.Value.y * Screen.height
                );
                await ActionExecutor.TripleClickAt(screenPos);
            }
            else if (action.Search != null)
            {
                var search = action.Search.ToSearch();
                if (search == null)
                    throw new InvalidOperationException("Invalid search query");
                await ActionExecutor.TripleClick(search, searchTime: DefaultSearchTime);
            }
            else
            {
                throw new InvalidOperationException("Triple-click action has no search or screen position");
            }
        }

        private static async Task ExecuteHoldAsync(HoldAction action, CancellationToken ct)
        {
            if (action.Search == null)
                throw new InvalidOperationException("Hold action has no search");

            var search = action.Search.ToSearch();
            if (search == null)
                throw new InvalidOperationException("Invalid search query");

            await ActionExecutor.Hold(search, action.Duration, searchTime: DefaultSearchTime);
        }

        private static async Task ExecuteSwipeAsync(SwipeAction action, CancellationToken ct)
        {
            if (action.Search == null)
                throw new InvalidOperationException("Swipe action has no search");

            var search = action.Search.ToSearch();
            if (search == null)
                throw new InvalidOperationException("Invalid search query");

            var direction = ParseSwipeDirection(action.Direction);
            await ActionExecutor.Swipe(search, direction, action.Distance, action.Duration, throwIfMissing: true, searchTime: DefaultSearchTime);
        }

        private static SwipeDirection ParseSwipeDirection(string direction)
        {
            return direction?.ToLower() switch
            {
                "up" => SwipeDirection.Up,
                "down" => SwipeDirection.Down,
                "left" => SwipeDirection.Left,
                "right" => SwipeDirection.Right,
                _ => SwipeDirection.Up
            };
        }

        private static async Task ExecutePinchAsync(PinchAction action, CancellationToken ct)
        {
            if (action.Search != null)
            {
                var search = action.Search.ToSearch();
                if (search == null)
                    throw new InvalidOperationException("Invalid search query");

                // No Search-based Pinch, so resolve element and use PinchAt
                var result = await search.Resolve(DefaultSearchTime);
                if (!result.Found)
                    throw new InvalidOperationException("Could not find element for pinch");

                var screenPos = InputInjector.GetScreenPosition(result.Element);
                await ActionExecutor.PinchAt(screenPos, action.Scale, action.Duration);
            }
            else
            {
                // No search - pinch at screen center
                var centerPos = new Vector2(Screen.width / 2f, Screen.height / 2f);
                await ActionExecutor.PinchAt(centerPos, action.Scale, action.Duration);
            }
        }

        private static async Task ExecuteTwoFingerSwipeAsync(TwoFingerSwipeAction action, CancellationToken ct)
        {
            // TwoFingerSwipeAt(Vector2, ...) takes string direction
            var direction = action.Direction ?? "up";

            if (action.Search != null)
            {
                var search = action.Search.ToSearch();
                if (search == null)
                    throw new InvalidOperationException("Invalid search query");

                // No Search-based TwoFingerSwipe, so resolve element and use TwoFingerSwipeAt
                var result = await search.Resolve(DefaultSearchTime);
                if (!result.Found)
                    throw new InvalidOperationException("Could not find element for two-finger swipe");

                var screenPos = InputInjector.GetScreenPosition(result.Element);
                await ActionExecutor.TwoFingerSwipeAt(
                    screenPos,
                    direction,
                    action.Distance,
                    action.Duration,
                    action.FingerSpacing);
            }
            else
            {
                // No search - swipe at screen center
                var centerPos = new Vector2(Screen.width / 2f, Screen.height / 2f);
                await ActionExecutor.TwoFingerSwipeAt(
                    centerPos,
                    direction,
                    action.Distance,
                    action.Duration,
                    action.FingerSpacing);
            }
        }

        private static async Task ExecuteRotateAsync(RotateAction action, CancellationToken ct)
        {
            if (action.Search != null)
            {
                var search = action.Search.ToSearch();
                if (search == null)
                    throw new InvalidOperationException("Invalid search query");

                // No Search-based Rotate, so resolve element and use RotateAt
                var result = await search.Resolve(DefaultSearchTime);
                if (!result.Found)
                    throw new InvalidOperationException("Could not find element for rotate");

                var screenPos = InputInjector.GetScreenPosition(result.Element);
                await ActionExecutor.RotateAt(
                    screenPos,
                    action.Degrees,
                    action.Duration,
                    action.FingerDistance);
            }
            else
            {
                // No search - rotate at screen center
                var centerPos = new Vector2(Screen.width / 2f, Screen.height / 2f);
                await ActionExecutor.RotateAt(
                    centerPos,
                    action.Degrees,
                    action.Duration,
                    action.FingerDistance);
            }
        }

        private static async Task ExecuteSetSliderAsync(SetSliderAction action, CancellationToken ct)
        {
            if (action.Search == null)
                throw new InvalidOperationException("SetSlider action has no search");

            var search = action.Search.ToSearch();
            if (search == null)
                throw new InvalidOperationException("Invalid search query");

            await ActionExecutor.SetSlider(search, action.Value, searchTime: DefaultSearchTime);
        }

        private static async Task ExecuteSetScrollbarAsync(SetScrollbarAction action, CancellationToken ct)
        {
            if (action.Search == null)
                throw new InvalidOperationException("SetScrollbar action has no search");

            var search = action.Search.ToSearch();
            if (search == null)
                throw new InvalidOperationException("Invalid search query");

            await ActionExecutor.SetScrollbar(search, action.Value, searchTime: DefaultSearchTime);
        }

        private static async Task ExecuteClickDropdownAsync(ClickDropdownAction action, CancellationToken ct)
        {
            if (action.Search == null)
                throw new InvalidOperationException("ClickDropdown action has no search");

            var search = action.Search.ToSearch();
            if (search == null)
                throw new InvalidOperationException("Invalid search query");

            bool found;
            if (action.OptionIndex >= 0)
            {
                found = await ActionExecutor.ClickDropdown(search, action.OptionIndex, searchTime: DefaultSearchTime);
            }
            else if (!string.IsNullOrEmpty(action.OptionLabel))
            {
                found = await ActionExecutor.ClickDropdown(search, action.OptionLabel, searchTime: DefaultSearchTime);
            }
            else
            {
                throw new InvalidOperationException("ClickDropdown requires either index or label");
            }

            if (!found)
            {
                throw new InvalidOperationException($"Dropdown option not found");
            }
        }

        private static async Task ExecuteKeyPressAsync(KeyPressAction action, CancellationToken ct)
        {
            if (Enum.TryParse<Key>(action.Key, true, out var key))
            {
                await ActionExecutor.PressKey(key);
            }
            else
            {
                Debug.LogWarning($"[AITest] Unknown key: {action.Key}");
            }
        }

        private static async Task ExecuteKeyHoldAsync(KeyHoldAction action, CancellationToken ct)
        {
            var keys = new List<Key>();
            foreach (var keyName in action.Keys)
            {
                if (Enum.TryParse<Key>(keyName.Trim(), true, out var key))
                {
                    keys.Add(key);
                }
                else
                {
                    Debug.LogWarning($"[AITest] Unknown key: {keyName}");
                }
            }

            if (keys.Count > 0)
            {
                await ActionExecutor.HoldKeys(action.Duration, keys.ToArray());
            }
        }

        /// <summary>
        /// Safe delay that won't hang when exiting play mode.
        /// </summary>
        private static async Task DelaySafe(int milliseconds, CancellationToken ct)
        {
            if (!Application.isPlaying || ct.IsCancellationRequested)
                return;

            try
            {
                await Task.Delay(milliseconds, ct);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling
            }
            catch
            {
                // Unity shutting down
            }
        }
    }
}
