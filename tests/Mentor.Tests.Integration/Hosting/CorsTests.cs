using System.Net;
using Mentor.Api.Hosting;
using Mentor.Tests.Integration.Support;

namespace Mentor.Tests.Integration.Hosting;

public sealed class CorsTests
{
    private const string Frontend = "https://mentor.example";

    private static readonly Dictionary<string, string?> FrontendOrigin = new() { [$"{ApiHost.CorsOriginsSection}:0"] = Frontend };

    [Fact]
    public async Task The_configured_frontend_may_send_a_conditional_update_and_read_the_etag()
    {
        await using var probe = await ProbeHost.StartAsync(settings: FrontendOrigin);

        var preflight = await probe.SendAsync(Preflight(Frontend));
        Assert.Equal(HttpStatusCode.NoContent, preflight.StatusCode);
        Assert.Equal(Frontend, Assert.Single(preflight.Headers.GetValues("Access-Control-Allow-Origin")));

        var request = ProbeHost.Request(HttpMethod.Get, "/probe/public");
        request.Headers.Add("Origin", Frontend);
        var response = await probe.SendAsync(request);
        Assert.Contains("ETag", string.Join(',', response.Headers.GetValues("Access-Control-Expose-Headers")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Any_other_origin_is_not_allowed()
    {
        await using var probe = await ProbeHost.StartAsync(settings: FrontendOrigin);

        var preflight = await probe.SendAsync(Preflight("https://elsewhere.example"));

        Assert.False(preflight.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = ProbeHost.Request(HttpMethod.Options, "/probe/permitted");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "PUT");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,if-match");
        return request;
    }
}
