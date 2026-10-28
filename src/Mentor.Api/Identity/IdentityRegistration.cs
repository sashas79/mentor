using Mentor.Api.Hosting;
using Microsoft.AspNetCore.Authentication;

namespace Mentor.Api.Identity;

/// <summary>
/// Registers the one identity scheme that Identity:Mode selects (ADR-002). The schemes never coexist: under Entra no
/// simulated header is read, and under Simulated no bearer token is. An unrecognised mode registers neither, and
/// <see cref="PrototypeSwitches"/> refuses to start the host.
/// </summary>
internal static class IdentityRegistration
{
    public static WebApplicationBuilder AddMentorIdentity(this WebApplicationBuilder builder)
    {
        switch (ModeOf(builder.Configuration))
        {
            case IdentityMode.Entra:
                builder.Services.AddEntraIdentity(builder.Configuration);
                break;
            case IdentityMode.Simulated:
                builder.Services.AddAuthentication(SimulatedIdentityHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, SimulatedIdentityHandler>(SimulatedIdentityHandler.SchemeName, configureOptions: null);
                break;
        }

        builder.Services.AddScoped<CallerResolver>();
        return builder;
    }

    public static WebApplication MapMentorIdentity(this WebApplication app)
    {
        if (ModeOf(app.Configuration) == IdentityMode.Simulated)
        {
            app.MapSimulatedRoster();
        }

        app.MapSession();
        return app;
    }

    // Read the way IdentityModeOptions binds it: absent means Entra.
    private static IdentityMode? ModeOf(IConfiguration configuration)
    {
        var value = configuration[$"{IdentityModeOptions.Section}:{nameof(IdentityModeOptions.Mode)}"];
        if (string.IsNullOrEmpty(value))
        {
            return IdentityMode.Entra;
        }

        return Enum.TryParse<IdentityMode>(value, ignoreCase: true, out var mode) && Enum.IsDefined(mode) ? mode : null;
    }
}
