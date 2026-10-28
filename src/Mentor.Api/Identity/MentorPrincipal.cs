using System.Security.Claims;
using Mentor.Core.Audit;

namespace Mentor.Api.Identity;

/// <summary>
/// The one principal shape both identity schemes produce (ADR-002), so that authorisation is identical under either:
/// the Entra object identifier, the display name, the Talent Management app role when it is held, and the scheme that
/// authenticated the caller. Nothing else a token carries reaches the application.
/// </summary>
public static class MentorPrincipal
{
    public const string ObjectIdClaim = "oid";
    public const string NameClaim = "name";
    public const string RoleClaim = "roles";

    /// <summary>Entra or Simulated; audit entries record it, and a simulated run is never production authentication.</summary>
    public const string IdentitySchemeClaim = "mentor_identity_scheme";

    /// <summary>
    /// The Entra app role, assigned in the tenant, of a Talent Management administrator; the simulated roster declares the
    /// same value. Every other functional role follows from application records (<see cref="CallerResolver"/>).
    /// </summary>
    public const string TalentManagementAppRole = "TalentManagement.Administrator";

    public static ClaimsPrincipal Create(Guid objectId, string displayName, bool talentManagement, IdentityScheme scheme, string authenticationType)
    {
        var claims = new List<Claim>
        {
            new(ObjectIdClaim, objectId.ToString()),
            new(NameClaim, displayName),
            new(IdentitySchemeClaim, scheme.ToString()),
        };
        if (talentManagement)
        {
            claims.Add(new Claim(RoleClaim, TalentManagementAppRole));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType, NameClaim, RoleClaim));
    }

    /// <summary>The caller's identity when the principal has the shape <see cref="Create"/> gives it; otherwise none.</summary>
    public static AuthenticatedIdentity? IdentityOf(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirstValue(ObjectIdClaim), out var objectId)
            || !Enum.TryParse<IdentityScheme>(principal.FindFirstValue(IdentitySchemeClaim), out var scheme)
            || scheme is not (IdentityScheme.Entra or IdentityScheme.Simulated))
        {
            return null;
        }

        return new AuthenticatedIdentity(objectId, principal.FindFirstValue(NameClaim) ?? string.Empty, scheme, principal.IsInRole(TalentManagementAppRole));
    }
}

/// <summary>What authentication established about a caller, before any application record is consulted.</summary>
public sealed record AuthenticatedIdentity(Guid ObjectId, string DisplayName, IdentityScheme Scheme, bool TalentManagementAppRole);
