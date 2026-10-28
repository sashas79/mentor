using System.Security.Claims;
using Mentor.Api.Authorisation;
using Mentor.Api.Identity;
using Mentor.Core.Notifications;
using Mentor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Mentor.Api.Notifications;

/// <summary>
/// A notification event as its recipient sees it (CONTRACT-NOTIFICATION-EVENTS): the material action, the record it
/// concerns, the role the recipient holds towards it, the action required of them, and how and when it was delivered.
/// </summary>
public sealed record NotificationView(
    Guid Id,
    NotificationEventType EventType,
    string SubjectType,
    Guid SubjectId,
    Guid RecipientEmployeeId,
    RecipientRole RecipientRole,
    RequiredAction RequiredAction,
    NotificationDelivery Delivery,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? ReadAt)
{
    public static NotificationView Of(NotificationEvent notification) => new(
        notification.Id,
        notification.EventType,
        notification.SubjectType,
        notification.SubjectId,
        notification.RecipientEmployeeId,
        notification.RecipientRole,
        notification.RequiredAction,
        notification.Delivery,
        notification.CreatedAt,
        notification.DeliveredAt,
        notification.ReadAt);
}

/// <summary>
/// The caller's inbox (ADR-009): their own events, newest first, and <see cref="Delivery"/>, how this deployment
/// delivers them, so the frontend labels a Simulated inbox as simulated delivery.
/// </summary>
public sealed record NotificationInbox(NotificationDelivery Delivery, IReadOnlyList<NotificationView> Events);

/// <summary>
/// The in-app inbox (COMP-MEETINGS-NOTIFICATIONS, FR-057). The intended recipient alone reads their events and marks
/// them read; nobody else, a Talent Management administrator included, retrieves another recipient's pending-action
/// details (ADR-003). The read marker is the only change the API makes to an event, and it is set once, by the server.
/// </summary>
internal static class NotificationEndpoints
{
    public const string Route = "/api/notifications";
    public const string NotificationRoute = "/api/notifications/{notificationId:guid}";
    public const string MarkReadRoute = "/api/notifications/{notificationId:guid}/read";

    public static IEndpointRouteBuilder MapNotifications(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Route, ListAsync)
            .WithName("ListNotifications")
            .WithTags("Notifications")
            .RequirePermission(AuthorisationMatrix.Notification.Read);
        endpoints.MapGet(NotificationRoute, GetAsync)
            .WithName("GetNotification")
            .WithTags("Notifications")
            .RequirePermission(AuthorisationMatrix.Notification.Read);
        endpoints.MapPost(MarkReadRoute, MarkReadAsync)
            .WithName("MarkNotificationRead")
            .WithTags("Notifications")
            .RequirePermission(AuthorisationMatrix.Notification.MarkRead);
        return endpoints;
    }

    // The caller's own events only; a caller without an employee record yet has none. unread=true leaves out the read.
    private static async Task<Ok<NotificationInbox>> ListAsync(
        bool? unread, ClaimsPrincipal user, CallerResolver callers, NotificationEvents notifications, MentorDbContext db, CancellationToken cancellationToken)
    {
        var caller = await callers.ResolveAsync(user, cancellationToken);
        IReadOnlyList<NotificationView> events = caller?.EmployeeId is { } recipient
            ? [.. (await db.NotificationEvents.AsNoTracking()
                    .Where(notification => notification.RecipientEmployeeId == recipient && (unread != true || notification.ReadAt == null))
                    .OrderByDescending(notification => notification.CreatedAt)
                    .ThenBy(notification => notification.Id)
                    .ToListAsync(cancellationToken))
                .Select(NotificationView.Of)]
            : [];
        return TypedResults.Ok(new NotificationInbox(notifications.Delivery, events));
    }

    private static async Task<Results<Ok<NotificationView>, NotFound, ForbidHttpResult>> GetAsync(
        Guid notificationId, ClaimsPrincipal user, IAuthorizationService authorization, MentorDbContext db, CancellationToken cancellationToken)
    {
        var notification = await db.NotificationEvents.AsNoTracking().SingleOrDefaultAsync(notification => notification.Id == notificationId, cancellationToken);
        if (notification is null)
        {
            return TypedResults.NotFound();
        }

        if (!await authorization.IsPermittedAsync(user, AuthorisationMatrix.Notification.Read, ProtectedRecord.Of(notification)))
        {
            return TypedResults.Forbid();
        }

        return TypedResults.Ok(NotificationView.Of(notification));
    }

    // Marking read is idempotent: the first marking's server time stands.
    private static async Task<Results<Ok<NotificationView>, NotFound, ForbidHttpResult>> MarkReadAsync(
        Guid notificationId, ClaimsPrincipal user, IAuthorizationService authorization, MentorDbContext db, CancellationToken cancellationToken)
    {
        var notification = await db.NotificationEvents.AsNoTracking().SingleOrDefaultAsync(notification => notification.Id == notificationId, cancellationToken);
        if (notification is null)
        {
            return TypedResults.NotFound();
        }

        if (!await authorization.IsPermittedAsync(user, AuthorisationMatrix.Notification.MarkRead, ProtectedRecord.Of(notification)))
        {
            return TypedResults.Forbid();
        }

        if (notification.ReadAt is null)
        {
            // read_at is the one column the application may update (ADR-013), and the database clock sets it.
            await db.NotificationEvents
                .Where(notification => notification.Id == notificationId && notification.ReadAt == null)
                .ExecuteUpdateAsync(read => read.SetProperty(notification => notification.ReadAt, _ => DateTimeOffset.UtcNow), cancellationToken);
            notification = await db.NotificationEvents.AsNoTracking().SingleAsync(notification => notification.Id == notificationId, cancellationToken);
        }

        return TypedResults.Ok(NotificationView.Of(notification));
    }
}
