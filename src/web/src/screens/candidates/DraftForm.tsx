import { useState, type FormEvent } from "react";
import type { Schemas } from "@/api/client";
import { contractEnums } from "@/api/enums.gen";
import { Field, linesOf, Outcome } from "@/ui/components";
import type { Command } from "@/ui/hooks";
import { label } from "@/ui/labels";

type Form = Schemas["AgreementDraftForm"];
type Content = Schemas["AgreementContent"];

const numberOrNull = (text: string): number | null => (text.trim() === "" ? null : Number(text));
const textOrNull = (text: string): string | null => (text.trim() === "" ? null : text);

/**
 * The values the participants enter in an agreement draft (FR-022, CONTRACT-AGREEMENT-CONTENT). The parties and the
 * fixed policy text are the server's and are not entered. The choices offered are those the contract names; whether the
 * values are complete and consistent is the server's to say.
 */
export function DraftForm({ from, command, onSubmit }: { from?: Content; command: Command; onSubmit: (form: Form) => void }) {
  const terms = from?.terms;
  const [durationCategory, setDurationCategory] = useState<string>(terms?.durationCategory ?? "");
  const [startDate, setStartDate] = useState(terms?.startDate ?? "");
  const [endDate, setEndDate] = useState(terms?.endDate ?? "");
  const [meetingIntervalWeeks, setMeetingIntervalWeeks] = useState(terms?.meetingIntervalWeeks?.toString() ?? "");
  const [approximateMinutes, setApproximateMinutes] = useState(terms?.approximateMinutesPerMeeting?.toString() ?? "");
  const [contactInterval, setContactInterval] = useState(terms?.betweenMeetingContact?.interval.toString() ?? "");
  const [contactUnit, setContactUnit] = useState<string>(terms?.betweenMeetingContact?.unit ?? "");
  const [missedMeetings, setMissedMeetings] = useState(terms?.missedMeetingArrangements ?? "");
  const [reviewDate, setReviewDate] = useState(terms?.reviewDate ?? "");
  const [goals, setGoals] = useState((from?.goals ?? []).join("\n"));
  const [barriers, setBarriers] = useState((from?.barriers ?? []).join("\n"));
  const [contingencies, setContingencies] = useState((from?.contingencies ?? []).join("\n"));
  const [mentorDuties, setMentorDuties] = useState((from?.responsibilities.mentor ?? []).join("\n"));
  const [protegeDuties, setProtegeDuties] = useState((from?.responsibilities.protege ?? []).join("\n"));

  const submit = (event: FormEvent) => {
    event.preventDefault();
    onSubmit({
      terms: {
        durationCategory: (durationCategory || null) as Schemas["DurationCategory"] | null,
        startDate: textOrNull(startDate),
        endDate: textOrNull(endDate),
        meetingIntervalWeeks: numberOrNull(meetingIntervalWeeks),
        approximateMinutesPerMeeting: numberOrNull(approximateMinutes),
        betweenMeetingContact:
          contactInterval.trim() === "" && contactUnit === ""
            ? null
            : { interval: numberOrNull(contactInterval), unit: (contactUnit || null) as Schemas["ContactUnit"] | null },
        missedMeetingArrangements: textOrNull(missedMeetings),
        reviewDate: textOrNull(reviewDate),
      },
      goals: linesOf(goals),
      barriers: linesOf(barriers),
      contingencies: linesOf(contingencies),
      responsibilities: { mentor: linesOf(mentorDuties), protege: linesOf(protegeDuties) },
    });
  };

  return (
    <form className="stack" onSubmit={submit} aria-label="Agreement draft">
      <Field label="Duration">
        <select value={durationCategory} onChange={(event) => setDurationCategory(event.target.value)}>
          <option value="">Choose a duration</option>
          {contractEnums.DurationCategory.map((value) => (
            <option key={value} value={value}>
              {label(value)}
            </option>
          ))}
        </select>
      </Field>
      <div className="row">
        <Field label="Start date" hint="Optional.">
          <input type="date" value={startDate} onChange={(event) => setStartDate(event.target.value)} />
        </Field>
        <Field label="End date" hint="Optional; a 12+ months agreement may be open ended.">
          <input type="date" value={endDate} onChange={(event) => setEndDate(event.target.value)} />
        </Field>
        <Field label="Review date">
          <input type="date" value={reviewDate} onChange={(event) => setReviewDate(event.target.value)} />
        </Field>
      </div>
      <div className="row">
        <Field label="Meet every (weeks)">
          <input type="number" min={1} value={meetingIntervalWeeks} onChange={(event) => setMeetingIntervalWeeks(event.target.value)} />
        </Field>
        <Field label="Approximate meeting length (minutes)">
          <input type="number" min={1} value={approximateMinutes} onChange={(event) => setApproximateMinutes(event.target.value)} />
        </Field>
      </div>
      <div className="row">
        <Field label="Contact between meetings at least every">
          <input type="number" min={1} value={contactInterval} onChange={(event) => setContactInterval(event.target.value)} />
        </Field>
        <Field label="Unit">
          <select value={contactUnit} onChange={(event) => setContactUnit(event.target.value)}>
            <option value="">Choose</option>
            {contractEnums.ContactUnit.map((value) => (
              <option key={value} value={value}>
                {label(value)}
              </option>
            ))}
          </select>
        </Field>
      </div>
      <Field label="What happens when a meeting is missed">
        <textarea value={missedMeetings} onChange={(event) => setMissedMeetings(event.target.value)} />
      </Field>
      <Field label="Development goals" hint="One per line. Make each specific, measurable, achievable, relevant and time-bound.">
        <textarea value={goals} onChange={(event) => setGoals(event.target.value)} />
      </Field>
      <Field label="Barriers" hint="One per line.">
        <textarea value={barriers} onChange={(event) => setBarriers(event.target.value)} />
      </Field>
      <Field label="Contingencies" hint="One per line.">
        <textarea value={contingencies} onChange={(event) => setContingencies(event.target.value)} />
      </Field>
      <Field label="The mentor's responsibilities" hint="One per line.">
        <textarea value={mentorDuties} onChange={(event) => setMentorDuties(event.target.value)} />
      </Field>
      <Field label="The protégé's responsibilities" hint="One per line.">
        <textarea value={protegeDuties} onChange={(event) => setProtegeDuties(event.target.value)} />
      </Field>
      <Outcome command={command} />
      <div>
        <button type="submit" disabled={command.pending}>
          Save draft revision
        </button>
      </div>
    </form>
  );
}
