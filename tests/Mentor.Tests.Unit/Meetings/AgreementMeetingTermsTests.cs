using Mentor.Core.Agreements;

namespace Mentor.Tests.Unit.Meetings;

// FR-055, ADR-010: the agreed meeting frequency and duration are read from the accepted agreement content.
public sealed class AgreementMeetingTermsTests
{
    [Fact]
    public void The_terms_carry_the_agreed_interval_in_weeks_and_approximate_minutes_per_meeting()
    {
        const string content = """{"parties":{"protege":"P","mentor":"M"},"terms":{"durationCategory":"SIX_MONTHS","meetingIntervalWeeks":2,"approximateMinutesPerMeeting":45},"goals":[]}""";

        Assert.Equal(new AgreementMeetingTerms(2, 45), AgreementMeetingTerms.Of(content));
    }

    [Fact]
    public void The_property_names_are_the_contracts()
    {
        Assert.Equal("terms", AgreementMeetingTerms.TermsProperty);
        Assert.Equal("meetingIntervalWeeks", AgreementMeetingTerms.MeetingIntervalWeeksProperty);
        Assert.Equal("approximateMinutesPerMeeting", AgreementMeetingTerms.ApproximateMinutesPerMeetingProperty);
    }

    [Theory]
    [InlineData("""{"parties":{},"goals":[]}""")]
    [InlineData("""{"terms":"weekly"}""")]
    [InlineData("""{"terms":{"durationCategory":"SIX_MONTHS"}}""")]
    [InlineData("""{"terms":{"meetingIntervalWeeks":0,"approximateMinutesPerMeeting":-30}}""")]
    [InlineData("""{"terms":{"meetingIntervalWeeks":"two","approximateMinutesPerMeeting":45.5}}""")]
    [InlineData("""[1, 2]""")]
    [InlineData("not json")]
    public void A_document_without_positive_whole_number_terms_has_none(string content)
    {
        Assert.Equal(AgreementMeetingTerms.None, AgreementMeetingTerms.Of(content));
    }

    [Fact]
    public void Either_term_can_be_present_alone()
    {
        Assert.Equal(new AgreementMeetingTerms(3, null), AgreementMeetingTerms.Of("""{"terms":{"meetingIntervalWeeks":3}}"""));
        Assert.Equal(new AgreementMeetingTerms(null, 60), AgreementMeetingTerms.Of("""{"terms":{"approximateMinutesPerMeeting":60}}"""));
    }
}
