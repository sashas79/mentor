using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mentor.Api.Identity;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mentor.Tests.Integration.Identity;

/// <summary>The API as Program composes it, on a test database, and the calls the identity tests make of it.</summary>
internal static class IdentityApi
{
    /// <summary>The API on <paramref name="db"/>; <paramref name="testServices"/>, if given, are applied after everything Program registers.</summary>
    public static ApiFactory Start(TestDatabase db, string identityMode = "Entra", IReadOnlyDictionary<string, string>? settings = null, Action<IServiceCollection>? testServices = null)
    {
        var all = new Dictionary<string, string>
        {
            ["Identity:Mode"] = identityMode,
            ["ConnectionStrings:Mentor"] = db.ConnectionString(Login.App),
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            all[key] = value;
        }

        return new ApiFactory(Environments.Development, all, testServices);
    }

    public static Task<Guid> EmployeeAsync(TestDatabase db, Guid objectId, string displayName) =>
        db.ScalarAsync<Guid>(Login.Owner,
            "INSERT INTO mentor.employee (id, entra_object_id, display_name) VALUES (gen_random_uuid(), $1, $2) RETURNING id",
            objectId, displayName);

    public static Action<HttpRequestMessage> Bearer(string token) =>
        request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public static Action<HttpRequestMessage> Simulated(string objectId) =>
        request => request.Headers.TryAddWithoutValidation(SimulatedIdentityHandler.HeaderName, objectId);

    /// <summary>Signs in as the caller of the profile, with the credential of the mode the callers were recorded for.</summary>
    public static Action<HttpRequestMessage> As(TestCallers callers, Profile profile) => request =>
    {
        foreach (var (name, values) in callers.Request(HttpMethod.Get, "/", profile).Headers)
        {
            request.Headers.TryAddWithoutValidation(name, values);
        }
    };

    public static async Task<HttpResponseMessage> SendAsync(ApiFactory api, string route, Action<HttpRequestMessage>? signIn = null)
    {
        using var client = api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        signIn?.Invoke(request);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static async Task<JsonElement> SessionAsync(ApiFactory api, Action<HttpRequestMessage> signIn)
    {
        var response = await SendAsync(api, SessionEndpoint.Route, signIn);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    public static string[] Strings(this JsonElement session, string property) =>
        [.. session.GetProperty(property).EnumerateArray().Select(value => value.GetString()!)];

    public static Guid? EmployeeId(this JsonElement session) =>
        session.GetProperty("employeeId").ValueKind == JsonValueKind.Null ? null : session.GetProperty("employeeId").GetGuid();
}
