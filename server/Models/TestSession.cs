namespace UIAutomation.Server.Models;

public class TestSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TestName { get; set; } = "";
    public string Result { get; set; } = "pass"; // "pass", "fail", "warn"
    public string? Scene { get; set; }
    public string? StartTime { get; set; }
    public double Duration { get; set; }
    public string? Project { get; set; }
    public string? Branch { get; set; }
    public string? Commit { get; set; }
    public string? AppVersion { get; set; }
    public string? Platform { get; set; }
    public string? MachineName { get; set; }
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
    public int EventCount { get; set; }
    public int LogCount { get; set; }
    public bool HasVideo { get; set; }
    public string ZipPath { get; set; } = "";
    public long ZipSize { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? FailureMessage { get; set; } // extracted from session logs on upload
    public string? CustomMetadata { get; set; } // JSON blob
    public string? ProjectId { get; set; } // FK to Project (set from API key on upload)
    public string? RunId { get; set; } // FK to TestRun (groups sessions from the same test batch)
}
