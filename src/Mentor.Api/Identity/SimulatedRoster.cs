namespace Mentor.Api.Identity;

/// <summary>The role a simulated person declares: the stand-in for the Entra app-role assignment (ADR-002).</summary>
public enum DeclaredRole
{
    Employee,
    TalentManagementAdministrator,
}

/// <summary>One fictitious person of the demonstration roster.</summary>
public sealed record SimulatedPerson(Guid ObjectId, string DisplayName, DeclaredRole DeclaredRole);

/// <summary>
/// The fixed demonstration roster of the Simulated identity scheme (ADR-002, NFR-010). The people are fictitious and their
/// object identifiers can never be issued by Entra, so a simulated record is recognisable as one. Nobody outside the
/// roster can be simulated, and nobody can declare a role the roster does not give them.
/// </summary>
public static class SimulatedRoster
{
    public const string Route = "/api/identity/simulated-roster";

    public static IReadOnlyList<SimulatedPerson> People { get; } =
    [
        new(Simulated(1), "Thandiwe Mokoena", DeclaredRole.Employee),
        new(Simulated(2), "Johan van der Merwe", DeclaredRole.Employee),
        new(Simulated(3), "Priya Naidoo", DeclaredRole.Employee),
        new(Simulated(4), "Sipho Dlamini", DeclaredRole.Employee),
        new(Simulated(5), "Anele Jacobs", DeclaredRole.Employee),
        new(Simulated(6), "Lerato Khumalo", DeclaredRole.TalentManagementAdministrator),
    ];

    public static SimulatedPerson? Find(Guid objectId) => People.FirstOrDefault(person => person.ObjectId == objectId);

    // Version-4 layout with an all-zero prefix: a value Entra never assigns.
    private static Guid Simulated(int number) => Guid.Parse($"00000000-0000-4000-8000-{number:D12}");

    // Sign-in support, public by ADR-003: the frontend lists the roster before anyone has signed in. It exists only while
    // the Simulated scheme does.
    public static IEndpointConventionBuilder MapSimulatedRoster(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet(Route, () => TypedResults.Ok(People))
            .WithName("GetSimulatedRoster")
            .AllowAnonymous();
}
