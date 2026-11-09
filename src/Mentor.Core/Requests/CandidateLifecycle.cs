namespace Mentor.Core.Requests;

/// <summary>Which of a candidate's two identified participants an employee is, if either (ADR-015).</summary>
public enum Participant
{
    None,
    Protege,
    Mentor,
}

/// <summary>
/// The decisions a candidate admits in each status (ADR-015). A command checks them against the head it reads after
/// taking the candidate's version lock (ADR-005), so no concurrent command can change that head before it commits.
/// </summary>
public static class CandidateLifecycle
{
    public static Participant ParticipantOf(MentorSelection candidate, Guid employeeId)
    {
        if (employeeId == candidate.ProtegeEmployeeId)
        {
            return Participant.Protege;
        }

        return employeeId == candidate.MentorEmployeeId ? Participant.Mentor : Participant.None;
    }

    /// <summary>The mentor accepts or declines an invitation once, while it is pending.</summary>
    public static bool AdmitsResponse(MentorSelection candidate) => candidate.Status == CandidateStatus.Invited;

    /// <summary>
    /// A participant records their own willingness to proceed once, and only while the mentor's acceptance stands and
    /// mutual willingness is not yet complete (ACCEPTED). Willingness is affirmative only: there is nothing to revoke,
    /// and a participant who no longer wishes to proceed withdraws.
    /// </summary>
    public static bool AdmitsWillingness(MentorSelection candidate, Participant participant) =>
        candidate.Status == CandidateStatus.Accepted && participant switch
        {
            Participant.Protege => candidate.ProtegeWillingAt is null,
            Participant.Mentor => candidate.MentorWillingAt is null,
            _ => false,
        };

    /// <summary>
    /// Whether <paramref name="participant"/>'s willingness is the second one, which moves the candidate on to DRAFTING
    /// in the same command: one participant's willingness alone is never mutual acceptance (FR-020).
    /// </summary>
    public static bool CompletesMutualWillingness(MentorSelection candidate, Participant participant) => participant switch
    {
        Participant.Protege => candidate.MentorWillingAt is not null,
        Participant.Mentor => candidate.ProtegeWillingAt is not null,
        _ => false,
    };

    /// <summary>Either participant withdraws before formation: from INVITED, ACCEPTED or DRAFTING. Withdrawal is terminal.</summary>
    public static bool AdmitsWithdrawal(MentorSelection candidate) =>
        candidate.Status is CandidateStatus.Invited or CandidateStatus.Accepted or CandidateStatus.Drafting;
}
