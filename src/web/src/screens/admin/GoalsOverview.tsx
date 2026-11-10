import { Link } from "react-router-dom";
import { expect } from "@/api/client";
import { useIdentity } from "@/identity/IdentityProvider";
import { Badge, Empty, Page, Section, Show } from "@/ui/components";
import { useLoad } from "@/ui/hooks";
import { label, when } from "@/ui/labels";

/**
 * Talent Management's structured view of every mentorship's goals and progress (FR-040, FR-066, AC-FR-040-01). It shows
 * each goal's completion status, the progress submitted against it and the mentor's decisions. Nothing said in a
 * meeting is stored, so nothing of it appears.
 */
export function GoalsOverview() {
  const { api } = useIdentity();
  const overview = useLoad(() => expect(api.GET("/api/register/goals")), []);

  return (
    <Page title="Goals and progress" intro="Structured goals and progress of every mentorship. Meeting discussions are never recorded.">
      <Show loaded={overview}>
        {(relationships) =>
          relationships.length === 0 ? (
            <Empty>There are no mentorships yet.</Empty>
          ) : (
            relationships.map((relationship) => (
              <Section
                key={relationship.relationshipId}
                title={`${relationship.protege.displayName} mentored by ${relationship.mentor.displayName}`}
                actions={<Link to={`/relationships/${relationship.relationshipId}`}>Open</Link>}
              >
                <p className="row">
                  <Badge>{label(relationship.provenance)}</Badge>
                  <span>
                    {relationship.summary.complete} of {relationship.summary.total} goals complete
                  </span>
                  {Number(relationship.summary.awaitingDecision) > 0 ? (
                    <Badge tone="info">{relationship.summary.awaitingDecision} awaiting the mentor's decision</Badge>
                  ) : null}
                </p>
                {relationship.goals.length === 0 ? (
                  <Empty>No agreed goals{relationship.provenance === "Legacy" ? " — a historical mentorship" : ""}.</Empty>
                ) : (
                  <ul>
                    {relationship.goals.map((goal) => (
                      <li key={goal.id}>
                        <strong>{goal.agreedDescription}</strong> — {label(goal.completionStatus)}
                        {goal.pendingCompletionRequestId ? <> · <Badge tone="info">Completion requested</Badge></> : null}
                        {goal.progress.length > 0 ? (
                          <ol>
                            {goal.progress.map((entry) => (
                              <li key={entry.id}>
                                <span className="muted">{when(entry.submittedAt)}</span> {entry.progressUpdate}
                                {entry.completionRequested ? <> · completion request: {label(entry.completion)}</> : null}
                              </li>
                            ))}
                          </ol>
                        ) : (
                          <div className="muted">No progress submitted.</div>
                        )}
                      </li>
                    ))}
                  </ul>
                )}
              </Section>
            ))
          )
        }
      </Show>
    </Page>
  );
}
