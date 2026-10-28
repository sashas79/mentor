using System.Security.Claims;
using Mentor.Api.Identity;
using Mentor.Core.Audit;

namespace Mentor.Tests.Unit.Identity;

public sealed class MentorPrincipalTests
{
    [Theory]
    [InlineData(IdentityScheme.Entra, false)]
    [InlineData(IdentityScheme.Simulated, true)]
    public void Both_schemes_produce_one_principal_shape(IdentityScheme scheme, bool talentManagement)
    {
        var objectId = Guid.NewGuid();

        var identity = MentorPrincipal.IdentityOf(MentorPrincipal.Create(objectId, "Naledi Zulu", talentManagement, scheme, "Test"));

        Assert.Equal(new AuthenticatedIdentity(objectId, "Naledi Zulu", scheme, talentManagement), identity);
    }

    [Fact]
    public void An_unauthenticated_principal_is_nobody()
    {
        Assert.Null(MentorPrincipal.IdentityOf(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public void A_principal_from_any_other_scheme_is_nobody()
    {
        var foreign = new ClaimsPrincipal(new ClaimsIdentity([new Claim(MentorPrincipal.ObjectIdClaim, Guid.NewGuid().ToString())], "Foreign"));

        Assert.Null(MentorPrincipal.IdentityOf(foreign));
    }

    // A database account enters legacy rows (ADR-008); it is never an application caller.
    [Fact]
    public void The_database_identity_scheme_is_never_an_application_caller()
    {
        var principal = MentorPrincipal.Create(Guid.NewGuid(), "tm_alice", talentManagement: true, IdentityScheme.Database, "Database");

        Assert.Null(MentorPrincipal.IdentityOf(principal));
    }
}
