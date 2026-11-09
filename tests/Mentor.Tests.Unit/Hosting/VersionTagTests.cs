using Mentor.Api.Hosting;
using Microsoft.Extensions.Primitives;

namespace Mentor.Tests.Unit.Hosting;

// ADR-005: a version goes out as a strong ETag and comes back in If-Match as exactly that.
public sealed class VersionTagTests
{
    [Fact]
    public void A_version_is_written_as_a_strong_entity_tag()
    {
        Assert.Equal("\"7\"", VersionTag.Of(7));
    }

    [Theory]
    [InlineData("\"1\"", 1)]
    [InlineData(" \"42\" ", 42)]
    [InlineData("\"0\"", 0)]
    public void If_match_naming_one_strong_tag_names_its_version(string ifMatch, long expected)
    {
        Assert.True(VersionTag.TryRead(new StringValues(ifMatch), out var version));

        Assert.Equal(expected, version);
    }

    // A change is decided on one known state: a wildcard, a weak tag, a list or anything but a version names none.
    [Theory]
    [InlineData("*")]
    [InlineData("W/\"1\"")]
    [InlineData("1")]
    [InlineData("\"1\", \"2\"")]
    [InlineData("\"\"")]
    [InlineData("\"-1\"")]
    [InlineData("\"one\"")]
    [InlineData("")]
    public void If_match_naming_anything_else_names_no_version(string ifMatch)
    {
        Assert.False(VersionTag.TryRead(new StringValues(ifMatch), out _));
    }

    [Fact]
    public void Absent_or_repeated_if_match_names_no_version()
    {
        Assert.False(VersionTag.TryRead(StringValues.Empty, out _));
        Assert.False(VersionTag.TryRead(new StringValues(["\"1\"", "\"2\""]), out _));
    }
}
