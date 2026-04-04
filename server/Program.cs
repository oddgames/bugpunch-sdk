using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using UIAutomation.Server.Data;
using UIAutomation.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Allow large file uploads (500 MB) — diagnostic zips with video can exceed the 30MB default
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 500 * 1024 * 1024;
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 500 * 1024 * 1024;
});

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Services
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<GeminiService>();
builder.Services.AddHostedService<RunTimeoutService>();

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    // Auth endpoints: 10 requests per minute per IP
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1)
            }));
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

// Auth — get signing key, configure JWT + API key schemes
{
    using var tempScope = builder.Services.BuildServiceProvider().CreateScope();
    var tempDb = tempScope.ServiceProvider.GetRequiredService<AppDbContext>();
    var tempAuth = new AuthService(tempDb, builder.Configuration);
    var signingKey = tempAuth.GetSigningKey();

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = "Combined";
        options.DefaultChallengeScheme = "Combined";
    })
    .AddJwtBearer("Jwt", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "uiat-server",
            ValidateAudience = true,
            ValidAudience = "uiat-client",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey
        };
        // Allow JWT token in query string for media URLs (<video src>, <img src>)
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["token"];
                if (!string.IsNullOrEmpty(token))
                    context.Token = token;
                return Task.CompletedTask;
            }
        };
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthHandler>("ApiKey", null)
    .AddPolicyScheme("Combined", "JWT or API Key", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            if (context.Request.Headers.ContainsKey("X-Api-Key"))
                return "ApiKey";
            return "Jwt";
        };
    });

    builder.Services.AddAuthorization();
}

var app = builder.Build();

// Ensure database is created and migrated
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();

    // Manual schema migrations for SQLite (EnsureCreated doesn't update existing tables)
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();

    // Add KeyValue column if missing (renamed from KeyPrefix)
    cmd.CommandText = "PRAGMA table_info(ApiKeys)";
    var hasKeyValue = false;
    using (var reader = await cmd.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            if (reader.GetString(1) == "KeyValue") hasKeyValue = true;
        }
    }
    if (!hasKeyValue)
    {
        cmd.CommandText = "ALTER TABLE ApiKeys ADD COLUMN KeyValue TEXT NOT NULL DEFAULT ''";
        await cmd.ExecuteNonQueryAsync();
    }

    // Fix legacy KeyPrefix NOT NULL constraint — SQLite can't ALTER COLUMN,
    // so we recreate the table without the old KeyPrefix column
    cmd.CommandText = "PRAGMA table_info(ApiKeys)";
    var hasKeyPrefix = false;
    using (var reader3 = await cmd.ExecuteReaderAsync())
    {
        while (await reader3.ReadAsync())
        {
            if (reader3.GetString(1) == "KeyPrefix") hasKeyPrefix = true;
        }
    }
    if (hasKeyPrefix)
    {
        cmd.CommandText = @"
            CREATE TABLE ApiKeys_new (
                Id TEXT PRIMARY KEY,
                KeyHash TEXT NOT NULL DEFAULT '',
                KeyValue TEXT NOT NULL DEFAULT '',
                Label TEXT NOT NULL DEFAULT '',
                ProjectId TEXT NOT NULL DEFAULT '',
                CreatedByUserId TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT '',
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id),
                FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id)
            );
            INSERT INTO ApiKeys_new (Id, KeyHash, KeyValue, Label, ProjectId, CreatedByUserId, CreatedAt)
                SELECT Id, KeyHash, KeyValue, Label, ProjectId, CreatedByUserId, CreatedAt FROM ApiKeys;
            DROP TABLE ApiKeys;
            ALTER TABLE ApiKeys_new RENAME TO ApiKeys;
            CREATE INDEX IF NOT EXISTS IX_ApiKeys_KeyHash ON ApiKeys (KeyHash);
            CREATE INDEX IF NOT EXISTS IX_ApiKeys_ProjectId ON ApiKeys (ProjectId);
        ";
        await cmd.ExecuteNonQueryAsync();
        Console.WriteLine("Migrated ApiKeys table: removed legacy KeyPrefix column.");
    }

    // Add RunId column to TestSessions if missing
    cmd.CommandText = "PRAGMA table_info(TestSessions)";
    var hasRunId = false;
    using (var reader2 = await cmd.ExecuteReaderAsync())
    {
        while (await reader2.ReadAsync())
        {
            if (reader2.GetString(1) == "RunId") hasRunId = true;
        }
    }
    if (!hasRunId)
    {
        cmd.CommandText = "ALTER TABLE TestSessions ADD COLUMN RunId TEXT";
        await cmd.ExecuteNonQueryAsync();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_TestSessions_RunId ON TestSessions (RunId)";
        await cmd.ExecuteNonQueryAsync();
    }

    // Add FailureMessage column to TestSessions if missing
    cmd.CommandText = "PRAGMA table_info(TestSessions)";
    var hasFailureMsg = false;
    using (var readerFm = await cmd.ExecuteReaderAsync())
    {
        while (await readerFm.ReadAsync())
            if (readerFm.GetString(1) == "FailureMessage") hasFailureMsg = true;
    }
    if (!hasFailureMsg)
    {
        cmd.CommandText = "ALTER TABLE TestSessions ADD COLUMN FailureMessage TEXT";
        await cmd.ExecuteNonQueryAsync();
    }

    // Create TestRuns table if missing
    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS TestRuns (
        Id TEXT PRIMARY KEY,
        Project TEXT,
        Branch TEXT,
        ""Commit"" TEXT,
        AppVersion TEXT,
        Platform TEXT,
        MachineName TEXT,
        ProjectId TEXT,
        StartedAt TEXT NOT NULL,
        FinishedAt TEXT,
        IsComplete INTEGER NOT NULL DEFAULT 0
    )";
    await cmd.ExecuteNonQueryAsync();
    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_TestRuns_ProjectId ON TestRuns (ProjectId)";
    await cmd.ExecuteNonQueryAsync();
    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_TestRuns_StartedAt ON TestRuns (StartedAt)";
    await cmd.ExecuteNonQueryAsync();

    // Create GeminiTests table if missing
    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS GeminiTests (
        Id TEXT PRIMARY KEY,
        Name TEXT NOT NULL,
        Prompt TEXT NOT NULL,
        Scene TEXT,
        MaxSteps INTEGER NOT NULL DEFAULT 20,
        Enabled INTEGER NOT NULL DEFAULT 1,
        Tags TEXT,
        SortOrder INTEGER NOT NULL DEFAULT 0,
        ProjectId TEXT NOT NULL,
        CreatedAt TEXT NOT NULL,
        UpdatedAt TEXT NOT NULL,
        FOREIGN KEY (ProjectId) REFERENCES Projects(Id)
    )";
    await cmd.ExecuteNonQueryAsync();
    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_GeminiTests_ProjectId ON GeminiTests (ProjectId)";
    await cmd.ExecuteNonQueryAsync();
    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_GeminiTests_Name ON GeminiTests (Name)";
    await cmd.ExecuteNonQueryAsync();

    // Create GeminiRuns table if missing
    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS GeminiRuns (
        Id TEXT PRIMARY KEY,
        ProjectId TEXT NOT NULL,
        Branch TEXT, ""Commit"" TEXT, AppVersion TEXT,
        Platform TEXT, MachineName TEXT, GeminiModel TEXT,
        CreatedByUserId TEXT,
        StartedAt TEXT NOT NULL, FinishedAt TEXT, IsComplete INTEGER NOT NULL DEFAULT 0,
        TotalTests INTEGER NOT NULL DEFAULT 0, PassedTests INTEGER NOT NULL DEFAULT 0, FailedTests INTEGER NOT NULL DEFAULT 0,
        FOREIGN KEY (ProjectId) REFERENCES Projects(Id)
    )";
    await cmd.ExecuteNonQueryAsync();
    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_GeminiRuns_ProjectId ON GeminiRuns (ProjectId)";
    await cmd.ExecuteNonQueryAsync();
    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_GeminiRuns_StartedAt ON GeminiRuns (StartedAt)";
    await cmd.ExecuteNonQueryAsync();
    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_GeminiRuns_AppVersion ON GeminiRuns (AppVersion)";
    await cmd.ExecuteNonQueryAsync();

    // Create GeminiResults table if missing
    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS GeminiResults (
        Id TEXT PRIMARY KEY,
        RunId TEXT NOT NULL,
        TestDefinitionId TEXT,
        TestName TEXT NOT NULL, Prompt TEXT NOT NULL,
        Scene TEXT, Result TEXT NOT NULL DEFAULT 'pass',
        FailReason TEXT, StepsExecuted INTEGER NOT NULL DEFAULT 0,
        MaxSteps INTEGER NOT NULL DEFAULT 20,
        DurationSeconds REAL NOT NULL DEFAULT 0,
        AppVersion TEXT, GeminiModel TEXT,
        ErrorCount INTEGER NOT NULL DEFAULT 0, ExceptionCount INTEGER NOT NULL DEFAULT 0,
        StartedAt TEXT NOT NULL, FinishedAt TEXT,
        FOREIGN KEY (RunId) REFERENCES GeminiRuns(Id)
    )";
    await cmd.ExecuteNonQueryAsync();
    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_GeminiResults_RunId ON GeminiResults (RunId)";
    await cmd.ExecuteNonQueryAsync();
    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_GeminiResults_TestName ON GeminiResults (TestName)";
    await cmd.ExecuteNonQueryAsync();
    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_GeminiResults_Result ON GeminiResults (Result)";
    await cmd.ExecuteNonQueryAsync();
    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_GeminiResults_AppVersion ON GeminiResults (AppVersion)";
    await cmd.ExecuteNonQueryAsync();

    // Create GeminiSteps table if missing
    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS GeminiSteps (
        Id TEXT PRIMARY KEY,
        ResultId TEXT NOT NULL,
        StepIndex INTEGER NOT NULL,
        ActionJson TEXT, ModelResponse TEXT,
        Success INTEGER NOT NULL DEFAULT 0,
        Error TEXT, ElapsedMs REAL NOT NULL DEFAULT 0,
        Timestamp TEXT NOT NULL,
        FOREIGN KEY (ResultId) REFERENCES GeminiResults(Id)
    )";
    await cmd.ExecuteNonQueryAsync();
    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_GeminiSteps_ResultId ON GeminiSteps (ResultId)";
    await cmd.ExecuteNonQueryAsync();

    // Create GeminiErrors table if missing
    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS GeminiErrors (
        Id TEXT PRIMARY KEY,
        ResultId TEXT NOT NULL,
        StepId TEXT,
        ProjectId TEXT NOT NULL,
        TestName TEXT, Scene TEXT,
        AppVersion TEXT, Branch TEXT, Platform TEXT,
        Category TEXT NOT NULL DEFAULT 'error',
        Message TEXT NOT NULL DEFAULT '',
        StackTrace TEXT, ActionJson TEXT,
        StepIndex INTEGER NOT NULL DEFAULT 0,
        Timestamp TEXT NOT NULL,
        FOREIGN KEY (ResultId) REFERENCES GeminiResults(Id),
        FOREIGN KEY (ProjectId) REFERENCES Projects(Id)
    )";
    await cmd.ExecuteNonQueryAsync();
    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_GeminiErrors_ResultId ON GeminiErrors (ResultId)";
    await cmd.ExecuteNonQueryAsync();
    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_GeminiErrors_ProjectId ON GeminiErrors (ProjectId)";
    await cmd.ExecuteNonQueryAsync();
    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_GeminiErrors_TestName ON GeminiErrors (TestName)";
    await cmd.ExecuteNonQueryAsync();
    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_GeminiErrors_Category ON GeminiErrors (Category)";
    await cmd.ExecuteNonQueryAsync();
    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_GeminiErrors_AppVersion ON GeminiErrors (AppVersion)";
    await cmd.ExecuteNonQueryAsync();
    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_GeminiErrors_Timestamp ON GeminiErrors (Timestamp)";
    await cmd.ExecuteNonQueryAsync();

    // Regenerate keys that have empty KeyValue (lost during schema change)
    var emptyKeys = await db.ApiKeys.Where(k => k.KeyValue == "").ToListAsync();
    if (emptyKeys.Count > 0)
    {
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        foreach (var key in emptyKeys)
        {
            var bytes = new byte[16];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            var rawKey = "uiat_" + Convert.ToHexString(bytes).ToLowerInvariant();
            key.KeyValue = rawKey;
            key.KeyHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant();
        }
        await db.SaveChangesAsync();
        Console.WriteLine($"Regenerated {emptyKeys.Count} API key(s) with missing key values.");
    }
}

var storagePath = builder.Configuration["SessionStorage:Path"] ?? "data/sessions";
Directory.CreateDirectory(storagePath);

// Global error handler — surface exception details in response
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (Exception ex)
    {
        var msg = ex.Message;
        if (ex.InnerException != null) msg += " | " + ex.InnerException.Message;
        var fullError = $"[ERROR] {context.Request.Method} {context.Request.Path}: {msg}\n{ex}";
        Console.Error.WriteLine(fullError);
        File.AppendAllText("data/error.log", fullError + "\n\n");
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync(msg);
        }
    }
});

app.UseCors();
app.UseRateLimiter();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

// ==================== Helpers ====================

static string? GetUserId(HttpContext ctx) =>
    ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);

static bool IsAdmin(HttpContext ctx) =>
    ctx.User.FindFirstValue("isAdmin") == "true";

// Resolve projectId: from API key claim (set by ApiKeyAuthHandler), or X-Project-Id header
static string? ResolveProjectId(HttpContext ctx)
{
    var apiKeyProject = ctx.User.FindFirstValue("projectId");
    if (!string.IsNullOrEmpty(apiKeyProject)) return apiKeyProject;

    if (ctx.Request.Headers.TryGetValue("X-Project-Id", out var header))
        return header.ToString();

    return null;
}

// Get project IDs for scoping queries — returns null for admins (no filter)
static async Task<List<string>?> GetScopedProjectIds(HttpContext ctx, AuthService auth, string? projectId)
{
    var userId = GetUserId(ctx);
    if (userId == null) return new List<string>();

    if (!string.IsNullOrEmpty(projectId))
        return new List<string> { projectId };

    if (IsAdmin(ctx))
        return null; // admins see everything

    return await auth.GetUserProjectIdsAsync(userId);
}

// ==================== Public Endpoints ====================

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));


app.MapGet("/api/storage", () =>
{
    var sessionsDir = new DirectoryInfo(storagePath);
    long totalBytes = 0;
    int fileCount = 0;
    if (sessionsDir.Exists)
    {
        foreach (var f in sessionsDir.GetFiles("*.zip"))
        {
            totalBytes += f.Length;
            fileCount++;
        }
    }
    return Results.Ok(new
    {
        totalBytes,
        totalMB = Math.Round(totalBytes / 1024.0 / 1024.0, 1),
        totalGB = Math.Round(totalBytes / 1024.0 / 1024.0 / 1024.0, 2),
        fileCount
    });
});

// ==================== Auth Endpoints ====================

// Bootstrap: only works when no users exist yet (first-time setup)
app.MapPost("/api/auth/setup", async (AuthRegisterRequest req, AuthService auth) =>
{
    if (await auth.HasAnyUsersAsync())
        return Results.BadRequest("Setup already completed. Use invite to add users.");

    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest("Email and password required");

    if (req.Password.Length < 6)
        return Results.BadRequest("Password must be at least 6 characters");

    var result = await auth.RegisterAsync(req.Email, req.Password, req.DisplayName ?? req.Email.Split('@')[0]);
    if (result == null)
        return Results.Conflict("Email already registered");

    var (user, token) = result.Value;
    var orgs = await auth.GetUserOrgsAsync(user.Id);
    return Results.Ok(new
    {
        token,
        user = new { user.Id, user.Email, user.DisplayName, user.IsAdmin },
        organizations = orgs
    });
}).RequireRateLimiting("auth");

// Check if setup is needed (no users exist)
app.MapGet("/api/auth/needs-setup", async (AuthService auth) =>
{
    return Results.Ok(new { needsSetup = !await auth.HasAnyUsersAsync() });
});

app.MapPost("/api/auth/login", async (AuthLoginRequest req, AuthService auth) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest("Email and password required");

    var result = await auth.LoginAsync(req.Email, req.Password);
    if (result == null)
        return Results.Unauthorized();

    var (user, token) = result.Value;
    var orgs = await auth.GetUserOrgsAsync(user.Id);
    return Results.Ok(new
    {
        token,
        user = new { user.Id, user.Email, user.DisplayName, user.IsAdmin },
        organizations = orgs
    });
}).RequireRateLimiting("auth");

app.MapGet("/api/auth/me", async (HttpContext ctx, AuthService auth) =>
{
    var userId = GetUserId(ctx);
    if (userId == null) return Results.Unauthorized();

    var orgs = await auth.GetUserOrgsAsync(userId);
    return Results.Ok(new
    {
        id = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier),
        email = ctx.User.FindFirstValue(ClaimTypes.Email),
        displayName = ctx.User.FindFirstValue("displayName"),
        isAdmin = IsAdmin(ctx),
        organizations = orgs
    });
}).RequireAuthorization();

app.MapPost("/api/auth/validate-key", async (ValidateKeyRequest req, AuthService auth) =>
{
    if (string.IsNullOrWhiteSpace(req.Key))
        return Results.BadRequest("Key required");

    var result = await auth.ValidateApiKeyAsync(req.Key);
    if (result == null)
        return Results.Ok(new { valid = false, projectName = (string?)null });

    return Results.Ok(new { valid = true, projectName = result.Value.Project.Name });
}).RequireRateLimiting("auth");

// ==================== Organization Endpoints ====================

app.MapGet("/api/orgs", async (HttpContext ctx, AuthService auth) =>
{
    var userId = GetUserId(ctx);
    if (userId == null) return Results.Unauthorized();
    return Results.Ok(await auth.GetUserOrgsAsync(userId));
}).RequireAuthorization();

app.MapPost("/api/orgs", async (HttpContext ctx, CreateOrgRequest req, AuthService auth) =>
{
    var userId = GetUserId(ctx);
    if (userId == null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest("Organization name required");

    try
    {
        var org = await auth.CreateOrgAsync(req.Name, userId);
        return Results.Ok(new { id = org.Id, name = org.Name });
    }
    catch (DbUpdateException)
    {
        return Results.Conflict("Organization name already exists");
    }
}).RequireAuthorization();

app.MapGet("/api/orgs/{orgId}/members", async (HttpContext ctx, string orgId, AuthService auth) =>
{
    var userId = GetUserId(ctx);
    if (userId == null) return Results.Unauthorized();
    if (!await auth.IsOrgMemberAsync(userId, orgId) && !IsAdmin(ctx))
        return Results.Forbid();
    return Results.Ok(await auth.GetOrgMembersAsync(orgId));
}).RequireAuthorization();

app.MapPost("/api/orgs/{orgId}/members", async (HttpContext ctx, string orgId, AddMemberRequest req, AuthService auth) =>
{
    var userId = GetUserId(ctx);
    if (userId == null) return Results.Unauthorized();
    if (!await auth.IsOrgAdminAsync(userId, orgId) && !IsAdmin(ctx))
        return Results.Forbid();

    if (string.IsNullOrWhiteSpace(req.Email))
        return Results.BadRequest("Email required");

    // If user doesn't exist and password provided, create the account (invite)
    var member = await auth.AddOrgMemberAsync(orgId, req.Email, req.Role ?? "member");
    if (member == null)
    {
        // Check if already a member
        var existingMembers = await auth.GetOrgMembersAsync(orgId);
        var alreadyMember = existingMembers.Any(m =>
        {
            var json = System.Text.Json.JsonSerializer.Serialize(m);
            return json.Contains(req.Email.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
        });
        if (alreadyMember)
            return Results.BadRequest("Already a member of this organization");

        if (!string.IsNullOrWhiteSpace(req.Password))
        {
            if (req.Password.Length < 6)
                return Results.BadRequest("Password must be at least 6 characters");

            var regResult = await auth.RegisterAsync(req.Email, req.Password, req.DisplayName ?? req.Email.Split('@')[0]);
            if (regResult == null)
                return Results.BadRequest("Email already registered");

            member = await auth.AddOrgMemberAsync(orgId, req.Email, req.Role ?? "member");
        }
    }

    if (member == null)
        return Results.BadRequest("User not found. Provide a password to create their account.");
    return Results.Ok(new { member.Id, member.UserId, member.OrgId, member.Role });
}).RequireAuthorization();

app.MapDelete("/api/orgs/{orgId}/members/{memberId}", async (HttpContext ctx, string orgId, string memberId, AuthService auth) =>
{
    var userId = GetUserId(ctx);
    if (userId == null) return Results.Unauthorized();
    if (!await auth.IsOrgAdminAsync(userId, orgId) && !IsAdmin(ctx))
        return Results.Forbid();
    return await auth.RemoveOrgMemberAsync(orgId, memberId)
        ? Results.Ok() : Results.NotFound();
}).RequireAuthorization();

// ==================== Project Endpoints ====================

app.MapGet("/api/orgs/{orgId}/projects", async (HttpContext ctx, string orgId, AuthService auth) =>
{
    var userId = GetUserId(ctx);
    if (userId == null) return Results.Unauthorized();
    if (!await auth.IsOrgMemberAsync(userId, orgId) && !IsAdmin(ctx))
        return Results.Forbid();
    return Results.Ok(await auth.GetOrgProjectsAsync(orgId));
}).RequireAuthorization();

app.MapPost("/api/orgs/{orgId}/projects", async (HttpContext ctx, string orgId, CreateProjectRequest req, AuthService auth) =>
{
    var userId = GetUserId(ctx);
    if (userId == null) return Results.Unauthorized();
    if (!await auth.IsOrgAdminAsync(userId, orgId) && !IsAdmin(ctx))
        return Results.Forbid();
    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest("Project name required");

    try
    {
        var project = await auth.CreateProjectAsync(orgId, req.Name, userId);
        // Return the auto-generated API key
        var keys = await auth.ListApiKeysAsync(project.Id);
        return Results.Ok(new { id = project.Id, name = project.Name, orgId = project.OrgId, keys });
    }
    catch (DbUpdateException)
    {
        return Results.Conflict("Project name already exists in this organization");
    }
}).RequireAuthorization();

app.MapDelete("/api/projects/{projectId}", async (HttpContext ctx, string projectId, AuthService auth) =>
{
    var userId = GetUserId(ctx);
    if (userId == null) return Results.Unauthorized();

    var orgId = await auth.GetProjectOrgIdAsync(projectId);
    if (orgId == null) return Results.NotFound();
    if (!await auth.IsOrgAdminAsync(userId, orgId) && !IsAdmin(ctx))
        return Results.Forbid();

    return await auth.DeleteProjectAsync(projectId) ? Results.Ok() : Results.NotFound();
}).RequireAuthorization();

// ==================== API Key Endpoints ====================

app.MapGet("/api/projects/{projectId}/keys", async (HttpContext ctx, string projectId, AuthService auth) =>
{
    var userId = GetUserId(ctx);
    if (userId == null) return Results.Unauthorized();

    var orgId = await auth.GetProjectOrgIdAsync(projectId);
    if (orgId == null) return Results.NotFound();
    if (!await auth.IsOrgMemberAsync(userId, orgId) && !IsAdmin(ctx))
        return Results.Forbid();

    return Results.Ok(await auth.ListApiKeysAsync(projectId));
}).RequireAuthorization();

app.MapPost("/api/projects/{projectId}/keys", async (HttpContext ctx, string projectId, CreateKeyRequest req, AuthService auth) =>
{
    var userId = GetUserId(ctx);
    if (userId == null) return Results.Unauthorized();

    var orgId = await auth.GetProjectOrgIdAsync(projectId);
    if (orgId == null) return Results.NotFound("Project not found");
    if (!await auth.IsOrgAdminAsync(userId, orgId) && !IsAdmin(ctx))
        return Results.Forbid();

    try
    {
        var apiKey = await auth.CreateApiKeyAsync(projectId, req.Label ?? "API Key", userId);
        return Results.Ok(new { id = apiKey.Id, key = apiKey.KeyValue, label = apiKey.Label });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
}).RequireAuthorization();

app.MapDelete("/api/keys/{keyId}", async (HttpContext ctx, string keyId, AuthService auth) =>
{
    var userId = GetUserId(ctx);
    if (userId == null) return Results.Unauthorized();

    var keyOrgId = await auth.GetApiKeyOrgIdAsync(keyId);
    if (keyOrgId == null) return Results.NotFound();
    if (!await auth.IsOrgAdminAsync(userId, keyOrgId) && !IsAdmin(ctx))
        return Results.Forbid();

    return await auth.DeleteApiKeyAsync(keyId) ? Results.Ok() : Results.NotFound();
}).RequireAuthorization();

// ==================== Session Endpoints ====================

// Upload a session zip
app.MapPost("/api/sessions/upload", async (HttpContext ctx, HttpRequest request, SessionService service) =>
{
    var projectId = ResolveProjectId(ctx);

    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart form data");

    var form = await request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();
    if (file == null || file.Length == 0)
        return Results.BadRequest("No file uploaded");

    if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("File must be a .zip archive");

    using var stream = file.OpenReadStream();
    var session = await service.ImportAsync(stream, file.FileName);

    if (!string.IsNullOrEmpty(projectId))
    {
        session.ProjectId = projectId;
        await service.UpdateAsync(session);
    }

    return Results.Ok(session);
}).RequireAuthorization();

// List sessions — scoped to user's projects
app.MapGet("/api/sessions", async (
    HttpContext ctx,
    string? testName, string? result, string? project, string? branch,
    string? appVersion,
    DateTime? from, DateTime? to, string? projectId,
    int? page, int? pageSize, string? sort, string? order,
    SessionService service, AuthService auth) =>
{
    var scopedIds = await GetScopedProjectIds(ctx, auth, projectId);

    var p = page ?? 1;
    var ps = pageSize ?? 50;
    if (p < 1) p = 1;
    if (ps < 1 || ps > 200) ps = 50;

    var (items, total) = await service.QueryAsync(
        testName, result, project, branch, from, to,
        p, ps, sort ?? "createdAt", order ?? "desc", scopedIds, appVersion);

    return Results.Ok(new { items, total, page = p, pageSize = ps });
}).RequireAuthorization();

// Get single session
app.MapGet("/api/sessions/{id}", async (HttpContext ctx, string id, SessionService service, AuthService auth) =>
{
    var session = await service.GetAsync(id);
    if (session == null) return Results.NotFound();

    var userId = GetUserId(ctx);
    if (userId != null && !IsAdmin(ctx) && !string.IsNullOrEmpty(session.ProjectId))
    {
        var orgId = await auth.GetProjectOrgIdAsync(session.ProjectId);
        if (orgId != null && !await auth.IsOrgMemberAsync(userId, orgId))
            return Results.Forbid();
    }

    return Results.Ok(session);
}).RequireAuthorization();

// Get raw session.json from zip
app.MapGet("/api/sessions/{id}/session.json", async (string id, SessionService service) =>
{
    var session = await service.GetAsync(id);
    if (session == null || !File.Exists(session.ZipPath))
        return Results.NotFound();

    var data = ZipProcessor.ReadFileFromZip(session.ZipPath, "session.json");
    return data != null
        ? Results.Bytes(data, "application/json", "session.json")
        : Results.NotFound();
}).RequireAuthorization();

// List files in a session zip
app.MapGet("/api/sessions/{id}/files", async (string id, SessionService service) =>
{
    var session = await service.GetAsync(id);
    if (session == null || !File.Exists(session.ZipPath))
        return Results.NotFound();

    var files = ZipProcessor.ListFiles(session.ZipPath);
    return Results.Ok(files);
}).RequireAuthorization();

// Serve a specific file from the zip
app.MapGet("/api/sessions/{id}/files/{fileName}", async (string id, string fileName, SessionService service) =>
{
    var session = await service.GetAsync(id);
    if (session == null || !File.Exists(session.ZipPath))
        return Results.NotFound();

    var data = ZipProcessor.ReadFileFromZip(session.ZipPath, fileName);
    if (data == null) return Results.NotFound();

    return Results.Bytes(data, GetContentType(fileName), fileName);
}).RequireAuthorization();

// Video with range support
app.MapGet("/api/sessions/{id}/video", async (string id, SessionService service) =>
{
    var session = await service.GetAsync(id);
    if (session == null || !File.Exists(session.ZipPath))
        return Results.NotFound();

    var cacheDir = Path.Combine(Path.GetTempPath(), "uiat_video_cache");
    Directory.CreateDirectory(cacheDir);
    var cachedPath = Path.Combine(cacheDir, $"{id}.mp4");

    if (!File.Exists(cachedPath))
    {
        var videoData = ZipProcessor.ReadFileFromZip(session.ZipPath, "recording.mp4");
        if (videoData == null) return Results.NotFound();
        await File.WriteAllBytesAsync(cachedPath, videoData);
    }

    return Results.File(cachedPath, "video/mp4", enableRangeProcessing: true);
}).RequireAuthorization();

// Stats — scoped to user's projects
app.MapGet("/api/stats", async (HttpContext ctx, string? projectId, SessionService service, AuthService auth) =>
{
    var scopedIds = await GetScopedProjectIds(ctx, auth, projectId);
    return Results.Ok(await service.GetStatsAsync(scopedIds));
}).RequireAuthorization();

// Distinct metadata project names in sessions — scoped
app.MapGet("/api/session-projects", async (HttpContext ctx, string? projectId, SessionService service, AuthService auth) =>
{
    var scopedIds = await GetScopedProjectIds(ctx, auth, projectId);
    return Results.Ok(await service.GetSessionProjectsAsync(scopedIds));
}).RequireAuthorization();

// Versions — scoped
app.MapGet("/api/versions", async (HttpContext ctx, string? project, string? projectId, SessionService service, AuthService auth) =>
{
    var scopedIds = await GetScopedProjectIds(ctx, auth, projectId);
    return Results.Ok(await service.GetVersionsAsync(project, scopedIds));
}).RequireAuthorization();

// Branches — scoped
app.MapGet("/api/branches", async (HttpContext ctx, string? project, string? projectId, SessionService service, AuthService auth) =>
{
    var scopedIds = await GetScopedProjectIds(ctx, auth, projectId);
    return Results.Ok(await service.GetBranchesAsync(project, scopedIds));
}).RequireAuthorization();

// Tests — scoped
app.MapGet("/api/tests", async (HttpContext ctx, string? project, string? branch, string? projectId, SessionService service, AuthService auth) =>
{
    var scopedIds = await GetScopedProjectIds(ctx, auth, projectId);
    return Results.Ok(await service.GetTestsAsync(project, branch, scopedIds));
}).RequireAuthorization();

// Delete session
app.MapDelete("/api/sessions/{id}", async (HttpContext ctx, string id, SessionService service, AuthService auth) =>
{
    var userId = GetUserId(ctx);
    if (userId == null) return Results.Unauthorized();

    var session = await service.GetAsync(id);
    if (session == null) return Results.NotFound();

    if (!IsAdmin(ctx) && !string.IsNullOrEmpty(session.ProjectId))
    {
        var orgId = await auth.GetProjectOrgIdAsync(session.ProjectId);
        if (orgId != null && !await auth.IsOrgAdminAsync(userId, orgId))
            return Results.Forbid();
    }

    return await service.DeleteAsync(id) ? Results.Ok() : Results.NotFound();
}).RequireAuthorization();

// Bulk delete
app.MapDelete("/api/sessions/bulk", async (HttpContext ctx, DateTime? olderThan, string? project, string? result, string? projectId, SessionService service, AuthService auth) =>
{
    var userId = GetUserId(ctx);
    if (userId == null) return Results.Unauthorized();

    var scopedIds = await GetScopedProjectIds(ctx, auth, projectId);
    var count = await service.BulkDeleteAsync(olderThan, project, result, scopedIds);
    return Results.Ok(new { deleted = count });
}).RequireAuthorization();

// ==================== Run Endpoints ====================

// Start a new test run — returns runId
app.MapPost("/api/runs/start", async (HttpContext ctx, SessionService service) =>
{
    var projectId = ResolveProjectId(ctx);
    var run = await service.StartRunAsync(projectId);
    return Results.Ok(new { runId = run.Id });
}).RequireAuthorization();

// Mark a run as complete
app.MapPost("/api/runs/{runId}/finish", async (string runId, SessionService service) =>
{
    return await service.FinishRunAsync(runId) ? Results.Ok() : Results.NotFound();
}).RequireAuthorization();

// List runs with aggregated stats — scoped
app.MapGet("/api/runs", async (
    HttpContext ctx, string? project, string? branch, string? appVersion,
    string? result, int? page, int? pageSize, string? projectId,
    SessionService service, AuthService auth) =>
{
    var scopedIds = await GetScopedProjectIds(ctx, auth, projectId);
    var p = Math.Max(1, page ?? 1);
    var ps = Math.Clamp(pageSize ?? 50, 1, 200);

    var (items, total) = await service.QueryRunsAsync(project, branch, appVersion, result, p, ps, scopedIds);
    return Results.Ok(new { items, total, page = p, pageSize = ps });
}).RequireAuthorization();

// Get all sessions in a run
app.MapGet("/api/runs/{runId}", async (HttpContext ctx, string runId, SessionService service, AuthService auth) =>
{
    var scopedIds = await GetScopedProjectIds(ctx, auth, null);
    var sessions = await service.GetRunSessionsAsync(runId, scopedIds);
    return Results.Ok(sessions);
}).RequireAuthorization();

// Delete a run and all its sessions
app.MapDelete("/api/runs/{runId}", async (HttpContext ctx, string runId, SessionService service, AuthService auth) =>
{
    var userId = GetUserId(ctx);
    if (userId == null) return Results.Unauthorized();

    var run = await service.GetRunAsync(runId);
    if (run == null) return Results.NotFound();

    if (!IsAdmin(ctx) && !string.IsNullOrEmpty(run.ProjectId))
    {
        var orgId = await auth.GetProjectOrgIdAsync(run.ProjectId);
        if (orgId != null && !await auth.IsOrgAdminAsync(userId, orgId))
            return Results.Forbid();
    }

    return await service.DeleteRunAsync(runId) ? Results.Ok() : Results.NotFound();
}).RequireAuthorization();

// ==================== Gemini Test Endpoints ====================

// List all Gemini tests for a project (or all accessible projects)
app.MapGet("/api/gemini-tests", async (HttpContext ctx, string? projectId, bool? enabledOnly, AppDbContext db, AuthService auth) =>
{
    var scopedIds = await GetScopedProjectIds(ctx, auth, projectId);

    var query = db.GeminiTests.AsQueryable();
    if (scopedIds != null)
        query = query.Where(t => scopedIds.Contains(t.ProjectId));
    if (enabledOnly == true)
        query = query.Where(t => t.Enabled);

    var tests = await query.OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync();
    return Results.Ok(tests.Select(t => new
    {
        t.Id, t.Name, t.Prompt, t.Scene, t.MaxSteps, t.Enabled, t.Tags, t.SortOrder, t.ProjectId,
        t.CreatedAt, t.UpdatedAt
    }));
}).RequireAuthorization();

// Get a single Gemini test
app.MapGet("/api/gemini-tests/{id}", async (string id, AppDbContext db) =>
{
    var test = await db.GeminiTests.FindAsync(id);
    return test == null ? Results.NotFound() : Results.Ok(new
    {
        test.Id, test.Name, test.Prompt, test.Scene, test.MaxSteps, test.Enabled,
        test.Tags, test.SortOrder, test.ProjectId, test.CreatedAt, test.UpdatedAt
    });
}).RequireAuthorization();

// Create a Gemini test
app.MapPost("/api/gemini-tests", async (HttpContext ctx, CreateGeminiTestRequest req, AppDbContext db) =>
{
    var projectId = ResolveProjectId(ctx);
    if (string.IsNullOrEmpty(projectId))
        return Results.BadRequest(new { error = "Project ID required (via API key or X-Project-Id header)" });

    var test = new UIAutomation.Server.Models.GeminiTest
    {
        Name = req.Name,
        Prompt = req.Prompt,
        Scene = req.Scene,
        MaxSteps = req.MaxSteps ?? 20,
        Enabled = req.Enabled ?? true,
        Tags = req.Tags,
        SortOrder = req.SortOrder ?? 0,
        ProjectId = projectId
    };

    db.GeminiTests.Add(test);
    await db.SaveChangesAsync();
    return Results.Created($"/api/gemini-tests/{test.Id}", new
    {
        test.Id, test.Name, test.Prompt, test.Scene, test.MaxSteps, test.Enabled,
        test.Tags, test.SortOrder, test.ProjectId, test.CreatedAt, test.UpdatedAt
    });
}).RequireAuthorization();

// Update a Gemini test
app.MapPut("/api/gemini-tests/{id}", async (string id, UpdateGeminiTestRequest req, AppDbContext db) =>
{
    var test = await db.GeminiTests.FindAsync(id);
    if (test == null) return Results.NotFound();

    if (req.Name != null) test.Name = req.Name;
    if (req.Prompt != null) test.Prompt = req.Prompt;
    if (req.Scene != null) test.Scene = req.Scene;
    if (req.MaxSteps.HasValue) test.MaxSteps = req.MaxSteps.Value;
    if (req.Enabled.HasValue) test.Enabled = req.Enabled.Value;
    if (req.Tags != null) test.Tags = req.Tags;
    if (req.SortOrder.HasValue) test.SortOrder = req.SortOrder.Value;
    test.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(new
    {
        test.Id, test.Name, test.Prompt, test.Scene, test.MaxSteps, test.Enabled,
        test.Tags, test.SortOrder, test.ProjectId, test.CreatedAt, test.UpdatedAt
    });
}).RequireAuthorization();

// Delete a Gemini test
app.MapDelete("/api/gemini-tests/{id}", async (string id, AppDbContext db) =>
{
    var test = await db.GeminiTests.FindAsync(id);
    if (test == null) return Results.NotFound();
    db.GeminiTests.Remove(test);
    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization();

// ==================== Gemini Run & Result Tracking ====================

// Start a Gemini test run
app.MapPost("/api/gemini-runs/start", async (HttpContext ctx, StartGeminiRunRequest req, GeminiService gemini) =>
{
    var projectId = ResolveProjectId(ctx);
    if (string.IsNullOrEmpty(projectId))
        return Results.BadRequest(new { error = "Project ID required" });

    var run = new UIAutomation.Server.Models.GeminiRun
    {
        ProjectId = projectId,
        Branch = req.Branch,
        Commit = req.Commit,
        AppVersion = req.AppVersion,
        Platform = req.Platform,
        MachineName = req.MachineName,
        GeminiModel = req.GeminiModel,
        CreatedByUserId = GetUserId(ctx)
    };

    await gemini.StartRunAsync(run);
    return Results.Created($"/api/gemini-runs/{run.Id}", new
    {
        run.Id, run.ProjectId, run.Branch, run.Commit, run.AppVersion,
        run.Platform, run.MachineName, run.GeminiModel, run.StartedAt
    });
}).RequireAuthorization();

// Finish a Gemini test run
app.MapPost("/api/gemini-runs/{runId}/finish", async (string runId, GeminiService gemini) =>
{
    var run = await gemini.FinishRunAsync(runId);
    return run == null ? Results.NotFound() : Results.Ok(new
    {
        run.Id, run.TotalTests, run.PassedTests, run.FailedTests,
        run.StartedAt, run.FinishedAt, run.IsComplete
    });
}).RequireAuthorization();

// Upload a test result (with steps and errors)
app.MapPost("/api/gemini-runs/{runId}/results", async (
    HttpContext ctx, string runId, UploadGeminiResultRequest req, GeminiService gemini) =>
{
    var projectId = ResolveProjectId(ctx);

    var result = new UIAutomation.Server.Models.GeminiResult
    {
        RunId = runId,
        TestDefinitionId = req.TestDefinitionId,
        TestName = req.TestName,
        Prompt = req.Prompt,
        Scene = req.Scene,
        Result = req.Passed ? "pass" : "fail",
        FailReason = req.FailReason,
        StepsExecuted = req.StepsExecuted,
        MaxSteps = req.MaxSteps,
        DurationSeconds = req.DurationSeconds,
        AppVersion = req.AppVersion,
        GeminiModel = req.GeminiModel,
        ErrorCount = req.Errors?.Count ?? 0,
        ExceptionCount = req.Errors?.Count(e => e.Category == "exception") ?? 0,
        StartedAt = req.StartedAt ?? DateTime.UtcNow.AddSeconds(-req.DurationSeconds),
        FinishedAt = DateTime.UtcNow
    };

    var steps = new List<UIAutomation.Server.Models.GeminiStep>();
    if (req.Steps != null)
    {
        foreach (var s in req.Steps)
        {
            steps.Add(new UIAutomation.Server.Models.GeminiStep
            {
                ResultId = result.Id,
                StepIndex = s.StepIndex,
                ActionJson = s.ActionJson,
                ModelResponse = s.ModelResponse,
                Success = s.Success,
                Error = s.Error,
                ElapsedMs = s.ElapsedMs
            });
        }
    }

    var errors = new List<UIAutomation.Server.Models.GeminiError>();
    if (req.Errors != null)
    {
        foreach (var e in req.Errors)
        {
            errors.Add(new UIAutomation.Server.Models.GeminiError
            {
                ResultId = result.Id,
                ProjectId = projectId ?? "",
                TestName = req.TestName,
                Scene = req.Scene,
                AppVersion = req.AppVersion,
                Branch = req.Branch,
                Platform = req.Platform,
                Category = e.Category ?? "error",
                Message = e.Message ?? "",
                StackTrace = e.StackTrace,
                ActionJson = e.ActionJson,
                StepIndex = e.StepIndex
            });
        }
    }

    await gemini.AddResultAsync(result, steps, errors);
    return Results.Created($"/api/gemini-results/{result.Id}", new { result.Id, result.TestName, result.Result });
}).RequireAuthorization();

// Query runs — scoped to user's projects
app.MapGet("/api/gemini-runs", async (
    HttpContext ctx, string? projectId, string? appVersion, string? branch,
    int? page, int? pageSize, GeminiService gemini, AuthService auth) =>
{
    var scopedIds = await GetScopedProjectIds(ctx, auth, projectId);
    return Results.Ok(await gemini.QueryRunsAsync(scopedIds, appVersion, branch, page ?? 1, pageSize ?? 20));
}).RequireAuthorization();

// Get a run with its results
app.MapGet("/api/gemini-runs/{runId}", async (string runId, GeminiService gemini) =>
{
    var run = await gemini.GetRunAsync(runId);
    if (run == null) return Results.NotFound();
    var results = await gemini.GetRunResultsAsync(runId);
    return Results.Ok(new
    {
        run.Id, run.ProjectId, run.Branch, run.Commit, run.AppVersion,
        run.Platform, run.MachineName, run.GeminiModel,
        run.StartedAt, run.FinishedAt, run.IsComplete,
        run.TotalTests, run.PassedTests, run.FailedTests,
        results
    });
}).RequireAuthorization();

// Delete a run (cascades to results, steps, errors)
app.MapDelete("/api/gemini-runs/{runId}", async (string runId, GeminiService gemini) =>
{
    return await gemini.DeleteRunAsync(runId) ? Results.Ok() : Results.NotFound();
}).RequireAuthorization();

// Get a single result with steps and errors
app.MapGet("/api/gemini-results/{resultId}", async (string resultId, GeminiService gemini) =>
{
    var result = await gemini.GetResultWithStepsAsync(resultId);
    return result == null ? Results.NotFound() : Results.Ok(result);
}).RequireAuthorization();

// Query results across all runs
app.MapGet("/api/gemini-results", async (
    HttpContext ctx, string? projectId, string? testName, string? result,
    string? appVersion, string? branch, DateTime? from, DateTime? to,
    int? page, int? pageSize, GeminiService gemini, AuthService auth) =>
{
    var scopedIds = await GetScopedProjectIds(ctx, auth, projectId);
    return Results.Ok(await gemini.QueryResultsAsync(
        scopedIds, testName, result, appVersion, branch, from, to, page ?? 1, pageSize ?? 20));
}).RequireAuthorization();

// ==================== Gemini Error Tracking ====================

// Query errors globally
app.MapGet("/api/gemini-errors", async (
    HttpContext ctx, string? projectId, string? testName, string? category,
    string? appVersion, string? search, DateTime? from, DateTime? to,
    int? page, int? pageSize, GeminiService gemini, AuthService auth) =>
{
    var scopedIds = await GetScopedProjectIds(ctx, auth, projectId);
    return Results.Ok(await gemini.QueryErrorsAsync(
        scopedIds, testName, category, appVersion, search, from, to, page ?? 1, pageSize ?? 50));
}).RequireAuthorization();

// Error frequency — top recurring errors
app.MapGet("/api/gemini-errors/frequency", async (
    HttpContext ctx, string? projectId, string? appVersion, int? top,
    GeminiService gemini, AuthService auth) =>
{
    var scopedIds = await GetScopedProjectIds(ctx, auth, projectId);
    return Results.Ok(await gemini.GetErrorFrequencyAsync(scopedIds, appVersion, top ?? 20));
}).RequireAuthorization();

// ==================== Gemini Stats ====================

// Aggregate stats — pass rates, per-test, per-version, error breakdown
app.MapGet("/api/gemini-stats", async (
    HttpContext ctx, string? projectId, GeminiService gemini, AuthService auth) =>
{
    var scopedIds = await GetScopedProjectIds(ctx, auth, projectId);
    return Results.Ok(await gemini.GetStatsAsync(scopedIds));
}).RequireAuthorization();

// Health timeline — pass rate over time
app.MapGet("/api/gemini-stats/timeline", async (
    HttpContext ctx, string? projectId, string? testName, int? days,
    GeminiService gemini, AuthService auth) =>
{
    var scopedIds = await GetScopedProjectIds(ctx, auth, projectId);
    return Results.Ok(await gemini.GetHealthTimelineAsync(scopedIds, testName, days ?? 30));
}).RequireAuthorization();

// Distinct versions from Gemini results
app.MapGet("/api/gemini-versions", async (
    HttpContext ctx, string? projectId, GeminiService gemini, AuthService auth) =>
{
    var scopedIds = await GetScopedProjectIds(ctx, auth, projectId);
    return Results.Ok(await gemini.GetVersionsAsync(scopedIds));
}).RequireAuthorization();

// ==================== Gemini Proxy (device doesn't need API key) ====================

// Proxy a Gemini API call — device sends screenshot + conversation, server calls Gemini
app.MapPost("/api/gemini-proxy/generate", async (HttpContext ctx, GeminiProxyRequest req) =>
{
    var geminiKey = app.Configuration["Gemini:ApiKey"];
    if (string.IsNullOrEmpty(geminiKey))
        return Results.BadRequest(new { error = "Gemini API key not configured on server. Set Gemini:ApiKey in appsettings.json." });

    var model = req.Model ?? "gemini-2.0-flash";
    var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={geminiKey}";

    using var httpClient = new HttpClient();
    httpClient.Timeout = TimeSpan.FromSeconds(30);

    var body = System.Text.Json.JsonSerializer.Serialize(new
    {
        contents = req.Contents,
        systemInstruction = req.SystemInstruction,
        generationConfig = req.GenerationConfig
    });

    var response = await httpClient.PostAsync(url,
        new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

    var responseBody = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
        return Results.Json(new { error = $"Gemini API error: {(int)response.StatusCode}", body = responseBody },
            statusCode: (int)response.StatusCode);

    // Return the raw Gemini response — client parses it
    return Results.Content(responseBody, "application/json");
}).RequireAuthorization();

// Upload video for a Gemini result
app.MapPost("/api/gemini-runs/{runId}/results/{resultId}/video", async (
    string runId, string resultId, HttpRequest req, AppDbContext db) =>
{
    var result = await db.GeminiResults.FindAsync(resultId);
    if (result == null || result.RunId != runId)
        return Results.NotFound();

    var videoDir = Path.Combine(
        app.Configuration["SessionStorage:Path"] ?? "data/sessions",
        "gemini_videos");
    Directory.CreateDirectory(videoDir);

    var videoPath = Path.Combine(videoDir, $"{resultId}.mp4");

    await using var fs = File.Create(videoPath);
    await req.Body.CopyToAsync(fs);

    // Store video path reference (reuse FailReason field or add to custom metadata)
    // For now, store as a convention: video is at gemini_videos/{resultId}.mp4
    return Results.Ok(new { videoPath = $"gemini_videos/{resultId}.mp4", size = new FileInfo(videoPath).Length });
}).RequireAuthorization()
  .DisableAntiforgery();

// Stream/download video for a Gemini result
app.MapGet("/api/gemini-runs/{runId}/results/{resultId}/video", async (
    string runId, string resultId, AppDbContext db) =>
{
    var result = await db.GeminiResults.FindAsync(resultId);
    if (result == null || result.RunId != runId)
        return Results.NotFound();

    var videoDir = Path.Combine(
        app.Configuration["SessionStorage:Path"] ?? "data/sessions",
        "gemini_videos");
    var videoPath = Path.Combine(videoDir, $"{resultId}.mp4");

    if (!File.Exists(videoPath))
        return Results.NotFound();

    return Results.File(videoPath, "video/mp4", enableRangeProcessing: true);
}).RequireAuthorization();

// SPA fallback
app.MapFallbackToFile("index.html");

static string GetContentType(string fileName)
{
    var ext = Path.GetExtension(fileName).ToLowerInvariant();
    return ext switch
    {
        ".json" => "application/json",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".mp4" => "video/mp4",
        ".txt" => "text/plain",
        _ => "application/octet-stream"
    };
}

app.Run();

// ==================== Request Models ====================

record AuthRegisterRequest(string Email, string Password, string? DisplayName);
record AuthLoginRequest(string Email, string Password);
record ValidateKeyRequest(string Key);
record CreateOrgRequest(string Name);
record CreateProjectRequest(string Name);
record AddMemberRequest(string Email, string? Role, string? Password, string? DisplayName);
record CreateKeyRequest(string? Label);
record CreateGeminiTestRequest(string Name, string Prompt, string? Scene, int? MaxSteps, bool? Enabled, string? Tags, int? SortOrder);
record UpdateGeminiTestRequest(string? Name, string? Prompt, string? Scene, int? MaxSteps, bool? Enabled, string? Tags, int? SortOrder);
record StartGeminiRunRequest(string? Branch, string? Commit, string? AppVersion, string? Platform, string? MachineName, string? GeminiModel);
record UploadGeminiResultRequest(
    string TestName, string Prompt, bool Passed, string? TestDefinitionId,
    string? Scene, string? FailReason, int StepsExecuted, int MaxSteps,
    double DurationSeconds, string? AppVersion, string? GeminiModel,
    string? Branch, string? Platform, DateTime? StartedAt,
    List<UploadGeminiStepRequest>? Steps, List<UploadGeminiErrorRequest>? Errors);
record UploadGeminiStepRequest(int StepIndex, string? ActionJson, string? ModelResponse, bool Success, string? Error, double ElapsedMs);
record UploadGeminiErrorRequest(string? Category, string? Message, string? StackTrace, string? ActionJson, int StepIndex);
record GeminiProxyRequest(object[]? Contents, object? SystemInstruction, object? GenerationConfig, string? Model);
