using Mentor.Core.Agreements;

namespace Mentor.Tests.Unit.Agreements;

// CONTRACT-AGREEMENT-CONTENT: the content identity is the hash of one canonical form (RFC 8785), so the same content has
// the same hash however it was spaced, ordered or escaped, and whoever holds it as JSON can recompute that hash.
public sealed class CanonicalJsonTests
{
    [Theory]
    [InlineData("""{ "b" : 1, "a" : [ true, false, null ] }""", """{"a":[true,false,null],"b":1}""")]
    // Ordinal order of UTF-16 code units: "B" (0x42) < "_" (0x5F) < "a" (0x61) < "aa".
    [InlineData("""{"aa":4,"a":2,"_":3,"B":1}""", """{"B":1,"_":3,"a":2,"aa":4}""")]
    [InlineData("""{"outer":{"z":1,"y":{"x":2,"w":3}}}""", """{"outer":{"y":{"w":3,"x":2},"z":1}}""")]
    // Arrays keep their order: a goal's position is part of the content.
    [InlineData("""[ 3, 1, 2 ]""", """[3,1,2]""")]
    [InlineData("""{"n":-0,"m":-12}""", """{"m":-12,"n":0}""")]
    public void Members_are_ordered_by_name_and_nothing_else_is_written(string json, string canonical)
    {
        Assert.Equal(canonical, CanonicalJson.Canonicalize(json));
    }

    [Theory]
    // Escaped characters outside ASCII are written as themselves, a pair of surrogates included.
    [InlineData("\"prot\\u00e9g\\u00e9 \\ud83d\\ude00\"", "\"protégé 😀\"")]
    [InlineData("\"tab\\tline\\nquote\\\"reverse\\\\\"", "\"tab\\tline\\nquote\\\"reverse\\\\\"")]
    [InlineData("\"\\b\\f\\r\"", "\"\\b\\f\\r\"")]
    // Other control characters are \u escapes with lowercase hexadecimal digits.
    [InlineData("\"\\u001F\\u0001\"", "\"\\u001f\\u0001\"")]
    // The solidus, DEL and the line and paragraph separators need no escape.
    [InlineData("\"\\/\\u007f\\u2028\\u2029\"", "\"/\u007f\u2028\u2029\"")]
    public void A_string_escapes_only_the_quotation_mark_the_reverse_solidus_and_control_characters(string json, string canonical)
    {
        Assert.Equal(canonical, CanonicalJson.Canonicalize(json));
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("1e3")]
    [InlineData("9007199254740992")]
    [InlineData("""{"a":1,"a":2}""")]
    [InlineData("\"\\ud800\"")]
    [InlineData("not json")]
    [InlineData("""{"a":1,}""")]
    public void A_document_without_one_canonical_form_is_refused(string json)
    {
        Assert.Throws<FormatException>(() => CanonicalJson.Canonicalize(json));
        Assert.False(CanonicalJson.IsCanonical(json));
    }

    [Fact]
    public void The_canonical_form_is_its_own_canonical_form_and_nothing_else_is()
    {
        const string json = """{ "terms": { "durationCategory": "SIX_MONTHS" }, "goals": [ "Lead a design review" ] }""";

        var canonical = CanonicalJson.Canonicalize(json);

        Assert.Equal(canonical, CanonicalJson.Canonicalize(canonical));
        Assert.True(CanonicalJson.IsCanonical(canonical));
        Assert.False(CanonicalJson.IsCanonical(json));
    }
}
