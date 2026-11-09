using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mentor.Tests.Integration.Support;
using Microsoft.Extensions.Hosting;

namespace Mentor.Tests.Integration.Relationships;

// CONTRACT-MENTORSHIP-API, ADR-012: the OpenAPI document the frontend client is generated from publishes exactly what an
// attribute edit accepts: each attribute's business values, exactly as the server accepts them, or null for an attribute
// left out, and none of the three required, since an edit names any subset; nothing else may be named.
public sealed class RelationshipContractTests
{
    [Fact]
    public async Task The_contract_lists_exactly_the_values_an_edit_accepts_and_requires_none_of_the_attributes()
    {
        await using var api = new ApiFactory(Environments.Development);

        var response = await api.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var body = document.GetProperty("paths").GetProperty("/api/relationships/{relationshipId}").GetProperty("patch").GetProperty("requestBody")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.Equal("#/components/schemas/RelationshipAttributesRequest", body.GetProperty("$ref").GetString());
        var schema = document.GetProperty("components").GetProperty("schemas").GetProperty("RelationshipAttributesRequest");
        Assert.False(schema.TryGetProperty("required", out var required) && required.GetArrayLength() > 0);
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["isActive", "state", "status"], schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["InProgress", "OnHold", "Complete", null], ValuesOf(schema, "state"));
        Assert.Equal(["Blank", "NeedsReassigning", null], ValuesOf(schema, "status"));
        Assert.Equal(["Y", "N", null], ValuesOf(schema, "isActive"));
    }

    private static List<string?> ValuesOf(JsonElement schema, string property) =>
        [.. schema.GetProperty("properties").GetProperty(property).GetProperty("enum").EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.Null ? null : value.GetString())];
}
