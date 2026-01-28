using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using ODDGames.UIAutomation.AI;
using ODDGames.UIAutomation.AI.Editor;

namespace ODDGames.UIAutomation.VisualBuilder.Editor
{
    /// <summary>
    /// AI Assistant that integrates with the Visual Test Builder to suggest and add blocks
    /// in real-time. Uses the same interface as manual editing.
    /// </summary>
    public class AIAssistant : IDisposable
    {
        public enum State
        {
            Idle,
            Initializing,
            Running,
            Paused,
            Stopping,
            Completed,
            Failed
        }

        private VisualTest targetTest;
        private AITestRunner runner;
        private CancellationTokenSource cts;

        private State currentState = State.Idle;
        private string currentStatus = "Ready";
        private string currentReasoning = "";
        private string startScene;
        private bool isDisposed;

        public State CurrentState => currentState;
        public string Status => currentStatus;
        public string Reasoning => currentReasoning;
        public int BlocksAdded { get; private set; }
        public bool IsActive => currentState == State.Running || currentState == State.Paused;
        public bool IsPaused => currentState == State.Paused;

        public event Action<State> OnStateChanged;
        public event Action<VisualBlock> OnBlockAdded;
        public event Action<string> OnReasoningReceived;
        public event Action<string> OnStatusChanged;
        public event Action<bool, string> OnCompleted;

        public AIAssistant(VisualTest test)
        {
            targetTest = test ?? throw new ArgumentNullException(nameof(test));
        }

        public async Task StartAsync(string prompt)
        {
            if (currentState != State.Idle)
                throw new InvalidOperationException($"Cannot start in state: {currentState}");

            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Play mode is required");

            cts = new CancellationTokenSource();
            BlocksAdded = 0;
            startScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            SetState(State.Initializing);
            SetStatus("Initializing AI...");

            try
            {
                // Get settings and create config with provider
                var settings = AITestSettings.Instance;
                var config = settings.CreateRunnerConfig();

                // Override some settings for visual builder use
                config.EnableHistoryReplay = false;

                // Validate that we have a provider
                if (config.ModelProvider == null)
                {
                    throw new InvalidOperationException(
                        "No AI provider configured. Please add your Gemini API key and select a model in Project Settings > UI Test > AI Testing");
                }

                var aiTest = ScriptableObject.CreateInstance<AITest>();
                aiTest.prompt = prompt;
                aiTest.model = settings.GetEffectiveModel();
                aiTest.maxActions = settings.defaultMaxActions > 0 ? settings.defaultMaxActions : 100;
                aiTest.timeoutSeconds = settings.defaultTimeout > 0 ? settings.defaultTimeout : 600f;
                aiTest.actionDelaySeconds = settings.defaultActionDelay > 0 ? settings.defaultActionDelay : 0.3f;

                runner = new AITestRunner(aiTest, settings.GetGlobalKnowledge(), config);
                runner.OnActionExecuted += HandleActionExecuted;
                runner.OnReasoning += HandleReasoning;

                SetState(State.Running);
                SetStatus("AI is analyzing screen...");

                var result = await runner.RunAsync(cts.Token);

                if (result.IsSuccess)
                {
                    SetState(State.Completed);
                    SetStatus($"Completed: {result.Message}");
                    OnCompleted?.Invoke(true, result.Message);
                }
                else if (result.Status == TestStatus.Cancelled)
                {
                    SetState(State.Idle);
                    SetStatus("Stopped");
                    OnCompleted?.Invoke(false, "Stopped by user");
                }
                else
                {
                    SetState(State.Failed);
                    SetStatus($"Failed: {result.Message}");
                    OnCompleted?.Invoke(false, result.Message);
                }
            }
            catch (OperationCanceledException)
            {
                SetState(State.Idle);
                SetStatus("Stopped");
                OnCompleted?.Invoke(false, "Stopped by user");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AIAssistant] Error: {ex}");
                SetState(State.Failed);
                SetStatus($"Error: {ex.Message}");
                OnCompleted?.Invoke(false, ex.Message);
            }
            finally
            {
                Cleanup();
            }
        }

        public void Pause()
        {
            if (currentState == State.Running)
            {
                SetState(State.Paused);
                SetStatus("Paused");
            }
        }

        public void Resume()
        {
            if (currentState == State.Paused)
            {
                SetState(State.Running);
                SetStatus("Resumed");
            }
        }

        public void Stop()
        {
            if (IsActive)
            {
                SetState(State.Stopping);
                SetStatus("Stopping...");
                cts?.Cancel();
                runner?.Cancel();
            }
        }

        public async Task RestartAsync(string prompt)
        {
            Stop();
            await Task.Delay(100);

            if (targetTest != null && BlocksAdded > 0)
            {
                Undo.RecordObject(targetTest, "AI Restart - Clear Blocks");
                var removeCount = Mathf.Min(BlocksAdded, targetTest.blocks.Count);
                for (int i = 0; i < removeCount; i++)
                {
                    targetTest.blocks.RemoveAt(targetTest.blocks.Count - 1);
                }
                EditorUtility.SetDirty(targetTest);
            }

            currentState = State.Idle;
            BlocksAdded = 0;

            await StartAsync(prompt);
        }

        private void HandleReasoning(string reasoning)
        {
            currentReasoning = reasoning ?? "";
            OnReasoningReceived?.Invoke(currentReasoning);
        }

        private void HandleActionExecuted(AIAction action, ActionResult result)
        {
            if (action == null) return;
            if (action is PassAction || action is FailAction || action is ScreenshotAction) return;

            while (currentState == State.Paused && !cts.IsCancellationRequested)
            {
                System.Threading.Thread.Sleep(100);
            }

            if (cts.IsCancellationRequested) return;

            // Skip adding block if action failed (element not found, etc.)
            if (result == null || !result.Success)
            {
                Debug.Log($"[AIAssistant] Action failed, not adding block: {result?.Error ?? "Unknown error"}");
                SetStatus($"Action failed: {result?.Error ?? "Unknown error"}");
                return;
            }

            SetStatus($"Executed: {action.Description}");

            var block = ConvertActionToBlock(action);
            if (block != null)
            {
                AddBlockToTest(block);
            }
        }

        private void AddBlockToTest(VisualBlock block)
        {
            if (targetTest == null || block == null) return;

            Undo.RecordObject(targetTest, $"AI: Add {block.type} block");
            targetTest.blocks.Add(block);
            EditorUtility.SetDirty(targetTest);
            BlocksAdded++;

            OnBlockAdded?.Invoke(block);
        }

        private VisualBlock ConvertActionToBlock(AIAction action)
        {
            var block = new VisualBlock
            {
                id = Guid.NewGuid().ToString(),
                comment = action.Description
            };

            switch (action)
            {
                case ClickAction click:
                    block.type = BlockType.Click;
                    block.target = CreateSelector(click.SearchQuery, click.TargetElement);
                    break;

                case TypeAction type:
                    block.type = BlockType.Type;
                    block.target = CreateSelector(type.SearchQuery, type.TargetElement);
                    block.text = type.Text;
                    block.clearFirst = type.ClearFirst;
                    block.pressEnter = type.PressEnter;
                    break;

                case DragAction drag:
                    block.type = BlockType.Drag;
                    block.target = CreateSelector(drag.FromSearch, drag.FromElement);
                    if (drag.ToElement != null || !string.IsNullOrEmpty(drag.ToSearch))
                    {
                        block.dragTarget = CreateSelector(drag.ToSearch, drag.ToElement);
                    }
                    else
                    {
                        block.dragDirection = drag.Direction;
                        block.dragDistance = drag.Distance;
                    }
                    block.dragDuration = drag.Duration;
                    break;

                case ScrollAction scroll:
                    block.type = BlockType.Scroll;
                    block.target = CreateSelector(scroll.SearchQuery, scroll.TargetElement);
                    block.scrollDirection = scroll.Direction;
                    block.scrollAmount = scroll.Amount;
                    break;

                case WaitAction wait:
                    block.type = BlockType.Wait;
                    block.waitSeconds = wait.Seconds;
                    break;

                case DoubleClickAction doubleClick:
                    block.type = BlockType.DoubleClick;
                    block.target = CreateSelector(doubleClick.SearchQuery, doubleClick.TargetElement);
                    break;

                case HoldAction hold:
                    block.type = BlockType.Hold;
                    block.target = CreateSelector(hold.SearchQuery, hold.TargetElement);
                    block.holdSeconds = hold.Duration;
                    break;

                case KeyPressAction keyPress:
                    block.type = BlockType.KeyPress;
                    block.keyName = keyPress.Key;
                    break;

                case KeyHoldAction keyHold:
                    block.type = BlockType.KeyHold;
                    block.keyHoldKeys = string.Join(",", keyHold.Keys ?? Array.Empty<string>());
                    block.keyHoldDuration = keyHold.Duration;
                    break;

                case SetSliderAction setSlider:
                    block.type = BlockType.SetSlider;
                    block.target = CreateSelector(setSlider.SearchQuery, setSlider.TargetElement);
                    block.sliderValue = setSlider.Value * 100f; // Convert 0-1 to 0-100
                    break;

                case SetScrollbarAction setScrollbar:
                    block.type = BlockType.SetScrollbar;
                    block.target = CreateSelector(setScrollbar.SearchQuery, setScrollbar.TargetElement);
                    block.scrollbarValue = setScrollbar.Value * 100f; // Convert 0-1 to 0-100
                    break;

                case SwipeAction swipe:
                    block.type = BlockType.Swipe;
                    block.target = CreateSelector(swipe.SearchQuery, swipe.TargetElement);
                    block.swipeDirection = swipe.Direction;
                    block.swipeDistance = swipe.Distance;
                    block.swipeDuration = swipe.Duration;
                    break;

                case PinchAction pinch:
                    block.type = BlockType.Pinch;
                    block.target = CreateSelector(pinch.SearchQuery, pinch.TargetElement);
                    block.pinchScale = pinch.Scale;
                    block.pinchDuration = pinch.Duration;
                    break;

                case TwoFingerSwipeAction twoFingerSwipe:
                    block.type = BlockType.TwoFingerSwipe;
                    block.target = CreateSelector(twoFingerSwipe.SearchQuery, twoFingerSwipe.TargetElement);
                    block.swipeDirection = twoFingerSwipe.Direction;
                    block.swipeDistance = twoFingerSwipe.Distance;
                    block.swipeDuration = twoFingerSwipe.Duration;
                    block.twoFingerSpacing = twoFingerSwipe.FingerSpacing;
                    break;

                case RotateAction rotate:
                    block.type = BlockType.Rotate;
                    block.target = CreateSelector(rotate.SearchQuery, rotate.TargetElement);
                    block.rotateDegrees = rotate.Degrees;
                    block.rotateDuration = rotate.Duration;
                    block.rotateFingerDistance = rotate.FingerDistance;
                    break;

                default:
                    return null;
            }

            return block;
        }

        private ElementSelector CreateSelector(string searchQuery, ElementInfo elementInfo)
        {
            // PREFER the SearchQuery JSON - it contains the full chain information
            // that the AI used (e.g., Name("Settings").Near("Volume"))
            if (!string.IsNullOrEmpty(searchQuery))
            {
                var selector = ElementSelector.FromJson(searchQuery);
                if (selector != null)
                {
                    // Use element info for display name if available
                    if (elementInfo != null)
                    {
                        selector.displayName = null; // Let GetDisplayText() generate from query
                    }
                    return selector;
                }
            }

            // Fall back to using the resolved ElementInfo if no query
            if (elementInfo != null)
            {
                if (!string.IsNullOrEmpty(elementInfo.text))
                    return ElementSelector.ByText(elementInfo.text, elementInfo.text);

                if (!string.IsNullOrEmpty(elementInfo.name))
                    return ElementSelector.ByName(elementInfo.name, elementInfo.name);
            }

            // Last resort - if searchQuery isn't valid JSON, try using it as name pattern
            if (!string.IsNullOrEmpty(searchQuery))
            {
                return ElementSelector.ByName(searchQuery, searchQuery);
            }

            return null;
        }

        private void SetState(State state)
        {
            if (currentState != state)
            {
                currentState = state;
                OnStateChanged?.Invoke(state);
            }
        }

        private void SetStatus(string status)
        {
            currentStatus = status;
            OnStatusChanged?.Invoke(status);
        }

        private void Cleanup()
        {
            if (runner != null)
            {
                runner.OnActionExecuted -= HandleActionExecuted;
                runner.OnReasoning -= HandleReasoning;
                runner = null;
            }

            try { cts?.Dispose(); } catch { }
            cts = null;
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;

            Stop();
            Cleanup();
        }
    }
}
