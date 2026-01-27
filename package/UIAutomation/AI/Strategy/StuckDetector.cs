using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ODDGames.UIAutomation.AI
{
    /// <summary>
    /// Detects when an AI test is stuck using multiple heuristics.
    /// </summary>
    public class StuckDetector
    {
        private readonly StuckDetectorConfig config;
        private readonly List<ScreenHashRecord> screenHistory = new List<ScreenHashRecord>();
        private readonly List<ActionRecord> actionHistory = new List<ActionRecord>();

        private float testStartTime;
        private int consecutiveSimilarScreens;
        private int consecutiveFailedActions;
        private int consecutiveNoProgress;
        private string lastSignificantScreenHash;

        /// <summary>
        /// Gets the current stuck level (0 = not stuck, higher = more stuck).
        /// </summary>
        public int StuckLevel { get; private set; }

        /// <summary>
        /// Gets the reason for the current stuck state.
        /// </summary>
        public string StuckReason { get; private set; }

        /// <summary>
        /// Event fired when stuck level changes.
        /// </summary>
        public event Action<int, string> OnStuckLevelChanged;

        public StuckDetector(StuckDetectorConfig config = null)
        {
            this.config = config ?? new StuckDetectorConfig();
        }

        /// <summary>
        /// Resets the detector for a new test run.
        /// </summary>
        public void Reset()
        {
            screenHistory.Clear();
            actionHistory.Clear();
            testStartTime = Time.realtimeSinceStartup;
            consecutiveSimilarScreens = 0;
            consecutiveFailedActions = 0;
            consecutiveNoProgress = 0;
            lastSignificantScreenHash = null;
            StuckLevel = 0;
            StuckReason = null;
        }

        /// <summary>
        /// Records a new screen state using just the visual hash.
        /// Prefer using RecordScreen(visualHash, elementStateHash) for accurate change detection.
        /// </summary>
        public void RecordScreen(string screenHash)
        {
            RecordScreen(screenHash, null);
        }

        /// <summary>
        /// Records a new screen state with both visual and element state hashes.
        /// The screen is considered "changed" if either hash differs from the previous.
        /// </summary>
        public void RecordScreen(string visualHash, string elementStateHash)
        {
            var record = new ScreenHashRecord
            {
                VisualHash = visualHash,
                ElementStateHash = elementStateHash,
                Timestamp = Time.realtimeSinceStartup
            };

            screenHistory.Add(record);

            // Check for similar screens - screen is "changed" if EITHER hash changed
            if (screenHistory.Count > 1)
            {
                var previous = screenHistory[screenHistory.Count - 2];

                // Visual similarity check
                bool visualSimilar = ScreenHash.AreSimilar(visualHash, previous.VisualHash, config.ScreenSimilarityThreshold);

                // Element state check - states must be exactly equal for "no change"
                bool elementStateSame = string.IsNullOrEmpty(elementStateHash) ||
                                        string.IsNullOrEmpty(previous.ElementStateHash) ||
                                        ScreenHash.AreElementStatesEqual(elementStateHash, previous.ElementStateHash);

                // Screen is "unchanged" only if both visual AND element state are the same
                if (visualSimilar && elementStateSame)
                {
                    consecutiveSimilarScreens++;
                    Debug.Log($"[StuckDetector] Screen unchanged (visual similar: {visualSimilar}, state same: {elementStateSame})");
                }
                else
                {
                    consecutiveSimilarScreens = 0;
                    lastSignificantScreenHash = visualHash;
                    Debug.Log($"[StuckDetector] Screen changed! (visual similar: {visualSimilar}, state same: {elementStateSame})");
                }
            }
            else
            {
                lastSignificantScreenHash = visualHash;
            }

            UpdateStuckLevel();
        }

        /// <summary>
        /// Records an action result.
        /// </summary>
        public void RecordAction(string actionType, string target, bool success)
        {
            var record = new ActionRecord
            {
                ActionType = actionType,
                Target = target,
                Success = success,
                Timestamp = Time.realtimeSinceStartup
            };

            actionHistory.Add(record);

            if (!success)
            {
                consecutiveFailedActions++;
            }
            else
            {
                consecutiveFailedActions = 0;
            }

            // Check for repetitive actions
            CheckRepetitiveActions();

            UpdateStuckLevel();
        }

        /// <summary>
        /// Records that meaningful progress was made.
        /// </summary>
        public void RecordProgress()
        {
            consecutiveNoProgress = 0;
            UpdateStuckLevel();
        }

        /// <summary>
        /// Checks if the test should escalate to a higher model tier.
        /// </summary>
        public bool ShouldEscalate()
        {
            return StuckLevel >= config.EscalationThreshold;
        }

        /// <summary>
        /// Gets suggestions for the AI based on current stuck state.
        /// </summary>
        public string GetStuckSuggestions()
        {
            if (StuckLevel == 0)
                return null;

            var suggestions = new List<string>();

            if (consecutiveSimilarScreens > 2)
            {
                suggestions.Add("The screen hasn't changed - try a different action or target.");
            }

            if (consecutiveFailedActions > 1)
            {
                suggestions.Add("Recent actions failed - verify element IDs are correct or try different elements.");
            }

            if (HasRepetitiveActions())
            {
                suggestions.Add("You've been repeating similar actions - try a completely different approach.");
            }

            float elapsed = Time.realtimeSinceStartup - testStartTime;
            if (elapsed > config.TimeWarningThresholdSeconds)
            {
                suggestions.Add($"Test has been running for {elapsed:F0} seconds - focus on the most direct path to the goal.");
            }

            return suggestions.Count > 0 ? string.Join(" ", suggestions) : null;
        }

        private void CheckRepetitiveActions()
        {
            if (actionHistory.Count < 4)
                return;

            // Check last 4 actions for repetition
            var lastFour = actionHistory.TakeLast(4).ToList();
            var pattern = $"{lastFour[0].ActionType}:{lastFour[0].Target}|{lastFour[1].ActionType}:{lastFour[1].Target}";
            var repeated = $"{lastFour[2].ActionType}:{lastFour[2].Target}|{lastFour[3].ActionType}:{lastFour[3].Target}";

            if (pattern == repeated)
            {
                consecutiveNoProgress++;
            }
        }

        private bool HasRepetitiveActions()
        {
            if (actionHistory.Count < 4)
                return false;

            var lastFour = actionHistory.TakeLast(4).ToList();
            var pattern = $"{lastFour[0].ActionType}:{lastFour[0].Target}";
            var allSame = lastFour.All(a => $"{a.ActionType}:{a.Target}" == pattern);

            return allSame;
        }

        private void UpdateStuckLevel()
        {
            int newLevel = 0;
            string reason = null;

            // Factor 1: Similar screens
            if (consecutiveSimilarScreens >= config.SimilarScreensForStuck)
            {
                newLevel = Math.Max(newLevel, 1);
                reason = $"Screen unchanged for {consecutiveSimilarScreens} actions";

                if (consecutiveSimilarScreens >= config.SimilarScreensForStuck * 2)
                {
                    newLevel = Math.Max(newLevel, 2);
                }
            }

            // Factor 2: Failed actions
            if (consecutiveFailedActions >= config.FailedActionsForStuck)
            {
                newLevel = Math.Max(newLevel, 1);
                reason = reason == null
                    ? $"{consecutiveFailedActions} consecutive failed actions"
                    : reason + $"; {consecutiveFailedActions} failed actions";

                if (consecutiveFailedActions >= config.FailedActionsForStuck * 2)
                {
                    newLevel = Math.Max(newLevel, 2);
                }
            }

            // Factor 3: No progress
            if (consecutiveNoProgress >= config.NoProgressActionsForStuck)
            {
                newLevel = Math.Max(newLevel, 1);
                reason = reason == null
                    ? "Repetitive actions with no progress"
                    : reason + "; repetitive actions";

                if (consecutiveNoProgress >= config.NoProgressActionsForStuck * 2)
                {
                    newLevel = Math.Max(newLevel, 2);
                }
            }

            // Factor 4: Time elapsed
            float elapsed = Time.realtimeSinceStartup - testStartTime;
            if (elapsed > config.TimeWarningThresholdSeconds)
            {
                newLevel = Math.Max(newLevel, 1);
                if (elapsed > config.TimeStuckThresholdSeconds)
                {
                    newLevel = Math.Max(newLevel, 2);
                    reason = reason == null
                        ? $"Test running too long ({elapsed:F0}s)"
                        : reason + $"; running {elapsed:F0}s";
                }
            }

            // Factor 5: Total action count without progress
            if (actionHistory.Count > config.MaxActionsWithoutEscalation)
            {
                newLevel = Math.Max(newLevel, 1);
            }

            if (newLevel != StuckLevel)
            {
                StuckLevel = newLevel;
                StuckReason = reason;
                OnStuckLevelChanged?.Invoke(newLevel, reason);
            }
        }

        private struct ScreenHashRecord
        {
            public string VisualHash;
            public string ElementStateHash;
            public float Timestamp;
        }

        private struct ActionRecord
        {
            public string ActionType;
            public string Target;
            public bool Success;
            public float Timestamp;
        }
    }

    /// <summary>
    /// Configuration for stuck detection thresholds.
    /// </summary>
    [Serializable]
    public class StuckDetectorConfig
    {
        /// <summary>Screen hash similarity threshold (Hamming distance). With 2x2 (4-bit) hash, use 1.</summary>
        public int ScreenSimilarityThreshold = 1;

        /// <summary>Number of similar screens before considering stuck</summary>
        public int SimilarScreensForStuck = 3;

        /// <summary>Number of consecutive failed actions before stuck</summary>
        public int FailedActionsForStuck = 3;

        /// <summary>Number of no-progress actions before stuck</summary>
        public int NoProgressActionsForStuck = 4;

        /// <summary>Seconds before showing time warning</summary>
        public float TimeWarningThresholdSeconds = 60f;

        /// <summary>Seconds before considering time-stuck</summary>
        public float TimeStuckThresholdSeconds = 120f;

        /// <summary>Max actions before forced escalation consideration</summary>
        public int MaxActionsWithoutEscalation = 20;

        /// <summary>Stuck level at which to trigger model escalation</summary>
        public int EscalationThreshold = 2;
    }
}
