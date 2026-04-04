namespace UIAutomation.Server.Models;

/// <summary>
/// An API key for uploading sessions to a specific project.
/// </summary>
public class ApiKey
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string KeyHash { get; set; } = ""; // SHA256 — used for fast lookup
    public string KeyValue { get; set; } = ""; // full key — shown to admins
    public string Label { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string CreatedByUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Project? Project { get; set; }
    public User? CreatedBy { get; set; }
}
