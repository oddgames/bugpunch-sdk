using System;
using System.Collections.Generic;

namespace ODDGames.UIAutomation.AI
{
    /// <summary>
    /// A recorded sequence of actions from a successful test run.
    /// Can be replayed to quickly execute tests without AI inference.
    /// </summary>
    [Serializable]
    public class ActionSequence
    {
        /// <summary>When this sequence was recorded</summary>
        public long timestampTicks;

        /// <summary>How long the original run took</summary>
        public float durationSeconds;

        /// <summary>Screen hash at the start of the test</summary>
        public string screenHashAtStart;

        /// <summary>The sequence of actions that led to success</summary>
        public List<RecordedAction> actions = new List<RecordedAction>();

        public DateTime Timestamp
        {
            get => new DateTime(timestampTicks);
            set => timestampTicks = value.Ticks;
        }
    }

    /// <summary>
    /// A single recorded action within an ActionSequence.
    /// </summary>
    [Serializable]
    public class RecordedAction
    {
        /// <summary>Type of action: "click", "type", "drag", "scroll", "wait"</summary>
        public string actionType;

        /// <summary>Target element identifier or description</summary>
        public string target;

        /// <summary>Parameters for the action</summary>
        public Dictionary<string, object> parameters = new Dictionary<string, object>();

        /// <summary>Screen hash before this action was executed</summary>
        public string screenHashBefore;

        /// <summary>Screen hash after this action was executed</summary>
        public string screenHashAfter;

        /// <summary>AI's reasoning for choosing this action (for debugging)</summary>
        public string reasoning;
    }
}
