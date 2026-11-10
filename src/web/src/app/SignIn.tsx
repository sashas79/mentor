import { useIdentity } from "@/identity/IdentityProvider";
import { label } from "@/ui/labels";

/**
 * Sign-in, the way the API authenticates (ADR-002). Under Entra the employee signs in with their Microsoft Entra ID
 * identity and no application password (AC-FR-001-01). Under the labelled simulation, a person of the server's
 * demonstration roster is chosen; their role is the one the roster declares.
 */
export function SignIn() {
  const identity = useIdentity();

  if (identity.phase === "starting") {
    return (
      <main className="shell-main">
        <p role="status">Connecting to the mentorship service…</p>
      </main>
    );
  }

  if (identity.phase === "unavailable") {
    return (
      <main className="shell-main">
        <h1>Mentorship</h1>
        <div className="message message-problem" role="alert">
          {identity.unavailableReason}
        </div>
      </main>
    );
  }

  if (identity.simulated) {
    return (
      <main className="shell-main">
        <h1>Sign in to Mentorship</h1>
        <p className="intro">
          This prototype runs with simulated identities. Choose a demonstration person to continue as; the server decides what
          they may do.
        </p>
        <ul className="list" aria-label="Demonstration people">
          {identity.roster.map((person) => (
            <li key={person.objectId} className="row">
              <span>
                <strong>{person.displayName}</strong> <span className="muted">— {label(person.declaredRole)}</span>
              </span>
              <button type="button" onClick={() => identity.signInAs(person.objectId)}>
                Continue as {person.displayName}
              </button>
            </li>
          ))}
        </ul>
      </main>
    );
  }

  return (
    <main className="shell-main">
      <h1>Sign in to Mentorship</h1>
      <p className="intro">Sign in with your Dariel Microsoft account. The application has no password of its own.</p>
      <button type="button" onClick={() => void identity.signInWithEntra()}>
        Sign in with Microsoft
      </button>
    </main>
  );
}
