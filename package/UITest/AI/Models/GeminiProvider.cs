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
    /// Model provider for Google Gemini API.
    /// Supports multiple model tiers: Flash Lite, Flash, and Pro.
    /// </summary>
    public class GeminiProvider : IModelProvider
    {
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
        private const int DefaultTimeoutSeconds = 120;

        private readonly string apiKey;
        private readonly string modelName;
        private readonly ModelTier tier;
        private readonly int timeoutSeconds;

        public string Name => $"Gemini ({GetTierName()})";
        public ModelTier Tier => tier;
        public bool SupportsVision => true;
        public bool SupportsToolCalling => true;

        public int ContextWindowSize => tier switch
        {
            ModelTier.GeminiFlashLite => 32768,
            ModelTier.GeminiFlash => 1048576,
            ModelTier.GeminiPro => 2097152,
            _ => 32768
        };

        public GeminiProvider(string apiKey, ModelTier tier, int timeoutSeconds = DefaultTimeoutSeconds)
        {
            this.apiKey = apiKey;
            this.tier = tier;
            this.timeoutSeconds = timeoutSeconds;
            this.modelName = GetModelName(tier);
        }

        private static string GetModelName(ModelTier tier) => tier switch
        {
            ModelTier.GeminiFlashLite => "gemini-2.0-flash-lite",
            ModelTier.GeminiFlash => "gemini-2.0-flash",
            ModelTier.GeminiPro => "gemini-1.5-pro",
            _ => "gemini-2.0-flash"
        };

        private string GetTierName() => tier switch
        {
            ModelTier.GeminiFlashLite => "Flash Lite",
            ModelTier.GeminiFlash => "Flash",
            ModelTier.GeminiPro => "Pro",
            _ => "Flash"
        };

        public async UniTask<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken ct = default)
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
                return ParseResponse(responseJson, startTime);
            }
            catch (Exception ex)
            {
                return ModelResponse.Failed($"Exception: {ex.Message}");
            }
        }

        public async UniTask<bool> TestConnectionAsync(CancellationToken ct = default)
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
            // Gemini uses similar tokenization to GPT-4: ~4 characters per token
            return text.Length / 4;
        }

        private object BuildRequestBody(ModelRequest request)
        {
            var contents = new List<object>();

            // Add system instruction if provided
            object systemInstruction = null;
            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                systemInstruction = new
                {
                    parts = new[] { new { text = request.SystemPrompt } }
                };
            }

            // Add conversation history
            foreach (var msg in request.Messages)
            {
                var role = msg.Role == "assistant" ? "model" : "user";
                var parts = new List<object>();

                if (!string.IsNullOrEmpty(msg.Content))
                {
                    parts.Add(new { text = msg.Content });
                }

                if (msg.Screenshot != null && msg.Screenshot.Length > 0)
                {
                    parts.Add(new
                    {
                        inline_data = new
                        {
                            mime_type = "image/png",
                            data = Convert.ToBase64String(msg.Screenshot)
                        }
                    });
                }

                contents.Add(new { role = role, parts = parts });
            }

            // Add current screenshot and element list
            if (request.ScreenshotPng != null && request.ScreenshotPng.Length > 0)
            {
                var parts = new List<object>();

                var promptText = "Current screen state:\n";
                if (!string.IsNullOrEmpty(request.ElementListJson))
                {
                    promptText += request.ElementListJson + "\n\n";
                }
                promptText += "Based on the screenshot and available elements, choose the next action to make progress toward the test goal. " +
                              "Respond with a JSON object containing the tool call.";

                parts.Add(new { text = promptText });
                parts.Add(new
                {
                    inline_data = new
                    {
                        mime_type = "image/png",
                        data = Convert.ToBase64String(request.ScreenshotPng)
                    }
                });

                contents.Add(new { role = "user", parts = parts });
            }

            // Build the request
            var body = new Dictionary<string, object>
            {
                ["contents"] = contents,
                ["generationConfig"] = new
                {
                    maxOutputTokens = request.MaxTokens,
                    temperature = request.Temperature
                }
            };

            if (systemInstruction != null)
            {
                body["systemInstruction"] = systemInstruction;
            }

            // Add tools if available
            if (request.Tools != null && request.Tools.Count > 0)
            {
                body["tools"] = new[] { BuildToolsDeclaration(request.Tools) };
            }

            return body;
        }

        private object BuildToolsDeclaration(List<ToolDefinition> tools)
        {
            var functionDeclarations = new List<object>();

            foreach (var tool in tools)
            {
                var properties = new Dictionary<string, object>();
                var required = tool.Parameters?.Required ?? new List<string>();

                if (tool.Parameters?.Properties != null)
                {
                    foreach (var prop in tool.Parameters.Properties)
                    {
                        var propDef = new Dictionary<string, object>
                        {
                            ["type"] = MapType(prop.Value.Type),
                            ["description"] = prop.Value.Description
                        };

                        if (prop.Value.Enum != null && prop.Value.Enum.Count > 0)
                        {
                            propDef["enum"] = prop.Value.Enum;
                        }

                        properties[prop.Key] = propDef;
                    }
                }

                functionDeclarations.Add(new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = properties,
                        required = required
                    }
                });
            }

            return new { functionDeclarations = functionDeclarations };
        }

        private string MapType(string type) => type?.ToUpperInvariant() switch
        {
            "STRING" => "STRING",
            "NUMBER" => "NUMBER",
            "INTEGER" => "INTEGER",
            "BOOLEAN" => "BOOLEAN",
            "ARRAY" => "ARRAY",
            "OBJECT" => "OBJECT",
            _ => "STRING"
        };

        private ModelResponse ParseResponse(string json, float startTime)
        {
            try
            {
                var response = new ModelResponse
                {
                    Success = true,
                    LatencyMs = (Time.realtimeSinceStartup - startTime) * 1000f,
                    ModelTier = tier
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
                                        // Text content
                                        if (part.TryGetValue("text", out var text))
                                        {
                                            response.RawContent = text?.ToString();
                                        }

                                        // Function call
                                        if (part.TryGetValue("functionCall", out var funcCallObj) &&
                                            funcCallObj is Dictionary<string, object> funcCall)
                                        {
                                            var toolCall = new ToolCall
                                            {
                                                Name = funcCall.GetValueOrDefault("name")?.ToString()
                                            };

                                            if (funcCall.TryGetValue("args", out var argsObj) &&
                                                argsObj is Dictionary<string, object> args)
                                            {
                                                toolCall.Arguments = args;
                                            }

                                            response.ToolCalls.Add(toolCall);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Try to parse tool calls from text content if none found via function calling
                if (response.ToolCalls.Count == 0 && !string.IsNullOrEmpty(response.RawContent))
                {
                    var parsedCalls = TryParseToolCallsFromContent(response.RawContent);
                    if (parsedCalls != null)
                    {
                        response.ToolCalls.AddRange(parsedCalls);
                    }
                }

                // Extract reasoning
                if (!string.IsNullOrEmpty(response.RawContent))
                {
                    response.Reasoning = ExtractReasoning(response.RawContent);
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

        private List<ToolCall> TryParseToolCallsFromContent(string content)
        {
            // Try to find JSON object in content
            var startIdx = content.IndexOf('{');
            if (startIdx < 0) return null;

            var endIdx = content.LastIndexOf('}');
            if (endIdx <= startIdx) return null;

            try
            {
                var jsonPart = content.Substring(startIdx, endIdx - startIdx + 1);
                var parsed = SimpleJsonParser.Parse(jsonPart);

                if (parsed.TryGetValue("action", out var action) ||
                    parsed.TryGetValue("name", out action) ||
                    parsed.TryGetValue("tool", out action))
                {
                    var toolCall = new ToolCall
                    {
                        Name = action.ToString()
                    };

                    // Copy other properties as arguments
                    foreach (var kvp in parsed)
                    {
                        if (kvp.Key != "action" && kvp.Key != "name" && kvp.Key != "tool")
                        {
                            toolCall.Arguments[kvp.Key] = kvp.Value;
                        }
                    }

                    return new List<ToolCall> { toolCall };
                }
            }
            catch
            {
                // Ignore parse errors
            }

            return null;
        }

        private string ExtractReasoning(string content)
        {
            // Remove JSON parts and return the text reasoning
            var lines = content.Split('\n');
            var reasoning = new StringBuilder();
            bool inJson = false;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
                    inJson = true;

                if (!inJson && !string.IsNullOrWhiteSpace(trimmed))
                {
                    if (reasoning.Length > 0) reasoning.Append(" ");
                    reasoning.Append(trimmed);
                }

                if (trimmed.EndsWith("}") || trimmed.EndsWith("]"))
                    inJson = false;
            }

            return reasoning.ToString();
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
}
