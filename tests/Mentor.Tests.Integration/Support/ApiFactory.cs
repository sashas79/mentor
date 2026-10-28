using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace Mentor.Tests.Integration.Support;

/// <summary>The API exactly as Program composes it, in the given environment and with the given settings.</summary>
internal sealed class ApiFactory(string environment, IReadOnlyDictionary<string, string>? settings = null)
    : WebApplicationFactory<Program>
{
    public async Task<HttpResponseMessage> GetAsync(string route)
    {
        using var client = CreateClient();
        return await client.GetAsync(route, TestContext.Current.CancellationToken);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureLogging(logging => logging.ClearProviders());
    }
}
