using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace Mentor.Tests.Integration.Support;

/// <summary>
/// The API exactly as Program composes it, in the given environment and with the given settings. Unless the settings
/// name one, its database is a TLS connection to a port where nothing listens.
/// </summary>
internal sealed class ApiFactory(string environment, IReadOnlyDictionary<string, string>? settings = null)
    : WebApplicationFactory<Program>
{
    public const string UnreachableDatabase = "Host=127.0.0.1;Port=9;Database=mentor;Username=mentor_api;SSL Mode=Require;Timeout=2";

    public async Task<HttpResponseMessage> GetAsync(string route)
    {
        using var client = CreateClient();
        return await client.GetAsync(route, TestContext.Current.CancellationToken);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:Mentor", UnreachableDatabase);
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureLogging(logging => logging.ClearProviders());
    }
}
