using System.Text.Json;
using Mentor.Core.Agreements;
using Mentor.Core.Relationships;
using Mentor.Infrastructure.Persistence;

namespace Mentor.Tests.Unit.Agreements;

// CONTRACT-AGREEMENT-CONTENT: what a participant submits, how it is validated, and the canonical content document a
// revision stores and its hash identifies (FR-022 to FR-026, FR-055, BR-010, BR-011, BR-017, BR-018, DR-006).
public sealed class AgreementContentTests
{
    private static readonly AgreementParties Parties = new(
        new AgreementParty(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Thandiwe Mokoena"),
        new AgreementParty(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Johan van der Merwe"));

    private static readonly AgreementDraftForm Valid = new(
        new AgreementTermsForm(DurationCategory.TwelveMonths, new DateOnly(2026, 12, 1), new DateOnly(2027, 11, 30), 2, 45,
            new ContactLimitForm(1, ContactUnit.Weeks), "Whoever cannot attend proposes a new date.", new DateOnly(2027, 3, 1)),
        ["Lead a design review by June 2027", "Present at the architecture forum"],
        ["Delivery deadlines"],
        ["Move the meeting rather than skip it"],
        new AgreementResponsibilitiesForm(["Share experience of leading reviews"], ["Prepare an agenda for each meeting"]));

    [Fact]
    public void A_complete_form_makes_the_details_it_states()
    {
        Assert.True(AgreementDraftDetails.TryCreate(Valid, out var details, out var errors));

        Assert.Empty(errors);
        Assert.Equal(
            new AgreementTerms(DurationCategory.TwelveMonths, new DateOnly(2026, 12, 1), new DateOnly(2027, 11, 30), 2, 45,
                new ContactLimit(1, ContactUnit.Weeks), "Whoever cannot attend proposes a new date.", new DateOnly(2027, 3, 1)),
            details.Terms);
        Assert.Equal(["Lead a design review by June 2027", "Present at the architecture forum"], details.Goals);
        Assert.Equal(["Delivery deadlines"], details.Barriers);
        Assert.Equal(["Move the meeting rather than skip it"], details.Contingencies);
        Assert.Equal(["Share experience of leading reviews"], details.Responsibilities.Mentor);
        Assert.Equal(["Prepare an agenda for each meeting"], details.Responsibilities.Protege);
    }

    // AC-FR-023-01, AC-FR-023-02, AC-FR-024-01, AC-BR-010-01, AC-BR-011-01: every duration category, with either date,
    // both or neither; a 12+ agreement needs no end date. The other terms are optional too: "where supplied" (FR-025).
    [Theory]
    [InlineData(DurationCategory.SixMonths)]
    [InlineData(DurationCategory.TwelveMonths)]
    [InlineData(DurationCategory.TwelvePlusMonths)]
    public void Every_duration_category_stands_with_either_date_both_or_neither(DurationCategory category)
    {
        DateOnly? start = new DateOnly(2026, 12, 1);
        DateOnly? end = new DateOnly(2027, 5, 31);
        foreach (var (from, until) in new (DateOnly?, DateOnly?)[] { (null, null), (start, null), (null, end), (start, end) })
        {
            var form = Valid with { Terms = new AgreementTermsForm(category, from, until, null, null, null, null, null) };

            Assert.True(AgreementDraftDetails.TryCreate(form, out var details, out _));
            Assert.Equal(new AgreementTerms(category, from, until, null, null, null, null, null), details.Terms);
        }
    }

    [Fact]
    public void Barriers_and_contingencies_may_be_none()
    {
        Assert.True(AgreementDraftDetails.TryCreate(Valid with { Barriers = null, Contingencies = [] }, out var details, out _));

        Assert.Empty(details.Barriers);
        Assert.Empty(details.Contingencies);
    }

    [Fact]
    public void The_text_written_is_normalised_so_the_same_words_make_the_same_content()
    {
        var form = Valid with
        {
            Goals = ["  Lead a design review\r\nby June  ", "Café talks\rmonthly"],
            Terms = Valid.Terms! with { MissedMeetingArrangements = "   " },
        };

        Assert.True(AgreementDraftDetails.TryCreate(form, out var details, out _));

        Assert.Equal(["Lead a design review\nby June", "Café talks\nmonthly"], details.Goals);
        Assert.Null(details.Terms.MissedMeetingArrangements);
    }

    // Every refusal is keyed by the member's path in the form, and no message repeats what was submitted.
    [Fact]
    public void An_incomplete_or_impossible_form_is_refused_member_by_member()
    {
        var terms = Valid.Terms!;
        var tooLong = new string('x', AgreementDraftDetails.MaxItemLength + 1);
        var cases = new (AgreementDraftForm Form, string Key)[]
        {
            (Valid with { Terms = null }, "terms"),
            (Valid with { Terms = terms with { DurationCategory = null } }, "terms.durationCategory"),
            (Valid with { Terms = terms with { DurationCategory = (DurationCategory)7 } }, "terms.durationCategory"),
            (Valid with { Terms = terms with { StartDate = new DateOnly(2027, 1, 2), EndDate = new DateOnly(2027, 1, 1) } }, "terms.endDate"),
            (Valid with { Terms = terms with { MeetingIntervalWeeks = 0 } }, "terms.meetingIntervalWeeks"),
            (Valid with { Terms = terms with { MeetingIntervalWeeks = AgreementDraftDetails.MaxMeetingIntervalWeeks + 1 } }, "terms.meetingIntervalWeeks"),
            (Valid with { Terms = terms with { ApproximateMinutesPerMeeting = 0 } }, "terms.approximateMinutesPerMeeting"),
            (Valid with { Terms = terms with { ApproximateMinutesPerMeeting = AgreementDraftDetails.MaxMinutesPerMeeting + 1 } }, "terms.approximateMinutesPerMeeting"),
            (Valid with { Terms = terms with { BetweenMeetingContact = new ContactLimitForm(null, ContactUnit.Days) } }, "terms.betweenMeetingContact.interval"),
            (Valid with { Terms = terms with { BetweenMeetingContact = new ContactLimitForm(0, ContactUnit.Days) } }, "terms.betweenMeetingContact.interval"),
            (Valid with { Terms = terms with { BetweenMeetingContact = new ContactLimitForm(AgreementDraftDetails.MaxContactInterval + 1, ContactUnit.Days) } }, "terms.betweenMeetingContact.interval"),
            (Valid with { Terms = terms with { BetweenMeetingContact = new ContactLimitForm(1, null) } }, "terms.betweenMeetingContact.unit"),
            (Valid with { Terms = terms with { BetweenMeetingContact = new ContactLimitForm(1, (ContactUnit)9) } }, "terms.betweenMeetingContact.unit"),
            (Valid with { Terms = terms with { MissedMeetingArrangements = new string('x', AgreementDraftDetails.MaxArrangementsLength + 1) } }, "terms.missedMeetingArrangements"),
            (Valid with { Goals = null }, "goals"),
            (Valid with { Goals = [] }, "goals"),
            (Valid with { Goals = ["Lead a design review", "  "] }, "goals[1]"),
            (Valid with { Goals = [null] }, "goals[0]"),
            (Valid with { Goals = [tooLong] }, "goals[0]"),
            (Valid with { Goals = ["Lead a\u0000 design review"] }, "goals[0]"),
            (Valid with { Goals = ["\ud800"] }, "goals[0]"),
            (Valid with { Goals = [.. Enumerable.Repeat("A goal", AgreementDraftDetails.MaxListItems + 1)] }, "goals"),
            (Valid with { Barriers = [""] }, "barriers[0]"),
            (Valid with { Contingencies = [tooLong] }, "contingencies[0]"),
            (Valid with { Responsibilities = null }, "responsibilities.mentor"),
            (Valid with { Responsibilities = new AgreementResponsibilitiesForm([], ["Prepare an agenda"]) }, "responsibilities.mentor"),
            (Valid with { Responsibilities = new AgreementResponsibilitiesForm(["Share experience"], null) }, "responsibilities.protege"),
            (Valid with { Responsibilities = new AgreementResponsibilitiesForm(["Share experience"], ["\u0007"]) }, "responsibilities.protege[0]"),
        };

        var mismatches = new List<string>();
        foreach (var (form, key) in cases)
        {
            if (AgreementDraftDetails.TryCreate(form, out var details, out var errors) || details is not null || !errors.ContainsKey(key))
            {
                mismatches.Add($"{key}: [{string.Join(", ", errors.Keys)}]");
            }
            else if (errors.Values.SelectMany(messages => messages).Any(message => message.Contains("design review", StringComparison.Ordinal)))
            {
                mismatches.Add($"{key}: a message repeats what was submitted");
            }
        }

        Assert.Empty(mismatches);
        Assert.False(AgreementDraftDetails.TryCreate(null, out _, out var nothing));
        Assert.Equal(["goals", "responsibilities.mentor", "responsibilities.protege", "terms"], nothing.Keys.Order());
    }

    // The layout of the canonical document is the contract the hash depends on: the sections, the member names, dates as
    // yyyy-MM-dd, absent values as null and enumerations as the database stores them.
    [Fact]
    public void The_content_document_is_written_in_one_canonical_layout()
    {
        var content = new AgreementContent(
            Parties,
            [new PolicyClause(AgreementPolicy.Confidentiality, "Confidential.")],
            new AgreementTerms(DurationCategory.TwelvePlusMonths, new DateOnly(2026, 12, 1), null, 2, 45, new ContactLimit(1, ContactUnit.Weeks), "Tell the other a day ahead.", null),
            ["Lead a design review"],
            [],
            ["Move the meeting"],
            new AgreementResponsibilities(["Share experience"], ["Prepare an agenda"]));

        Assert.Equal(
            """{"barriers":[],"contingencies":["Move the meeting"],"goals":["Lead a design review"],"parties":{"mentor":{"displayName":"Johan van der Merwe","employeeId":"22222222-2222-2222-2222-222222222222"},"protege":{"displayName":"Thandiwe Mokoena","employeeId":"11111111-1111-1111-1111-111111111111"}},"policy":[{"clause":"CONFIDENTIALITY","text":"Confidential."}],"responsibilities":{"mentor":["Share experience"],"protege":["Prepare an agenda"]},"terms":{"approximateMinutesPerMeeting":45,"betweenMeetingContact":{"interval":1,"unit":"WEEKS"},"durationCategory":"TWELVE_PLUS_MONTHS","endDate":null,"meetingIntervalWeeks":2,"missedMeetingArrangements":"Tell the other a day ahead.","reviewDate":null,"startDate":"2026-12-01"}}""",
            content.ToCanonicalJson());
    }

    // AC-FR-022-01, AC-FR-022-02: a revision's content is the parties the server names, the fixed policy text of the
    // agreement and the entered values, in one canonical document whose hash is the content identity.
    [Fact]
    public void A_composed_content_holds_the_parties_the_policy_and_the_entered_values_and_parses_back_whole()
    {
        Assert.True(AgreementDraftDetails.TryCreate(Valid, out var details, out _));

        var content = AgreementContent.Compose(Parties, details);
        var canonical = content.ToCanonicalJson();
        var parsed = AgreementContent.Parse(canonical);

        Assert.Same(AgreementPolicy.Clauses, content.Policy);
        Assert.True(CanonicalJson.IsCanonical(canonical));
        Assert.Equal(64, AgreementContentHash.Compute(canonical).Length);
        using var document = JsonDocument.Parse(canonical);
        Assert.Equal(["barriers", "contingencies", "goals", "parties", "policy", "responsibilities", "terms"],
            document.RootElement.EnumerateObject().Select(member => member.Name));
        Assert.Equal(Parties, parsed.Parties);
        Assert.Equal(AgreementPolicy.Clauses, parsed.Policy);
        Assert.Equal(details.Terms, parsed.Terms);
        Assert.Equal(details.Goals, parsed.Goals);
        Assert.Equal(details.Barriers, parsed.Barriers);
        Assert.Equal(details.Contingencies, parsed.Contingencies);
        Assert.Equal(details.Responsibilities.Mentor, parsed.Responsibilities.Mentor);
        Assert.Equal(details.Responsibilities.Protege, parsed.Responsibilities.Protege);
        Assert.Equal(canonical, parsed.ToCanonicalJson());
    }

    // What a revision shows is always the whole of what its hash identifies: anything but an agreement content document in
    // exactly its canonical form is refused rather than shown in part.
    [Theory]
    [InlineData("""{"parties":{"protege":"P","mentor":"M"},"terms":{"durationCategory":"SIX_MONTHS"},"goals":["Lead a design review"],"revision":1}""")]
    [InlineData("""{"goals":[]}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void Anything_but_a_canonical_content_document_is_refused(string text)
    {
        Assert.Throws<FormatException>(() => AgreementContent.Parse(text));
    }

    [Fact]
    public void A_content_document_with_an_extra_member_an_unknown_value_or_another_layout_is_refused()
    {
        Assert.True(AgreementDraftDetails.TryCreate(Valid, out var details, out _));
        var canonical = AgreementContent.Compose(Parties, details).ToCanonicalJson();

        Assert.Throws<FormatException>(() => AgreementContent.Parse(canonical.Replace("\"barriers\":", "\"aside\":1,\"barriers\":", StringComparison.Ordinal)));
        Assert.Throws<FormatException>(() => AgreementContent.Parse(canonical.Replace("TWELVE_MONTHS", "TEN_MONTHS", StringComparison.Ordinal)));
        Assert.Throws<FormatException>(() => AgreementContent.Parse(canonical.Replace("\"goals\":", " \"goals\" : ", StringComparison.Ordinal)));
        Assert.Throws<FormatException>(() => AgreementContent.Parse(canonical.Replace("\"2026-12-01\"", "\"2026-12-1\"", StringComparison.Ordinal)));
    }

    // The content writes enumerations as the database stores them, by an explicit mapping that renaming a member cannot change.
    [Fact]
    public void Enumerations_are_written_as_the_database_stores_them()
    {
        Assert.All(Enum.GetValues<DurationCategory>(), category => Assert.Equal(EnumColumn.StoredValue(category), AgreementContent.StoredValue(category)));
        Assert.All(Enum.GetValues<ContactUnit>(), unit => Assert.Equal(EnumColumn.StoredValue(unit), AgreementContent.StoredValue(unit)));
    }

    // AC-FR-022-01, AC-FR-025-02, AC-FR-026-01, AC-FR-026-02, AC-BR-017-01, AC-BR-018-01: the fixed commitments every
    // agreement carries, each stated once.
    [Fact]
    public void The_policy_states_each_fixed_commitment_of_the_agreement_once()
    {
        Assert.Equal(
            [
                AgreementPolicy.VoluntaryPartnership, AgreementPolicy.SmartGoals, AgreementPolicy.AvoidCancelling, AgreementPolicy.AgreeNextMeeting,
                AgreementPolicy.RegularReview, AgreementPolicy.Confidentiality, AgreementPolicy.HonestConstructiveFeedback,
                AgreementPolicy.OpennessToFeedback, AgreementPolicy.AmicableConclusion,
            ],
            AgreementPolicy.Clauses.Select(clause => clause.Clause));
        Assert.All(AgreementPolicy.Clauses, clause => Assert.False(string.IsNullOrWhiteSpace(clause.Text)));
        var text = string.Join(" ", AgreementPolicy.Clauses.Select(clause => clause.Text));
        Assert.All(
            new[] { "voluntary", "SMART", "specific, measurable, achievable, relevant and time-bound", "development activities", "cancelling",
                "next meeting", "every three to six months", "confidential", "honest and constructive feedback", "open to receiving feedback", "amicable agreement" },
            phrase => Assert.Contains(phrase, text, StringComparison.Ordinal));
    }
}
