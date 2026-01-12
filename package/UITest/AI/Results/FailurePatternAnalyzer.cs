using System;
using System.Collections.Generic;
using System.Linq;

namespace ODDGames.UITest.AI
{
    /// <summary>
    /// Analyzes test runs to identify common failure patterns.
    /// </summary>
    public class FailurePatternAnalyzer
    {
        private readonly AITestResultStore store;

        public FailurePatternAnalyzer(AITestResultStore store = null)
        {
            this.store = store ?? AITestResultStore.Instance;
        }

        /// <summary>
        /// Analyzes all runs to find common failure patterns.
        /// </summary>
        public List<FailurePattern> AnalyzePatterns(int minOccurrences = 2)
        {
            var patterns = new List<FailurePattern>();
            var failedRuns = new List<AITestRun>();

            // Load all failed runs
            var failedSummaries = store.QueryRuns(new ResultQuery
            {
                Status = TestStatus.Failed,
                Limit = 1000
            });

            foreach (var summary in failedSummaries)
            {
                var run = store.LoadRun(summary.id);
                if (run != null)
                {
                    failedRuns.Add(run);
                }
            }

            if (failedRuns.Count < minOccurrences)
            {
                return patterns;
            }

            // Pattern 1: Common last action before failure
            var byLastAction = failedRuns
                .Where(r => r.actions.Count > 0)
                .GroupBy(r => $"{r.actions.Last().actionType}:{r.actions.Last().target}")
                .Where(g => g.Count() >= minOccurrences);

            foreach (var group in byLastAction)
            {
                var parts = group.Key.Split(':');
                patterns.Add(new FailurePattern
                {
                    Type = FailurePatternType.LastAction,
                    ActionType = parts[0],
                    Target = parts.Length > 1 ? parts[1] : "",
                    Occurrences = group.Count(),
                    FailureRate = CalculateFailureRate(group.Key, failedRuns),
                    CommonReasons = ExtractCommonReasons(group),
                    AffectedTests = group.Select(r => r.testName).Distinct().ToList()
                });
            }

            // Pattern 2: Common screen hash at failure point
            var byScreenHash = failedRuns
                .Where(r => r.screenshots.Count > 0)
                .GroupBy(r => r.screenshots.Last().screenHash)
                .Where(g => g.Count() >= minOccurrences && !string.IsNullOrEmpty(g.Key));

            foreach (var group in byScreenHash)
            {
                patterns.Add(new FailurePattern
                {
                    Type = FailurePatternType.ScreenState,
                    ScreenHash = group.Key,
                    Occurrences = group.Count(),
                    Description = $"Tests frequently fail on this screen state",
                    AffectedTests = group.Select(r => r.testName).Distinct().ToList()
                });
            }

            // Pattern 3: Tests that fail after specific action sequences
            var byActionSequence = failedRuns
                .Where(r => r.actions.Count >= 2)
                .GroupBy(r => GetActionSequenceKey(r.actions.TakeLast(3).ToList()))
                .Where(g => g.Count() >= minOccurrences);

            foreach (var group in byActionSequence)
            {
                patterns.Add(new FailurePattern
                {
                    Type = FailurePatternType.ActionSequence,
                    ActionSequence = group.Key,
                    Occurrences = group.Count(),
                    Description = $"Failures after action sequence: {group.Key}",
                    CommonReasons = ExtractCommonReasons(group),
                    AffectedTests = group.Select(r => r.testName).Distinct().ToList()
                });
            }

            // Pattern 4: Time-based failures (tests that consistently timeout)
            var timeoutTests = failedRuns
                .Where(r => r.status == TestStatus.TimedOut || r.status == TestStatus.MaxActionsReached)
                .GroupBy(r => r.testName)
                .Where(g => g.Count() >= minOccurrences);

            foreach (var group in timeoutTests)
            {
                patterns.Add(new FailurePattern
                {
                    Type = FailurePatternType.Timeout,
                    TestName = group.Key,
                    Occurrences = group.Count(),
                    AverageDuration = group.Average(r => r.durationSeconds),
                    Description = $"Test '{group.Key}' frequently times out",
                    AffectedTests = new List<string> { group.Key }
                });
            }

            // Sort by occurrences
            return patterns.OrderByDescending(p => p.Occurrences).ToList();
        }

        /// <summary>
        /// Analyzes patterns for a specific test.
        /// </summary>
        public List<FailurePattern> AnalyzePatternsForTest(string testName, int minOccurrences = 2)
        {
            var patterns = new List<FailurePattern>();

            var failedSummaries = store.QueryRuns(new ResultQuery
            {
                TestName = testName,
                Status = TestStatus.Failed,
                Limit = 100
            });

            var failedRuns = failedSummaries
                .Select(s => store.LoadRun(s.id))
                .Where(r => r != null)
                .ToList();

            if (failedRuns.Count < minOccurrences)
            {
                return patterns;
            }

            // Analyze action that most commonly leads to failure
            var byActionBeforeFailure = failedRuns
                .Where(r => r.actions.Count > 0)
                .SelectMany(r => r.actions.Select((a, i) => new { Action = a, Index = i, Run = r }))
                .Where(x => !x.Action.success)
                .GroupBy(x => $"{x.Action.actionType}:{x.Action.target}")
                .Where(g => g.Count() >= minOccurrences)
                .OrderByDescending(g => g.Count());

            foreach (var group in byActionBeforeFailure.Take(5))
            {
                var parts = group.Key.Split(':');
                patterns.Add(new FailurePattern
                {
                    Type = FailurePatternType.FailedAction,
                    ActionType = parts[0],
                    Target = parts.Length > 1 ? parts[1] : "",
                    Occurrences = group.Count(),
                    Description = $"Action '{group.Key}' frequently fails",
                    CommonReasons = group.Select(x => x.Action.error).Where(e => !string.IsNullOrEmpty(e)).Distinct().ToList()
                });
            }

            return patterns;
        }

        /// <summary>
        /// Gets recommendations based on failure patterns.
        /// </summary>
        public List<string> GetRecommendations(List<FailurePattern> patterns)
        {
            var recommendations = new List<string>();

            foreach (var pattern in patterns.Take(5))
            {
                switch (pattern.Type)
                {
                    case FailurePatternType.LastAction:
                        recommendations.Add($"Review the '{pattern.Target}' element - it's involved in {pattern.Occurrences} failures. Consider adding specific knowledge about how to interact with it.");
                        break;

                    case FailurePatternType.ScreenState:
                        recommendations.Add($"A specific screen state appears in {pattern.Occurrences} failures. Consider adding knowledge about navigation from this state.");
                        break;

                    case FailurePatternType.ActionSequence:
                        recommendations.Add($"The sequence '{pattern.ActionSequence}' leads to failures. Consider alternative action paths.");
                        break;

                    case FailurePatternType.Timeout:
                        recommendations.Add($"Test '{pattern.TestName}' frequently times out (avg {pattern.AverageDuration:F0}s). Consider increasing timeout or simplifying the test goal.");
                        break;

                    case FailurePatternType.FailedAction:
                        recommendations.Add($"Action '{pattern.ActionType}' on '{pattern.Target}' often fails. Verify the element is reliably available and interactable.");
                        break;
                }
            }

            return recommendations;
        }

        private float CalculateFailureRate(string actionKey, List<AITestRun> allRuns)
        {
            var totalWithAction = allRuns.Count(r =>
                r.actions.Any(a => $"{a.actionType}:{a.target}" == actionKey));

            if (totalWithAction == 0)
                return 0;

            var failedWithAction = allRuns.Count(r =>
                r.status != TestStatus.Passed &&
                r.actions.Any(a => $"{a.actionType}:{a.target}" == actionKey));

            return (float)failedWithAction / totalWithAction * 100f;
        }

        private List<string> ExtractCommonReasons(IEnumerable<AITestRun> runs)
        {
            return runs
                .Where(r => !string.IsNullOrEmpty(r.failureReason))
                .Select(r => r.failureReason)
                .GroupBy(r => r)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => g.Key)
                .ToList();
        }

        private string GetActionSequenceKey(List<StoredActionRecord> actions)
        {
            return string.Join(" → ", actions.Select(a => $"{a.actionType}({a.target})"));
        }
    }

    /// <summary>
    /// Type of failure pattern.
    /// </summary>
    public enum FailurePatternType
    {
        LastAction,
        ScreenState,
        ActionSequence,
        Timeout,
        FailedAction
    }

    /// <summary>
    /// A detected failure pattern.
    /// </summary>
    public class FailurePattern
    {
        public FailurePatternType Type { get; set; }
        public string ActionType { get; set; }
        public string Target { get; set; }
        public string ScreenHash { get; set; }
        public string ActionSequence { get; set; }
        public string TestName { get; set; }
        public int Occurrences { get; set; }
        public float FailureRate { get; set; }
        public float AverageDuration { get; set; }
        public string Description { get; set; }
        public List<string> CommonReasons { get; set; } = new List<string>();
        public List<string> AffectedTests { get; set; } = new List<string>();
    }
}
