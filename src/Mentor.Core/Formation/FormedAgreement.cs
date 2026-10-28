namespace Mentor.Core.Formation;

/// <summary>
/// The immutable agreement of a digitally formed relationship (ADR-004): the approved revision's content and hash, so
/// byte-identical to what both participants accepted, with the administrative annex values retained beside it.
/// </summary>
public sealed class FormedAgreement
{
    public Guid Id { get; init; }

    public Guid RelationshipId { get; init; }

    public Guid CandidateId { get; init; }

    public Guid RevisionId { get; init; }

    public required string Content { get; init; }

    public required string ContentHash { get; init; }

    public string? AdminReference { get; init; }

    public string? AdminRemarks { get; init; }

    public DateTimeOffset FormedAt { get; private set; }
}
