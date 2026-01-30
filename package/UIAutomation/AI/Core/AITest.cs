using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ODDGames.UIAutomation.AI
{
    /// <summary>
    /// ScriptableObject-based AI test configuration.
    /// Create these assets to define tests that are automatically discovered
    /// and run by the Unity Test Runner via <see cref="AITestDiscovery"/>.
    ///
    /// Usage:
    /// 1. Create > UITest > AI Test
    /// 2. Fill in the prompt describing what to test
    /// 3. The test appears automatically in Test Runner under "AITestDiscovery"
    ///
    /// Example prompt: "Navigate to settings, toggle dark mode, verify it's enabled"
    /// </summary>
    [CreateAssetMenu(fileName = "NewAITest", menuName = "UITest/AI Test", order = 100)]
    public class AITest : ScriptableObject
    {
        [Header("Test Definition")]
        [Tooltip("Natural language description of what the test should do.\nExample: 'Navigate to settings and enable dark mode'")]
        [TextArea(3, 10)]
        public string prompt;

        [Header("Test-Specific Knowledge")]
        [Tooltip("Additional context specific to this test.\nExample: 'The settings button has a gear icon in the top-right corner'")]
        [TextArea(3, 8)]
        public string knowledge;

        [Header("Configuration")]
        [Tooltip("Gemini model to use for this test. Leave empty to use project default.")]
        public string model;

        [Tooltip("Maximum number of actions before failing the test")]
        [Range(10, 200)]
        public int maxActions = 50;

        [Tooltip("Maximum time in seconds before failing the test")]
        [Range(30, 600)]
        public float timeoutSeconds = 180f;

        [Tooltip("Delay between actions in seconds (for visual debugging)")]
        [Range(0.1f, 2f)]
        public float actionDelaySeconds = 0.3f;

        [Header("Group")]
        [Tooltip("Optional group this test belongs to. Group knowledge will be included in AI context.")]
        [SerializeField]
        private AITestGroup _group;

        /// <summary>
        /// The group this test belongs to.
        /// </summary>
        public AITestGroup Group => _group;

        /// <summary>
        /// Sets the group for this test.
        /// </summary>
        public void SetGroup(AITestGroup newGroup)
        {
            if (_group == newGroup)
                return;

            // Remove from old group
            if (_group != null)
            {
                _group.RemoveTest(this);
            }

            _group = newGroup;

            // Add to new group
            if (_group != null)
            {
                _group.AddTest(this);
            }

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        [Header("History (Auto-populated)")]
        [Tooltip("Successful action sequences from previous runs. Used for fast replay.")]
        [SerializeField]
        private List<ActionSequence> successfulRuns = new List<ActionSequence>();

        /// <summary>
        /// Gets all successful runs for this test.
        /// </summary>
        public IReadOnlyList<ActionSequence> SuccessfulRuns => successfulRuns;

        /// <summary>
        /// Records a successful run for future replay.
        /// </summary>
        public void RecordSuccessfulRun(ActionSequence sequence)
        {
            if (sequence == null || sequence.actions.Count == 0)
                return;

            // Keep only the last 5 successful runs
            while (successfulRuns.Count >= 5)
            {
                successfulRuns.RemoveAt(0);
            }

            successfulRuns.Add(sequence);

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>
        /// Gets the best historical run for replay.
        /// Prefers shorter sequences with matching starting screen.
        /// </summary>
        public ActionSequence GetBestHistoricalRun(string currentScreenHash = null)
        {
            if (successfulRuns.Count == 0)
                return null;

            // If we have a current screen hash, prefer runs that started from a similar screen
            if (!string.IsNullOrEmpty(currentScreenHash))
            {
                var matchingStart = successfulRuns
                    .Where(r => !string.IsNullOrEmpty(r.screenHashAtStart))
                    .OrderBy(r => r.actions.Count)
                    .FirstOrDefault(r => IsSimilarHash(r.screenHashAtStart, currentScreenHash));

                if (matchingStart != null)
                    return matchingStart;
            }

            // Otherwise return the most recent shortest run
            return successfulRuns
                .OrderByDescending(r => r.timestampTicks)
                .ThenBy(r => r.actions.Count)
                .FirstOrDefault();
        }

        /// <summary>
        /// Clears all recorded successful runs.
        /// </summary>
        public void ClearHistory()
        {
            successfulRuns.Clear();

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>
        /// Simple hash similarity check (Hamming distance).
        /// </summary>
        private static bool IsSimilarHash(string hash1, string hash2, int threshold = 5)
        {
            if (string.IsNullOrEmpty(hash1) || string.IsNullOrEmpty(hash2))
                return false;

            if (hash1.Length != hash2.Length)
                return false;

            int differences = 0;
            for (int i = 0; i < hash1.Length; i++)
            {
                if (hash1[i] != hash2[i])
                    differences++;
            }

            return differences <= threshold;
        }

        private void OnValidate()
        {
            // Auto-add to group's test list when group is assigned
            if (_group != null)
            {
                _group.AddTest(this);
            }
        }
    }
}
