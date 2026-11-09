using Mentor.Api.MentorRegistry;
using Mentor.Tests.Integration.Persistence;

namespace Mentor.Tests.Integration.MentorRegistry;

/// <summary>The routes the registry tests call, the bodies they send and the rows they read back.</summary>
internal static class MentorRegistryApi
{
    public const string ApplicationsRoute = MentorRegistryEndpoints.ApplicationsRoute;
    public const string PendingRoute = MentorRegistryEndpoints.PendingApplicationsRoute;
    public const string OwnRoute = MentorRegistryEndpoints.OwnProfileRoute;
    public const string MentorsRoute = MentorRegistryEndpoints.DiscoveryRoute;
    public const string Inbox = "/api/notifications";

    public static string ProfileRoute(Guid id) => MentorRegistryEndpoints.ProfileRoute.Replace("{id:guid}", id.ToString(), StringComparison.Ordinal);

    public static string ApproveRoute(Guid id) => MentorRegistryEndpoints.ApproveRoute.Replace("{id:guid}", id.ToString(), StringComparison.Ordinal);

    public static string DeclineRoute(Guid id) => MentorRegistryEndpoints.DeclineRoute.Replace("{id:guid}", id.ToString(), StringComparison.Ordinal);

    public static string AuditRoute(Guid profile) => $"/api/audit-trail/records/MentorProfile/{profile}";

    /// <summary>A complete application or edit body.</summary>
    public static object Details(
        string? biography = "Ten years of platform delivery",
        string? roleTitle = "Principal Engineer",
        string? industryExperience = "Banking and telecoms",
        string? mentoringExperience = "Two graduates to senior") =>
        new { biography, roleTitle, industryExperience, mentoringExperience };

    /// <summary>The profile row of <paramref name="employee"/>.</summary>
    public static Task<Guid> ProfileIdAsync(TestDatabase db, Guid employee) =>
        db.ScalarAsync<Guid>(Login.Owner, "SELECT id FROM mentor.mentor_profile WHERE employee_id = $1", employee);

    public static Task<string> ApprovalStatusAsync(TestDatabase db, Guid profile) =>
        db.ScalarAsync<string>(Login.Owner, "SELECT approval_status FROM mentor.mentor_profile WHERE id = $1", profile);

    public static Task<long> AuditEntriesAsync(TestDatabase db, Guid profile) =>
        db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.audit_entry WHERE record_type = 'MENTOR_PROFILE' AND record_id = $1", profile);

    public static Task<long> NotificationsAsync(TestDatabase db, Guid recipient) =>
        db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.notification_event WHERE recipient_employee_id = $1", recipient);

    /// <summary>A mentor who is none of the callers, with a decided application: approved unless <paramref name="status"/> says otherwise.</summary>
    public static async Task<(Guid Employee, Guid Profile)> MentorAsync(TestDatabase db, string displayName, Guid decidedBy, string status = "APPROVED")
    {
        var employee = await new Seed(db).EmployeeAsync(displayName);
        var profile = await db.ScalarAsync<Guid>(Login.Owner, """
            INSERT INTO mentor.mentor_profile (id, employee_id, biography, role_title, industry_experience, mentoring_experience,
                approval_status, approval_decided_by_employee_id, approval_decided_at)
            VALUES (gen_random_uuid(), $1, 'Biography', 'Role', 'Industry', 'Mentoring', $2, $3, now())
            RETURNING id
            """, employee, status, decidedBy);
        return (employee, profile);
    }
}
