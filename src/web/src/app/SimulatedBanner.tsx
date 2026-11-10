import { useIdentity } from "@/identity/IdentityProvider";
import { useSession } from "@/session/SessionProvider";

/**
 * The persistent label of a prototype run (ADR-002, ADR-012, AC-FR-001-03, AC-NFR-010-01): whenever the API runs the
 * simulated identity, every screen, sign-in included, says that the person is simulated and that this is not a
 * production Microsoft Entra sign-in.
 */
export function SimulatedBanner() {
  const { simulated } = useIdentity();
  const { session } = useSession();
  if (!simulated && session?.identityScheme !== "Simulated") {
    return null;
  }

  return (
    <div className="banner-simulated" role="note" aria-label="Simulated identity">
      Simulated identity — prototype only{session ? `: signed in as ${session.displayName} (simulated)` : ""}. This is not a
      production Microsoft Entra ID sign-in.
    </div>
  );
}
