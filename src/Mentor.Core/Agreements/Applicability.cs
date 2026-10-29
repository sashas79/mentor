using Mentor.Core.Requests;

namespace Mentor.Core.Agreements;

/// <summary>
/// Whether evidence bound to a revision (an acceptance, a formation decision) still applies (ADR-004). It is never
/// stored on the evidence: it is derived from the candidate head, so the evidence stays immutable while its standing
/// changes, and the trail records the event that changed it.
/// </summary>
public enum Applicability
{
    /// <summary>The revision is the candidate's current one and the candidate is DRAFTING: the evidence counts toward formation.</summary>
    Applicable,

    /// <summary>A later revision replaced the one the evidence names: it is history and counts for nothing.</summary>
    Superseded,

    /// <summary>The candidate was formed on this revision: the evidence is the record of that formation and applies to no further action.</summary>
    Formed,

    /// <summary>The candidate was withdrawn: the evidence applies to nothing.</summary>
    Withdrawn,
}

public static class ApplicabilityRule
{
    /// <summary>The standing of evidence bound to <paramref name="revisionId"/> under a candidate in <paramref name="status"/> whose current revision is <paramref name="currentRevisionId"/>.</summary>
    public static Applicability Of(CandidateStatus status, Guid? currentRevisionId, Guid revisionId)
    {
        if (revisionId != currentRevisionId)
        {
            return Applicability.Superseded;
        }

        return status switch
        {
            CandidateStatus.Drafting => Applicability.Applicable,
            CandidateStatus.Formed => Applicability.Formed,
            CandidateStatus.Withdrawn => Applicability.Withdrawn,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "A candidate holds a revision only from DRAFTING on."),
        };
    }
}
