using Mentor.Api.Audit;
using Mentor.Api.Authorisation;
using Mentor.Api.Identity;
using Mentor.Core.Agreements;
using Mentor.Core.Audit;
using Mentor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mentor.Tests.Unit.Audit;

// What the writer takes from the caller and what it refuses (ADR-007; AC-NFR-004-01, AC-FR-065-01). No database is
// involved: the entry is only added to the unit of work.
public sealed class AuditTrailTests
{
    private static MentorDbContext Context() =>
        new(new DbContextOptionsBuilder<MentorDbContext>().UseMentorDatabase("Host=localhost;Database=mentor").Options);

    private static Caller Caller(Guid? employee, IdentityScheme scheme = IdentityScheme.Entra) =>
        new(Guid.NewGuid(), "Lerato Khumalo", scheme, employee, new HashSet<FunctionalRole> { FunctionalRole.Employee, FunctionalRole.TalentManagement });

    [Theory]
    [InlineData(IdentityScheme.Entra)]
    [InlineData(IdentityScheme.Simulated)]
    public void An_entry_takes_its_actor_and_scheme_from_the_caller_and_is_an_application_event(IdentityScheme scheme)
    {
        using var db = Context();
        var employee = Guid.NewGuid();
        var candidate = Guid.NewGuid();
        var acceptance = Guid.NewGuid();
        var revision = new RevisionReference(Guid.NewGuid(), new string('a', 64));

        var entry = new AuditTrail(db).Append(Caller(employee, scheme), new AuditEvent(AuditAction.AcceptanceRecorded, AuditRecordType.AgreementAcceptance, acceptance)
        {
            CandidateId = candidate,
            Revision = revision,
            Outcome = AuditOutcome.Accepted,
            NewValues = new AuditValues().Set("participantEmployeeId", employee),
        });

        Assert.Equal(EntityState.Added, db.Entry(entry).State);
        Assert.NotEqual(Guid.Empty, entry.Id);
        Assert.Equal((employee, scheme, AuditProvenance.Application), (entry.ActorEmployeeId, entry.IdentityScheme, entry.Provenance));
        Assert.Equal((AuditAction.AcceptanceRecorded, AuditRecordType.AgreementAcceptance, (Guid?)acceptance), (entry.Action, entry.RecordType, entry.RecordId));
        Assert.Equal((candidate, revision.RevisionId, revision.ContentHash, "ACCEPTED"), (entry.CandidateId!.Value, entry.RevisionId!.Value, entry.ContentHash, entry.Outcome));
        Assert.Equal($$"""{"participantEmployeeId":"{{employee}}"}""", entry.NewValues);
        Assert.Null(entry.PriorValues);
        Assert.Null(entry.InvalidatedAcceptanceIds);
        Assert.Null(entry.InvalidatedDecisionIds);
        Assert.Null(entry.SourceFile);
        Assert.Null(entry.SourceRow);
        // The time is the database's to assign.
        Assert.Equal(default, entry.OccurredAt);
    }

    [Fact]
    public void A_caller_without_an_employee_record_cannot_be_an_actor()
    {
        using var db = Context();

        Assert.Throws<InvalidOperationException>(() =>
            new AuditTrail(db).Append(Caller(employee: null), new AuditEvent(AuditAction.MentorApplicationDecided, AuditRecordType.MentorProfile, Guid.NewGuid())));

        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(AuditOutcome.ResetToStep1)]
    [InlineData(AuditOutcome.Withdrawn)]
    public void A_reset_or_a_withdrawal_names_the_evidence_it_made_inapplicable(string outcome)
    {
        using var db = Context();
        var trail = new AuditTrail(db);
        var candidate = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() =>
            trail.Append(Caller(Guid.NewGuid()), new AuditEvent(AuditAction.CandidateWithdrawn, AuditRecordType.Candidate, candidate) { CandidateId = candidate, Outcome = outcome }));
        Assert.Empty(db.ChangeTracker.Entries());

        var nothing = trail.Append(Caller(Guid.NewGuid()),
            new AuditEvent(AuditAction.CandidateWithdrawn, AuditRecordType.Candidate, candidate) { CandidateId = candidate, Outcome = outcome, Invalidated = InapplicableEvidence.None });
        var acceptances = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var some = trail.Append(Caller(Guid.NewGuid()),
            new AuditEvent(AuditAction.RevisionCreated, AuditRecordType.AgreementRevision, Guid.NewGuid())
            {
                CandidateId = candidate,
                Outcome = outcome,
                Invalidated = new InapplicableEvidence(acceptances, []),
            });

        Assert.Equal(([], []), (nothing.InvalidatedAcceptanceIds!, nothing.InvalidatedDecisionIds!));
        Assert.Equal(acceptances, some.InvalidatedAcceptanceIds);
        Assert.Empty(some.InvalidatedDecisionIds!);
    }

    [Fact]
    public void A_revision_is_referenced_together_with_its_hash() =>
        Assert.Throws<ArgumentException>(() => new RevisionReference(Guid.NewGuid(), " "));
}
