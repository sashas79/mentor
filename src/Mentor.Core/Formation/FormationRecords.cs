using Mentor.Core.Agreements;
using Mentor.Core.Goals;
using Mentor.Core.Relationships;
using Mentor.Core.Requests;

namespace Mentor.Core.Formation;

/// <summary>
/// The records an approval forms (ADR-004, ADR-005, ADR-006), composed from the candidate and the approved revision alone,
/// so that no value an actor supplies reaches them: the relationship with its formation-time attributes, the formed
/// agreement that keeps the accepted content byte for byte, and one development goal per agreed goal.
/// </summary>
public static class FormationRecords
{
    /// <summary>
    /// The digital relationship formed from <paramref name="candidate"/>: its two identified participants, State In
    /// Progress, Status blank and IsActive N (FR-071: the default, which no actor supplies), and the duration and dates
    /// the accepted content states, either date possibly absent (FR-024).
    /// </summary>
    public static MentorshipRelationship Relationship(MentorSelection candidate, AgreementContent content)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(content);
        if (content.Parties.Protege.EmployeeId != candidate.ProtegeEmployeeId || content.Parties.Mentor.EmployeeId != candidate.MentorEmployeeId)
        {
            throw new ArgumentException("The accepted content names other parties than the candidate's participants.", nameof(content));
        }

        return new MentorshipRelationship
        {
            Provenance = Provenance.Digital,
            CandidateId = candidate.Id,
            ProtegeEmployeeId = candidate.ProtegeEmployeeId,
            MentorEmployeeId = candidate.MentorEmployeeId,
            State = RelationshipState.InProgress,
            Status = RelationshipStatus.Blank,
            IsActive = ActiveFlag.N,
            DurationCategory = content.Terms.DurationCategory,
            StartDate = content.Terms.StartDate,
            EndDate = content.Terms.EndDate,
        };
    }

    /// <summary>
    /// The formed agreement of the relationship: the approved revision's content and content hash, the same bytes both
    /// participants accepted, and the administrative annex as it stands at formation, kept beside the content and never
    /// inside it (ADR-004).
    /// </summary>
    public static FormedAgreement Agreement(MentorSelection candidate, AgreementRevision revision, Guid relationshipId)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(revision);
        if (revision.CandidateId != candidate.Id)
        {
            throw new ArgumentException("The revision is not one of the candidate's.", nameof(revision));
        }

        return new FormedAgreement
        {
            RelationshipId = relationshipId,
            CandidateId = candidate.Id,
            RevisionId = revision.Id,
            Content = revision.Content,
            ContentHash = revision.ContentHash,
            AdminReference = candidate.AdminReference,
            AdminRemarks = candidate.AdminRemarks,
        };
    }

    /// <summary>
    /// One goal per agreed goal of the accepted content, in its order from position 1, worded exactly as agreed and not yet
    /// complete (DR-009): only the mentor's approval of a completion request completes a goal.
    /// </summary>
    public static IReadOnlyList<DevelopmentGoal> Goals(AgreementContent content, Guid relationshipId, Guid formedAgreementId)
    {
        ArgumentNullException.ThrowIfNull(content);
        return
        [
            .. content.Goals.Select((goal, index) => new DevelopmentGoal
            {
                RelationshipId = relationshipId,
                FormedAgreementId = formedAgreementId,
                Position = index + 1,
                AgreedDescription = goal,
                CompletionStatus = GoalCompletionStatus.Incomplete,
            }),
        ];
    }
}
