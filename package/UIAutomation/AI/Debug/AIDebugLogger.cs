using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace ODDGames.UIAutomation.AI
{
    /// <summary>
    /// Debug logger that captures all AI test execution data to a timestamped folder.
    /// Saves screenshots, element lists, prompts, responses, and actions for each step.
    /// </summary>
    public class AIDebugLogger : IDisposable
    {
        private readonly string sessionFolder;
        private readonly string sessionId;
        private int stepCount;
        private bool disposed;

        private readonly List<StepSummary> steps = new List<StepSummary>();
        private StringBuilder sessionLog = new StringBuilder();

        public string SessionFolder => sessionFolder;
        public bool IsEnabled { get; private set; }

        public AIDebugLogger(bool enabled = true)
        {
            IsEnabled = enabled;

            if (!enabled)
                return;

            sessionId = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            sessionFolder = Path.Combine(Application.dataPath, "..", "AIDebug", sessionId);

            try
            {
                Directory.CreateDirectory(sessionFolder);
                Log($"Debug session started: {sessionId}");
                Log($"Output folder: {sessionFolder}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AIDebugLogger] Failed to create debug folder: {ex.Message}");
                IsEnabled = false;
            }
        }

        /// <summary>
        /// Log a step with screenshot, elements, prompt sent to AI, and response received.
        /// </summary>
        public void LogStep(
            int stepNumber,
            ScreenState screen,
            string promptSentToAI,
            string aiReasoning,
            List<ToolCall> toolCalls,
            string rawResponse)
        {
            if (!IsEnabled)
            {
                Debug.Log("[AIDebugLogger] LogStep skipped - not enabled");
                return;
            }

            Debug.Log($"[AIDebugLogger] LogStep({stepNumber}) starting...");
            stepCount = stepNumber;
            var stepFolder = Path.Combine(sessionFolder, $"step_{stepNumber:D3}");

            try
            {
                Directory.CreateDirectory(stepFolder);
                Debug.Log($"[AIDebugLogger] Created folder: {stepFolder}");

                // Save screenshot (may be annotated depending on capture settings)
                if (screen?.ScreenshotPng != null)
                {
                    var screenshotPath = Path.Combine(stepFolder, "screenshot.png");
                    File.WriteAllBytes(screenshotPath, screen.ScreenshotPng);
                }

                // Save element list as JSON
                if (screen?.Elements != null)
                {
                    // Create simplified element data to avoid Unity type serialization issues
                    var elementsData = screen.Elements.Select(e => new
                    {
                        e.name,
                        e.text,
                        e.type,
                        bounds = new { e.bounds.x, e.bounds.y, e.bounds.width, e.bounds.height },
                        e.isEnabled,
                        e.path,
                        searchPattern = e.GetSearchPattern(e.needsDisambiguation)
                    }).ToList();
                    var elementsJson = JsonConvert.SerializeObject(elementsData, Formatting.Indented);
                    File.WriteAllText(Path.Combine(stepFolder, "elements.json"), elementsJson);

                    // Also save element list prompt (what AI sees)
                    var elementPrompt = screen.GetElementListPrompt();
                    File.WriteAllText(Path.Combine(stepFolder, "elements_prompt.txt"), elementPrompt);
                }

                // Save screen state metadata
                var screenMeta = new
                {
                    ScreenHash = screen?.ScreenHash,
                    ElementStateHash = screen?.ElementStateHash,
                    ElementCount = screen?.Elements?.Count ?? 0,
                    Timestamp = DateTime.Now.ToString("HH:mm:ss.fff")
                };
                File.WriteAllText(
                    Path.Combine(stepFolder, "screen_meta.json"),
                    JsonConvert.SerializeObject(screenMeta, Formatting.Indented));

                // Save prompt sent to AI
                if (!string.IsNullOrEmpty(promptSentToAI))
                {
                    File.WriteAllText(Path.Combine(stepFolder, "prompt.txt"), promptSentToAI);
                }

                // Save AI reasoning
                if (!string.IsNullOrEmpty(aiReasoning))
                {
                    File.WriteAllText(Path.Combine(stepFolder, "reasoning.txt"), aiReasoning);
                }

                // Save tool calls
                if (toolCalls != null && toolCalls.Count > 0)
                {
                    var toolCallsData = new List<object>();
                    foreach (var tc in toolCalls)
                    {
                        toolCallsData.Add(new
                        {
                            Id = tc.Id,
                            Name = tc.Name,
                            Arguments = tc.Arguments
                        });
                    }
                    File.WriteAllText(
                        Path.Combine(stepFolder, "tool_calls.json"),
                        JsonConvert.SerializeObject(toolCallsData, Formatting.Indented));
                }

                // Save raw response
                if (!string.IsNullOrEmpty(rawResponse))
                {
                    File.WriteAllText(Path.Combine(stepFolder, "raw_response.txt"), rawResponse);
                }

                // Track step summary
                Debug.Log($"[AIDebugLogger] Creating step summary...");
                var toolCallNames = "";
                if (toolCalls != null && toolCalls.Count > 0)
                {
                    var names = new List<string>();
                    foreach (var tc in toolCalls)
                    {
                        names.Add(tc.Name ?? "null");
                    }
                    toolCallNames = string.Join(", ", names);
                }

                var summary = new StepSummary
                {
                    StepNumber = stepNumber,
                    Timestamp = DateTime.Now,
                    ScreenHash = screen?.ScreenHash,
                    ElementCount = screen?.Elements?.Count ?? 0,
                    Reasoning = TruncateString(aiReasoning, 200),
                    ToolCallCount = toolCalls?.Count ?? 0,
                    ToolCalls = toolCallNames
                };
                steps.Add(summary);

                Log($"Step {stepNumber}: {summary.ElementCount} elements, {summary.ToolCallCount} tool calls");
                Debug.Log($"[AIDebugLogger] LogStep({stepNumber}) completed successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AIDebugLogger] Failed to log step {stepNumber}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Log an action execution result.
        /// </summary>
        public void LogAction(
            int stepNumber,
            AIAction action,
            ActionResult result,
            string searchQueryJson)
        {
            if (!IsEnabled) return;

            var stepFolder = Path.Combine(sessionFolder, $"step_{stepNumber:D3}");

            try
            {
                Directory.CreateDirectory(stepFolder);

                var actionData = new
                {
                    ActionType = action?.ActionType,
                    Description = action?.Description,
                    SearchQuery = searchQueryJson,
                    Success = result?.Success ?? false,
                    Error = result?.Error,
                    ScreenChanged = result?.ScreenChanged ?? false,
                    ScreenHashAfter = result?.ScreenHashAfter,
                    Timestamp = DateTime.Now.ToString("HH:mm:ss.fff")
                };

                File.WriteAllText(
                    Path.Combine(stepFolder, "action_result.json"),
                    JsonConvert.SerializeObject(actionData, Formatting.Indented));

                Log($"Action: {action?.ActionType} - Success: {result?.Success}, Error: {result?.Error ?? "none"}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AIDebugLogger] Failed to log action: {ex.Message}");
            }
        }

        /// <summary>
        /// Log the system prompt at the start of the session.
        /// </summary>
        public void LogSystemPrompt(string systemPrompt)
        {
            if (!IsEnabled) return;

            try
            {
                File.WriteAllText(Path.Combine(sessionFolder, "system_prompt.txt"), systemPrompt);
                Log("System prompt saved");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AIDebugLogger] Failed to log system prompt: {ex.Message}");
            }
        }

        /// <summary>
        /// Log test configuration.
        /// </summary>
        public void LogConfig(AITest test, AITestRunnerConfig config, string modelName)
        {
            if (!IsEnabled) return;

            try
            {
                var configData = new
                {
                    TestName = test?.name,
                    Prompt = test?.prompt,
                    Model = modelName,
                    MaxActions = test?.maxActions ?? 0,
                    TimeoutSeconds = test?.timeoutSeconds ?? 0,
                    ActionDelaySeconds = test?.actionDelaySeconds ?? 0,
                    SendScreenshots = config?.SendScreenshots ?? false,
                    EnableHistoryReplay = config?.EnableHistoryReplay ?? false,
                    StartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                File.WriteAllText(
                    Path.Combine(sessionFolder, "config.json"),
                    JsonConvert.SerializeObject(configData, Formatting.Indented));

                Log($"Test: {test?.name}");
                Log($"Prompt: {test?.prompt}");
                Log($"Model: {modelName}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AIDebugLogger] Failed to log config: {ex.Message}");
            }
        }

        /// <summary>
        /// Log the final test result.
        /// </summary>
        public void LogResult(AITestResult result)
        {
            if (!IsEnabled) return;

            try
            {
                var resultData = new
                {
                    Status = result?.Status.ToString(),
                    Message = result?.Message,
                    ActionCount = result?.ActionCount ?? 0,
                    DurationSeconds = result?.DurationSeconds ?? 0,
                    FinalModel = result?.FinalModel,
                    CompletedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                File.WriteAllText(
                    Path.Combine(sessionFolder, "result.json"),
                    JsonConvert.SerializeObject(resultData, Formatting.Indented));

                Log($"Result: {result?.Status} - {result?.Message}");
                Log($"Duration: {result?.DurationSeconds:F1}s, Actions: {result?.ActionCount}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AIDebugLogger] Failed to log result: {ex.Message}");
            }
        }

        /// <summary>
        /// Log a general message.
        /// </summary>
        public void Log(string message)
        {
            if (!IsEnabled) return;

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var logLine = $"[{timestamp}] {message}";
            sessionLog.AppendLine(logLine);
            Debug.Log($"[AIDebug] {logLine}");
        }

        /// <summary>
        /// Save the session summary and close.
        /// </summary>
        public void Dispose()
        {
            if (disposed || !IsEnabled) return;
            disposed = true;

            try
            {
                // Save session log
                File.WriteAllText(
                    Path.Combine(sessionFolder, "session.log"),
                    sessionLog.ToString());

                // Save step summary
                var summaryJson = JsonConvert.SerializeObject(steps, Formatting.Indented);
                File.WriteAllText(
                    Path.Combine(sessionFolder, "steps_summary.json"),
                    summaryJson);

                // Create index.html for easy browsing
                CreateIndexHtml();

                Log($"Debug session complete: {steps.Count} steps logged");
                Debug.Log($"[AIDebugLogger] Session saved to: {sessionFolder}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AIDebugLogger] Failed to save session: {ex.Message}");
            }
        }

        private void CreateIndexHtml()
        {
            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html><head>");
            html.AppendLine("<title>AI Debug Session</title>");
            html.AppendLine("<style>");
            html.AppendLine("body { font-family: Arial, sans-serif; margin: 20px; background: #1e1e1e; color: #d4d4d4; }");
            html.AppendLine("h1 { color: #569cd6; }");
            html.AppendLine("h2 { color: #4ec9b0; margin-top: 30px; }");
            html.AppendLine(".step { background: #2d2d2d; padding: 15px; margin: 10px 0; border-radius: 5px; }");
            html.AppendLine(".step img { max-width: 400px; margin: 10px 0; border: 1px solid #444; }");
            html.AppendLine(".success { color: #4ec9b0; }");
            html.AppendLine(".error { color: #f14c4c; }");
            html.AppendLine("pre { background: #1e1e1e; padding: 10px; overflow-x: auto; border: 1px solid #444; }");
            html.AppendLine("a { color: #569cd6; }");
            html.AppendLine("</style>");
            html.AppendLine("</head><body>");

            html.AppendLine($"<h1>AI Debug Session: {sessionId}</h1>");
            html.AppendLine("<p><a href='config.json'>Config</a> | <a href='system_prompt.txt'>System Prompt</a> | <a href='result.json'>Result</a> | <a href='session.log'>Full Log</a></p>");

            foreach (var step in steps)
            {
                html.AppendLine($"<div class='step'>");
                html.AppendLine($"<h2>Step {step.StepNumber}</h2>");
                html.AppendLine($"<p>Time: {step.Timestamp:HH:mm:ss} | Elements: {step.ElementCount} | Tools: {step.ToolCalls}</p>");

                var stepDir = $"step_{step.StepNumber:D3}";
                html.AppendLine($"<p><a href='{stepDir}/screenshot.png'>Screenshot</a>");
                html.AppendLine($" | <a href='{stepDir}/elements.json'>Elements JSON</a>");
                html.AppendLine($" | <a href='{stepDir}/elements_prompt.txt'>Elements Prompt</a>");
                html.AppendLine($" | <a href='{stepDir}/prompt.txt'>Prompt</a>");
                html.AppendLine($" | <a href='{stepDir}/reasoning.txt'>Reasoning</a>");
                html.AppendLine($" | <a href='{stepDir}/tool_calls.json'>Tool Calls</a>");
                html.AppendLine($" | <a href='{stepDir}/action_result.json'>Action Result</a></p>");

                html.AppendLine($"<img src='{stepDir}/screenshot.png' alt='Step {step.StepNumber}'/>");

                if (!string.IsNullOrEmpty(step.Reasoning))
                {
                    html.AppendLine($"<pre>{HtmlEncode(step.Reasoning)}</pre>");
                }

                html.AppendLine("</div>");
            }

            html.AppendLine("</body></html>");

            File.WriteAllText(Path.Combine(sessionFolder, "index.html"), html.ToString());
        }

        private static string TruncateString(string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str)) return str;
            return str.Length <= maxLength ? str : str.Substring(0, maxLength) + "...";
        }

        private static string HtmlEncode(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            return str
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }

        private class StepSummary
        {
            public int StepNumber;
            public DateTime Timestamp;
            public string ScreenHash;
            public int ElementCount;
            public string Reasoning;
            public int ToolCallCount;
            public string ToolCalls;
        }
    }
}
