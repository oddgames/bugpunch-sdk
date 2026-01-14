using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ODDGames.UITest.AI
{
    /// <summary>
    /// A message in the conversation with the AI model.
    /// </summary>
    [Serializable]
    public class Message
    {
        /// <summary>Role: "system", "user", or "assistant"</summary>
        public string Role;

        /// <summary>Text content of the message</summary>
        public string Content;

        /// <summary>Optional screenshot as PNG bytes (for vision models)</summary>
        public byte[] Screenshot;

        /// <summary>Optional structured element list JSON</summary>
        public string ElementsJson;

        /// <summary>Tool calls made by the assistant</summary>
        public List<ToolCall> ToolCalls;

        /// <summary>Tool call ID for tool result messages</summary>
        public string ToolCallId;

        public Message() { }

        public Message(string role, string content)
        {
            this.Role = role;
            this.Content = content;
        }

        public static Message System(string content) => new Message("system", content);
        public static Message User(string content) => new Message("user", content);
        public static Message Assistant(string content) => new Message("assistant", content);
    }

    /// <summary>
    /// Request to send to an AI model.
    /// </summary>
    public class ModelRequest
    {
        /// <summary>System prompt (instructions for the AI)</summary>
        public string SystemPrompt { get; set; }

        /// <summary>Conversation history</summary>
        public List<Message> Messages { get; set; } = new List<Message>();

        /// <summary>Current screenshot as PNG bytes</summary>
        public byte[] ScreenshotPng { get; set; }

        /// <summary>Structured list of available UI elements</summary>
        public string ElementListJson { get; set; }

        /// <summary>Available tools/functions the AI can call</summary>
        public List<ToolDefinition> Tools { get; set; } = new List<ToolDefinition>();

        /// <summary>Maximum tokens to generate</summary>
        public int MaxTokens { get; set; } = 1024;

        /// <summary>Temperature for sampling (0 = deterministic, 1 = creative)</summary>
        public float Temperature { get; set; } = 0.3f;
    }

    /// <summary>
    /// Response from an AI model.
    /// </summary>
    public class ModelResponse
    {
        /// <summary>Whether the request succeeded</summary>
        public bool Success { get; set; }

        /// <summary>Raw text content from the model</summary>
        public string RawContent { get; set; }

        /// <summary>Parsed tool calls from the response</summary>
        public List<ToolCall> ToolCalls { get; set; } = new List<ToolCall>();

        /// <summary>AI's reasoning/thinking (for debug display)</summary>
        public string Reasoning { get; set; }

        /// <summary>Time taken for the request in milliseconds</summary>
        public float LatencyMs { get; set; }

        /// <summary>Error message if Success is false</summary>
        public string Error { get; set; }

        /// <summary>Number of tokens used in the request</summary>
        public int TokensUsed { get; set; }

        /// <summary>The model that generated this response</summary>
        public string Model { get; set; }

        public static ModelResponse Failed(string error) => new ModelResponse
        {
            Success = false,
            Error = error
        };
    }

    /// <summary>
    /// A tool/function that the AI can call.
    /// </summary>
    [Serializable]
    public class ToolDefinition
    {
        /// <summary>Name of the tool (e.g., "click", "type")</summary>
        public string Name { get; set; }

        /// <summary>Description of what the tool does</summary>
        public string Description { get; set; }

        /// <summary>JSON Schema for the tool's parameters</summary>
        public ToolParameters Parameters { get; set; }
    }

    /// <summary>
    /// Parameters schema for a tool.
    /// </summary>
    [Serializable]
    public class ToolParameters
    {
        public string Type { get; set; } = "object";
        public Dictionary<string, ToolProperty> Properties { get; set; } = new Dictionary<string, ToolProperty>();
        public List<string> Required { get; set; } = new List<string>();
    }

    /// <summary>
    /// A property in a tool's parameters.
    /// Supports nested object schemas with Properties and Required fields.
    /// </summary>
    [Serializable]
    public class ToolProperty
    {
        public string Type { get; set; }
        public string Description { get; set; }
        public List<string> Enum { get; set; }
        /// <summary>For array types, specifies the schema for items in the array</summary>
        public ToolProperty Items { get; set; }
        /// <summary>For object types, defines the nested properties</summary>
        public Dictionary<string, ToolProperty> Properties { get; set; }
        /// <summary>For object types, lists required property names</summary>
        public List<string> Required { get; set; }
    }

    /// <summary>
    /// A tool call parsed from the AI's response.
    /// </summary>
    [Serializable]
    public class ToolCall
    {
        /// <summary>Unique ID for the tool call (for tracking results)</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>Name of the tool being called</summary>
        public string Name { get; set; }

        /// <summary>Arguments for the tool</summary>
        public Dictionary<string, object> Arguments { get; set; } = new Dictionary<string, object>();

        /// <summary>Get an argument as a string</summary>
        public string GetString(string key, string defaultValue = null)
        {
            if (Arguments.TryGetValue(key, out var value))
                return value?.ToString() ?? defaultValue;
            return defaultValue;
        }

        /// <summary>Get an argument as a float</summary>
        public float GetFloat(string key, float defaultValue = 0f)
        {
            if (Arguments.TryGetValue(key, out var value))
            {
                if (value is float f) return f;
                if (value is double d) return (float)d;
                if (value is int i) return i;
                if (float.TryParse(value?.ToString(), out var parsed))
                    return parsed;
            }
            return defaultValue;
        }

        /// <summary>Get an argument as an int</summary>
        public int GetInt(string key, int defaultValue = 0)
        {
            if (Arguments.TryGetValue(key, out var value))
            {
                if (value is int i) return i;
                if (value is long l) return (int)l;
                if (value is float f) return (int)f;
                if (value is double d) return (int)d;
                if (int.TryParse(value?.ToString(), out var parsed))
                    return parsed;
            }
            return defaultValue;
        }

        /// <summary>Get an argument as a SearchQuery (handles both object and string formats)</summary>
        /// <param name="key">The argument key</param>
        /// <param name="error">Out parameter containing any deserialization error message</param>
        /// <returns>The parsed SearchQuery, or null if parsing failed</returns>
        public SearchQuery GetSearchQuery(string key, out string error)
        {
            error = null;

            if (!Arguments.TryGetValue(key, out var value) || value == null)
                return null;

            try
            {
                // Serialize to JSON and then deserialize to SearchQuery
                // This handles both Dictionary<string, object> and already-parsed objects
                var json = JsonConvert.SerializeObject(value);
                return JsonConvert.DeserializeObject<SearchQuery>(json);
            }
            catch (JsonException ex)
            {
                error = $"Failed to parse search query: {ex.Message}";

                // If it's a string, try to parse as JSON directly
                if (value is string str && !string.IsNullOrEmpty(str))
                {
                    try
                    {
                        var result = SearchQuery.FromJson(str);
                        if (result != null)
                        {
                            error = null;
                            return result;
                        }
                    }
                    catch (Exception innerEx)
                    {
                        error = $"Failed to parse search query JSON string: {innerEx.Message}";
                    }
                }
                return null;
            }
        }

        /// <summary>Get an argument as a SearchQuery (handles both object and string formats)</summary>
        public SearchQuery GetSearchQuery(string key)
        {
            return GetSearchQuery(key, out _);
        }
    }
}
