using System.Text.Json.Nodes;
using Mentor.Core.Relationships;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Mentor.Api.Relationships;

/// <summary>
/// Publishes in the OpenAPI document (CONTRACT-MENTORSHIP-API) exactly what an attribute edit accepts. The body binds the
/// values as text, so that only the exact business values pass (<see cref="RelationshipEdit"/>), which on its own would
/// publish three unconstrained strings. This lists each attribute's values on its property, with null for an attribute the
/// edit leaves out, and marks every property optional, since an edit names any subset of them; a client generated from the
/// document (ADR-012) offers exactly the values the server accepts.
/// </summary>
internal static class AttributeValueSchemas
{
    private static readonly IReadOnlyDictionary<string, string[]> Values = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["state"] = Enum.GetNames<RelationshipState>(),
        ["status"] = Enum.GetNames<RelationshipStatus>(),
        ["isActive"] = Enum.GetNames<ActiveFlag>(),
    };

    public static Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);
        if (context.JsonPropertyInfo is { } property)
        {
            if (property.DeclaringType == typeof(RelationshipAttributesRequest) && Values.TryGetValue(property.Name, out var names))
            {
                schema.Enum = [.. names.Select(name => (JsonNode)JsonValue.Create(name)), null!];
            }
        }
        else if (context.JsonTypeInfo.Type == typeof(RelationshipAttributesRequest))
        {
            schema.Required?.Clear();
        }

        return Task.CompletedTask;
    }
}
