import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { createApi, expect, type Api, type Schemas } from "@/api/client";
import type { EntraIdentity } from "./entra";
import { entraSettingsFrom, type EntraSettings } from "./entraSettings";
import { rememberedSimulatedPerson, rememberSimulatedPerson, simulatedHeaders } from "./simulated";

export type Capabilities = Schemas["Capabilities"];
export type SimulatedPerson = Schemas["SimulatedPerson"];

type Phase =
  /** The API's capabilities are being read; nothing can be offered before they are known. */
  | "starting"
  /** The API could not be reached, or Entra is required but this deployment has no Entra settings. */
  | "unavailable"
  | "signedOut"
  | "signedIn";

export interface Identity {
  phase: Phase;
  /** Why the application cannot start, when phase is unavailable. */
  unavailableReason?: string;
  capabilities?: Capabilities;
  /** True when the API runs the labelled simulated identity: every screen says so. */
  simulated: boolean;
  /** The demonstration roster offered at simulated sign-in. */
  roster: SimulatedPerson[];
  api: Api;
  signInAs(objectId: string): void;
  signInWithEntra(): Promise<void>;
  signOut(): Promise<void>;
}

const IdentityContext = createContext<Identity | null>(null);

export function useIdentity(): Identity {
  const identity = useContext(IdentityContext);
  if (!identity) {
    throw new Error("useIdentity is used outside IdentityProvider.");
  }

  return identity;
}

interface IdentityProviderProps {
  children: ReactNode;
  baseUrl?: string;
  entraSettings?: EntraSettings | null;
  startEntra?: (settings: EntraSettings) => Promise<EntraIdentity>;
}

const configuredEntra = entraSettingsFrom(import.meta.env);
// MSAL is loaded only when the API requires Entra; a simulated prototype run never needs it.
const startConfiguredEntra = async (settings: EntraSettings) => (await import("./entra")).EntraIdentity.start(settings);

/**
 * Reads the API's capabilities before anyone signs in (ADR-002, ADR-012), then signs the employee in the way the API
 * authenticates: Entra, or the labelled simulated identity when the API reports that mode. The server authenticates
 * and authorises every request either way; the browser only carries the identity.
 */
export function IdentityProvider({
  children,
  baseUrl = import.meta.env.VITE_API_BASE_URL ?? "",
  entraSettings = configuredEntra,
  startEntra = startConfiguredEntra,
}: IdentityProviderProps) {
  const [phase, setPhase] = useState<Phase>("starting");
  const [unavailableReason, setUnavailableReason] = useState<string>();
  const [capabilities, setCapabilities] = useState<Capabilities>();
  const [roster, setRoster] = useState<SimulatedPerson[]>([]);
  const entra = useRef<EntraIdentity | null>(null);
  // MSAL is started once: React may run the start-up effect twice, and two instances would race for the sign-in redirect.
  const entraStarting = useRef<Promise<EntraIdentity> | null>(null);
  const simulatedPerson = useRef<string | null>(null);

  const api = useMemo(
    () =>
      createApi(baseUrl, async () => {
        if (simulatedPerson.current) {
          return simulatedHeaders(simulatedPerson.current);
        }

        return entra.current ? entra.current.headers() : {};
      }),
    [baseUrl],
  );

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const found = await expect(api.GET("/api/capabilities"));
        if (cancelled) return;
        setCapabilities(found);
        if (found.identityMode === "Simulated") {
          const people = await expect(api.GET("/api/identity/simulated-roster"));
          if (cancelled) return;
          setRoster(people);
          const remembered = rememberedSimulatedPerson();
          if (remembered && people.some((person) => person.objectId === remembered)) {
            simulatedPerson.current = remembered;
            setPhase("signedIn");
          } else {
            setPhase("signedOut");
          }

          return;
        }

        if (!entraSettings) {
          setUnavailableReason(
            "The API requires Microsoft Entra ID sign-in, and this deployment of the application has no Entra registration configured.",
          );
          setPhase("unavailable");
          return;
        }

        entraStarting.current ??= startEntra(entraSettings);
        entra.current = await entraStarting.current;
        if (cancelled) return;
        setPhase(entra.current.signedIn ? "signedIn" : "signedOut");
      } catch {
        if (cancelled) return;
        setUnavailableReason("The mentorship service could not be reached. Try again in a moment.");
        setPhase("unavailable");
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [api, entraSettings, startEntra]);

  const signInAs = useCallback((objectId: string) => {
    simulatedPerson.current = objectId;
    rememberSimulatedPerson(objectId);
    setPhase("signedIn");
  }, []);

  const signInWithEntra = useCallback(async () => {
    await entra.current?.signIn();
  }, []);

  const signOut = useCallback(async () => {
    if (simulatedPerson.current) {
      simulatedPerson.current = null;
      rememberSimulatedPerson(null);
      setPhase("signedOut");
      return;
    }

    await entra.current?.signOut();
    setPhase("signedOut");
  }, []);

  const value = useMemo<Identity>(
    () => ({
      phase,
      unavailableReason,
      capabilities,
      simulated: capabilities?.identityMode === "Simulated",
      roster,
      api,
      signInAs,
      signInWithEntra,
      signOut,
    }),
    [phase, unavailableReason, capabilities, roster, api, signInAs, signInWithEntra, signOut],
  );

  return <IdentityContext.Provider value={value}>{children}</IdentityContext.Provider>;
}
