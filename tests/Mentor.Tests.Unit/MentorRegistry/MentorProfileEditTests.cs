using System.Text.Json;
using Mentor.Api.MentorRegistry;

namespace Mentor.Tests.Unit.MentorRegistry;

// FR-008: an application or profile edit carries the biography and attributes only; a body that names the approval,
// the employee or the record id is malformed, not quietly stripped.
public sealed class MentorProfileEditTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void An_edit_names_the_biography_and_attributes_alone()
    {
        var edit = JsonSerializer.Deserialize<MentorProfileEdit>(
            """{"biography":"Ten years of delivery","roleTitle":"Principal Engineer","industryExperience":null,"mentoringExperience":"Two graduates"}""", Web);

        Assert.Equal(new MentorProfileEdit("Ten years of delivery", "Principal Engineer", null, "Two graduates"), edit);
    }

    [Theory]
    [InlineData("""{"biography":"Ten years of delivery","approvalStatus":"Approved"}""")]
    [InlineData("""{"biography":"Ten years of delivery","approvalDecidedByEmployeeId":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11"}""")]
    [InlineData("""{"biography":"Ten years of delivery","employeeId":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11"}""")]
    [InlineData("""{"biography":"Ten years of delivery","id":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11"}""")]
    [InlineData("""{"biography":"Ten years of delivery","ApprovalStatus":"Approved"}""")]
    public void A_body_that_names_anything_else_is_malformed(string body)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MentorProfileEdit>(body, Web));
    }
}
