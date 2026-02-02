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

        /// <summary>
        /// Converts a SearchQuery to a Search, throwing if invalid or null.
        /// </summary>
        private static Search RequireSearch(SearchQuery query, string actionName)
        {
            if (query == null)
                throw new InvalidOperationException($"{actionName} requires a search query");
            var search = query.ToSearch();
            if (search == null)
                throw new InvalidOperationException($"{actionName} has invalid search query");
            return search;
        }

        /// <summary>
        /// Converts a SearchQuery to a Search, returning null if query is null.
        /// </summary>
        private static Search OptionalSearch(SearchQuery query)
        {
            return query?.ToSearch();
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
                        if (click.ScreenPosition.HasValue)
                            await ActionExecutor.ClickAt(click.ScreenPosition.Value);
                        else
                            await ActionExecutor.Click(RequireSearch(click.Search, "Click"), searchTime: DefaultSearchTime);
                        break;

                    case DoubleClickAction doubleClick:
                        if (doubleClick.ScreenPosition.HasValue)
                        {
                            var pos = doubleClick.ScreenPosition.Value;
                            await ActionExecutor.DoubleClickAt(new Vector2(pos.x * Screen.width, pos.y * Screen.height));
                        }
                        else
                            await ActionExecutor.DoubleClick(RequireSearch(doubleClick.Search, "DoubleClick"), searchTime: DefaultSearchTime);
                        break;

                    case TripleClickAction tripleClick:
                        if (tripleClick.ScreenPosition.HasValue)
                        {
                            var pos = tripleClick.ScreenPosition.Value;
                            await ActionExecutor.TripleClickAt(new Vector2(pos.x * Screen.width, pos.y * Screen.height));
                        }
                        else
                            await ActionExecutor.TripleClick(RequireSearch(tripleClick.Search, "TripleClick"), searchTime: DefaultSearchTime);
                        break;

                    case HoldAction hold:
                        await ActionExecutor.Hold(RequireSearch(hold.Search, "Hold"), hold.Duration, searchTime: DefaultSearchTime);
                        break;

                    case TypeAction type:
                        var typeSearch = OptionalSearch(type.Search);
                        if (typeSearch != null)
                        {
                            await ActionExecutor.Type(typeSearch, type.Text, clearFirst: type.ClearFirst, searchTime: DefaultSearchTime);
                            if (type.PressEnter)
                            {
                                await DelaySafe(50, ct);
                                await ActionExecutor.PressKey(Key.Enter);
                            }
                        }
                        else
                        {
                            await ActionExecutor.TypeText(type.Text);
                            if (type.PressEnter)
                            {
                                await DelaySafe(50, ct);
                                await ActionExecutor.PressKey(Key.Enter);
                            }
                        }
                        break;

                    case DragAction drag:
                        {
                            var fromSearch = RequireSearch(drag.FromSearch, "Drag");
                            var fromResult = await fromSearch.Resolve(DefaultSearchTime);
                            if (!fromResult.Found)
                                throw new InvalidOperationException("Could not find element for 'from' search");
                            var startPos = InputInjector.GetScreenPosition(fromResult.Element);

                            if (drag.ToSearch != null)
                            {
                                var toSearch = drag.ToSearch.ToSearch();
                                if (toSearch == null)
                                    throw new InvalidOperationException("Invalid 'to' search query");
                                var toResult = await toSearch.Resolve(DefaultSearchTime);
                                if (!toResult.Found)
                                    throw new InvalidOperationException("Could not find element for 'to' search");
                                var endPos = InputInjector.GetScreenPosition(toResult.Element);
                                await ActionExecutor.DragFromTo(startPos, endPos, drag.Duration);
                            }
                            else if (!string.IsNullOrEmpty(drag.Direction))
                            {
                                var offset = InputInjector.GetDirectionOffset(drag.Direction, drag.Distance / Screen.height);
                                var endPos = startPos + offset;
                                await ActionExecutor.DragFromTo(startPos, endPos, drag.Duration);
                            }
                            else
                            {
                                throw new InvalidOperationException("Drag action has no 'to' search or direction");
                            }
                        }
                        break;

                    case ScrollAction scroll:
                        {
                            var scrollSearch = RequireSearch(scroll.Search, "Scroll");
                            var result = await scrollSearch.Resolve(DefaultSearchTime);
                            if (!result.Found)
                                throw new InvalidOperationException("Could not find element for scroll");
                            var screenPos = InputInjector.GetScreenPosition(result.Element);
                            float delta = scroll.Direction.ToLower() == "up" ? scroll.Amount : -scroll.Amount;
                            await ActionExecutor.ScrollAt(screenPos, delta);
                        }
                        break;

                    case SwipeAction swipe:
                        {
                            var direction = ParseSwipeDirection(swipe.Direction);
                            await ActionExecutor.Swipe(RequireSearch(swipe.Search, "Swipe"), direction, swipe.Distance, swipe.Duration, throwIfMissing: true, searchTime: DefaultSearchTime);
                        }
                        break;

                    case PinchAction pinch:
                        {
                            var pinchSearch = OptionalSearch(pinch.Search);
                            if (pinchSearch != null)
                                await ActionExecutor.Pinch(pinchSearch, pinch.Scale, pinch.Duration, searchTime: DefaultSearchTime);
                            else
                                await ActionExecutor.Pinch(pinch.Scale, pinch.Duration);
                        }
                        break;

                    case TwoFingerSwipeAction twoFingerSwipe:
                        {
                            var swipeSearch = OptionalSearch(twoFingerSwipe.Search);
                            var direction = twoFingerSwipe.Direction ?? "up";
                            if (swipeSearch != null)
                                await ActionExecutor.TwoFingerSwipe(swipeSearch, direction, twoFingerSwipe.Distance, twoFingerSwipe.Duration, twoFingerSwipe.FingerSpacing, searchTime: DefaultSearchTime);
                            else
                                await ActionExecutor.TwoFingerSwipeAt(new Vector2(Screen.width / 2f, Screen.height / 2f), direction, twoFingerSwipe.Distance, twoFingerSwipe.Duration, twoFingerSwipe.FingerSpacing);
                        }
                        break;

                    case RotateAction rotate:
                        {
                            var rotateSearch = OptionalSearch(rotate.Search);
                            if (rotateSearch != null)
                                await ActionExecutor.Rotate(rotateSearch, rotate.Degrees, rotate.Duration, rotate.FingerDistance, searchTime: DefaultSearchTime);
                            else
                                await ActionExecutor.Rotate(rotate.Degrees, rotate.Duration, rotate.FingerDistance);
                        }
                        break;

                    case SetSliderAction setSlider:
                        await ActionExecutor.SetSlider(RequireSearch(setSlider.Search, "SetSlider"), setSlider.Value, searchTime: DefaultSearchTime);
                        break;

                    case SetScrollbarAction setScrollbar:
                        await ActionExecutor.SetScrollbar(RequireSearch(setScrollbar.Search, "SetScrollbar"), setScrollbar.Value, searchTime: DefaultSearchTime);
                        break;

                    case ClickDropdownAction clickDropdown:
                        {
                            var dropdownSearch = RequireSearch(clickDropdown.Search, "ClickDropdown");
                            if (clickDropdown.OptionIndex >= 0)
                                await ActionExecutor.ClickDropdown(dropdownSearch, clickDropdown.OptionIndex, searchTime: DefaultSearchTime);
                            else if (!string.IsNullOrEmpty(clickDropdown.OptionLabel))
                                await ActionExecutor.ClickDropdown(dropdownSearch, clickDropdown.OptionLabel, searchTime: DefaultSearchTime);
                            else
                                throw new InvalidOperationException("ClickDropdown requires either index or label");
                        }
                        break;

                    case KeyPressAction keyPress:
                        if (Enum.TryParse<Key>(keyPress.Key, true, out var key))
                            await ActionExecutor.PressKey(key);
                        else
                            Debug.LogWarning($"[AITest] Unknown key: {keyPress.Key}");
                        break;

                    case KeyHoldAction keyHold:
                        {
                            var keys = new List<Key>();
                            foreach (var keyName in keyHold.Keys)
                            {
                                if (Enum.TryParse<Key>(keyName.Trim(), true, out var k))
                                    keys.Add(k);
                            }
                            if (keys.Count > 0)
                                await ActionExecutor.HoldKeys(keyHold.Duration, keys.ToArray());
                        }
                        break;

                    case WaitAction wait:
                        await DelaySafe((int)(wait.Seconds * 1000), ct);
                        break;

                    case GetHierarchyAction:
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
