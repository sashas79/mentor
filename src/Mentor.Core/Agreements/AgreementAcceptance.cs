namespace Mentor.Core.Agreements;

/// <summary>
/// A participant's acceptance of one exact revision (ADR-004). Whether it still applies is derived from the candidate's
/// current revision and status; it is never stored.
/// </summary>
public sealed class AgreementAcceptance
{
    public Guid Id { get; init; }

    public Guid RevisionId { get; init; }

    /// <summary>The hash of the content shown to the participant; it must be the revision's own.</summary>
    public required string ContentHash { get; init; }

    public Guid ParticipantEmployeeId { get; init; }

    public DateTimeOffset AcceptedAt { get; private set; }
}
