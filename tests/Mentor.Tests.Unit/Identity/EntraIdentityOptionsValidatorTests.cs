using Mentor.Api.Identity;

namespace Mentor.Tests.Unit.Identity;

public sealed class EntraIdentityOptionsValidatorTests
{
    private static EntraIdentityOptions Complete() => new()
    {
        TenantId = "7d1b6c2e-3f4a-4b5c-9d6e-0f1a2b3c4d5e",
        ClientId = "c0ffee00-1234-4abc-8def-0123456789ab",
        Scopes = "Mentor.Access",
    };

    [Fact]
    public void Complete_configuration_is_accepted()
    {
        Assert.True(new EntraIdentityOptionsValidator().Validate(null, Complete()).Succeeded);
    }

    [Theory]
    [InlineData(nameof(EntraIdentityOptions.TenantId), null)]
    [InlineData(nameof(EntraIdentityOptions.TenantId), "common")]
    [InlineData(nameof(EntraIdentityOptions.TenantId), "organizations")]
    [InlineData(nameof(EntraIdentityOptions.ClientId), " ")]
    [InlineData(nameof(EntraIdentityOptions.Scopes), "")]
    [InlineData(nameof(EntraIdentityOptions.Instance), "http://login.microsoftonline.com/")]
    public void Incomplete_configuration_is_refused_and_the_refusal_names_the_setting(string setting, string? value)
    {
        var options = Complete();
        typeof(EntraIdentityOptions).GetProperty(setting)!.SetValue(options, value);

        var result = new EntraIdentityOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains($"AzureAd:{setting}", result.FailureMessage);
    }

    [Fact]
    public void Only_the_company_tenant_issues_accepted_tokens()
    {
        Assert.Equal(
            ["https://login.microsoftonline.com/7d1b6c2e-3f4a-4b5c-9d6e-0f1a2b3c4d5e/v2.0", "https://sts.windows.net/7d1b6c2e-3f4a-4b5c-9d6e-0f1a2b3c4d5e/"],
            Complete().Issuers);
    }
}
