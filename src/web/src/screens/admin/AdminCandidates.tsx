import { Link } from "react-router-dom";
import { expect, type Api } from "@/api/client";
import { useIdentity } from "@/identity/IdentityProvider";
import { Badge, Empty, Page, Show } from "@/ui/components";
import { useLoad } from "@/ui/hooks";
import { label, when } from "@/ui/labels";

/**
 * Every pairing, as the audit trail records it: each one begins with its invitation, an entry about that candidate. The
 * API offers Talent Management no list of all candidates, so the trail, which Talent Management alone reads, names them,
 * and each is then read as itself, with the standing the server reports.
 */
export async function allCandidates(api: Api) {
  const entries = await expect(api.GET("/api/audit-trail/records/{recordType}", { params: { path: { recordType: "Candidate" } } }));
  const ids = [...new Set(entries.map((entry) => entry.recordId).filter((id): id is string => Boolean(id)))];
  const candidates = await Promise.all(ids.map((candidateId) => expect(api.GET("/api/candidates/{candidateId}", { params: { path: { candidateId } } }))));
  return candidates.sort((first, second) => second.updatedAt.localeCompare(first.updatedAt));
}

/** Talent Management's view of every pairing, to read its draft and decide its formation (COMP-FORMATION). */
export function AdminCandidates() {
  const { api } = useIdentity();
  const candidates = useLoad(() => allCandidates(api), []);

  return (
    <Page title="Pairings and formation" intro="Every pairing, most recently changed first. Open one to read its agreement draft and decide its formation.">
      <Show loaded={candidates}>
        {(found) =>
          found.length === 0 ? (
            <Empty>No pairing has been made yet.</Empty>
          ) : (
            <table>
              <thead>
                <tr>
                  <th>Protégé</th>
                  <th>Mentor</th>
                  <th>Standing</th>
                  <th>Last changed</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {found.map((candidate) => (
                  <tr key={candidate.id}>
                    <td>{candidate.protegeDisplayName}</td>
                    <td>{candidate.mentorDisplayName}</td>
                    <td>
                      <Badge>{label(candidate.status)}</Badge>
                    </td>
                    <td>{when(candidate.updatedAt)}</td>
                    <td className="row">
                      <Link to={`/candidates/${candidate.id}/draft`}>Draft</Link>
                      <Link to={`/candidates/${candidate.id}/formation`}>Formation</Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )
        }
      </Show>
    </Page>
  );
}
