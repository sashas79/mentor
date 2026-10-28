using Mentor.Api.Hosting;
using Mentor.Core.Notifications;
using Mentor.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Mentor.Api.Notifications;

/// <summary>
/// Raises the notification events of a material workflow action (ADR-009, CONTRACT-NOTIFICATION-EVENTS). A command
/// raises them into its own unit of work, the scoped <see cref="MentorDbContext"/>, so they commit, or roll back, with
/// the action and its audit entries; nothing is written here. Delivery is the configured Notifications:Delivery:
/// Simulated records an in-app delivery at the moment of the action, which the prototype's inbox shows as such; None
/// records the recipient's pending action without claiming any delivery, which is what a production transport would
/// consume. No e-mail, Teams, push, queue, scheduler or reminder exists.
/// </summary>
public sealed class NotificationEvents(MentorDbContext db, IOptions<NotificationDeliveryOptions> options)
{
    /// <summary>How this deployment delivers events, reported on /api/capabilities and with every inbox.</summary>
    public NotificationDelivery Delivery => options.Value.Delivery;

    /// <summary>Adds the event to the unit of work; the command's save commits it with the action.</summary>
    public NotificationEvent Raise(WorkflowNotification notification)
    {
        var @event = new NotificationEvent
        {
            EventType = notification.EventType,
            SubjectType = notification.SubjectType,
            SubjectId = notification.SubjectId,
            RecipientEmployeeId = notification.RecipientEmployeeId,
            RecipientRole = notification.RecipientRole,
            RequiredAction = notification.RequiredAction,
            Delivery = Delivery,
        };
        db.NotificationEvents.Add(@event);
        return @event;
    }

    public IReadOnlyList<NotificationEvent> Raise(IEnumerable<WorkflowNotification> notifications) =>
        [.. notifications.Select(Raise)];
}

internal static class NotificationRegistration
{
    public static IServiceCollection AddNotificationEvents(this IServiceCollection services)
    {
        services.AddScoped<NotificationEvents>();
        return services;
    }
}
