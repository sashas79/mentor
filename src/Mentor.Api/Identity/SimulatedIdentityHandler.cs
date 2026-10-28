using System.Text.Encodings.Web;
using Mentor.Core.Audit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Mentor.Api.Identity;

/// <summary>
/// The labelled prototype identity scheme (ADR-002). A request names a person of the <see cref="SimulatedRoster"/> by
/// object identifier in <see cref="HeaderName"/>, and is authenticated as that person with the role the roster declares
/// and an identity scheme of Simulated. It is registered only under Identity:Mode=Simulated, which the Production
/// environment refuses, and it is never production authentication.
/// </summary>
internal sealed class SimulatedIdentityHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Simulated";
    public const string HeaderName = "X-Simulated-Identity";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var header))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Guid.TryParse(header.ToString(), out var objectId) || SimulatedRoster.Find(objectId) is not { } person)
        {
            return Task.FromResult(AuthenticateResult.Fail("The header names nobody on the simulated roster."));
        }

        var principal = MentorPrincipal.Create(
            person.ObjectId,
            person.DisplayName,
            person.DeclaredRole == DeclaredRole.TalentManagementAdministrator,
            IdentityScheme.Simulated,
            SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
