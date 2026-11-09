using Mentor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mentor.Api.Relationships;

/// <summary>
/// The first statement of every relationship command (ADR-005): advance the relationship's version from the one the caller
/// decided on. No row updated means another command committed first. The row lock this takes is held until the command
/// commits, so competing edits of one relationship run one after the other and none overwrites another unseen (FR-050).
/// </summary>
internal static class RelationshipVersion
{
    public static async Task<bool> ClaimAsync(MentorDbContext db, Guid relationshipId, long version, CancellationToken cancellationToken) =>
        await db.MentorshipRelationships
            .Where(row => row.Id == relationshipId && row.Version == version)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Version, row => row.Version + 1), cancellationToken) == 1;
}
