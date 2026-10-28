using System.Security.Cryptography;
using Mentor.Api.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Mentor.Tests.Integration.Support;

/// <summary>
/// A stand-in Entra tenant: the AzureAd settings of the API, and access tokens signed with a key the API is told to
/// trust instead of fetching the tenant's metadata. Everything else Microsoft.Identity.Web validates as it would in
/// production: issuer, audience, signature, lifetime, and the scope and object identifier the API requires.
/// </summary>
internal static class TestEntra
{
    public static readonly Guid TenantId = Guid.Parse("7d1b6c2e-3f4a-4b5c-9d6e-0f1a2b3c4d5e");
    public static readonly Guid ClientId = Guid.Parse("c0ffee00-1234-4abc-8def-0123456789ab");
    public const string Scope = "Mentor.Access";

    public static readonly string Issuer = $"https://login.microsoftonline.com/{TenantId}/v2.0";

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "tenant-signing-key" };

    /// <summary>A key the tenant does not publish, standing for a forged signature.</summary>
    public static readonly RsaSecurityKey ForeignKey = new(RSA.Create(2048)) { KeyId = "tenant-signing-key" };

    public static IReadOnlyDictionary<string, string> Settings { get; } = new Dictionary<string, string>
    {
        ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
        ["AzureAd:TenantId"] = TenantId.ToString(),
        ["AzureAd:ClientId"] = ClientId.ToString(),
        ["AzureAd:Scopes"] = Scope,
    };

    /// <summary>Trusts the tenant's signing key without contacting Entra; does nothing when the Entra scheme is absent.</summary>
    public static void TrustSigningKey(IServiceCollection services) =>
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            configuration.SigningKeys.Add(SigningKey);
            options.Configuration = configuration;
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        });

    /// <summary>An access token as the tenant issues it to the SPA for a signed-in employee.</summary>
    public static string Token(
        Guid objectId,
        string name = "Test Employee",
        string[]? roles = null,
        string? scope = Scope,
        string? audience = null,
        string? issuer = null,
        Guid? tenant = null,
        DateTime? expires = null,
        SecurityKey? key = null,
        bool includeObjectId = true)
    {
        var claims = new Dictionary<string, object>
        {
            ["tid"] = (tenant ?? TenantId).ToString(),
            ["name"] = name,
            ["ver"] = "2.0",
        };
        if (includeObjectId)
        {
            claims["oid"] = objectId.ToString();
        }

        if (scope is not null)
        {
            claims["scp"] = scope;
        }

        if (roles is not null)
        {
            claims["roles"] = roles;
        }

        var expiry = expires ?? DateTime.UtcNow.AddHours(1);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? (tenant is { } other ? $"https://login.microsoftonline.com/{other}/v2.0" : Issuer),
            Audience = audience ?? ClientId.ToString(),
            IssuedAt = expiry.AddHours(-2),
            NotBefore = expiry.AddHours(-2),
            Expires = expiry,
            Claims = claims,
            SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.RsaSha256),
        });
    }

    /// <summary>A token for a Talent Management administrator, by app-role assignment.</summary>
    public static string AdministratorToken(Guid objectId, string name = "Test Administrator") =>
        Token(objectId, name, roles: [MentorPrincipal.TalentManagementAppRole]);
}
