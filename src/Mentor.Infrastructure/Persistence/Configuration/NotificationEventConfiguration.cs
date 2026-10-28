using Mentor.Core.Employees;
using Mentor.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class NotificationEventConfiguration : IEntityTypeConfiguration<NotificationEvent>
{
    public void Configure(EntityTypeBuilder<NotificationEvent> builder)
    {
        builder.ToTable("notification_event", table =>
        {
            table.HasCheckConstraint("ck_notification_event_event_type", EnumColumn.Check<NotificationEventType>("event_type"));
            table.HasCheckConstraint("ck_notification_event_recipient_role", EnumColumn.Check<RecipientRole>("recipient_role"));
            table.HasCheckConstraint("ck_notification_event_required_action", EnumColumn.Check<RequiredAction>("required_action"));
            table.HasCheckConstraint("ck_notification_event_delivery", EnumColumn.Check<NotificationDelivery>("delivery"));
            // A simulated delivery happens when the event is recorded; no other delivery is claimed (ADR-009).
            table.HasCheckConstraint("ck_notification_event_delivered_at", """
                CASE delivery
                    WHEN 'SIMULATED' THEN delivered_at = created_at
                    ELSE delivered_at IS NULL
                END
                """);
        });

        builder.HasOne<Employee>().WithMany().HasForeignKey(notification => notification.RecipientEmployeeId);

        builder.Property(notification => notification.EventType).StoredAsText();
        builder.Property(notification => notification.RecipientRole).StoredAsText();
        builder.Property(notification => notification.RequiredAction).StoredAsText();
        builder.Property(notification => notification.Delivery).StoredAsText();
        builder.Property(notification => notification.CreatedAt).IsInsertTime();
        builder.Property(notification => notification.DeliveredAt).IsInsertTime();
    }
}
