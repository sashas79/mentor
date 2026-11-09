using System.Diagnostics.CodeAnalysis;
using Mentor.Core.Legacy;

namespace Mentor.Core.Relationships;

/// <summary>
/// One attribute an administrator's edit changes (ADR-006, FR-051): its value before, null where a legacy row's source left
/// it unresolved, and the value chosen.
/// </summary>
public sealed record AttributeChange(RelationshipAttribute Attribute, Enum? Prior, Enum New)
{
    /// <summary>The field the audit trail records the change under: the attribute's camel-case property name.</summary>
    public string Field => Attribute switch
    {
        RelationshipAttribute.State => "state",
        RelationshipAttribute.Status => "status",
        RelationshipAttribute.IsActive => "isActive",
        _ => throw new ArgumentOutOfRangeException(nameof(Attribute), Attribute, "Not a relationship attribute."),
    };
}

/// <summary>
/// A Talent Management administrator's edit of a relationship's State, Status and IsActive (COMP-RELATIONSHIP-ADMIN,
/// ADR-006): any subset of the three, each exactly one of its business values, written as the API names them
/// (InProgress, OnHold or Complete; Blank or NeedsReassigning; Y or N). Nothing else is checked: no attribute is derived
/// from another, no combination is refused (BR-009), an attribute the edit leaves out keeps its value, and a value equal to
/// the current one is no change. An edit never writes null: null means only that a legacy row's source left the value
/// unresolved, and there is no "unknown" value to choose (FR-044, BR-006).
/// </summary>
public sealed record RelationshipEdit
{
    private RelationshipEdit(RelationshipState? state, RelationshipStatus? status, ActiveFlag? isActive) =>
        (State, Status, IsActive) = (state, status, isActive);

    public RelationshipState? State { get; }

    public RelationshipStatus? Status { get; }

    public ActiveFlag? IsActive { get; }

    /// <summary>
    /// The edit naming <paramref name="state"/>, <paramref name="status"/> and <paramref name="isActive"/>, each absent
    /// (null) or exactly a member name of its enumeration, case included: IsActive is Y or N, never y, Yes, 0 or 1
    /// (FR-071). At least one attribute is named. The errors are keyed by the member at fault.
    /// </summary>
    public static bool TryCreate(
        string? state, string? status, string? isActive, [NotNullWhen(true)] out RelationshipEdit? edit, out IReadOnlyDictionary<string, string[]> errors)
    {
        var problems = new Dictionary<string, string[]>();
        var parsedState = Parse<RelationshipState>(state, "state", "State is InProgress, OnHold or Complete.", problems);
        var parsedStatus = Parse<RelationshipStatus>(status, "status", "Status is NeedsReassigning or Blank.", problems);
        var parsedIsActive = Parse<ActiveFlag>(isActive, "isActive", "IsActive is Y or N.", problems);
        if (state is null && status is null && isActive is null)
        {
            problems["attributes"] = ["An edit names at least one of state, status and isActive."];
        }

        errors = problems;
        edit = problems.Count == 0 ? new RelationshipEdit(parsedState, parsedStatus, parsedIsActive) : null;
        return edit is not null;
    }

    /// <summary>
    /// What the edit changes on <paramref name="relationship"/>: each attribute it names whose value differs, in the order
    /// State, Status, IsActive. The attributes it does not name are not touched, whatever the others become.
    /// </summary>
    public IReadOnlyList<AttributeChange> ChangesTo(MentorshipRelationship relationship)
    {
        ArgumentNullException.ThrowIfNull(relationship);
        var changes = new List<AttributeChange>();
        if (State is { } state && relationship.State != state)
        {
            changes.Add(new AttributeChange(RelationshipAttribute.State, relationship.State, state));
        }

        if (Status is { } status && relationship.Status != status)
        {
            changes.Add(new AttributeChange(RelationshipAttribute.Status, relationship.Status, status));
        }

        if (IsActive is { } isActive && relationship.IsActive != isActive)
        {
            changes.Add(new AttributeChange(RelationshipAttribute.IsActive, relationship.IsActive, isActive));
        }

        return changes;
    }

    private static T? Parse<T>(string? text, string field, string message, Dictionary<string, string[]> problems)
        where T : struct, Enum
    {
        if (text is null)
        {
            return null;
        }

        foreach (var value in Enum.GetValues<T>())
        {
            if (string.Equals(Enum.GetName(value), text, StringComparison.Ordinal))
            {
                return value;
            }
        }

        problems[field] = [message];
        return null;
    }
}
