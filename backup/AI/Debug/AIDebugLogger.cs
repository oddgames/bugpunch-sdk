using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
                    var sb = new StringBuilder();
                    sb.AppendLine("[");
                    for (int i = 0; i < screen.Elements.Count; i++)
                    {
                        var e = screen.Elements[i];
                        sb.AppendLine("  {");
                        sb.AppendLine($"    \"name\": {JsonEscape(e.name)},");
                        sb.AppendLine($"    \"text\": {JsonEscape(e.text)},");
                        sb.AppendLine($"    \"type\": {JsonEscape(e.type)},");
                        sb.AppendLine($"    \"bounds\": {{ \"x\": {e.bounds.x}, \"y\": {e.bounds.y}, \"width\": {e.bounds.width}, \"height\": {e.bounds.height} }},");
                        sb.AppendLine($"    \"isEnabled\": {e.isEnabled.ToString().ToLower()},");
                        sb.AppendLine($"    \"path\": {JsonEscape(e.path)},");
                        sb.AppendLine($"    \"searchPattern\": {JsonEscape(e.GetSearchPattern(e.needsDisambiguation))}");
                        sb.Append("  }");
                        if (i < screen.Elements.Count - 1) sb.Append(",");
                        sb.AppendLine();
                    }
                    sb.AppendLine("]");
                    File.WriteAllText(Path.Combine(stepFolder, "elements.json"), sb.ToString());

                    // Also save element list prompt (what AI sees)
                    var elementPrompt = screen.GetElementListPrompt();
                    File.WriteAllText(Path.Combine(stepFolder, "elements_prompt.txt"), elementPrompt);
                }

                // Save screen state metadata
                var screenMeta = new StringBuilder();
                screenMeta.AppendLine("{");
                screenMeta.AppendLine($"  \"ScreenHash\": {JsonEscape(screen?.ScreenHash)},");
                screenMeta.AppendLine($"  \"ElementStateHash\": {JsonEscape(screen?.ElementStateHash)},");
                screenMeta.AppendLine($"  \"ElementCount\": {screen?.Elements?.Count ?? 0},");
                screenMeta.AppendLine($"  \"Timestamp\": {JsonEscape(DateTime.Now.ToString("HH:mm:ss.fff"))}");
                screenMeta.AppendLine("}");
                File.WriteAllText(Path.Combine(stepFolder, "screen_meta.json"), screenMeta.ToString());

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
                    var tcJson = new StringBuilder();
                    tcJson.AppendLine("[");
                    for (int i = 0; i < toolCalls.Count; i++)
                    {
                        var tc = toolCalls[i];
                        tcJson.AppendLine("  {");
                        tcJson.AppendLine($"    \"Id\": {JsonEscape(tc.Id)},");
                        tcJson.AppendLine($"    \"Name\": {JsonEscape(tc.Name)},");
                        tcJson.Append($"    \"Arguments\": ");
                        if (tc.Arguments != null)
                        {
                            tcJson.AppendLine("{");
                            var argKeys = new List<string>(tc.Arguments.Keys);
                            for (int j = 0; j < argKeys.Count; j++)
                            {
                                var key = argKeys[j];
                                var val = tc.Arguments[key];
                                tcJson.Append($"      {JsonEscape(key)}: {JsonEscape(val?.ToString())}");
                                if (j < argKeys.Count - 1) tcJson.Append(",");
                                tcJson.AppendLine();
                            }
                            tcJson.AppendLine("    }");
                        }
                        else
                        {
                            tcJson.AppendLine("null");
                        }
                        tcJson.Append("  }");
                        if (i < toolCalls.Count - 1) tcJson.Append(",");
                        tcJson.AppendLine();
                    }
                    tcJson.AppendLine("]");
                    File.WriteAllText(Path.Combine(stepFolder, "tool_calls.json"), tcJson.ToString());
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

                var actionJson = new StringBuilder();
                actionJson.AppendLine("{");
                actionJson.AppendLine($"  \"ActionType\": {JsonEscape(action?.ActionType)},");
                actionJson.AppendLine($"  \"Description\": {JsonEscape(action?.Description)},");
                actionJson.AppendLine($"  \"SearchQuery\": {searchQueryJson ?? "null"},");
                actionJson.AppendLine($"  \"Success\": {(result?.Success ?? false).ToString().ToLower()},");
                actionJson.AppendLine($"  \"Error\": {JsonEscape(result?.Error)},");
                actionJson.AppendLine($"  \"ScreenChanged\": {(result?.ScreenChanged ?? false).ToString().ToLower()},");
                actionJson.AppendLine($"  \"ScreenHashAfter\": {JsonEscape(result?.ScreenHashAfter)},");
                actionJson.AppendLine($"  \"Timestamp\": {JsonEscape(DateTime.Now.ToString("HH:mm:ss.fff"))}");
                actionJson.AppendLine("}");

                File.WriteAllText(Path.Combine(stepFolder, "action_result.json"), actionJson.ToString());

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
                var configJson = new StringBuilder();
                configJson.AppendLine("{");
                configJson.AppendLine($"  \"TestName\": {JsonEscape(test?.name)},");
                configJson.AppendLine($"  \"Prompt\": {JsonEscape(test?.prompt)},");
                configJson.AppendLine($"  \"Model\": {JsonEscape(modelName)},");
                configJson.AppendLine($"  \"MaxActions\": {test?.maxActions ?? 0},");
                configJson.AppendLine($"  \"TimeoutSeconds\": {test?.timeoutSeconds ?? 0},");
                configJson.AppendLine($"  \"ActionDelaySeconds\": {test?.actionDelaySeconds ?? 0},");
                configJson.AppendLine($"  \"SendScreenshots\": {(config?.SendScreenshots ?? false).ToString().ToLower()},");
                configJson.AppendLine($"  \"EnableHistoryReplay\": {(config?.EnableHistoryReplay ?? false).ToString().ToLower()},");
                configJson.AppendLine($"  \"StartedAt\": {JsonEscape(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))}");
                configJson.AppendLine("}");

                File.WriteAllText(Path.Combine(sessionFolder, "config.json"), configJson.ToString());

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
                var resultJson = new StringBuilder();
                resultJson.AppendLine("{");
                resultJson.AppendLine($"  \"Status\": {JsonEscape(result?.Status.ToString())},");
                resultJson.AppendLine($"  \"Message\": {JsonEscape(result?.Message)},");
                resultJson.AppendLine($"  \"ActionCount\": {result?.ActionCount ?? 0},");
                resultJson.AppendLine($"  \"DurationSeconds\": {result?.DurationSeconds ?? 0},");
                resultJson.AppendLine($"  \"FinalModel\": {JsonEscape(result?.FinalModel)},");
                resultJson.AppendLine($"  \"CompletedAt\": {JsonEscape(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))}");
                resultJson.AppendLine("}");

                File.WriteAllText(Path.Combine(sessionFolder, "result.json"), resultJson.ToString());

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
                var summaryJson = new StringBuilder();
                summaryJson.AppendLine("[");
                for (int i = 0; i < steps.Count; i++)
                {
                    var step = steps[i];
                    summaryJson.AppendLine("  {");
                    summaryJson.AppendLine($"    \"StepNumber\": {step.StepNumber},");
                    summaryJson.AppendLine($"    \"Timestamp\": {JsonEscape(step.Timestamp.ToString("o"))},");
                    summaryJson.AppendLine($"    \"ScreenHash\": {JsonEscape(step.ScreenHash)},");
                    summaryJson.AppendLine($"    \"ElementCount\": {step.ElementCount},");
                    summaryJson.AppendLine($"    \"Reasoning\": {JsonEscape(step.Reasoning)},");
                    summaryJson.AppendLine($"    \"ToolCallCount\": {step.ToolCallCount},");
                    summaryJson.AppendLine($"    \"ToolCalls\": {JsonEscape(step.ToolCalls)}");
                    summaryJson.Append("  }");
                    if (i < steps.Count - 1) summaryJson.Append(",");
                    summaryJson.AppendLine();
                }
                summaryJson.AppendLine("]");
                File.WriteAllText(Path.Combine(sessionFolder, "steps_summary.json"), summaryJson.ToString());

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
            return str.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static string JsonEscape(string str)
        {
            if (str == null) return "null";
            return "\"" + str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
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
