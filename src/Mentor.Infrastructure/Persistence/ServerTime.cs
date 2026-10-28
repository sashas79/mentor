using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mentor.Infrastructure.Persistence;

/// <summary>
/// Times the database assigns (ADR-013). EF Core never sends them, so no client value can reach them, and reads back
/// what the database wrote; the database's triggers overwrite any value a raw statement supplies.
/// </summary>
internal static class ServerTime
{
    /// <summary>Set once, when the row is inserted.</summary>
    public static PropertyBuilder<DateTimeOffset> IsInsertTime(this PropertyBuilder<DateTimeOffset> property)
    {
        property.HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        return property.NeverSent();
    }

    /// <summary>Set when the row is inserted and again whenever it is updated.</summary>
    public static PropertyBuilder<DateTimeOffset> IsUpdateTime(this PropertyBuilder<DateTimeOffset> property)
    {
        property.HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();
        return property.NeverSent();
    }

    /// <summary>Set by a database trigger when the row is inserted.</summary>
    public static PropertyBuilder<DateTimeOffset?> IsInsertTime(this PropertyBuilder<DateTimeOffset?> property)
    {
        property.ValueGeneratedOnAdd();
        return property.NeverSent();
    }

    private static PropertyBuilder<TProperty> NeverSent<TProperty>(this PropertyBuilder<TProperty> property)
    {
        property.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        property.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        return property;
    }
}
