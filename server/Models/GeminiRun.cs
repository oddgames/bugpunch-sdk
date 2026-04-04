namespace UIAutomation.Server.Models;

/// <summary>
/// A batch execution of Gemini AI tests — groups multiple GeminiResults
/// from a single test run (editor session or device batch).
/// </summary>
public class GeminiRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = "";
    public string? Branch { get; set; }
    public string? Commit { get; set; }
    public string? AppVersion { get; set; }
    public string? Platform { get; set; }
    public string? MachineName { get; set; }
    public string? GeminiModel { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public bool IsComplete { get; set; }
    public int TotalTests { get; set; }
    public int PassedTests { get; set; }
    public int FailedTests { get; set; }

    public Project? Project { get; set; }
    public User? CreatedBy { get; set; }
}
