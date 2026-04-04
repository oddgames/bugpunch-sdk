namespace UIAutomation.Server.Models;

/// <summary>
/// A tracked error or exception from a Gemini test execution.
/// Stored globally for cross-test, cross-project error analysis.
/// </summary>
public class GeminiError
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ResultId { get; set; } = "";
    public string? StepId { get; set; }
    public string ProjectId { get; set; } = "";
    public string? TestName { get; set; }
    public string? Scene { get; set; }
    public string? AppVersion { get; set; }
    public string? Branch { get; set; }
    public string? Platform { get; set; }
    public string Category { get; set; } = "error"; // "error", "exception", "timeout", "api_error"
    public string Message { get; set; } = "";
    public string? StackTrace { get; set; }
    public string? ActionJson { get; set; }
    public int StepIndex { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public GeminiResult? Result { get; set; }
    public Project? Project { get; set; }
}
