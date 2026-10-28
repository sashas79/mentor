using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mentor.Tests.Integration.Support;

internal static class ProblemAssert
{
    /// <summary>Asserts an RFC 9457 problem with the given status and returns its body.</summary>
    public static async Task<JsonElement> IsProblem(HttpStatusCode status, HttpResponseMessage response)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
        return problem;
    }
}
