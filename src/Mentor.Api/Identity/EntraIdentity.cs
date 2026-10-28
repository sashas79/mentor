using System.Security.Claims;
using Mentor.Core.Audit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace Mentor.Api.Identity;

/// <summary>
/// The AzureAd section (CONTRACT-ENTRA-IDENTITY): the company tenant, the API's app registration and the delegated scopes
/// the frontend requests. Microsoft.Identity.Web binds the same section.
/// </summary>
public sealed class EntraIdentityOptions
{
    public const string Section = "AzureAd";

    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    /// <summary>The company tenant. Only its own tokens are accepted, so it is a tenant id, never common or organizations.</summary>
    public string? TenantId { get; set; }

    /// <summary>The API's application (client) id; tokens must name it, or api://ClientId, as their audience.</summary>
    public string? ClientId { get; set; }

    /// <summary>The delegated scopes, separated by spaces, of which a token must carry at least one.</summary>
    public string? Scopes { get; set; }

    public IReadOnlyList<string> AcceptedScopes =>
        (Scopes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The issuers the tenant signs access tokens as: v2.0 endpoint and v1.0 endpoint.</summary>
    public IReadOnlyList<string> Issuers =>
        [$"{Instance.TrimEnd('/')}/{TenantId}/v2.0", $"https://sts.windows.net/{TenantId}/"];
}

/// <summary>Production authentication (ADR-002): Entra ID bearer tokens validated by Microsoft.Identity.Web.</summary>
internal static class EntraIdentity
{
    public static void AddEntraIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(EntraIdentityOptions.Section);
        services.AddOptions<EntraIdentityOptions>().Bind(section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<EntraIdentityOptions>, EntraIdentityOptionsValidator>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(_ => { }, section.Bind, JwtBearerDefaults.AuthenticationScheme);

        // Runs after Microsoft.Identity.Web has configured the scheme, so that it wraps the checks Microsoft.Identity.Web
        // chains onto token validation rather than being replaced by them.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .PostConfigure<IOptions<EntraIdentityOptions>>((jwt, entra) =>
            {
                // Issuer, audience, signature and lifetime are all validated; the issuers are the company tenant's alone.
                jwt.TokenValidationParameters.ValidIssuers = entra.Value.Issuers;

                var validated = jwt.Events.OnTokenValidated;
                jwt.Events.OnTokenValidated = async context =>
                {
                    await validated(context);
                    if (context.Result is null)
                    {
                        Normalise(context, entra.Value.AcceptedScopes);
                    }
                };
            });
    }

    // Replaces the token's principal with the shape both schemes share. A token with no object identifier, or one that
    // was not issued to a signed-in employee through a delegated scope of this API (an app-only token, say), is refused.
    private static void Normalise(TokenValidatedContext context, IReadOnlyList<string> acceptedScopes)
    {
        var token = context.Principal!;
        if (!Guid.TryParse(FirstValue(token, "oid", "http://schemas.microsoft.com/identity/claims/objectidentifier"), out var objectId))
        {
            context.Fail("The token carries no Entra object identifier.");
            return;
        }

        var scopes = token.FindAll(claim => claim.Type is "scp" or "http://schemas.microsoft.com/identity/claims/scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (!scopes.Intersect(acceptedScopes, StringComparer.Ordinal).Any())
        {
            context.Fail("The token carries no delegated scope that this API accepts.");
            return;
        }

        var talentManagement = token.FindAll(claim => claim.Type is "roles" or ClaimTypes.Role)
            .Any(claim => claim.Value == MentorPrincipal.TalentManagementAppRole);
        var displayName = FirstValue(token, "name", "preferred_username") ?? string.Empty;
        context.Principal = MentorPrincipal.Create(objectId, displayName, talentManagement, IdentityScheme.Entra, JwtBearerDefaults.AuthenticationScheme);
    }

    private static string? FirstValue(ClaimsPrincipal principal, params string[] claimTypes) =>
        claimTypes.Select(principal.FindFirstValue).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

/// <summary>Refuses to start in Entra mode without the configuration that validating a token needs.</summary>
internal sealed class EntraIdentityOptionsValidator : IValidateOptions<EntraIdentityOptions>
{
    private const string Hint = "set it, or set Identity:Mode=Simulated for a prototype run outside Production";

    public ValidateOptionsResult Validate(string? name, EntraIdentityOptions options)
    {
        var failures = new List<string>();
        if (!Uri.TryCreate(options.Instance, UriKind.Absolute, out var instance) || instance.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add($"{EntraIdentityOptions.Section}:Instance must be an https URL.");
        }

        if (!Guid.TryParse(options.TenantId, out _))
        {
            failures.Add($"{EntraIdentityOptions.Section}:TenantId must be the company tenant id; {Hint}.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add($"{EntraIdentityOptions.Section}:ClientId is required; {Hint}.");
        }

        if (options.AcceptedScopes.Count == 0)
        {
            failures.Add($"{EntraIdentityOptions.Section}:Scopes must name the delegated scope the frontend requests; {Hint}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
