namespace UIAutomation.Server.Models;

/// <summary>
/// An organization — the top-level grouping. Users and projects belong to an org.
/// </summary>
public class Organization
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
