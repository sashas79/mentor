import type { Schemas } from "@/api/client";
import { Empty, Facts } from "@/ui/components";
import { label, when } from "@/ui/labels";

type Content = Schemas["AgreementContent"];
type Annex = Schemas["AdministrativeAnnexView"] | Schemas["RetainedAnnexView"];

function List({ items, none }: { items: string[]; none: string }) {
  return items.length === 0 ? <Empty>{none}</Empty> : <ul>{items.map((item, index) => <li key={`${index}:${item}`}>{item}</li>)}</ul>;
}

/**
 * The complete content of an agreement revision exactly as the server holds it, which is what each participant accepts
 * (AC-FR-022-01, AC-FR-022-02). That is the fixed policy text, the identified mentor and protégé, the term and review
 * expectations, meetings and contact, goals, barriers and contingencies, and each party's responsibilities. Nothing is
 * left out or added, and the content identity it is accepted by is shown with it.
 */
export function AgreementContentView({ content, contentHash, heading }: { content: Content; contentHash: string; heading: string }) {
  const terms = content.terms;
  const unknown = <span className="unknown">Not set</span>;
  return (
    <article className="agreement" aria-label={heading}>
      <h3>{heading}</h3>
      <p className="muted">
        Content identity: <span className="hash">{contentHash}</span>
      </p>
      <h4>Parties</h4>
      <Facts
        items={[
          ["Mentor", content.parties.mentor.displayName],
          ["Protégé", content.parties.protege.displayName],
        ]}
      />
      <h4>Agreement</h4>
      {content.policy.map((clause) => (
        <section key={clause.clause}>
          <h5>{clause.clause}</h5>
          <p>{clause.text}</p>
        </section>
      ))}
      <h4>Term, meetings and review</h4>
      <Facts
        items={[
          ["Duration", label(terms.durationCategory)],
          ["Start date", terms.startDate ? when(terms.startDate) : unknown],
          ["End date", terms.endDate ? when(terms.endDate) : unknown],
          ["Meets every", terms.meetingIntervalWeeks !== null ? `${terms.meetingIntervalWeeks} weeks` : unknown],
          ["Approximate meeting length", terms.approximateMinutesPerMeeting !== null ? `${terms.approximateMinutesPerMeeting} minutes` : unknown],
          [
            "Contact between meetings",
            terms.betweenMeetingContact ? `At least every ${terms.betweenMeetingContact.interval} ${label(terms.betweenMeetingContact.unit)}` : unknown,
          ],
          ["Missed meetings", terms.missedMeetingArrangements ?? unknown],
          ["Review date", terms.reviewDate ? when(terms.reviewDate) : unknown],
        ]}
      />
      <h4>Development goals</h4>
      <List items={content.goals} none="No goals." />
      <h4>Barriers</h4>
      <List items={content.barriers} none="None named." />
      <h4>Contingencies</h4>
      <List items={content.contingencies} none="None named." />
      <h4>The mentor's responsibilities</h4>
      <List items={content.responsibilities.mentor} none="None named." />
      <h4>The protégé's responsibilities</h4>
      <List items={content.responsibilities.protege} none="None named." />
    </article>
  );
}

/**
 * The administrative annex (ADR-004): Talent Management's reference and remarks, kept apart from the accepted content
 * and labelled so that nobody takes it for part of what the participants accepted.
 */
export function AnnexView({ annex }: { annex: Annex }) {
  return (
    <aside className="annex" aria-label="Administrative annex">
      <h3>Administrative annex — not part of the accepted agreement</h3>
      {annex.notice ? <p className="muted">{annex.notice}</p> : null}
      <Facts
        items={[
          ["Administrative reference", annex.adminReference ?? <span className="unknown">None</span>],
          ["Administrative remarks", annex.adminRemarks ?? <span className="unknown">None</span>],
        ]}
      />
    </aside>
  );
}
