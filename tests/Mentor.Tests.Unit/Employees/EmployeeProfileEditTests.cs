using System.Text.Json;
using Mentor.Api.Employees;

namespace Mentor.Tests.Unit.Employees;

// FR-006: a profile edit carries display attributes only; a body that names the identity key or the record id is
// malformed, not quietly stripped.
public sealed class EmployeeProfileEditTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void An_edit_names_the_display_attributes_alone()
    {
        var edit = JsonSerializer.Deserialize<EmployeeProfileEdit>("""{"displayName":"Naledi Zulu","levelTitle":null}""", Web);

        Assert.Equal(new EmployeeProfileEdit("Naledi Zulu", null), edit);
    }

    [Theory]
    [InlineData("""{"displayName":"Naledi Zulu","entraObjectId":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11"}""")]
    [InlineData("""{"displayName":"Naledi Zulu","id":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11"}""")]
    [InlineData("""{"displayName":"Naledi Zulu","EntraObjectId":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11"}""")]
    public void A_body_that_names_anything_else_is_malformed(string body)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EmployeeProfileEdit>(body, Web));
    }
}
