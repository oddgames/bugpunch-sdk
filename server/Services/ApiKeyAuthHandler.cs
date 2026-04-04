using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace UIAutomation.Server.Services;

/// <summary>
/// Custom authentication handler that validates X-Api-Key headers
/// and sets claims including the project's ID for team scoping.
/// </summary>
public class ApiKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly AuthService _auth;

    public ApiKeyAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AuthService auth)
        : base(options, logger, encoder)
    {
        _auth = auth;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader))
            return AuthenticateResult.NoResult();

        var rawKey = apiKeyHeader.ToString();
        if (string.IsNullOrEmpty(rawKey))
            return AuthenticateResult.Fail("Empty API key");

        var result = await _auth.ValidateApiKeyAsync(rawKey);
        if (result == null)
            return AuthenticateResult.Fail("Invalid API key");

        var (key, project) = result.Value;

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, $"apikey:{key.Id}"),
            new Claim("projectId", project.Id),
            new Claim("projectName", project.Name),
            new Claim("authMethod", "apikey")
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
