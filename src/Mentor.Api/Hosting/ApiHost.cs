using System.Text.Json.Serialization;
using Mentor.Api.Authorisation;
using Mentor.Api.Identity;
using Mentor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Net.Http.Headers;

namespace Mentor.Api.Hosting;

/// <summary>
/// Composition root of the API host (COMP-API-HOST, ADR-001): the HTTP pipeline every module shares.
/// </summary>
public static class ApiHost
{
    /// <summary>When true the OpenAPI document (CONTRACT-MENTORSHIP-API) is served; it is off unless configured.</summary>
    public const string OpenApiSwitch = "OpenApi:Enabled";

    /// <summary>Origins of the separately deployed frontend (ADR-012) allowed to call the API from a browser.</summary>
    public const string CorsOriginsSection = "Cors:AllowedOrigins";

    public static WebApplicationBuilder AddMentorApiHost(this WebApplicationBuilder builder)
    {
        builder.Logging.AddRedactedLogging();

        var services = builder.Services;
        services.AddPrototypeSwitches();
        services.AddMentorPersistence();
        builder.AddMentorIdentity();
        services.AddDefaultDenyAuthorization();
        services.AddAuthorisationMatrix();
        services.AddProblemDetails();
        services.AddExceptionHandler<RedactingExceptionHandler>();
        // A malformed parameter or body is answered with 400 in every environment instead of an exception.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IConfiguration>((cors, configuration) =>
            cors.AddDefaultPolicy(policy => policy
                .WithOrigins(configuration.GetSection(CorsOriginsSection).Get<string[]>() ?? [])
                .AllowAnyHeader()
                .AllowAnyMethod()
                // The client reads the ETag it returns in If-Match (ADR-005).
                .WithExposedHeaders(HeaderNames.ETag, HeaderNames.Location)));
        services.AddHealthChecks().AddDbContextCheck<MentorDbContext>("database", tags: [HealthEndpoints.ReadyTag]);
        services.AddOpenApi();
        return builder;
    }

    public static WebApplication UseMentorApiHost(this WebApplication app)
    {
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHealthChecks(HealthEndpoints.Live, new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();
        app.MapHealthChecks(HealthEndpoints.Ready, new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthEndpoints.ReadyTag) })
            .AllowAnonymous();
        app.MapCapabilities();
        app.MapMentorIdentity();
        if (app.Configuration.GetValue<bool>(OpenApiSwitch))
        {
            app.MapOpenApi().AllowAnonymous();
        }

        return app;
    }
}
