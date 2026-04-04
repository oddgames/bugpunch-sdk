using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ODDGames.UIAutomation.AI
{
    /// <summary>
    /// Provides AI-powered navigation to help recover from blocked actions.
    /// When an ActionExecutor action fails, this can analyze the screen and
    /// attempt to navigate around obstacles (dialogs, popups, etc).
    /// Uses the same multi-turn conversation flow as AI tests.
    /// </summary>
    public static class AINavigator
    {
        private static IModelProvider _modelProvider;
        private static bool _isNavigating;

        /// <summary>
        /// Maximum turns for recovery attempts (keep low - recovery should be quick).
        /// </summary>
        public static int MaxRecoveryTurns { get; set; } = 5;

        /// <summary>
        /// Timeout in seconds for recovery attempts.
        /// </summary>
        public static float RecoveryTimeoutSeconds { get; set; } = 30f;

        /// <summary>
        /// Sets the model provider to use for AI navigation.
        /// Call this once at test setup (e.g., in UITestBase or a [SetUpFixture]).
        /// </summary>
        /// <example>
        /// // In Editor context:
        /// AINavigator.SetModelProvider(AITestSettings.Instance.CreateModelProvider());
        ///
        /// // Or manually:
        /// AINavigator.SetModelProvider(new GeminiToolProvider(apiKey, "gemini-2.5-flash"));
        /// </example>
        public static void SetModelProvider(IModelProvider provider)
        {
            _modelProvider = provider;
        }

        /// <summary>
        /// Whether AI navigation is currently in progress.
        /// </summary>
        public static bool IsNavigating => _isNavigating;

        /// <summary>
        /// Attempts to navigate around obstacles blocking an action.
        /// Uses a multi-turn conversation loop (same as AI tests) to analyze
        /// the screen and clear obstacles through multiple attempts if needed.
        /// </summary>
        /// <param name="failedAction">Description of the action that failed</param>
        /// <param name="errorMessage">The error message from the failed action</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Result indicating whether obstacles were cleared</returns>
        public static async Task<NavigationResult> TryNavigateAsync(
            string failedAction,
            string errorMessage,
            CancellationToken ct = default)
        {
            if (_modelProvider == null)
            {
                return new NavigationResult
                {
                    Success = false,
                    Explanation = "AI navigation not available - no model provider configured. Call AINavigator.SetModelProvider() first."
                };
            }

            if (_isNavigating)
            {
                return new NavigationResult
                {
                    Success = false,
                    Explanation = "AI navigation already in progress"
                };
            }

            _isNavigating = true;

            try
            {
                Debug.Log($"[AINavigator] Action failed: {failedAction} - {errorMessage}");
                Debug.Log("[AINavigator] Starting multi-turn recovery loop...");

                // Capture initial screen state
                var screenState = await AIScreenCapture.CaptureAsync(annotateScreenshot: true);

                // Build system prompt for recovery
                var systemPrompt = GetRecoverySystemPrompt();

                // Build task prompt with context about the failure
                var taskPrompt = BuildRecoveryTaskPrompt(failedAction, errorMessage);

                // Create agentic loop with recovery configuration
                var loop = new AgenticLoop(
                    _modelProvider,
                    systemPrompt,
                    taskPrompt,
                    new AgenticLoopConfig
                    {
                        MaxTurns = MaxRecoveryTurns,
                        TimeoutSeconds = RecoveryTimeoutSeconds,
                        ActionDelaySeconds = 0.2f,
                        SendScreenshots = true,
                        EnableDebugLogging = true
                    }
                );

                // Track actions for explanation
                var actionsExecuted = new List<string>();
                loop.OnActionExecuting += action =>
                {
                    Debug.Log($"[AINavigator] Executing: {action.Name}");
                    actionsExecuted.Add(action.Name);
                };

                // Run the recovery loop
                var result = await loop.RunAsync(
                    screenState.ScreenshotPng,
                    screenState.GetElementListPrompt(),
                    ExecuteRecoveryActionAsync,
                    ct
                );

                // Build explanation
                var explanation = new System.Text.StringBuilder();
                explanation.AppendLine($"Blocked action: {failedAction}");
                explanation.AppendLine($"Error: {errorMessage}");

                if (actionsExecuted.Count > 0)
                {
                    explanation.AppendLine($"Recovery steps: {string.Join(" → ", actionsExecuted)}");
                }

                // Interpret result
                if (result.Passed)
                {
                    // "cleared" was called - obstacle successfully removed
                    explanation.AppendLine($"Result: {result.Message}");
                    return new NavigationResult
                    {
                        Success = true,
                        NoBlockerFound = false,
                        Explanation = explanation.ToString()
                    };
                }
                else if (result.Message?.Contains("no_blocker") == true ||
                         result.Message?.Contains("no blocker") == true ||
                         result.Message?.Contains("genuine") == true)
                {
                    // AI determined there's no blocker - this is a real test failure
                    explanation.AppendLine($"Result: {result.Message}");
                    return new NavigationResult
                    {
                        Success = false,
                        NoBlockerFound = true,
                        Explanation = explanation.ToString()
                    };
                }
                else
                {
                    // Max turns, timeout, or error - recovery failed
                    explanation.AppendLine($"Recovery failed: {result.Message}");
                    return new NavigationResult
                    {
                        Success = false,
                        NoBlockerFound = false,
                        Explanation = explanation.ToString()
                    };
                }
            }
            catch (OperationCanceledException)
            {
                return new NavigationResult
                {
                    Success = false,
                    Explanation = "AI navigation was cancelled"
                };
            }
            catch (Exception ex)
            {
                return new NavigationResult
                {
                    Success = false,
                    Explanation = $"AI navigation error: {ex.Message}"
                };
            }
            finally
            {
                _isNavigating = false;
            }
        }

        /// <summary>
        /// Executes a recovery action from the agentic loop.
        /// </summary>
        private static async Task<AgenticActionResult> ExecuteRecoveryActionAsync(ToolCall toolCall, CancellationToken ct)
        {
            // Capture current screen state
            var screen = await AIScreenCapture.CaptureAsync(annotateScreenshot: true);

            // Handle terminal actions for recovery
            if (toolCall.Name == "cleared" || toolCall.Name == "pass")
            {
                return new AgenticActionResult
                {
                    Success = true,
                    IsTerminal = true,
                    TerminalPassed = true,
                    TerminalReason = toolCall.GetString("reason", "Obstacle cleared successfully")
                };
            }

            if (toolCall.Name == "no_blocker" || toolCall.Name == "fail")
            {
                return new AgenticActionResult
                {
                    Success = true,
                    IsTerminal = true,
                    TerminalPassed = false,
                    TerminalReason = toolCall.GetString("reason", "No blocker found - genuine test failure")
                };
            }

            try
            {
                // Parse and execute action
                var action = AIActionExecutor.Parse(toolCall, screen);
                var actionResult = await AIActionExecutor.ExecuteAsync(action, ct);

                // Capture new state
                var newScreen = await AIScreenCapture.CaptureAsync(annotateScreenshot: true);

                return new AgenticActionResult
                {
                    Success = actionResult.Success,
                    Error = actionResult.Error,
                    ScreenshotAfter = newScreen.ScreenshotPng,
                    ElementListJson = newScreen.GetElementListPrompt(),
                    ResultMessage = actionResult.Success
                        ? $"{action.Description} - Success"
                        : $"{action.Description} - Failed: {actionResult.Error}"
                };
            }
            catch (Exception ex)
            {
                return new AgenticActionResult
                {
                    Success = false,
                    Error = ex.Message,
                    ResultMessage = $"Action failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Builds the system prompt for recovery mode.
        /// Uses conversation flow similar to AI tests but focused on obstacle clearing.
        /// </summary>
        private static string GetRecoverySystemPrompt()
        {
            return @"You are a UI recovery assistant for automated testing. A test action has FAILED, and your job is to analyze the screen and clear any obstacles blocking the test.

=== CONTEXT ===
An automated test tried to perform an action but it failed. Your goal is to:
1. Identify what's blocking the test (dialogs, popups, wrong screen, loading)
2. Take actions to clear the obstacle
3. Call 'cleared' when the obstacle is removed
4. Call 'no_blocker' if there's genuinely nothing blocking (real test bug)

=== AVAILABLE ACTIONS ===
- click: Click to dismiss dialogs, close popups, press Back, etc.
- scroll: Scroll to find hidden elements
- swipe: Navigate between screens
- wait: Wait for loading/animations
- key_press: Press Escape to close dialogs, etc.
- cleared: Call when you've successfully cleared the obstacle
- no_blocker: Call when there's NO blocker - the test has a genuine bug

=== WORKFLOW ===
1. Analyze the screenshot and element list
2. Look for blockers: dialogs, popups, overlays, loading indicators
3. If blocker found: take ONE action to dismiss it
4. After action: check if obstacle is cleared
5. If cleared: call 'cleared' with reason
6. If no blocker exists: call 'no_blocker' with reason

=== WHEN TO CALL 'cleared' ===
- You clicked a dismiss button and the dialog closed
- You navigated away from a blocking screen
- The loading finished and UI is now accessible
- After 1-2 actions when the obstacle appears gone

=== WHEN TO CALL 'no_blocker' ===
- The screen looks normal with no dialogs/popups/overlays
- The target element simply doesn't exist on this screen
- You've tried 2-3 actions and nothing helps
- This appears to be a genuine test bug, not a flaky test

=== IMPORTANT ===
- DO NOT try to perform the original failed action
- Focus ONLY on clearing obstacles
- Take ONE action at a time
- Call 'cleared' or 'no_blocker' as soon as you know the answer";
        }

        /// <summary>
        /// Builds the task prompt describing the failure and goal.
        /// </summary>
        private static string BuildRecoveryTaskPrompt(string failedAction, string errorMessage)
        {
            return $@"## RECOVERY TASK

**Failed Action:** {failedAction}
**Error:** {errorMessage}

The test will automatically retry the original action after you clear any obstacles.
Your job is to:
1. Find what's blocking the test
2. Clear the obstacle (click dismiss, scroll, wait, etc.)
3. Call 'cleared' when done, or 'no_blocker' if nothing is blocking

What do you see on the screen? Is there something blocking the test?";
        }
    }

    /// <summary>
    /// Result of an AI navigation attempt.
    /// </summary>
    public class NavigationResult
    {
        /// <summary>Whether navigation succeeded and the original action now works.</summary>
        public bool Success { get; set; }

        /// <summary>
        /// Whether the AI determined there was no blocker present.
        /// If true, the test failure is genuine (not flaky) and should fail normally.
        /// If false, AI attempted navigation (test may be flaky).
        /// </summary>
        public bool NoBlockerFound { get; set; }

        /// <summary>AI's explanation of what was blocking and what was done.</summary>
        public string Explanation { get; set; }
    }
}
