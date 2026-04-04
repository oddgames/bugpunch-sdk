namespace UIAutomation.Server.Models;

/// <summary>
/// Result of a single Gemini AI test within a run.
/// Tracks pass/fail, timing, steps, and errors.
/// </summary>
public class GeminiResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RunId { get; set; } = "";
    public string? TestDefinitionId { get; set; }
    public string TestName { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string? Scene { get; set; }
    public string Result { get; set; } = "pass"; // "pass", "fail", "error"
    public string? FailReason { get; set; }
    public int StepsExecuted { get; set; }
    public int MaxSteps { get; set; }
    public double DurationSeconds { get; set; }
    public string? AppVersion { get; set; }
    public string? GeminiModel { get; set; }
    public int ErrorCount { get; set; }
    public int ExceptionCount { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

    public GeminiRun? Run { get; set; }
}
