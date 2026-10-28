using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mentor.Api.Hosting;
using Mentor.Tests.Integration.Support;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Mentor.Tests.Integration.Hosting;

public sealed class PrototypeSwitchTests
{
    [Fact]
    public async Task Without_configuration_the_api_reports_entra_identity_and_no_simulated_delivery()
    {
        await using var api = new ApiFactory(Environments.Production);

        var capabilities = await CapabilitiesOf(api);

        Assert.Equal("Entra", capabilities.GetProperty("identityMode").GetString());
        Assert.Equal("None", capabilities.GetProperty("notificationDelivery").GetString());
    }

    [Fact]
    public async Task A_prototype_run_reports_its_simulated_facilities_so_the_frontend_can_label_them()
    {
        await using var api = new ApiFactory(Environments.Development, new Dictionary<string, string>
        {
            ["Identity:Mode"] = "Simulated",
            ["Notifications:Delivery"] = "Simulated",
        });

        var capabilities = await CapabilitiesOf(api);

        Assert.Equal("Simulated", capabilities.GetProperty("identityMode").GetString());
        Assert.Equal("Simulated", capabilities.GetProperty("notificationDelivery").GetString());
    }

    [Theory]
    [InlineData("Identity:Mode")]
    [InlineData("Notifications:Delivery")]
    public async Task Production_refuses_to_start_with_a_simulated_facility(string simulatedSwitch)
    {
        await using var api = new ApiFactory(Environments.Production, new Dictionary<string, string> { [simulatedSwitch] = "Simulated" });

        var failure = Record.Exception(() => api.CreateClient());

        var refusal = Assert.IsType<OptionsValidationException>(FirstInChain<OptionsValidationException>(failure));
        Assert.Contains($"{simulatedSwitch}=Simulated", refusal.Message);
    }

    private static async Task<JsonElement> CapabilitiesOf(ApiFactory api)
    {
        var response = await api.GetAsync(CapabilitiesEndpoint.Route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    // The host may wrap the validation failure; anything else is returned as is so the assertion shows it.
    private static Exception? FirstInChain<TException>(Exception? exception)
        where TException : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException)
            {
                return current;
            }
        }

        return exception;
    }
}
