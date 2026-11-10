import { expect } from "@/api/client";
import { useIdentity } from "@/identity/IdentityProvider";
import { Empty, Outcome, Page, Show } from "@/ui/components";
import { useCommand, useLoad } from "@/ui/hooks";
import { when } from "@/ui/labels";

/** Talent Management's decisions on pending mentor applications (FR-008): only approved mentors appear in discovery. */
export function MentorApplications() {
  const { api } = useIdentity();
  const pending = useLoad(() => expect(api.GET("/api/mentor-applications/pending")), []);
  const command = useCommand();

  const decide = (id: string, name: string, approve: boolean) =>
    void command.run(
      async () => {
        const options = { params: { path: { id } } };
        await expect(approve ? api.POST("/api/mentor-applications/{id}/approve", options) : api.POST("/api/mentor-applications/{id}/decline", options));
        await pending.reload();
      },
      approve ? `${name} is approved as a mentor.` : `${name}'s application is declined.`,
      pending.reload,
    );

  return (
    <Page title="Mentor applications" intro="Applications awaiting your decision. An approved mentor appears to every protégé.">
      <Outcome command={command} />
      <Show loaded={pending}>
        {(applications) =>
          applications.length === 0 ? (
            <Empty>No application awaits a decision.</Empty>
          ) : (
            <ul className="list">
              {applications.map((application) => (
                <li key={application.id}>
                  <div className="row">
                    <strong>{application.displayName}</strong>
                    <span className="muted">
                      {application.roleTitle ?? application.levelTitle ?? ""} — applied {when(application.createdAt)}
                    </span>
                  </div>
                  <p>{application.biography}</p>
                  {application.industryExperience ? <p className="muted">Industry: {application.industryExperience}</p> : null}
                  {application.mentoringExperience ? <p className="muted">Mentoring: {application.mentoringExperience}</p> : null}
                  <div className="row">
                    <button type="button" disabled={command.pending} onClick={() => decide(application.id, application.displayName, true)}>
                      Approve
                    </button>
                    <button type="button" className="secondary" disabled={command.pending} onClick={() => decide(application.id, application.displayName, false)}>
                      Decline
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          )
        }
      </Show>
    </Page>
  );
}
