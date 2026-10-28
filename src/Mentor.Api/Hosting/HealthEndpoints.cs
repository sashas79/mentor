namespace Mentor.Api.Hosting;

/// <summary>Health endpoints for container probes; both answer without authentication (ADR-003).</summary>
public static class HealthEndpoints
{
    /// <summary>Answers while the process can serve requests; it runs no check.</summary>
    public const string Live = "/health/live";

    /// <summary>Answers Healthy only while every check tagged <see cref="ReadyTag"/> passes.</summary>
    public const string Ready = "/health/ready";

    /// <summary>Tag on a dependency's health check that makes readiness depend on it.</summary>
    public const string ReadyTag = "ready";
}
