import { useState, type FormEvent } from "react";
import type { Schemas } from "@/api/client";
import { Empty, Field, Outcome } from "@/ui/components";
import type { Command } from "@/ui/hooks";
import { label, when } from "@/ui/labels";

type Meeting = Schemas["MeetingView"];
type MeetingRequest = Schemas["MeetingRequest"];

/** A local date and time as entered, as the instant the server stores; none when the field is empty. */
export function instantOf(local: string): string | null {
  if (!local) return null;
  const parsed = new Date(local);
  return Number.isNaN(parsed.getTime()) ? null : parsed.toISOString();
}

/** The instant as a value of a datetime-local field, in the browser's time zone. */
function localOf(instant: string | null | undefined): string {
  if (!instant) return "";
  const parsed = new Date(instant);
  const offset = parsed.getTimezoneOffset() * 60_000;
  return new Date(parsed.getTime() - offset).toISOString().slice(0, 16);
}

/**
 * When a meeting is proposed or scheduled, if known, and whether it was arranged outside the application (ADR-010,
 * AC-FR-018-02). No calendar is consulted and nothing said in a meeting is recorded: the form has no place for it.
 */
export function MeetingForm({
  initial,
  submitText,
  command,
  onSubmit,
}: {
  initial?: Meeting;
  submitText: string;
  command: Command;
  onSubmit: (meeting: MeetingRequest) => void;
}) {
  const [scheduledAt, setScheduledAt] = useState(localOf(initial?.scheduledAt));
  const [outside, setOutside] = useState(initial?.arrangedOutsideApplication ?? false);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    onSubmit({ scheduledAt: instantOf(scheduledAt), arrangedOutsideApplication: outside });
  };

  return (
    <form className="stack" onSubmit={submit}>
      <Field label="Date and time" hint="Leave empty if it is not known yet.">
        <input type="datetime-local" value={scheduledAt} onChange={(event) => setScheduledAt(event.target.value)} />
      </Field>
      <label className="row">
        <input type="checkbox" checked={outside} onChange={(event) => setOutside(event.target.checked)} />
        Arranged outside the application (for example by e-mail or Teams)
      </label>
      <Outcome command={command} />
      <div>
        <button type="submit" disabled={command.pending}>
          {submitText}
        </button>
      </div>
    </form>
  );
}

/** Meetings as the server lists them; a participant may change when one is and how it was arranged. */
export function MeetingList({
  meetings,
  canRecord,
  command,
  onUpdate,
}: {
  meetings: Meeting[];
  canRecord: boolean;
  command: Command;
  onUpdate: (meeting: Meeting, change: MeetingRequest) => void;
}) {
  const [editing, setEditing] = useState<string | null>(null);
  if (meetings.length === 0) return <Empty>No meetings yet.</Empty>;
  return (
    <ul className="list">
      {meetings.map((meeting) => (
        <li key={meeting.id}>
          <div className="row">
            <strong>{label(meeting.kind)}</strong>
            <span>{meeting.scheduledAt ? when(meeting.scheduledAt) : <span className="unknown">Time not set</span>}</span>
            {meeting.arrangedOutsideApplication ? <span className="muted">arranged outside the application</span> : null}
            {canRecord && editing !== meeting.id ? (
              <button type="button" className="secondary" onClick={() => setEditing(meeting.id)}>
                Change
              </button>
            ) : null}
          </div>
          {editing === meeting.id ? (
            <MeetingForm
              initial={meeting}
              submitText="Save meeting"
              command={command}
              onSubmit={(change) => {
                onUpdate(meeting, change);
                setEditing(null);
              }}
            />
          ) : null}
        </li>
      ))}
    </ul>
  );
}
