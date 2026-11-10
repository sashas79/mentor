import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { problem } from "./support/fakeApi";
import { People, simulatedApi, start } from "./support/world";

const relationshipId = "r0000000-0000-4000-8000-000000000001";
const goalId = "g0000000-0000-4000-8000-000000000001";

function relationship(overrides: Record<string, unknown> = {}) {
  return {
    id: relationshipId,
    provenance: "Digital",
    candidateId: "c1",
    protege: { employeeId: People.protege.employeeId, displayName: "Zoë Mokoena" },
    mentor: { employeeId: People.mentor.employeeId, displayName: "Thandi Nkosi" },
    state: "Complete",
    status: "Blank",
    isActive: "N",
    durationCategory: "TwelveMonths",
    startDate: "2026-12-01",
    endDate: null,
    source: null,
    createdAt: "2026-11-06T09:00:00Z",
    updatedAt: "2026-11-06T09:00:00Z",
    version: 4,
    ...overrides,
  };
}

function goal(overrides: Record<string, unknown> = {}) {
  return {
    id: goalId,
    relationshipId,
    formedAgreementId: "fa1",
    position: 1,
    agreedDescription: "Lead a design review by June 2027",
    completionStatus: "Incomplete",
    pendingCompletionRequestId: null,
    progress: [],
    createdAt: "2026-11-06T09:00:00Z",
    updatedAt: "2026-11-06T09:00:00Z",
    ...overrides,
  };
}

const schedule = {
  relationshipId,
  agreedFrequencyWeeks: 2,
  agreedDurationMinutes: 45,
  sessions: [
    {
      id: "s1",
      kind: "RecurringSession",
      candidateId: null,
      relationshipId,
      scheduledAt: "2026-12-03T09:00:00Z",
      arrangedOutsideApplication: false,
      frequencyWeeks: 2,
      durationMinutes: 45,
      createdByEmployeeId: People.protege.employeeId,
      createdAt: "2026-11-06T09:00:00Z",
      updatedAt: "2026-11-06T09:00:00Z",
    },
  ],
};

function mentorshipApi(read: () => unknown = () => relationship(), goals: () => unknown[] = () => [goal()]) {
  return simulatedApi()
    .on("GET", "/api/relationships/{relationshipId}", () => ({ body: read() }))
    .on("GET", "/api/relationships/{relationshipId}/goals", () => ({ body: goals() }))
    .on("GET", "/api/relationships/{relationshipId}/meetings", { body: schedule });
}

describe("Relationship attributes (AC-FR-047, AC-FR-048, AC-FR-049)", () => {
  it("offers Talent Management every value the contract names for each attribute, and sends only those chosen", async () => {
    let current = relationship();
    const api = mentorshipApi(() => current).on("PATCH", "/api/relationships/{relationshipId}", (request) => {
      current = relationship({ ...(request.body as object), version: 5 });
      return { body: current };
    });
    start({ api, as: People.administrator, at: `/relationships/${relationshipId}` });

    const form = await screen.findByRole("form", { name: "Change relationship attributes" });
    const [state, status, isActive] = within(form).getAllByRole("combobox");
    expect(within(state!).getAllByRole("option").map((option) => option.textContent)).toEqual(["Leave as it is", "In Progress", "On hold", "Complete"]);
    expect(within(status!).getAllByRole("option").map((option) => option.textContent)).toEqual(["Leave as it is", "Blank", "Needs reassigning"]);
    expect(within(isActive!).getAllByRole("option").map((option) => option.textContent)).toEqual(["Leave as it is", "Y", "N"]);

    // AC-FR-047-01 and AC-FR-049-01: Complete to In Progress, and IsActive Y, in one edit on the version read.
    await userEvent.selectOptions(state!, "InProgress");
    await userEvent.selectOptions(isActive!, "Y");
    await userEvent.click(within(form).getByRole("button", { name: "Save attributes" }));

    expect(await screen.findByText("The attributes are saved.")).toBeInTheDocument();
    const [edited] = api.to("PATCH", "/api/relationships/{relationshipId}");
    expect(edited?.body).toEqual({ state: "InProgress", isActive: "Y" });
    expect(edited?.headers.get("If-Match")).toBe('"4"');
    await waitFor(() => expect(screen.getByText("State", { selector: "dt" }).closest(".fact")).toHaveTextContent("In Progress"));
  });

  // AC-FR-048-01: Status alone, with State left as it is.
  it("changes Status alone", async () => {
    const api = mentorshipApi().on("PATCH", "/api/relationships/{relationshipId}", { body: relationship({ status: "NeedsReassigning", version: 5 }) });
    start({ api, as: People.administrator, at: `/relationships/${relationshipId}` });

    const form = await screen.findByRole("form", { name: "Change relationship attributes" });
    await userEvent.selectOptions(within(form).getAllByRole("combobox")[1]!, "NeedsReassigning");
    await userEvent.click(within(form).getByRole("button", { name: "Save attributes" }));

    await screen.findByText("The attributes are saved.");
    expect(api.to("PATCH", "/api/relationships/{relationshipId}")[0]?.body).toEqual({ status: "NeedsReassigning" });
  });

  // ADR-005: on 409 the relationship is read again so that the administrator decides on its current values.
  it("reads the relationship again when it changed since it was read", async () => {
    let reads = 0;
    const api = mentorshipApi(() => relationship(++reads > 1 ? { state: "OnHold", version: 6 } : {})).on(
      "PATCH",
      "/api/relationships/{relationshipId}",
      problem(409, "Relationship changed", "The relationship has changed since that version was read."),
    );
    start({ api, as: People.administrator, at: `/relationships/${relationshipId}` });

    const form = await screen.findByRole("form", { name: "Change relationship attributes" });
    await userEvent.selectOptions(within(form).getAllByRole("combobox")[0]!, "InProgress");
    await userEvent.click(within(form).getByRole("button", { name: "Save attributes" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Relationship changed");
    await waitFor(() => expect(screen.getByText("State", { selector: "dt" }).closest(".fact")).toHaveTextContent("On hold"));
  });

  it("does not offer the parties an attribute edit, and shows an unresolved legacy value as unknown", async () => {
    start({
      api: mentorshipApi(() => relationship({ provenance: "Legacy", candidateId: null, state: null, isActive: null, source: { file: "Tracker.xlsx", row: 8 } }), () => []),
      as: People.protege,
      at: `/relationships/${relationshipId}`,
    });

    expect(await screen.findByText("State", { selector: "dt" })).toBeInTheDocument();
    expect(screen.getByText("State", { selector: "dt" }).closest(".fact")).toHaveTextContent("Unknown — unresolved in the source");
    expect(screen.getByText("Source", { selector: "dt" }).closest(".fact")).toHaveTextContent("Tracker.xlsx, row 8");
    expect(screen.queryByRole("form", { name: "Change relationship attributes" })).toBeNull();
  });
});

describe("Goals, progress and completion (AC-FR-035 to AC-FR-037)", () => {
  // AC-FR-035-01: the protégé submits a progress update against a goal of their relationship.
  it("lets the protégé submit progress and request completion, which stays pending until the mentor decides", async () => {
    let current = goal();
    const api = mentorshipApi(undefined, () => [current])
      .on("POST", "/api/goals/{goalId}/progress", { body: goal() })
      .on("POST", "/api/goals/{goalId}/completion-requests", (request) => {
        current = goal({
          pendingCompletionRequestId: "p2",
          progress: [
            {
              id: "p2",
              protegeEmployeeId: People.protege.employeeId,
              progressUpdate: (request.body as { progressUpdate: string }).progressUpdate,
              completionRequested: true,
              submittedAt: "2026-11-07T09:00:00Z",
              completion: "Pending",
              decision: null,
            },
          ],
        });
        return { body: current };
      });
    start({ api, as: People.protege, at: `/relationships/${relationshipId}` });

    const update = await screen.findByRole("textbox", { name: /Progress update/ });
    await userEvent.type(update, "Drafted the review checklist");
    await userEvent.click(screen.getByRole("button", { name: "Submit progress" }));
    expect(await screen.findByText("Your progress is submitted.")).toBeInTheDocument();
    expect(api.to("POST", "/api/goals/{goalId}/progress")[0]?.body).toEqual({ progressUpdate: "Drafted the review checklist" });

    await userEvent.type(screen.getByRole("textbox", { name: /Progress update/ }), "Led the design review");
    await userEvent.click(screen.getByRole("button", { name: "Request completion" }));

    // AC-FR-036-01: visibly pending, and the goal is not complete.
    expect(await screen.findByText(/Completion requested\. The goal stays incomplete/)).toBeInTheDocument();
    expect(await screen.findByText("Completion requested — awaiting the mentor")).toBeInTheDocument();
    expect(screen.getByText("Completion request: Pending")).toBeInTheDocument();
    const card = screen.getByText("1. Lead a design review by June 2027").closest("li")!;
    expect(within(card).getByText("Incomplete")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Approve completion" })).toBeNull();
  });

  // AC-FR-037-01, AC-FR-037-02: only the mentor decides; the decision shows the mentor, the outcome and its time.
  it("lets the mentor approve a pending completion request", async () => {
    const request = {
      id: "p2",
      protegeEmployeeId: People.protege.employeeId,
      progressUpdate: "Led the design review",
      completionRequested: true,
      submittedAt: "2026-11-07T09:00:00Z",
      completion: "Pending",
      decision: null,
    };
    let current = goal({ pendingCompletionRequestId: "p2", progress: [request] });
    const api = mentorshipApi(undefined, () => [current]).on("POST", "/api/goals/{goalId}/completion-requests/{requestId}/decision", () => {
      current = goal({
        completionStatus: "Complete",
        progress: [
          {
            ...request,
            completion: "Approved",
            decision: { id: "d1", decision: "Approved", mentorEmployeeId: People.mentor.employeeId, decidedAt: "2026-11-08T09:00:00Z" },
          },
        ],
      });
      return { body: current };
    });
    start({ api, as: People.mentor, at: `/relationships/${relationshipId}` });

    expect(screen.queryByRole("textbox", { name: /Progress update/ })).toBeNull();
    await userEvent.click(await screen.findByRole("button", { name: "Approve completion" }));

    expect(await screen.findByText("Completion approved: the goal is complete.")).toBeInTheDocument();
    const [decided] = api.to("POST", "/api/goals/{goalId}/completion-requests/{requestId}/decision");
    expect(decided?.params).toEqual({ goalId, requestId: "p2" });
    expect(decided?.body).toEqual({ decision: "Approved" });
    expect(await screen.findByText("Completion request: Approved")).toBeInTheDocument();
    expect(screen.getByText(/Approved by Thandi Nkosi/)).toBeInTheDocument();
  });

  it("offers Talent Management no progress or decision on goals", async () => {
    start({ api: mentorshipApi(), as: People.administrator, at: `/relationships/${relationshipId}` });

    expect(await screen.findByText("1. Lead a design review by June 2027")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Submit progress" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Approve completion" })).toBeNull();
  });
});

describe("Meeting schedule (AC-FR-055-01, AC-FR-056-01)", () => {
  it("shows both participants the agreed frequency and length and the planned sessions, and plans another", async () => {
    const api = mentorshipApi().on("POST", "/api/relationships/{relationshipId}/meetings", { status: 201, body: schedule.sessions[0] });
    start({ api, as: People.mentor, at: `/relationships/${relationshipId}` });

    expect((await screen.findByText("Agreed frequency")).closest(".fact")).toHaveTextContent("Every 2 weeks");
    expect(screen.getByText("Agreed approximate length").closest(".fact")).toHaveTextContent("45 minutes");
    expect(screen.getByText("Recurring session")).toBeInTheDocument();

    await userEvent.type(screen.getAllByLabelText(/Date and time/).at(-1)!, "2026-12-17T09:00");
    await userEvent.click(screen.getByRole("button", { name: "Plan session" }));

    expect(await screen.findByText("The session is planned.")).toBeInTheDocument();
    const [planned] = api.to("POST", "/api/relationships/{relationshipId}/meetings");
    expect(planned?.body).toEqual({ scheduledAt: new Date("2026-12-17T09:00").toISOString(), arrangedOutsideApplication: false });
  });
});
