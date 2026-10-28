using Mentor.Tests.Integration.Support;

namespace Mentor.Tests.Integration.Authorisation;

/// <summary>Where the caller stands towards the record an action targets.</summary>
public enum Standing
{
    /// <summary>Someone else's record, in which the caller has no part.</summary>
    Unrelated,

    /// <summary>The caller's own profile, application, request or notification.</summary>
    Owner,

    /// <summary>The caller is the protégé of the candidate or relationship.</summary>
    Protege,

    /// <summary>The caller is the mentor the candidate, relationship or request's candidate names.</summary>
    NamedMentor,

    /// <summary>Someone else's approved mentor profile.</summary>
    Discoverable,
}

/// <summary>
/// ADR-003 restated, independently of the code under test: who may take each action and on which records. A row lists
/// entries "who" or "who:standing,standing", where who is everyone, applicant, mentor or administrator. An entry
/// without standings applies to any record. Whatever no entry covers must be denied.
/// </summary>
internal static class ExpectedMatrix
{
    private static readonly Dictionary<string, string> Rows = new()
    {
        ["session.read"] = "everyone",
        ["employee-profile.read"] = "everyone:owner administrator",
        ["employee-profile.update"] = "everyone:owner",
        ["mentor-application.create"] = "everyone",
        ["mentor-application.list-pending"] = "administrator",
        ["mentor-application.decide"] = "administrator",
        ["mentor-profile.read"] = "applicant:owner mentor:owner everyone:discoverable administrator",
        ["mentor-profile.update"] = "applicant:owner mentor:owner",
        ["mentor-discovery.list"] = "everyone",
        ["mentorship-request.create"] = "everyone",
        ["mentorship-request.read"] = "everyone:owner mentor:mentor administrator",
        ["candidate.create"] = "everyone:owner",
        ["candidate.read"] = "everyone:protege mentor:mentor administrator",
        ["candidate.respond"] = "mentor:mentor",
        ["candidate.record-willingness"] = "everyone:protege,mentor",
        ["candidate.withdraw"] = "everyone:protege,mentor",
        ["agreement-draft.read"] = "everyone:protege,mentor administrator",
        ["agreement-draft.create-revision"] = "everyone:protege,mentor",
        ["agreement-draft.accept"] = "everyone:protege,mentor",
        ["agreement-draft.change-annex"] = "administrator",
        ["formation.decide"] = "administrator",
        ["formation.read"] = "everyone:protege,mentor administrator",
        ["relationship.read"] = "everyone:protege,mentor administrator",
        ["relationship.update-attributes"] = "administrator",
        ["goals.read"] = "everyone:protege,mentor administrator",
        ["goals.submit-progress"] = "everyone:protege",
        ["goals.request-completion"] = "everyone:protege",
        ["goals.decide-completion"] = "everyone:mentor",
        ["meeting.read"] = "everyone:protege,mentor administrator",
        ["meeting.record"] = "everyone:protege,mentor",
        ["notification.read"] = "everyone:owner",
        ["notification.mark-read"] = "everyone:owner",
        ["register.read"] = "administrator",
        ["register.export"] = "administrator",
        ["audit-trail.read"] = "administrator",
    };

    public static IReadOnlyCollection<string> Permissions => Rows.Keys;

    /// <summary>Whether the caller reaches the endpoint at all, before any record is consulted.</summary>
    public static bool ReachesEndpoint(string permission, Profile profile) =>
        Entries(permission).Any(entry => Covers(entry.Who, profile));

    public static bool Admits(string permission, Profile profile, Standing standing) =>
        Entries(permission).Any(entry => Covers(entry.Who, profile) && (entry.Standings is null || entry.Standings.Contains(standing)));

    /// <summary>The standings a caller of the profile can hold: one with no employee record owns nothing and is a party to nothing.</summary>
    public static IEnumerable<Standing> StandingsOf(Profile profile) =>
        profile == Profile.Newcomer ? [Standing.Unrelated, Standing.Discoverable] : Enum.GetValues<Standing>();

    private static IEnumerable<(string Who, HashSet<Standing>? Standings)> Entries(string permission) =>
        Rows[permission].Split(' ').Select(entry => entry.Split(':')).Select(parts => (parts[0], parts.Length == 1 ? null : parts[1].Split(',').Select(ParseStanding).ToHashSet()));

    private static bool Covers(string who, Profile profile) => who switch
    {
        "everyone" => true,
        "applicant" => profile == Profile.Applicant,
        "mentor" => profile == Profile.Mentor,
        "administrator" => profile == Profile.Administrator,
        _ => throw new ArgumentOutOfRangeException(nameof(who), who, "Not an actor of ADR-003."),
    };

    private static Standing ParseStanding(string standing) => standing switch
    {
        "owner" => Standing.Owner,
        "protege" => Standing.Protege,
        "mentor" => Standing.NamedMentor,
        "discoverable" => Standing.Discoverable,
        _ => throw new ArgumentOutOfRangeException(nameof(standing), standing, "Not a standing of ADR-003."),
    };
}
