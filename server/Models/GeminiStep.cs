namespace UIAutomation.Server.Models;

/// <summary>
/// A single AI action step within a Gemini test result.
/// Records what the model said, what was executed, and whether it worked.
/// </summary>
public class GeminiStep
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ResultId { get; set; } = "";
    public int StepIndex { get; set; }
    public string? ActionJson { get; set; }
    public string? ModelResponse { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public double ElapsedMs { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public GeminiResult? Result { get; set; }
}
