using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mentor.Core.Relationships;

namespace Mentor.Core.Agreements;

/// <summary>How the limit on contact between meetings is counted, in the source agreement's wording (AC-FR-025-01).</summary>
public enum ContactUnit
{
    Days,
    Weeks,
}

/// <summary>The limit on telephone or e-mail contact between meetings: at most once every <see cref="Interval"/> days or weeks.</summary>
public sealed record ContactLimit(int Interval, ContactUnit Unit);

/// <summary>
/// The terms of the agreement (FR-023, FR-024, FR-025, FR-055, BR-010, BR-011, BR-017): the duration category, the
/// optional start and end dates, which every category may leave empty, and, where supplied, the meeting interval in
/// weeks, the approximate minutes per meeting, the limit on contact between meetings, the arrangements when either
/// participant cannot attend, and the review date.
/// </summary>
public sealed record AgreementTerms(
    DurationCategory DurationCategory,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int? MeetingIntervalWeeks,
    int? ApproximateMinutesPerMeeting,
    ContactLimit? BetweenMeetingContact,
    string? MissedMeetingArrangements,
    DateOnly? ReviewDate);

/// <summary>The distinct agreed duties of each participant (FR-026, AC-FR-026-01).</summary>
public sealed record AgreementResponsibilities(IReadOnlyList<string> Mentor, IReadOnlyList<string> Protege);

/// <summary>A participant as the agreement names them: the stable employee identity, and the name it read at the time.</summary>
public sealed record AgreementParty(Guid EmployeeId, string DisplayName);

/// <summary>The identified participants (AC-FR-022-01): written by the server from the candidate, never taken from a request body (ADR-004).</summary>
public sealed record AgreementParties(AgreementParty Protege, AgreementParty Mentor);

/// <summary>What the participants write into a draft (the entered values of COMP-AGREEMENT-DRAFTING), validated.</summary>
public sealed record AgreementDraftDetails(
    AgreementTerms Terms,
    IReadOnlyList<string> Goals,
    IReadOnlyList<string> Barriers,
    IReadOnlyList<string> Contingencies,
    AgreementResponsibilities Responsibilities)
{
    public const int MaxListItems = 20;
    public const int MaxItemLength = 1000;
    public const int MaxArrangementsLength = 2000;
    public const int MaxMeetingIntervalWeeks = 52;
    public const int MaxMinutesPerMeeting = 480;
    public const int MaxContactInterval = 365;

    /// <summary>
    /// Normalises the submitted form (<see cref="AgreementText"/>) and validates it. The duration category, at least one
    /// goal and at least one responsibility of each participant are required; barriers and contingencies may be none;
    /// every date and the remaining terms are optional. The errors are keyed by the path of the member in the form and
    /// are empty when the details are valid; no message repeats what was submitted.
    /// </summary>
    public static bool TryCreate(AgreementDraftForm? form, [NotNullWhen(true)] out AgreementDraftDetails? details, out IReadOnlyDictionary<string, string[]> errors)
    {
        var failures = new Dictionary<string, string[]>();
        var terms = ValidTerms(form?.Terms, failures);
        var goals = List(form?.Goals, "goals", "development goal", required: true, failures);
        var barriers = List(form?.Barriers, "barriers", "barrier", required: false, failures);
        var contingencies = List(form?.Contingencies, "contingencies", "contingency plan", required: false, failures);
        var mentor = List(form?.Responsibilities?.Mentor, "responsibilities.mentor", "responsibility of the mentor", required: true, failures);
        var protege = List(form?.Responsibilities?.Protege, "responsibilities.protege", "responsibility of the protégé", required: true, failures);

        errors = failures;
        details = failures.Count == 0 && terms is not null
            ? new AgreementDraftDetails(terms, goals, barriers, contingencies, new AgreementResponsibilities(mentor, protege))
            : null;
        return details is not null;
    }

    private static AgreementTerms? ValidTerms(AgreementTermsForm? form, Dictionary<string, string[]> failures)
    {
        if (form is null)
        {
            failures["terms"] = ["The terms of the agreement are required."];
            return null;
        }

        var count = failures.Count;
        if (form.DurationCategory is not { } duration)
        {
            failures["terms.durationCategory"] = ["A duration category is required."];
        }
        else if (!Enum.IsDefined(duration))
        {
            failures["terms.durationCategory"] = ["The duration category is 6 months, 12 months or 12+ months."];
        }

        if (form.StartDate is { } start && form.EndDate is { } end && end < start)
        {
            failures["terms.endDate"] = ["The end date cannot be before the start date."];
        }

        Bounded(form.MeetingIntervalWeeks, MaxMeetingIntervalWeeks, "terms.meetingIntervalWeeks", "The meeting interval is a whole number of weeks", failures);
        Bounded(form.ApproximateMinutesPerMeeting, MaxMinutesPerMeeting, "terms.approximateMinutesPerMeeting", "The approximate meeting duration is a whole number of minutes", failures);

        ContactLimit? contact = null;
        if (form.BetweenMeetingContact is { } limit)
        {
            if (limit.Interval is null)
            {
                failures["terms.betweenMeetingContact.interval"] = ["The contact limit names how many days or weeks."];
            }

            Bounded(limit.Interval, MaxContactInterval, "terms.betweenMeetingContact.interval", "The contact limit is a whole number", failures);
            if (limit.Unit is not { } unit || !Enum.IsDefined(unit))
            {
                failures["terms.betweenMeetingContact.unit"] = ["The contact limit is counted in days or weeks."];
            }
            else if (limit.Interval is { } interval)
            {
                contact = new ContactLimit(interval, unit);
            }
        }

        var arrangements = Text(form.MissedMeetingArrangements, MaxArrangementsLength, "terms.missedMeetingArrangements", "The missed-meeting arrangements", failures);

        return failures.Count == count
            ? new AgreementTerms(form.DurationCategory!.Value, form.StartDate, form.EndDate, form.MeetingIntervalWeeks, form.ApproximateMinutesPerMeeting,
                contact, arrangements, form.ReviewDate)
            : null;
    }

    private static void Bounded(int? value, int max, string path, string what, Dictionary<string, string[]> failures)
    {
        if (value is { } number && (number < 1 || number > max))
        {
            failures[path] = [$"{what} from 1 to {max}."];
        }
    }

    private static IReadOnlyList<string> List(IReadOnlyList<string?>? submitted, string path, string what, bool required, Dictionary<string, string[]> failures)
    {
        if (submitted is null || submitted.Count == 0)
        {
            if (required)
            {
                failures[path] = [$"At least one {what} is required."];
            }

            return [];
        }

        if (submitted.Count > MaxListItems)
        {
            failures[path] = [$"At most {MaxListItems} items of this kind are accepted."];
            return [];
        }

        var items = new List<string>(submitted.Count);
        for (var index = 0; index < submitted.Count; index++)
        {
            var item = Text(submitted[index], MaxItemLength, $"{path}[{index}]", $"Each {what}", failures);
            if (item is null)
            {
                failures.TryAdd($"{path}[{index}]", [$"Each {what} must be stated."]);
            }
            else
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static string? Text(string? submitted, int maxLength, string path, string what, Dictionary<string, string[]> failures)
    {
        var problem = AgreementText.Normalize(submitted, maxLength, lineBreaks: true, out var value);
        switch (problem)
        {
            case AgreementText.Problem.TooLong:
                failures[path] = [$"{what} must be at most {maxLength} characters."];
                return null;
            case AgreementText.Problem.NotText:
            case AgreementText.Problem.NotOneLine:
                failures[path] = [$"{what} holds characters that are not text."];
                return null;
            default:
                return value;
        }
    }
}

/// <summary>
/// The content document of one revision (CONTRACT-AGREEMENT-CONTENT, ADR-004): the substantive content and nothing
/// else, in the sections parties, policy, terms, goals, barriers, contingencies and responsibilities. Its canonical JSON
/// (<see cref="CanonicalJson"/>) is what a revision stores and what <see cref="AgreementContentHash"/> identifies, so the
/// content a participant accepts, the hash that identifies it and the content formation retains are the same bytes. The
/// administrative annex is not part of it.
/// <para>
/// In the document, a date is written yyyy-MM-dd, an absent value is null, and the duration category and the contact
/// unit are written as the database stores enumerations (SIX_MONTHS, WEEKS).
/// </para>
/// </summary>
public sealed record AgreementContent(
    AgreementParties Parties,
    IReadOnlyList<PolicyClause> Policy,
    AgreementTerms Terms,
    IReadOnlyList<string> Goals,
    IReadOnlyList<string> Barriers,
    IReadOnlyList<string> Contingencies,
    AgreementResponsibilities Responsibilities)
{
    private const string DateFormat = "yyyy-MM-dd";

    private static readonly IReadOnlyDictionary<DurationCategory, string> DurationValues = new Dictionary<DurationCategory, string>
    {
        [DurationCategory.SixMonths] = "SIX_MONTHS",
        [DurationCategory.TwelveMonths] = "TWELVE_MONTHS",
        [DurationCategory.TwelvePlusMonths] = "TWELVE_PLUS_MONTHS",
    };

    private static readonly IReadOnlyDictionary<ContactUnit, string> UnitValues = new Dictionary<ContactUnit, string>
    {
        [ContactUnit.Days] = "DAYS",
        [ContactUnit.Weeks] = "WEEKS",
    };

    /// <summary>The content of a revision: the candidate's participants, the fixed policy text of this release and the entered values.</summary>
    public static AgreementContent Compose(AgreementParties parties, AgreementDraftDetails details) =>
        new(parties, AgreementPolicy.Clauses, details.Terms, details.Goals, details.Barriers, details.Contingencies, details.Responsibilities);

    /// <summary>The document in canonical form: what a revision stores and its content hash is computed over.</summary>
    public string ToCanonicalJson()
    {
        var document = new JsonObject
        {
            ["parties"] = new JsonObject
            {
                ["protege"] = Party(Parties.Protege),
                ["mentor"] = Party(Parties.Mentor),
            },
            ["policy"] = new JsonArray([.. Policy.Select(clause => (JsonNode)new JsonObject { ["clause"] = clause.Clause, ["text"] = clause.Text })]),
            ["terms"] = new JsonObject
            {
                ["durationCategory"] = DurationValues[Terms.DurationCategory],
                ["startDate"] = Date(Terms.StartDate),
                ["endDate"] = Date(Terms.EndDate),
                ["meetingIntervalWeeks"] = Terms.MeetingIntervalWeeks,
                ["approximateMinutesPerMeeting"] = Terms.ApproximateMinutesPerMeeting,
                ["betweenMeetingContact"] = Terms.BetweenMeetingContact is { } limit
                    ? new JsonObject { ["interval"] = limit.Interval, ["unit"] = UnitValues[limit.Unit] }
                    : null,
                ["missedMeetingArrangements"] = Terms.MissedMeetingArrangements,
                ["reviewDate"] = Date(Terms.ReviewDate),
            },
            ["goals"] = Texts(Goals),
            ["barriers"] = Texts(Barriers),
            ["contingencies"] = Texts(Contingencies),
            ["responsibilities"] = new JsonObject
            {
                ["mentor"] = Texts(Responsibilities.Mentor),
                ["protege"] = Texts(Responsibilities.Protege),
            },
        };
        return CanonicalJson.Canonicalize(document.ToJsonString());
    }

    /// <summary>
    /// The content a revision or formed agreement stores. <see cref="FormatException"/> unless the text is an agreement
    /// content document in exactly its canonical form, so what is shown of a revision is always the whole of what its hash
    /// identifies.
    /// </summary>
    public static AgreementContent Parse(string canonicalJson)
    {
        AgreementContent content;
        try
        {
            using var document = JsonDocument.Parse(canonicalJson);
            content = Read(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new FormatException("The text is not an agreement content document.", exception);
        }

        if (!string.Equals(content.ToCanonicalJson(), canonicalJson, StringComparison.Ordinal))
        {
            throw new FormatException("The text is not an agreement content document in canonical form.");
        }

        return content;
    }

    internal static string StoredValue(DurationCategory category) => DurationValues[category];

    internal static string StoredValue(ContactUnit unit) => UnitValues[unit];

    private static JsonObject Party(AgreementParty party) => new()
    {
        ["employeeId"] = party.EmployeeId.ToString("D"),
        ["displayName"] = party.DisplayName,
    };

    private static JsonArray Texts(IEnumerable<string> texts) => new([.. texts.Select(text => (JsonNode?)text)]);

    private static string? Date(DateOnly? date) => date?.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static AgreementContent Read(JsonElement root)
    {
        var parties = root.GetProperty("parties");
        var terms = root.GetProperty("terms");
        var responsibilities = root.GetProperty("responsibilities");
        return new AgreementContent(
            new AgreementParties(ReadParty(parties.GetProperty("protege")), ReadParty(parties.GetProperty("mentor"))),
            [.. root.GetProperty("policy").EnumerateArray()
                .Select(clause => new PolicyClause(clause.GetProperty("clause").GetString()!, clause.GetProperty("text").GetString()!))],
            new AgreementTerms(
                Enumeration(DurationValues, terms.GetProperty("durationCategory").GetString()),
                ReadDate(terms.GetProperty("startDate")),
                ReadDate(terms.GetProperty("endDate")),
                ReadNumber(terms.GetProperty("meetingIntervalWeeks")),
                ReadNumber(terms.GetProperty("approximateMinutesPerMeeting")),
                terms.GetProperty("betweenMeetingContact") is { ValueKind: not JsonValueKind.Null } limit
                    ? new ContactLimit(limit.GetProperty("interval").GetInt32(), Enumeration(UnitValues, limit.GetProperty("unit").GetString()))
                    : null,
                ReadText(terms.GetProperty("missedMeetingArrangements")),
                ReadDate(terms.GetProperty("reviewDate"))),
            ReadTexts(root.GetProperty("goals")),
            ReadTexts(root.GetProperty("barriers")),
            ReadTexts(root.GetProperty("contingencies")),
            new AgreementResponsibilities(ReadTexts(responsibilities.GetProperty("mentor")), ReadTexts(responsibilities.GetProperty("protege"))));
    }

    private static AgreementParty ReadParty(JsonElement party) =>
        new(Guid.ParseExact(party.GetProperty("employeeId").GetString()!, "D"), party.GetProperty("displayName").GetString()!);

    private static TEnum Enumeration<TEnum>(IReadOnlyDictionary<TEnum, string> values, string? stored)
        where TEnum : struct, Enum =>
        values.Single(value => value.Value == stored).Key;

    private static DateOnly? ReadDate(JsonElement date) =>
        date.ValueKind == JsonValueKind.Null ? null : DateOnly.ParseExact(date.GetString()!, DateFormat, CultureInfo.InvariantCulture);

    private static int? ReadNumber(JsonElement number) => number.ValueKind == JsonValueKind.Null ? null : number.GetInt32();

    private static string? ReadText(JsonElement text) => text.ValueKind == JsonValueKind.Null ? null : text.GetString();

    private static IReadOnlyList<string> ReadTexts(JsonElement texts) => [.. texts.EnumerateArray().Select(text => text.GetString()!)];
}
