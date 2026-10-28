namespace Mentor.Core.Requests;

/// <summary>The lifecycle of a candidate (ADR-015).</summary>
public enum CandidateStatus
{
    Invited,
    Declined,
    Accepted,
    Drafting,
    Formed,
    Withdrawn,
}

public enum MentorResponse
{
    Pending,
    Accepted,
    Declined,
}

/// <summary>
/// The candidate (DR-004, ADR-015): one request paired with one discoverable mentor. Its participants are fixed when it
/// is created; the row carries the pairing decisions, the current agreement revision, the administrative annex
/// (ADR-004) and the version used by the concurrency protocol (ADR-005).
/// </summary>
public sealed class MentorSelection
{
    public Guid Id { get; init; }

    public Guid RequestId { get; init; }

    public Guid ProtegeEmployeeId { get; init; }

    public Guid MentorEmployeeId { get; init; }

    public CandidateStatus Status { get; set; }

    public MentorResponse MentorResponse { get; set; }

    public DateTimeOffset? MentorRespondedAt { get; set; }

    public DateTimeOffset? ProtegeWillingAt { get; set; }

    public DateTimeOffset? MentorWillingAt { get; set; }

    public Guid? WithdrawnByEmployeeId { get; set; }

    public DateTimeOffset? WithdrawnAt { get; set; }

    public Guid? CurrentRevisionId { get; set; }

    /// <summary>Administrative annex: not agreement content, never hashed or accepted (ADR-004).</summary>
    public string? AdminReference { get; set; }

    /// <summary>Administrative annex: not agreement content, never hashed or accepted (ADR-004).</summary>
    public string? AdminRemarks { get; set; }

    public long Version { get; set; }

    public DateTimeOffset InvitedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
