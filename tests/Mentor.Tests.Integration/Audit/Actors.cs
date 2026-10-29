using Mentor.Api.Authorisation;
using Mentor.Api.Identity;
using Mentor.Core.Audit;

namespace Mentor.Tests.Integration.Audit;

/// <summary>Callers as the identity module resolves them, for commands run in process against a test database.</summary>
internal static class Actors
{
    public static Caller Administrator(Guid employee, IdentityScheme scheme = IdentityScheme.Entra) =>
        new(Guid.NewGuid(), "Talent Management", scheme, employee, new HashSet<FunctionalRole> { FunctionalRole.Employee, FunctionalRole.TalentManagement });

    public static Caller Employee(Guid employee, IdentityScheme scheme = IdentityScheme.Entra) =>
        new(Guid.NewGuid(), "Employee", scheme, employee, new HashSet<FunctionalRole> { FunctionalRole.Employee });
}
