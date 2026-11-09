using Mentor.Core.Requests;

namespace Mentor.Core.Agreements;

/// <summary>
/// The agreement workflow of a candidate (ADR-004, WF-03): Step 1 draft preparation, Step 2 one participant has accepted
/// the current revision, Step 3 both have and the draft awaits the Talent Management formation decision, Step 4 formed.
/// The step is never stored: it is derived from the candidate head and the acceptances, so a replacement revision returns
/// the draft to Step 1 by moving the current revision alone, and no acceptance or decision is ever touched.
/// </summary>
public static class AgreementWorkflow
{
    public const int DraftPreparation = 1;
    public const int OneAcceptance = 2;
    public const int BothAccepted = 3;
    public const int Formed = 4;

    /// <summary>
    /// Whether the candidate admits a draft operation: a revision, an acceptance or an administrative annex change. Only a
    /// DRAFTING candidate does (ADR-015): before mutual willingness there is no draft, and a formed or withdrawn candidate
    /// accepts no further draft operation (COMP-AGREEMENT-DRAFTING).
    /// </summary>
    public static bool AdmitsDraftChange(MentorSelection candidate) => candidate.Status == CandidateStatus.Drafting;

    /// <summary>
    /// The step of the candidate's draft: for a DRAFTING candidate, 1 without a current revision or with no applicable
    /// acceptance, 2 with one participant's, 3 with both; 4 once formed; none before drafting or after withdrawal. Only the
    /// identified participants' acceptances of the current revision count (BR-019): an acceptance of a superseded
    /// revision, or anyone else's, never does.
    /// </summary>
    public static int? StepOf(MentorSelection candidate, IEnumerable<AgreementAcceptance> acceptances)
    {
        switch (candidate.Status)
        {
            case CandidateStatus.Formed:
                return Formed;
            case CandidateStatus.Drafting when candidate.CurrentRevisionId is { } current:
                var accepted = acceptances
                    .Where(acceptance => acceptance.RevisionId == current)
                    .Select(acceptance => acceptance.ParticipantEmployeeId)
                    .Where(participant => participant == candidate.ProtegeEmployeeId || participant == candidate.MentorEmployeeId)
                    .Distinct()
                    .Count();
                return DraftPreparation + accepted;
            case CandidateStatus.Drafting:
                return DraftPreparation;
            default:
                return null;
        }
    }
}
