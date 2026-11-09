using Mentor.Core.Agreements;
using Mentor.Core.Requests;

namespace Mentor.Core.Formation;

/// <summary>Why a formation decision cannot be taken on the revision it names (ADR-005).</summary>
public enum FormationBlock
{
    None,

    /// <summary>
    /// The candidate does not hold current mutual willingness: it is not DRAFTING, or the mutual acceptance columns do not
    /// all stand (before drafting, withdrawn or already formed).
    /// </summary>
    NotDrafting,

    /// <summary>The revision named is not the candidate's current revision: a replacement has superseded it.</summary>
    RevisionSuperseded,

    /// <summary>The content hash named is not the hash of the revision named.</summary>
    ContentMismatch,

    /// <summary>The two identified participants have not both accepted the revision named, with its hash.</summary>
    NotAccepted,
}

/// <summary>
/// The prerequisites of a formation decision (COMP-FORMATION, ADR-005, FR-030, FR-031, FR-070, BR-003), checked against
/// the candidate head read after the formation command has taken the candidate's version lock, so that nothing can change
/// between the check and the commit. A decision, approval or rejection, is taken on the draft at Step 3 only: the current
/// revision, which both identified participants accepted with its exact content hash, under current mutual willingness.
/// Nothing stored is trusted for this: the step, the applicability of an acceptance and the mutual acceptance invariant are
/// all re-derived here from the head and the acceptance rows.
/// </summary>
public static class FormationRules
{
    /// <summary>
    /// The mutual acceptance invariant of ADR-015, checked column by column rather than by the status alone (ADR-005 check
    /// 1): DRAFTING, the mentor's acceptance of the invitation, both participants' willingness, and no withdrawal.
    /// </summary>
    public static bool HoldsCurrentMutualWillingness(MentorSelection candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return candidate.Status == CandidateStatus.Drafting
            && candidate.MentorResponse == MentorResponse.Accepted
            && candidate.ProtegeWillingAt is not null
            && candidate.MentorWillingAt is not null
            && candidate.WithdrawnAt is null;
    }

    /// <summary>
    /// Whether the candidate's protégé and its mentor have each accepted <paramref name="revision"/> with the revision's
    /// own content hash (ADR-005 check 3). Only those two employees' acceptances of exactly that revision count: an
    /// acceptance of another revision, of other content or by anyone else counts for nothing (FR-031, BR-019).
    /// </summary>
    public static bool BothParticipantsAccepted(MentorSelection candidate, RevisionReference revision, IEnumerable<AgreementAcceptance> acceptances)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(acceptances);
        var accepted = acceptances
            .Where(acceptance => acceptance.RevisionId == revision.RevisionId
                && string.Equals(acceptance.ContentHash, revision.ContentHash, StringComparison.Ordinal))
            .Select(acceptance => acceptance.ParticipantEmployeeId)
            .ToHashSet();
        return accepted.Contains(candidate.ProtegeEmployeeId) && accepted.Contains(candidate.MentorEmployeeId);
    }

    /// <summary>
    /// What, if anything, prevents a formation decision on <paramref name="revision"/> of <paramref name="candidate"/>
    /// when the administrator names <paramref name="reviewedHash"/> as the content they reviewed. The checks run in the
    /// order of ADR-005: mutual willingness, then the currency of the revision and its hash, then both acceptances.
    /// </summary>
    public static FormationBlock Check(MentorSelection candidate, RevisionReference revision, string reviewedHash, IEnumerable<AgreementAcceptance> acceptances)
    {
        ArgumentNullException.ThrowIfNull(revision);
        if (!HoldsCurrentMutualWillingness(candidate))
        {
            return FormationBlock.NotDrafting;
        }

        if (candidate.CurrentRevisionId != revision.RevisionId)
        {
            return FormationBlock.RevisionSuperseded;
        }

        if (!string.Equals(revision.ContentHash, reviewedHash, StringComparison.Ordinal))
        {
            return FormationBlock.ContentMismatch;
        }

        return BothParticipantsAccepted(candidate, revision, acceptances) ? FormationBlock.None : FormationBlock.NotAccepted;
    }
}
