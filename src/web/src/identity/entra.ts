import {
  InteractionRequiredAuthError,
  PublicClientApplication,
  type AccountInfo,
  type IPublicClientApplication,
} from "@azure/msal-browser";
import type { EntraSettings } from "./entraSettings";

/**
 * Sign-in through Microsoft Entra ID with the authorisation code flow and PKCE (MSAL). The employee signs in with their
 * Entra identity and no application password (AC-FR-001-01). Tokens stay in session storage for this tab.
 */
export class EntraIdentity {
  private constructor(
    private readonly msal: IPublicClientApplication,
    private readonly settings: EntraSettings,
    private account: AccountInfo | null,
  ) {}

  /** Starts MSAL and completes a sign-in redirect that brought the browser back here, if one did. */
  static async start(settings: EntraSettings, msal?: IPublicClientApplication): Promise<EntraIdentity> {
    const client =
      msal ??
      new PublicClientApplication({
        auth: {
          clientId: settings.clientId,
          authority: settings.authority,
          redirectUri: window.location.origin,
          postLogoutRedirectUri: window.location.origin,
        },
        cache: { cacheLocation: "sessionStorage" },
      });
    await client.initialize();
    const redirect = await client.handleRedirectPromise();
    const account = redirect?.account ?? client.getActiveAccount() ?? client.getAllAccounts()[0] ?? null;
    if (account) {
      client.setActiveAccount(account);
    }

    return new EntraIdentity(client, settings, account);
  }

  get signedIn(): boolean {
    return this.account !== null;
  }

  signIn(): Promise<void> {
    return this.msal.loginRedirect({ scopes: [this.settings.apiScope] });
  }

  signOut(): Promise<void> {
    return this.msal.logoutRedirect({ account: this.account ?? undefined });
  }

  /** The bearer token for the API, renewed silently, or by a redirect when Entra needs the employee again. */
  async headers(): Promise<Record<string, string>> {
    if (!this.account) {
      return {};
    }

    try {
      const token = await this.msal.acquireTokenSilent({ scopes: [this.settings.apiScope], account: this.account });
      return { Authorization: `Bearer ${token.accessToken}` };
    } catch (error) {
      if (error instanceof InteractionRequiredAuthError) {
        await this.msal.acquireTokenRedirect({ scopes: [this.settings.apiScope], account: this.account });
      }

      throw error;
    }
  }
}
