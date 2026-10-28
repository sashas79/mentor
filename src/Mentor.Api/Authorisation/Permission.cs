using Mentor.Api.Identity;

namespace Mentor.Api.Authorisation;

/// <summary>What the caller must be to the record for a grant to apply.</summary>
public enum Relation
{
    /// <summary>The role alone suffices.</summary>
    Any,

    /// <summary>The record is the caller's own: their profile, their mentor application, their request, a notification addressed to them.</summary>
    Owner,

    /// <summary>The caller is the protégé of the candidate or relationship.</summary>
    Protege,

    /// <summary>The caller is the mentor the candidate or relationship names; for a request, a mentor one of its candidates names.</summary>
    NamedMentor,

    /// <summary>The record is an approved mentor profile, which discovery shows to everyone.</summary>
    Discoverable,
}

/// <summary>One row of a permission: an actor, and what that actor must be to the record.</summary>
public sealed record Grant(FunctionalRole Role, Relation Relation)
{
    public static readonly Grant AnyEmployee = new(FunctionalRole.Employee, Relation.Any);
    public static readonly Grant OwnRecord = new(FunctionalRole.Employee, Relation.Owner);
    public static readonly Grant TheProtege = new(FunctionalRole.Employee, Relation.Protege);
    public static readonly Grant TheMentor = new(FunctionalRole.Employee, Relation.NamedMentor);
    public static readonly Grant TheApprovedMentor = new(FunctionalRole.Mentor, Relation.NamedMentor);
    public static readonly Grant TalentManagement = new(FunctionalRole.TalentManagement, Relation.Any);
}

/// <summary>
/// An action on a resource and the grants that permit it (CONTRACT-AUTHORISATION-MATRIX). Whatever no grant covers is
/// denied.
/// </summary>
public sealed class Permission(string name, params Grant[] grants)
{
    /// <summary>The policy name an endpoint requires, in the form resource.action.</summary>
    public string Name { get; } = name;

    public IReadOnlyList<Grant> Grants { get; } = grants;

    /// <summary>
    /// Whether the caller may take the action. Without a record it checks the actor only, as an endpoint does before the
    /// record is loaded; with one it also checks the caller's relation to that record, which every action on an existing
    /// record must do before it reads or changes anything.
    /// </summary>
    public bool Admits(Caller caller, ProtectedRecord? record) =>
        Grants.Any(grant => caller.Holds(grant.Role) && (record is null || Relates(grant.Relation, caller.EmployeeId, record)));

    public override string ToString() => Name;

    // A caller with no employee record yet owns nothing and is a party to nothing.
    private static bool Relates(Relation relation, Guid? employee, ProtectedRecord record) => relation switch
    {
        Relation.Any => true,
        Relation.Discoverable => record.Discoverable,
        Relation.Owner => employee is not null && record.Owner == employee,
        Relation.Protege => employee is not null && record.Protege == employee,
        Relation.NamedMentor => employee is { } id && record.NamedMentors.Contains(id),
        _ => false,
    };
}
