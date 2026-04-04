using System.Threading.Tasks;
using System;
using System.Threading;

using UnityEngine;

namespace ODDGames.UIAutomation.AI
{
    /// <summary>
    /// Replays previously successful action sequences for faster test execution.
    /// </summary>
    public class HistoryReplayer
    {
        private readonly HistoryReplayerConfig config;

        /// <summary>
        /// Current replay status.
        /// </summary>
        public ReplayStatus Status { get; private set; }

        /// <summary>
        /// Index of current action in replay sequence.
        /// </summary>
        public int CurrentActionIndex { get; private set; }

        /// <summary>
        /// Total actions in the sequence being replayed.
        /// </summary>
        public int TotalActions { get; private set; }

        /// <summary>
        /// Event fired when replay diverges from expected state.
        /// </summary>
        public event Action<int, string> OnDiverged;

        /// <summary>
        /// Event fired when an action is replayed.
        /// </summary>
        public event Action<int, RecordedAction> OnActionReplayed;

        public HistoryReplayer(HistoryReplayerConfig config = null)
        {
            this.config = config ?? new HistoryReplayerConfig();
        }

        /// <summary>
        /// Attempts to replay a successful action sequence.
        /// </summary>
        /// <param name="sequence">The sequence to replay</param>
        /// <param name="currentScreenHash">Current screen hash to validate starting point</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Result indicating success, divergence, or failure</returns>
        public async Task<ReplayResult> ReplayAsync(
            ActionSequence sequence,
            string currentScreenHash,
            CancellationToken ct = default)
        {
            if (sequence == null || sequence.actions.Count == 0)
            {
                return new ReplayResult
                {
                    Status = ReplayStatus.NoHistory,
                    Message = "No action sequence provided"
                };
            }

            Status = ReplayStatus.InProgress;
            CurrentActionIndex = 0;
            TotalActions = sequence.actions.Count;

            // Validate starting screen
            if (config.ValidateStartingScreen &&
                !string.IsNullOrEmpty(sequence.screenHashAtStart) &&
                !string.IsNullOrEmpty(currentScreenHash))
            {
                if (!ScreenHash.AreSimilar(sequence.screenHashAtStart, currentScreenHash, config.ScreenSimilarityThreshold))
                {
                    Status = ReplayStatus.Diverged;
                    return new ReplayResult
                    {
                        Status = ReplayStatus.Diverged,
                        DivergedAtAction = 0,
                        Message = "Starting screen does not match recorded sequence"
                    };
                }
            }

            // Replay each action
            for (int i = 0; i < sequence.actions.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                CurrentActionIndex = i;
                var recordedAction = sequence.actions[i];

                try
                {
                    // Parse the action
                    var action = ParseRecordedAction(recordedAction);
                    if (action == null)
                    {
                        Status = ReplayStatus.Failed;
                        return new ReplayResult
                        {
                            Status = ReplayStatus.Failed,
                            DivergedAtAction = i,
                            Message = $"Failed to parse action: {recordedAction.actionType}"
                        };
                    }

                    // Execute the action
                    var result = await AIActionExecutor.ExecuteAsync(action, ct);

                    if (!result.Success)
                    {
                        Status = ReplayStatus.Failed;
                        OnDiverged?.Invoke(i, result.Error);
                        return new ReplayResult
                        {
                            Status = ReplayStatus.Failed,
                            DivergedAtAction = i,
                            Message = $"Action failed: {result.Error}"
                        };
                    }

                    OnActionReplayed?.Invoke(i, recordedAction);

                    // Validate screen after action if we have expected hash
                    if (config.ValidateEachStep && !string.IsNullOrEmpty(recordedAction.screenHashAfter))
                    {
                        if (!Application.isPlaying || ct.IsCancellationRequested)
                            break;

                        try
                        {
                            await Task.Delay(config.ScreenCaptureDelayMs, ct);
                        }
                        catch { break; }

                        var screenState = await AIScreenCapture.CaptureAsync(annotateScreenshot: false);

                        if (!ScreenHash.AreSimilar(recordedAction.screenHashAfter, screenState.ScreenHash, config.ScreenSimilarityThreshold))
                        {
                            Status = ReplayStatus.Diverged;
                            OnDiverged?.Invoke(i, "Screen state diverged from expected");
                            return new ReplayResult
                            {
                                Status = ReplayStatus.Diverged,
                                DivergedAtAction = i,
                                Message = $"Screen diverged after action {i + 1}",
                                LastScreenState = screenState
                            };
                        }
                    }

                    // Delay between actions
                    if (i < sequence.actions.Count - 1)
                    {
                        if (!Application.isPlaying || ct.IsCancellationRequested)
                            break;

                        try
                        {
                            await Task.Delay(config.ActionDelayMs, ct);
                        }
                        catch { break; }
                    }
                }
                catch (OperationCanceledException)
                {
                    Status = ReplayStatus.Cancelled;
                    throw;
                }
                catch (Exception ex)
                {
                    Status = ReplayStatus.Failed;
                    return new ReplayResult
                    {
                        Status = ReplayStatus.Failed,
                        DivergedAtAction = i,
                        Message = $"Exception during replay: {ex.Message}"
                    };
                }
            }

            Status = ReplayStatus.Completed;
            return new ReplayResult
            {
                Status = ReplayStatus.Completed,
                Message = $"Successfully replayed {sequence.actions.Count} actions"
            };
        }

        /// <summary>
        /// Parses a recorded action into an executable AIAction.
        /// </summary>
        private AIAction ParseRecordedAction(RecordedAction recorded)
        {
            switch (recorded.actionType.ToLower())
            {
                case "click":
                    var click = new ClickAction { Search = ParseSearchQuery(recorded.target) };
                    if (recorded.parameters != null)
                    {
                        if (recorded.parameters.TryGetValue("x", out var x) &&
                            recorded.parameters.TryGetValue("y", out var y))
                        {
                            click.ScreenPosition = new Vector2(
                                Convert.ToSingle(x),
                                Convert.ToSingle(y)
                            );
                        }
                    }
                    return click;

                case "type":
                    var type = new TypeAction { Search = ParseSearchQuery(recorded.target) };
                    if (recorded.parameters != null)
                    {
                        if (recorded.parameters.TryGetValue("text", out var text))
                            type.Text = text.ToString();
                        if (recorded.parameters.TryGetValue("clear_first", out var clear))
                            type.ClearFirst = Convert.ToBoolean(clear);
                        if (recorded.parameters.TryGetValue("press_enter", out var enter))
                            type.PressEnter = Convert.ToBoolean(enter);
                    }
                    return type;

                case "drag":
                    var drag = new DragAction { FromSearch = ParseSearchQuery(recorded.target) };
                    if (recorded.parameters != null)
                    {
                        if (recorded.parameters.TryGetValue("to", out var toSearch))
                            drag.ToSearch = ParseSearchQuery(toSearch.ToString());
                        if (recorded.parameters.TryGetValue("direction", out var dir))
                            drag.Direction = dir.ToString();
                        if (recorded.parameters.TryGetValue("distance", out var dist))
                            drag.Distance = Convert.ToSingle(dist);
                        if (recorded.parameters.TryGetValue("duration", out var dur))
                            drag.Duration = Convert.ToSingle(dur);
                    }
                    return drag;

                case "scroll":
                    var scroll = new ScrollAction { Search = ParseSearchQuery(recorded.target) };
                    if (recorded.parameters != null)
                    {
                        if (recorded.parameters.TryGetValue("direction", out var scrollDir))
                            scroll.Direction = scrollDir.ToString();
                        if (recorded.parameters.TryGetValue("amount", out var amt))
                            scroll.Amount = Convert.ToSingle(amt);
                    }
                    return scroll;

                case "wait":
                    var wait = new WaitAction();
                    if (recorded.parameters != null &&
                        recorded.parameters.TryGetValue("seconds", out var secs))
                    {
                        wait.Seconds = Convert.ToSingle(secs);
                    }
                    return wait;

                default:
                    return null;
            }
        }

        /// <summary>
        /// Parses a target string into a SearchQuery.
        /// The target can be a JSON SearchQuery or a simple name/text value.
        /// </summary>
        private SearchQuery ParseSearchQuery(string target)
        {
            if (string.IsNullOrEmpty(target))
                return null;

            // Try parsing as JSON first
            var query = SearchQuery.FromJson(target);
            if (query != null)
                return query;

            // Fall back to treating it as a name search
            return SearchQuery.Name(target);
        }

        /// <summary>
        /// Resets the replayer state.
        /// </summary>
        public void Reset()
        {
            Status = ReplayStatus.NotStarted;
            CurrentActionIndex = 0;
            TotalActions = 0;
        }
    }

    /// <summary>
    /// Status of a history replay operation.
    /// </summary>
    public enum ReplayStatus
    {
        NotStarted,
        InProgress,
        Completed,
        Diverged,
        Failed,
        Cancelled,
        NoHistory
    }

    /// <summary>
    /// Result of a replay attempt.
    /// </summary>
    public class ReplayResult
    {
        /// <summary>Final status of the replay</summary>
        public ReplayStatus Status { get; set; }

        /// <summary>Action index where divergence/failure occurred (-1 if N/A)</summary>
        public int DivergedAtAction { get; set; } = -1;

        /// <summary>Human-readable message</summary>
        public string Message { get; set; }

        /// <summary>Screen state at divergence point (if applicable)</summary>
        public ScreenState LastScreenState { get; set; }

        /// <summary>Whether replay was successful</summary>
        public bool IsSuccess => Status == ReplayStatus.Completed;

        /// <summary>Whether AI should take over (diverged but can continue)</summary>
        public bool ShouldFallbackToAI => Status == ReplayStatus.Diverged;
    }

    /// <summary>
    /// Configuration for history replayer.
    /// </summary>
    [Serializable]
    public class HistoryReplayerConfig
    {
        /// <summary>Whether to validate the starting screen matches</summary>
        public bool ValidateStartingScreen = true;

        /// <summary>Whether to validate screen after each action</summary>
        public bool ValidateEachStep = false;

        /// <summary>Screen hash similarity threshold</summary>
        public int ScreenSimilarityThreshold = 10;

        /// <summary>Delay between actions in milliseconds</summary>
        public int ActionDelayMs = 100;

        /// <summary>Delay before capturing screen for validation</summary>
        public int ScreenCaptureDelayMs = 50;
    }
}
