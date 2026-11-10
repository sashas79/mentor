import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import type { EntraIdentity } from "@/identity/entra";
import { SimulatedIdentityHeader } from "@/identity/simulated";
import { FakeApi, problem } from "./support/fakeApi";
import { People, sessionOf, simulatedApi, start } from "./support/world";

const entraSettings = { clientId: "spa-client", authority: "https://login.microsoftonline.com/tenant", apiScope: "api://mentor/access_as_user" };

/** A stand-in for MSAL's sign-in: signed in or not, with the access token it would obtain. */
function fakeEntra(signedIn: boolean, signIn = () => Promise.resolve()) {
  return () =>
    Promise.resolve({
      signedIn,
      signIn,
      signOut: () => Promise.resolve(),
      headers: () => Promise.resolve({ Authorization: "Bearer entra-access-token" }),
    } as unknown as EntraIdentity);
}

function entraApi(): FakeApi {
  return new FakeApi()
    .on("GET", "/api/capabilities", { body: { identityMode: "Entra", notificationDelivery: "None" } })
    .on("GET", "/api/employees/me", (request) =>
      request.headers.get("Authorization") === "Bearer entra-access-token" ? { body: { id: People.protege.employeeId } } : { status: 401 },
    )
    .on("GET", "/api/session", (request) =>
      request.headers.get("Authorization") === "Bearer entra-access-token" ? { body: sessionOf(People.protege, "Entra") } : { status: 401 },
    )
    .on("GET", "/api/notifications", { body: { delivery: "None", events: [] } });
}

describe("Sign-in and identity (ADR-002)", () => {
  // AC-FR-001-03, AC-NFR-010-01: a prototype run with simulated identities says so on every screen, sign-in included.
  it("labels a simulated run on the sign-in screen and offers the server's demonstration roster", async () => {
    start({ api: simulatedApi() });

    expect(await screen.findByRole("note", { name: "Simulated identity" })).toHaveTextContent(
      "Simulated identity — prototype only. This is not a production Microsoft Entra ID sign-in.",
    );
    const roster = await screen.findByRole("list", { name: "Demonstration people" });
    expect(within(roster).getAllByRole("button").map((button) => button.textContent)).toEqual([
      "Continue as Zoë Mokoena",
      "Continue as Thandi Nkosi",
      "Continue as Lerato Khumalo",
    ]);
    expect(within(roster).getByText("— Talent Management administrator")).toBeInTheDocument();
  });

  it("signs in as the chosen roster person, names them on every request and keeps the banner on every screen", async () => {
    const api = simulatedApi();
    start({ api });

    await userEvent.click(await screen.findByRole("button", { name: "Continue as Thandi Nkosi" }));

    expect(await screen.findByRole("heading", { name: "Welcome, Thandi Nkosi" })).toBeInTheDocument();
    expect(screen.getByRole("note", { name: "Simulated identity" })).toHaveTextContent("signed in as Thandi Nkosi (simulated)");
    const signedIn = api.received.filter((request) => request.path !== "/api/capabilities" && request.path !== "/api/identity/simulated-roster");
    expect(signedIn.length).toBeGreaterThan(0);
    expect(signedIn.every((request) => request.headers.get(SimulatedIdentityHeader) === People.mentor.objectId)).toBe(true);
    // The employee's own profile is opened first, which records the employee, and then the session is read.
    expect(signedIn.slice(0, 2).map((request) => request.path)).toEqual(["/api/employees/me", "/api/session"]);

    await userEvent.click(screen.getByRole("link", { name: "Notifications" }));
    expect(await screen.findByRole("heading", { name: "Notifications" })).toBeInTheDocument();
    expect(screen.getByRole("note", { name: "Simulated identity" })).toBeInTheDocument();
  });

  it("signs out to the sign-in screen and forgets the simulated person", async () => {
    start({ api: simulatedApi(), as: People.protege });

    await userEvent.click(await screen.findByRole("button", { name: "Sign out" }));

    expect(await screen.findByRole("heading", { name: "Sign in to Mentorship" })).toBeInTheDocument();
    expect(window.sessionStorage.length).toBe(0);
  });

  // AC-FR-001-01: under Entra the employee signs in with Microsoft and no application password; the bearer token is sent.
  it("offers Microsoft sign-in under Entra, with no password field and no simulation banner", async () => {
    let redirected = false;
    start({
      api: entraApi(),
      entraSettings,
      startEntra: fakeEntra(false, () => {
        redirected = true;
        return Promise.resolve();
      }),
    });

    await userEvent.click(await screen.findByRole("button", { name: "Sign in with Microsoft" }));

    expect(redirected).toBe(true);
    expect(document.querySelector("input[type=password]")).toBeNull();
    expect(screen.queryByRole("note", { name: "Simulated identity" })).toBeNull();
  });

  it("calls the API with the Entra access token as the bearer token once signed in", async () => {
    const api = entraApi();
    start({ api, entraSettings, startEntra: fakeEntra(true) });

    expect(await screen.findByRole("heading", { name: "Welcome, Zoë Mokoena" })).toBeInTheDocument();
    expect(screen.getByText("Microsoft Entra ID")).toBeInTheDocument();
    expect(api.to("GET", "/api/session")[0]?.headers.get("Authorization")).toBe("Bearer entra-access-token");
    expect(screen.queryByRole("note", { name: "Simulated identity" })).toBeNull();
  });

  it("does not start under Entra when this deployment has no Entra registration", async () => {
    start({ api: entraApi(), entraSettings: null });

    expect(await screen.findByRole("alert")).toHaveTextContent("no Entra registration configured");
  });

  // AC-FR-001-02: the server rejects an unauthenticated caller, and the application shows that rather than any data.
  it("shows the server's refusal when the session is not authenticated", async () => {
    const api = simulatedApi().on("GET", "/api/session", problem(401, "Unauthorized"));
    start({ api, as: People.protege });

    expect(await screen.findByRole("alert")).toHaveTextContent("Unauthorized");
    expect(screen.queryByRole("navigation", { name: "Main" })).toBeNull();
  });

  it("says so when the service cannot be reached", async () => {
    start({ api: new FakeApi().on("GET", "/api/capabilities", { status: 503 }) });

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("could not be reached"));
  });
});
