import { Link } from "react-router-dom";
import { expect } from "@/api/client";
import { useIdentity } from "@/identity/IdentityProvider";
import { useSession } from "@/session/SessionProvider";
import { Empty, Facts, Page, Section, Show } from "@/ui/components";
import { useLoad } from "@/ui/hooks";
import { label, when } from "@/ui/labels";
import { subjectLink } from "./Notifications";

/** Where the signed-in employee starts: who the server knows them as, and what awaits them. */
export function Home() {
  const { api } = useIdentity();
  const { session, can } = useSession();
  const unread = useLoad(() => expect(api.GET("/api/notifications", { params: { query: { unread: true } } })), []);

  return (
    <Page title={`Welcome, ${session?.displayName ?? ""}`}>
      <Section title="You">
        <Facts
          items={[
            ["Roles", session?.roles.map(label).join(", ") ?? ""],
            ["Signed in with", label(session?.identityScheme)],
          ]}
        />
        {!session?.employeeId ? (
          <p className="message message-note">
            Save <Link to="/profile">your profile</Link> to start: requests, applications and decisions are recorded against it.
          </p>
        ) : null}
      </Section>
      <Section title="Waiting for you">
        <Show loaded={unread}>
          {(inbox) =>
            inbox.events.length === 0 ? (
              <Empty>Nothing is waiting for you.</Empty>
            ) : (
              <ul className="list">
                {inbox.events.map((event) => {
                  const link = subjectLink(event);
                  return (
                    <li key={event.id} className="row">
                      <strong>{label(event.eventType)}</strong>
                      <span>{event.requiredAction === "None" ? "For your information" : label(event.requiredAction)}</span>
                      <span className="muted">{when(event.createdAt)}</span>
                      {link ? <Link to={link}>Open</Link> : null}
                    </li>
                  );
                })}
              </ul>
            )
          }
        </Show>
        <p>
          <Link to="/notifications">All notifications</Link>
        </p>
      </Section>
      <Section title="Get started">
        <ul>
          {can("mentor-discovery.list") ? (
            <li>
              <Link to="/mentors">Browse approved mentors</Link> and <Link to="/requests">request mentorship</Link>.
            </li>
          ) : null}
          {can("mentor-application.create") ? (
            <li>
              <Link to="/mentor-profile">Apply to mentor, or maintain your mentor profile</Link>.
            </li>
          ) : null}
          {can("register.read") ? (
            <li>
              Talent Management: <Link to="/admin/register">relationship register</Link>,{" "}
              <Link to="/admin/applications">mentor applications</Link>, <Link to="/admin/candidates">pairings and formation</Link>.
            </li>
          ) : null}
        </ul>
      </Section>
    </Page>
  );
}
