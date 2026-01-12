using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ODDGames.UITest.AI
{
    using ODDGames.UITest; // For InputInjector

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
            var action = new ClickAction
            {
                ElementId = call.GetString("element_id")
            };

            Debug.Log($"[AITest] ParseClickAction: ElementId='{action.ElementId}'");

            // Check for screen position (normalized 0-1)
            var x = call.GetFloat("x", -1f);
            var y = call.GetFloat("y", -1f);

            // Prioritize element_id over x/y coordinates when both are provided
            // Element bounds are more reliable than AI-estimated positions
            if (!string.IsNullOrEmpty(action.ElementId))
            {
                action.TargetElement = screen?.FindElement(action.ElementId);
                if (action.TargetElement != null)
                {
                    Debug.Log($"[AITest] Found element '{action.ElementId}': {action.TargetElement.name} at bounds {action.TargetElement.bounds}");
                }
                else
                {
                    Debug.LogWarning($"[AITest] Element '{action.ElementId}' NOT FOUND! Available elements: {screen?.Elements?.Count ?? 0}");
                    if (screen?.Elements != null)
                    {
                        foreach (var e in screen.Elements)
                        {
                            Debug.Log($"[AITest]   - {e.id}: {e.name} ({e.type})");
                        }
                    }
                    // Fall back to x/y coordinates if element not found
                    if (x >= 0 && y >= 0)
                    {
                        action.ScreenPosition = new Vector2(x, y);
                        Debug.Log($"[AITest] Falling back to screen position: ({x}, {y})");
                    }
                }
            }
            else if (x >= 0 && y >= 0)
            {
                // Only use x/y if no element_id was provided
                action.ScreenPosition = new Vector2(x, y);
                Debug.Log($"[AITest] Using screen position (no element_id): ({x}, {y})");
            }
            else
            {
                Debug.LogWarning("[AITest] Click action has no element_id and no screen position!");
            }

            return action;
        }

        private static TypeAction ParseTypeAction(ToolCall call, ScreenState screen)
        {
            var action = new TypeAction
            {
                ElementId = call.GetString("element_id"),
                Text = call.GetString("text", ""),
                ClearFirst = call.Arguments.TryGetValue("clear_first", out var clear) ? Convert.ToBoolean(clear) : true,
                PressEnter = call.Arguments.TryGetValue("press_enter", out var enter) && Convert.ToBoolean(enter)
            };

            if (!string.IsNullOrEmpty(action.ElementId))
            {
                action.TargetElement = screen?.FindElement(action.ElementId);
            }

            return action;
        }

        private static DragAction ParseDragAction(ToolCall call, ScreenState screen)
        {
            var action = new DragAction
            {
                FromElementId = call.GetString("from_element_id"),
                ToElementId = call.GetString("to_element_id"),
                Direction = call.GetString("direction"),
                Distance = call.GetFloat("distance", 200f),
                Duration = call.GetFloat("duration", 0.3f)
            };

            if (!string.IsNullOrEmpty(action.FromElementId))
            {
                action.FromElement = screen?.FindElement(action.FromElementId);
            }
            if (!string.IsNullOrEmpty(action.ToElementId))
            {
                action.ToElement = screen?.FindElement(action.ToElementId);
            }

            return action;
        }

        private static ScrollAction ParseScrollAction(ToolCall call, ScreenState screen)
        {
            var action = new ScrollAction
            {
                ElementId = call.GetString("element_id"),
                Direction = call.GetString("direction", "down"),
                Amount = call.GetFloat("amount", 0.3f)
            };

            if (!string.IsNullOrEmpty(action.ElementId))
            {
                action.TargetElement = screen?.FindElement(action.ElementId);
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
            var action = new DoubleClickAction
            {
                ElementId = call.GetString("element_id")
            };

            var x = call.GetFloat("x", -1f);
            var y = call.GetFloat("y", -1f);

            if (!string.IsNullOrEmpty(action.ElementId))
            {
                action.TargetElement = screen?.FindElement(action.ElementId);
            }
            else if (x >= 0 && y >= 0)
            {
                action.ScreenPosition = new Vector2(x, y);
            }

            return action;
        }

        private static HoldAction ParseHoldAction(ToolCall call, ScreenState screen)
        {
            var action = new HoldAction
            {
                ElementId = call.GetString("element_id"),
                Duration = call.GetFloat("duration", 1f)
            };

            if (!string.IsNullOrEmpty(action.ElementId))
            {
                action.TargetElement = screen?.FindElement(action.ElementId);
            }

            return action;
        }

        private static SwipeAction ParseSwipeAction(ToolCall call, ScreenState screen)
        {
            var action = new SwipeAction
            {
                ElementId = call.GetString("element_id"),
                Direction = call.GetString("direction", "up"),
                Distance = call.GetFloat("distance", 0.2f),
                Duration = call.GetFloat("duration", 0.3f)
            };

            if (!string.IsNullOrEmpty(action.ElementId))
            {
                action.TargetElement = screen?.FindElement(action.ElementId);
            }

            return action;
        }

        private static PinchAction ParsePinchAction(ToolCall call, ScreenState screen)
        {
            var action = new PinchAction
            {
                ElementId = call.GetString("element_id"),
                Scale = call.GetFloat("scale", 1.5f),
                Duration = call.GetFloat("duration", 0.5f)
            };

            if (!string.IsNullOrEmpty(action.ElementId))
            {
                action.TargetElement = screen?.FindElement(action.ElementId);
            }

            return action;
        }

        private static TwoFingerSwipeAction ParseTwoFingerSwipeAction(ToolCall call, ScreenState screen)
        {
            var action = new TwoFingerSwipeAction
            {
                ElementId = call.GetString("element_id"),
                Direction = call.GetString("direction", "up"),
                Distance = call.GetFloat("distance", 0.2f),
                Duration = call.GetFloat("duration", 0.3f),
                FingerSpacing = call.GetFloat("finger_spacing", 0.03f)
            };

            if (!string.IsNullOrEmpty(action.ElementId))
            {
                action.TargetElement = screen?.FindElement(action.ElementId);
            }

            return action;
        }

        private static RotateAction ParseRotateAction(ToolCall call, ScreenState screen)
        {
            var action = new RotateAction
            {
                ElementId = call.GetString("element_id"),
                Degrees = call.GetFloat("degrees", 90f),
                Duration = call.GetFloat("duration", 0.5f),
                FingerDistance = call.GetFloat("finger_distance", 0.05f)
            };

            if (!string.IsNullOrEmpty(action.ElementId))
            {
                action.TargetElement = screen?.FindElement(action.ElementId);
            }

            return action;
        }

        private static SetSliderAction ParseSetSliderAction(ToolCall call, ScreenState screen)
        {
            var action = new SetSliderAction
            {
                ElementId = call.GetString("element_id"),
                Value = Mathf.Clamp01(call.GetFloat("value", 0.5f))
            };

            if (!string.IsNullOrEmpty(action.ElementId))
            {
                action.TargetElement = screen?.FindElement(action.ElementId);
            }

            return action;
        }

        private static SetScrollbarAction ParseSetScrollbarAction(ToolCall call, ScreenState screen)
        {
            var action = new SetScrollbarAction
            {
                ElementId = call.GetString("element_id"),
                Value = Mathf.Clamp01(call.GetFloat("value", 0.5f))
            };

            if (!string.IsNullOrEmpty(action.ElementId))
            {
                action.TargetElement = screen?.FindElement(action.ElementId);
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
        /// </summary>
        public static async UniTask<ActionResult> ExecuteAsync(AIAction action, CancellationToken ct = default)
        {
            var startTime = Time.realtimeSinceStartup;

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

                return new ActionResult
                {
                    Success = true,
                    ExecutionTimeMs = (Time.realtimeSinceStartup - startTime) * 1000f
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
            Vector2 screenPos;

            if (action.ScreenPosition.HasValue)
            {
                // Normalized position (0-1)
                screenPos = new Vector2(
                    action.ScreenPosition.Value.x * Screen.width,
                    action.ScreenPosition.Value.y * Screen.height
                );
                Debug.Log($"[AITest] Click using normalized position: ({action.ScreenPosition.Value.x:F2}, {action.ScreenPosition.Value.y:F2}) → screen {screenPos}");
            }
            else if (action.TargetElement?.gameObject != null)
            {
                // Use the same screen position calculation as UITestBehaviour
                screenPos = InputInjector.GetScreenPosition(action.TargetElement.gameObject);
                Debug.Log($"[AITest] Click using element '{action.TargetElement.id}' ({action.TargetElement.name}), screenPos: {screenPos}");
            }
            else
            {
                Debug.LogError($"[AITest] Click action has no target! ElementId: {action.ElementId}, ScreenPosition: {action.ScreenPosition}");
                throw new InvalidOperationException("Click action has no target position or element");
            }

            // Use the shared InputInjector utility (same as UITestBehaviour)
            Debug.Log($"[AITest] Injecting click at screen position: {screenPos} (screen size: {Screen.width}x{Screen.height})");
            await InputInjector.InjectPointerTap(screenPos);
            Debug.Log($"[AITest] Click injection complete at {screenPos}");
        }

        private static async UniTask ExecuteTypeAsync(TypeAction action, CancellationToken ct)
        {
            if (action.TargetElement?.gameObject == null)
            {
                // No target element, just type text directly
                await InputInjector.TypeText(action.Text);
                if (action.PressEnter)
                {
                    await DelaySafe(50, ct);
                    await InputInjector.PressKey(Key.Enter);
                }
                return;
            }

            Debug.Log($"[AITest] Typing into '{action.TargetElement.name}': \"{action.Text}\"");

            // Use shared InputInjector helper for consistent behavior
            await InputInjector.TypeIntoField(
                action.TargetElement.gameObject,
                action.Text,
                clearFirst: action.ClearFirst,
                pressEnter: action.PressEnter);
        }

        private static async UniTask ExecuteDragAsync(DragAction action, CancellationToken ct)
        {
            Vector2 startPos;
            Vector2 endPos;

            if (action.FromElement?.gameObject != null)
            {
                startPos = InputInjector.GetScreenPosition(action.FromElement.gameObject);
            }
            else
            {
                throw new InvalidOperationException("Drag action has no starting element");
            }

            if (action.ToElement?.gameObject != null)
            {
                endPos = InputInjector.GetScreenPosition(action.ToElement.gameObject);
            }
            else if (!string.IsNullOrEmpty(action.Direction))
            {
                // Use shared helper for consistent distance scaling
                var offset = InputInjector.GetDirectionOffset(action.Direction, action.Distance / Screen.height);
                endPos = startPos + offset;
            }
            else
            {
                throw new InvalidOperationException("Drag action has no target or direction");
            }

            Debug.Log($"[AITest] Injecting drag from {startPos} to {endPos} over {action.Duration}s");
            await InputInjector.InjectPointerDrag(startPos, endPos, action.Duration);
        }

        private static async UniTask ExecuteScrollAsync(ScrollAction action, CancellationToken ct)
        {
            if (action.TargetElement?.gameObject == null)
            {
                throw new InvalidOperationException("Scroll action has no target element");
            }

            Debug.Log($"[AITest] Scrolling '{action.TargetElement.name}' {action.Direction}");

            // Use shared InputInjector helper for consistent behavior
            await InputInjector.ScrollElement(action.TargetElement.gameObject, action.Direction, action.Amount);
        }

        private static async UniTask ExecuteDoubleClickAsync(DoubleClickAction action, CancellationToken ct)
        {
            Vector2 screenPos;

            if (action.ScreenPosition.HasValue)
            {
                screenPos = new Vector2(
                    action.ScreenPosition.Value.x * Screen.width,
                    action.ScreenPosition.Value.y * Screen.height
                );
            }
            else if (action.TargetElement?.gameObject != null)
            {
                screenPos = InputInjector.GetScreenPosition(action.TargetElement.gameObject);
            }
            else
            {
                throw new InvalidOperationException("Double-click action has no target");
            }

            // Double click = two rapid clicks
            await InputInjector.InjectPointerTap(screenPos);
            await DelaySafe(50, ct);
            await InputInjector.InjectPointerTap(screenPos);
        }

        private static async UniTask ExecuteHoldAsync(HoldAction action, CancellationToken ct)
        {
            if (action.TargetElement?.gameObject == null)
            {
                throw new InvalidOperationException("Hold action has no target element");
            }

            var screenPos = InputInjector.GetScreenPosition(action.TargetElement.gameObject);
            await InputInjector.InjectPointerHold(screenPos, action.Duration);
        }

        private static async UniTask ExecuteSwipeAsync(SwipeAction action, CancellationToken ct)
        {
            if (action.TargetElement?.gameObject == null)
            {
                throw new InvalidOperationException("Swipe action has no target element");
            }

            Debug.Log($"[AITest] Swiping {action.Direction} on '{action.TargetElement.name}'");

            // Use shared InputInjector helper for consistent behavior
            await InputInjector.Swipe(action.TargetElement.gameObject, action.Direction, action.Distance, action.Duration);
        }

        private static async UniTask ExecutePinchAsync(PinchAction action, CancellationToken ct)
        {
            Debug.Log($"[AITest] Pinch {(action.Scale < 1 ? "in" : "out")} {action.Scale:F1}x on '{action.TargetElement?.name ?? "screen"}'");

            // Use shared InputInjector helper for consistent behavior
            await InputInjector.Pinch(action.TargetElement?.gameObject, action.Scale, action.Duration);
        }

        private static async UniTask ExecuteTwoFingerSwipeAsync(TwoFingerSwipeAction action, CancellationToken ct)
        {
            Debug.Log($"[AITest] Two-finger swipe {action.Direction} on '{action.TargetElement?.name ?? "screen"}'");

            // Use shared InputInjector helper for consistent behavior
            await InputInjector.TwoFingerSwipe(
                action.TargetElement?.gameObject,
                action.Direction,
                action.Distance,
                action.Duration,
                action.FingerSpacing);
        }

        private static async UniTask ExecuteRotateAsync(RotateAction action, CancellationToken ct)
        {
            Debug.Log($"[AITest] Rotate {(action.Degrees >= 0 ? "CW" : "CCW")} {Mathf.Abs(action.Degrees)}° on '{action.TargetElement?.name ?? "screen"}'");

            // Use shared InputInjector helper for consistent behavior
            await InputInjector.Rotate(
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
                throw new InvalidOperationException($"Element {action.ElementId} is not a Slider");
            }

            Debug.Log($"[AITest] SetSlider '{action.TargetElement.name}' to {action.Value:P0}");

            // Use shared InputInjector helper for consistent behavior
            // AI sends value as 0-1 normalized
            await InputInjector.SetSlider(slider, action.Value);
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
                throw new InvalidOperationException($"Element {action.ElementId} is not a Scrollbar");
            }

            Debug.Log($"[AITest] SetScrollbar '{action.TargetElement.name}' to {action.Value:P0}");

            // Use shared InputInjector helper for consistent behavior
            // AI sends value as 0-1 normalized
            await InputInjector.SetScrollbar(scrollbar, action.Value);
        }

        private static async UniTask ExecuteKeyPressAsync(KeyPressAction action, CancellationToken ct)
        {
            if (Enum.TryParse<Key>(action.Key, true, out var key))
            {
                await InputInjector.PressKey(key);
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
                await InputInjector.HoldKeys(keys.ToArray(), action.Duration);
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
