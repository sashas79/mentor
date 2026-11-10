import { Link } from "react-router-dom";
import { expect, type Schemas } from "@/api/client";
import { useIdentity } from "@/identity/IdentityProvider";
import { Badge, Empty, Outcome, Page, Show } from "@/ui/components";
import { useCommand, useLoad } from "@/ui/hooks";
import { label, when } from "@/ui/labels";

type Notification = Schemas["NotificationView"];

/** Where the record a notification concerns is opened, by the subject type the server names (CONTRACT-NOTIFICATION-EVENTS). */
export function subjectLink(notification: Pick<Notification, "subjectType" | "subjectId">): string | null {
  switch (notification.subjectType) {
    case "CANDIDATE":
      return `/candidates/${notification.subjectId}`;
    case "MENTOR_PROFILE":
      return "/mentor-profile";
    case "DEVELOPMENT_GOAL":
      return `/goals/${notification.subjectId}`;
    default:
      return null;
  }
}

/**
 * The caller's own notification inbox (FR-057, ADR-009). Each event names the material action, the role it reached the
 * caller in and the next action required of them (AC-FR-057-01). Delivery is in-app, and labelled simulated when the
 * deployment simulates it (AC-FR-057-02). The server lists only the caller's own events (AC-FR-057-03).
 */
export function Notifications() {
  const { api } = useIdentity();
  const inbox = useLoad(() => expect(api.GET("/api/notifications")), []);
  const command = useCommand();

  return (
    <Page title="Notifications" intro="Workflow events addressed to you, newest first, with what each asks you to do next.">
      <Outcome command={command} />
      <Show loaded={inbox}>
        {(found) => (
          <>
            <p className="muted">
              Delivery: {found.delivery === "Simulated" ? <Badge tone="warn">Simulated — shown in the application only</Badge> : label(found.delivery)}
            </p>
            {found.events.length === 0 ? (
              <Empty>You have no notifications.</Empty>
            ) : (
              <ul className="list">
                {found.events.map((event) => {
                  const link = subjectLink(event);
                  return (
                    <li key={event.id}>
                      <div className="row">
                        <strong>{label(event.eventType)}</strong>
                        {event.readAt ? <Badge>Read</Badge> : <Badge tone="info">Unread</Badge>}
                        <span className="muted">to you as {label(event.recipientRole)}</span>
                      </div>
                      <p>
                        Next: {event.requiredAction === "None" ? <span className="muted">nothing is required of you</span> : <strong>{label(event.requiredAction)}</strong>}
                      </p>
                      <div className="row">
                        <span className="muted">{when(event.createdAt)}</span>
                        {link ? <Link to={link}>Open</Link> : null}
                        {!event.readAt ? (
                          <button
                            type="button"
                            className="secondary"
                            disabled={command.pending}
                            onClick={() =>
                              void command.run(
                                async () => {
                                  await expect(api.POST("/api/notifications/{notificationId}/read", { params: { path: { notificationId: event.id } } }));
                                  await inbox.reload();
                                },
                                "Marked as read.",
                              )
                            }
                          >
                            Mark as read
                          </button>
                        ) : null}
                      </div>
                    </li>
                  );
                })}
              </ul>
            )}
          </>
        )}
      </Show>
    </Page>
  );
}
