using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mentor.Api.Employees;
using Mentor.Tests.Integration.Support;

namespace Mentor.Tests.Integration.Employees;

/// <summary>The calls the profile tests make of the API, signed in the way <see cref="Identity.IdentityApi"/> signs in.</summary>
internal static class EmployeeProfileApi
{
    public static string Route(Guid id) => EmployeeProfileEndpoints.Route.Replace("{id:guid}", id.ToString(), StringComparison.Ordinal);

    public static async Task<HttpResponseMessage> CallAsync(ApiFactory api, HttpMethod method, string route, Action<HttpRequestMessage>? signIn, object? body = null)
    {
        using var client = api.CreateClient();
        using var request = new HttpRequestMessage(method, route);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        signIn?.Invoke(request);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>GET /api/employees/me, which must succeed.</summary>
    public static async Task<JsonElement> OwnProfileAsync(ApiFactory api, Action<HttpRequestMessage> signIn) =>
        await ProfileOfAsync(await CallAsync(api, HttpMethod.Get, EmployeeProfileEndpoints.OwnRoute, signIn));

    /// <summary>PUT /api/employees/me, which must succeed.</summary>
    public static async Task<JsonElement> SaveOwnProfileAsync(ApiFactory api, Action<HttpRequestMessage> signIn, string? displayName, string? levelTitle) =>
        await ProfileOfAsync(await CallAsync(api, HttpMethod.Put, EmployeeProfileEndpoints.OwnRoute, signIn, new { displayName, levelTitle }));

    /// <summary>GET /api/employees/{id}, whatever it answers.</summary>
    public static Task<HttpResponseMessage> ProfileAsync(ApiFactory api, Guid id, Action<HttpRequestMessage>? signIn) =>
        CallAsync(api, HttpMethod.Get, Route(id), signIn);

    public static async Task<JsonElement> ProfileOfAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    public static Guid Id(this JsonElement profile) => profile.GetProperty("id").GetGuid();

    public static Guid EntraObjectId(this JsonElement profile) => profile.GetProperty("entraObjectId").GetGuid();

    public static string DisplayName(this JsonElement profile) => profile.GetProperty("displayName").GetString()!;

    public static string? LevelTitle(this JsonElement profile) =>
        profile.GetProperty("levelTitle").ValueKind == JsonValueKind.Null ? null : profile.GetProperty("levelTitle").GetString();
}
