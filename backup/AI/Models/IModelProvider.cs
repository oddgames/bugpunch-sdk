using System.Threading.Tasks;
using System.Threading;


namespace ODDGames.UIAutomation.AI
{
    /// <summary>
    /// Interface for AI model providers.
    /// </summary>
    public interface IModelProvider
    {
        /// <summary>Human-readable name of this provider</summary>
        string Name { get; }

        /// <summary>The model ID being used (e.g., "gemini-2.5-flash")</summary>
        string ModelId { get; }

        /// <summary>Whether this provider supports vision (image input)</summary>
        bool SupportsVision { get; }

        /// <summary>Whether this provider supports tool/function calling</summary>
        bool SupportsToolCalling { get; }

        /// <summary>Approximate context window size in tokens</summary>
        int ContextWindowSize { get; }

        /// <summary>
        /// Sends a request to the model and returns the response.
        /// </summary>
        Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken ct = default);

        /// <summary>
        /// Tests the connection to this provider.
        /// </summary>
        Task<bool> TestConnectionAsync(CancellationToken ct = default);

        /// <summary>
        /// Estimates the token count for the given text.
        /// </summary>
        int EstimateTokens(string text);
    }
}
