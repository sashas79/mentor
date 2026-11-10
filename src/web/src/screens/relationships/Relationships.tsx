import { Link, Navigate, useParams } from "react-router-dom";
import { expect } from "@/api/client";
import { useIdentity } from "@/identity/IdentityProvider";
import { useSession } from "@/session/SessionProvider";
import { Badge, Empty, Page, ProblemMessage, Show } from "@/ui/components";
import { useLoad } from "@/ui/hooks";
import { label } from "@/ui/labels";

/** The caller's own mentorships, as protégé or as mentor, any number with the same person (FR-054, AC-FR-003-02). */
export function Relationships() {
  const { api } = useIdentity();
  const { isMe } = useSession();
  const relationships = useLoad(() => expect(api.GET("/api/relationships")), []);

  return (
    <Page title="My mentorships">
      <Show loaded={relationships}>
        {(own) =>
          own.length === 0 ? (
            <Empty>You have no formed mentorship yet.</Empty>
          ) : (
            <ul className="list">
              {own.map((relationship) => (
                <li key={relationship.id} className="row">
                  <Link to={`/relationships/${relationship.id}`}>
                    {isMe(relationship.mentor.employeeId) ? `Mentoring ${relationship.protege.displayName}` : `Mentored by ${relationship.mentor.displayName}`}
                  </Link>
                  <Badge>{label(relationship.state)}</Badge>
                  {relationship.provenance === "Legacy" ? <Badge tone="warn">Historical</Badge> : null}
                </li>
              ))}
            </ul>
          )
        }
      </Show>
    </Page>
  );
}

/** A notification about a goal opens the mentorship the goal belongs to. */
export function GoalRedirect() {
  const { goalId = "" } = useParams();
  const { api } = useIdentity();
  const goal = useLoad(() => expect(api.GET("/api/goals/{goalId}", { params: { path: { goalId } } })), [goalId]);
  if (goal.problem) return <ProblemMessage problem={goal.problem} />;
  if (!goal.data) return <p role="status">Loading…</p>;
  return <Navigate to={`/relationships/${goal.data.relationshipId}#goal-${goal.data.id}`} replace />;
}
