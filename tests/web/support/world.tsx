import { render } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import type { Schemas } from "@/api/client";
import { App } from "@/app/App";
import type { EntraIdentity } from "@/identity/entra";
import type { EntraSettings } from "@/identity/entraSettings";
import { IdentityProvider } from "@/identity/IdentityProvider";
import { rememberSimulatedPerson, SimulatedIdentityHeader } from "@/identity/simulated";
import { SessionProvider } from "@/session/SessionProvider";
import { BaseUrl, FakeApi } from "./fakeApi";

type Session = Schemas["Session"];

/** The permissions each actor's roles reach, as the server's matrix (ADR-003) reports them in the session. */
const employeePermissions = [
  "session.read",
  "employee-profile.read",
  "employee-profile.update",
  "mentor-application.create",
  "mentor-profile.read",
  "mentor-discovery.list",
  "mentorship-request.create",
  "mentorship-request.read",
  "candidate.create",
  "candidate.read",
  "candidate.record-willingness",
  "candidate.withdraw",
  "agreement-draft.read",
  "agreement-draft.create-revision",
  "agreement-draft.accept",
  "formation.read",
  "relationship.read",
  "goals.read",
  "goals.submit-progress",
  "goals.request-completion",
  "goals.decide-completion",
  "meeting.read",
  "meeting.record",
  "notification.read",
  "notification.mark-read",
];

const talentManagementPermissions = [
  "mentor-application.list-pending",
  "mentor-application.decide",
  "agreement-draft.change-annex",
  "formation.decide",
  "relationship.update-attributes",
  "register.read",
  "register.export",
  "audit-trail.read",
];

export interface Person {
  objectId: string;
  employeeId: string;
  displayName: string;
  roles: Schemas["FunctionalRole"][];
  permissions: string[];
}

export const People: Record<"protege" | "mentor" | "administrator", Person> = {
  protege: {
    objectId: "11111111-0000-4000-8000-000000000001",
    employeeId: "e1111111-0000-4000-8000-000000000001",
    displayName: "Zoë Mokoena",
    roles: ["Employee"],
    permissions: employeePermissions,
  },
  mentor: {
    objectId: "22222222-0000-4000-8000-000000000002",
    employeeId: "e2222222-0000-4000-8000-000000000002",
    displayName: "Thandi Nkosi",
    roles: ["Employee", "Mentor"],
    permissions: [...employeePermissions, "mentor-profile.update", "candidate.respond"],
  },
  administrator: {
    objectId: "33333333-0000-4000-8000-000000000003",
    employeeId: "e3333333-0000-4000-8000-000000000003",
    displayName: "Lerato Khumalo",
    roles: ["Employee", "TalentManagement"],
    permissions: [...employeePermissions, ...talentManagementPermissions],
  },
};

export function sessionOf(person: Person, scheme: Schemas["IdentityScheme"] = "Simulated"): Session {
  return {
    objectId: person.objectId,
    displayName: person.displayName,
    identityScheme: scheme,
    employeeId: person.employeeId,
    roles: person.roles,
    matrixVersion: 1,
    permissions: person.permissions,
  };
}

/**
 * A prototype API run under the simulated identity, with the three demonstration people. The session follows the person
 * the request names, as the server's does.
 */
export function simulatedApi(): FakeApi {
  const people = Object.values(People);
  const byObjectId = (objectId: string | null) => people.find((person) => person.objectId === objectId);
  return new FakeApi()
    .on("GET", "/api/capabilities", { body: { identityMode: "Simulated", notificationDelivery: "Simulated" } })
    .on("GET", "/api/identity/simulated-roster", {
      body: people.map((person) => ({
        objectId: person.objectId,
        displayName: person.displayName,
        declaredRole: person.roles.includes("TalentManagement") ? "TalentManagementAdministrator" : "Employee",
      })),
    })
    .on("GET", "/api/employees/me", (request) => {
      const person = byObjectId(request.headers.get(SimulatedIdentityHeader));
      return person
        ? {
            body: {
              id: person.employeeId,
              entraObjectId: person.objectId,
              displayName: person.displayName,
              levelTitle: null,
              createdAt: "2026-11-01T08:00:00Z",
              updatedAt: "2026-11-01T08:00:00Z",
            },
          }
        : { status: 401 };
    })
    .on("GET", "/api/session", (request) => {
      const person = byObjectId(request.headers.get(SimulatedIdentityHeader));
      return person ? { body: sessionOf(person) } : { status: 401 };
    })
    .on("GET", "/api/notifications", { body: { delivery: "Simulated", events: [] } });
}

interface Start {
  api: FakeApi;
  /** Signed in already as this person under the simulated identity, or not signed in. */
  as?: Person;
  at?: string;
  entraSettings?: EntraSettings | null;
  startEntra?: (settings: EntraSettings) => Promise<EntraIdentity>;
}

/** The application as main.tsx composes it, on the fake API, at a route. */
export function start({ api, as, at = "/", entraSettings = null, startEntra }: Start) {
  api.install();
  if (as) {
    rememberSimulatedPerson(as.objectId);
  }

  return render(
    <IdentityProvider baseUrl={BaseUrl} entraSettings={entraSettings} {...(startEntra ? { startEntra } : {})}>
      <SessionProvider>
        <MemoryRouter initialEntries={[at]}>
          <App />
        </MemoryRouter>
      </SessionProvider>
    </IdentityProvider>,
  );
}
