using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Mentor.Api.Hosting;

/// <summary>
/// Authenticates nobody. It is the default scheme until an identity scheme for the configured Identity:Mode
/// (ADR-002) is registered, so every protected endpoint refuses every caller with 401 rather than failing.
/// </summary>
internal sealed class NoIdentityHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "NoIdentity";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
}
