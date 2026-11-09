using System.Globalization;
using Microsoft.Extensions.Primitives;

namespace Mentor.Api.Hosting;

/// <summary>
/// The version of an aggregate head on the wire (ADR-005). Every response for a candidate or a relationship carries its
/// version as a strong ETag, and every change to one names, in If-Match, the version the caller decided on. A change
/// without a usable If-Match is refused with 428; one naming a version that is no longer current, with 409.
/// </summary>
public static class VersionTag
{
    public static string Of(long version) => $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";

    public static void Write(HttpResponse response, long version) => response.Headers.ETag = Of(version);

    /// <summary>
    /// The version an If-Match header names: exactly one strong entity tag holding a version. A wildcard, a weak tag or a
    /// list names none, since a change is decided on one known state.
    /// </summary>
    public static bool TryRead(StringValues ifMatch, out long version)
    {
        version = 0;
        if (ifMatch.Count != 1 || ifMatch[0]?.Trim() is not { Length: > 2 } tag || tag[0] != '"' || tag[^1] != '"')
        {
            return false;
        }

        return long.TryParse(tag.AsSpan(1, tag.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out version);
    }
}
