using Microsoft.EntityFrameworkCore;
using UIAutomation.Server.Models;

namespace UIAutomation.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<TestSession> TestSessions => Set<TestSession>();
    public DbSet<TestRun> TestRuns => Set<TestRun>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrgMember> OrgMembers => Set<OrgMember>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<GeminiTest> GeminiTests => Set<GeminiTest>();
    public DbSet<GeminiRun> GeminiRuns => Set<GeminiRun>();
    public DbSet<GeminiResult> GeminiResults => Set<GeminiResult>();
    public DbSet<GeminiStep> GeminiSteps => Set<GeminiStep>();
    public DbSet<GeminiError> GeminiErrors => Set<GeminiError>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // TestSession
        var session = modelBuilder.Entity<TestSession>();
        session.HasKey(e => e.Id);
        session.HasIndex(e => e.TestName);
        session.HasIndex(e => e.Result);
        session.HasIndex(e => e.Project);
        session.HasIndex(e => e.Branch);
        session.HasIndex(e => e.StartTime);
        session.HasIndex(e => e.CreatedAt);
        session.HasIndex(e => e.ProjectId);
        session.HasIndex(e => e.RunId);

        // TestRun
        var testRun = modelBuilder.Entity<TestRun>();
        testRun.HasKey(e => e.Id);
        testRun.HasIndex(e => e.ProjectId);
        testRun.HasIndex(e => e.StartedAt);

        // User
        var user = modelBuilder.Entity<User>();
        user.HasKey(e => e.Id);
        user.HasIndex(e => e.Email).IsUnique();

        // Organization
        var org = modelBuilder.Entity<Organization>();
        org.HasKey(e => e.Id);
        org.HasIndex(e => e.Name).IsUnique();

        // OrgMember
        var member = modelBuilder.Entity<OrgMember>();
        member.HasKey(e => e.Id);
        member.HasIndex(e => new { e.OrgId, e.UserId }).IsUnique();
        member.HasOne(e => e.Organization).WithMany().HasForeignKey(e => e.OrgId);
        member.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);

        // Project
        var project = modelBuilder.Entity<Project>();
        project.HasKey(e => e.Id);
        project.HasIndex(e => new { e.OrgId, e.Name }).IsUnique();
        project.HasOne(e => e.Organization).WithMany().HasForeignKey(e => e.OrgId);

        // ApiKey
        var apiKey = modelBuilder.Entity<ApiKey>();
        apiKey.HasKey(e => e.Id);
        apiKey.HasIndex(e => e.KeyHash);
        apiKey.HasIndex(e => e.ProjectId);
        apiKey.HasOne(e => e.Project).WithMany().HasForeignKey(e => e.ProjectId);
        apiKey.HasOne(e => e.CreatedBy).WithMany().HasForeignKey(e => e.CreatedByUserId);

        // GeminiTest
        var geminiTest = modelBuilder.Entity<GeminiTest>();
        geminiTest.HasKey(e => e.Id);
        geminiTest.HasIndex(e => e.ProjectId);
        geminiTest.HasIndex(e => e.Name);
        geminiTest.HasOne(e => e.Project).WithMany().HasForeignKey(e => e.ProjectId);

        // GeminiRun
        var geminiRun = modelBuilder.Entity<GeminiRun>();
        geminiRun.HasKey(e => e.Id);
        geminiRun.HasIndex(e => e.ProjectId);
        geminiRun.HasIndex(e => e.StartedAt);
        geminiRun.HasIndex(e => e.AppVersion);
        geminiRun.HasOne(e => e.Project).WithMany().HasForeignKey(e => e.ProjectId);
        geminiRun.HasOne(e => e.CreatedBy).WithMany().HasForeignKey(e => e.CreatedByUserId);

        // GeminiResult
        var geminiResult = modelBuilder.Entity<GeminiResult>();
        geminiResult.HasKey(e => e.Id);
        geminiResult.HasIndex(e => e.RunId);
        geminiResult.HasIndex(e => e.TestName);
        geminiResult.HasIndex(e => e.Result);
        geminiResult.HasIndex(e => e.AppVersion);
        geminiResult.HasOne(e => e.Run).WithMany().HasForeignKey(e => e.RunId);

        // GeminiStep
        var geminiStep = modelBuilder.Entity<GeminiStep>();
        geminiStep.HasKey(e => e.Id);
        geminiStep.HasIndex(e => e.ResultId);
        geminiStep.HasOne(e => e.Result).WithMany().HasForeignKey(e => e.ResultId);

        // GeminiError
        var geminiError = modelBuilder.Entity<GeminiError>();
        geminiError.HasKey(e => e.Id);
        geminiError.HasIndex(e => e.ResultId);
        geminiError.HasIndex(e => e.ProjectId);
        geminiError.HasIndex(e => e.TestName);
        geminiError.HasIndex(e => e.Category);
        geminiError.HasIndex(e => e.AppVersion);
        geminiError.HasIndex(e => e.Timestamp);
        geminiError.HasOne(e => e.Result).WithMany().HasForeignKey(e => e.ResultId);
        geminiError.HasOne(e => e.Project).WithMany().HasForeignKey(e => e.ProjectId);
    }
}
