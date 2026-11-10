/** The Entra application registration the SPA signs in with (ADR-002, CONTRACT-ENTRA-IDENTITY). */
export interface EntraSettings {
  clientId: string;
  authority: string;
  /** The API's delegated scope; the access token for it is sent as the bearer token. */
  apiScope: string;
}

/** The settings from the build environment, or none when the deployment has not been configured for Entra. */
export function entraSettingsFrom(env: ImportMetaEnv): EntraSettings | null {
  const clientId = env.VITE_ENTRA_CLIENT_ID?.trim();
  const authority = env.VITE_ENTRA_AUTHORITY?.trim();
  const apiScope = env.VITE_ENTRA_API_SCOPE?.trim();
  return clientId && authority && apiScope ? { clientId, authority, apiScope } : null;
}
