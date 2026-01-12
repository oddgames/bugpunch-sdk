using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ODDGames.UITest.AI;

namespace ODDGames.UITest.VisualBuilder
{
    /// <summary>
    /// State of the AI recording session.
    /// </summary>
    public enum RecordingState
    {
        /// <summary>Session not yet started</summary>
        Idle,
        /// <summary>Session is initializing</summary>
        Initializing,
        /// <summary>AI is thinking/processing</summary>
        Thinking,
        /// <summary>An action is being executed</summary>
        Executing,
        /// <summary>Recording completed successfully</summary>
        Completed,
        /// <summary>Recording was cancelled</summary>
        Cancelled,
        /// <summary>Recording failed with an error</summary>
        Failed
    }

    /// <summary>
    /// Event args for when a block is added during recording.
    /// </summary>
    public class BlockAddedEventArgs : EventArgs
    {
        public VisualBlock Block { get; set; }
        public int Index { get; set; }
        public byte[] ScreenshotPng { get; set; }
    }

    /// <summary>
    /// Event args for recording completion.
    /// </summary>
    public class RecordingCompletedEventArgs : EventArgs
    {
        public VisualTest Test { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public int ActionCount { get; set; }
        public float DurationSeconds { get; set; }
    }

    /// <summary>
    /// Event args for recording failure.
    /// </summary>
    public class RecordingFailedEventArgs : EventArgs
    {
        public string Error { get; set; }
        public Exception Exception { get; set; }
    }

    /// <summary>
    /// Hooks into AITestRunner to capture actions as they execute and converts them to VisualBlocks.
    /// Builds a VisualTest asset as recording progresses.
    /// </summary>
    public class AIRecordingSession : IDisposable
    {
        private readonly string prompt;
        private readonly string passCondition;
        private readonly string startScene;

        private VisualTest recordedTest;
        private AITestRunner runner;
        private CancellationTokenSource cts;

        private RecordingState state = RecordingState.Idle;
        private float startTime;
        private byte[] latestScreenshot;
        private string currentStatus;
        private bool isDisposed;

        /// <summary>
        /// Current state of the recording session.
        /// </summary>
        public RecordingState State => state;

        /// <summary>
        /// Current status message for display.
        /// </summary>
        public string Status => currentStatus;

        /// <summary>
        /// Number of blocks recorded so far.
        /// </summary>
        public int BlockCount => recordedTest?.blocks?.Count ?? 0;

        /// <summary>
        /// The VisualTest being built during recording.
        /// </summary>
        public VisualTest RecordedTest => recordedTest;

        /// <summary>
        /// Most recent screenshot captured during recording.
        /// </summary>
        public byte[] LatestScreenshot => latestScreenshot;

        /// <summary>
        /// Duration of the recording so far.
        /// </summary>
        public float ElapsedSeconds => state == RecordingState.Idle ? 0 : Time.realtimeSinceStartup - startTime;

        /// <summary>
        /// Event fired when a new block is added during recording.
        /// </summary>
        public event EventHandler<BlockAddedEventArgs> OnBlockAdded;

        /// <summary>
        /// Event fired when recording completes successfully.
        /// </summary>
        public event EventHandler<RecordingCompletedEventArgs> OnRecordingComplete;

        /// <summary>
        /// Event fired when recording fails.
        /// </summary>
        public event EventHandler<RecordingFailedEventArgs> OnRecordingFailed;

        /// <summary>
        /// Event fired when the recording state changes.
        /// </summary>
        public event EventHandler<RecordingState> OnStateChanged;

        /// <summary>
        /// Event fired when a screenshot is captured.
        /// </summary>
        public event EventHandler<byte[]> OnScreenshotCaptured;

        /// <summary>
        /// Event fired when AI provides reasoning.
        /// </summary>
        public event EventHandler<string> OnReasoningReceived;

        /// <summary>
        /// Creates a new AI recording session.
        /// </summary>
        /// <param name="prompt">The natural language description of what to test</param>
        /// <param name="passCondition">The condition that determines test success</param>
        /// <param name="startScene">Optional scene name to record in the test</param>
        public AIRecordingSession(string prompt, string passCondition, string startScene = null)
        {
            this.prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
            this.passCondition = passCondition ?? throw new ArgumentNullException(nameof(passCondition));
            this.startScene = startScene;
        }

        /// <summary>
        /// Starts the AI recording session.
        /// </summary>
        public async UniTask<VisualTest> StartAsync(CancellationToken externalCt = default)
        {
            if (state != RecordingState.Idle)
            {
                throw new InvalidOperationException($"Cannot start recording in state: {state}");
            }

            cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            var ct = cts.Token;

            try
            {
                SetState(RecordingState.Initializing);
                startTime = Time.realtimeSinceStartup;
                currentStatus = "Initializing AI recording...";

                // Create the VisualTest that will be built during recording
                recordedTest = ScriptableObject.CreateInstance<VisualTest>();
                recordedTest.testName = GenerateTestName(prompt);
                recordedTest.description = $"AI-generated test from prompt: {prompt}";
                recordedTest.originalPrompt = prompt;
                recordedTest.passCondition = passCondition;
                recordedTest.startScene = startScene;

                // Create a temporary AITest to drive the recording
                var aiTest = CreateAITest();

                // Create the runner and hook into events
                runner = new AITestRunner(aiTest, GlobalKnowledge.CreateDefault(), CreateConfig());

                runner.OnScreenCaptured += HandleScreenCaptured;
                runner.OnActionExecuted += HandleActionExecuted;
                runner.OnReasoning += HandleReasoning;
                runner.OnTestCompleted += HandleTestCompleted;

                SetState(RecordingState.Thinking);
                currentStatus = "AI is analyzing the screen...";

                // Run the AI test
                var result = await runner.RunAsync(ct);

                // Process the result
                if (result.IsSuccess)
                {
                    SetState(RecordingState.Completed);
                    currentStatus = $"Recording completed: {result.Message}";

                    OnRecordingComplete?.Invoke(this, new RecordingCompletedEventArgs
                    {
                        Test = recordedTest,
                        Success = true,
                        Message = result.Message,
                        ActionCount = recordedTest.blocks.Count,
                        DurationSeconds = result.DurationSeconds
                    });

                    return recordedTest;
                }
                else if (result.Status == TestStatus.Cancelled)
                {
                    SetState(RecordingState.Cancelled);
                    currentStatus = "Recording cancelled";
                    return null;
                }
                else
                {
                    SetState(RecordingState.Failed);
                    currentStatus = $"Recording failed: {result.Message}";

                    OnRecordingFailed?.Invoke(this, new RecordingFailedEventArgs
                    {
                        Error = result.Message
                    });

                    return null;
                }
            }
            catch (OperationCanceledException)
            {
                SetState(RecordingState.Cancelled);
                currentStatus = "Recording cancelled";
                return null;
            }
            catch (Exception ex)
            {
                SetState(RecordingState.Failed);
                currentStatus = $"Recording error: {ex.Message}";

                OnRecordingFailed?.Invoke(this, new RecordingFailedEventArgs
                {
                    Error = ex.Message,
                    Exception = ex
                });

                Debug.LogError($"[AIRecordingSession] Error: {ex}");
                return null;
            }
            finally
            {
                UnhookRunner();
            }
        }

        /// <summary>
        /// Cancels the recording session.
        /// </summary>
        public void Cancel()
        {
            if (state == RecordingState.Idle || state == RecordingState.Completed ||
                state == RecordingState.Cancelled || state == RecordingState.Failed)
            {
                return;
            }

            try
            {
                cts?.Cancel();
                runner?.Cancel();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AIRecordingSession] Exception during cancel: {ex.Message}");
            }

            SetState(RecordingState.Cancelled);
            currentStatus = "Recording cancelled";
        }

        /// <summary>
        /// Disposes resources used by the session.
        /// </summary>
        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;

            Cancel();
            UnhookRunner();

            try
            {
                cts?.Dispose();
            }
            catch { }

            cts = null;
            runner = null;
        }

        private void SetState(RecordingState newState)
        {
            if (state != newState)
            {
                state = newState;
                OnStateChanged?.Invoke(this, newState);
            }
        }

        private void UnhookRunner()
        {
            if (runner != null)
            {
                runner.OnScreenCaptured -= HandleScreenCaptured;
                runner.OnActionExecuted -= HandleActionExecuted;
                runner.OnReasoning -= HandleReasoning;
                runner.OnTestCompleted -= HandleTestCompleted;
            }
        }

        private AITest CreateAITest()
        {
            var aiTest = ScriptableObject.CreateInstance<AITest>();
            aiTest.prompt = prompt;
            aiTest.passCondition = passCondition;
            aiTest.startingTier = ModelTier.GeminiFlash;
            aiTest.maxActions = 50;
            aiTest.timeoutSeconds = 300f;
            aiTest.actionDelaySeconds = 0.5f; // Slightly slower for recording visual feedback
            return aiTest;
        }

        private AITestRunnerConfig CreateConfig()
        {
            return new AITestRunnerConfig
            {
                MaxContextTokens = 8000,
                CompactionThreshold = 0.8f,
                EnableHistoryReplay = false, // Don't replay history when recording
                SendScreenshots = true // Send screenshots for better AI understanding
            };
        }

        private void HandleScreenCaptured(ScreenState screen)
        {
            if (screen?.ScreenshotPng != null)
            {
                latestScreenshot = screen.ScreenshotPng;
                OnScreenshotCaptured?.Invoke(this, latestScreenshot);
            }

            SetState(RecordingState.Thinking);
            currentStatus = "AI is analyzing the screen...";
        }

        private void HandleReasoning(string reasoning)
        {
            OnReasoningReceived?.Invoke(this, reasoning);
        }

        private void HandleActionExecuted(AIAction action, ActionResult result)
        {
            if (action == null) return;

            // Skip terminal actions (Pass/Fail) - they don't become blocks
            if (action is PassAction || action is FailAction || action is ScreenshotAction)
            {
                return;
            }

            SetState(RecordingState.Executing);
            currentStatus = $"Executing: {action.Description}";

            // Convert AIAction to VisualBlock
            var block = ConvertActionToBlock(action);
            if (block != null)
            {
                recordedTest.blocks.Add(block);

                OnBlockAdded?.Invoke(this, new BlockAddedEventArgs
                {
                    Block = block,
                    Index = recordedTest.blocks.Count - 1,
                    ScreenshotPng = latestScreenshot
                });
            }
        }

        private void HandleTestCompleted(AITestResult result)
        {
            // This is handled in StartAsync after RunAsync returns
        }

        /// <summary>
        /// Converts an AIAction to a VisualBlock.
        /// </summary>
        private VisualBlock ConvertActionToBlock(AIAction action)
        {
            var block = new VisualBlock
            {
                id = Guid.NewGuid().ToString()
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
                    // Unknown action type, skip it
                    Debug.LogWarning($"[AIRecordingSession] Unknown action type: {action.GetType().Name}");
                    return null;
            }

            // Add action description as comment
            block.comment = action.Description;

            return block;
        }

        /// <summary>
        /// Creates an ElementSelector from element ID and info.
        /// </summary>
        private ElementSelector CreateSelector(string elementId, ElementInfo elementInfo)
        {
            if (elementInfo != null)
            {
                // Prefer text-based selector if text is available
                if (!string.IsNullOrEmpty(elementInfo.text))
                {
                    return ElementSelector.ByText(elementInfo.text, elementInfo.text);
                }

                // Fall back to name-based selector
                if (!string.IsNullOrEmpty(elementInfo.name))
                {
                    return ElementSelector.ByName(elementInfo.name, elementInfo.name);
                }
            }

            // Fall back to ID-based selector
            if (!string.IsNullOrEmpty(elementId))
            {
                return ElementSelector.ById(elementId, elementId);
            }

            return null;
        }

        /// <summary>
        /// Generates a test name from the prompt.
        /// </summary>
        private string GenerateTestName(string prompt)
        {
            if (string.IsNullOrEmpty(prompt))
                return "AI Generated Test";

            // Take first 50 characters, trim at word boundary
            var name = prompt.Length <= 50 ? prompt : prompt.Substring(0, 50);
            var lastSpace = name.LastIndexOf(' ');
            if (lastSpace > 20 && name.Length == 50)
            {
                name = name.Substring(0, lastSpace);
            }

            // Remove problematic characters for file names
            name = name.Replace('/', '-').Replace('\\', '-').Replace(':', '-')
                       .Replace('*', '-').Replace('?', '-').Replace('"', '-')
                       .Replace('<', '-').Replace('>', '-').Replace('|', '-');

            return name.Trim();
        }
    }
}
