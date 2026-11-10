import { Link } from "react-router-dom";
import { expect } from "@/api/client";
import { useIdentity } from "@/identity/IdentityProvider";
import { useSession } from "@/session/SessionProvider";
import { Badge, Empty, Page, Show } from "@/ui/components";
import { useLoad } from "@/ui/hooks";
import { label, when } from "@/ui/labels";

/** The pairings the caller takes part in, as protégé or as mentor (FR-018 to FR-027, AC-FR-003-02). */
export function Candidates() {
  const { api } = useIdentity();
  const { isMe } = useSession();
  const candidates = useLoad(() => expect(api.GET("/api/candidates")), []);

  return (
    <Page title="Pairings and agreements" intro="Every pairing you are part of, as a protégé or as a mentor, with where it stands.">
      <Show loaded={candidates}>
        {(own) =>
          own.length === 0 ? (
            <Empty>You are not part of any pairing yet.</Empty>
          ) : (
            <ul className="list">
              {own.map((candidate) => {
                const asMentor = isMe(candidate.mentorEmployeeId);
                return (
                  <li key={candidate.id} className="row">
                    <Link to={`/candidates/${candidate.id}`}>
                      {asMentor ? `Mentoring ${candidate.protegeDisplayName}` : `Mentored by ${candidate.mentorDisplayName}`}
                    </Link>
                    <Badge>{label(candidate.status)}</Badge>
                    <span className="muted">invited {when(candidate.invitedAt)}</span>
                  </li>
                );
              })}
            </ul>
          )
        }
      </Show>
    </Page>
  );
}
