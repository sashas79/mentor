namespace Mentor.Core.Agreements;

/// <summary>
/// One revision together with the content hash that identifies its content (ADR-004): what an acceptance, a formation
/// decision and an audit entry name, always as a pair, so that every stored copy of a hash can be checked against the
/// revision's own.
/// </summary>
public sealed record RevisionReference(Guid RevisionId, string ContentHash)
{
    public string ContentHash { get; } = !string.IsNullOrWhiteSpace(ContentHash)
        ? ContentHash
        : throw new ArgumentException("A revision is referenced together with its content hash.", nameof(ContentHash));

    public static RevisionReference Of(AgreementRevision revision) => new(revision.Id, revision.ContentHash);
}
