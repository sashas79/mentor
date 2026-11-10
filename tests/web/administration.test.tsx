import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { People, simulatedApi, start } from "./support/world";

describe("Talent Management administration", () => {
  // AC-FR-040-01: the administrator views each mentorship's goals and structured progress.
  it("shows every mentorship's goals, completion status and progress", async () => {
    const api = simulatedApi().on("GET", "/api/register/goals", [
      {
        relationshipId: "r1",
        provenance: "Digital",
        mentor: { employeeId: People.mentor.employeeId, displayName: "Thandi Nkosi", level: null },
        protege: { employeeId: People.protege.employeeId, displayName: "Zoë Mokoena", level: null },
        state: "InProgress",
        status: "Blank",
        isActive: "Y",
        summary: { total: 2, complete: 1, incomplete: 1, awaitingDecision: 1 },
        goals: [
          {
            id: "g1",
            relationshipId: "r1",
            formedAgreementId: "fa1",
            position: 1,
            agreedDescription: "Lead a design review",
            completionStatus: "Complete",
            pendingCompletionRequestId: null,
            progress: [
              {
                id: "p1",
                protegeEmployeeId: People.protege.employeeId,
                progressUpdate: "Led the billing design review",
                completionRequested: true,
                submittedAt: "2026-11-07T09:00:00Z",
                completion: "Approved",
                decision: null,
              },
            ],
            createdAt: "2026-11-06T09:00:00Z",
            updatedAt: "2026-11-08T09:00:00Z",
          },
          {
            id: "g2",
            relationshipId: "r1",
            formedAgreementId: "fa1",
            position: 2,
            agreedDescription: "Present at the architecture forum",
            completionStatus: "Incomplete",
            pendingCompletionRequestId: "p3",
            progress: [],
            createdAt: "2026-11-06T09:00:00Z",
            updatedAt: "2026-11-06T09:00:00Z",
          },
        ],
      },
      {
        relationshipId: "r2",
        provenance: "Legacy",
        mentor: { employeeId: People.mentor.employeeId, displayName: "Thandi Nkosi", level: null },
        protege: { employeeId: "e8", displayName: "Sipho Dlamini", level: null },
        state: null,
        status: null,
        isActive: null,
        summary: { total: 0, complete: 0, incomplete: 0, awaitingDecision: 0 },
        goals: [],
      },
    ]);
    start({ api, as: People.administrator, at: "/admin/goals" });

    expect(await screen.findByText("1 of 2 goals complete")).toBeInTheDocument();
    expect(screen.getByText("1 awaiting the mentor's decision")).toBeInTheDocument();
    expect(screen.getByText(/Led the billing design review/)).toHaveTextContent("completion request: Approved");
    expect(screen.getByText(/completion request: Approved/)).toBeInTheDocument();
    expect(screen.getByText("Present at the architecture forum").closest("li")).toHaveTextContent("Completion requested");
    expect(screen.getByText("No agreed goals — a historical mentorship.")).toBeInTheDocument();
  });

  it("approves a pending mentor application", async () => {
    const pending = {
      id: "mp9",
      employeeId: "e9",
      displayName: "Sipho Dlamini",
      levelTitle: null,
      biography: "Data engineering lead.",
      roleTitle: "Lead Engineer",
      industryExperience: null,
      mentoringExperience: null,
      approvalStatus: "Pending",
      approvalDecidedByEmployeeId: null,
      approvalDecidedAt: null,
      createdAt: "2026-11-01T09:00:00Z",
      updatedAt: "2026-11-01T09:00:00Z",
    };
    let decided = false;
    const api = simulatedApi()
      .on("GET", "/api/mentor-applications/pending", () => ({ body: decided ? [] : [pending] }))
      .on("POST", "/api/mentor-applications/{id}/approve", () => {
        decided = true;
        return { body: { ...pending, approvalStatus: "Approved" } };
      });
    start({ api, as: People.administrator, at: "/admin/applications" });

    await userEvent.click(await screen.findByRole("button", { name: "Approve" }));

    expect(await screen.findByText("Sipho Dlamini is approved as a mentor.")).toBeInTheDocument();
    expect(api.to("POST", "/api/mentor-applications/{id}/approve")[0]?.params.id).toBe("mp9");
    expect(await screen.findByText("No application awaits a decision.")).toBeInTheDocument();
  });

  it("lists every pairing the trail names, each read as itself", async () => {
    const entry = (recordId: string) => ({ id: `${recordId}-entry`, recordId, recordType: "Candidate", action: "InvitationSent" });
    const pairing = (id: string, protege: string, status: string, updatedAt: string) => ({
      id,
      requestId: "q",
      protegeEmployeeId: "p",
      protegeDisplayName: protege,
      mentorEmployeeId: People.mentor.employeeId,
      mentorDisplayName: "Thandi Nkosi",
      status,
      mentorResponse: "Accepted",
      invitedAt: "2026-11-01T09:00:00Z",
      mentorRespondedAt: null,
      protegeWillingAt: null,
      mentorWillingAt: null,
      withdrawnByEmployeeId: null,
      withdrawnAt: null,
      currentRevisionId: null,
      version: 1,
      updatedAt,
    });
    const api = simulatedApi()
      .on("GET", "/api/audit-trail/records/{recordType}", [entry("c1"), entry("c2"), entry("c1")])
      .on("GET", "/api/candidates/{candidateId}", (request) => ({
        body:
          request.params.candidateId === "c1"
            ? pairing("c1", "Zoë Mokoena", "Drafting", "2026-11-05T09:00:00Z")
            : pairing("c2", "Sipho Dlamini", "Formed", "2026-11-08T09:00:00Z"),
      }));
    start({ api, as: People.administrator, at: "/admin/candidates" });

    const rows = (await screen.findAllByRole("row")).slice(1);
    expect(rows.map((row) => within(row).getAllByRole("cell")[0]?.textContent)).toEqual(["Sipho Dlamini", "Zoë Mokoena"]);
    expect(api.to("GET", "/api/audit-trail/records/{recordType}")[0]?.params.recordType).toBe("Candidate");
    expect(api.to("GET", "/api/candidates/{candidateId}")).toHaveLength(2);
    expect(within(rows[1]!).getByRole("link", { name: "Formation" })).toHaveAttribute("href", "/candidates/c1/formation");
  });
});
