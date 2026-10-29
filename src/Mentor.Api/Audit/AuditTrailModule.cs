namespace Mentor.Api.Audit;

/// <summary>
/// The audit trail module (COMP-AUDIT-TRAIL): the <see cref="AuditTrail"/> every command appends to, and the
/// administrator's read-only endpoints.
/// </summary>
public static class AuditTrailModule
{
    public static IServiceCollection AddAuditTrail(this IServiceCollection services)
    {
        services.AddScoped<AuditTrail>();
        return services;
    }

    public static WebApplication MapAuditTrail(this WebApplication app)
    {
        AuditTrailEndpoints.Map(app);
        return app;
    }
}
