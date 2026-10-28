using Microsoft.Extensions.Options;

namespace Mentor.Api.Hosting;

/// <summary>How callers are authenticated (ADR-002). Simulated is a labelled prototype facility, never production authentication.</summary>
public enum IdentityMode
{
    Entra,
    Simulated,
}

/// <summary>How notification events reach their recipient (ADR-009). Simulated is the labelled in-app inbox of the prototype.</summary>
public enum NotificationDelivery
{
    None,
    Simulated,
}

/// <summary>The Identity:Mode switch. Absent configuration means Entra.</summary>
public sealed class IdentityModeOptions
{
    public const string Section = "Identity";

    public IdentityMode Mode { get; set; } = IdentityMode.Entra;
}

/// <summary>The Notifications:Delivery switch. Absent configuration means None.</summary>
public sealed class NotificationDeliveryOptions
{
    public const string Section = "Notifications";

    public NotificationDelivery Delivery { get; set; } = NotificationDelivery.None;
}

/// <summary>Binds the prototype switches and refuses to start the host on a setting they do not allow.</summary>
internal static class PrototypeSwitches
{
    public static IServiceCollection AddPrototypeSwitches(this IServiceCollection services)
    {
        services.AddOptions<IdentityModeOptions>().BindConfiguration(IdentityModeOptions.Section).ValidateOnStart();
        services.AddOptions<NotificationDeliveryOptions>().BindConfiguration(NotificationDeliveryOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdentityModeOptions>, ProductionDisablesSimulation>();
        services.AddSingleton<IValidateOptions<NotificationDeliveryOptions>, ProductionDisablesSimulation>();
        return services;
    }
}

/// <summary>Production configuration disables every simulated facility (COMP-API-HOST).</summary>
internal sealed class ProductionDisablesSimulation(IHostEnvironment environment)
    : IValidateOptions<IdentityModeOptions>, IValidateOptions<NotificationDeliveryOptions>
{
    public ValidateOptionsResult Validate(string? name, IdentityModeOptions options) =>
        Check($"{IdentityModeOptions.Section}:Mode", options.Mode, IdentityMode.Simulated);

    public ValidateOptionsResult Validate(string? name, NotificationDeliveryOptions options) =>
        Check($"{NotificationDeliveryOptions.Section}:Delivery", options.Delivery, NotificationDelivery.Simulated);

    private ValidateOptionsResult Check<TSetting>(string key, TSetting setting, TSetting simulated)
        where TSetting : struct, Enum
    {
        if (!Enum.IsDefined(setting))
        {
            return ValidateOptionsResult.Fail($"{key} '{setting}' is not a recognised setting.");
        }

        if (environment.IsProduction() && setting.Equals(simulated))
        {
            return ValidateOptionsResult.Fail($"{key}={simulated} is a prototype facility and is not permitted in the Production environment.");
        }

        return ValidateOptionsResult.Success;
    }
}
