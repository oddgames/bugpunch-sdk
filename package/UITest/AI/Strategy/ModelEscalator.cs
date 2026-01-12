using System;
using System.Collections.Generic;

namespace ODDGames.UITest.AI
{
    /// <summary>
    /// Manages model tier escalation when tests are stuck.
    /// </summary>
    public class ModelEscalator
    {
        private readonly Dictionary<ModelTier, IModelProvider> providers = new Dictionary<ModelTier, IModelProvider>();
        private ModelTier currentTier;
        private int escalationCount;

        /// <summary>
        /// Gets the current model tier.
        /// </summary>
        public ModelTier CurrentTier => currentTier;

        /// <summary>
        /// Gets how many times we've escalated during this run.
        /// </summary>
        public int EscalationCount => escalationCount;

        /// <summary>
        /// Gets the current model provider.
        /// </summary>
        public IModelProvider CurrentProvider => GetProvider(currentTier);

        /// <summary>
        /// Event fired when model tier changes.
        /// </summary>
        public event Action<ModelTier, ModelTier, string> OnEscalated;

        /// <summary>
        /// Creates a model escalator starting at the specified tier.
        /// </summary>
        public ModelEscalator(ModelTier startingTier = ModelTier.LocalFast)
        {
            currentTier = startingTier;
        }

        /// <summary>
        /// Registers a provider for a specific tier.
        /// </summary>
        public void RegisterProvider(ModelTier tier, IModelProvider provider)
        {
            providers[tier] = provider;
        }

        /// <summary>
        /// Gets the provider for a specific tier.
        /// </summary>
        public IModelProvider GetProvider(ModelTier tier)
        {
            return providers.TryGetValue(tier, out var provider) ? provider : null;
        }

        /// <summary>
        /// Checks if a provider is available for a tier.
        /// </summary>
        public bool HasProvider(ModelTier tier)
        {
            return providers.ContainsKey(tier) && providers[tier] != null;
        }

        /// <summary>
        /// Resets to the starting tier for a new test run.
        /// </summary>
        public void Reset(ModelTier startingTier)
        {
            currentTier = startingTier;
            escalationCount = 0;
        }

        /// <summary>
        /// Attempts to escalate to a higher model tier.
        /// </summary>
        /// <param name="reason">Reason for escalation</param>
        /// <returns>True if escalation was successful, false if already at max tier</returns>
        public bool TryEscalate(string reason = null)
        {
            var nextTier = GetNextTier(currentTier);

            if (nextTier == currentTier)
            {
                // Already at max tier
                return false;
            }

            // Find next available tier
            while (nextTier != currentTier && !HasProvider(nextTier))
            {
                nextTier = GetNextTier(nextTier);
            }

            if (nextTier == currentTier)
            {
                // No higher tiers available
                return false;
            }

            var previousTier = currentTier;
            currentTier = nextTier;
            escalationCount++;

            OnEscalated?.Invoke(previousTier, currentTier, reason ?? "Unknown");

            return true;
        }

        /// <summary>
        /// Forces a specific tier (use with caution).
        /// </summary>
        public bool SetTier(ModelTier tier)
        {
            if (!HasProvider(tier))
                return false;

            var previousTier = currentTier;
            currentTier = tier;

            if (tier != previousTier)
            {
                OnEscalated?.Invoke(previousTier, currentTier, "Manual tier change");
            }

            return true;
        }

        /// <summary>
        /// Gets the next tier in the escalation chain.
        /// </summary>
        public static ModelTier GetNextTier(ModelTier current)
        {
            return current switch
            {
                ModelTier.LocalFast => ModelTier.GeminiFlashLite,
                ModelTier.GeminiFlashLite => ModelTier.GeminiFlash,
                ModelTier.GeminiFlash => ModelTier.GeminiPro,
                ModelTier.GeminiPro => ModelTier.GeminiPro, // Max tier
                _ => current
            };
        }

        /// <summary>
        /// Gets the tier level (0 = lowest, 3 = highest).
        /// </summary>
        public static int GetTierLevel(ModelTier tier)
        {
            return tier switch
            {
                ModelTier.LocalFast => 0,
                ModelTier.GeminiFlashLite => 1,
                ModelTier.GeminiFlash => 2,
                ModelTier.GeminiPro => 3,
                _ => 0
            };
        }

        /// <summary>
        /// Checks if the current tier is the maximum available.
        /// </summary>
        public bool IsAtMaxTier()
        {
            var nextTier = GetNextTier(currentTier);

            // Check if next tier is different and available
            while (nextTier != currentTier)
            {
                if (HasProvider(nextTier))
                    return false;

                nextTier = GetNextTier(nextTier);
            }

            return true;
        }

        /// <summary>
        /// Gets a description of the current escalation state.
        /// </summary>
        public string GetStateDescription()
        {
            var tierName = currentTier.ToString();
            var maxNote = IsAtMaxTier() ? " (max)" : "";
            var escalationNote = escalationCount > 0 ? $", escalated {escalationCount}x" : "";

            return $"{tierName}{maxNote}{escalationNote}";
        }

        /// <summary>
        /// Gets all registered tiers in order.
        /// </summary>
        public IEnumerable<ModelTier> GetAvailableTiers()
        {
            foreach (ModelTier tier in Enum.GetValues(typeof(ModelTier)))
            {
                if (HasProvider(tier))
                    yield return tier;
            }
        }
    }
}
