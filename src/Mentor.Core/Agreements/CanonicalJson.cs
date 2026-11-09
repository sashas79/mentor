using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Mentor.Core.Agreements;

/// <summary>
/// The canonical form of the JSON an agreement's content identity is computed over (CONTRACT-AGREEMENT-CONTENT): the
/// JSON Canonicalization Scheme of RFC 8785, for the values an agreement holds. Object members are written in the
/// ordinal order of their names (UTF-16 code units) and arrays in their own order, with no whitespace; a string escapes
/// only the quotation mark, the reverse solidus and the control characters, writing every other character as itself;
/// numbers are integers. Whatever spacing, member order or escaping a document was transmitted in, its canonical form
/// is the same, so whoever holds the content can recompute its hash.
/// </summary>
public static class CanonicalJson
{
    // RFC 8785 serialises numbers as IEEE 754 doubles; beyond 2^53 an integer is no longer exact.
    private const long MaxExactInteger = 9_007_199_254_740_991;

    /// <summary>The canonical form of <paramref name="json"/>; <see cref="FormatException"/> when it has none.</summary>
    public static string Canonicalize(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return Canonicalize(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new FormatException("The text is not JSON.", exception);
        }
    }

    /// <summary>
    /// The canonical form of <paramref name="element"/>. <see cref="FormatException"/> when an object repeats a member
    /// name, a number is not an exact integer or a string holds an unpaired surrogate: none of these has one canonical
    /// form, and none occurs in an agreement.
    /// </summary>
    public static string Canonicalize(JsonElement element)
    {
        var canonical = new StringBuilder();
        Write(canonical, element);
        return canonical.ToString();
    }

    /// <summary>Whether <paramref name="json"/> is written exactly in its canonical form.</summary>
    public static bool IsCanonical(string json)
    {
        try
        {
            return string.Equals(Canonicalize(json), json, StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void Write(StringBuilder canonical, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(canonical, element);
                break;
            case JsonValueKind.Array:
                canonical.Append('[');
                var first = true;
                foreach (var item in element.EnumerateArray())
                {
                    if (!first)
                    {
                        canonical.Append(',');
                    }

                    first = false;
                    Write(canonical, item);
                }

                canonical.Append(']');
                break;
            case JsonValueKind.String:
                WriteString(canonical, String(element));
                break;
            case JsonValueKind.Number:
                if (!element.TryGetInt64(out var number) || number is > MaxExactInteger or < -MaxExactInteger)
                {
                    throw new FormatException("A number of the document is not an exact integer.");
                }

                canonical.Append(number.ToString(CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.True:
                canonical.Append("true");
                break;
            case JsonValueKind.False:
                canonical.Append("false");
                break;
            case JsonValueKind.Null:
                canonical.Append("null");
                break;
            default:
                throw new FormatException("The document holds a value that is not JSON.");
        }
    }

    private static void WriteObject(StringBuilder canonical, JsonElement element)
    {
        var members = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var member in element.EnumerateObject())
        {
            if (!members.TryAdd(member.Name, member.Value))
            {
                throw new FormatException("An object of the document repeats a member name.");
            }
        }

        canonical.Append('{');
        var first = true;
        foreach (var (name, value) in members)
        {
            if (!first)
            {
                canonical.Append(',');
            }

            first = false;
            WriteString(canonical, name);
            canonical.Append(':');
            Write(canonical, value);
        }

        canonical.Append('}');
    }

    private static string String(JsonElement element)
    {
        try
        {
            return element.GetString()!;
        }
        catch (InvalidOperationException exception)
        {
            throw new FormatException("A string of the document is not valid text.", exception);
        }
    }

    private static void WriteString(StringBuilder canonical, string value)
    {
        canonical.Append('"');
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            switch (character)
            {
                case '"':
                    canonical.Append("\\\"");
                    break;
                case '\\':
                    canonical.Append("\\\\");
                    break;
                case '\b':
                    canonical.Append("\\b");
                    break;
                case '\f':
                    canonical.Append("\\f");
                    break;
                case '\n':
                    canonical.Append("\\n");
                    break;
                case '\r':
                    canonical.Append("\\r");
                    break;
                case '\t':
                    canonical.Append("\\t");
                    break;
                case < ' ':
                    canonical.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default:
                    if (char.IsHighSurrogate(character) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                    {
                        canonical.Append(character).Append(value[++index]);
                    }
                    else if (char.IsSurrogate(character))
                    {
                        throw new FormatException("A string of the document holds an unpaired surrogate.");
                    }
                    else
                    {
                        canonical.Append(character);
                    }

                    break;
            }
        }

        canonical.Append('"');
    }
}
