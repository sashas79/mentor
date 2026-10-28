using Mentor.Infrastructure.Persistence;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Mentor.Tests.Unit.Persistence;

public sealed class MentorDatabaseOptionsValidatorTests
{
    private const string Password = "s3cret-password";

    [Theory]
    [InlineData("Production", "Require", true)]
    [InlineData("Production", "VerifyCA", true)]
    [InlineData("Production", "VerifyFull", true)]
    [InlineData("Production", "Prefer", false)]
    [InlineData("Production", "Allow", false)]
    [InlineData("Production", "Disable", false)]
    [InlineData("Development", "Disable", true)]
    [InlineData("Prototype", "Prefer", true)]
    public void Production_connections_must_be_encrypted(string environment, string sslMode, bool permitted)
    {
        var result = Validate(environment, $"Host=db;Database=mentor;Username=mentor_api;Password={Password};SSL Mode={sslMode}");

        Assert.Equal(permitted, result.Succeeded);
    }

    [Fact]
    public void Production_connections_without_an_ssl_mode_are_refused()
    {
        Assert.False(Validate("Production", "Host=db;Database=mentor").Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Host=db;Not A Keyword=1")]
    public void A_missing_or_unreadable_connection_string_is_refused(string? connectionString)
    {
        Assert.False(Validate("Development", connectionString).Succeeded);
    }

    [Fact]
    public void A_refusal_never_repeats_the_connection_string()
    {
        var result = Validate("Production", $"Host=db;Password={Password};SSL Mode=Disable");

        Assert.DoesNotContain(Password, result.FailureMessage);
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(string environment, string? connectionString) =>
        new MentorDatabaseOptionsValidator(new Environment(environment)).Validate(null, new MentorDatabaseOptions { ConnectionString = connectionString });

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Mentor.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
