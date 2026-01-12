using System.Threading;
using Cysharp.Threading.Tasks;

namespace ODDGames.UITest.AI
{
    /// <summary>
    /// Interface for AI model providers (LM Studio, Gemini, etc.)
    /// </summary>
    public interface IModelProvider
    {
        /// <summary>Human-readable name of this provider</summary>
        string Name { get; }

        /// <summary>The model tier this provider represents</summary>
        ModelTier Tier { get; }

        /// <summary>Whether this provider supports vision (image input)</summary>
        bool SupportsVision { get; }

        /// <summary>Whether this provider supports tool/function calling</summary>
        bool SupportsToolCalling { get; }

        /// <summary>Approximate context window size in tokens</summary>
        int ContextWindowSize { get; }

        /// <summary>
        /// Sends a request to the model and returns the response.
        /// </summary>
        UniTask<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken ct = default);

        /// <summary>
        /// Tests the connection to this provider.
        /// </summary>
        UniTask<bool> TestConnectionAsync(CancellationToken ct = default);

        /// <summary>
        /// Estimates the token count for the given text.
        /// </summary>
        int EstimateTokens(string text);
    }
}
