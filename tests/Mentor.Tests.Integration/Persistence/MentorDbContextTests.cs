using Mentor.Core.Agreements;
using Mentor.Core.Audit;
using Mentor.Core.Employees;
using Mentor.Core.Formation;
using Mentor.Core.Goals;
using Mentor.Core.Meetings;
using Mentor.Core.MentorRegistry;
using Mentor.Core.Notifications;
using Mentor.Core.Relationships;
using Mentor.Core.Requests;
using Microsoft.EntityFrameworkCore;

namespace Mentor.Tests.Integration.Persistence;

// The EF Core mapping works under the application's own account and its column-level privileges.
public sealed class MentorDbContextTests(PostgresServer postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // AC-FR-017-01, AC-FR-064-01: every step of a digital mentorship is its own identifiable record, and the request
    // stays identifiable after formation.
    [Fact]
    public async Task A_digital_mentorship_is_recorded_step_by_step_as_separate_records()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        await using var context = db.Context(Login.App);

        var protege = new Employee { EntraObjectId = Guid.NewGuid(), DisplayName = "Thandi", LevelTitle = "Developer" };
        var mentor = new Employee { EntraObjectId = Guid.NewGuid(), DisplayName = "Sipho", LevelTitle = "Principal Engineer" };
        var administrator = new Employee { EntraObjectId = Guid.NewGuid(), DisplayName = "Talent Management" };
        context.Employees.AddRange(protege, mentor, administrator);
        await context.SaveChangesAsync(Cancellation);

        var profile = new MentorProfile { EmployeeId = mentor.Id, Biography = "Ten years of delivery", RoleTitle = "Principal Engineer" };
        context.MentorProfiles.Add(profile);
        await context.SaveChangesAsync(Cancellation);
        profile.ApprovalStatus = MentorApprovalStatus.Approved;
        profile.ApprovalDecidedByEmployeeId = administrator.Id;
        profile.ApprovalDecidedAt = DateTimeOffset.UtcNow;

        var request = new MentorshipRequest
        {
            ProtegeEmployeeId = protege.Id,
            Motivation = "To grow as an engineer",
            DevelopmentGoals = "Lead a design review",
            NontechnicalExpectations = "Confidence in presenting",
        };
        context.MentorshipRequests.Add(request);
        await context.SaveChangesAsync(Cancellation);

        var candidate = new MentorSelection { RequestId = request.Id, ProtegeEmployeeId = protege.Id, MentorEmployeeId = mentor.Id };
        context.MentorSelections.Add(candidate);
        await context.SaveChangesAsync(Cancellation);
        Assert.Equal(1, candidate.Version);

        candidate.MentorResponse = MentorResponse.Accepted;
        candidate.MentorRespondedAt = DateTimeOffset.UtcNow;
        candidate.Status = CandidateStatus.Accepted;
        await context.SaveChangesAsync(Cancellation);
        candidate.ProtegeWillingAt = DateTimeOffset.UtcNow;
        candidate.MentorWillingAt = DateTimeOffset.UtcNow;
        candidate.Status = CandidateStatus.Drafting;
        candidate.Version++;
        await context.SaveChangesAsync(Cancellation);

        var content = Seed.Content();
        var revision = new AgreementRevision
        {
            CandidateId = candidate.Id,
            RevisionNumber = 1,
            Content = content,
            ContentHash = AgreementContentHash.Compute(content),
            CreatedByEmployeeId = protege.Id,
        };
        context.AgreementRevisions.Add(revision);
        await context.SaveChangesAsync(Cancellation);
        candidate.CurrentRevisionId = revision.Id;
        context.AgreementAcceptances.AddRange(
            new AgreementAcceptance { RevisionId = revision.Id, ContentHash = revision.ContentHash, ParticipantEmployeeId = protege.Id },
            new AgreementAcceptance { RevisionId = revision.Id, ContentHash = revision.ContentHash, ParticipantEmployeeId = mentor.Id });
        await context.SaveChangesAsync(Cancellation);

        // Formation: one transaction (ADR-005).
        await using (var formation = await context.Database.BeginTransactionAsync(Cancellation))
        {
            context.FormationDecisions.Add(new FormationDecision
            {
                CandidateId = candidate.Id,
                RevisionId = revision.Id,
                ContentHash = revision.ContentHash,
                Outcome = FormationOutcome.Approved,
                AdministratorEmployeeId = administrator.Id,
            });
            candidate.Status = CandidateStatus.Formed;
            candidate.Version++;
            request.Disposition = RequestDisposition.Formed;
            var relationship = new MentorshipRelationship
            {
                Provenance = Provenance.Digital,
                CandidateId = candidate.Id,
                ProtegeEmployeeId = protege.Id,
                MentorEmployeeId = mentor.Id,
                State = RelationshipState.InProgress,
                Status = RelationshipStatus.Blank,
                IsActive = ActiveFlag.N,
                DurationCategory = DurationCategory.SixMonths,
            };
            context.MentorshipRelationships.Add(relationship);
            await context.SaveChangesAsync(Cancellation);
            var agreement = new FormedAgreement
            {
                RelationshipId = relationship.Id,
                CandidateId = candidate.Id,
                RevisionId = revision.Id,
                Content = revision.Content,
                ContentHash = revision.ContentHash,
                AdminReference = candidate.AdminReference,
            };
            context.FormedAgreements.Add(agreement);
            await context.SaveChangesAsync(Cancellation);
            context.DevelopmentGoals.Add(new DevelopmentGoal
            {
                RelationshipId = relationship.Id,
                FormedAgreementId = agreement.Id,
                Position = 1,
                AgreedDescription = "Lead a design review",
            });
            context.AuditEntries.Add(new AuditEntry
            {
                ActorEmployeeId = administrator.Id,
                IdentityScheme = IdentityScheme.Simulated,
                Action = AuditAction.FormationDecided,
                RecordType = "CANDIDATE",
                RecordId = candidate.Id,
                CandidateId = candidate.Id,
                RevisionId = revision.Id,
                ContentHash = revision.ContentHash,
                Outcome = "APPROVED",
                NewValues = """{"status": "FORMED"}""",
                Provenance = AuditProvenance.Application,
            });
            context.NotificationEvents.Add(new NotificationEvent
            {
                EventType = NotificationEventType.FormationDecided,
                SubjectType = "CANDIDATE",
                SubjectId = candidate.Id,
                RecipientEmployeeId = protege.Id,
                RecipientRole = RecipientRole.Protege,
                RequiredAction = "NONE",
                Delivery = NotificationDelivery.Simulated,
            });
            await context.SaveChangesAsync(Cancellation);
            await formation.CommitAsync(Cancellation);
        }

        var goal = await context.DevelopmentGoals.SingleAsync(Cancellation);
        var completionRequest = new GoalProgress
        {
            GoalId = goal.Id,
            RelationshipId = goal.RelationshipId,
            ProtegeEmployeeId = protege.Id,
            ProgressUpdate = "Ran the first design review",
            CompletionRequested = true,
        };
        context.GoalProgress.Add(completionRequest);
        context.Meetings.Add(new Meeting
        {
            Kind = MeetingKind.RecurringSession,
            RelationshipId = goal.RelationshipId,
            FrequencyWeeks = 2,
            DurationMinutes = 60,
            CreatedByEmployeeId = protege.Id,
        });
        await context.SaveChangesAsync(Cancellation);
        context.GoalApprovals.Add(new GoalApproval
        {
            ProgressId = completionRequest.Id,
            RelationshipId = goal.RelationshipId,
            MentorEmployeeId = mentor.Id,
            Decision = GoalDecision.Approved,
        });
        await context.SaveChangesAsync(Cancellation);
        goal.CompletionStatus = GoalCompletionStatus.Complete;
        await context.SaveChangesAsync(Cancellation);

        await using var reader = db.Context(Login.App);
        var storedRequest = await reader.MentorshipRequests.SingleAsync(Cancellation);
        var storedCandidate = await reader.MentorSelections.SingleAsync(Cancellation);
        var storedRelationship = await reader.MentorshipRelationships.SingleAsync(Cancellation);
        var storedAgreement = await reader.FormedAgreements.SingleAsync(Cancellation);
        var notification = await reader.NotificationEvents.SingleAsync(Cancellation);
        Assert.Equal(RequestDisposition.Formed, storedRequest.Disposition);
        Assert.Equal(storedRequest.Id, storedCandidate.RequestId);
        Assert.Equal((CandidateStatus.Formed, 3L), (storedCandidate.Status, storedCandidate.Version));
        Assert.Equal(storedCandidate.Id, storedRelationship.CandidateId);
        Assert.Equal((RelationshipState.InProgress, RelationshipStatus.Blank, ActiveFlag.N),
            (storedRelationship.State, storedRelationship.Status, storedRelationship.IsActive));
        Assert.Equal(revision.Content, storedAgreement.Content);
        Assert.Equal(GoalCompletionStatus.Complete, (await reader.DevelopmentGoals.SingleAsync(Cancellation)).CompletionStatus);
        Assert.Equal(notification.CreatedAt, notification.DeliveredAt);
        Assert.Equal(MentorApprovalStatus.Approved, (await reader.MentorProfiles.SingleAsync(Cancellation)).ApprovalStatus);
        Assert.NotEqual(default, storedRequest.SubmittedAt);
        Guid[] records = [storedRequest.Id, storedCandidate.Id, revision.Id, storedAgreement.Id, storedRelationship.Id, goal.Id, completionRequest.Id];
        Assert.Equal(records.Length, records.Distinct().Count());
    }

    // AC-FR-061-01: the register is reconstructed from the stored records of both provenances, with unresolved legacy
    // values left empty beside the retained source text.
    [Fact]
    public async Task The_register_is_reconstructed_from_stored_records()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var mentorship = await seed.FormedMentorshipAsync();
        await seed.AuthoriseLegacyEntryAsync(Login.Owner, mentorship.Administrator);
        await seed.LegacyRelationshipAsync(Login.Owner, mentorship.Administrator, mentorship.Protege, mentorship.Mentor, sourceRow: 8);
        await using var context = db.Context(Login.App);

        var register = await (
            from relationship in context.MentorshipRelationships
            join mentor in context.Employees on relationship.MentorEmployeeId equals mentor.Id
            join protege in context.Employees on relationship.ProtegeEmployeeId equals protege.Id
            orderby relationship.Provenance
            select new
            {
                Mentor = mentor.DisplayName,
                Protege = protege.DisplayName,
                relationship.State,
                relationship.Status,
                relationship.IsActive,
                relationship.Provenance,
                relationship.SourceRow,
                relationship.SourceActiveText,
            }).ToListAsync(Cancellation);

        Assert.Collection(register,
            digital => Assert.Equal(
                ("Mentor", "Protégé", RelationshipState.InProgress, RelationshipStatus.Blank, ActiveFlag.N, Provenance.Digital, (int?)null),
                (digital.Mentor, digital.Protege, digital.State!.Value, digital.Status!.Value, digital.IsActive!.Value, digital.Provenance, digital.SourceRow)),
            legacy =>
            {
                Assert.Equal((Provenance.Legacy, 8), (legacy.Provenance, legacy.SourceRow!.Value));
                Assert.Null(legacy.State);
                Assert.Null(legacy.Status);
                Assert.Null(legacy.IsActive);
                Assert.Equal("Successfully complete", legacy.SourceActiveText);
            });
    }
}
