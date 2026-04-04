using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using UIAutomation.Server.Data;
using UIAutomation.Server.Models;

namespace UIAutomation.Server.Services;

public class AuthService
{
    private readonly AppDbContext _db;
    private readonly string _jwtSecret;
    private readonly int _jwtExpiryDays;

    public AuthService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _jwtSecret = GetOrCreateJwtSecret(config);
        _jwtExpiryDays = config.GetValue("Auth:JwtExpiryDays", 7);
    }

    private static string GetOrCreateJwtSecret(IConfiguration config)
    {
        var secret = config["Auth:JwtSecret"];
        if (!string.IsNullOrEmpty(secret) && secret != "auto-generated-on-first-run-if-missing")
            return secret;

        var secretFile = Path.Combine("data", "jwt-secret.txt");
        Directory.CreateDirectory("data");

        if (File.Exists(secretFile))
            return File.ReadAllText(secretFile).Trim();

        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        secret = Convert.ToBase64String(bytes);
        File.WriteAllText(secretFile, secret);
        return secret;
    }

    public SymmetricSecurityKey GetSigningKey() =>
        new(Encoding.UTF8.GetBytes(_jwtSecret));

    // ==================== Registration & Login ====================

    public async Task<bool> HasAnyUsersAsync() => await _db.Users.AnyAsync();

    public async Task<(User User, string Token)?> RegisterAsync(string email, string password, string displayName)
    {
        email = email.Trim().ToLowerInvariant();

        if (await _db.Users.AnyAsync(u => u.Email == email))
            return null;

        var isFirstUser = !await _db.Users.AnyAsync();

        var user = new User
        {
            Email = email,
            DisplayName = displayName.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsAdmin = isFirstUser
        };
        _db.Users.Add(user);

        // Create a default organization for the first user
        if (isFirstUser)
        {
            var org = new Organization { Name = displayName.Trim() };
            _db.Organizations.Add(org);
            _db.OrgMembers.Add(new OrgMember
            {
                OrgId = org.Id,
                UserId = user.Id,
                Role = "admin"
            });
        }

        await _db.SaveChangesAsync();

        var token = GenerateJwt(user);
        return (user, token);
    }

    public async Task<(User User, string Token)?> LoginAsync(string email, string password)
    {
        email = email.Trim().ToLowerInvariant();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return null;

        var token = GenerateJwt(user);
        return (user, token);
    }

    private string GenerateJwt(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("displayName", user.DisplayName),
            new Claim("isAdmin", user.IsAdmin.ToString().ToLower())
        };

        var key = GetSigningKey();
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "uiat-server",
            audience: "uiat-client",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(_jwtExpiryDays),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // ==================== Organizations ====================

    public async Task<List<object>> GetUserOrgsAsync(string userId)
    {
        return await _db.OrgMembers
            .Where(m => m.UserId == userId)
            .Include(m => m.Organization)
            .Select(m => (object)new
            {
                id = m.Organization!.Id,
                name = m.Organization.Name,
                role = m.Role
            })
            .ToListAsync();
    }

    public async Task<Organization> CreateOrgAsync(string name, string creatorUserId)
    {
        var org = new Organization { Name = name.Trim() };
        _db.Organizations.Add(org);
        _db.OrgMembers.Add(new OrgMember
        {
            OrgId = org.Id,
            UserId = creatorUserId,
            Role = "admin"
        });
        await _db.SaveChangesAsync();
        return org;
    }

    public async Task<List<object>> GetOrgMembersAsync(string orgId)
    {
        return await _db.OrgMembers
            .Where(m => m.OrgId == orgId)
            .Include(m => m.User)
            .Select(m => (object)new
            {
                userId = m.User!.Id,
                email = m.User.Email,
                displayName = m.User.DisplayName,
                role = m.Role
            })
            .ToListAsync();
    }

    public async Task<OrgMember?> AddOrgMemberAsync(string orgId, string email, string role)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email.Trim().ToLowerInvariant());
        if (user == null) return null;

        var exists = await _db.OrgMembers.AnyAsync(m => m.OrgId == orgId && m.UserId == user.Id);
        if (exists) return null;

        var member = new OrgMember { OrgId = orgId, UserId = user.Id, Role = role };
        _db.OrgMembers.Add(member);
        await _db.SaveChangesAsync();
        return member;
    }

    public async Task<bool> RemoveOrgMemberAsync(string orgId, string userId)
    {
        var member = await _db.OrgMembers
            .FirstOrDefaultAsync(m => m.OrgId == orgId && m.UserId == userId);
        if (member == null) return false;

        _db.OrgMembers.Remove(member);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> IsOrgMemberAsync(string userId, string orgId)
    {
        return await _db.OrgMembers.AnyAsync(m => m.OrgId == orgId && m.UserId == userId);
    }

    public async Task<bool> IsOrgAdminAsync(string userId, string orgId)
    {
        return await _db.OrgMembers.AnyAsync(
            m => m.OrgId == orgId && m.UserId == userId && m.Role == "admin");
    }

    // ==================== Projects ====================

    public async Task<List<object>> GetOrgProjectsAsync(string orgId)
    {
        return await _db.Projects
            .Where(p => p.OrgId == orgId)
            .OrderBy(p => p.Name)
            .Select(p => (object)new
            {
                id = p.Id,
                name = p.Name,
                orgId = p.OrgId,
                createdAt = p.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<Project> CreateProjectAsync(string orgId, string name, string creatorUserId)
    {
        var project = new Project { Name = name.Trim(), OrgId = orgId };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        // Auto-generate an API key for the new project
        await CreateApiKeyAsync(project.Id, "Default", creatorUserId);

        return project;
    }

    public async Task<bool> DeleteProjectAsync(string projectId)
    {
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null) return false;

        // Delete associated API keys
        var keys = await _db.ApiKeys.Where(k => k.ProjectId == projectId).ToListAsync();
        _db.ApiKeys.RemoveRange(keys);

        _db.Projects.Remove(project);
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Get the org that owns a project.
    /// </summary>
    public async Task<string?> GetProjectOrgIdAsync(string projectId)
    {
        return await _db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => p.OrgId)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Get all project IDs the user has access to (via org membership).
    /// </summary>
    public async Task<List<string>> GetUserProjectIdsAsync(string userId)
    {
        var orgIds = await _db.OrgMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.OrgId)
            .ToListAsync();

        return await _db.Projects
            .Where(p => orgIds.Contains(p.OrgId))
            .Select(p => p.Id)
            .ToListAsync();
    }

    // ==================== API Keys ====================

    public async Task<ApiKey> CreateApiKeyAsync(string projectId, string label, string userId)
    {
        var bytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        var rawKey = "uiat_" + Convert.ToHexString(bytes).ToLowerInvariant();

        var apiKey = new ApiKey
        {
            KeyHash = HashKey(rawKey),
            KeyValue = rawKey,
            Label = label.Trim(),
            ProjectId = projectId,
            CreatedByUserId = userId
        };

        _db.ApiKeys.Add(apiKey);
        await _db.SaveChangesAsync();

        return apiKey;
    }

    public async Task<List<object>> ListApiKeysAsync(string projectId)
    {
        return await _db.ApiKeys
            .Where(k => k.ProjectId == projectId)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => (object)new
            {
                id = k.Id,
                key = k.KeyValue,
                label = k.Label,
                createdAt = k.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<string?> GetApiKeyOrgIdAsync(string keyId)
    {
        return await _db.ApiKeys
            .Where(k => k.Id == keyId)
            .Include(k => k.Project)
            .Select(k => k.Project!.OrgId)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> DeleteApiKeyAsync(string keyId)
    {
        var key = await _db.ApiKeys.FindAsync(keyId);
        if (key == null) return false;

        _db.ApiKeys.Remove(key);
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Validates an API key and returns the associated project, or null.
    /// </summary>
    public async Task<(ApiKey Key, Project Project)?> ValidateApiKeyAsync(string rawKey)
    {
        var hash = HashKey(rawKey);
        var key = await _db.ApiKeys
            .Include(k => k.Project)
            .FirstOrDefaultAsync(k => k.KeyHash == hash);

        if (key?.Project == null) return null;
        return (key, key.Project);
    }

    private static string HashKey(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
