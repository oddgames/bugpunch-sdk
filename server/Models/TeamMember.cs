namespace UIAutomation.Server.Models;

/// <summary>
/// Membership linking a user to an organization with a role.
/// </summary>
public class OrgMember
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrgId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Role { get; set; } = "member"; // "admin" or "member"

    public Organization? Organization { get; set; }
    public User? User { get; set; }
}
