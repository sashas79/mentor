using static Mentor.Api.Authorisation.Grant;

namespace Mentor.Api.Authorisation;

/// <summary>
/// The server-side actor/resource/action matrix (CONTRACT-AUTHORISATION-MATRIX, ADR-003), versioned with the code that
/// enforces it. Every endpoint requires one of these permissions or is a listed public endpoint; whatever no grant
/// covers is denied. Two rows of ADR-003 have no permission on purpose:
/// <list type="bullet">
/// <item>Legacy entry has no API operation. Talent Management administrators enter historical relationships through
/// reviewed scripts under the mentor_legacy_entry database role (ADR-008, BR-022), so nobody, an operator included, can
/// enter one through the application.</item>
/// <item>An IT or system operator has no row. Technical access to the environment grants no application permission.</item>
/// </list>
/// Nobody updates an agreement revision, an acceptance or a decision, and nobody changes an employee's identity key:
/// the matrix has no such action, and the database grants none.
/// </summary>
public static class AuthorisationMatrix
{
    /// <summary>Raised whenever a row changes, so that a client or a reviewer can tell which matrix a deployment enforces.</summary>
    public const int Version = 1;

    /// <summary>Sign-in support: the caller's own identity, roles and reachable actions.</summary>
    public static class Session
    {
        public static readonly Permission Read = new("session.read", AnyEmployee);
    }

    public static class EmployeeProfile
    {
        public static readonly Permission Read = new("employee-profile.read", OwnRecord, TalentManagement);

        /// <summary>Display attributes only.</summary>
        public static readonly Permission Update = new("employee-profile.update", OwnRecord);
    }

    public static class MentorApplication
    {
        /// <summary>The caller's own application, once.</summary>
        public static readonly Permission Create = new("mentor-application.create", AnyEmployee);

        public static readonly Permission ListPending = new("mentor-application.list-pending", TalentManagement);

        /// <summary>Approve or decline.</summary>
        public static readonly Permission Decide = new("mentor-application.decide", TalentManagement);
    }

    public static class MentorProfile
    {
        /// <summary>A pending or declined profile is visible only to its owner and Talent Management.</summary>
        public static readonly Permission Read = new("mentor-profile.read",
            new Grant(FunctionalRole.Applicant, Relation.Owner),
            new Grant(FunctionalRole.Mentor, Relation.Owner),
            new Grant(FunctionalRole.Employee, Relation.Discoverable),
            TalentManagement);

        /// <summary>Biography and attributes.</summary>
        public static readonly Permission Update = new("mentor-profile.update",
            new Grant(FunctionalRole.Applicant, Relation.Owner),
            new Grant(FunctionalRole.Mentor, Relation.Owner));
    }

    public static class MentorDiscovery
    {
        /// <summary>Approved mentors only.</summary>
        public static readonly Permission List = new("mentor-discovery.list", AnyEmployee);
    }

    public static class MentorshipRequest
    {
        /// <summary>The caller's own request.</summary>
        public static readonly Permission Create = new("mentorship-request.create", AnyEmployee);

        /// <summary>A mentor reads a request only while one of its candidates names them.</summary>
        public static readonly Permission Read = new("mentorship-request.read", OwnRecord, TheApprovedMentor, TalentManagement);
    }

    /// <summary>The pairing of a request with one mentor (ADR-015). Participants are fixed; Talent Management records none of its decisions.</summary>
    public static class Candidate
    {
        /// <summary>The request's protégé selects a discoverable mentor; checked against the request.</summary>
        public static readonly Permission Create = new("candidate.create", OwnRecord);

        public static readonly Permission Read = new("candidate.read", TheProtege, TheApprovedMentor, TalentManagement);

        /// <summary>Accept or decline the invitation.</summary>
        public static readonly Permission Respond = new("candidate.respond", TheApprovedMentor);

        /// <summary>Each participant records only their own willingness to proceed.</summary>
        public static readonly Permission RecordWillingness = new("candidate.record-willingness", TheProtege, TheMentor);

        /// <summary>Either participant, before formation.</summary>
        public static readonly Permission Withdraw = new("candidate.withdraw", TheProtege, TheMentor);
    }

    /// <summary>Checked against the candidate the draft belongs to (ADR-004).</summary>
    public static class AgreementDraft
    {
        public static readonly Permission Read = new("agreement-draft.read", TheProtege, TheMentor, TalentManagement);

        public static readonly Permission CreateRevision = new("agreement-draft.create-revision", TheProtege, TheMentor);

        /// <summary>Each participant records only their own acceptance of the current revision.</summary>
        public static readonly Permission Accept = new("agreement-draft.accept", TheProtege, TheMentor);

        /// <summary>The administrative annex, outside the accepted content.</summary>
        public static readonly Permission ChangeAnnex = new("agreement-draft.change-annex", TalentManagement);
    }

    public static class Formation
    {
        /// <summary>Approve, which forms the relationship, or reject.</summary>
        public static readonly Permission Decide = new("formation.decide", TalentManagement);

        /// <summary>The outcome; checked against the candidate.</summary>
        public static readonly Permission Read = new("formation.read", TheProtege, TheMentor, TalentManagement);
    }

    public static class Relationship
    {
        public static readonly Permission Read = new("relationship.read", TheProtege, TheMentor, TalentManagement);

        /// <summary>State, Status and IsActive.</summary>
        public static readonly Permission UpdateAttributes = new("relationship.update-attributes", TalentManagement);
    }

    /// <summary>Checked against the relationship. No actor reaches private discussion content, because none is stored.</summary>
    public static class Goals
    {
        /// <summary>Talent Management reads structured goals and progress.</summary>
        public static readonly Permission Read = new("goals.read", TheProtege, TheMentor, TalentManagement);

        public static readonly Permission SubmitProgress = new("goals.submit-progress", TheProtege);

        public static readonly Permission RequestCompletion = new("goals.request-completion", TheProtege);

        /// <summary>Approve or reject a completion request.</summary>
        public static readonly Permission DecideCompletion = new("goals.decide-completion", TheMentor);
    }

    /// <summary>Checked against the candidate or relationship the meeting belongs to.</summary>
    public static class Meeting
    {
        public static readonly Permission Read = new("meeting.read", TheProtege, TheMentor, TalentManagement);

        public static readonly Permission Record = new("meeting.record", TheProtege, TheMentor);
    }

    /// <summary>The intended recipient alone; Talent Management included, nobody else sees another's pending actions.</summary>
    public static class Notification
    {
        public static readonly Permission Read = new("notification.read", OwnRecord);

        public static readonly Permission MarkRead = new("notification.mark-read", OwnRecord);
    }

    public static class Register
    {
        public static readonly Permission Read = new("register.read", TalentManagement);

        public static readonly Permission Export = new("register.export", TalentManagement);
    }

    public static class AuditTrail
    {
        public static readonly Permission Read = new("audit-trail.read", TalentManagement);
    }

    public static IReadOnlyList<Permission> All { get; } =
    [
        Session.Read,
        EmployeeProfile.Read, EmployeeProfile.Update,
        MentorApplication.Create, MentorApplication.ListPending, MentorApplication.Decide,
        MentorProfile.Read, MentorProfile.Update,
        MentorDiscovery.List,
        MentorshipRequest.Create, MentorshipRequest.Read,
        Candidate.Create, Candidate.Read, Candidate.Respond, Candidate.RecordWillingness, Candidate.Withdraw,
        AgreementDraft.Read, AgreementDraft.CreateRevision, AgreementDraft.Accept, AgreementDraft.ChangeAnnex,
        Formation.Decide, Formation.Read,
        Relationship.Read, Relationship.UpdateAttributes,
        Goals.Read, Goals.SubmitProgress, Goals.RequestCompletion, Goals.DecideCompletion,
        Meeting.Read, Meeting.Record,
        Notification.Read, Notification.MarkRead,
        Register.Read, Register.Export,
        AuditTrail.Read,
    ];

    public static Permission? Find(string? name) => All.FirstOrDefault(permission => permission.Name == name);
}
