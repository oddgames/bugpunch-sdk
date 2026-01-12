using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace ODDGames.UITest.AI
{
    /// <summary>
    /// Model provider for LM Studio (OpenAI-compatible API at localhost).
    /// Supports vision models like LLaVA, BakLLaVA, etc.
    /// Uses structured output (JSON schema) for reliable action parsing.
    /// </summary>
    public class LMStudioProvider : IModelProvider
    {
        private const string DefaultEndpoint = "http://localhost:1234/v1/chat/completions";
        private const int DefaultTimeoutSeconds = 120;

        private readonly string endpoint;
        private readonly string modelName;
        private readonly int timeoutSeconds;
        private readonly bool useStructuredOutput;

        public string Name => "LM Studio (Local)";
        public ModelTier Tier => ModelTier.LocalFast;
        public bool SupportsVision => true; // Depends on loaded model
        public bool SupportsToolCalling => true; // Via structured output
        public int ContextWindowSize => 8192; // Varies by model

        public LMStudioProvider(string endpoint = null, string modelName = null, int timeoutSeconds = DefaultTimeoutSeconds, bool useStructuredOutput = true)
        {
            this.endpoint = string.IsNullOrEmpty(endpoint) ? DefaultEndpoint : endpoint;
            if (!this.endpoint.EndsWith("/v1/chat/completions"))
            {
                this.endpoint = this.endpoint.TrimEnd('/') + "/v1/chat/completions";
            }
            this.modelName = modelName;
            this.timeoutSeconds = timeoutSeconds;
            this.useStructuredOutput = useStructuredOutput;
        }

        public async UniTask<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken ct = default)
        {
            var startTime = Time.realtimeSinceStartup;

            try
            {
                var messages = BuildMessages(request);
                var requestBody = BuildRequestBody(request, messages);
                var jsonBody = JsonUtility.ToJson(new OpenAIRequestWrapper { json = requestBody });

                // Use the actual JSON, not the wrapper
                jsonBody = SerializeRequest(requestBody);

                var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
                using var webRequest = new UnityWebRequest(endpoint, "POST");
                using var uploadHandler = new UploadHandlerRaw(bodyBytes);
                using var downloadHandler = new DownloadHandlerBuffer();
                webRequest.uploadHandler = uploadHandler;
                webRequest.downloadHandler = downloadHandler;
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.timeout = timeoutSeconds;

                var operation = webRequest.SendWebRequest();

                while (!operation.isDone)
                {
                    if (ct.IsCancellationRequested || !Application.isPlaying)
                    {
                        webRequest.Abort();
                        return ModelResponse.Failed("Request cancelled");
                    }

                    try
                    {
                        // Use time-based delay instead of frame-based to avoid hanging
                        await UniTask.Delay(100, ignoreTimeScale: true, cancellationToken: ct);
                    }
                    catch (OperationCanceledException)
                    {
                        webRequest.Abort();
                        return ModelResponse.Failed("Request cancelled");
                    }
                    catch
                    {
                        // Unity may be shutting down
                        webRequest.Abort();
                        return ModelResponse.Failed("Request aborted");
                    }
                }

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    return ModelResponse.Failed($"HTTP error: {webRequest.error}");
                }

                var responseJson = webRequest.downloadHandler.text;
                return ParseResponse(responseJson, startTime);
            }
            catch (Exception ex)
            {
                return ModelResponse.Failed($"Exception: {ex.Message}");
            }
        }

        public async UniTask<bool> TestConnectionAsync(CancellationToken ct = default)
        {
            try
            {
                var testEndpoint = endpoint.Replace("/chat/completions", "/models");

                using var webRequest = UnityWebRequest.Get(testEndpoint);
                webRequest.timeout = 5;

                var operation = webRequest.SendWebRequest();

                while (!operation.isDone)
                {
                    if (ct.IsCancellationRequested || !Application.isPlaying)
                    {
                        webRequest.Abort();
                        return false;
                    }

                    try
                    {
                        await UniTask.Delay(100, ignoreTimeScale: true, cancellationToken: ct);
                    }
                    catch
                    {
                        webRequest.Abort();
                        return false;
                    }
                }

                return webRequest.result == UnityWebRequest.Result.Success;
            }
            catch
            {
                return false;
            }
        }

        public int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            // Rough estimate: ~4 characters per token for English
            return text.Length / 4;
        }

        private List<object> BuildMessages(ModelRequest request)
        {
            var messages = new List<object>();

            // System message
            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                messages.Add(new { role = "system", content = request.SystemPrompt });
            }

            // Conversation history
            foreach (var msg in request.Messages)
            {
                if (msg.Screenshot != null && msg.Screenshot.Length > 0)
                {
                    // Vision message with image
                    var content = new List<object>
                    {
                        new { type = "text", text = msg.Content ?? "" }
                    };

                    var base64Image = Convert.ToBase64String(msg.Screenshot);
                    content.Add(new
                    {
                        type = "image_url",
                        image_url = new { url = $"data:image/png;base64,{base64Image}" }
                    });

                    messages.Add(new { role = msg.Role, content = content });
                }
                else
                {
                    messages.Add(new { role = msg.Role, content = msg.Content });
                }
            }

            // Add current screenshot if provided
            if (request.ScreenshotPng != null && request.ScreenshotPng.Length > 0)
            {
                var content = new List<object>();

                var promptText = "Current screen state:\n";
                if (!string.IsNullOrEmpty(request.ElementListJson))
                {
                    promptText += request.ElementListJson + "\n\n";
                }
                promptText += "Based on the screenshot and available elements, choose the next action to make progress toward the test goal.";

                content.Add(new { type = "text", text = promptText });

                var base64Image = Convert.ToBase64String(request.ScreenshotPng);
                content.Add(new
                {
                    type = "image_url",
                    image_url = new { url = $"data:image/png;base64,{base64Image}" }
                });

                messages.Add(new { role = "user", content = content });
            }

            return messages;
        }

        private object BuildRequestBody(ModelRequest request, List<object> messages)
        {
            var body = new Dictionary<string, object>
            {
                ["messages"] = messages,
                ["max_tokens"] = request.MaxTokens,
                ["temperature"] = request.Temperature,
                ["stream"] = false
            };

            // Add model name if specified
            if (!string.IsNullOrEmpty(modelName))
            {
                body["model"] = modelName;
            }

            // Use structured output (JSON schema) for reliable parsing
            if (useStructuredOutput)
            {
                body["response_format"] = new Dictionary<string, object>
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new Dictionary<string, object>
                    {
                        ["name"] = "ui_action",
                        ["strict"] = true,
                        ["schema"] = GetActionSchema()
                    }
                };
            }
            // Fallback: Add tools if available (for models that support native tool calling)
            else if (request.Tools != null && request.Tools.Count > 0)
            {
                body["tools"] = ToolSchema.ToOpenAIFormat(request.Tools);
                body["tool_choice"] = "auto";
            }

            return body;
        }

        /// <summary>
        /// Gets the JSON schema for structured action output.
        /// Includes a "screenshot" action so AI can request visual clarification when needed.
        /// Uses nullable types for optional fields to satisfy strict mode.
        /// </summary>
        private Dictionary<string, object> GetActionSchema()
        {
            return new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["reasoning"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "Brief explanation of why this action was chosen"
                    },
                    ["action"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["enum"] = new List<string> { "click", "type", "drag", "scroll", "wait", "pass", "fail", "screenshot" },
                        ["description"] = "The action to perform. Use 'screenshot' if you need to see the screen to understand the UI layout better."
                    },
                    ["element_id"] = new Dictionary<string, object>
                    {
                        ["type"] = new List<string> { "string", "null" },
                        ["description"] = "Element ID from the list (e.g., 'e1', 'e2'). Required for click/type/drag/scroll."
                    },
                    ["text"] = new Dictionary<string, object>
                    {
                        ["type"] = new List<string> { "string", "null" },
                        ["description"] = "Text to type (for 'type' action)"
                    },
                    ["direction"] = new Dictionary<string, object>
                    {
                        ["type"] = new List<string> { "string", "null" },
                        ["enum"] = new List<object> { "up", "down", "left", "right", null },
                        ["description"] = "Direction for drag/scroll"
                    },
                    ["reason"] = new Dictionary<string, object>
                    {
                        ["type"] = new List<string> { "string", "null" },
                        ["description"] = "Reason for pass/fail/screenshot"
                    },
                    ["seconds"] = new Dictionary<string, object>
                    {
                        ["type"] = new List<string> { "number", "null" },
                        ["description"] = "Seconds to wait (for 'wait' action)"
                    },
                    ["x"] = new Dictionary<string, object>
                    {
                        ["type"] = new List<string> { "number", "null" },
                        ["description"] = "Screen X coordinate (0.0-1.0) for click when element not in list"
                    },
                    ["y"] = new Dictionary<string, object>
                    {
                        ["type"] = new List<string> { "number", "null" },
                        ["description"] = "Screen Y coordinate (0.0-1.0) for click when element not in list"
                    }
                },
                ["required"] = new List<string> { "reasoning", "action", "element_id", "text", "direction", "reason", "seconds", "x", "y" },
                ["additionalProperties"] = false
            };
        }

        private string SerializeRequest(object request)
        {
            // Manual JSON serialization since JsonUtility doesn't handle anonymous types
            var sb = new StringBuilder();
            SerializeValue(sb, request);
            return sb.ToString();
        }

        private void SerializeValue(StringBuilder sb, object value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            if (value is string str)
            {
                sb.Append('"');
                sb.Append(EscapeString(str));
                sb.Append('"');
                return;
            }

            if (value is bool b)
            {
                sb.Append(b ? "true" : "false");
                return;
            }

            if (value is int || value is float || value is double || value is long)
            {
                sb.Append(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
                return;
            }

            if (value is System.Collections.IList list)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    SerializeValue(sb, item);
                }
                sb.Append(']');
                return;
            }

            if (value is System.Collections.IDictionary dict)
            {
                sb.Append('{');
                bool first = true;
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"');
                    sb.Append(EscapeString(entry.Key.ToString()));
                    sb.Append("\":");
                    SerializeValue(sb, entry.Value);
                }
                sb.Append('}');
                return;
            }

            // Anonymous type or regular object
            var type = value.GetType();
            var props = type.GetProperties();

            sb.Append('{');
            bool firstProp = true;
            foreach (var prop in props)
            {
                var propValue = prop.GetValue(value);
                if (propValue == null) continue;

                if (!firstProp) sb.Append(',');
                firstProp = false;

                sb.Append('"');
                sb.Append(EscapeString(prop.Name));
                sb.Append("\":");
                SerializeValue(sb, propValue);
            }
            sb.Append('}');
        }

        private string EscapeString(string str)
        {
            return str
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        private ModelResponse ParseResponse(string json, float startTime)
        {
            try
            {
                var response = new ModelResponse
                {
                    Success = true,
                    LatencyMs = (Time.realtimeSinceStartup - startTime) * 1000f,
                    ModelTier = Tier
                };

                // Parse the response JSON
                // LM Studio follows OpenAI format: { choices: [{ message: { content, tool_calls } }] }
                var parsed = ParseJson(json);

                if (parsed.TryGetValue("choices", out var choicesObj) && choicesObj is List<object> choices && choices.Count > 0)
                {
                    if (choices[0] is Dictionary<string, object> choice)
                    {
                        if (choice.TryGetValue("message", out var msgObj) && msgObj is Dictionary<string, object> message)
                        {
                            // Get content
                            if (message.TryGetValue("content", out var content))
                            {
                                response.RawContent = content?.ToString();
                            }

                            // Get tool calls (native tool calling)
                            if (message.TryGetValue("tool_calls", out var toolCallsObj) && toolCallsObj is List<object> toolCalls)
                            {
                                foreach (var tcObj in toolCalls)
                                {
                                    if (tcObj is Dictionary<string, object> tc &&
                                        tc.TryGetValue("function", out var funcObj) &&
                                        funcObj is Dictionary<string, object> func)
                                    {
                                        var toolCall = new ToolCall
                                        {
                                            Name = func.GetValueOrDefault("name")?.ToString()
                                        };

                                        if (func.TryGetValue("arguments", out var argsObj))
                                        {
                                            var argsStr = argsObj.ToString();
                                            toolCall.Arguments = ParseJson(argsStr);
                                        }

                                        response.ToolCalls.Add(toolCall);
                                    }
                                }
                            }
                        }
                    }
                }

                // Try to parse structured output or tool calls from content
                if (response.ToolCalls.Count == 0 && !string.IsNullOrEmpty(response.RawContent))
                {
                    var parsedCalls = TryParseStructuredOutput(response.RawContent);
                    if (parsedCalls != null)
                    {
                        response.ToolCalls.AddRange(parsedCalls);

                        // Extract reasoning from structured output
                        var contentParsed = ParseJson(response.RawContent);
                        if (contentParsed.TryGetValue("reasoning", out var reasoning))
                        {
                            response.Reasoning = reasoning?.ToString();
                        }
                    }
                }

                // Fallback: Extract reasoning from content if not already set
                if (string.IsNullOrEmpty(response.Reasoning) && !string.IsNullOrEmpty(response.RawContent))
                {
                    response.Reasoning = ExtractReasoning(response.RawContent);
                }

                // Get usage info
                if (parsed.TryGetValue("usage", out var usageObj) && usageObj is Dictionary<string, object> usage)
                {
                    if (usage.TryGetValue("total_tokens", out var tokens))
                    {
                        response.TokensUsed = Convert.ToInt32(tokens);
                    }
                }

                Debug.Log($"[AITest] LMStudio response parsed: ToolCalls={response.ToolCalls.Count}, Reasoning='{response.Reasoning?.Substring(0, Math.Min(50, response.Reasoning?.Length ?? 0))}...'");

                return response;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AITest] Failed to parse LMStudio response: {ex.Message}\nJSON: {json}");
                return ModelResponse.Failed($"Failed to parse response: {ex.Message}");
            }
        }

        /// <summary>
        /// Parses structured output format: { "reasoning": "...", "action": "click", "element_id": "e1" }
        /// </summary>
        private List<ToolCall> TryParseStructuredOutput(string content)
        {
            // Try to find JSON object in content
            var startIdx = content.IndexOf('{');
            if (startIdx < 0) return null;

            var endIdx = content.LastIndexOf('}');
            if (endIdx <= startIdx) return null;

            try
            {
                var jsonPart = content.Substring(startIdx, endIdx - startIdx + 1);
                var parsed = ParseJson(jsonPart);

                // Check for structured output format with "action" field
                if (parsed.TryGetValue("action", out var action))
                {
                    var actionName = action.ToString();
                    Debug.Log($"[AITest] Parsed structured action: {actionName}");

                    var toolCall = new ToolCall
                    {
                        Name = actionName
                    };

                    // Copy all properties except "action" and "reasoning" as arguments
                    foreach (var kvp in parsed)
                    {
                        if (kvp.Key != "action" && kvp.Key != "reasoning" && kvp.Value != null)
                        {
                            toolCall.Arguments[kvp.Key] = kvp.Value;
                        }
                    }

                    return new List<ToolCall> { toolCall };
                }

                // Fallback: check for "name" field (older format)
                if (parsed.TryGetValue("name", out var name))
                {
                    var toolCall = new ToolCall
                    {
                        Name = name.ToString()
                    };

                    foreach (var kvp in parsed)
                    {
                        if (kvp.Key != "name" && kvp.Key != "reasoning")
                        {
                            toolCall.Arguments[kvp.Key] = kvp.Value;
                        }
                    }

                    return new List<ToolCall> { toolCall };
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AITest] Failed to parse structured output: {ex.Message}");
            }

            return null;
        }

        private List<ToolCall> TryParseToolCallsFromContent(string content)
        {
            // Delegate to structured output parser
            return TryParseStructuredOutput(content);
        }

        private string ExtractReasoning(string content)
        {
            // Remove JSON parts and return the text reasoning
            var lines = content.Split('\n');
            var reasoning = new StringBuilder();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("{") && !trimmed.StartsWith("[") &&
                    !trimmed.EndsWith("}") && !trimmed.EndsWith("]"))
                {
                    if (reasoning.Length > 0) reasoning.Append(" ");
                    reasoning.Append(trimmed);
                }
            }

            return reasoning.ToString();
        }

        private Dictionary<string, object> ParseJson(string json)
        {
            // Simple JSON parser for response handling
            return SimpleJsonParser.Parse(json);
        }

        // Dummy class for JsonUtility
        [Serializable]
        private class OpenAIRequestWrapper
        {
            public object json;
        }
    }

    /// <summary>
    /// Simple JSON parser for handling API responses.
    /// </summary>
    internal static class SimpleJsonParser
    {
        public static Dictionary<string, object> Parse(string json)
        {
            var result = new Dictionary<string, object>();
            if (string.IsNullOrEmpty(json)) return result;

            int index = 0;
            SkipWhitespace(json, ref index);

            if (index < json.Length && json[index] == '{')
            {
                return ParseObject(json, ref index);
            }

            return result;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
                index++;
        }

        private static Dictionary<string, object> ParseObject(string json, ref int index)
        {
            var result = new Dictionary<string, object>();
            index++; // Skip '{'

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] == '}')
                {
                    index++;
                    break;
                }

                // Parse key
                var key = ParseString(json, ref index);
                SkipWhitespace(json, ref index);

                if (index < json.Length && json[index] == ':')
                {
                    index++;
                    SkipWhitespace(json, ref index);

                    // Parse value
                    var value = ParseValue(json, ref index);
                    result[key] = value;
                }

                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',')
                    index++;
            }

            return result;
        }

        private static object ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) return null;

            char c = json[index];

            if (c == '"') return ParseString(json, ref index);
            if (c == '{') return ParseObject(json, ref index);
            if (c == '[') return ParseArray(json, ref index);
            if (c == 't' || c == 'f') return ParseBool(json, ref index);
            if (c == 'n') return ParseNull(json, ref index);
            if (char.IsDigit(c) || c == '-') return ParseNumber(json, ref index);

            return null;
        }

        private static string ParseString(string json, ref int index)
        {
            if (json[index] != '"') return "";
            index++;

            var sb = new StringBuilder();
            while (index < json.Length)
            {
                char c = json[index];
                if (c == '"')
                {
                    index++;
                    break;
                }
                if (c == '\\' && index + 1 < json.Length)
                {
                    index++;
                    c = json[index];
                    switch (c)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        default: sb.Append(c); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
                index++;
            }
            return sb.ToString();
        }

        private static List<object> ParseArray(string json, ref int index)
        {
            var result = new List<object>();
            index++; // Skip '['

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] == ']')
                {
                    index++;
                    break;
                }

                var value = ParseValue(json, ref index);
                result.Add(value);

                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',')
                    index++;
            }

            return result;
        }

        private static bool ParseBool(string json, ref int index)
        {
            if (json.Substring(index).StartsWith("true"))
            {
                index += 4;
                return true;
            }
            if (json.Substring(index).StartsWith("false"))
            {
                index += 5;
                return false;
            }
            return false;
        }

        private static object ParseNull(string json, ref int index)
        {
            if (json.Substring(index).StartsWith("null"))
            {
                index += 4;
            }
            return null;
        }

        private static object ParseNumber(string json, ref int index)
        {
            int start = index;
            bool hasDecimal = false;

            if (json[index] == '-') index++;

            while (index < json.Length)
            {
                char c = json[index];
                if (char.IsDigit(c))
                {
                    index++;
                }
                else if (c == '.' && !hasDecimal)
                {
                    hasDecimal = true;
                    index++;
                }
                else if (c == 'e' || c == 'E')
                {
                    index++;
                    if (index < json.Length && (json[index] == '+' || json[index] == '-'))
                        index++;
                }
                else
                {
                    break;
                }
            }

            var numStr = json.Substring(start, index - start);
            if (hasDecimal)
            {
                if (double.TryParse(numStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d))
                    return d;
            }
            else
            {
                if (long.TryParse(numStr, out var l))
                    return l;
            }

            return 0;
        }
    }
}
