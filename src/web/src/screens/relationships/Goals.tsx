import { useState } from "react";
import type { Schemas } from "@/api/client";
import { Badge, Empty, Field } from "@/ui/components";
import type { Command } from "@/ui/hooks";
import { label, when } from "@/ui/labels";

type Goal = Schemas["GoalView"];

/**
 * A goal's agreed wording, its completion status, and every progress entry with its standing (FR-034 to FR-040). A
 * completion request stays visibly pending until the mentor decides, and the goal is complete only by the mentor's
 * approval (AC-FR-036-01, AC-FR-037-01). A decision shows the mentor, the outcome and its time, and has no reason
 * (AC-FR-037-02). Only structured goals and progress exist: nothing said in a meeting is recorded or shown.
 */
export function GoalCard({
  goal,
  mentorName,
  asProtege,
  asMentor,
  command,
  onProgress,
  onRequestCompletion,
  onDecide,
}: {
  goal: Goal;
  mentorName: string;
  asProtege: boolean;
  asMentor: boolean;
  command: Command;
  onProgress: (goal: Goal, update: string) => Promise<boolean>;
  onRequestCompletion: (goal: Goal, update: string) => Promise<boolean>;
  onDecide: (goal: Goal, requestId: string, decision: Schemas["GoalDecision"]) => void;
}) {
  const [update, setUpdate] = useState("");
  const pending = goal.pendingCompletionRequestId;

  return (
    <li id={`goal-${goal.id}`}>
      <div className="row">
        <strong>
          {goal.position}. {goal.agreedDescription}
        </strong>
        <Badge tone={goal.completionStatus === "Complete" ? "good" : "neutral"}>{label(goal.completionStatus)}</Badge>
        {pending ? <Badge tone="info">Completion requested — awaiting the mentor</Badge> : null}
      </div>
      {goal.progress.length === 0 ? (
        <Empty>No progress submitted yet.</Empty>
      ) : (
        <ol>
          {goal.progress.map((entry) => (
            <li key={entry.id}>
              <span className="muted">{when(entry.submittedAt)}</span> {entry.progressUpdate}
              {entry.completionRequested ? (
                <>
                  {" "}
                  <Badge tone={entry.completion === "Approved" ? "good" : entry.completion === "Rejected" ? "warn" : "info"}>
                    Completion request: {label(entry.completion)}
                  </Badge>
                  {entry.decision ? (
                    <span className="muted">
                      {" "}
                      {label(entry.decision.decision)} by {mentorName}, {when(entry.decision.decidedAt)}
                    </span>
                  ) : null}
                </>
              ) : null}
            </li>
          ))}
        </ol>
      )}
      {asProtege ? (
        <div className="stack">
          <Field label="Progress update" hint="What you have done towards this goal. Do not record what was said in a meeting.">
            <textarea value={update} onChange={(event) => setUpdate(event.target.value)} />
          </Field>
          <div className="row">
            <button
              type="button"
              disabled={command.pending || !update.trim()}
              onClick={() => void onProgress(goal, update).then((ok) => ok && setUpdate(""))}
            >
              Submit progress
            </button>
            <button
              type="button"
              className="secondary"
              disabled={command.pending || !update.trim()}
              onClick={() => void onRequestCompletion(goal, update).then((ok) => ok && setUpdate(""))}
            >
              Request completion
            </button>
          </div>
        </div>
      ) : null}
      {asMentor && pending ? (
        <div className="row">
          <span>The protégé asks you to confirm this goal is complete.</span>
          <button type="button" disabled={command.pending} onClick={() => onDecide(goal, pending, "Approved")}>
            Approve completion
          </button>
          <button type="button" className="secondary" disabled={command.pending} onClick={() => onDecide(goal, pending, "Rejected")}>
            Reject
          </button>
        </div>
      ) : null}
    </li>
  );
}
