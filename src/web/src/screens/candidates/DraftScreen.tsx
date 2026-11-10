import { useState, type FormEvent } from "react";
import { Link, useParams } from "react-router-dom";
import { expect, ifMatch, type Schemas } from "@/api/client";
import { contractEnums } from "@/api/enums.gen";
import { useIdentity } from "@/identity/IdentityProvider";
import { useSession } from "@/session/SessionProvider";
import { Badge, Empty, Facts, Field, Outcome, Page, Section, Show } from "@/ui/components";
import { useCommand, useLoad } from "@/ui/hooks";
import { label, shortHash, when } from "@/ui/labels";
import { AgreementContentView, AnnexView } from "./AgreementContent";
import { DraftForm } from "./DraftForm";

type Draft = Schemas["AgreementDraftView"];

/**
 * The agreement draft of a pairing (FR-022, FR-027, FR-067 to FR-070, ADR-004). Both participants review the complete
 * current content, fixed policy text included, before they accept it (AC-FR-022-02). Each accepts that exact content,
 * named by its content identity, for themselves alone (AC-FR-027-02). Every acceptance is listed with the revision it was
 * given to and whether it still applies, so an acceptance of superseded content is never counted with a later one
 * (AC-FR-027-03, AC-FR-027-04). A replacement revision is a new draft both must accept anew. Talent Management keeps the
 * administrative annex, which is labelled as not part of the accepted agreement.
 */
export function DraftScreen() {
  const { candidateId = "" } = useParams();
  const { api } = useIdentity();
  const { can, isMe } = useSession();
  const draft = useLoad(() => expect(api.GET("/api/candidates/{candidateId}/draft", { params: { path: { candidateId } } })), [candidateId]);
  const [viewing, setViewing] = useState<string | null>(null);
  const earlier = useLoad(
    () =>
      viewing
        ? expect(api.GET("/api/candidates/{candidateId}/draft/revisions/{revisionId}", { params: { path: { candidateId, revisionId: viewing } } }))
        : Promise.resolve(null),
    [candidateId, viewing],
  );
  const [revising, setRevising] = useState(false);
  const accept = useCommand();
  const revise = useCommand();
  const annex = useCommand();

  return (
    <Show loaded={draft}>
      {(found: Draft) => {
        const participant = isMe(found.protege.employeeId) || isMe(found.mentor.employeeId);
        const current = found.currentRevision;
        const nameOf = (employeeId: string) =>
          employeeId === found.protege.employeeId ? found.protege.displayName : employeeId === found.mentor.employeeId ? found.mentor.displayName : employeeId;

        return (
          <Page title={`Agreement draft: ${found.protege.displayName} with ${found.mentor.displayName}`}>
            <Section title="Where the draft stands">
              <Facts
                items={[
                  ["Pairing", <Badge tone="info">{label(found.candidateStatus)}</Badge>],
                  ["Step", found.step !== null ? String(found.step) : <span className="unknown">Not started</span>],
                  ["Current revision", current ? `Revision ${current.revisionNumber} of ${when(current.createdAt)}` : "None yet"],
                ]}
              />
              <p className="row">
                <Link to={`/candidates/${found.candidateId}`}>Pairing</Link>
                <Link to={`/candidates/${found.candidateId}/formation`}>Formation</Link>
              </p>
            </Section>

            {current ? (
              <Section title={`Review revision ${current.revisionNumber}`}>
                <AgreementContentView content={current.content} contentHash={current.contentHash} heading={`Mentorship agreement — revision ${current.revisionNumber}`} />
                {participant && can("agreement-draft.accept") ? (
                  <div className="stack">
                    <p>
                      Accepting records your own acceptance of exactly this content (identity <span className="hash">{shortHash(current.contentHash)}</span>).
                      The other participant accepts for themselves.
                    </p>
                    <div>
                      <button
                        type="button"
                        disabled={accept.pending}
                        onClick={() =>
                          void accept.run(
                            async () => {
                              await expect(
                                api.POST("/api/candidates/{candidateId}/draft/revisions/{revisionId}/acceptances", {
                                  params: { path: { candidateId, revisionId: current.id } },
                                  headers: ifMatch(found.version),
                                  body: { contentHash: current.contentHash },
                                }),
                              );
                              await draft.reload();
                            },
                            `You accepted revision ${current.revisionNumber}.`,
                            draft.reload,
                          )
                        }
                      >
                        I accept revision {current.revisionNumber}
                      </button>
                    </div>
                    <Outcome command={accept} />
                  </div>
                ) : null}
              </Section>
            ) : (
              <Section title="No draft yet">
                <Empty>No revision has been prepared yet.</Empty>
              </Section>
            )}

            <Section title="Acceptances">
              {found.acceptances.length === 0 ? (
                <Empty>Nobody has accepted a revision yet.</Empty>
              ) : (
                <table>
                  <thead>
                    <tr>
                      <th>Participant</th>
                      <th>Revision</th>
                      <th>Content identity</th>
                      <th>Accepted</th>
                      <th>Counts towards formation</th>
                    </tr>
                  </thead>
                  <tbody>
                    {found.acceptances.map((acceptance) => (
                      <tr key={acceptance.id}>
                        <td>
                          {nameOf(acceptance.participantEmployeeId)} ({label(acceptance.participant)})
                        </td>
                        <td>{acceptance.revisionNumber}</td>
                        <td className="hash" title={acceptance.contentHash}>
                          {shortHash(acceptance.contentHash)}
                        </td>
                        <td>{when(acceptance.acceptedAt)}</td>
                        <td>
                          <Badge tone={acceptance.applicability === "Applicable" ? "good" : "warn"}>{label(acceptance.applicability)}</Badge>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </Section>

            <Section title="Revisions">
              <ul className="list">
                {found.revisions.map((revision) => (
                  <li key={revision.id} className="row">
                    <strong>Revision {revision.revisionNumber}</strong>
                    {revision.isCurrent ? <Badge tone="info">Current</Badge> : <Badge>Superseded</Badge>}
                    <span className="hash" title={revision.contentHash}>
                      {shortHash(revision.contentHash)}
                    </span>
                    <span className="muted">
                      by {nameOf(revision.createdByEmployeeId)}, {when(revision.createdAt)}
                    </span>
                    {!revision.isCurrent ? (
                      <button type="button" className="secondary" onClick={() => setViewing(viewing === revision.id ? null : revision.id)}>
                        {viewing === revision.id ? "Hide" : "Read"}
                      </button>
                    ) : null}
                  </li>
                ))}
              </ul>
              {viewing ? (
                <Show loaded={earlier}>
                  {(revision) =>
                    revision ? (
                      <AgreementContentView
                        content={revision.content}
                        contentHash={revision.contentHash}
                        heading={`Superseded revision ${revision.revisionNumber}, kept as it was`}
                      />
                    ) : null
                  }
                </Show>
              ) : null}
            </Section>

            {participant && can("agreement-draft.create-revision") ? (
              <Section
                title={current ? "Prepare a replacement draft" : "Prepare the draft"}
                actions={
                  current ? (
                    <button type="button" className="secondary" onClick={() => setRevising(!revising)}>
                      {revising ? "Cancel" : "Revise"}
                    </button>
                  ) : null
                }
              >
                {current ? (
                  <p className="muted">
                    A replacement revision is a new draft. The draft returns to step 1, earlier acceptances no longer count, and both of
                    you accept the replacement anew.
                  </p>
                ) : null}
                {!current || revising ? (
                  <DraftForm
                    key={current?.id ?? "first"}
                    from={current?.content}
                    command={revise}
                    onSubmit={(form) =>
                      void revise.run(
                        async () => {
                          await expect(
                            api.POST("/api/candidates/{candidateId}/draft/revisions", {
                              params: { path: { candidateId } },
                              headers: ifMatch(found.version),
                              body: form,
                            }),
                          );
                          setRevising(false);
                          await draft.reload();
                        },
                        "The draft revision is saved.",
                        draft.reload,
                      )
                    }
                  />
                ) : (
                  <Outcome command={revise} />
                )}
              </Section>
            ) : null}

            <Section title="Administrative annex">
              <AnnexView annex={found.annex} />
              {found.annex.changes.length > 0 ? (
                <ul>
                  {found.annex.changes.map((change) => (
                    <li key={change.id}>
                      {label(change.basis)}: {change.priorValue ?? "none"} → {change.newValue ?? "none"} ({when(change.changedAt)})
                    </li>
                  ))}
                </ul>
              ) : null}
              {can("agreement-draft.change-annex") ? (
                <AnnexForm
                  command={annex}
                  onSubmit={(change) =>
                    void annex.run(
                      async () => {
                        await expect(
                          api.POST("/api/candidates/{candidateId}/draft/annex", {
                            params: { path: { candidateId } },
                            headers: ifMatch(found.version),
                            body: change,
                          }),
                        );
                        await draft.reload();
                      },
                      "The annex is changed. The accepted content and its acceptances are unchanged.",
                      draft.reload,
                    )
                  }
                />
              ) : null}
            </Section>
          </Page>
        );
      }}
    </Show>
  );
}

function AnnexForm({ command, onSubmit }: { command: ReturnType<typeof useCommand>; onSubmit: (change: Schemas["AnnexChangeRequest"]) => void }) {
  const [basis, setBasis] = useState<string>("");
  const [value, setValue] = useState("");
  const submit = (event: FormEvent) => {
    event.preventDefault();
    onSubmit({ basis: (basis || null) as Schemas["PermittedChangeBasis"] | null, value: value.trim() ? value : null });
  };

  return (
    <form className="stack" onSubmit={submit} aria-label="Change the administrative annex">
      <Field label="Change">
        <select value={basis} onChange={(event) => setBasis(event.target.value)} required>
          <option value="">Choose</option>
          {contractEnums.PermittedChangeBasis.map((option) => (
            <option key={option} value={option}>
              {label(option)}
            </option>
          ))}
        </select>
      </Field>
      <Field label="New value" hint="Leave empty to clear it.">
        <textarea value={value} onChange={(event) => setValue(event.target.value)} />
      </Field>
      <Outcome command={command} />
      <div>
        <button type="submit" disabled={command.pending}>
          Change annex
        </button>
      </div>
    </form>
  );
}
