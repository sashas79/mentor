import { Link } from "react-router-dom";
import { expect } from "@/api/client";
import { useIdentity } from "@/identity/IdentityProvider";
import { Empty, Page, Show } from "@/ui/components";
import { useLoad } from "@/ui/hooks";

/**
 * Mentor discovery (FR-009, FR-012): every approved mentor the server lists, however many mentorships they already
 * have, each with a summary that opens their full profile (AC-FR-012-01).
 */
export function Mentors() {
  const { api } = useIdentity();
  const mentors = useLoad(() => expect(api.GET("/api/mentors")), []);

  return (
    <Page title="Mentors" intro="Approved mentors. Open a profile to read it and to invite the mentor for one of your requests.">
      <Show loaded={mentors}>
        {(found) =>
          found.length === 0 ? (
            <Empty>No approved mentors yet.</Empty>
          ) : (
            <ul className="list" aria-label="Approved mentors">
              {found.map((mentor) => (
                <li key={mentor.id}>
                  <div className="row">
                    <Link to={`/mentors/${mentor.id}`}>
                      <strong>{mentor.displayName}</strong>
                    </Link>
                    {mentor.roleTitle ?? mentor.levelTitle ? <span className="muted">{mentor.roleTitle ?? mentor.levelTitle}</span> : null}
                  </div>
                  <p>{mentor.biography}</p>
                </li>
              ))}
            </ul>
          )
        }
      </Show>
    </Page>
  );
}
