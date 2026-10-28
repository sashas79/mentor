using System.Net;
using Mentor.Api.Hosting;
using Mentor.Tests.Integration.Support;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Mentor.Tests.Integration.Persistence;

// How the API reaches the authoritative store: as a member of mentor_app, over TLS in production (NFR-009).
public sealed class DatabaseConnectionTests(PostgresServer postgres)
{
    [Fact]
    public async Task The_api_is_ready_while_it_can_reach_the_database_under_the_application_role()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        await using var api = new ApiFactory(Environments.Development, new Dictionary<string, string>
        {
            ["ConnectionStrings:Mentor"] = db.ConnectionString(Login.App),
        });

        var response = await api.GetAsync(HealthEndpoints.Ready);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_api_is_not_ready_while_the_database_is_unreachable()
    {
        await using var api = new ApiFactory(Environments.Development);

        var response = await api.GetAsync(HealthEndpoints.Ready);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("Disable")]
    [InlineData("Prefer")]
    public async Task Production_refuses_a_database_connection_without_tls(string sslMode)
    {
        await using var api = new ApiFactory(Environments.Production, new Dictionary<string, string>
        {
            ["ConnectionStrings:Mentor"] = $"Host=db.internal;Database=mentor;Username=mentor_api;SSL Mode={sslMode}",
        });

        var failure = Record.Exception(() => api.CreateClient());

        var refusal = Assert.IsType<OptionsValidationException>(Unwrap(failure));
        Assert.Contains("SSL Mode", refusal.Message);
        Assert.DoesNotContain("mentor_api", refusal.Message);
    }

    private static Exception? Unwrap(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OptionsValidationException)
            {
                return current;
            }
        }

        return exception;
    }
}
