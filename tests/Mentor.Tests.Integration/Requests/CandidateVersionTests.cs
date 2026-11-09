using System.Net;
using Mentor.Api.Requests;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;
using static Mentor.Tests.Integration.Requests.PairingApi;

namespace Mentor.Tests.Integration.Requests;

// ADR-005: every response for a candidate carries its version as a strong ETag, and every pairing decision names the
// version it was taken on in If-Match. Without one it is refused with 428, on a version no longer current with 409, and
// either way nothing is persisted: no change, no audit entry, no notification.
public sealed class CandidateVersionTests(PostgresServer server)
{
    [Fact]
    public async Task Every_response_for_a_candidate_carries_its_version_as_a_strong_etag()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var invited = await CandidateAsync(api);

        using var read = await api.SendAsync(HttpMethod.Get, Candidate(invited.Id()), Profile.Mentor);
        using var accepted = await TryAsync(api, Accept(invited.Id()), Profile.Mentor, invited);

        Assert.Equal(("\"1\"", false), (read.Headers.ETag!.Tag.ToString(), read.Headers.ETag.IsWeak));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("\"2\"", accepted.Headers.ETag!.Tag.ToString());
        // A list carries each candidate's version in its body, since one response has one ETag.
        var listed = Assert.Single((await api.JsonAsync(HttpMethod.Get, CandidatesRoute, Profile.Employee)).EnumerateArray());
        Assert.Equal(2L, listed.GetProperty("version").GetInt64());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("*")]
    [InlineData("W/\"1\"")]
    [InlineData("1")]
    [InlineData("\"1\", \"2\"")]
    [InlineData("\"one\"")]
    public async Task A_decision_without_a_usable_if_match_is_refused_with_428_and_persists_nothing(string? ifMatch)
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var invited = await CandidateAsync(api);
        var id = invited.Id();

        foreach (var (route, caller) in new[] { (Accept(id), Profile.Mentor), (Willingness(id), Profile.Employee), (Withdraw(id), Profile.Employee) })
        {
            var refused = await ProblemAssert.IsProblem(HttpStatusCode.PreconditionRequired, await api.SendAsync(HttpMethod.Post, route, caller, ifMatch: ifMatch));
            Assert.Equal(MentorshipRequestEndpoints.VersionRequired, refused.Text("title"));
        }

        Assert.Equal(("INVITED", 1L), (await StatusAsync(api.Db, id), await VersionAsync(api.Db, id)));
        Assert.Equal(1L, await AuditEntriesAsync(api.Db, id));
        Assert.Equal(1L, await NotificationsAsync(api.Db, id));
    }

    // The protégé decides on what they last read; the mentor's answer came first, so the decision is refused, and once
    // the protégé reads again they decide on the current state.
    [Fact]
    public async Task A_decision_on_a_version_no_longer_current_is_refused_with_409_and_persists_nothing()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var invited = await CandidateAsync(api);
        var id = invited.Id();
        var accepted = await DecideAsync(api, Accept(id), Profile.Mentor, invited);

        var stale = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await TryAsync(api, Withdraw(id), Profile.Employee, invited));

        Assert.Equal(MentorshipRequestEndpoints.CandidateChanged, stale.Text("title"));
        Assert.Equal(("ACCEPTED", 2L), (await StatusAsync(api.Db, id), await VersionAsync(api.Db, id)));
        Assert.Equal(2L, await AuditEntriesAsync(api.Db, id));
        Assert.Equal(2L, await NotificationsAsync(api.Db, id));
        var current = await api.JsonAsync(HttpMethod.Get, Candidate(id), Profile.Employee);
        Assert.Equal(accepted.GetRawText(), current.GetRawText());
        var withdrawn = await DecideAsync(api, Withdraw(id), Profile.Employee, current);
        Assert.Equal(("Withdrawn", 3L), (withdrawn.Text("status"), withdrawn.GetProperty("version").GetInt64()));
    }

    // Two decisions taken at once on one version: the version update admits exactly one, and the other persists nothing.
    [Fact]
    public async Task Two_simultaneous_decisions_on_one_version_end_with_exactly_one_taken()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var invited = await CandidateAsync(api);
        var id = invited.Id();

        var responses = await Task.WhenAll(
            TryAsync(api, Accept(id), Profile.Mentor, invited),
            TryAsync(api, Withdraw(id), Profile.Employee, invited));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(response => response.StatusCode).Order());
        var taken = responses[0].StatusCode == HttpStatusCode.OK ? "ACCEPTED" : "WITHDRAWN";
        Assert.Equal((taken, 2L), (await StatusAsync(api.Db, id), await VersionAsync(api.Db, id)));
        Assert.Equal(2L, await AuditEntriesAsync(api.Db, id));
        Assert.Equal(2L, await NotificationsAsync(api.Db, id));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }
}
