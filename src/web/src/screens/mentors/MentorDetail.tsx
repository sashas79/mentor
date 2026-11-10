import { useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { expect } from "@/api/client";
import { useIdentity } from "@/identity/IdentityProvider";
import { useSession } from "@/session/SessionProvider";
import { Empty, Facts, Field, Outcome, Page, Section, Show } from "@/ui/components";
import { useCommand, useLoad } from "@/ui/hooks";
import { label, when } from "@/ui/labels";

/**
 * A mentor's profile as a protégé reads it (AC-FR-010-02), and the protégé's invitation of this mentor for one of their
 * own requests (FR-018, AC-FR-018-01). Which requests may take another candidate is the server's to say.
 */
export function MentorDetail() {
  const { profileId = "" } = useParams();
  const { api } = useIdentity();
  const { can } = useSession();
  const navigate = useNavigate();
  const mentor = useLoad(() => expect(api.GET("/api/mentor-profiles/{id}", { params: { path: { id: profileId } } })), [profileId]);
  const requests = useLoad(() => (can("mentorship-request.create") ? expect(api.GET("/api/mentorship-requests")) : Promise.resolve([])), []);
  const [requestId, setRequestId] = useState("");
  const command = useCommand();

  return (
    <Show loaded={mentor}>
      {(profile) => {
        const invite = (event: FormEvent) => {
          event.preventDefault();
          void command.run(async () => {
            const candidate = await expect(
              api.POST("/api/mentorship-requests/{requestId}/candidates", {
                params: { path: { requestId } },
                body: { mentorEmployeeId: profile.employeeId },
              }),
            );
            navigate(`/candidates/${candidate.id}`);
          }, `${profile.displayName} is invited.`);
        };

        return (
          <Page title={profile.displayName}>
            <Section title="Profile">
              <Facts
                items={[
                  ["Role or title", profile.roleTitle ?? profile.levelTitle ?? <span className="unknown">Not given</span>],
                  ["Mentor since", profile.approvalDecidedAt ? when(profile.approvalDecidedAt) : label(profile.approvalStatus)],
                ]}
              />
              <h3>Biography</h3>
              <p>{profile.biography}</p>
              <h3>Industry experience</h3>
              <p>{profile.industryExperience ?? <span className="unknown">Not given</span>}</p>
              <h3>Mentoring experience</h3>
              <p>{profile.mentoringExperience ?? <span className="unknown">Not given</span>}</p>
            </Section>
            {can("candidate.create") ? (
              <Section title={`Invite ${profile.displayName}`}>
                <Show loaded={requests}>
                  {(own) =>
                    own.length === 0 ? (
                      <Empty>
                        To invite a mentor, first <Link to="/requests">submit a mentorship request</Link>.
                      </Empty>
                    ) : (
                      <form className="stack" onSubmit={invite}>
                        <Field label="For your request">
                          <select value={requestId} onChange={(event) => setRequestId(event.target.value)} required>
                            <option value="">Choose a request</option>
                            {own.map((request) => (
                              <option key={request.id} value={request.id}>
                                {when(request.submittedAt)} — {request.motivation.slice(0, 60)} ({label(request.disposition)})
                              </option>
                            ))}
                          </select>
                        </Field>
                        <Outcome command={command} />
                        <div>
                          <button type="submit" disabled={command.pending}>
                            Invite this mentor
                          </button>
                        </div>
                      </form>
                    )
                  }
                </Show>
              </Section>
            ) : null}
          </Page>
        );
      }}
    </Show>
  );
}
