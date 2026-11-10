import { Link, useParams } from "react-router-dom";
import { expect } from "@/api/client";
import { useIdentity } from "@/identity/IdentityProvider";
import { Badge, Empty, Facts, Page, Section, Show } from "@/ui/components";
import { useLoad } from "@/ui/hooks";
import { label, when } from "@/ui/labels";

/** One mentorship request and the mentors invited for it (FR-014 to FR-018). */
export function RequestDetail() {
  const { requestId = "" } = useParams();
  const { api } = useIdentity();
  const request = useLoad(() => expect(api.GET("/api/mentorship-requests/{requestId}", { params: { path: { requestId } } })), [requestId]);
  const candidates = useLoad(
    () => expect(api.GET("/api/mentorship-requests/{requestId}/candidates", { params: { path: { requestId } } })),
    [requestId],
  );

  return (
    <Show loaded={request}>
      {(found) => (
        <Page title={`Mentorship request of ${when(found.submittedAt)}`}>
          <Section title="Request">
            <Facts
              items={[
                ["Protégé", found.protegeDisplayName],
                ["Standing", <Badge>{label(found.disposition)}</Badge>],
              ]}
            />
            <h3>Motivation</h3>
            <p>{found.motivation}</p>
            <h3>Technical and career development goals</h3>
            <p>{found.developmentGoals}</p>
            <h3>Expectations beyond technical skills</h3>
            <p>{found.nontechnicalExpectations}</p>
          </Section>
          <Section title="Invited mentors" actions={<Link to="/mentors">Find a mentor to invite</Link>}>
            <Show loaded={candidates}>
              {(invited) =>
                invited.length === 0 ? (
                  <Empty>No mentor has been invited for this request yet.</Empty>
                ) : (
                  <ul className="list">
                    {invited.map((candidate) => (
                      <li key={candidate.id} className="row">
                        <Link to={`/candidates/${candidate.id}`}>{candidate.mentorDisplayName}</Link>
                        <Badge>{label(candidate.status)}</Badge>
                        <span className="muted">invited {when(candidate.invitedAt)}</span>
                      </li>
                    ))}
                  </ul>
                )
              }
            </Show>
          </Section>
        </Page>
      )}
    </Show>
  );
}
