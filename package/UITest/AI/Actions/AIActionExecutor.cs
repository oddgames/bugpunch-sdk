using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

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
                "type" => ParseTypeAction(call, screen),
                "drag" => ParseDragAction(call, screen),
                "scroll" => ParseScrollAction(call, screen),
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
            if (action.TargetElement?.gameObject != null)
            {
                // Click to focus the input field
                var screenPos = InputInjector.GetScreenPosition(action.TargetElement.gameObject);
                await InputInjector.InjectPointerTap(screenPos);
                await DelaySafe(100, ct);
            }

            // Type the text using InputInjector
            await InputInjector.TypeText(action.Text);

            if (action.PressEnter)
            {
                await DelaySafe(50, ct);
                await InputInjector.PressKey(Key.Enter);
            }
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
                var offset = action.Direction switch
                {
                    "up" => new Vector2(0, action.Distance),
                    "down" => new Vector2(0, -action.Distance),
                    "left" => new Vector2(-action.Distance, 0),
                    "right" => new Vector2(action.Distance, 0),
                    _ => Vector2.zero
                };
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

            var center = InputInjector.GetScreenPosition(action.TargetElement.gameObject);
            var scrollDelta = action.Direction switch
            {
                "up" => new Vector2(0, action.Amount * 500f),
                "down" => new Vector2(0, -action.Amount * 500f),
                "left" => new Vector2(-action.Amount * 500f, 0),
                "right" => new Vector2(action.Amount * 500f, 0),
                _ => Vector2.zero
            };

            Debug.Log($"[AITest] Injecting scroll at {center} with delta {scrollDelta}");
            await InputInjector.InjectScroll(center, scrollDelta);
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
