using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ODDGames.UITest.AI
{
    using ODDGames.UITest; // For InputInjector, Search

    /// <summary>
    /// Parses and executes AI tool calls as UI actions.
    /// </summary>
    public static class AIActionExecutor
    {
        /// <summary>
        /// Parses a tool call into an executable action.
        /// </summary>
        public static AIAction Parse(ToolCall call, ScreenState screen)
        {
            if (call == null)
                throw new ArgumentNullException(nameof(call));

            return call.Name switch
            {
                "click" => ParseClickAction(call, screen),
                "double_click" => ParseDoubleClickAction(call, screen),
                "triple_click" => ParseTripleClickAction(call, screen),
                "hold" => ParseHoldAction(call, screen),
                "type" => ParseTypeAction(call, screen),
                "drag" => ParseDragAction(call, screen),
                "scroll" => ParseScrollAction(call, screen),
                "swipe" => ParseSwipeAction(call, screen),
                "two_finger_swipe" => ParseTwoFingerSwipeAction(call, screen),
                "pinch" => ParsePinchAction(call, screen),
                "rotate" => ParseRotateAction(call, screen),
                "set_slider" => ParseSetSliderAction(call, screen),
                "set_scrollbar" => ParseSetScrollbarAction(call, screen),
                "click_dropdown" => ParseClickDropdownAction(call, screen),
                "key_press" => ParseKeyPressAction(call),
                "key_hold" => ParseKeyHoldAction(call),
                "wait" => ParseWaitAction(call),
                "pass" => new PassAction { Reason = call.GetString("reason") },
                "fail" => new FailAction { Reason = call.GetString("reason", "Test failed") },
                "screenshot" => new ScreenshotAction { Reason = call.GetString("reason", "Need visual clarification") },
                _ => throw new ArgumentException($"Unknown action type: {call.Name}")
            };
        }

        private static ClickAction ParseClickAction(ToolCall call, ScreenState screen)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            var searchQueryJson = searchQuery?.ToJson();
            var action = new ClickAction
            {
                SearchQuery = searchQueryJson
            };

            Debug.Log($"[AITest] ParseClickAction: search={searchQueryJson}");

            // Check for screen position (normalized 0-1)
            var x = call.GetFloat("x", -1f);
            var y = call.GetFloat("y", -1f);

            // Prioritize search query over x/y coordinates
            if (searchQuery != null)
            {
                action.TargetElement = FindElementBySearchQuery(searchQuery, screen);
                if (action.TargetElement != null)
                {
                    Debug.Log($"[AITest] Found element via search: {action.TargetElement.name} at bounds {action.TargetElement.bounds}");
                }
                else
                {
                    Debug.LogWarning($"[AITest] Search '{searchQueryJson}' found no elements!");
                    // Fall back to x/y coordinates if search fails
                    if (x >= 0 && y >= 0)
                    {
                        action.ScreenPosition = new Vector2(x, y);
                        Debug.Log($"[AITest] Falling back to screen position: ({x}, {y})");
                    }
                }
            }
            else if (x >= 0 && y >= 0)
            {
                // Only use x/y if no search was provided
                action.ScreenPosition = new Vector2(x, y);
                Debug.Log($"[AITest] Using screen position (no search): ({x}, {y})");
            }
            else
            {
                Debug.LogWarning("[AITest] Click action has no search and no screen position!");
            }

            return action;
        }

        /// <summary>
        /// Finds an element using a SearchQuery object.
        /// First tries to match against discovered elements, then falls back to live search.
        /// </summary>
        private static ElementInfo FindElementBySearchQuery(SearchQuery searchQuery, ScreenState screen)
        {
            if (searchQuery == null || screen == null)
                return null;

            // Convert SearchQuery to Search object
            var search = searchQuery.ToSearch();
            if (search == null)
            {
                Debug.LogWarning($"[AITest] Could not convert SearchQuery to Search: {searchQuery.ToJson()}");
                return null;
            }

            // Execute the search against all GameObjects
            var matchingObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(go => search.Matches(go))
                .ToList();

            // Apply post-processing (First, Last, Skip, Take, etc.)
            if (search.HasPostProcessing)
            {
                matchingObjects = search.ApplyPostProcessing(matchingObjects).ToList();
            }

            var queryJson = searchQuery.ToJson();

            if (matchingObjects.Count == 0)
            {
                Debug.LogWarning($"[AITest] Search '{queryJson}' matched 0 elements");
                return null;
            }

            if (matchingObjects.Count > 1)
            {
                Debug.LogWarning($"[AITest] Search '{queryJson}' matched {matchingObjects.Count} elements - using first. Consider using .First() or more specific filters.");
            }

            var go = matchingObjects.First();

            // Find the corresponding ElementInfo if it exists
            var elementInfo = screen.Elements?.FirstOrDefault(e => e.gameObject == go);
            if (elementInfo != null)
                return elementInfo;

            // Create a new ElementInfo for the found GameObject
            var bounds = InputInjector.GetScreenBounds(go);
            return new ElementInfo
            {
                id = queryJson,
                gameObject = go,
                name = go.name,
                type = "unknown",
                bounds = bounds,
                normalizedBounds = new Rect(
                    bounds.x / Screen.width,
                    bounds.y / Screen.height,
                    bounds.width / Screen.width,
                    bounds.height / Screen.height
                ),
                isEnabled = true
            };
        }

        /// <summary>
        /// Gets a SearchQuery from a tool call, throwing if there's a parse error.
        /// </summary>
        private static SearchQuery GetSearchQueryOrThrow(ToolCall call, string key)
        {
            var query = call.GetSearchQuery(key, out var error);
            if (error != null)
            {
                throw new ArgumentException($"Invalid '{key}' search query: {error}");
            }
            return query;
        }

        private static TypeAction ParseTypeAction(ToolCall call, ScreenState screen)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            var action = new TypeAction
            {
                SearchQuery = searchQuery?.ToJson(),
                Text = call.GetString("text", ""),
                ClearFirst = call.Arguments.TryGetValue("clear_first", out var clear) ? Convert.ToBoolean(clear) : true,
                PressEnter = call.Arguments.TryGetValue("press_enter", out var enter) && Convert.ToBoolean(enter)
            };

            if (searchQuery != null)
            {
                action.TargetElement = FindElementBySearchQuery(searchQuery, screen);
            }

            return action;
        }

        private static DragAction ParseDragAction(ToolCall call, ScreenState screen)
        {
            var fromSearch = GetSearchQueryOrThrow(call, "from");
            var toSearch = GetSearchQueryOrThrow(call, "to");
            var action = new DragAction
            {
                FromSearch = fromSearch?.ToJson(),
                ToSearch = toSearch?.ToJson(),
                Direction = call.GetString("direction"),
                Distance = call.GetFloat("distance", 200f),
                Duration = call.GetFloat("duration", 0.3f)
            };

            if (fromSearch != null)
            {
                action.FromElement = FindElementBySearchQuery(fromSearch, screen);
            }
            if (toSearch != null)
            {
                action.ToElement = FindElementBySearchQuery(toSearch, screen);
            }

            return action;
        }

        private static ScrollAction ParseScrollAction(ToolCall call, ScreenState screen)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            var action = new ScrollAction
            {
                SearchQuery = searchQuery?.ToJson(),
                Direction = call.GetString("direction", "down"),
                Amount = call.GetFloat("amount", 0.3f)
            };

            if (searchQuery != null)
            {
                action.TargetElement = FindElementBySearchQuery(searchQuery, screen);
            }

            return action;
        }

        private static WaitAction ParseWaitAction(ToolCall call)
        {
            return new WaitAction
            {
                Seconds = Mathf.Clamp(call.GetFloat("seconds", 1f), 0.1f, 10f)
            };
        }

        private static DoubleClickAction ParseDoubleClickAction(ToolCall call, ScreenState screen)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            var action = new DoubleClickAction
            {
                SearchQuery = searchQuery?.ToJson()
            };

            var x = call.GetFloat("x", -1f);
            var y = call.GetFloat("y", -1f);

            if (searchQuery != null)
            {
                action.TargetElement = FindElementBySearchQuery(searchQuery, screen);
            }
            else if (x >= 0 && y >= 0)
            {
                action.ScreenPosition = new Vector2(x, y);
            }

            return action;
        }

        private static TripleClickAction ParseTripleClickAction(ToolCall call, ScreenState screen)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            var action = new TripleClickAction
            {
                SearchQuery = searchQuery?.ToJson()
            };

            var x = call.GetFloat("x", -1f);
            var y = call.GetFloat("y", -1f);

            if (searchQuery != null)
            {
                action.TargetElement = FindElementBySearchQuery(searchQuery, screen);
            }
            else if (x >= 0 && y >= 0)
            {
                action.ScreenPosition = new Vector2(x, y);
            }

            return action;
        }

        private static HoldAction ParseHoldAction(ToolCall call, ScreenState screen)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            var action = new HoldAction
            {
                SearchQuery = searchQuery?.ToJson(),
                Duration = call.GetFloat("duration", 1f)
            };

            if (searchQuery != null)
            {
                action.TargetElement = FindElementBySearchQuery(searchQuery, screen);
            }

            return action;
        }

        private static SwipeAction ParseSwipeAction(ToolCall call, ScreenState screen)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            var action = new SwipeAction
            {
                SearchQuery = searchQuery?.ToJson(),
                Direction = call.GetString("direction", "up"),
                Distance = call.GetFloat("distance", 0.2f),
                Duration = call.GetFloat("duration", 0.3f)
            };

            if (searchQuery != null)
            {
                action.TargetElement = FindElementBySearchQuery(searchQuery, screen);
            }

            return action;
        }

        private static PinchAction ParsePinchAction(ToolCall call, ScreenState screen)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            var action = new PinchAction
            {
                SearchQuery = searchQuery?.ToJson(),
                Scale = call.GetFloat("scale", 1.5f),
                Duration = call.GetFloat("duration", 0.5f)
            };

            if (searchQuery != null)
            {
                action.TargetElement = FindElementBySearchQuery(searchQuery, screen);
            }

            return action;
        }

        private static TwoFingerSwipeAction ParseTwoFingerSwipeAction(ToolCall call, ScreenState screen)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            var action = new TwoFingerSwipeAction
            {
                SearchQuery = searchQuery?.ToJson(),
                Direction = call.GetString("direction", "up"),
                Distance = call.GetFloat("distance", 0.2f),
                Duration = call.GetFloat("duration", 0.3f),
                FingerSpacing = call.GetFloat("finger_spacing", 0.03f)
            };

            if (searchQuery != null)
            {
                action.TargetElement = FindElementBySearchQuery(searchQuery, screen);
            }

            return action;
        }

        private static RotateAction ParseRotateAction(ToolCall call, ScreenState screen)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            var action = new RotateAction
            {
                SearchQuery = searchQuery?.ToJson(),
                Degrees = call.GetFloat("degrees", 90f),
                Duration = call.GetFloat("duration", 0.5f),
                FingerDistance = call.GetFloat("finger_distance", 0.05f)
            };

            if (searchQuery != null)
            {
                action.TargetElement = FindElementBySearchQuery(searchQuery, screen);
            }

            return action;
        }

        private static SetSliderAction ParseSetSliderAction(ToolCall call, ScreenState screen)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            var searchQueryJson = searchQuery?.ToJson();
            Debug.Log($"[AITest] ParseSetSliderAction: searchQuery={searchQueryJson}, value={call.GetFloat("value", 0.5f)}");

            var action = new SetSliderAction
            {
                SearchQuery = searchQueryJson,
                Value = Mathf.Clamp01(call.GetFloat("value", 0.5f))
            };

            if (searchQuery != null)
            {
                action.TargetElement = FindElementBySearchQuery(searchQuery, screen);
                Debug.Log($"[AITest] ParseSetSliderAction: TargetElement={(action.TargetElement != null ? action.TargetElement.name : "null")}");
            }
            else
            {
                Debug.LogWarning("[AITest] ParseSetSliderAction: searchQuery is null!");
            }

            return action;
        }

        private static SetScrollbarAction ParseSetScrollbarAction(ToolCall call, ScreenState screen)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            var action = new SetScrollbarAction
            {
                SearchQuery = searchQuery?.ToJson(),
                Value = Mathf.Clamp01(call.GetFloat("value", 0.5f))
            };

            if (searchQuery != null)
            {
                action.TargetElement = FindElementBySearchQuery(searchQuery, screen);
            }

            return action;
        }

        private static ClickDropdownAction ParseClickDropdownAction(ToolCall call, ScreenState screen)
        {
            var searchQuery = GetSearchQueryOrThrow(call, "search");
            var action = new ClickDropdownAction
            {
                SearchQuery = searchQuery?.ToJson(),
                OptionIndex = call.GetInt("index", -1),
                OptionLabel = call.GetString("label", null)
            };

            if (searchQuery != null)
            {
                action.TargetElement = FindElementBySearchQuery(searchQuery, screen);
            }

            return action;
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

        /// <summary>
        /// Executes an AI action using Unity's Input System.
        /// Captures screen state before and after to detect changes.
        /// </summary>
        public static async UniTask<ActionResult> ExecuteAsync(AIAction action, CancellationToken ct = default)
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

        private static async UniTask ExecuteClickAsync(ClickAction action, CancellationToken ct)
        {
            if (action.ScreenPosition.HasValue)
            {
                // Normalized position (0-1)
                var screenPos = new Vector2(
                    action.ScreenPosition.Value.x * Screen.width,
                    action.ScreenPosition.Value.y * Screen.height
                );
                await ActionExecutor.ClickAtAsync(screenPos);
            }
            else if (action.TargetElement?.gameObject != null)
            {
                await ActionExecutor.ClickAsync(action.TargetElement.gameObject);
            }
            else
            {
                throw new InvalidOperationException("Click action has no target position or element");
            }
        }

        private static async UniTask ExecuteTypeAsync(TypeAction action, CancellationToken ct)
        {
            if (action.TargetElement?.gameObject == null)
            {
                // No target element, just type text directly
                await ActionExecutor.TypeTextAsync(action.Text);
                if (action.PressEnter)
                {
                    await DelaySafe(50, ct);
                    await ActionExecutor.PressKeyAsync(Key.Enter);
                }
                return;
            }

            await ActionExecutor.TypeAsync(action.TargetElement.gameObject, action.Text, action.ClearFirst, action.PressEnter);
        }

        private static async UniTask ExecuteDragAsync(DragAction action, CancellationToken ct)
        {
            if (action.FromElement?.gameObject == null)
            {
                throw new InvalidOperationException("Drag action has no starting element");
            }

            if (action.ToElement?.gameObject != null)
            {
                await ActionExecutor.DragToAsync(action.FromElement.gameObject, action.ToElement.gameObject, action.Duration);
            }
            else if (!string.IsNullOrEmpty(action.Direction))
            {
                // Normalize distance to screen height fraction
                await ActionExecutor.DragAsync(action.FromElement.gameObject, action.Direction, action.Distance / Screen.height, action.Duration);
            }
            else
            {
                throw new InvalidOperationException("Drag action has no target or direction");
            }
        }

        private static async UniTask ExecuteScrollAsync(ScrollAction action, CancellationToken ct)
        {
            if (action.TargetElement?.gameObject == null)
            {
                throw new InvalidOperationException("Scroll action has no target element");
            }

            await ActionExecutor.ScrollAsync(action.TargetElement.gameObject, action.Direction, action.Amount);
        }

        private static async UniTask ExecuteDoubleClickAsync(DoubleClickAction action, CancellationToken ct)
        {
            if (action.ScreenPosition.HasValue)
            {
                var screenPos = new Vector2(
                    action.ScreenPosition.Value.x * Screen.width,
                    action.ScreenPosition.Value.y * Screen.height
                );
                await ActionExecutor.DoubleClickAtAsync(screenPos);
            }
            else if (action.TargetElement?.gameObject != null)
            {
                await ActionExecutor.DoubleClickAsync(action.TargetElement.gameObject);
            }
            else
            {
                throw new InvalidOperationException("Double-click action has no target");
            }
        }

        private static async UniTask ExecuteTripleClickAsync(TripleClickAction action, CancellationToken ct)
        {
            if (action.ScreenPosition.HasValue)
            {
                var screenPos = new Vector2(
                    action.ScreenPosition.Value.x * Screen.width,
                    action.ScreenPosition.Value.y * Screen.height
                );
                await ActionExecutor.TripleClickAtAsync(screenPos);
            }
            else if (action.TargetElement?.gameObject != null)
            {
                await ActionExecutor.TripleClickAsync(action.TargetElement.gameObject);
            }
            else
            {
                throw new InvalidOperationException("Triple-click action has no target");
            }
        }

        private static async UniTask ExecuteHoldAsync(HoldAction action, CancellationToken ct)
        {
            if (action.TargetElement?.gameObject == null)
            {
                throw new InvalidOperationException("Hold action has no target element");
            }

            await ActionExecutor.HoldAsync(action.TargetElement.gameObject, action.Duration);
        }

        private static async UniTask ExecuteSwipeAsync(SwipeAction action, CancellationToken ct)
        {
            if (action.TargetElement?.gameObject == null)
            {
                throw new InvalidOperationException("Swipe action has no target element");
            }

            await ActionExecutor.SwipeAsync(action.TargetElement.gameObject, action.Direction, action.Distance, action.Duration);
        }

        private static async UniTask ExecutePinchAsync(PinchAction action, CancellationToken ct)
        {
            await ActionExecutor.PinchAsync(action.TargetElement?.gameObject, action.Scale, action.Duration);
        }

        private static async UniTask ExecuteTwoFingerSwipeAsync(TwoFingerSwipeAction action, CancellationToken ct)
        {
            await ActionExecutor.TwoFingerSwipeAsync(
                action.TargetElement?.gameObject,
                action.Direction,
                action.Distance,
                action.Duration,
                action.FingerSpacing);
        }

        private static async UniTask ExecuteRotateAsync(RotateAction action, CancellationToken ct)
        {
            await ActionExecutor.RotateAsync(
                action.TargetElement?.gameObject,
                action.Degrees,
                action.Duration,
                action.FingerDistance);
        }

        private static async UniTask ExecuteSetSliderAsync(SetSliderAction action, CancellationToken ct)
        {
            if (action.TargetElement?.gameObject == null)
            {
                throw new InvalidOperationException("SetSlider action has no target element");
            }

            var slider = action.TargetElement.gameObject.GetComponent<Slider>();
            if (slider == null)
            {
                throw new InvalidOperationException($"Element {action.SearchQuery} is not a Slider");
            }

            // AI sends value as 0-1 normalized
            await ActionExecutor.SetSliderAsync(slider, action.Value);
        }

        private static async UniTask ExecuteSetScrollbarAsync(SetScrollbarAction action, CancellationToken ct)
        {
            if (action.TargetElement?.gameObject == null)
            {
                throw new InvalidOperationException("SetScrollbar action has no target element");
            }

            var scrollbar = action.TargetElement.gameObject.GetComponent<Scrollbar>();
            if (scrollbar == null)
            {
                throw new InvalidOperationException($"Element {action.SearchQuery} is not a Scrollbar");
            }

            // AI sends value as 0-1 normalized
            await ActionExecutor.SetScrollbarAsync(scrollbar, action.Value);
        }

        private static async UniTask ExecuteClickDropdownAsync(ClickDropdownAction action, CancellationToken ct)
        {
            if (action.TargetElement?.gameObject == null)
            {
                throw new InvalidOperationException("ClickDropdown action has no target element");
            }

            // Build a search from the target element's name
            var search = new Search().Name(action.TargetElement.name);

            bool found;
            if (action.OptionIndex >= 0)
            {
                found = await ActionExecutor.ClickDropdownAsync(search, action.OptionIndex);
            }
            else if (!string.IsNullOrEmpty(action.OptionLabel))
            {
                found = await ActionExecutor.ClickDropdownAsync(search, action.OptionLabel);
            }
            else
            {
                throw new InvalidOperationException("ClickDropdown requires either index or label");
            }

            if (!found)
            {
                throw new InvalidOperationException($"Dropdown option not found in '{action.SearchQuery}'");
            }
        }

        private static async UniTask ExecuteKeyPressAsync(KeyPressAction action, CancellationToken ct)
        {
            if (Enum.TryParse<Key>(action.Key, true, out var key))
            {
                await ActionExecutor.PressKeyAsync(key);
            }
            else
            {
                Debug.LogWarning($"[AITest] Unknown key: {action.Key}");
            }
        }

        private static async UniTask ExecuteKeyHoldAsync(KeyHoldAction action, CancellationToken ct)
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
                await ActionExecutor.HoldKeysAsync(keys.ToArray(), action.Duration);
            }
        }

        /// <summary>
        /// Safe delay that won't hang when exiting play mode.
        /// </summary>
        private static async UniTask DelaySafe(int milliseconds, CancellationToken ct)
        {
            if (!Application.isPlaying || ct.IsCancellationRequested)
                return;

            try
            {
                await UniTask.Delay(milliseconds, ignoreTimeScale: true, cancellationToken: ct);
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
