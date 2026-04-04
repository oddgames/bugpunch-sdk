namespace UIAutomation.Server.Models;

/// <summary>
/// A project within an organization. Sessions and API keys belong to a project.
/// </summary>
public class Project
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string OrgId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Organization? Organization { get; set; }
}
