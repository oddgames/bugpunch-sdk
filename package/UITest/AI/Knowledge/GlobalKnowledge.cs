using System;
using System.Collections.Generic;

namespace ODDGames.UITest.AI
{
    /// <summary>
    /// Project-wide knowledge context for AI tests.
    /// Stored in Project Settings.
    /// </summary>
    [Serializable]
    public class GlobalKnowledge
    {
        /// <summary>
        /// General context about the application being tested.
        /// Example: "This is a mobile game. UI uses Unity's new Input System.
        /// Buttons are usually blue with white text. Loading screens show a spinner."
        /// </summary>
        public string context = "";

        /// <summary>
        /// Common patterns that apply to most tests.
        /// Examples:
        /// - "Back buttons are in top-left corner"
        /// - "Confirmation dialogs have 'Yes' and 'No' buttons"
        /// </summary>
        public List<string> commonPatterns = new List<string>();

        /// <summary>
        /// Default model for new tests. If empty, uses the first available model.
        /// </summary>
        public string defaultModel;

        /// <summary>
        /// Default timeout in seconds.
        /// </summary>
        public float defaultTimeoutSeconds = 180f;

        /// <summary>
        /// Default maximum actions per test.
        /// </summary>
        public int defaultMaxActions = 50;

        /// <summary>
        /// Creates a default instance.
        /// </summary>
        public static GlobalKnowledge CreateDefault()
        {
            return new GlobalKnowledge
            {
                context = "",
                commonPatterns = new List<string>(),
                defaultModel = null,
                defaultTimeoutSeconds = 180f,
                defaultMaxActions = 50
            };
        }
    }
}
