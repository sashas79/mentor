using Mentor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mentor.Api.Requests;

/// <summary>
/// The first statement of every command on a candidate (ADR-005), pairing decisions and draft operations alike: the
/// version the caller decided on is claimed by advancing it. False means another command committed first and nothing
/// may be persisted. Otherwise the row lock the update takes holds every later read of the transaction to a state that
/// no concurrent command can change before it commits, so commands on one candidate run strictly one after another.
/// </summary>
internal static class CandidateVersion
{
    public static async Task<bool> ClaimAsync(MentorDbContext db, Guid candidateId, long version, CancellationToken cancellationToken) =>
        await db.MentorSelections
            .Where(row => row.Id == candidateId && row.Version == version)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Version, row => row.Version + 1), cancellationToken) == 1;
}
