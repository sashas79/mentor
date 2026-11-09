using System.Security.Claims;
using Mentor.Api.Authorisation;
using Mentor.Api.Employees;
using Mentor.Api.Hosting;
using Mentor.Api.Requests;
using Mentor.Core.Relationships;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.OpenApi;

namespace Mentor.Api.Relationships;

/// <summary>
/// The endpoints of formed relationship administration (COMP-RELATIONSHIP-ADMIN, CONTRACT-MENTORSHIP-API). The parties
/// read their own relationships and Talent Management reads any; Talent Management alone edits State, Status and IsActive
/// (ADR-003). A relationship's version is its ETag, and an edit names it in If-Match (ADR-005). No route creates or
/// deletes a relationship or changes anything but those three attributes: relationships are formed by COMP-FORMATION and
/// entered as legacy rows by reviewed scripts (ADR-008).
/// </summary>
public static class RelationshipEndpoints
{
    public const string RelationshipsRoute = "/api/relationships";
    public const string RelationshipRoute = "/api/relationships/{relationshipId:guid}";

    /// <summary>The problem (409) of an edit taken on a version that is no longer the relationship's.</summary>
    public const string RelationshipChanged = "Relationship changed";

    /// <summary>The problem (409) of an edit whose every named attribute already holds the value named.</summary>
    public const string RelationshipUnchanged = "Relationship unchanged";

    private const string Tag = "Relationships";

    public static IServiceCollection AddRelationshipAdministration(this IServiceCollection services)
    {
        services.AddScoped<RelationshipAdministration>();
        services.ConfigureAll<OpenApiOptions>(options => options.AddSchemaTransformer(AttributeValueSchemas.TransformAsync));
        return services;
    }

    public static IEndpointRouteBuilder MapRelationshipAdministration(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(RelationshipsRoute, ListOwnAsync)
            .WithName("ListOwnRelationships")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.Relationship.Read);
        endpoints.MapGet(RelationshipRoute, GetAsync)
            .WithName("GetRelationship")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.Relationship.Read);
        endpoints.MapPatch(RelationshipRoute, EditAsync)
            .WithName("UpdateRelationshipAttributes")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.Relationship.UpdateAttributes);
        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<RelationshipView>>> ListOwnAsync(
        ClaimsPrincipal user, RelationshipAdministration relationships, CancellationToken cancellationToken) =>
        TypedResults.Ok(await relationships.ListOwnAsync(user, cancellationToken));

    private static async Task<Results<Ok<RelationshipView>, NotFound, ForbidHttpResult>> GetAsync(
        Guid relationshipId, ClaimsPrincipal user, RelationshipAdministration relationships, HttpResponse response, CancellationToken cancellationToken)
    {
        var result = await relationships.ReadAsync(user, relationshipId, cancellationToken);
        if (result.Value is { } relationship)
        {
            VersionTag.Write(response, relationship.Version);
            return TypedResults.Ok(relationship);
        }

        return result.Refusal == RelationshipRefusal.NotFound ? TypedResults.NotFound() : TypedResults.Forbid();
    }

    private static async Task<Results<Ok<RelationshipView>, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult>> EditAsync(
        Guid relationshipId, RelationshipAttributesRequest attributes, ClaimsPrincipal user, RelationshipAdministration relationships, HttpRequest request,
        HttpResponse response, CancellationToken cancellationToken)
    {
        if (!RelationshipEdit.TryCreate(attributes.State, attributes.Status, attributes.IsActive, out var edit, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var version = VersionTag.TryRead(request.Headers.IfMatch, out var decidedOn) ? decidedOn : (long?)null;
        var result = await relationships.EditAsync(user, relationshipId, version, edit, cancellationToken);
        if (result.Value is { } relationship)
        {
            VersionTag.Write(response, relationship.Version);
            return TypedResults.Ok(relationship);
        }

        return result.Refusal switch
        {
            RelationshipRefusal.NotFound => TypedResults.NotFound(),
            RelationshipRefusal.Forbidden => TypedResults.Forbid(),
            RelationshipRefusal.NoEmployeeRecord => EmployeeRecordRequired.Problem(),
            RelationshipRefusal.VersionRequired => Problem(StatusCodes.Status428PreconditionRequired, MentorshipRequestEndpoints.VersionRequired,
                "An edit names the relationship version it was taken on: send the ETag of the relationship, as last read, in If-Match."),
            RelationshipRefusal.Stale => Problem(StatusCodes.Status409Conflict, RelationshipChanged,
                "The relationship has changed since that version was read; read it again and decide on its current values."),
            RelationshipRefusal.Unchanged => Problem(StatusCodes.Status409Conflict, RelationshipUnchanged,
                "Every attribute named already holds the value named; nothing was changed."),
            _ => throw new InvalidOperationException($"Refusal {result.Refusal} is not answered."),
        };
    }

    private static ProblemHttpResult Problem(int status, string title, string detail) =>
        TypedResults.Problem(statusCode: status, title: title, detail: detail);
}
