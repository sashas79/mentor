import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { expect, problemOf, type Schemas } from "@/api/client";
import { useIdentity } from "@/identity/IdentityProvider";
import { Badge, Empty, Outcome, Page, Show } from "@/ui/components";
import { useCommand, useLoad } from "@/ui/hooks";
import { label, when } from "@/ui/labels";

type Entry = Schemas["RegisterEntryView"];

/** The file name the server offers the export under, from Content-Disposition. */
export function fileNameOf(disposition: string | null): string {
  const encoded = disposition?.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
  if (encoded) return decodeURIComponent(encoded);
  return disposition?.match(/filename="?([^";]+)"?/i)?.[1] ?? "mentorship-register.csv";
}

/** Hands the file to the browser to save. */
export function save(file: Blob, name: string): void {
  const url = URL.createObjectURL(file);
  const link = document.createElement("a");
  link.href = url;
  link.download = name;
  document.body.append(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

/** A stored value, or unknown beside the source text it was not resolved from; nothing is filled in. */
function cell(value: ReactNode | null | undefined, sourceText?: string | null): ReactNode {
  return (
    <>
      {value === null || value === undefined ? <span className="unknown">Unknown</span> : value}
      {sourceText ? <div className="source-text">Source: “{sourceText}”</div> : null}
    </>
  );
}

function review(entry: Entry): ReactNode {
  if (!entry.reviewException) return null;
  const { reason, unresolvedAttributes } = entry.reviewException;
  return (
    <Badge tone="warn">
      {label(reason)}
      {unresolvedAttributes.length > 0 ? `: ${unresolvedAttributes.join(", ")}` : ""}
    </Badge>
  );
}

/**
 * The relationship register and its export (FR-060, FR-061, ADR-011), for Talent Management only (AC-FR-060-02). Both are
 * reconstructed by the server from the authoritative records each time they are asked for. Every value is shown as
 * stored. An unknown stays unknown beside the tracker text it was not resolved from, and a BLANK Status reads Blank,
 * which is not the same as unknown. Historical rows and their unresolved mappings are marked apart from mentorships
 * formed in the application (AC-FR-060-01).
 */
export function Register() {
  const { api } = useIdentity();
  const register = useLoad(() => expect(api.GET("/api/register")), []);
  const exported = useCommand();

  const exportCsv = () =>
    void exported.run(async () => {
      const { data, error, response } = await api.GET("/api/register/export", { parseAs: "blob" });
      if (!response.ok || !data) throw problemOf(response.status, error);
      save(data, fileNameOf(response.headers.get("Content-Disposition")));
    }, "The register is exported. The export is recorded in the audit trail.");

  return (
    <Page title="Relationship register" intro="Every mentorship, historical and formed in the application, as the records hold it now.">
      <div className="row">
        <button type="button" onClick={exportCsv} disabled={exported.pending}>
          Export as CSV
        </button>
        <span className="muted">Spreadsheet-compatible. Each export is recorded.</span>
      </div>
      <Outcome command={exported} />
      <Show loaded={register}>
        {(entries) =>
          entries.length === 0 ? (
            <Empty>The register is empty.</Empty>
          ) : (
            <div className="table-scroll">
              <table aria-label="Relationship register">
                <thead>
                  <tr>
                    <th>Mentor</th>
                    <th>Protégé</th>
                    <th>State</th>
                    <th>Status</th>
                    <th>IsActive</th>
                    <th>Start date</th>
                    <th>End date</th>
                    <th>Duration</th>
                    <th>Provenance</th>
                    <th>Review</th>
                  </tr>
                </thead>
                <tbody>
                  {entries.map((entry) => (
                    <RegisterRow key={entry.relationshipId ?? entry.reviewExceptionId} entry={entry} />
                  ))}
                </tbody>
              </table>
            </div>
          )
        }
      </Show>
    </Page>
  );
}

function RegisterRow({ entry }: { entry: Entry }) {
  const source = entry.source;
  const person = (who: Schemas["RegisterPersonView"] | null, text: string | null | undefined) =>
    who ? (
      <>
        {who.displayName}
        {who.level ? <div className="source-text">{who.level}</div> : null}
      </>
    ) : (
      cell(null, text)
    );
  const date = (value: string | null) => (value ? when(value) : null);

  return (
    <tr>
      <td>{person(entry.mentor, source?.mentorText)}</td>
      <td>{person(entry.protege, source?.protegeText)}</td>
      <td>{cell(entry.state ? label(entry.state) : null, source?.activeText)}</td>
      <td>{cell(entry.status ? label(entry.status) : null)}</td>
      <td>{cell(entry.isActive ? label(entry.isActive) : null)}</td>
      <td>{cell(date(entry.startDate), source?.startText)}</td>
      <td>{cell(date(entry.endDate), source?.endText)}</td>
      <td>{cell(entry.durationCategory ? label(entry.durationCategory) : null, source?.durationText)}</td>
      <td>
        <Badge tone={entry.provenance === "Legacy" ? "warn" : "neutral"}>{label(entry.provenance)}</Badge>
        {source ? (
          <div className="source-text">
            {source.file}, row {source.row}
          </div>
        ) : null}
      </td>
      <td>
        {entry.kind === "HeldSourceRow" ? <div>{label(entry.kind)}</div> : null}
        {review(entry)}
        {entry.relationshipId ? (
          <div>
            <Link to={`/relationships/${entry.relationshipId}`}>Open</Link>
          </div>
        ) : null}
      </td>
    </tr>
  );
}
