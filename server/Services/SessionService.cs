using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UIAutomation.Server.Data;
using UIAutomation.Server.Models;

namespace UIAutomation.Server.Services;

/// <summary>
/// Business logic for importing, querying, and managing test sessions.
/// </summary>
public class SessionService
{
    private readonly AppDbContext _db;
    private readonly string _storagePath;

    public SessionService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _storagePath = config["SessionStorage:Path"] ?? "data/sessions";
        Directory.CreateDirectory(_storagePath);
    }

    /// <summary>
    /// Imports a zip file: reads metadata, stores in DB, copies zip to storage.
    /// </summary>
    public async Task<TestSession> ImportAsync(Stream zipStream, string originalFileName)
    {
        var session = new TestSession();
        var zipPath = Path.Combine(_storagePath, $"{session.Id}.zip");

        await using (var fs = File.Create(zipPath))
        {
            await zipStream.CopyToAsync(fs);
        }

        session.ZipPath = zipPath;
        session.ZipSize = new FileInfo(zipPath).Length;

        try
        {
            using var doc = ZipProcessor.ReadSessionJson(zipPath);
            if (doc != null)
            {
                var root = doc.RootElement;

                session.TestName = root.GetStringOrDefault("testName", Path.GetFileNameWithoutExtension(originalFileName)) ?? "";
                session.Scene = root.GetStringOrDefault("scene");
                session.StartTime = root.GetStringOrDefault("startTime");
                session.ScreenWidth = root.GetIntOrDefault("screenWidth");
                session.ScreenHeight = root.GetIntOrDefault("screenHeight");
                session.Duration = root.GetDoubleOrDefault("videoDuration");

                if (root.TryGetProperty("events", out var events))
                    session.EventCount = events.GetArrayLength();
                if (root.TryGetProperty("logs", out var logs))
                    session.LogCount = logs.GetArrayLength();

                session.Result = ZipProcessor.DetermineResult(doc);

                // Extract failure message from logs (logType 10 = ActionFailure)
                if (session.Result == "fail" && root.TryGetProperty("logs", out var logEntries))
                {
                    foreach (var log in logEntries.EnumerateArray())
                    {
                        if (log.TryGetProperty("logType", out var lt) && lt.GetInt32() == 10)
                        {
                            session.FailureMessage = log.GetStringOrDefault("message");
                            break;
                        }
                    }
                    // Fallback: look for Exception or Error log entries
                    if (session.FailureMessage == null)
                    {
                        foreach (var log in logEntries.EnumerateArray())
                        {
                            if (log.TryGetProperty("logType", out var lt2))
                            {
                                var t = lt2.GetInt32();
                                if (t == 4 || t == 0) // Exception or Error
                                {
                                    session.FailureMessage = log.GetStringOrDefault("message");
                                    break;
                                }
                            }
                        }
                    }
                }

                if (root.TryGetProperty("metadata", out var meta))
                {
                    session.Project = meta.GetStringOrDefault("project");
                    session.Branch = meta.GetStringOrDefault("branch");
                    session.Commit = meta.GetStringOrDefault("commit");
                    session.AppVersion = meta.GetStringOrDefault("appVersion");
                    session.Platform = meta.GetStringOrDefault("platform");
                    session.MachineName = meta.GetStringOrDefault("machineName");

                    session.RunId = meta.GetStringOrDefault("runId");

                    if (meta.TryGetProperty("customKeys", out var keys) &&
                        meta.TryGetProperty("customValues", out var values))
                    {
                        var dict = new Dictionary<string, string>();
                        var keysArr = keys.EnumerateArray().ToList();
                        var valsArr = values.EnumerateArray().ToList();
                        for (int i = 0; i < Math.Min(keysArr.Count, valsArr.Count); i++)
                        {
                            var k = keysArr[i].GetString();
                            var v = valsArr[i].GetString();
                            if (k != null) dict[k] = v ?? "";
                        }
                        if (dict.Count > 0)
                            session.CustomMetadata = JsonSerializer.Serialize(dict);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            session.TestName = Path.GetFileNameWithoutExtension(originalFileName);
            Console.WriteLine($"Warning: metadata extraction failed: {ex.Message}");
        }

        session.HasVideo = ZipProcessor.HasVideo(zipPath);

        _db.TestSessions.Add(session);
        await _db.SaveChangesAsync();

        // Backfill run metadata from first session
        if (!string.IsNullOrEmpty(session.RunId))
            await BackfillRunMetadataAsync(session.RunId, session);

        return session;
    }

    /// <summary>
    /// Updates an existing session (e.g., to set ProjectId after import).
    /// </summary>
    public async Task UpdateAsync(TestSession session)
    {
        _db.TestSessions.Update(session);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Applies project scoping to a query. null = no filter (admin), empty = no access.
    /// </summary>
    private static IQueryable<TestSession> ApplyProjectScope(IQueryable<TestSession> query, List<string>? projectIds)
    {
        if (projectIds == null) return query; // admin — no filter
        return query.Where(s => s.ProjectId != null && projectIds.Contains(s.ProjectId));
    }

    /// <summary>
    /// Queries sessions with filtering, sorting, pagination, and project scoping.
    /// </summary>
    public async Task<(List<TestSession> Items, int Total)> QueryAsync(
        string? testName = null, string? result = null, string? project = null,
        string? branch = null, DateTime? from = null, DateTime? to = null,
        int page = 1, int pageSize = 50, string sort = "createdAt", string order = "desc",
        List<string>? projectIds = null, string? appVersion = null)
    {
        var query = ApplyProjectScope(_db.TestSessions.AsQueryable(), projectIds);

        if (!string.IsNullOrEmpty(testName))
            query = query.Where(s => s.TestName.Contains(testName));
        if (!string.IsNullOrEmpty(result))
            query = query.Where(s => s.Result == result);
        if (!string.IsNullOrEmpty(project))
            query = query.Where(s => s.Project == project);
        if (!string.IsNullOrEmpty(branch))
            query = query.Where(s => s.Branch == branch);
        if (!string.IsNullOrEmpty(appVersion))
            query = query.Where(s => s.AppVersion == appVersion);
        if (from.HasValue)
            query = query.Where(s => s.CreatedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(s => s.CreatedAt <= to.Value);

        var total = await query.CountAsync();

        query = (sort.ToLower(), order.ToLower()) switch
        {
            ("testname", "asc") => query.OrderBy(s => s.TestName),
            ("testname", _) => query.OrderByDescending(s => s.TestName),
            ("result", "asc") => query.OrderBy(s => s.Result),
            ("result", _) => query.OrderByDescending(s => s.Result),
            ("starttime", "asc") => query.OrderBy(s => s.StartTime),
            ("starttime", _) => query.OrderByDescending(s => s.StartTime),
            ("duration", "asc") => query.OrderBy(s => s.Duration),
            ("duration", _) => query.OrderByDescending(s => s.Duration),
            ("project", "asc") => query.OrderBy(s => s.Project),
            ("project", _) => query.OrderByDescending(s => s.Project),
            ("createdat", "asc") => query.OrderBy(s => s.CreatedAt),
            _ => query.OrderByDescending(s => s.CreatedAt),
        };

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<TestSession?> GetAsync(string id)
    {
        return await _db.TestSessions.FindAsync(id);
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var session = await _db.TestSessions.FindAsync(id);
        if (session == null) return false;

        var runId = session.RunId;

        if (File.Exists(session.ZipPath))
            File.Delete(session.ZipPath);

        _db.TestSessions.Remove(session);
        await _db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(runId))
            await CleanupOrphanedRunAsync(runId);

        return true;
    }

    public async Task<int> BulkDeleteAsync(DateTime? olderThan = null, string? project = null,
        string? result = null, List<string>? projectIds = null)
    {
        var query = ApplyProjectScope(_db.TestSessions.AsQueryable(), projectIds);

        if (olderThan.HasValue)
            query = query.Where(s => s.CreatedAt < olderThan.Value);
        if (!string.IsNullOrEmpty(project))
            query = query.Where(s => s.Project == project);
        if (!string.IsNullOrEmpty(result))
            query = query.Where(s => s.Result == result);

        var sessions = await query.ToListAsync();
        foreach (var session in sessions)
        {
            if (File.Exists(session.ZipPath))
                File.Delete(session.ZipPath);
        }

        var affectedRunIds = sessions
            .Where(s => !string.IsNullOrEmpty(s.RunId))
            .Select(s => s.RunId!)
            .Distinct()
            .ToList();

        _db.TestSessions.RemoveRange(sessions);
        await _db.SaveChangesAsync();

        await CleanupOrphanedRunsAsync(affectedRunIds);

        return sessions.Count;
    }

    public async Task<object> GetStatsAsync(List<string>? projectIds = null)
    {
        var query = ApplyProjectScope(_db.TestSessions.AsQueryable(), projectIds);

        var total = await query.CountAsync();
        var passed = await query.CountAsync(s => s.Result == "pass");
        var failed = await query.CountAsync(s => s.Result == "fail");
        var warned = await query.CountAsync(s => s.Result == "warn");

        var byProject = await query
            .GroupBy(s => s.Project)
            .Select(g => new { Project = g.Key, Count = g.Count(), Failures = g.Count(s => s.Result == "fail") })
            .ToListAsync();

        return new { total, passed, failed, warned, byProject };
    }

    /// <summary>
    /// Gets distinct metadata project names from sessions (not auth Projects).
    /// </summary>
    public async Task<List<string>> GetSessionProjectsAsync(List<string>? projectIds = null)
    {
        var query = ApplyProjectScope(_db.TestSessions.AsQueryable(), projectIds);

        return await query
            .Where(s => s.Project != null)
            .Select(s => s.Project!)
            .Distinct()
            .OrderBy(p => p)
            .ToListAsync();
    }

    public async Task<List<string>> GetVersionsAsync(string? project = null, List<string>? projectIds = null)
    {
        var query = ApplyProjectScope(_db.TestSessions.Where(s => s.AppVersion != null), projectIds);

        if (!string.IsNullOrEmpty(project))
            query = query.Where(s => s.Project == project);

        return await query
            .Select(s => s.AppVersion!)
            .Distinct()
            .OrderByDescending(v => v)
            .ToListAsync();
    }

    public async Task<List<string>> GetBranchesAsync(string? project, List<string>? projectIds = null)
    {
        var query = ApplyProjectScope(_db.TestSessions.Where(s => s.Branch != null), projectIds);

        if (!string.IsNullOrEmpty(project))
            query = query.Where(s => s.Project == project);

        return await query
            .Select(s => s.Branch!)
            .Distinct()
            .OrderBy(b => b)
            .ToListAsync();
    }

    // ==================== Run Methods ====================

    public async Task<TestRun> StartRunAsync(string? projectId)
    {
        var run = new TestRun { ProjectId = projectId };
        _db.TestRuns.Add(run);
        await _db.SaveChangesAsync();
        return run;
    }

    public async Task<bool> FinishRunAsync(string runId)
    {
        var run = await _db.TestRuns.FindAsync(runId);
        if (run == null) return false;
        run.IsComplete = true;
        run.FinishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<TestRun?> GetRunAsync(string runId)
    {
        return await _db.TestRuns.FindAsync(runId);
    }

    public async Task<(List<object> Items, int Total)> QueryRunsAsync(
        string? project = null, string? branch = null, string? appVersion = null,
        string? result = null, int page = 1, int pageSize = 50,
        List<string>? projectIds = null)
    {
        var query = _db.TestRuns.AsQueryable();

        if (projectIds != null)
            query = query.Where(r => r.ProjectId != null && projectIds.Contains(r.ProjectId));
        if (!string.IsNullOrEmpty(project))
            query = query.Where(r => r.Project == project);
        if (!string.IsNullOrEmpty(branch))
            query = query.Where(r => r.Branch == branch);
        if (!string.IsNullOrEmpty(appVersion))
            query = query.Where(r => r.AppVersion == appVersion);

        var total = await query.CountAsync();

        var runs = await query
            .OrderByDescending(r => r.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var runIds = runs.Select(r => r.Id).ToList();
        var sessionStats = await _db.TestSessions
            .Where(s => s.RunId != null && runIds.Contains(s.RunId))
            .GroupBy(s => s.RunId!)
            .Select(g => new
            {
                RunId = g.Key,
                TotalTests = g.Count(),
                Passed = g.Count(s => s.Result == "pass"),
                Failed = g.Count(s => s.Result == "fail"),
                Warned = g.Count(s => s.Result == "warn"),
                TotalDuration = g.Sum(s => s.Duration)
            })
            .ToListAsync();

        var statsMap = sessionStats.ToDictionary(s => s.RunId);

        var items = runs.Select(r =>
        {
            var stats = statsMap.GetValueOrDefault(r.Id);
            var overallResult = !r.IsComplete ? "running" : stats?.Failed > 0 ? "fail" : stats?.Warned > 0 ? "warn" : "pass";
            return (object)new
            {
                runId = r.Id,
                project = r.Project,
                branch = r.Branch,
                commit = r.Commit,
                appVersion = r.AppVersion,
                platform = r.Platform,
                machineName = r.MachineName,
                startedAt = r.StartedAt,
                finishedAt = r.FinishedAt,
                isComplete = r.IsComplete,
                totalTests = stats?.TotalTests ?? 0,
                passed = stats?.Passed ?? 0,
                failed = stats?.Failed ?? 0,
                warned = stats?.Warned ?? 0,
                totalDuration = stats?.TotalDuration ?? 0,
                result = overallResult
            };
        }).ToList();

        if (!string.IsNullOrEmpty(result))
        {
            items = items.Where(i =>
            {
                var prop = i.GetType().GetProperty("result");
                return prop?.GetValue(i)?.ToString() == result;
            }).ToList();
        }

        return (items, total);
    }

    public async Task<List<TestSession>> GetRunSessionsAsync(string runId, List<string>? projectIds = null)
    {
        var query = ApplyProjectScope(_db.TestSessions.AsQueryable(), projectIds);
        return await query
            .Where(s => s.RunId == runId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> DeleteRunAsync(string runId)
    {
        var sessions = await _db.TestSessions
            .Where(s => s.RunId == runId)
            .ToListAsync();

        foreach (var session in sessions)
        {
            if (File.Exists(session.ZipPath))
                File.Delete(session.ZipPath);
        }

        _db.TestSessions.RemoveRange(sessions);

        var run = await _db.TestRuns.FindAsync(runId);
        if (run != null)
            _db.TestRuns.Remove(run);

        await _db.SaveChangesAsync();
        return run != null;
    }

    private async Task CleanupOrphanedRunAsync(string runId)
    {
        var hasRemaining = await _db.TestSessions.AnyAsync(s => s.RunId == runId);
        if (!hasRemaining)
        {
            var run = await _db.TestRuns.FindAsync(runId);
            if (run != null)
            {
                _db.TestRuns.Remove(run);
                await _db.SaveChangesAsync();
            }
        }
    }

    private async Task CleanupOrphanedRunsAsync(List<string> runIds)
    {
        if (runIds.Count == 0) return;

        var runIdsWithSessions = await _db.TestSessions
            .Where(s => s.RunId != null && runIds.Contains(s.RunId))
            .Select(s => s.RunId!)
            .Distinct()
            .ToListAsync();

        var orphanedRunIds = runIds.Except(runIdsWithSessions).ToList();
        if (orphanedRunIds.Count == 0) return;

        var orphanedRuns = await _db.TestRuns
            .Where(r => orphanedRunIds.Contains(r.Id))
            .ToListAsync();

        _db.TestRuns.RemoveRange(orphanedRuns);
        await _db.SaveChangesAsync();
    }

    private async Task BackfillRunMetadataAsync(string runId, TestSession session)
    {
        var run = await _db.TestRuns.FindAsync(runId);
        if (run == null) return;

        if (run.Project == null && session.Project != null)
        {
            run.Project = session.Project;
            run.Branch = session.Branch;
            run.Commit = session.Commit;
            run.AppVersion = session.AppVersion;
            run.Platform = session.Platform;
            run.MachineName = session.MachineName;
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Marks incomplete runs older than <paramref name="timeout"/> as timed out.
    /// Returns the number of runs that were timed out.
    /// </summary>
    public async Task<int> TimeoutStaleRunsAsync(TimeSpan timeout)
    {
        var cutoff = DateTime.UtcNow - timeout;
        var staleRuns = await _db.TestRuns
            .Where(r => !r.IsComplete && r.StartedAt < cutoff)
            .ToListAsync();

        if (staleRuns.Count == 0) return 0;

        foreach (var run in staleRuns)
        {
            run.IsComplete = true;
            run.FinishedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return staleRuns.Count;
    }

    public async Task<List<object>> GetTestsAsync(string? project, string? branch, List<string>? projectIds = null)
    {
        var query = ApplyProjectScope(_db.TestSessions.AsQueryable(), projectIds);

        if (!string.IsNullOrEmpty(project))
            query = query.Where(s => s.Project == project);
        if (!string.IsNullOrEmpty(branch))
            query = query.Where(s => s.Branch == branch);

        return await query
            .GroupBy(s => s.TestName)
            .Select(g => (object)new
            {
                TestName = g.Key,
                LatestResult = g.OrderByDescending(s => s.CreatedAt).First().Result,
                RunCount = g.Count(),
                LastRun = g.Max(s => s.CreatedAt)
            })
            .ToListAsync();
    }
}

internal static class JsonElementExtensions
{
    public static string? GetStringOrDefault(this JsonElement element, string propertyName, string? defaultValue = null)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() : defaultValue;
    }

    public static int GetIntOrDefault(this JsonElement element, string propertyName, int defaultValue = 0)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32() : defaultValue;
    }

    public static double GetDoubleOrDefault(this JsonElement element, string propertyName, double defaultValue = 0)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetDouble() : defaultValue;
    }
}
