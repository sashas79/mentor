using Mentor.Core.Agreements;
using Mentor.Core.Audit;
using Mentor.Core.Employees;
using Mentor.Core.Formation;
using Mentor.Core.Goals;
using Mentor.Core.Legacy;
using Mentor.Core.Meetings;
using Mentor.Core.MentorRegistry;
using Mentor.Core.Notifications;
using Mentor.Core.Relationships;
using Mentor.Core.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Mentor.Infrastructure.Persistence;

/// <summary>
/// The EF Core mapping of the authoritative schema (CONTRACT-DATABASE-SCHEMA). Insert-only evidence: agreement
/// revisions and acceptances, permitted changes, formation decisions, formed agreements, goal progress and approvals,
/// audit entries and the legacy tables. Operational rows, updated but never deleted: employees, mentor profiles,
/// requests, candidates, relationships, goals, meetings and notification events.
/// </summary>
public sealed class MentorDbContext(DbContextOptions<MentorDbContext> options) : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<MentorProfile> MentorProfiles => Set<MentorProfile>();

    public DbSet<MentorshipRequest> MentorshipRequests => Set<MentorshipRequest>();

    public DbSet<MentorSelection> MentorSelections => Set<MentorSelection>();

    public DbSet<AgreementRevision> AgreementRevisions => Set<AgreementRevision>();

    public DbSet<AgreementAcceptance> AgreementAcceptances => Set<AgreementAcceptance>();

    public DbSet<PermittedChange> PermittedChanges => Set<PermittedChange>();

    public DbSet<FormationDecision> FormationDecisions => Set<FormationDecision>();

    public DbSet<FormedAgreement> FormedAgreements => Set<FormedAgreement>();

    public DbSet<MentorshipRelationship> MentorshipRelationships => Set<MentorshipRelationship>();

    public DbSet<DevelopmentGoal> DevelopmentGoals => Set<DevelopmentGoal>();

    public DbSet<GoalProgress> GoalProgress => Set<GoalProgress>();

    public DbSet<GoalApproval> GoalApprovals => Set<GoalApproval>();

    public DbSet<Meeting> Meetings => Set<Meeting>();

    public DbSet<NotificationEvent> NotificationEvents => Set<NotificationEvent>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<LegacyEntryAuthorisation> LegacyEntryAuthorisations => Set<LegacyEntryAuthorisation>();

    public DbSet<LegacyReviewException> LegacyReviewExceptions => Set<LegacyReviewException>();

    // Every table and constraint name derives from the singular table names of the architecture (agreement_revision),
    // never from the plural DbSet names.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Conventions.Remove<TableNameFromDbSetConvention>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(MentorDatabase.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MentorDbContext).Assembly);

        // No row is ever deleted (ADR-013), so no reference cascades or clears.
        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetForeignKeys()))
        {
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}
