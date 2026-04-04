using Microsoft.EntityFrameworkCore;
using UIAutomation.Server.Data;
using UIAutomation.Server.Models;

namespace UIAutomation.Server.Services;

/// <summary>
/// Business logic for Gemini AI test runs, results, steps, and error tracking.
/// </summary>
public class GeminiService
{
    private readonly AppDbContext _db;

    public GeminiService(AppDbContext db)
    {
        _db = db;
    }

    #region Runs

    public async Task<GeminiRun> StartRunAsync(GeminiRun run)
    {
        _db.GeminiRuns.Add(run);
        await _db.SaveChangesAsync();
        return run;
    }

    public async Task<GeminiRun?> FinishRunAsync(string runId)
    {
        var run = await _db.GeminiRuns.FindAsync(runId);
        if (run == null) return null;

        // Aggregate from results
        var results = await _db.GeminiResults.Where(r => r.RunId == runId).ToListAsync();
        run.TotalTests = results.Count;
        run.PassedTests = results.Count(r => r.Result == "pass");
        run.FailedTests = results.Count(r => r.Result != "pass");
        run.FinishedAt = DateTime.UtcNow;
        run.IsComplete = true;

        await _db.SaveChangesAsync();
        return run;
    }

    public async Task<GeminiRun?> GetRunAsync(string runId)
    {
        return await _db.GeminiRuns.FindAsync(runId);
    }

    public async Task<object> QueryRunsAsync(
        List<string>? projectIds,
        string? appVersion = null,
        string? branch = null,
        int page = 1,
        int pageSize = 20)
    {
        var query = _db.GeminiRuns.AsQueryable();
        if (projectIds != null)
            query = query.Where(r => projectIds.Contains(r.ProjectId));
        if (!string.IsNullOrEmpty(appVersion))
            query = query.Where(r => r.AppVersion == appVersion);
        if (!string.IsNullOrEmpty(branch))
            query = query.Where(r => r.Branch == branch);

        var total = await query.CountAsync();
        var runs = await query
            .OrderByDescending(r => r.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                r.Id, r.ProjectId, r.Branch, r.Commit, r.AppVersion,
                r.Platform, r.MachineName, r.GeminiModel,
                r.StartedAt, r.FinishedAt, r.IsComplete,
                r.TotalTests, r.PassedTests, r.FailedTests
            })
            .ToListAsync();

        return new { total, page, pageSize, runs };
    }

    public async Task<bool> DeleteRunAsync(string runId)
    {
        var run = await _db.GeminiRuns.FindAsync(runId);
        if (run == null) return false;

        // Cascade: delete errors, steps, results, then run
        var resultIds = await _db.GeminiResults
            .Where(r => r.RunId == runId)
            .Select(r => r.Id)
            .ToListAsync();

        if (resultIds.Count > 0)
        {
            _db.GeminiErrors.RemoveRange(
                _db.GeminiErrors.Where(e => resultIds.Contains(e.ResultId)));
            _db.GeminiSteps.RemoveRange(
                _db.GeminiSteps.Where(s => resultIds.Contains(s.ResultId)));
            _db.GeminiResults.RemoveRange(
                _db.GeminiResults.Where(r => r.RunId == runId));
        }

        _db.GeminiRuns.Remove(run);
        await _db.SaveChangesAsync();
        return true;
    }

    #endregion

    #region Results

    public async Task<GeminiResult> AddResultAsync(GeminiResult result, List<GeminiStep> steps, List<GeminiError> errors)
    {
        _db.GeminiResults.Add(result);

        if (steps.Count > 0)
            _db.GeminiSteps.AddRange(steps);

        if (errors.Count > 0)
            _db.GeminiErrors.AddRange(errors);

        await _db.SaveChangesAsync();
        return result;
    }

    public async Task<object?> GetResultWithStepsAsync(string resultId)
    {
        var result = await _db.GeminiResults.FindAsync(resultId);
        if (result == null) return null;

        var steps = await _db.GeminiSteps
            .Where(s => s.ResultId == resultId)
            .OrderBy(s => s.StepIndex)
            .Select(s => new
            {
                s.Id, s.StepIndex, s.ActionJson, s.ModelResponse,
                s.Success, s.Error, s.ElapsedMs, s.Timestamp
            })
            .ToListAsync();

        var errors = await _db.GeminiErrors
            .Where(e => e.ResultId == resultId)
            .OrderBy(e => e.Timestamp)
            .Select(e => new
            {
                e.Id, e.Category, e.Message, e.StackTrace,
                e.ActionJson, e.StepIndex, e.Timestamp
            })
            .ToListAsync();

        return new
        {
            result.Id, result.RunId, result.TestDefinitionId, result.TestName,
            result.Prompt, result.Scene, result.Result, result.FailReason,
            result.StepsExecuted, result.MaxSteps, result.DurationSeconds,
            result.AppVersion, result.GeminiModel,
            result.ErrorCount, result.ExceptionCount,
            result.StartedAt, result.FinishedAt,
            steps, errors
        };
    }

    public async Task<List<object>> GetRunResultsAsync(string runId)
    {
        return await _db.GeminiResults
            .Where(r => r.RunId == runId)
            .OrderBy(r => r.StartedAt)
            .Select(r => (object)new
            {
                r.Id, r.TestName, r.Prompt, r.Scene, r.Result, r.FailReason,
                r.StepsExecuted, r.MaxSteps, r.DurationSeconds,
                r.AppVersion, r.GeminiModel,
                r.ErrorCount, r.ExceptionCount,
                r.StartedAt, r.FinishedAt
            })
            .ToListAsync();
    }

    /// <summary>
    /// Query results across all runs with flexible filtering.
    /// </summary>
    public async Task<object> QueryResultsAsync(
        List<string>? projectIds,
        string? testName = null,
        string? result = null,
        string? appVersion = null,
        string? branch = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 20)
    {
        var query = from r in _db.GeminiResults
                    join run in _db.GeminiRuns on r.RunId equals run.Id
                    select new { Result = r, Run = run };

        if (projectIds != null)
            query = query.Where(x => projectIds.Contains(x.Run.ProjectId));
        if (!string.IsNullOrEmpty(testName))
            query = query.Where(x => x.Result.TestName.Contains(testName));
        if (!string.IsNullOrEmpty(result))
            query = query.Where(x => x.Result.Result == result);
        if (!string.IsNullOrEmpty(appVersion))
            query = query.Where(x => x.Result.AppVersion == appVersion);
        if (!string.IsNullOrEmpty(branch))
            query = query.Where(x => x.Run.Branch == branch);
        if (from.HasValue)
            query = query.Where(x => x.Result.StartedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(x => x.Result.StartedAt <= to.Value);

        var total = await query.CountAsync();
        var results = await query
            .OrderByDescending(x => x.Result.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Result.Id, x.Result.RunId, x.Result.TestName, x.Result.Scene,
                x.Result.Result, x.Result.FailReason,
                x.Result.StepsExecuted, x.Result.DurationSeconds,
                x.Result.AppVersion, x.Result.GeminiModel,
                x.Result.ErrorCount, x.Result.ExceptionCount,
                x.Result.StartedAt, x.Result.FinishedAt,
                x.Run.Branch, x.Run.Platform, x.Run.MachineName
            })
            .ToListAsync();

        return new { total, page, pageSize, results };
    }

    #endregion

    #region Errors — Global Tracking

    /// <summary>
    /// Query errors globally across all tests, projects, and versions.
    /// </summary>
    public async Task<object> QueryErrorsAsync(
        List<string>? projectIds,
        string? testName = null,
        string? category = null,
        string? appVersion = null,
        string? search = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50)
    {
        var query = _db.GeminiErrors.AsQueryable();

        if (projectIds != null)
            query = query.Where(e => projectIds.Contains(e.ProjectId));
        if (!string.IsNullOrEmpty(testName))
            query = query.Where(e => e.TestName != null && e.TestName.Contains(testName));
        if (!string.IsNullOrEmpty(category))
            query = query.Where(e => e.Category == category);
        if (!string.IsNullOrEmpty(appVersion))
            query = query.Where(e => e.AppVersion == appVersion);
        if (!string.IsNullOrEmpty(search))
            query = query.Where(e => e.Message.Contains(search));
        if (from.HasValue)
            query = query.Where(e => e.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(e => e.Timestamp <= to.Value);

        var total = await query.CountAsync();
        var errors = await query
            .OrderByDescending(e => e.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id, e.ResultId, e.StepId, e.ProjectId, e.TestName, e.Scene,
                e.AppVersion, e.Branch, e.Platform, e.Category,
                e.Message, e.StackTrace, e.ActionJson, e.StepIndex, e.Timestamp
            })
            .ToListAsync();

        return new { total, page, pageSize, errors };
    }

    /// <summary>
    /// Error frequency grouped by message — shows which errors occur most often.
    /// </summary>
    public async Task<List<object>> GetErrorFrequencyAsync(
        List<string>? projectIds,
        string? appVersion = null,
        int top = 20)
    {
        var query = _db.GeminiErrors.AsQueryable();
        if (projectIds != null)
            query = query.Where(e => projectIds.Contains(e.ProjectId));
        if (!string.IsNullOrEmpty(appVersion))
            query = query.Where(e => e.AppVersion == appVersion);

        var grouped = await query
            .GroupBy(e => e.Message)
            .Select(g => new
            {
                Message = g.Key,
                Count = g.Count(),
                Category = g.First().Category,
                LatestOccurrence = g.Max(e => e.Timestamp),
                AffectedTests = g.Select(e => e.TestName).Distinct().Count(),
                AffectedVersions = g.Select(e => e.AppVersion).Distinct().Count()
            })
            .OrderByDescending(x => x.Count)
            .Take(top)
            .ToListAsync();

        return grouped.Select(x => (object)x).ToList();
    }

    #endregion

    #region Stats

    /// <summary>
    /// Aggregated stats across all Gemini tests — scoped to projects.
    /// </summary>
    public async Task<object> GetStatsAsync(List<string>? projectIds)
    {
        var runQuery = _db.GeminiRuns.AsQueryable();
        var resultQuery = _db.GeminiResults.AsQueryable();
        var errorQuery = _db.GeminiErrors.AsQueryable();

        if (projectIds != null)
        {
            runQuery = runQuery.Where(r => projectIds.Contains(r.ProjectId));
            errorQuery = errorQuery.Where(e => projectIds.Contains(e.ProjectId));
            // Results are scoped through runs
            var runIds = _db.GeminiRuns
                .Where(r => projectIds.Contains(r.ProjectId))
                .Select(r => r.Id);
            resultQuery = resultQuery.Where(r => runIds.Contains(r.RunId));
        }

        var totalRuns = await runQuery.CountAsync();
        var totalResults = await resultQuery.CountAsync();
        var passedResults = await resultQuery.CountAsync(r => r.Result == "pass");
        var failedResults = await resultQuery.CountAsync(r => r.Result != "pass");
        var totalErrors = await errorQuery.CountAsync();

        // Pass rate by version
        var byVersion = await resultQuery
            .Where(r => r.AppVersion != null)
            .GroupBy(r => r.AppVersion)
            .Select(g => new
            {
                Version = g.Key,
                Total = g.Count(),
                Passed = g.Count(r => r.Result == "pass"),
                Failed = g.Count(r => r.Result != "pass"),
                AvgDuration = g.Average(r => r.DurationSeconds),
                AvgSteps = g.Average(r => (double)r.StepsExecuted)
            })
            .OrderByDescending(v => v.Version)
            .Take(10)
            .ToListAsync();

        // Per-test stats (flakiness, pass rate)
        var byTest = await resultQuery
            .GroupBy(r => r.TestName)
            .Select(g => new
            {
                TestName = g.Key,
                Total = g.Count(),
                Passed = g.Count(r => r.Result == "pass"),
                Failed = g.Count(r => r.Result != "pass"),
                AvgDuration = g.Average(r => r.DurationSeconds),
                AvgSteps = g.Average(r => (double)r.StepsExecuted),
                ErrorCount = g.Sum(r => r.ErrorCount),
                LastRun = g.Max(r => r.StartedAt),
                LatestResult = g.OrderByDescending(r => r.StartedAt).First().Result
            })
            .OrderBy(t => t.TestName)
            .ToListAsync();

        // Error breakdown by category
        var errorsByCategory = await errorQuery
            .GroupBy(e => e.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToListAsync();

        return new
        {
            totalRuns, totalResults, passedResults, failedResults, totalErrors,
            passRate = totalResults > 0 ? (double)passedResults / totalResults * 100 : 0,
            byVersion, byTest, errorsByCategory
        };
    }

    /// <summary>
    /// Test health over time — pass rate bucketed by day for a specific test or all tests.
    /// </summary>
    public async Task<List<object>> GetHealthTimelineAsync(
        List<string>? projectIds,
        string? testName = null,
        int days = 30)
    {
        var since = DateTime.UtcNow.AddDays(-days);

        var query = _db.GeminiResults.AsQueryable();
        if (projectIds != null)
        {
            var runIds = _db.GeminiRuns
                .Where(r => projectIds.Contains(r.ProjectId))
                .Select(r => r.Id);
            query = query.Where(r => runIds.Contains(r.RunId));
        }
        if (!string.IsNullOrEmpty(testName))
            query = query.Where(r => r.TestName == testName);

        query = query.Where(r => r.StartedAt >= since);

        var grouped = await query
            .GroupBy(r => r.StartedAt.Date)
            .Select(g => new
            {
                Date = g.Key,
                Total = g.Count(),
                Passed = g.Count(r => r.Result == "pass"),
                Failed = g.Count(r => r.Result != "pass"),
                AvgDuration = g.Average(r => r.DurationSeconds)
            })
            .OrderBy(x => x.Date)
            .ToListAsync();

        return grouped.Select(x => (object)x).ToList();
    }

    /// <summary>
    /// Distinct app versions from Gemini results.
    /// </summary>
    public async Task<List<string>> GetVersionsAsync(List<string>? projectIds)
    {
        var query = _db.GeminiResults.AsQueryable();
        if (projectIds != null)
        {
            var runIds = _db.GeminiRuns
                .Where(r => projectIds.Contains(r.ProjectId))
                .Select(r => r.Id);
            query = query.Where(r => runIds.Contains(r.RunId));
        }

        return await query
            .Where(r => r.AppVersion != null)
            .Select(r => r.AppVersion!)
            .Distinct()
            .OrderByDescending(v => v)
            .ToListAsync();
    }

    #endregion
}
