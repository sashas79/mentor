/**
 * The labelled prototype identity (ADR-002). When the API reports identity mode Simulated, a person of the API's fixed
 * demonstration roster is chosen at sign-in, and every request names them by object identifier in this header. The
 * roster and the roles it declares are the server's; the browser only remembers whom the user chose, for this tab.
 * A simulated sign-in is never a production Entra sign-in (AC-FR-001-03).
 */
export const SimulatedIdentityHeader = "X-Simulated-Identity";

const storageKey = "mentor.simulated-identity";

/** The roster person chosen in this tab, if any. Storage can be unavailable; then nobody is remembered. */
export function rememberedSimulatedPerson(): string | null {
  try {
    return window.sessionStorage.getItem(storageKey);
  } catch {
    return null;
  }
}

export function rememberSimulatedPerson(objectId: string | null): void {
  try {
    if (objectId) {
      window.sessionStorage.setItem(storageKey, objectId);
    } else {
      window.sessionStorage.removeItem(storageKey);
    }
  } catch {
    // Without storage the choice lasts until the page is reloaded.
  }
}

export function simulatedHeaders(objectId: string): Record<string, string> {
  return { [SimulatedIdentityHeader]: objectId };
}
