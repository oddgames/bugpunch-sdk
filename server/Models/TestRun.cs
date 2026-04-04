namespace UIAutomation.Server.Models;

public class TestRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? Project { get; set; }
    public string? Branch { get; set; }
    public string? Commit { get; set; }
    public string? AppVersion { get; set; }
    public string? Platform { get; set; }
    public string? MachineName { get; set; }
    public string? ProjectId { get; set; } // FK for auth scoping
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public bool IsComplete { get; set; }
}
