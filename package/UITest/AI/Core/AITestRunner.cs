using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ODDGames.UITest.AI
{
    /// <summary>
    /// Main execution engine for AI-powered UI tests.
    /// </summary>
    public class AITestRunner
    {
        private static AITestRunner currentRunner;
        private static bool initialized;

        /// <summary>
        /// Initialize cleanup handlers on domain load.
        /// </summary>
#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void InitializeEditor()
        {
            if (initialized) return;
            initialized = true;

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Debug.Log("[AITest] Exiting play mode - cancelling any running tests");
                CancelAll();
            }
        }

        private static void OnBeforeAssemblyReload()
        {
            Debug.Log("[AITest] Assembly reload - cancelling any running tests");
            CancelAll();
        }

        /// <summary>
        /// Cancels all running tests immediately.
        /// </summary>
        public static void CancelAll()
        {
            try
            {
                var runner = currentRunner;
                currentRunner = null; // Clear immediately to prevent re-entry

                if (runner != null)
                {
                    runner.Cancel();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AITest] Exception in CancelAll: {ex.Message}");
            }
        }
#endif

        /// <summary>
        /// Gets the currently running test runner, if any.
        /// </summary>
        public static AITestRunner Current => currentRunner;

        /// <summary>
        /// Whether a test is currently running.
        /// </summary>
        public static bool IsRunning => currentRunner != null && currentRunner.isRunning;

        private readonly AITest test;
        private readonly GlobalKnowledge globalKnowledge;
        private readonly AITestRunnerConfig config;

        private ConversationManager conversation;
        private IModelProvider modelProvider;
        private StuckDetector stuckDetector;
        private HistoryReplayer historyReplayer;

        private bool isRunning;
        private int actionCount;
        private float startTime;
        private CancellationTokenSource cts;

        private List<AITestActionRecord> actionHistory = new List<AITestActionRecord>();
        private List<ScreenshotRecord> screenshotHistory = new List<ScreenshotRecord>();
        private List<string> logs = new List<string>();

        /// <summary>
        /// Current action being executed.
        /// </summary>
        public int CurrentAction => actionCount;

        /// <summary>
        /// Total actions allowed.
        /// </summary>
        public int MaxActions => test.maxActions;

        /// <summary>
        /// Current model being used.
        /// </summary>
        public string CurrentModel => modelProvider?.ModelId;

        /// <summary>
        /// Conversation statistics.
        /// </summary>
        public ConversationStats ConversationStats => conversation?.GetStats();

        /// <summary>
        /// Current stuck level.
        /// </summary>
        public int StuckLevel => stuckDetector?.StuckLevel ?? 0;

        /// <summary>
        /// Event fired when test starts.
        /// </summary>
        public event Action<AITest> OnTestStarted;

        /// <summary>
        /// Event fired when a screenshot is captured.
        /// </summary>
        public event Action<ScreenState> OnScreenCaptured;

        /// <summary>
        /// Event fired when AI provides reasoning.
        /// </summary>
        public event Action<string> OnReasoning;

        /// <summary>
        /// Event fired when an action is executed.
        /// </summary>
        public event Action<AIAction, ActionResult> OnActionExecuted;

        /// <summary>
        /// Event fired when test completes.
        /// </summary>
        public event Action<AITestResult> OnTestCompleted;

        public AITestRunner(AITest test, GlobalKnowledge globalKnowledge = null, AITestRunnerConfig config = null)
        {
            this.test = test ?? throw new ArgumentNullException(nameof(test));
            this.globalKnowledge = globalKnowledge ?? GlobalKnowledge.CreateDefault();
            this.config = config ?? new AITestRunnerConfig();
        }

        /// <summary>
        /// Runs the AI test.
        /// </summary>
        public async UniTask<AITestResult> RunAsync(CancellationToken externalCt = default)
        {
            if (isRunning)
            {
                throw new InvalidOperationException("Test is already running");
            }

            currentRunner = this;
            isRunning = true;
            actionCount = 0;
            startTime = Time.realtimeSinceStartup;

            actionHistory.Clear();
            screenshotHistory.Clear();
            logs.Clear();

            cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            var ct = cts.Token;

            try
            {
                Log($"Starting AI test: {test.name}");
                OnTestStarted?.Invoke(test);

                // Initialize components
                InitializeComponents();

                // Try history replay first
                var replayResult = await TryHistoryReplayAsync(ct);
                if (replayResult.IsSuccess)
                {
                    Log("Test passed via history replay");
                    return CreateResult(TestStatus.Passed, "Passed via history replay");
                }

                // Set up conversation
                var systemPrompt = KnowledgeBuilder.BuildSystemPrompt(test, globalKnowledge);
                conversation.SetSystemMessage(systemPrompt);

                // Main execution loop
                while (actionCount < test.maxActions)
                {
                    ct.ThrowIfCancellationRequested();

                    // Check timeout
                    if (Time.realtimeSinceStartup - startTime > test.timeoutSeconds)
                    {
                        Log("Test timed out");
                        return CreateResult(TestStatus.TimedOut, $"Timeout after {test.timeoutSeconds}s");
                    }

                    // Capture current screen
                    var screen = await AIScreenCapture.CaptureAsync(annotateScreenshot: true);
                    OnScreenCaptured?.Invoke(screen);
                    RecordScreenshot(screen);

                    // Update stuck detector
                    stuckDetector.RecordScreen(screen.ScreenHash);

                    // Check if stuck and send recovery message
                    if (stuckDetector.ShouldEscalate())
                    {
                        Log($"Detected stuck state: {stuckDetector.StuckReason}");

                        // Send recovery message
                        var recoveryMsg = KnowledgeBuilder.BuildRecoveryMessage(
                            screen,
                            conversation.GetActionSummary(),
                            stuckDetector.StuckReason);
                        // Only send screenshot if configured (text-only mode is faster)
                        var screenshotForAI = config.SendScreenshots ? screen.ScreenshotPng : null;
                        conversation.AddUserMessage(recoveryMsg, screenshotForAI);
                    }
                    else
                    {
                        // Build regular step message
                        var stepMsg = KnowledgeBuilder.BuildStepMessage(
                            screen,
                            actionCount + 1,
                            test.maxActions,
                            stuckDetector.GetStuckSuggestions());
                        // Only send screenshot if configured (text-only mode is faster)
                        var screenshotForAI = config.SendScreenshots ? screen.ScreenshotPng : null;
                        conversation.AddUserMessage(stepMsg, screenshotForAI);
                    }

                    // Send to AI model - only include screenshot if configured
                    var screenshotForRequest = config.SendScreenshots ? screen.ScreenshotPng : null;
                    var request = conversation.BuildRequest(screenshotForRequest, screen.GetElementListPrompt());

                    if (modelProvider == null)
                    {
                        return CreateResult(TestStatus.Error, "No model provider configured");
                    }

                    Log($"Sending request to {modelProvider.Name}...");
                    Log($"Request has {request.Messages.Count} messages, screenshot: {screenshotForRequest != null}");

                    ModelResponse response;
                    try
                    {
                        response = await modelProvider.CompleteAsync(request, ct);
                        Log($"Received response: Success={response.Success}, ToolCalls={response.ToolCalls?.Count ?? 0}");
                    }
                    catch (Exception ex)
                    {
                        Log($"Model error: {ex.Message}");

                        // Check for cancellation
                        if (ct.IsCancellationRequested || !Application.isPlaying ||
                            ex.Message.Contains("cancelled") || ex.Message.Contains("aborted"))
                        {
                            return CreateResult(TestStatus.Cancelled, "Test was cancelled");
                        }

                        return CreateResult(TestStatus.Error, $"Model error: {ex.Message}");
                    }

                    if (!response.Success)
                    {
                        Log($"Model request failed: {response.Error}");

                        // Check for cancellation
                        if (ct.IsCancellationRequested || !Application.isPlaying ||
                            response.Error.Contains("cancelled") || response.Error.Contains("aborted"))
                        {
                            return CreateResult(TestStatus.Cancelled, "Test was cancelled");
                        }

                        return CreateResult(TestStatus.Error, response.Error);
                    }

                    // Record AI reasoning
                    if (!string.IsNullOrEmpty(response.Reasoning))
                    {
                        OnReasoning?.Invoke(response.Reasoning);
                        Log($"AI reasoning: {response.Reasoning}");
                    }

                    conversation.AddAssistantMessage(response.RawContent, response.ToolCalls);

                    // Process tool calls
                    if (response.ToolCalls == null || response.ToolCalls.Count == 0)
                    {
                        Log("No tool calls in response, prompting for action");
                        stuckDetector.RecordAction("none", "", false);
                        continue;
                    }

                    foreach (var toolCall in response.ToolCalls)
                    {
                        ct.ThrowIfCancellationRequested();

                        // Parse action
                        AIAction action;
                        try
                        {
                            action = AIActionExecutor.Parse(toolCall, screen);
                        }
                        catch (Exception ex)
                        {
                            Log($"Failed to parse action: {ex.Message}");
                            stuckDetector.RecordAction(toolCall.Name, "", false);
                            continue;
                        }

                        // Check for terminal actions
                        if (action is PassAction passAction)
                        {
                            Log($"Test PASSED: {passAction.Reason}");
                            RecordSuccessfulRun(screen.ScreenHash);
                            return CreateResult(TestStatus.Passed, passAction.Reason);
                        }

                        if (action is FailAction failAction)
                        {
                            Log($"Test FAILED: {failAction.Reason}");
                            return CreateResult(TestStatus.Failed, failAction.Reason);
                        }

                        // Handle screenshot request - send screenshot in next message
                        if (action is ScreenshotAction screenshotAction)
                        {
                            Log($"AI requested screenshot: {screenshotAction.Reason}");
                            // Send screenshot with the next request
                            conversation.AddUserMessage(
                                "Here is the screenshot you requested. Based on this visual, what action should I take?",
                                screen.ScreenshotPng);
                            conversation.AddToolResult(toolCall.Id, "Screenshot provided");
                            continue;
                        }

                        // Execute action
                        actionCount++;
                        Log($"Executing action {actionCount}: {action.Description}");

                        var result = await AIActionExecutor.ExecuteAsync(action, ct);
                        OnActionExecuted?.Invoke(action, result);
                        RecordAction(action, result, response.Reasoning);

                        stuckDetector.RecordAction(
                            action.ActionType,
                            GetActionTarget(action),
                            result.Success);

                        if (!result.Success)
                        {
                            Log($"Action failed: {result.Error}");
                            conversation.AddToolResult(toolCall.Id, $"Action failed: {result.Error}");
                        }
                        else
                        {
                            conversation.AddToolResult(toolCall.Id, "Action executed successfully");
                            stuckDetector.RecordProgress();
                        }

                        // Delay between actions for visual feedback
                        if (!Application.isPlaying || ct.IsCancellationRequested)
                        {
                            Log("Test cancelled during action delay");
                            return CreateResult(TestStatus.Cancelled, "Test was cancelled");
                        }

                        try
                        {
                            await UniTask.Delay((int)(test.actionDelaySeconds * 1000), ignoreTimeScale: true, cancellationToken: ct);
                        }
                        catch (OperationCanceledException)
                        {
                            Log("Test cancelled during action delay");
                            return CreateResult(TestStatus.Cancelled, "Test was cancelled");
                        }
                        catch
                        {
                            // Unity shutting down
                            return CreateResult(TestStatus.Cancelled, "Unity exiting");
                        }
                    }
                }

                Log("Max actions reached without pass/fail");
                return CreateResult(TestStatus.MaxActionsReached, $"Reached {test.maxActions} actions without completion");
            }
            catch (OperationCanceledException)
            {
                Log("Test cancelled");
                return CreateResult(TestStatus.Cancelled, "Test was cancelled");
            }
            catch (Exception ex)
            {
                Log($"Test error: {ex}");
                return CreateResult(TestStatus.Error, ex.Message);
            }
            finally
            {
                Log("Test cleanup - clearing state");
                isRunning = false;
                currentRunner = null;
                try { cts?.Dispose(); } catch { }
                cts = null;
            }
        }

        /// <summary>
        /// Cancels the running test.
        /// </summary>
        public void Cancel()
        {
            Debug.Log("[AITest] Cancel requested");
            isRunning = false;

            try
            {
                cts?.Cancel();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AITest] Exception during cancel: {ex.Message}");
            }

            try
            {
                cts?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AITest] Exception during CTS dispose: {ex.Message}");
            }

            cts = null;

            // Clear static reference immediately
            if (currentRunner == this)
            {
                currentRunner = null;
            }
        }

        private void InitializeComponents()
        {
            // Initialize conversation manager
            conversation = new ConversationManager();

            // Set up model provider
            modelProvider = config.ModelProvider;
            if (modelProvider != null)
            {
                Log($"Using model: {modelProvider.Name}");
            }

            // Initialize stuck detector
            stuckDetector = new StuckDetector(config.StuckDetectorConfig);
            stuckDetector.Reset();

            // Initialize history replayer
            historyReplayer = new HistoryReplayer(config.HistoryReplayerConfig);
        }

        private async UniTask<ReplayResult> TryHistoryReplayAsync(CancellationToken ct)
        {
            if (!config.EnableHistoryReplay)
            {
                return new ReplayResult { Status = ReplayStatus.NoHistory };
            }

            // Capture initial screen
            var screen = await AIScreenCapture.CaptureAsync(annotateScreenshot: false);
            var bestRun = test.GetBestHistoricalRun(screen.ScreenHash);

            if (bestRun == null)
            {
                Log("No suitable history found for replay");
                return new ReplayResult { Status = ReplayStatus.NoHistory };
            }

            Log($"Attempting history replay ({bestRun.actions.Count} actions)");
            var result = await historyReplayer.ReplayAsync(bestRun, screen.ScreenHash, ct);

            if (result.ShouldFallbackToAI)
            {
                Log($"History replay diverged at action {result.DivergedAtAction}, falling back to AI");
            }

            return result;
        }

        private void RecordSuccessfulRun(string startingScreenHash)
        {
            var sequence = new ActionSequence
            {
                timestampTicks = DateTime.UtcNow.Ticks,
                screenHashAtStart = startingScreenHash
            };

            foreach (var record in actionHistory)
            {
                sequence.actions.Add(new RecordedAction
                {
                    actionType = record.ActionType,
                    target = record.Target,
                    parameters = record.Parameters,
                    screenHashAfter = record.ScreenHashAfter
                });
            }

            test.RecordSuccessfulRun(sequence);
        }

        private AITestResult CreateResult(TestStatus status, string message)
        {
            float duration = Time.realtimeSinceStartup - startTime;

            var result = new AITestResult
            {
                TestName = test.name,
                GroupName = test.Group?.name,
                Status = status,
                Message = message,
                ActionCount = actionCount,
                FinalModel = modelProvider?.ModelId ?? test.model,
                DurationSeconds = duration,
                Actions = new List<AITestActionRecord>(actionHistory),
                Screenshots = new List<ScreenshotRecord>(screenshotHistory),
                Logs = new List<string>(logs)
            };

            OnTestCompleted?.Invoke(result);
            return result;
        }

        private void RecordAction(AIAction action, ActionResult result, string reasoning)
        {
            actionHistory.Add(new AITestActionRecord
            {
                Index = actionCount,
                Timestamp = Time.realtimeSinceStartup - startTime,
                ActionType = action.ActionType,
                Target = GetActionTarget(action),
                Success = result.Success,
                Error = result.Error,
                Reasoning = reasoning,
                ScreenHashAfter = result.ScreenHashAfter
            });
        }

        private void RecordScreenshot(ScreenState screen)
        {
            screenshotHistory.Add(new ScreenshotRecord
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = Time.realtimeSinceStartup - startTime,
                ScreenHash = screen.ScreenHash,
                ElementCount = screen.Elements?.Count ?? 0,
                ScreenshotPng = screen.ScreenshotPng
            });
        }

        private string GetActionTarget(AIAction action)
        {
            return action switch
            {
                ClickAction click => click.ElementId ?? $"({click.ScreenPosition?.x:F2},{click.ScreenPosition?.y:F2})",
                TypeAction type => type.ElementId,
                DragAction drag => drag.FromElementId,
                ScrollAction scroll => scroll.ElementId,
                WaitAction wait => $"{wait.Seconds}s",
                PassAction pass => pass.Reason,
                FailAction fail => fail.Reason,
                _ => ""
            };
        }

        private void Log(string message)
        {
            var timestamp = Time.realtimeSinceStartup - startTime;
            var logEntry = $"[{timestamp:F1}s] {message}";
            logs.Add(logEntry);
            Debug.Log($"[AITest] {logEntry}");
        }
    }

    /// <summary>
    /// Configuration for the AI test runner.
    /// </summary>
    [Serializable]
    public class AITestRunnerConfig
    {
        /// <summary>Whether to try replaying history before AI execution</summary>
        public bool EnableHistoryReplay = true;

        /// <summary>Stuck detector configuration</summary>
        public StuckDetectorConfig StuckDetectorConfig = new StuckDetectorConfig();

        /// <summary>History replayer configuration</summary>
        public HistoryReplayerConfig HistoryReplayerConfig = new HistoryReplayerConfig();

        /// <summary>Whether to send screenshots to AI (false = text-only mode for faster execution)</summary>
        public bool SendScreenshots = false;

        /// <summary>The model provider to use for AI test execution</summary>
        public IModelProvider ModelProvider;
    }

    /// <summary>
    /// Status of a completed test.
    /// </summary>
    public enum TestStatus
    {
        Passed,
        Failed,
        TimedOut,
        MaxActionsReached,
        Cancelled,
        Error
    }

    /// <summary>
    /// Result of an AI test run.
    /// </summary>
    public class AITestResult
    {
        public string TestName { get; set; }
        public string GroupName { get; set; }
        public TestStatus Status { get; set; }
        public string Message { get; set; }
        public int ActionCount { get; set; }
        public string FinalModel { get; set; }
        public float DurationSeconds { get; set; }
        public List<AITestActionRecord> Actions { get; set; }
        public List<ScreenshotRecord> Screenshots { get; set; }
        public List<string> Logs { get; set; }

        public bool IsSuccess => Status == TestStatus.Passed;
    }

    /// <summary>
    /// Record of an action executed during a test.
    /// </summary>
    [Serializable]
    public class AITestActionRecord
    {
        public int Index;
        public float Timestamp;
        public string ActionType;
        public string Target;
        public Dictionary<string, object> Parameters;
        public bool Success;
        public string Error;
        public string Reasoning;
        public string ScreenHashAfter;
    }

    /// <summary>
    /// Record of a screenshot captured during a test.
    /// </summary>
    [Serializable]
    public class ScreenshotRecord
    {
        public string Id;
        public float Timestamp;
        public string ScreenHash;
        public int ElementCount;
        public byte[] ScreenshotPng;
    }
}
