namespace Mentor.Core.Agreements;

/// <summary>
/// One immutable revision of a candidate's agreement draft (ADR-004). <see cref="ContentHash"/> is the one identity of
/// the content; a replacement is a new revision, never an edit.
/// </summary>
public sealed class AgreementRevision
{
    public Guid Id { get; init; }

    public Guid CandidateId { get; init; }

    public int RevisionNumber { get; init; }

    /// <summary>The revision this one replaces; none for the first.</summary>
    public Guid? ParentRevisionId { get; init; }

    /// <summary>The canonical JSON of the substantive content (CONTRACT-AGREEMENT-CONTENT).</summary>
    public required string Content { get; init; }

    /// <summary><see cref="AgreementContentHash.Compute"/> of <see cref="Content"/>; the database refuses any other value.</summary>
    public required string ContentHash { get; init; }

    public Guid CreatedByEmployeeId { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }
}
