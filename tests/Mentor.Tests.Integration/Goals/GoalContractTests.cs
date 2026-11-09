using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mentor.Tests.Integration.Support;
using Microsoft.Extensions.Hosting;

namespace Mentor.Tests.Integration.Goals;

// CONTRACT-MENTORSHIP-API, ADR-012: the OpenAPI document the frontend client is generated from publishes exactly what the
// goal commands accept: a progress entry and a completion request state their required progress update, and a decision is
// Approved or Rejected; nothing else may be named, so a generated client offers no notes or reason field.
public sealed class GoalContractTests
{
    [Fact]
    public async Task The_contract_names_the_progress_update_and_the_decision_and_nothing_else()
    {
        await using var api = new ApiFactory(Environments.Development);

        var response = await api.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        foreach (var (path, schema) in new[]
        {
            ("/api/goals/{goalId}/progress", "GoalProgressRequest"),
            ("/api/goals/{goalId}/completion-requests", "GoalProgressRequest"),
            ("/api/goals/{goalId}/completion-requests/{requestId}/decision", "GoalDecisionRequest"),
        })
        {
            var body = document.GetProperty("paths").GetProperty(path).GetProperty("post").GetProperty("requestBody").GetProperty("content")
                .GetProperty("application/json").GetProperty("schema");
            Assert.Equal($"#/components/schemas/{schema}", body.GetProperty("$ref").GetString());
        }

        var schemas = document.GetProperty("components").GetProperty("schemas");
        foreach (var (schema, property) in new[] { ("GoalProgressRequest", "progressUpdate"), ("GoalDecisionRequest", "decision") })
        {
            var request = schemas.GetProperty(schema);
            Assert.Equal([property], request.GetProperty("properties").EnumerateObject().Select(member => member.Name));
            Assert.Equal([property], request.GetProperty("required").EnumerateArray().Select(member => member.GetString()));
            Assert.False(request.GetProperty("additionalProperties").GetBoolean());
        }

        Assert.Equal(["Approved", "Rejected"], schemas.GetProperty("GoalDecision").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }
}
