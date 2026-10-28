using Mentor.Core.Notifications;
using Microsoft.Extensions.Options;

namespace Mentor.Api.Hosting;

internal static class CapabilitiesEndpoint
{
    public const string Route = "/api/capabilities";

    // Public by ADR-003: the frontend needs it before anyone has signed in.
    public static IEndpointConventionBuilder MapCapabilities(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet(Route, (IOptions<IdentityModeOptions> identity, IOptions<NotificationDeliveryOptions> notifications) =>
                TypedResults.Ok(new Capabilities(identity.Value.Mode, notifications.Value.Delivery)))
            .WithName("GetCapabilities")
            .AllowAnonymous();
}
