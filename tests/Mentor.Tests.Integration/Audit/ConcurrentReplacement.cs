using System.Data.Common;
using Mentor.Core.Agreements;
using Mentor.Core.Formation;
using Mentor.Tests.Integration.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Mentor.Tests.Integration.Audit;

/// <summary>
/// A replacement of a candidate's draft committed in the middle of the API's own read of the candidate's evidence.
/// Hooked into the API's <see cref="MentorDbContext"/>, it waits for the first query the API runs against
/// <paramref name="afterReadingTable"/> and, once that query has executed and before the API reads anything further,
/// commits on a connection of its own what a participant's replacement command commits in one transaction (ADR-004):
/// the second revision, the candidate advanced to it, the mentor's acceptance of it and the administrator's rejection
/// of it. It fires once.
/// </summary>
internal sealed class ConcurrentReplacement(TestDatabase db, Guid candidate, Revision first, Guid mentor, Guid administrator, string afterReadingTable)
    : DbCommandInterceptor
{
    private int fired;
    private Revision? second;

    /// <summary>The revision the replacement made current; asking before it was committed is a mistake of the test.</summary>
    public Revision Second => second ?? throw new InvalidOperationException("The replacement has not been committed yet.");

    public Guid MentorAccepted { get; private set; }

    public Guid Rejected { get; private set; }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains(afterReadingTable, StringComparison.Ordinal) && Interlocked.Exchange(ref fired, 1) == 0)
        {
            await CommitAsync(cancellationToken);
        }

        return result;
    }

    private async Task CommitAsync(CancellationToken cancellationToken)
    {
        await using var context = db.Context(Login.App);
        await using var replacement = await context.Database.BeginTransactionAsync(cancellationToken);
        var head = await context.MentorSelections.SingleAsync(selection => selection.Id == candidate, cancellationToken);
        var content = Seed.Content(2);
        var revision = new AgreementRevision
        {
            CandidateId = candidate,
            RevisionNumber = 2,
            ParentRevisionId = first.Id,
            Content = content,
            ContentHash = AgreementContentHash.Compute(content),
            CreatedByEmployeeId = mentor,
        };
        context.AgreementRevisions.Add(revision);
        await context.SaveChangesAsync(cancellationToken);

        head.CurrentRevisionId = revision.Id;
        head.Version++;
        var acceptance = new AgreementAcceptance { RevisionId = revision.Id, ContentHash = revision.ContentHash, ParticipantEmployeeId = mentor };
        var rejection = new FormationDecision
        {
            CandidateId = candidate,
            RevisionId = revision.Id,
            ContentHash = revision.ContentHash,
            Outcome = FormationOutcome.Rejected,
            AdministratorEmployeeId = administrator,
        };
        context.AgreementAcceptances.Add(acceptance);
        context.FormationDecisions.Add(rejection);
        await context.SaveChangesAsync(cancellationToken);
        await replacement.CommitAsync(cancellationToken);

        MentorAccepted = acceptance.Id;
        Rejected = rejection.Id;
        second = new Revision(revision.Id, content, revision.ContentHash);
    }
}
