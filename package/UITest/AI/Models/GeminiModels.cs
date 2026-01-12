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

        /// <summary>
        /// Infers vision support from model name/description.
        /// Gemini 1.5+ and 2.0+ models support vision. Older models like gemini-pro do not.
        /// </summary>
        private bool InferVisionSupport()
        {
            var id = ModelId.ToLowerInvariant();
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
        public static async UniTask<List<GeminiModelInfo>> ListModelsAsync(string apiKey, CancellationToken ct = default)
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

                    await UniTask.Delay(100, ignoreTimeScale: true, cancellationToken: ct);
                }

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[GeminiModels] Failed to list models: {webRequest.error}");
                    return new List<GeminiModelInfo>();
                }

                var responseJson = webRequest.downloadHandler.text;
                var models = ParseModelsResponse(responseJson);

                // Filter to models that support chat and tool calling (required for AI tests)
                var compatibleModels = models.FindAll(m => m.SupportsChat && m.SupportsTools);

                // Sort by capability: vision + tools first, then by name
                compatibleModels.Sort((a, b) =>
                {
                    // Prefer models with vision support
                    if (a.SupportsVision != b.SupportsVision)
                        return b.SupportsVision.CompareTo(a.SupportsVision);
                    // Then by context window size
                    return b.inputTokenLimit.CompareTo(a.inputTokenLimit);
                });

                // Cache the results
                cachedModels = compatibleModels;
                cacheTime = DateTime.UtcNow;

                Debug.Log($"[GeminiModels] Found {compatibleModels.Count} compatible models (chat + tools)");
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
