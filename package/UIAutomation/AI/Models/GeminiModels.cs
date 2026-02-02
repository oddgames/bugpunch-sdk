using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using UnityEngine;
using UnityEngine.Networking;

namespace ODDGames.UIAutomation.AI
{
    /// <summary>
    /// Information about a Gemini model from the API.
    /// </summary>
    [Serializable]
    public class GeminiModelInfo
    {
        /// <summary>Full model name (e.g., "models/gemini-2.5-flash")</summary>
        public string name;

        /// <summary>Display name for UI</summary>
        public string displayName;

        /// <summary>Model description</summary>
        public string description;

        /// <summary>Maximum input tokens</summary>
        public int inputTokenLimit;

        /// <summary>Maximum output tokens</summary>
        public int outputTokenLimit;

        /// <summary>Supported generation methods (e.g., "generateContent")</summary>
        public List<string> supportedGenerationMethods = new List<string>();

        /// <summary>Whether the model supports thinking/reasoning</summary>
        public bool thinking;

        /// <summary>Gets the short model ID without the "models/" prefix</summary>
        public string ModelId => name?.Replace("models/", "") ?? "";

        /// <summary>Whether this model supports content generation (chat)</summary>
        public bool SupportsChat => supportedGenerationMethods?.Contains("generateContent") ?? false;

        /// <summary>Whether this model supports vision (image input)</summary>
        public bool SupportsVision => InferVisionSupport();

        /// <summary>Whether this model supports tool/function calling</summary>
        public bool SupportsTools => InferToolSupport();

        /// <summary>Whether this model supports structured JSON output</summary>
        public bool SupportsStructuredOutput => InferStructuredOutputSupport();

        /// <summary>Whether this model is a Computer Use model (requires special tool format)</summary>
        public bool IsComputerUseModel => ModelId.ToLowerInvariant().Contains("computer-use");

        /// <summary>
        /// Infers vision support from model name/description.
        /// Gemini 1.5+ and 2.0+ models support vision. Older models like gemini-pro do not.
        /// TTS, embedding, AQA, and computer-use models don't work with standard function calling.
        /// </summary>
        private bool InferVisionSupport()
        {
            var id = ModelId.ToLowerInvariant();

            // Exclude non-standard models that don't work with our function calling approach
            if (id.Contains("tts")) return false;           // Text-to-speech models
            if (id.Contains("embedding")) return false;     // Embedding models
            if (id.Contains("aqa")) return false;           // Attributed QA models
            if (id.Contains("imagen")) return false;        // Image generation (not input)
            if (id.Contains("learnlm")) return false;       // Learning models
            // Note: computer-use models DO support vision, they just need special tool format

            // Vision models include "vision" or are 1.5/2.0+ generation
            if (id.Contains("vision")) return true;
            if (id.Contains("gemini-1.5") || id.Contains("gemini-2")) return true;

            // Older gemini-pro without version doesn't support vision
            if (id == "gemini-pro") return false;

            return true; // Default to true for newer models
        }

        /// <summary>
        /// Infers tool/function calling support from model name.
        /// All modern Gemini models support function calling.
        /// </summary>
        private bool InferToolSupport()
        {
            var id = ModelId.ToLowerInvariant();
            // All gemini-1.5+ and gemini-2+ support tools
            if (id.Contains("gemini-1.5") || id.Contains("gemini-2")) return true;
            // Basic gemini-pro supports function calling
            if (id.StartsWith("gemini-pro")) return true;
            return true; // Default to true
        }

        /// <summary>
        /// Infers structured output (JSON mode) support from model name.
        /// </summary>
        private bool InferStructuredOutputSupport()
        {
            var id = ModelId.ToLowerInvariant();
            // Gemini 1.5+ and 2.0+ support JSON mode
            if (id.Contains("gemini-1.5") || id.Contains("gemini-2")) return true;
            return false;
        }

        public override string ToString() => displayName ?? ModelId;
    }

    /// <summary>
    /// Fetches available Gemini models from the API.
    /// </summary>
    public static class GeminiModels
    {
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

        // Cached models
        private static List<GeminiModelInfo> cachedModels;
        private static DateTime cacheTime;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

        /// <summary>
        /// The selected default model (stored in settings, falls back to first available).
        /// </summary>
        private static string selectedDefaultModel;

        /// <summary>
        /// Gets or sets the default model to use.
        /// Returns the first cached model if no default is set.
        /// </summary>
        public static string DefaultModel
        {
            get
            {
                if (!string.IsNullOrEmpty(selectedDefaultModel))
                    return selectedDefaultModel;

                // Fall back to first cached model
                if (cachedModels != null && cachedModels.Count > 0)
                    return cachedModels[0].ModelId;

                return null;
            }
            set => selectedDefaultModel = value;
        }

        /// <summary>
        /// Gets cached models if available and not expired.
        /// </summary>
        public static List<GeminiModelInfo> CachedModels =>
            cachedModels != null && DateTime.UtcNow - cacheTime < CacheDuration
                ? cachedModels
                : null;

        /// <summary>
        /// Fetches available models from the Gemini API.
        /// </summary>
        public static async Task<List<GeminiModelInfo>> ListModelsAsync(string apiKey, CancellationToken ct = default)
        {
            // Return cache if valid
            if (CachedModels != null)
            {
                return CachedModels;
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogWarning("[GeminiModels] No API key provided");
                return new List<GeminiModelInfo>();
            }

            try
            {
                var url = $"{BaseUrl}?key={apiKey}&pageSize=100";

                using var webRequest = UnityWebRequest.Get(url);
                webRequest.timeout = 30;

                var operation = webRequest.SendWebRequest();

                while (!operation.isDone)
                {
                    if (ct.IsCancellationRequested)
                    {
                        webRequest.Abort();
                        return new List<GeminiModelInfo>();
                    }

                    await Task.Delay(100, ct);
                }

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[GeminiModels] Failed to list models: {webRequest.error}");
                    return new List<GeminiModelInfo>();
                }

                var responseJson = webRequest.downloadHandler.text;
                var models = ParseModelsResponse(responseJson);

                // Filter to models that support chat, tool calling, AND vision (required for AI testing)
                // Exclude Computer Use models - they require built-in Computer Use tools
                var compatibleModels = models.FindAll(m =>
                    m.SupportsChat &&
                    m.SupportsTools &&
                    m.SupportsVision &&
                    !m.IsComputerUseModel);

                // Sort by capability: prefer models with structured output, then by context window size
                compatibleModels.Sort((a, b) =>
                {
                    // Prefer models with structured output support
                    if (a.SupportsStructuredOutput != b.SupportsStructuredOutput)
                        return b.SupportsStructuredOutput.CompareTo(a.SupportsStructuredOutput);
                    // Then by context window size
                    return b.inputTokenLimit.CompareTo(a.inputTokenLimit);
                });

                // Cache the results
                cachedModels = compatibleModels;
                cacheTime = DateTime.UtcNow;

                Debug.Log($"[GeminiModels] Found {compatibleModels.Count} compatible models (chat + tools + vision + structured output, excluding Computer Use)");
                return compatibleModels;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GeminiModels] Exception listing models: {ex.Message}");
                return new List<GeminiModelInfo>();
            }
        }

        /// <summary>
        /// Clears the cached models.
        /// </summary>
        public static void ClearCache()
        {
            cachedModels = null;
        }

        /// <summary>
        /// Gets a model by ID from the cache, or null if not found.
        /// </summary>
        public static GeminiModelInfo GetCachedModel(string modelId)
        {
            if (cachedModels == null || string.IsNullOrEmpty(modelId))
                return null;

            return cachedModels.Find(m =>
                m.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase) ||
                m.name.Equals(modelId, StringComparison.OrdinalIgnoreCase) ||
                m.name.Equals($"models/{modelId}", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Validates that a model supports vision, tools, and structured output by making a test request.
        /// Returns null if valid, or an error message if the model doesn't support required features.
        /// </summary>
        public static async Task<string> ValidateModelCapabilitiesAsync(string apiKey, string modelId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(modelId))
                return "API key and model ID are required";

            // Use different validation for Computer Use models
            if (modelId.ToLowerInvariant().Contains("computer-use"))
            {
                return await ValidateComputerUseModelAsync(apiKey, modelId, ct);
            }

            try
            {
                var url = $"{BaseUrl}/{modelId}:generateContent?key={apiKey}";

                // Create a minimal 1x1 red PNG (68 bytes)
                var minimalPng = Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==");

                // Build a request that tests vision and structured JSON output
                // Note: Cannot use function calling (tools) with responseMimeType - they're mutually exclusive
                var requestBody = $@"{{
                    ""contents"": [{{
                        ""parts"": [
                            {{""text"": ""What color is this 1x1 pixel image? Respond with JSON containing a 'color' field.""}},
                            {{""inline_data"": {{
                                ""mime_type"": ""image/png"",
                                ""data"": ""{Convert.ToBase64String(minimalPng)}""
                            }}}}
                        ]
                    }}],
                    ""generationConfig"": {{
                        ""maxOutputTokens"": 50,
                        ""responseMimeType"": ""application/json"",
                        ""responseSchema"": {{
                            ""type"": ""OBJECT"",
                            ""properties"": {{
                                ""color"": {{""type"": ""STRING"", ""description"": ""The detected color""}}
                            }},
                            ""required"": [""color""]
                        }}
                    }}
                }}";

                var bodyBytes = Encoding.UTF8.GetBytes(requestBody);
                using var webRequest = new UnityWebRequest(url, "POST");
                using var uploadHandler = new UploadHandlerRaw(bodyBytes);
                using var downloadHandler = new DownloadHandlerBuffer();
                webRequest.uploadHandler = uploadHandler;
                webRequest.downloadHandler = downloadHandler;
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.timeout = 30;

                var operation = webRequest.SendWebRequest();

                while (!operation.isDone)
                {
                    if (ct.IsCancellationRequested)
                    {
                        webRequest.Abort();
                        return "Validation cancelled";
                    }
                    await Task.Delay(100, ct);
                }

                var responseBody = webRequest.downloadHandler?.text ?? "";

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    // Try to extract error message from JSON response
                    var errorMessage = ExtractErrorMessage(responseBody);

                    // Check for specific error patterns
                    if (responseBody.Contains("Image input modality is not enabled") ||
                        responseBody.Contains("modality"))
                    {
                        return $"Model does not support image input: {errorMessage}";
                    }

                    if (responseBody.Contains("responseMimeType") ||
                        responseBody.Contains("responseSchema") ||
                        responseBody.Contains("JSON"))
                    {
                        return $"Model does not support structured JSON output: {errorMessage}";
                    }

                    return $"Validation failed: {errorMessage}";
                }

                // Success - model supports all required capabilities
                return null;
            }
            catch (Exception ex)
            {
                return $"Validation error: {ex.Message}";
            }
        }

        /// <summary>
        /// Validates that a model supports vision by making a test request with an image.
        /// Returns null if valid, or an error message if the model doesn't support vision.
        /// </summary>
        [Obsolete("Use ValidateModelCapabilitiesAsync instead")]
        public static Task<string> ValidateVisionSupportAsync(string apiKey, string modelId, CancellationToken ct = default)
            => ValidateModelCapabilitiesAsync(apiKey, modelId, ct);

        /// <summary>
        /// Validates a Computer Use model by making a test request with the Computer Use tool.
        /// </summary>
        private static async Task<string> ValidateComputerUseModelAsync(string apiKey, string modelId, CancellationToken ct)
        {
            try
            {
                var url = $"{BaseUrl}/{modelId}:generateContent?key={apiKey}";

                // Create a minimal 1x1 red PNG
                var minimalPng = Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==");

                // Build a request with the Computer Use tool
                var requestBody = $@"{{
                    ""contents"": [{{
                        ""parts"": [
                            {{""text"": ""Describe what you see in this image.""}},
                            {{""inline_data"": {{
                                ""mime_type"": ""image/png"",
                                ""data"": ""{Convert.ToBase64String(minimalPng)}""
                            }}}}
                        ]
                    }}],
                    ""tools"": [{{
                        ""computer_use"": {{
                            ""environment"": ""ENVIRONMENT_BROWSER""
                        }}
                    }}],
                    ""generationConfig"": {{
                        ""maxOutputTokens"": 50
                    }}
                }}";

                var bodyBytes = Encoding.UTF8.GetBytes(requestBody);
                using var webRequest = new UnityWebRequest(url, "POST");
                using var uploadHandler = new UploadHandlerRaw(bodyBytes);
                using var downloadHandler = new DownloadHandlerBuffer();
                webRequest.uploadHandler = uploadHandler;
                webRequest.downloadHandler = downloadHandler;
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.timeout = 30;

                var operation = webRequest.SendWebRequest();

                while (!operation.isDone)
                {
                    if (ct.IsCancellationRequested)
                    {
                        webRequest.Abort();
                        return "Validation cancelled";
                    }
                    await Task.Delay(100, ct);
                }

                var responseBody = webRequest.downloadHandler?.text ?? "";

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    var errorMessage = ExtractErrorMessage(responseBody);
                    return $"Computer Use model validation failed: {errorMessage}";
                }

                // Success - Computer Use model works
                return null;
            }
            catch (Exception ex)
            {
                return $"Computer Use validation error: {ex.Message}";
            }
        }

        /// <summary>
        /// Extracts error message from Gemini API error response JSON.
        /// </summary>
        private static string ExtractErrorMessage(string responseBody)
        {
            if (string.IsNullOrEmpty(responseBody))
                return "Unknown error";

            try
            {
                var parsed = SimpleJsonParser.Parse(responseBody);
                if (parsed.TryGetValue("error", out var errorObj) && errorObj is Dictionary<string, object> error)
                {
                    return error.GetValueOrDefault("message")?.ToString() ?? "Unknown error";
                }
            }
            catch
            {
                // Ignore parse errors
            }

            // Return truncated raw response if parsing fails
            return responseBody.Length > 200 ? responseBody.Substring(0, 200) + "..." : responseBody;
        }

        private static List<GeminiModelInfo> ParseModelsResponse(string json)
        {
            var models = new List<GeminiModelInfo>();

            try
            {
                var parsed = SimpleJsonParser.Parse(json);

                if (parsed.TryGetValue("models", out var modelsObj) && modelsObj is List<object> modelsList)
                {
                    foreach (var modelObj in modelsList)
                    {
                        if (modelObj is Dictionary<string, object> model)
                        {
                            var info = new GeminiModelInfo
                            {
                                name = model.GetValueOrDefault("name")?.ToString() ?? "",
                                displayName = model.GetValueOrDefault("displayName")?.ToString() ?? "",
                                description = model.GetValueOrDefault("description")?.ToString() ?? ""
                            };

                            if (model.TryGetValue("inputTokenLimit", out var inputLimit))
                            {
                                info.inputTokenLimit = Convert.ToInt32(inputLimit);
                            }

                            if (model.TryGetValue("outputTokenLimit", out var outputLimit))
                            {
                                info.outputTokenLimit = Convert.ToInt32(outputLimit);
                            }

                            if (model.TryGetValue("thinking", out var thinking) && thinking is bool thinkingVal)
                            {
                                info.thinking = thinkingVal;
                            }

                            if (model.TryGetValue("supportedGenerationMethods", out var methods) && methods is List<object> methodList)
                            {
                                foreach (var method in methodList)
                                {
                                    info.supportedGenerationMethods.Add(method?.ToString() ?? "");
                                }
                            }

                            models.Add(info);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GeminiModels] Failed to parse models response: {ex.Message}");
            }

            return models;
        }
    }
}
