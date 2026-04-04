using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

namespace ODDGames.UIAutomation.AI
{
    /// <summary>
    /// Callback for executing actions in the agentic loop.
    /// Returns the action result and new screenshot for the next turn.
    /// </summary>
    public delegate Task<AgenticActionResult> AgenticActionCallback(ToolCall action, CancellationToken ct);

    /// <summary>
    /// Result of executing an agentic action.
    /// </summary>
    public class AgenticActionResult
    {
        /// <summary>Whether the action succeeded</summary>
        public bool Success { get; set; }

        /// <summary>Error message if failed</summary>
        public string Error { get; set; }

        /// <summary>Screenshot after action (PNG bytes)</summary>
        public byte[] ScreenshotAfter { get; set; }

        /// <summary>Element list JSON after action</summary>
        public string ElementListJson { get; set; }

        /// <summary>Whether this is a terminal action (pass/fail)</summary>
        public bool IsTerminal { get; set; }

        /// <summary>Terminal reason if IsTerminal is true</summary>
        public string TerminalReason { get; set; }

        /// <summary>Terminal status (passed/failed)</summary>
        public bool TerminalPassed { get; set; }

        /// <summary>Result message to send back to the model</summary>
        public string ResultMessage { get; set; }
    }

    /// <summary>
    /// Configuration for the agentic loop.
    /// </summary>
    public class AgenticLoopConfig
    {
        /// <summary>Maximum number of turns (API round-trips)</summary>
        public int MaxTurns { get; set; } = 50;

        /// <summary>Timeout in seconds</summary>
        public float TimeoutSeconds { get; set; } = 180f;

        /// <summary>Delay between actions in seconds</summary>
        public float ActionDelaySeconds { get; set; } = 0.3f;

        /// <summary>Whether to enable debug logging</summary>
        public bool EnableDebugLogging { get; set; } = true;

        /// <summary>Whether to send screenshots with each request</summary>
        public bool SendScreenshots { get; set; } = true;
    }

    /// <summary>
    /// Result of running the agentic loop.
    /// </summary>
    public class AgenticLoopResult
    {
        /// <summary>Whether the test passed</summary>
        public bool Passed { get; set; }

        /// <summary>Final message/reason</summary>
        public string Message { get; set; }

        /// <summary>Number of actions executed</summary>
        public int ActionCount { get; set; }

        /// <summary>Total tokens used</summary>
        public int TokensUsed { get; set; }

        /// <summary>Duration in seconds</summary>
        public float DurationSeconds { get; set; }

        /// <summary>List of actions executed</summary>
        public List<ToolCall> Actions { get; set; } = new List<ToolCall>();

        /// <summary>Error if loop failed</summary>
        public string Error { get; set; }

        /// <summary>Whether the loop was cancelled</summary>
        public bool Cancelled { get; set; }

        /// <summary>Whether the loop timed out</summary>
        public bool TimedOut { get; set; }

        /// <summary>Whether max turns was reached</summary>
        public bool MaxTurnsReached { get; set; }
    }

    /// <summary>
    /// Implements a generic agentic loop for AI-driven UI testing.
    /// This loop sends screenshots to the model, receives actions, executes them,
    /// and sends the results back with new screenshots until completion.
    /// Works with any IModelProvider (Gemini standard, Computer Use, etc.).
    /// </summary>
    public class AgenticLoop
    {
        private IModelProvider provider;
        private readonly AgenticLoopConfig config;
        private readonly string systemPrompt;
        private readonly string taskPrompt;

        /// <summary>
        /// Event fired when an action is about to be executed.
        /// </summary>
        public event Action<ToolCall> OnActionExecuting;

        /// <summary>
        /// Event fired when an action has been executed.
        /// </summary>
        public event Action<ToolCall, AgenticActionResult> OnActionExecuted;

        /// <summary>
        /// Event fired when the model provides reasoning.
        /// </summary>
        public event Action<string> OnReasoning;

        /// <summary>
        /// Event fired when a new turn starts.
        /// </summary>
        public event Action<int> OnTurnStarted;

        /// <summary>
        /// Gets or sets the model provider. Can be swapped during execution.
        /// </summary>
        public IModelProvider Provider
        {
            get => provider;
            set => provider = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Gets the current model ID.
        /// </summary>
        public string CurrentModel => provider?.ModelId;

        /// <summary>
        /// Creates a new agentic loop.
        /// </summary>
        /// <param name="provider">The model provider (can be any IModelProvider)</param>
        /// <param name="systemPrompt">System prompt for the model</param>
        /// <param name="taskPrompt">Task description prompt</param>
        /// <param name="config">Loop configuration</param>
        public AgenticLoop(
            IModelProvider provider,
            string systemPrompt,
            string taskPrompt,
            AgenticLoopConfig config = null)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            this.systemPrompt = systemPrompt;
            this.taskPrompt = taskPrompt;
            this.config = config ?? new AgenticLoopConfig();
        }

        /// <summary>
        /// Runs the agentic loop.
        /// </summary>
        /// <param name="initialScreenshot">Initial screenshot (PNG bytes)</param>
        /// <param name="initialElementList">Initial element list JSON</param>
        /// <param name="actionCallback">Callback to execute actions</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Loop result</returns>
        public async Task<AgenticLoopResult> RunAsync(
            byte[] initialScreenshot,
            string initialElementList,
            AgenticActionCallback actionCallback,
            CancellationToken ct = default)
        {
            if (actionCallback == null)
                throw new ArgumentNullException(nameof(actionCallback));

            var result = new AgenticLoopResult();
            var startTime = Time.realtimeSinceStartup;
            var currentScreenshot = initialScreenshot;
            var currentElementList = initialElementList;

            // Track tool results for the next request
            var pendingToolResults = new List<(string id, string result)>();

            try
            {
                // Build initial request
                var request = new ModelRequest
                {
                    SystemPrompt = systemPrompt,
                    ScreenshotPng = config.SendScreenshots ? currentScreenshot : null,
                    ElementListJson = currentElementList,
                    MaxTokens = 4096,
                    Temperature = 0.2f
                };

                // Add task prompt as first user message
                if (!string.IsNullOrEmpty(taskPrompt))
                {
                    request.Messages.Add(new Message
                    {
                        Role = "user",
                        Content = taskPrompt,
                        Screenshot = config.SendScreenshots ? currentScreenshot : null
                    });
                }

                // Main loop
                int turn = 0;
                while (turn < config.MaxTurns)
                {
                    ct.ThrowIfCancellationRequested();

                    // Check timeout
                    if (Time.realtimeSinceStartup - startTime > config.TimeoutSeconds)
                    {
                        result.TimedOut = true;
                        result.Message = $"Timeout after {config.TimeoutSeconds}s";
                        break;
                    }

                    turn++;
                    OnTurnStarted?.Invoke(turn);
                    Log($"Turn {turn}/{config.MaxTurns} (model: {provider.ModelId})");

                    // Send request
                    var response = await provider.CompleteAsync(request, ct);

                    if (!response.Success)
                    {
                        result.Error = response.Error;
                        result.Message = $"API error: {response.Error}";
                        break;
                    }

                    result.TokensUsed += response.TokensUsed;

                    // Handle reasoning
                    if (!string.IsNullOrEmpty(response.Reasoning))
                    {
                        OnReasoning?.Invoke(response.Reasoning);
                        Log($"Reasoning: {response.Reasoning}");
                    }

                    // No tool calls - model is done or needs prompting
                    if (response.ToolCalls == null || response.ToolCalls.Count == 0)
                    {
                        Log("No tool calls in response");

                        // Check if raw content indicates completion
                        if (!string.IsNullOrEmpty(response.RawContent) &&
                            (response.RawContent.ToLower().Contains("complete") ||
                             response.RawContent.ToLower().Contains("finished") ||
                             response.RawContent.ToLower().Contains("done") ||
                             response.RawContent.ToLower().Contains("passed")))
                        {
                            result.Passed = true;
                            result.Message = response.RawContent;
                            break;
                        }

                        // Add assistant message and prompt to continue
                        request.Messages.Add(new Message
                        {
                            Role = "assistant",
                            Content = response.RawContent
                        });
                        request.Messages.Add(new Message
                        {
                            Role = "user",
                            Content = "Please continue with the next action. Use one of the available tools.",
                            Screenshot = config.SendScreenshots ? currentScreenshot : null
                        });
                        continue;
                    }

                    // Add assistant response with tool calls to conversation
                    request.Messages.Add(new Message
                    {
                        Role = "assistant",
                        Content = response.RawContent,
                        ToolCalls = response.ToolCalls
                    });

                    // Process each tool call
                    pendingToolResults.Clear();
                    foreach (var toolCall in response.ToolCalls)
                    {
                        ct.ThrowIfCancellationRequested();

                        result.Actions.Add(toolCall);
                        result.ActionCount++;

                        OnActionExecuting?.Invoke(toolCall);
                        Log($"Executing action: {toolCall.Name}");

                        // Execute the action
                        var actionResult = await actionCallback(toolCall, ct);
                        OnActionExecuted?.Invoke(toolCall, actionResult);

                        // Check for terminal action
                        if (actionResult.IsTerminal)
                        {
                            result.Passed = actionResult.TerminalPassed;
                            result.Message = actionResult.TerminalReason;
                            result.DurationSeconds = Time.realtimeSinceStartup - startTime;
                            return result;
                        }

                        // Collect tool result
                        var resultMessage = actionResult.ResultMessage;
                        if (string.IsNullOrEmpty(resultMessage))
                        {
                            resultMessage = actionResult.Success
                                ? "Action executed successfully."
                                : $"Action failed: {actionResult.Error}";
                        }
                        pendingToolResults.Add((toolCall.Id, resultMessage));

                        // Update current state
                        if (actionResult.ScreenshotAfter != null)
                        {
                            currentScreenshot = actionResult.ScreenshotAfter;
                        }
                        if (actionResult.ElementListJson != null)
                        {
                            currentElementList = actionResult.ElementListJson;
                        }

                        // Add action delay
                        if (config.ActionDelaySeconds > 0)
                        {
                            await DelaySafe((int)(config.ActionDelaySeconds * 1000), ct);
                        }
                    }

                    // Add tool results to conversation
                    foreach (var (id, resultMsg) in pendingToolResults)
                    {
                        request.Messages.Add(new Message
                        {
                            Role = "user",
                            Content = resultMsg,
                            ToolCallId = id
                        });
                    }

                    // Add new screen state message with updated screenshot
                    var stateMessage = BuildStateMessage(currentElementList);
                    request.Messages.Add(new Message
                    {
                        Role = "user",
                        Content = stateMessage,
                        Screenshot = config.SendScreenshots ? currentScreenshot : null
                    });

                    // Update request with current state
                    request.ScreenshotPng = config.SendScreenshots ? currentScreenshot : null;
                    request.ElementListJson = currentElementList;
                }

                // Max turns reached
                if (turn >= config.MaxTurns)
                {
                    result.MaxTurnsReached = true;
                    result.Message = $"Max turns ({config.MaxTurns}) reached";
                }
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                result.Message = "Cancelled";
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                result.Message = $"Error: {ex.Message}";
                Log($"Exception: {ex}");
            }

            result.DurationSeconds = Time.realtimeSinceStartup - startTime;
            return result;
        }

        /// <summary>
        /// Builds a state message describing the current screen.
        /// </summary>
        private string BuildStateMessage(string elementListJson)
        {
            if (string.IsNullOrEmpty(elementListJson))
            {
                return "Here is the updated screen state. What action should I take next?";
            }

            return $"Here is the updated screen state:\n{elementListJson}\n\nWhat action should I take next?";
        }

        private async Task DelaySafe(int milliseconds, CancellationToken ct)
        {
            if (!Application.isPlaying || ct.IsCancellationRequested)
                return;

            try
            {
                await Task.Delay(milliseconds, ct);
            }
            catch
            {
                // Cancelled or shutting down
            }
        }

        private void Log(string message)
        {
            if (config.EnableDebugLogging)
            {
                Debug.Log($"[AgenticLoop] {message}");
            }
        }
    }

    // Backwards compatibility aliases
    /// <summary>Alias for AgenticActionCallback for backwards compatibility.</summary>
    public delegate Task<AgenticActionResult> ComputerUseActionCallback(ToolCall action, CancellationToken ct);

    /// <summary>Alias for AgenticActionResult for backwards compatibility.</summary>
    public class ComputerUseActionResult : AgenticActionResult { }

    /// <summary>Alias for AgenticLoopConfig for backwards compatibility.</summary>
    public class ComputerUseLoopConfig : AgenticLoopConfig { }

    /// <summary>Alias for AgenticLoopResult for backwards compatibility.</summary>
    public class ComputerUseLoopResult : AgenticLoopResult { }

    /// <summary>Alias for AgenticLoop for backwards compatibility.</summary>
    public class ComputerUseLoop : AgenticLoop
    {
        public ComputerUseLoop(
            IModelProvider provider,
            string systemPrompt,
            string taskPrompt,
            AgenticLoopConfig config = null)
            : base(provider, systemPrompt, taskPrompt, config)
        {
        }
    }
}
