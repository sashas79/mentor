using Mentor.Core.Employees;
using Mentor.Core.MentorRegistry;
using Mentor.Core.Notifications;
using Mentor.Core.Relationships;
using Mentor.Core.Requests;

namespace Mentor.Api.Authorisation;

/// <summary>
/// What the matrix needs to know about the record an action targets: whose it is, who its parties are, and whether it
/// is discoverable. Agreement drafts, formation decisions, goals, progress and meetings are checked against the
/// candidate or relationship they belong to.
/// </summary>
public sealed record ProtectedRecord
{
    public Guid? Owner { get; init; }

    public Guid? Protege { get; init; }

    public IReadOnlySet<Guid> NamedMentors { get; init; } = new HashSet<Guid>();

    public bool Discoverable { get; init; }

    public static ProtectedRecord Of(Employee employee) => new() { Owner = employee.Id };

    public static ProtectedRecord Of(MentorProfile profile) => new()
    {
        Owner = profile.EmployeeId,
        Discoverable = profile.ApprovalStatus == MentorApprovalStatus.Approved,
    };

    /// <summary>A request, owned by its protégé; <paramref name="namingMentors"/> are the mentors its candidates currently name.</summary>
    public static ProtectedRecord Of(MentorshipRequest request, IEnumerable<Guid> namingMentors) => new()
    {
        Owner = request.ProtegeEmployeeId,
        NamedMentors = namingMentors.ToHashSet(),
    };

    public static ProtectedRecord Of(MentorSelection candidate) => new()
    {
        Protege = candidate.ProtegeEmployeeId,
        NamedMentors = new HashSet<Guid> { candidate.MentorEmployeeId },
    };

    public static ProtectedRecord Of(MentorshipRelationship relationship) => new()
    {
        Protege = relationship.ProtegeEmployeeId,
        NamedMentors = new HashSet<Guid> { relationship.MentorEmployeeId },
    };

    public static ProtectedRecord Of(NotificationEvent notification) => new() { Owner = notification.RecipientEmployeeId };
}
