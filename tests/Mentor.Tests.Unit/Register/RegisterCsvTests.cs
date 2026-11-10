using System.Text;
using Mentor.Api.Register;
using Mentor.Core.Legacy;
using Mentor.Core.Relationships;

namespace Mentor.Tests.Unit.Register;

// CONTRACT-REGISTER-EXPORT, ADR-011, AC-FR-060-01, AC-FR-062-01, AC-FR-062-02: the export is UTF-8 with a byte-order mark,
// with the sixteen columns in order. Values are business values, dates are ISO 8601, and an unknown is an empty cell
// beside the source text, never an invented value. Provenance and the review exception tell an unresolved legacy value
// from a BLANK Status. A cell is never evaluated as a formula.
public sealed class RegisterCsvTests
{
    private static readonly RegisterPersonView Mentor = new(Guid.NewGuid(), "Thandi Nkosi", "Principal Engineer");
    private static readonly RegisterPersonView Protege = new(Guid.NewGuid(), "Zoë Mokoena", null);

    [Fact]
    public void The_file_is_utf8_with_a_byte_order_mark_and_names_the_contract_columns_in_order()
    {
        var bytes = RegisterCsv.Write([]);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal(
            "Mentor,Mentor level,Protégé,Protégé level,State,Status,IsActive,Start date,End date,Duration,Provenance,Source reference,"
            + "Source Active text,Source Duration text,Source date text,Review exception\r\n",
            Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public void A_digital_row_shows_its_business_values_and_no_source()
    {
        var entry = Digital(RelationshipState.InProgress, RelationshipStatus.NeedsReassigning, ActiveFlag.Y) with
        {
            StartDate = new DateOnly(2026, 3, 1),
            EndDate = new DateOnly(2026, 9, 1),
            DurationCategory = DurationCategory.SixMonths,
        };

        Assert.Equal(
            ["Thandi Nkosi", "Principal Engineer", "Zoë Mokoena", "", "In Progress", "Needs reassigning", "Y", "2026-03-01", "2026-09-01", "6 months",
                "Digital", "", "", "", "", ""],
            RegisterCsv.Fields(entry));
    }

    [Theory]
    [InlineData(RelationshipState.InProgress, "In Progress")]
    [InlineData(RelationshipState.OnHold, "On hold")]
    [InlineData(RelationshipState.Complete, "Complete")]
    public void Each_state_is_written_as_the_brs_names_it(RelationshipState state, string written)
    {
        Assert.Equal(written, RegisterCsv.Fields(Digital(state, RelationshipStatus.Blank, ActiveFlag.N))[4]);
    }

    [Theory]
    [InlineData(DurationCategory.SixMonths, "6 months")]
    [InlineData(DurationCategory.TwelveMonths, "12 months")]
    [InlineData(DurationCategory.TwelvePlusMonths, "12+ months")]
    public void Each_duration_category_is_written_as_br_010_names_it(DurationCategory duration, string written)
    {
        Assert.Equal(written, RegisterCsv.Fields(Digital(RelationshipState.Complete, RelationshipStatus.Blank, ActiveFlag.N) with { DurationCategory = duration })[9]);
    }

    // A BLANK Status and an unresolved one are both empty cells; only the provenance and the review exception tell them apart.
    [Fact]
    public void A_blank_status_and_an_unresolved_legacy_value_are_both_empty_and_told_apart_by_provenance_and_review()
    {
        var blank = RegisterCsv.Fields(Digital(RelationshipState.Complete, RelationshipStatus.Blank, ActiveFlag.N));
        var unresolved = RegisterCsv.Fields(Legacy(RelationshipState.Complete, status: null, isActive: null));

        Assert.Equal(("", "Digital", ""), (blank[5], blank[10], blank[15]));
        Assert.Equal(("", "", "Legacy", "Unresolved value: Status, IsActive"), (unresolved[5], unresolved[6], unresolved[10], unresolved[15]));
    }

    // AC-FR-062-01, AC-FR-062-02: an unknown date stays empty and the source text that was not a date is kept beside it.
    [Fact]
    public void A_legacy_row_keeps_unknown_dates_empty_beside_its_source_text()
    {
        var fields = RegisterCsv.Fields(Legacy(RelationshipState.Complete, status: null, isActive: null) with { StartDate = new DateOnly(2026, 1, 10) });

        Assert.Equal(
            ["Thandi Nkosi", "Principal Engineer", "Zoë Mokoena", "", "Complete", "", "", "2026-01-10", "", "", "Legacy",
                "Mentorship Tracker 2026.xlsx, row 8", "Successfully complete", "ongoing", "Start: 10/1/2026; End: ongoing", "Unresolved value: Status, IsActive"],
            fields);
    }

    [Theory]
    [InlineData(null, null, "")]
    [InlineData("", "ongoing", "End: ongoing")]
    [InlineData("10/1/2026", null, "Start: 10/1/2026")]
    [InlineData("March", "", "Start: March")]
    public void The_source_date_text_names_each_cell_the_source_gave(string? start, string? end, string written)
    {
        var entry = Legacy(RelationshipState.Complete, RelationshipStatus.Blank, ActiveFlag.N) with
        {
            Source = new RegisterSourceView("Tracker.xlsx", 3, "M", "P", null, null, start, end),
        };

        Assert.Equal(written, RegisterCsv.Fields(entry)[14]);
    }

    // A held row shows the names its source gives, its reason and its source text, and no resolved value.
    [Theory]
    [InlineData(LegacyReviewReason.UnresolvedPerson, "Unresolved person")]
    [InlineData(LegacyReviewReason.NoMentor, "No mentor")]
    public void A_held_source_row_shows_its_source_names_and_reason_and_nothing_resolved(LegacyReviewReason reason, string written)
    {
        var held = RegisterEntries.Held(new LegacyReviewException
        {
            Id = Guid.NewGuid(),
            SourceFile = "Mentorship Tracker 2026.xlsx",
            SourceRow = 9,
            Reason = reason,
            SourceMentorText = reason == LegacyReviewReason.NoMentor ? null : "J. Unknown",
            SourceProtegeText = "S. Dlamini",
            SourceActiveText = "Yes",
            SourceDurationText = "12 months",
            SourceStartText = "2025",
            SourceEndText = null,
        });

        Assert.Equal(
            [reason == LegacyReviewReason.NoMentor ? "" : "J. Unknown", "", "S. Dlamini", "", "", "", "", "", "", "", "Legacy",
                "Mentorship Tracker 2026.xlsx, row 9", "Yes", "12 months", "Start: 2025", written],
            RegisterCsv.Fields(held));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [InlineData("+27 82", "'+27 82")]
    [InlineData("-", "'-")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tTab", "'\tTab")]
    [InlineData("\rReturn", "\"'\rReturn\"")]
    public void A_cell_that_would_start_a_formula_opens_as_text(string field, string written)
    {
        Assert.Equal(written, RegisterCsv.Escape(field));
    }

    [Theory]
    [InlineData("Plain", "Plain")]
    [InlineData("", "")]
    [InlineData("Nkosi, Thandi", "\"Nkosi, Thandi\"")]
    [InlineData("The \"lead\"", "\"The \"\"lead\"\"\"")]
    [InlineData("Two\nlines", "\"Two\nlines\"")]
    [InlineData("No - needs reassigning", "No - needs reassigning")]
    public void A_field_is_quoted_only_when_rfc_4180_requires_it(string field, string written)
    {
        Assert.Equal(written, RegisterCsv.Escape(field));
    }

    [Fact]
    public void Each_row_is_one_crlf_terminated_record()
    {
        var digital = Digital(RelationshipState.Complete, RelationshipStatus.Blank, ActiveFlag.N) with { Mentor = Mentor with { DisplayName = "Nkosi, Thandi" } };

        var text = Encoding.UTF8.GetString(RegisterCsv.Write([digital, Legacy(null, null, null)])[3..]);

        var records = text.Split("\r\n");
        Assert.Equal(4, records.Length);
        Assert.Equal("", records[^1]);
        Assert.StartsWith("\"Nkosi, Thandi\",Principal Engineer,Zoë Mokoena,,Complete,,N,", records[1], StringComparison.Ordinal);
        Assert.EndsWith(",Legacy,\"Mentorship Tracker 2026.xlsx, row 8\",Successfully complete,ongoing,Start: 10/1/2026; End: ongoing,\"Unresolved value: State, Status, IsActive\"",
            records[2], StringComparison.Ordinal);
    }

    private static RegisterEntryView Digital(RelationshipState state, RelationshipStatus status, ActiveFlag isActive) => RegisterEntries.Of(
        new MentorshipRelationship
        {
            Id = Guid.NewGuid(),
            Provenance = Provenance.Digital,
            CandidateId = Guid.NewGuid(),
            ProtegeEmployeeId = Protege.EmployeeId,
            MentorEmployeeId = Mentor.EmployeeId,
            State = state,
            Status = status,
            IsActive = isActive,
        },
        Mentor,
        Protege);

    private static RegisterEntryView Legacy(RelationshipState? state, RelationshipStatus? status, ActiveFlag? isActive) => RegisterEntries.Of(
        new MentorshipRelationship
        {
            Id = Guid.NewGuid(),
            Provenance = Provenance.Legacy,
            ProtegeEmployeeId = Protege.EmployeeId,
            MentorEmployeeId = Mentor.EmployeeId,
            State = state,
            Status = status,
            IsActive = isActive,
            SourceFile = "Mentorship Tracker 2026.xlsx",
            SourceRow = 8,
            SourceMentorText = "T. Nkosi",
            SourceProtegeText = "Z. Mokoena",
            SourceActiveText = "Successfully complete",
            SourceDurationText = "ongoing",
            SourceStartText = "10/1/2026",
            SourceEndText = "ongoing",
        },
        Mentor,
        Protege);
}
