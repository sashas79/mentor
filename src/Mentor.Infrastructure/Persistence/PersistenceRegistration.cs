using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Mentor.Infrastructure.Persistence;

public sealed class MentorDatabaseOptions
{
    public string? ConnectionString { get; set; }
}

public static class PersistenceRegistration
{
    /// <summary>
    /// Registers <see cref="MentorDbContext"/> on ConnectionStrings:Mentor. The host refuses to start without it, and in
    /// the Production environment without TLS. The API does not migrate the schema: migrations run under the schema
    /// owner, while the API connects as a member of <see cref="MentorDatabase.ApplicationRole"/>.
    /// </summary>
    public static IServiceCollection AddMentorPersistence(this IServiceCollection services)
    {
        services.AddOptions<MentorDatabaseOptions>()
            .Configure<IConfiguration>((options, configuration) =>
                options.ConnectionString = configuration.GetConnectionString(MentorDatabase.ConnectionStringName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MentorDatabaseOptions>, MentorDatabaseOptionsValidator>();
        services.AddDbContext<MentorDbContext>((provider, options) =>
            options.UseMentorDatabase(provider.GetRequiredService<IOptions<MentorDatabaseOptions>>().Value.ConnectionString!));
        return services;
    }
}

internal sealed class MentorDatabaseOptionsValidator(IHostEnvironment environment) : IValidateOptions<MentorDatabaseOptions>
{
    private const string Key = $"ConnectionStrings:{MentorDatabase.ConnectionStringName}";

    private static readonly SslMode[] EncryptedModes = [SslMode.Require, SslMode.VerifyCA, SslMode.VerifyFull];

    // The messages never repeat the connection string, which can hold a password.
    public ValidateOptionsResult Validate(string? name, MentorDatabaseOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return ValidateOptionsResult.Fail($"{Key} is required.");
        }

        NpgsqlConnectionStringBuilder connection;
        try
        {
            connection = new NpgsqlConnectionStringBuilder(options.ConnectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return ValidateOptionsResult.Fail($"{Key} is not a valid PostgreSQL connection string.");
        }

        // The store is protected in transit (NFR-009): production connections must be encrypted.
        if (environment.IsProduction() && !EncryptedModes.Contains(connection.SslMode))
        {
            return ValidateOptionsResult.Fail($"{Key} must set SSL Mode to Require, VerifyCA or VerifyFull in the Production environment.");
        }

        return ValidateOptionsResult.Success;
    }
}
