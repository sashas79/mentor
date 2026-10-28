namespace Mentor.Api.Hosting;

/// <summary>
/// The prototype facilities this deployment runs with. The frontend reads them before sign-in to offer the
/// simulated-identity selector and to label simulated identity and simulated notification delivery
/// (ADR-002, ADR-009, ADR-012).
/// </summary>
public sealed record Capabilities(IdentityMode IdentityMode, NotificationDelivery NotificationDelivery);
