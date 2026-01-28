using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ODDGames.UIAutomation.AI
{
    /// <summary>
    /// Persistent storage for AI test results.
    /// Stores runs as JSON in the Library folder.
    /// </summary>
    public class AITestResultStore
    {
        private const string StorePath = "Library/AITestResults";
        private const string IndexFile = "index.json";
        private const string ScreenshotsFolder = "Screenshots";

        private static AITestResultStore instance;

        /// <summary>
        /// Gets the singleton instance of the result store.
        /// </summary>
        public static AITestResultStore Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new AITestResultStore();
                    instance.Load();
                }
                return instance;
            }
        }

        private ResultIndex index;
        private string basePath;

        private AITestResultStore()
        {
            basePath = Path.Combine(Application.dataPath, "..", StorePath);
        }

        /// <summary>
        /// Saves a test run to storage.
        /// </summary>
        public void SaveRun(AITestRun run, AITestResult result)
        {
            EnsureDirectoriesExist();

            // Save screenshots to disk
            if (result.Screenshots != null)
            {
                var screenshotsPath = Path.Combine(basePath, ScreenshotsFolder, run.id);
                Directory.CreateDirectory(screenshotsPath);

                for (int i = 0; i < result.Screenshots.Count && i < run.screenshots.Count; i++)
                {
                    var screenshot = result.Screenshots[i];
                    var storedRecord = run.screenshots[i];

                    if (screenshot.ScreenshotPng != null && screenshot.ScreenshotPng.Length > 0)
                    {
                        var fileName = $"{storedRecord.id}.png";
                        var filePath = Path.Combine(screenshotsPath, fileName);
                        File.WriteAllBytes(filePath, screenshot.ScreenshotPng);
                        storedRecord.filePath = Path.Combine(ScreenshotsFolder, run.id, fileName);
                    }
                }
            }

            // Save run data as JSON
            var runPath = Path.Combine(basePath, $"{run.id}.json");
            var json = JsonUtility.ToJson(run);
            File.WriteAllText(runPath, json);

            // Update index
            index.runs.Add(new RunSummary
            {
                id = run.id,
                testName = run.testName,
                groupName = run.groupName,
                status = run.status,
                timestampTicks = run.endTimeTicks,
                duration = run.durationSeconds
            });

            SaveIndex();
        }

        /// <summary>
        /// Loads a full run by ID.
        /// </summary>
        public AITestRun LoadRun(string id)
        {
            var path = Path.Combine(basePath, $"{id}.json");
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            return JsonUtility.FromJson<AITestRun>(json);
        }

        /// <summary>
        /// Loads a screenshot by path.
        /// </summary>
        public byte[] LoadScreenshot(string relativePath)
        {
            var fullPath = Path.Combine(basePath, relativePath);
            if (!File.Exists(fullPath))
                return null;

            return File.ReadAllBytes(fullPath);
        }

        /// <summary>
        /// Queries runs with optional filtering.
        /// </summary>
        public IEnumerable<RunSummary> QueryRuns(ResultQuery query = null)
        {
            query = query ?? new ResultQuery();

            var results = index.runs.AsEnumerable();

            if (query.Status.HasValue)
            {
                results = results.Where(r => r.status == query.Status.Value);
            }

            if (!string.IsNullOrEmpty(query.TestName))
            {
                results = results.Where(r =>
                    r.testName != null &&
                    r.testName.IndexOf(query.TestName, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (!string.IsNullOrEmpty(query.GroupName))
            {
                results = results.Where(r => r.groupName == query.GroupName);
            }

            if (query.Since.HasValue)
            {
                var sinceTicks = query.Since.Value.Ticks;
                results = results.Where(r => r.timestampTicks >= sinceTicks);
            }

            results = query.SortBy switch
            {
                SortBy.Newest => results.OrderByDescending(r => r.timestampTicks),
                SortBy.Oldest => results.OrderBy(r => r.timestampTicks),
                SortBy.Duration => results.OrderByDescending(r => r.duration),
                SortBy.Name => results.OrderBy(r => r.testName),
                _ => results
            };

            if (query.Limit > 0)
            {
                results = results.Take(query.Limit);
            }

            return results;
        }

        /// <summary>
        /// Gets statistics for runs, optionally filtered by group.
        /// </summary>
        public ResultStatistics GetStatistics(string groupName = null)
        {
            var runs = groupName != null
                ? index.runs.Where(r => r.groupName == groupName)
                : index.runs;

            var runsList = runs.ToList();

            if (runsList.Count == 0)
            {
                return new ResultStatistics();
            }

            return new ResultStatistics
            {
                TotalRuns = runsList.Count,
                Passed = runsList.Count(r => r.status == TestStatus.Passed),
                Failed = runsList.Count(r => r.status == TestStatus.Failed),
                Errors = runsList.Count(r => r.status == TestStatus.Error),
                TimedOut = runsList.Count(r => r.status == TestStatus.TimedOut),
                AverageDuration = runsList.Average(r => r.duration),
                TotalDuration = runsList.Sum(r => r.duration),
                LastRunTime = new DateTime(runsList.Max(r => r.timestampTicks), DateTimeKind.Utc),
                PassRate = runsList.Count > 0
                    ? (float)runsList.Count(r => r.status == TestStatus.Passed) / runsList.Count * 100f
                    : 0f
            };
        }

        /// <summary>
        /// Gets runs for a specific test.
        /// </summary>
        public IEnumerable<RunSummary> GetRunsForTest(string testName, int limit = 10)
        {
            return QueryRuns(new ResultQuery
            {
                TestName = testName,
                SortBy = SortBy.Newest,
                Limit = limit
            });
        }

        /// <summary>
        /// Gets the most recent failures.
        /// </summary>
        public IEnumerable<RunSummary> GetRecentFailures(int limit = 20)
        {
            return QueryRuns(new ResultQuery
            {
                Status = TestStatus.Failed,
                SortBy = SortBy.Newest,
                Limit = limit
            });
        }

        /// <summary>
        /// Deletes a run and its associated screenshots.
        /// </summary>
        public void DeleteRun(string id)
        {
            // Remove from index
            index.runs.RemoveAll(r => r.id == id);
            SaveIndex();

            // Delete run file
            var runPath = Path.Combine(basePath, $"{id}.json");
            if (File.Exists(runPath))
            {
                File.Delete(runPath);
            }

            // Delete screenshots folder
            var screenshotsPath = Path.Combine(basePath, ScreenshotsFolder, id);
            if (Directory.Exists(screenshotsPath))
            {
                Directory.Delete(screenshotsPath);
            }
        }

        /// <summary>
        /// Clears all stored results.
        /// </summary>
        public void ClearAll()
        {
            if (Directory.Exists(basePath))
            {
                Directory.Delete(basePath);
            }

            index = new ResultIndex();
            EnsureDirectoriesExist();
        }

        /// <summary>
        /// Gets the total storage size in bytes.
        /// </summary>
        public long GetStorageSize()
        {
            if (!Directory.Exists(basePath))
                return 0;

            return Directory.GetFiles(basePath, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
        }

        private void Load()
        {
            EnsureDirectoriesExist();

            var indexPath = Path.Combine(basePath, IndexFile);
            if (File.Exists(indexPath))
            {
                try
                {
                    var json = File.ReadAllText(indexPath);
                    index = JsonUtility.FromJson<ResultIndex>(json);
                }
                catch
                {
                    index = new ResultIndex();
                }
            }
            else
            {
                index = new ResultIndex();
            }
        }

        private void SaveIndex()
        {
            EnsureDirectoriesExist();

            var indexPath = Path.Combine(basePath, IndexFile);
            var json = JsonUtility.ToJson(index);
            File.WriteAllText(indexPath, json);
        }

        private void EnsureDirectoriesExist()
        {
            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }

            var screenshotsPath = Path.Combine(basePath, ScreenshotsFolder);
            if (!Directory.Exists(screenshotsPath))
            {
                Directory.CreateDirectory(screenshotsPath);
            }
        }
    }

    /// <summary>
    /// Index of all stored runs.
    /// </summary>
    [Serializable]
    public class ResultIndex
    {
        public List<RunSummary> runs = new List<RunSummary>();
    }

    /// <summary>
    /// Query parameters for filtering results.
    /// </summary>
    public class ResultQuery
    {
        public TestStatus? Status;
        public string TestName;
        public string GroupName;
        public DateTime? Since;
        public SortBy SortBy = SortBy.Newest;
        public int Limit = 100;
    }

    /// <summary>
    /// Sort order for results.
    /// </summary>
    public enum SortBy
    {
        Newest,
        Oldest,
        Duration,
        Name
    }

    /// <summary>
    /// Aggregated statistics for runs.
    /// </summary>
    public class ResultStatistics
    {
        public int TotalRuns;
        public int Passed;
        public int Failed;
        public int Errors;
        public int TimedOut;
        public float AverageDuration;
        public float TotalDuration;
        public DateTime LastRunTime;
        public float PassRate;
    }
}
