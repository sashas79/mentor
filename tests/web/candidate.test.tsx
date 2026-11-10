import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { problem, type Received } from "./support/fakeApi";
import { People, simulatedApi, start } from "./support/world";

const candidateId = "c0000000-0000-4000-8000-000000000001";

function candidate(overrides: Record<string, unknown> = {}) {
  return {
    id: candidateId,
    requestId: "q1",
    protegeEmployeeId: People.protege.employeeId,
    protegeDisplayName: "Zoë Mokoena",
    mentorEmployeeId: People.mentor.employeeId,
    mentorDisplayName: "Thandi Nkosi",
    status: "Invited",
    mentorResponse: "Pending",
    invitedAt: "2026-11-02T09:00:00Z",
    mentorRespondedAt: null,
    protegeWillingAt: null,
    mentorWillingAt: null,
    withdrawnByEmployeeId: null,
    withdrawnAt: null,
    currentRevisionId: null,
    version: 3,
    updatedAt: "2026-11-02T09:00:00Z",
    ...overrides,
  };
}

function pairingApi(read: () => unknown = () => candidate()) {
  return simulatedApi()
    .on("GET", "/api/candidates/{candidateId}", () => ({ body: read() }))
    .on("GET", "/api/candidates/{candidateId}/meetings", []);
}

describe("Pairing: invitation, response and willingness (AC-FR-018 to AC-FR-020)", () => {
  // AC-FR-019-01: the invited mentor accepts or declines, on the version they read.
  it("offers the invited mentor to accept or decline, and sends the version read in If-Match", async () => {
    let current = candidate();
    const api = pairingApi(() => current).on("POST", "/api/candidates/{candidateId}/accept", () => {
      current = candidate({ status: "Accepted", mentorResponse: "Accepted", mentorRespondedAt: "2026-11-03T10:00:00Z", version: 4 });
      return { body: current };
    });
    start({ api, as: People.mentor, at: `/candidates/${candidateId}` });

    await userEvent.click(await screen.findByRole("button", { name: "Accept invitation" }));

    expect(await screen.findByRole("status")).toHaveTextContent("You accepted the invitation.");
    const [accepted] = api.to("POST", "/api/candidates/{candidateId}/accept");
    expect(accepted?.headers.get("If-Match")).toBe('"3"');
    expect(accepted?.headers.get("X-Simulated-Identity")).toBe(People.mentor.objectId);
    expect(await screen.findByText(/Accepted, /)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Decline invitation" })).toBeInTheDocument();
  });

  it("does not offer the protégé the mentor's response", async () => {
    start({ api: pairingApi(), as: People.protege, at: `/candidates/${candidateId}` });

    expect(await screen.findByRole("button", { name: "Record my willingness to proceed" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Accept invitation" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Decline invitation" })).toBeNull();
  });

  // AC-FR-019-02, AC-FR-020-01, AC-FR-020-02: each participant's willingness is shown on its own; one is not both.
  it("shows each participant's willingness separately", async () => {
    start({
      api: pairingApi(() => candidate({ status: "Accepted", mentorResponse: "Accepted", protegeWillingAt: "2026-11-04T08:00:00Z" })),
      as: People.protege,
      at: `/candidates/${candidateId}`,
    });

    expect(await screen.findByText("Protégé willing to proceed")).toBeInTheDocument();
    const facts = screen.getByText("Mentor willing to proceed").closest(".fact");
    expect(facts).toHaveTextContent("Not recorded");
    expect(screen.getByText("Protégé willing to proceed").closest(".fact")).not.toHaveTextContent("Not recorded");
  });

  // ADR-005, ADR-012: on 409 the pairing is read again and the person decides on its current state.
  it("reads the pairing again when the server answers that it changed, and shows why", async () => {
    let reads = 0;
    const api = pairingApi(() => {
      reads += 1;
      return candidate(reads > 1 ? { status: "Declined", mentorResponse: "Declined", version: 5 } : {});
    }).on("POST", "/api/candidates/{candidateId}/willingness", problem(409, "Candidate changed", "The candidate has changed since it was read."));
    start({ api, as: People.protege, at: `/candidates/${candidateId}` });

    await userEvent.click(await screen.findByRole("button", { name: "Record my willingness to proceed" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Candidate changed");
    expect(alert).toHaveTextContent("The latest version has been read again");
    await waitFor(() => expect(screen.getByText("Standing").closest(".fact")).toHaveTextContent("Declined"));
    expect(reads).toBe(2);
  });

  // AC-FR-018-02: a proposed introduction belongs to the pairing and may be arranged outside the application.
  it("proposes an introductory meeting marked as arranged outside the application, with nothing about its content", async () => {
    const proposed: Received[] = [];
    const api = pairingApi().on("POST", "/api/candidates/{candidateId}/meetings", (request) => {
      proposed.push(request);
      return { status: 201, body: {} };
    });
    start({ api, as: People.protege, at: `/candidates/${candidateId}` });

    await userEvent.click(await screen.findByRole("checkbox", { name: /Arranged outside the application/ }));
    await userEvent.click(screen.getByRole("button", { name: "Propose meeting" }));

    expect(await screen.findByText("The introductory meeting is proposed.")).toBeInTheDocument();
    expect(proposed[0]?.params.candidateId).toBe(candidateId);
    expect(proposed[0]?.body).toEqual({ scheduledAt: null, arrangedOutsideApplication: true });
  });

  it("lists the pairing's meetings", async () => {
    const api = pairingApi().on("GET", "/api/candidates/{candidateId}/meetings", [
      {
        id: "m1",
        kind: "Introductory",
        candidateId,
        relationshipId: null,
        scheduledAt: "2026-11-05T10:00:00Z",
        arrangedOutsideApplication: true,
        frequencyWeeks: null,
        durationMinutes: null,
        createdByEmployeeId: People.protege.employeeId,
        createdAt: "2026-11-02T09:00:00Z",
        updatedAt: "2026-11-02T09:00:00Z",
      },
    ]);
    start({ api, as: People.mentor, at: `/candidates/${candidateId}` });

    const meetings = (await screen.findByText("Introductory meeting")).closest("li")!;
    expect(within(meetings).getByText("arranged outside the application")).toBeInTheDocument();
  });
});
