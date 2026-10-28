using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mentor.Api.Hosting;
using Mentor.Tests.Integration.Identity;
using Mentor.Tests.Integration.Persistence;

namespace Mentor.Tests.Integration.Support;

/// <summary>
/// The API as Program composes it, on a test database of its own that holds the callers of <see cref="TestCallers"/>,
/// and JSON calls to it signed in as one of them. Delivery is simulated unless the settings say otherwise, as a
/// prototype run has it.
/// </summary>
internal sealed class WorkflowApi : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly HttpClient client;

    private WorkflowApi(TestDatabase db, TestCallers callers, ApiFactory api)
    {
        Db = db;
        Callers = callers;
        Api = api;
        client = api.CreateClient();
    }

    public TestDatabase Db { get; }

    public TestCallers Callers { get; }

    public ApiFactory Api { get; }

    public static async Task<WorkflowApi> StartAsync(PostgresServer server, IdentityMode mode = IdentityMode.Entra, IReadOnlyDictionary<string, string>? settings = null)
    {
        var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, mode);
        var all = new Dictionary<string, string> { ["Notifications:Delivery"] = "Simulated" };
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            all[key] = value;
        }

        return new WorkflowApi(db, callers, IdentityApi.Start(db, mode.ToString(), all));
    }

    /// <summary>The employee record of a caller who has one.</summary>
    public Guid Employee(Profile profile) => Callers.EmployeeId(profile)!.Value;

    /// <summary>Sends the request signed in as the caller of <paramref name="profile"/>, or anonymously; an object body goes as JSON.</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, Profile? profile, object? body = null)
    {
        using var request = Callers.Request(method, route, profile);
        if (body is not null)
        {
            request.Content = body as HttpContent ?? JsonContent.Create(body, options: Json);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Sends the request and asserts the status before returning the JSON body.</summary>
    public async Task<JsonElement> JsonAsync(HttpMethod method, string route, Profile? profile, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await SendAsync(method, route, profile, body);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(expected == response.StatusCode, $"{method} {route} as {profile?.ToString() ?? "nobody"}: {response.StatusCode}, expected {expected}. {content}");
        return JsonSerializer.Deserialize<JsonElement>(content);
    }

    public async Task<HttpStatusCode> StatusAsync(HttpMethod method, string route, Profile? profile, object? body = null)
    {
        using var response = await SendAsync(method, route, profile, body);
        return response.StatusCode;
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await Api.DisposeAsync();
        await Db.DisposeAsync();
    }
}

internal static class JsonElementExtensions
{
    public static Guid Id(this JsonElement element) => element.GetProperty("id").GetGuid();

    public static Guid? OptionalGuid(this JsonElement element, string property) =>
        element.GetProperty(property).ValueKind == JsonValueKind.Null ? null : element.GetProperty(property).GetGuid();

    public static int? OptionalInt(this JsonElement element, string property) =>
        element.GetProperty(property).ValueKind == JsonValueKind.Null ? null : element.GetProperty(property).GetInt32();

    public static DateTimeOffset? OptionalTime(this JsonElement element, string property) =>
        element.GetProperty(property).ValueKind == JsonValueKind.Null ? null : element.GetProperty(property).GetDateTimeOffset();

    public static string Text(this JsonElement element, string property) => element.GetProperty(property).GetString()!;

    public static List<JsonElement> Items(this JsonElement element, string property) => [.. element.GetProperty(property).EnumerateArray()];

    public static string[] PropertyNames(this JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name).Order()];
}
