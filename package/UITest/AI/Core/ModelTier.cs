namespace ODDGames.UITest.AI
{
    /// <summary>
    /// Model tiers for AI test execution, ordered by speed/cost.
    /// Lower tiers are faster/cheaper, higher tiers are more capable.
    /// </summary>
    public enum ModelTier
    {
        /// <summary>Local LM Studio with fast model</summary>
        LocalFast = 0,

        /// <summary>Gemini 2.0 Flash Lite - fastest cloud option</summary>
        GeminiFlashLite = 1,

        /// <summary>Gemini 2.0 Flash - balanced speed/capability</summary>
        GeminiFlash = 2,

        /// <summary>Gemini 1.5 Pro - most capable, slowest</summary>
        GeminiPro = 3
    }
}
