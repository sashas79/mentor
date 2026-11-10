using Mentor.Core.Legacy;
using Mentor.Core.Relationships;

namespace Mentor.Api.Register;

/// <summary>
/// How the register's rows are derived from stored records (ADR-011). Each value is copied as stored and nothing is
/// inferred. The register lists historical rows first, in tracker order (source file, then row), whether entered or
/// held, and then digitally formed relationships in the order they were formed.
/// </summary>
internal static class RegisterEntries
{
    /// <summary>The row of an entered relationship, with its participants as they now read.</summary>
    public static RegisterEntryView Of(MentorshipRelationship relationship, RegisterPersonView mentor, RegisterPersonView protege) => new(
        RegisterEntryKind.Relationship,
        relationship.Id,
        null,
        relationship.Provenance,
        mentor,
        protege,
        relationship.State,
        relationship.Status,
        relationship.IsActive,
        relationship.StartDate,
        relationship.EndDate,
        relationship.DurationCategory,
        SourceOf(relationship),
        ReviewOf(relationship));

    /// <summary>The row of a tracker row held out of the relationship table: its source text and its reason only.</summary>
    public static RegisterEntryView Held(LegacyReviewException exception)
    {
        if (exception.RelationshipId is not null)
        {
            throw new ArgumentException("An unresolved value belongs to its relationship's row; only a held source row has a row of its own.", nameof(exception));
        }

        return new RegisterEntryView(
            RegisterEntryKind.HeldSourceRow,
            null,
            exception.Id,
            Provenance.Legacy,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            new RegisterSourceView(exception.SourceFile, exception.SourceRow, exception.SourceMentorText, exception.SourceProtegeText,
                exception.SourceActiveText, exception.SourceDurationText, exception.SourceStartText, exception.SourceEndText),
            new RegisterReviewView(exception.Reason, []));
    }

    /// <summary>
    /// What remains unresolved on an entered relationship: the attributes of a legacy row that still hold no value, in
    /// the order State, Status and IsActive. It is derived from the row as it now reads, never from the exceptions recorded
    /// at entry. A digital row always holds all three attributes, and none for a legacy row whose values are all set.
    /// </summary>
    public static RegisterReviewView? ReviewOf(MentorshipRelationship relationship)
    {
        if (relationship.Provenance != Provenance.Legacy)
        {
            return null;
        }

        var unresolved = new List<RelationshipAttribute>();
        if (relationship.State is null)
        {
            unresolved.Add(RelationshipAttribute.State);
        }

        if (relationship.Status is null)
        {
            unresolved.Add(RelationshipAttribute.Status);
        }

        if (relationship.IsActive is null)
        {
            unresolved.Add(RelationshipAttribute.IsActive);
        }

        return unresolved.Count == 0 ? null : new RegisterReviewView(LegacyReviewReason.UnresolvedValue, unresolved);
    }

    /// <summary>The register's order: historical rows by source file and row, then digital rows by formation time and id.</summary>
    public static IReadOnlyList<RegisterEntryView> Ordered(IEnumerable<(RegisterEntryView Entry, DateTimeOffset CreatedAt)> rows) =>
        [.. rows
            .OrderBy(row => row.Entry.Source is null ? 1 : 0)
            .ThenBy(row => row.Entry.Source?.File, StringComparer.Ordinal)
            .ThenBy(row => row.Entry.Source?.Row)
            .ThenBy(row => row.CreatedAt)
            .ThenBy(row => row.Entry.RelationshipId ?? row.Entry.ReviewExceptionId)
            .Select(row => row.Entry)];

    private static RegisterSourceView? SourceOf(MentorshipRelationship relationship) =>
        relationship is { Provenance: Provenance.Legacy, SourceFile: { } file, SourceRow: { } row }
            ? new RegisterSourceView(file, row, relationship.SourceMentorText, relationship.SourceProtegeText, relationship.SourceActiveText,
                relationship.SourceDurationText, relationship.SourceStartText, relationship.SourceEndText)
            : null;
}
