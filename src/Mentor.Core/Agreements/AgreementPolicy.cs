namespace Mentor.Core.Agreements;

/// <summary>One fixed commitment of the agreement: a stable identifier and the text both participants accept.</summary>
public sealed record PolicyClause(string Clause, string Text);

/// <summary>
/// The fixed policy text of the agreement (FR-022, FR-025, FR-026, BR-017, BR-018): the commitments of the existing
/// mentorship agreement that are the same for every pair, written by the server into every revision so that the content
/// each participant accepts includes them (AC-FR-022-02). They are commitments between the participants: nothing in
/// the application monitors, reminds about or enforces them, and no meeting discussion is recorded anywhere.
/// <para>
/// The source agreement (SRC-03) is cited by the BRS but not supplied with the project, so the wording states the
/// commitments the BRS attributes to it (AC-FR-022-01, AC-FR-025-02, AC-FR-026-01, AC-FR-026-02). Changing the wording
/// changes the content of every later revision, never of a revision already made: each revision keeps the text it was
/// accepted with.
/// </para>
/// </summary>
public static class AgreementPolicy
{
    public const string VoluntaryPartnership = "VOLUNTARY_PARTNERSHIP";
    public const string SmartGoals = "SMART_GOALS";
    public const string AvoidCancelling = "AVOID_CANCELLING";
    public const string AgreeNextMeeting = "AGREE_NEXT_MEETING";
    public const string RegularReview = "REGULAR_REVIEW";
    public const string Confidentiality = "CONFIDENTIALITY";
    public const string HonestConstructiveFeedback = "HONEST_CONSTRUCTIVE_FEEDBACK";
    public const string OpennessToFeedback = "OPENNESS_TO_FEEDBACK";
    public const string AmicableConclusion = "AMICABLE_CONCLUSION";

    /// <summary>The clauses in the order the agreement presents them.</summary>
    public static IReadOnlyList<PolicyClause> Clauses { get; } =
    [
        new(VoluntaryPartnership,
            "This mentoring partnership is voluntary: the mentor and the protégé named in this agreement each enter it by their own choice."),
        new(SmartGoals,
            "The protégé's development goals are set out individually in this agreement. Each goal is formed using SMART principles: specific, measurable, achievable, relevant and time-bound, and the development activities the participants agree work towards these goals."),
        new(AvoidCancelling,
            "The participants avoid cancelling a meeting except where it is unavoidable."),
        new(AgreeNextMeeting,
            "At each meeting the participants agree the date of their next meeting."),
        new(RegularReview,
            "The participants evaluate the mentoring relationship together every three to six months."),
        new(Confidentiality,
            "What the participants discuss in their meetings is confidential between them."),
        new(HonestConstructiveFeedback,
            "The mentor gives the protégé honest and constructive feedback."),
        new(OpennessToFeedback,
            "The protégé is open to receiving feedback."),
        new(AmicableConclusion,
            "The participants expect to conclude the relationship by amicable agreement once useful development has been achieved."),
    ];
}
