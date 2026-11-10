using System.Text.Json.Serialization;
using Mentor.Api.Agreements;
using Mentor.Api.Audit;
using Mentor.Api.Authorisation;
using Mentor.Api.Employees;
using Mentor.Api.Formation;
using Mentor.Api.Goals;
using Mentor.Api.Identity;
using Mentor.Api.Meetings;
using Mentor.Api.MentorRegistry;
using Mentor.Api.Notifications;
using Mentor.Api.Register;
using Mentor.Api.Relationships;
using Mentor.Api.Requests;
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
        // Modules.
        services.AddAuditTrail(); // COMP-AUDIT-TRAIL (ADR-007)
        services.AddEmployeeProfiles(); // COMP-EMPLOYEE-PROFILE
        services.AddMentorRegistry(); // COMP-MENTOR-REGISTRY
        services.AddMentorshipRequests(); // COMP-MENTORSHIP-REQUEST
        services.AddAgreementDrafting(); // COMP-AGREEMENT-DRAFTING
        services.AddFormation(); // COMP-FORMATION
        services.AddRelationshipAdministration(); // COMP-RELATIONSHIP-ADMIN
        services.AddGoalsAndProgress(); // COMP-GOALS-PROGRESS
        services.AddRelationshipRegister(); // COMP-REGISTER-REPORTING
        services.AddNotificationEvents(); // COMP-MEETINGS-NOTIFICATIONS
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
                // The client reads the ETag it returns in If-Match (ADR-005), and the name a register export is offered under.
                .WithExposedHeaders(HeaderNames.ETag, HeaderNames.Location, HeaderNames.ContentDisposition)));
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
        // Modules.
        app.MapAuditTrail(); // COMP-AUDIT-TRAIL (ADR-007)
        app.MapEmployeeProfiles(); // COMP-EMPLOYEE-PROFILE
        app.MapMentorRegistry(); // COMP-MENTOR-REGISTRY
        app.MapMentorshipRequests(); // COMP-MENTORSHIP-REQUEST
        app.MapAgreementDrafting(); // COMP-AGREEMENT-DRAFTING
        app.MapFormation(); // COMP-FORMATION
        app.MapRelationshipAdministration(); // COMP-RELATIONSHIP-ADMIN
        app.MapGoalsAndProgress(); // COMP-GOALS-PROGRESS
        app.MapRelationshipRegister(); // COMP-REGISTER-REPORTING
        app.MapMeetings(); // COMP-MEETINGS-NOTIFICATIONS
        app.MapNotifications();
        if (app.Configuration.GetValue<bool>(OpenApiSwitch))
        {
            app.MapOpenApi().AllowAnonymous();
        }

        return app;
    }
}
