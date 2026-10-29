using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Mentor.Core.Audit;

namespace Mentor.Infrastructure.Persistence;

/// <summary>
/// Writes <see cref="AuditValues"/> as the JSON object the jsonb columns of audit_entry hold: enumerations under the
/// values the database stores them as (ADR-013), so that a value in the trail reads exactly like the column it came
/// from; dates as ISO 8601.
/// </summary>
public static class AuditValuesJson
{
    public static string? Serialize(AuditValues? values)
    {
        if (values is null)
        {
            return null;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (field, value) in values.Fields)
            {
                writer.WritePropertyName(field);
                switch (value)
                {
                    case null:
                        writer.WriteNullValue();
                        break;
                    case Enum enumeration:
                        writer.WriteStringValue(EnumColumn.StoredValue(enumeration));
                        break;
                    case string text:
                        writer.WriteStringValue(text);
                        break;
                    case bool flag:
                        writer.WriteBooleanValue(flag);
                        break;
                    case int number:
                        writer.WriteNumberValue(number);
                        break;
                    case long number:
                        writer.WriteNumberValue(number);
                        break;
                    case Guid id:
                        writer.WriteStringValue(id);
                        break;
                    case DateOnly date:
                        writer.WriteStringValue(date.ToString("O", CultureInfo.InvariantCulture));
                        break;
                    case DateTimeOffset time:
                        writer.WriteStringValue(time);
                        break;
                    default:
                        throw new ArgumentException($"Field '{field}' holds a {value.GetType().Name}, which the trail does not store.", nameof(values));
                }
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
