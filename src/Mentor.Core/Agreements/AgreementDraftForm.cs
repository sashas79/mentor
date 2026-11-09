using System.Text.Json.Serialization;
using Mentor.Core.Relationships;

namespace Mentor.Core.Agreements;

/// <summary>
/// What a participant submits as a revision of the draft (ADR-004): a complete replacement of the entered values, and
/// nothing else. The parties and the policy text are the server's to write, the revision number, its parent and its
/// hash follow from the draft, and no form has a place for a signature, a scanned document or anything said in a meeting
/// (BR-012, BR-018): a body naming any other member, at any level, is refused as malformed rather than quietly stripped.
/// <see cref="AgreementDraftDetails.TryCreate"/> validates it.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgreementDraftForm(
    AgreementTermsForm? Terms,
    IReadOnlyList<string?>? Goals,
    IReadOnlyList<string?>? Barriers,
    IReadOnlyList<string?>? Contingencies,
    AgreementResponsibilitiesForm? Responsibilities);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgreementTermsForm(
    DurationCategory? DurationCategory,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int? MeetingIntervalWeeks,
    int? ApproximateMinutesPerMeeting,
    ContactLimitForm? BetweenMeetingContact,
    string? MissedMeetingArrangements,
    DateOnly? ReviewDate);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ContactLimitForm(int? Interval, ContactUnit? Unit);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgreementResponsibilitiesForm(IReadOnlyList<string?>? Mentor, IReadOnlyList<string?>? Protege);
