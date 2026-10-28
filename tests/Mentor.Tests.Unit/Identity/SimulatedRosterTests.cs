using Mentor.Api.Identity;

namespace Mentor.Tests.Unit.Identity;

public sealed class SimulatedRosterTests
{
    [Fact]
    public void Every_simulated_person_has_an_identity_of_their_own_that_entra_never_issues()
    {
        Assert.Equal(SimulatedRoster.People.Count, SimulatedRoster.People.Select(person => person.ObjectId).Distinct().Count());
        Assert.All(SimulatedRoster.People, person => Assert.StartsWith("00000000-0000-4000-8000-", person.ObjectId.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void The_roster_demonstrates_both_employees_and_an_administrator()
    {
        Assert.Contains(SimulatedRoster.People, person => person.DeclaredRole == DeclaredRole.TalentManagementAdministrator);
        Assert.True(SimulatedRoster.People.Count(person => person.DeclaredRole == DeclaredRole.Employee) >= 4);
    }

    [Fact]
    public void Nobody_outside_the_roster_is_found()
    {
        Assert.Null(SimulatedRoster.Find(Guid.NewGuid()));
        Assert.Equal(SimulatedRoster.People[0], SimulatedRoster.Find(SimulatedRoster.People[0].ObjectId));
    }
}
