using Mentor.Api.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mentor.Tests.Integration.Support;

/// <summary>
/// The real host composition (AddMentorApiHost and UseMentorApiHost) on a test server, with probe endpoints standing
/// in for module endpoints. <see cref="TestIdentityHandler"/> stands in for the identity schemes unless
/// <c>testIdentity</c> is false, when the scheme Identity:Mode selects authenticates callers, Entra trusting
/// <see cref="TestEntra"/> tokens.
/// </summary>
internal sealed class ProbeHost : IAsyncDisposable
{
    /// <summary>Stands for free text a participant could type about a meeting: it must never reach a log or a response.</summary>
    public const string Note = "meeting-note-5f1c9e";

    public const string Permission = "probe-permission";
    public const string PermittedRole = "probe-role";
    public const string OtherRole = "employee";

    private readonly WebApplication app;

    private ProbeHost(WebApplication app, CapturedLogs logs)
    {
        this.app = app;
        Logs = logs;
        Client = app.GetTestClient();
    }

    public CapturedLogs Logs { get; }

    public HttpClient Client { get; }

    public static async Task<ProbeHost> StartAsync(
        IdentityMode identityMode = IdentityMode.Entra,
        IReadOnlyDictionary<string, string?>? settings = null,
        bool testIdentity = true,
        Action<WebApplication>? mapProbes = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Identity:Mode"] = identityMode.ToString(),
            ["ConnectionStrings:Mentor"] = ApiFactory.UnreachableDatabase,
        });
        builder.Configuration.AddInMemoryCollection(TestEntra.Settings.Select(setting => KeyValuePair.Create(setting.Key, (string?)setting.Value)));
        builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        builder.AddMentorApiHost();

        var logs = new CapturedLogs();
        builder.Logging.ClearProviders().AddProvider(logs);
        TestEntra.TrustSigningKey(builder.Services);
        if (testIdentity)
        {
            builder.Services.AddAuthentication(TestIdentityHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestIdentityHandler>(TestIdentityHandler.SchemeName, configureOptions: null);
        }

        builder.Services.AddAuthorizationBuilder().AddPolicy(Permission, policy => policy.RequireRole(PermittedRole));

        var app = builder.Build();
        app.UseMentorApiHost();
        MapProbes(app);
        mapProbes?.Invoke(app);
        await app.StartAsync(TestContext.Current.CancellationToken);
        return new ProbeHost(app, logs);
    }

    public static HttpRequestMessage Request(HttpMethod method, string route, string? roles = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (roles is not null)
        {
            request.Headers.Add(TestIdentityHandler.RolesHeader, roles);
        }

        return request;
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) =>
        Client.SendAsync(request, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.DisposeAsync();
    }

    private static void MapProbes(WebApplication app)
    {
        app.MapGet("/probe/undeclared", () => "reached");
        app.MapGet("/probe/bare", () => "reached").RequireAuthorization();
        app.MapGet("/probe/permitted", () => "reached").RequireAuthorization(Permission);
        app.MapGet("/probe/public", () => "reached").AllowAnonymous();
        app.MapPost("/probe/notes/{id:guid}", (Guid id, string? search, ProbeNote note) => TypedResults.NoContent()).AllowAnonymous();
        app.MapGet("/probe/count", (int count) => count).AllowAnonymous();
        app.MapGet("/probe/throws", string () => throw Failure()).AllowAnonymous();
        app.MapGet("/probe/fails-after-start", async (HttpContext context) =>
        {
            await context.Response.WriteAsync("partial", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            throw Failure();
        }).AllowAnonymous();
    }

    // A fault whose messages quote request content, as a parse failure would.
    private static InvalidOperationException Failure() => new(Note, new FormatException(Note));
}

internal sealed record ProbeNote(string Text);
