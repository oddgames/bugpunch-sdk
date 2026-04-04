namespace UIAutomation.Server.Models;

public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
