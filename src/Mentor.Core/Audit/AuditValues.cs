namespace Mentor.Core.Audit;

/// <summary>
/// The changed fields of the affected record before or after an action, as the trail stores them (ADR-007): named
/// structured values only. A field holds an identifier, an enumeration, a flag, a number, a date, a time or a short
/// reference such as a content hash or an administrative reference. The trail has no place for narrative text, and
/// nothing said in a meeting exists in the store to be copied into it (NFR-002). Field names are the camel-case
/// property names of the record (state, isActive, currentRevisionId), so that the prior and new values of one field
/// line up.
/// </summary>
public sealed class AuditValues
{
    private readonly List<KeyValuePair<string, object?>> fields = [];

    /// <summary>The fields in the order they were set; a value is null, a string, a bool, an int, a long, a Guid, a DateOnly, a DateTimeOffset or an Enum.</summary>
    public IReadOnlyList<KeyValuePair<string, object?>> Fields => fields;

    /// <summary>An enumerated column, stored under the value the database stores it as.</summary>
    public AuditValues Set(string field, Enum? value) => Add(field, value);

    public AuditValues Set(string field, Guid? value) => Add(field, value);

    public AuditValues Set(string field, bool? value) => Add(field, value);

    public AuditValues Set(string field, int? value) => Add(field, value);

    public AuditValues Set(string field, long? value) => Add(field, value);

    public AuditValues Set(string field, DateOnly? value) => Add(field, value);

    public AuditValues Set(string field, DateTimeOffset? value) => Add(field, value);

    /// <summary>A short reference (a content hash, an administrative reference), never narrative text.</summary>
    public AuditValues Set(string field, string? value) => Add(field, value);

    private AuditValues Add(string field, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        if (fields.Any(existing => existing.Key == field))
        {
            throw new ArgumentException($"Field '{field}' is already set.", nameof(field));
        }

        fields.Add(new KeyValuePair<string, object?>(field, value));
        return this;
    }
}
