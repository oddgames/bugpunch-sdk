using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using ODDGames.UITest.AI;

namespace ODDGames.UITest.VisualBuilder.Editor
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

        public async UniTask StartAsync(string prompt, string passCondition)
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
                var aiTest = ScriptableObject.CreateInstance<AITest>();
                aiTest.prompt = prompt;
                aiTest.passCondition = passCondition;
                aiTest.startingTier = ModelTier.GeminiFlash;
                aiTest.maxActions = 100;
                aiTest.timeoutSeconds = 600f;
                aiTest.actionDelaySeconds = 0.3f;

                var config = new AITestRunnerConfig
                {
                    MaxContextTokens = 8000,
                    CompactionThreshold = 0.8f,
                    EnableHistoryReplay = false,
                    SendScreenshots = true
                };

                runner = new AITestRunner(aiTest, GlobalKnowledge.CreateDefault(), config);
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

        public async UniTask RestartAsync(string prompt, string passCondition)
        {
            Stop();
            await UniTask.Delay(100);

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

            await StartAsync(prompt, passCondition);
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

            SetStatus($"Executing: {action.Description}");

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
                    block.target = CreateSelector(click.ElementId, click.TargetElement);
                    break;

                case TypeAction type:
                    block.type = BlockType.Type;
                    block.target = CreateSelector(type.ElementId, type.TargetElement);
                    block.text = type.Text;
                    block.clearFirst = type.ClearFirst;
                    block.pressEnter = type.PressEnter;
                    break;

                case DragAction drag:
                    block.type = BlockType.Drag;
                    block.target = CreateSelector(drag.FromElementId, drag.FromElement);
                    if (drag.ToElement != null || !string.IsNullOrEmpty(drag.ToElementId))
                    {
                        block.dragTarget = CreateSelector(drag.ToElementId, drag.ToElement);
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
                    block.target = CreateSelector(scroll.ElementId, scroll.TargetElement);
                    block.scrollDirection = scroll.Direction;
                    block.scrollAmount = scroll.Amount;
                    break;

                case WaitAction wait:
                    block.type = BlockType.Wait;
                    block.waitSeconds = wait.Seconds;
                    break;

                default:
                    return null;
            }

            return block;
        }

        private ElementSelector CreateSelector(string elementId, ElementInfo elementInfo)
        {
            if (elementInfo != null)
            {
                if (!string.IsNullOrEmpty(elementInfo.text))
                    return ElementSelector.ByText(elementInfo.text, elementInfo.text);

                if (!string.IsNullOrEmpty(elementInfo.name))
                    return ElementSelector.ByName(elementInfo.name, elementInfo.name);
            }

            if (!string.IsNullOrEmpty(elementId))
                return ElementSelector.ById(elementId, elementId);

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
