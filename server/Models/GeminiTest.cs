namespace UIAutomation.Server.Models;

/// <summary>
/// A Gemini AI test definition — a prompt-based test that an AI agent executes
/// by observing screenshots and issuing UI actions.
/// </summary>
public class GeminiTest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string? Scene { get; set; }
    public int MaxSteps { get; set; } = 20;
    public bool Enabled { get; set; } = true;
    public string? Tags { get; set; }
    public int SortOrder { get; set; }
    public string ProjectId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Project? Project { get; set; }
}
