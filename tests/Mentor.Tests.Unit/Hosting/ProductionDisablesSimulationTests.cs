using Mentor.Api.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Mentor.Tests.Unit.Hosting;

public sealed class ProductionDisablesSimulationTests
{
    [Theory]
    [InlineData("Production", IdentityMode.Entra, true)]
    [InlineData("Production", IdentityMode.Simulated, false)]
    [InlineData("Development", IdentityMode.Simulated, true)]
    [InlineData("Prototype", IdentityMode.Simulated, true)]
    public void Simulated_identity_is_refused_only_in_production(string environment, IdentityMode mode, bool permitted)
    {
        var result = Validator(environment).Validate(null, new IdentityModeOptions { Mode = mode });

        Assert.Equal(permitted, result.Succeeded);
    }

    [Theory]
    [InlineData("Production", NotificationDelivery.None, true)]
    [InlineData("Production", NotificationDelivery.Simulated, false)]
    [InlineData("Development", NotificationDelivery.Simulated, true)]
    public void Simulated_notification_delivery_is_refused_only_in_production(string environment, NotificationDelivery delivery, bool permitted)
    {
        var result = Validator(environment).Validate(null, new NotificationDeliveryOptions { Delivery = delivery });

        Assert.Equal(permitted, result.Succeeded);
    }

    [Fact]
    public void The_refusal_names_the_switch_to_change()
    {
        var result = Validator("Production").Validate(null, new IdentityModeOptions { Mode = IdentityMode.Simulated });

        Assert.Contains("Identity:Mode=Simulated", result.FailureMessage);
    }

    [Fact]
    public void An_undefined_setting_is_refused()
    {
        var result = Validator("Development").Validate(null, new IdentityModeOptions { Mode = (IdentityMode)7 });

        Assert.False(result.Succeeded);
    }

    private static ProductionDisablesSimulation Validator(string environment) => new(new Environment(environment));

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Mentor.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
