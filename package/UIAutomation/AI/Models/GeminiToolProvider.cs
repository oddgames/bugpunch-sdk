using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.Networking;

namespace ODDGames.UIAutomation.AI
{
    /// <summary>
    /// Model provider for Gemini with vision and structured JSON output.
    /// Returns actions in a defined schema so we can reliably parse tool calls.
    /// </summary>
    public class GeminiToolProvider : IModelProvider
    {
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
        private const int DefaultTimeoutSeconds = 120;

        private readonly string apiKey;
        private readonly string modelName;
        private readonly int timeoutSeconds;

        /// <summary>Human-readable name of this provider</summary>
        public string Name { get; }

        /// <summary>The model ID being used</summary>
        public string ModelId => modelName;

        public bool SupportsVision => true;
        public bool SupportsToolCalling => true;
        public int ContextWindowSize => 1048576; // 1M tokens

        /// <summary>
        /// Creates a Gemini provider with structured output for UI testing.
        /// </summary>
        /// <param name="apiKey">Gemini API key</param>
        /// <param name="modelName">Model ID (e.g., "gemini-2.0-flash")</param>
        /// <param name="timeoutSeconds">Request timeout</param>
        public GeminiToolProvider(string apiKey, string modelName, int timeoutSeconds = DefaultTimeoutSeconds)
        {
            this.apiKey = apiKey;
            this.modelName = modelName;
            this.timeoutSeconds = timeoutSeconds;
            this.Name = $"Gemini ({this.modelName})";
        }

        public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken ct = default)
        {
            var startTime = Time.realtimeSinceStartup;

            if (string.IsNullOrEmpty(apiKey))
            {
                return ModelResponse.Failed("Gemini API key not configured");
            }

            try
            {
                var url = $"{BaseUrl}/{modelName}:generateContent?key={apiKey}";
                var requestBody = BuildRequestBody(request);
                var jsonBody = SerializeRequest(requestBody);

                // Log request details (truncate screenshot data for readability)
                var logJson = jsonBody;
                if (logJson.Length > 2000)
                {
                    var dataStart = logJson.IndexOf("\"data\":\"");
                    if (dataStart >= 0)
                    {
                        var dataEnd = logJson.IndexOf("\"", dataStart + 8);
                        if (dataEnd > dataStart + 50)
                        {
                            logJson = logJson.Substring(0, dataStart + 20) + "...[base64 truncated]..." + logJson.Substring(dataEnd - 10);
                        }
                    }
                }
                Debug.Log($"[GeminiToolProvider] Sending request ({jsonBody.Length} bytes):\n{logJson.Substring(0, Math.Min(3000, logJson.Length))}...");

                var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
                using var webRequest = new UnityWebRequest(url, "POST");
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
                        await Task.Delay(100, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        webRequest.Abort();
                        return ModelResponse.Failed("Request cancelled");
                    }
                    catch
                    {
                        webRequest.Abort();
                        return ModelResponse.Failed("Request aborted");
                    }
                }

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    var errorBody = webRequest.downloadHandler?.text ?? "";
                    return ModelResponse.Failed($"HTTP error: {webRequest.error}. Response: {errorBody}");
                }

                var responseJson = webRequest.downloadHandler.text;
                Debug.Log($"[GeminiToolProvider] Response: {responseJson.Substring(0, Math.Min(2000, responseJson.Length))}");
                return ParseResponse(responseJson, startTime);
            }
            catch (Exception ex)
            {
                return ModelResponse.Failed($"Exception: {ex.Message}");
            }
        }

        public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(apiKey))
                return false;

            try
            {
                var url = $"{BaseUrl}?key={apiKey}";

                using var webRequest = UnityWebRequest.Get(url);
                webRequest.timeout = 10;

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
                        await Task.Delay(100, ct);
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
            return text.Length / 4;
        }

        private object BuildRequestBody(ModelRequest request)
        {
            var contents = new List<object>();
            bool isFirstUserMessage = true;

            // Find the last tool result (we'll add the screenshot only there)
            int lastToolResultIndex = -1;
            for (int i = 0; i < request.Messages.Count; i++)
            {
                if (!string.IsNullOrEmpty(request.Messages[i].ToolCallId))
                    lastToolResultIndex = i;
            }

            // Add conversation history (text only, no screenshots except the latest)
            for (int msgIndex = 0; msgIndex < request.Messages.Count; msgIndex++)
            {
                var msg = request.Messages[msgIndex];
                var parts = new List<object>();

                // Handle tool result messages
                if (!string.IsNullOrEmpty(msg.ToolCallId))
                {
                    parts.Add(new { text = $"Action result: {msg.Content ?? "completed"}" });

                    // Add screenshot ONLY for the last tool result (current state)
                    if (msgIndex == lastToolResultIndex && msg.Screenshot != null && msg.Screenshot.Length > 0)
                    {
                        Debug.Log($"[GeminiToolProvider] Adding current screenshot ({msg.Screenshot.Length} bytes)");
                        parts.Add(new
                        {
                            inline_data = new
                            {
                                mime_type = "image/jpeg",
                                data = Convert.ToBase64String(msg.Screenshot)
                            }
                        });
                    }

                    contents.Add(new { role = "user", parts = parts });
                    continue;
                }

                // Handle assistant messages (their JSON response)
                if (msg.Role == "assistant")
                {
                    if (!string.IsNullOrEmpty(msg.Content))
                    {
                        parts.Add(new { text = msg.Content });
                        contents.Add(new { role = "model", parts = parts });
                    }
                    continue;
                }

                // Regular user message (test prompt)
                var textContent = msg.Content ?? "";

                // For the first user message, add element list
                if (isFirstUserMessage && msg.Role == "user" && !string.IsNullOrEmpty(request.ElementListJson))
                {
                    textContent += "\n\n## Available UI Elements:\n" + request.ElementListJson;
                }

                if (!string.IsNullOrEmpty(textContent))
                {
                    parts.Add(new { text = textContent });
                }

                // Add screenshot ONLY to the first user message if there are no tool results yet
                if (isFirstUserMessage && msg.Role == "user" && lastToolResultIndex < 0)
                {
                    var screenshot = msg.Screenshot ?? request.ScreenshotPng;
                    if (screenshot != null && screenshot.Length > 0)
                    {
                        Debug.Log($"[GeminiToolProvider] Adding initial screenshot ({screenshot.Length} bytes)");
                        parts.Add(new
                        {
                            inline_data = new
                            {
                                mime_type = "image/jpeg",
                                data = Convert.ToBase64String(screenshot)
                            }
                        });
                    }
                }

                if (msg.Role == "user")
                    isFirstUserMessage = false;

                if (parts.Count > 0)
                {
                    contents.Add(new { role = "user", parts = parts });
                }
            }

            Debug.Log($"[GeminiToolProvider] Built {contents.Count} messages (1 screenshot)");

            // Build the request with structured output schema
            var body = new Dictionary<string, object>
            {
                ["contents"] = contents,
                ["generationConfig"] = new Dictionary<string, object>
                {
                    ["maxOutputTokens"] = request.MaxTokens,
                    ["temperature"] = request.Temperature,
                    ["responseMimeType"] = "application/json",
                    ["responseSchema"] = BuildActionResponseSchema()
                }
            };

            // Add system instruction if provided
            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                body["systemInstruction"] = new
                {
                    parts = new[] { new { text = request.SystemPrompt } }
                };
            }

            return body;
        }

        /// <summary>
        /// Builds the JSON schema for structured action output.
        /// The model must respond with this exact structure.
        /// </summary>
        private object BuildActionResponseSchema()
        {
            return new Dictionary<string, object>
            {
                ["type"] = "OBJECT",
                ["properties"] = new Dictionary<string, object>
                {
                    ["reasoning"] = new Dictionary<string, object>
                    {
                        ["type"] = "STRING",
                        ["description"] = "Your analysis of the current screen state and why you chose this action"
                    },
                    ["action"] = new Dictionary<string, object>
                    {
                        ["type"] = "STRING",
                        ["description"] = "The action to perform",
                        ["enum"] = new[] { "click", "type", "scroll", "swipe", "drag", "wait", "key_press", "pass", "fail", "cleared", "no_blocker", "get_hierarchy", "screenshot" }
                    },
                    ["search"] = new Dictionary<string, object>
                    {
                        ["type"] = "STRING",
                        ["description"] = "Search pattern to find the element (e.g., 'Name(\"Button1\")', 'Text(\"Submit\")'). Use the patterns from the element list."
                    },
                    ["x"] = new Dictionary<string, object>
                    {
                        ["type"] = "NUMBER",
                        ["description"] = "X coordinate (0.0-1.0 normalized) when not using search pattern"
                    },
                    ["y"] = new Dictionary<string, object>
                    {
                        ["type"] = "NUMBER",
                        ["description"] = "Y coordinate (0.0-1.0 normalized) when not using search pattern"
                    },
                    ["text"] = new Dictionary<string, object>
                    {
                        ["type"] = "STRING",
                        ["description"] = "Text to type (for 'type' action)"
                    },
                    ["direction"] = new Dictionary<string, object>
                    {
                        ["type"] = "STRING",
                        ["description"] = "Scroll direction",
                        ["enum"] = new[] { "up", "down", "left", "right" }
                    },
                    ["reason"] = new Dictionary<string, object>
                    {
                        ["type"] = "STRING",
                        ["description"] = "Reason for pass/fail/cleared/no_blocker action"
                    },
                    ["key"] = new Dictionary<string, object>
                    {
                        ["type"] = "STRING",
                        ["description"] = "Key to press (for 'key_press' action, e.g., 'Escape', 'Enter', 'Tab')"
                    }
                },
                ["required"] = new[] { "reasoning", "action" }
            };
        }

        private ModelResponse ParseResponse(string json, float startTime)
        {
            try
            {
                var response = new ModelResponse
                {
                    Success = true,
                    LatencyMs = (Time.realtimeSinceStartup - startTime) * 1000f,
                    Model = modelName
                };

                var parsed = SimpleJsonParser.Parse(json);

                // Check for error
                if (parsed.TryGetValue("error", out var errorObj) && errorObj is Dictionary<string, object> error)
                {
                    return ModelResponse.Failed(error.GetValueOrDefault("message")?.ToString() ?? "Unknown error");
                }

                // Parse candidates
                if (parsed.TryGetValue("candidates", out var candidatesObj) &&
                    candidatesObj is List<object> candidates && candidates.Count > 0)
                {
                    if (candidates[0] is Dictionary<string, object> candidate)
                    {
                        if (candidate.TryGetValue("content", out var contentObj) &&
                            contentObj is Dictionary<string, object> content)
                        {
                            if (content.TryGetValue("parts", out var partsObj) &&
                                partsObj is List<object> parts)
                            {
                                foreach (var partObj in parts)
                                {
                                    if (partObj is Dictionary<string, object> part)
                                    {
                                        // Text content - this is our structured JSON response
                                        if (part.TryGetValue("text", out var text))
                                        {
                                            var textStr = text?.ToString();
                                            response.RawContent = textStr;

                                            // Parse the structured JSON into a tool call
                                            if (!string.IsNullOrEmpty(textStr))
                                            {
                                                var toolCall = ParseStructuredResponse(textStr);
                                                if (toolCall != null)
                                                {
                                                    response.ToolCalls.Add(toolCall);
                                                    response.Reasoning = toolCall.Arguments.GetValueOrDefault("reasoning")?.ToString();
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Get usage metadata
                if (parsed.TryGetValue("usageMetadata", out var usageObj) &&
                    usageObj is Dictionary<string, object> usage)
                {
                    if (usage.TryGetValue("totalTokenCount", out var tokens))
                    {
                        response.TokensUsed = Convert.ToInt32(tokens);
                    }
                }

                return response;
            }
            catch (Exception ex)
            {
                return ModelResponse.Failed($"Failed to parse response: {ex.Message}");
            }
        }

        /// <summary>
        /// Parses the structured JSON response into a ToolCall.
        /// </summary>
        private ToolCall ParseStructuredResponse(string jsonText)
        {
            try
            {
                var parsed = SimpleJsonParser.Parse(jsonText);

                var action = parsed.GetValueOrDefault("action")?.ToString();
                if (string.IsNullOrEmpty(action))
                {
                    Debug.LogWarning($"[GeminiToolProvider] No action in response: {jsonText}");
                    return null;
                }

                // Build arguments from the parsed response
                var args = new Dictionary<string, object>();

                // Copy all relevant fields
                if (parsed.TryGetValue("reasoning", out var reasoning))
                    args["reasoning"] = reasoning;
                if (parsed.TryGetValue("search", out var search))
                    args["search"] = search;
                if (parsed.TryGetValue("x", out var x))
                    args["x"] = x;
                if (parsed.TryGetValue("y", out var y))
                    args["y"] = y;
                if (parsed.TryGetValue("text", out var text))
                    args["text"] = text;
                if (parsed.TryGetValue("direction", out var direction))
                    args["direction"] = direction;
                if (parsed.TryGetValue("reason", out var reason))
                    args["reason"] = reason;
                if (parsed.TryGetValue("key", out var key))
                    args["key"] = key;

                Debug.Log($"[GeminiToolProvider] Parsed action: {action}, args: {string.Join(", ", args.Keys)}");

                return new ToolCall
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = action,
                    Arguments = args
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GeminiToolProvider] Failed to parse structured response: {ex.Message}\nJSON: {jsonText}");
                return null;
            }
        }

        private string SerializeRequest(object request)
        {
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
    }

    // Keep old name as alias for compatibility
    [Obsolete("Use GeminiToolProvider instead")]
    public class ComputerUseProvider : GeminiToolProvider
    {
        public ComputerUseProvider(string apiKey, string modelName, int timeoutSeconds = 120)
            : base(apiKey, modelName, timeoutSeconds) { }
    }
}
