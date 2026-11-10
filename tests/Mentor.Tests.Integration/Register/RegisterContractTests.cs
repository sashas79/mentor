using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mentor.Tests.Integration.Support;
using Microsoft.Extensions.Hosting;

namespace Mentor.Tests.Integration.Register;

// CONTRACT-MENTORSHIP-API, CONTRACT-REGISTER-EXPORT, ADR-012: the OpenAPI document the frontend client is generated from
// publishes the register's three read-only routes. The export is a CSV file, and a register row names the spreadsheet
// fields, the provenance, the source and the review exception, and nothing of goals, progress or agreements.
public sealed class RegisterContractTests
{
    [Fact]
    public async Task The_contract_publishes_the_register_its_csv_export_and_the_goals_view_as_reads()
    {
        await using var api = new ApiFactory(Environments.Development);

        var response = await api.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var paths = document.GetProperty("paths");
        foreach (var route in RegisterApi.Routes)
        {
            var path = paths.GetProperty(route);
            Assert.Equal(["get"], path.EnumerateObject().Select(operation => operation.Name).Where(name => name != "parameters"));
            Assert.False(path.GetProperty("get").TryGetProperty("requestBody", out _));
        }

        var exported = paths.GetProperty(RegisterApi.Export).GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("content");
        Assert.Contains(exported.EnumerateObject(), content => content.Name.StartsWith("text/csv", StringComparison.Ordinal));

        var schemas = document.GetProperty("components").GetProperty("schemas");
        Assert.Equal(
            ["durationCategory", "endDate", "isActive", "kind", "mentor", "protege", "provenance", "relationshipId", "reviewException", "reviewExceptionId", "source", "startDate", "state", "status"],
            schemas.GetProperty("RegisterEntryView").GetProperty("properties").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["Relationship", "HeldSourceRow"], schemas.GetProperty("RegisterEntryKind").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(
            ["activeText", "durationText", "endText", "file", "mentorText", "protegeText", "row", "startText"],
            schemas.GetProperty("RegisterSourceView").GetProperty("properties").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }
}
