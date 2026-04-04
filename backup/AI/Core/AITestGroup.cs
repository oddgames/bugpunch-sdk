using System.Collections.Generic;
using UnityEngine;

namespace ODDGames.UIAutomation.AI
{
    /// <summary>
    /// Groups related AI tests together with shared knowledge and default configuration.
    /// Knowledge from the group is included in the AI context for all tests in the group.
    /// </summary>
    [CreateAssetMenu(fileName = "NewAITestGroup", menuName = "UITest/AI Test Group", order = 101)]
    public class AITestGroup : ScriptableObject
    {
        [Header("Group Info")]
        [Tooltip("Display name for this group in the Test Explorer")]
        public string displayName;

        [Tooltip("Description of what tests in this group cover")]
        [TextArea(2, 4)]
        public string description;

        [Header("Group Knowledge")]
        [Tooltip("Shared context provided to AI for all tests in this group.\nExample: 'This app uses a bottom navigation bar. Main sections are Home, Search, Profile.'")]
        [TextArea(5, 15)]
        public string knowledge;

        [Header("Default Configuration")]
        [Tooltip("Default Gemini model for tests in this group. Leave empty to use project default.")]
        public string defaultModel;

        [Tooltip("Default timeout in seconds for tests in this group")]
        public float defaultTimeout = 180f;

        [Tooltip("Default maximum actions for tests in this group")]
        public int defaultMaxActions = 50;

        [Header("Tests in Group")]
        [Tooltip("Tests that belong to this group (auto-populated based on test.group references)")]
        [SerializeField]
        private List<AITest> tests = new List<AITest>();

        /// <summary>
        /// Gets all tests in this group.
        /// </summary>
        public IReadOnlyList<AITest> Tests => tests;

        /// <summary>
        /// Adds a test to this group.
        /// </summary>
        public void AddTest(AITest test)
        {
            if (test != null && !tests.Contains(test))
            {
                tests.Add(test);
            }
        }

        /// <summary>
        /// Removes a test from this group.
        /// </summary>
        public void RemoveTest(AITest test)
        {
            tests.Remove(test);
        }

        /// <summary>
        /// Refreshes the tests list by finding all AITest assets that reference this group.
        /// </summary>
        public void RefreshTestsList()
        {
            // This will be called from editor code to sync the list
            tests.RemoveAll(t => t == null || t.Group != this);
        }
    }
}
