import { Link, useParams } from "react-router-dom";
import { expect, ifMatch, type Schemas } from "@/api/client";
import { useIdentity } from "@/identity/IdentityProvider";
import { useSession } from "@/session/SessionProvider";
import { Badge, Empty, Facts, Outcome, Page, Section, Show } from "@/ui/components";
import { useCommand, useLoad } from "@/ui/hooks";
import { label, shortHash, when } from "@/ui/labels";
import { AgreementContentView, AnnexView } from "./AgreementContent";

/**
 * Formation of a pairing (COMP-FORMATION). Talent Management approves or rejects the current revision, named by its
 * content identity. Whether the prerequisites hold is the server's to decide, and its refusal is shown as given. Every
 * decision is listed with whether it still applies. Once formed, the relationship, the formed agreement exactly as
 * accepted and its goals are shown.
 */
export function FormationScreen() {
  const { candidateId = "" } = useParams();
  const { api } = useIdentity();
  const { can } = useSession();
  const formation = useLoad(() => expect(api.GET("/api/candidates/{candidateId}/formation", { params: { path: { candidateId } } })), [candidateId]);
  const draft = useLoad(() => expect(api.GET("/api/candidates/{candidateId}/draft", { params: { path: { candidateId } } })), [candidateId]);
  const command = useCommand();

  const reread = async () => {
    await Promise.all([formation.reload(), draft.reload()]);
  };

  const decide = (found: Schemas["FormationView"], revision: Schemas["AgreementRevisionView"], outcome: Schemas["FormationOutcome"]) =>
    void command.run(
      async () => {
        await expect(
          api.POST("/api/candidates/{candidateId}/draft/revisions/{revisionId}/formation-decisions", {
            params: { path: { candidateId, revisionId: revision.id } },
            headers: ifMatch(found.version),
            body: { outcome, contentHash: revision.contentHash },
          }),
        );
        await reread();
      },
      outcome === "Approved" ? "Formation approved: the mentorship is formed." : "Formation rejected.",
      reread,
    );

  return (
    <Show loaded={formation}>
      {(found) => {
        const current = draft.data?.currentRevision ?? null;
        const relationship = found.relationship;
        return (
          <Page title="Formation">
            <Section title="Where formation stands">
              <Facts
                items={[
                  ["Pairing", <Badge tone="info">{label(found.candidateStatus)}</Badge>],
                  ["Draft step", found.step !== null ? String(found.step) : <span className="unknown">Not started</span>],
                  ["Current revision", current ? `Revision ${current.revisionNumber} (${shortHash(current.contentHash)})` : "None"],
                ]}
              />
              <p className="row">
                <Link to={`/candidates/${candidateId}`}>Pairing</Link>
                <Link to={`/candidates/${candidateId}/draft`}>Agreement draft</Link>
              </p>
            </Section>

            {can("formation.decide") && current && !relationship ? (
              <Section title={`Decide on revision ${current.revisionNumber}`}>
                <p>
                  The decision binds to the content identity <span className="hash">{current.contentHash}</span>. The service forms the mentorship
                  only if both participants have accepted exactly that content.
                </p>
                <div className="row">
                  <button type="button" disabled={command.pending} onClick={() => decide(found, current, "Approved")}>
                    Approve formation
                  </button>
                  <button type="button" className="secondary" disabled={command.pending} onClick={() => decide(found, current, "Rejected")}>
                    Reject
                  </button>
                </div>
                <Outcome command={command} />
              </Section>
            ) : (
              <Outcome command={command} />
            )}

            <Section title="Decisions">
              {found.decisions.length === 0 ? (
                <Empty>No formation decision has been taken.</Empty>
              ) : (
                <table>
                  <thead>
                    <tr>
                      <th>Outcome</th>
                      <th>Revision</th>
                      <th>Content identity</th>
                      <th>Decided</th>
                      <th>Applies</th>
                    </tr>
                  </thead>
                  <tbody>
                    {found.decisions.map((decision) => (
                      <tr key={decision.id}>
                        <td>{label(decision.outcome)}</td>
                        <td>{decision.revisionNumber}</td>
                        <td className="hash" title={decision.contentHash}>
                          {shortHash(decision.contentHash)}
                        </td>
                        <td>{when(decision.decidedAt)}</td>
                        <td>{label(decision.applicability)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </Section>

            {relationship ? (
              <Section title="The formed mentorship" actions={<Link to={`/relationships/${relationship.id}`}>Open the mentorship</Link>}>
                <Facts
                  items={[
                    ["Formed", when(relationship.formedAt)],
                    ["State", label(relationship.state)],
                    ["Status", label(relationship.status)],
                    ["IsActive", label(relationship.isActive)],
                  ]}
                />
                <AgreementContentView
                  content={relationship.agreement.content}
                  contentHash={relationship.agreement.contentHash}
                  heading="The formed agreement, exactly as accepted"
                />
                <AnnexView annex={relationship.agreement.annex} />
                <h3>Development goals</h3>
                <ul>
                  {relationship.goals.map((goal) => (
                    <li key={goal.id}>
                      {goal.agreedDescription} — {label(goal.completionStatus)}
                    </li>
                  ))}
                </ul>
              </Section>
            ) : null}
          </Page>
        );
      }}
    </Show>
  );
}
