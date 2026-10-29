using System.Collections.Frozen;
using System.Text;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Mentor.Infrastructure.Persistence;

/// <summary>
/// Enumerations are text columns holding the values the architecture names (ADR-013): the member InProgress is stored
/// as IN_PROGRESS. The same values make the column's check constraint, so the two cannot drift apart.
/// </summary>
internal static class EnumColumn
{
    public static PropertyBuilder<TEnum> StoredAsText<TEnum>(this PropertyBuilder<TEnum> property)
        where TEnum : struct, Enum => property.HasConversion(Converter<TEnum>.Instance);

    public static PropertyBuilder<TEnum?> StoredAsText<TEnum>(this PropertyBuilder<TEnum?> property)
        where TEnum : struct, Enum => property.HasConversion(Converter<TEnum>.Instance);

    /// <summary>The check constraint expression that limits <paramref name="column"/> to the values of <typeparamref name="TEnum"/>.</summary>
    public static string Check<TEnum>(string column)
        where TEnum : struct, Enum =>
        $"{column} IN ({string.Join(", ", Enum.GetValues<TEnum>().Select(value => $"'{StoredValue(value)}'"))})";

    public static string StoredValue<TEnum>(TEnum value)
        where TEnum : struct, Enum => Converter<TEnum>.ToStored[value];

    /// <summary>The stored value of an enumeration member whose type is known only at run time, by the same rule.</summary>
    public static string StoredValue(Enum value) => ToUpperSnakeCase(value.ToString());

    public static TEnum FromStoredValue<TEnum>(string stored)
        where TEnum : struct, Enum => Converter<TEnum>.FromStored[stored];

    private static string ToUpperSnakeCase(string name)
    {
        var stored = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
            {
                stored.Append('_');
            }

            stored.Append(char.ToUpperInvariant(name[i]));
        }

        return stored.ToString();
    }

    private static class Converter<TEnum>
        where TEnum : struct, Enum
    {
        public static readonly FrozenDictionary<TEnum, string> ToStored =
            Enum.GetValues<TEnum>().ToFrozenDictionary(value => value, value => ToUpperSnakeCase(value.ToString()));

        public static readonly FrozenDictionary<string, TEnum> FromStored =
            ToStored.ToFrozenDictionary(pair => pair.Value, pair => pair.Key);

        public static readonly ValueConverter<TEnum, string> Instance =
            new(value => StoredValue(value), stored => FromStoredValue<TEnum>(stored));
    }
}
