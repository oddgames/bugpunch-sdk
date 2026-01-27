using System;
using System.Collections.Generic;

namespace ODDGames.UIAutomation.AI
{
    /// <summary>
    /// Complete record of a single AI test execution.
    /// Serialized to JSON for storage.
    /// </summary>
    [Serializable]
    public class AITestRun
    {
        /// <summary>Unique identifier for this run</summary>
        public string id;

        /// <summary>Path to the AITest asset</summary>
        public string testAssetPath;

        /// <summary>Name of the test</summary>
        public string testName;

        /// <summary>Name of the test group (if any)</summary>
        public string groupName;

        /// <summary>Start time (UTC ticks)</summary>
        public long startTimeTicks;

        /// <summary>End time (UTC ticks)</summary>
        public long endTimeTicks;

        /// <summary>Duration in seconds</summary>
        public float durationSeconds;

        /// <summary>Final test status</summary>
        public TestStatus status;

        /// <summary>Reason for failure (if failed)</summary>
        public string failureReason;

        /// <summary>Stack trace for errors</summary>
        public string errorStackTrace;

        /// <summary>Number of actions executed</summary>
        public int actionsExecuted;

        /// <summary>Model used for this test</summary>
        public string finalModel;

        /// <summary>All actions executed</summary>
        public List<StoredActionRecord> actions = new List<StoredActionRecord>();

        /// <summary>Screenshot metadata (actual images stored separately)</summary>
        public List<StoredScreenshotRecord> screenshots = new List<StoredScreenshotRecord>();

        /// <summary>Log entries</summary>
        public List<string> logs = new List<string>();

        /// <summary>Full AI conversation log</summary>
        public string conversationLog;

        /// <summary>Start time as DateTime</summary>
        public DateTime StartTime => new DateTime(startTimeTicks, DateTimeKind.Utc);

        /// <summary>End time as DateTime</summary>
        public DateTime EndTime => new DateTime(endTimeTicks, DateTimeKind.Utc);

        /// <summary>
        /// Creates an AITestRun from a result.
        /// </summary>
        public static AITestRun FromResult(AITestResult result, string testAssetPath)
        {
            var run = new AITestRun
            {
                id = Guid.NewGuid().ToString(),
                testAssetPath = testAssetPath,
                testName = result.TestName,
                groupName = result.GroupName,
                startTimeTicks = DateTime.UtcNow.AddSeconds(-result.DurationSeconds).Ticks,
                endTimeTicks = DateTime.UtcNow.Ticks,
                durationSeconds = result.DurationSeconds,
                status = result.Status,
                failureReason = result.Status != TestStatus.Passed ? result.Message : null,
                actionsExecuted = result.ActionCount,
                finalModel = result.FinalModel,
                logs = result.Logs != null ? new List<string>(result.Logs) : new List<string>()
            };

            // Convert action records
            if (result.Actions != null)
            {
                foreach (var action in result.Actions)
                {
                    run.actions.Add(new StoredActionRecord
                    {
                        index = action.Index,
                        timestamp = action.Timestamp,
                        actionType = action.ActionType,
                        target = action.Target,
                        success = action.Success,
                        error = action.Error,
                        reasoning = action.Reasoning,
                        screenshotId = null // Will be linked during storage
                    });
                }
            }

            // Convert screenshot records (without actual bytes)
            if (result.Screenshots != null)
            {
                foreach (var ss in result.Screenshots)
                {
                    run.screenshots.Add(new StoredScreenshotRecord
                    {
                        id = ss.Id,
                        timestamp = ss.Timestamp,
                        screenHash = ss.ScreenHash,
                        elementCount = ss.ElementCount,
                        filePath = null // Will be set during storage
                    });
                }
            }

            return run;
        }
    }

    /// <summary>
    /// Stored version of an action record (for JSON serialization).
    /// </summary>
    [Serializable]
    public class StoredActionRecord
    {
        public int index;
        public float timestamp;
        public string actionType;
        public string target;
        public string parametersJson;
        public bool success;
        public string error;
        public string screenshotId;
        public string reasoning;
    }

    /// <summary>
    /// Stored version of a screenshot record (for JSON serialization).
    /// </summary>
    [Serializable]
    public class StoredScreenshotRecord
    {
        public string id;
        public float timestamp;
        public string filePath;
        public string screenHash;
        public int elementCount;
    }

    /// <summary>
    /// Summary of a run for quick indexing.
    /// </summary>
    [Serializable]
    public class RunSummary
    {
        public string id;
        public string testName;
        public string groupName;
        public TestStatus status;
        public long timestampTicks;
        public float duration;

        public DateTime Timestamp => new DateTime(timestampTicks, DateTimeKind.Utc);
    }
}
