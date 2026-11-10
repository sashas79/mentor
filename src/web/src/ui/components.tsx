import type { ReactNode } from "react";
import type { ApiProblem } from "@/api/client";
import type { Command, Loaded } from "./hooks";

export function Page({ title, intro, children }: { title: string; intro?: ReactNode; children: ReactNode }) {
  return (
    <section className="page" aria-labelledby="page-title">
      <h1 id="page-title">{title}</h1>
      {intro ? <p className="intro">{intro}</p> : null}
      {children}
    </section>
  );
}

export function Section({ title, children, actions }: { title: string; children: ReactNode; actions?: ReactNode }) {
  return (
    <section className="panel">
      <header className="panel-header">
        <h2>{title}</h2>
        {actions ? <div className="panel-actions">{actions}</div> : null}
      </header>
      {children}
    </section>
  );
}

/** The server's refusal or failure, as it gave it. Field messages are listed under it. */
export function ProblemMessage({ problem }: { problem?: ApiProblem }) {
  if (!problem) return null;
  const fields = Object.entries(problem.fieldErrors);
  return (
    <div className="message message-problem" role="alert">
      <strong>{problem.title}</strong>
      {problem.detail ? <p>{problem.detail}</p> : null}
      {problem.isConflict ? <p>The latest version has been read again; review it and decide again.</p> : null}
      {fields.length > 0 ? (
        <ul>
          {fields.flatMap(([field, messages]) => messages.map((message) => <li key={`${field}:${message}`}>{message}</li>))}
        </ul>
      ) : null}
    </div>
  );
}

/** A command's outcome: the server's problem, or what was done. */
export function Outcome({ command }: { command: Command }) {
  if (command.problem) return <ProblemMessage problem={command.problem} />;
  if (command.done) {
    return (
      <div className="message message-done" role="status">
        {command.done}
      </div>
    );
  }

  return null;
}

/** What a screen shows once it is read: a placeholder while reading, the server's problem if it refused. */
export function Show<T>({ loaded, children }: { loaded: Loaded<T>; children: (data: T) => ReactNode }) {
  if (loaded.problem && loaded.data === undefined) return <ProblemMessage problem={loaded.problem} />;
  if (loaded.data === undefined) {
    return (
      <p className="muted" role="status">
        Loading…
      </p>
    );
  }

  return <>{children(loaded.data)}</>;
}

export function Facts({ items }: { items: [string, ReactNode][] }) {
  return (
    <dl className="facts">
      {items.map(([term, value]) => (
        <div key={term} className="fact">
          <dt>{term}</dt>
          <dd>{value}</dd>
        </div>
      ))}
    </dl>
  );
}

export function Badge({ children, tone = "neutral" }: { children: ReactNode; tone?: "neutral" | "good" | "warn" | "info" }) {
  return <span className={`badge badge-${tone}`}>{children}</span>;
}

export function Empty({ children }: { children: ReactNode }) {
  return <p className="empty">{children}</p>;
}

export function Field({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return (
    <label className="field">
      <span className="field-label">{label}</span>
      {children}
      {hint ? <span className="field-hint">{hint}</span> : null}
    </label>
  );
}

/** One text per line, as a list the server receives in order; blank lines are dropped. */
export function linesOf(text: string): string[] {
  return text
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}
