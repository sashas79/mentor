import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { subjectLink } from "@/screens/Notifications";
import { People, simulatedApi, start } from "./support/world";

const invitation = {
  id: "n1",
  eventType: "InvitationSent",
  subjectType: "CANDIDATE",
  subjectId: "c1",
  recipientEmployeeId: People.mentor.employeeId,
  recipientRole: "Mentor",
  requiredAction: "RespondToInvitation",
  delivery: "Simulated",
  createdAt: "2026-11-02T09:00:00Z",
  deliveredAt: "2026-11-02T09:00:00Z",
  readAt: null,
};

describe("Notifications (AC-FR-057-01, AC-FR-057-02, AC-FR-057-03)", () => {
  it("names the material action, the recipient's role, the next action and the simulated delivery", async () => {
    const api = simulatedApi()
      .on("GET", "/api/notifications", { body: { delivery: "Simulated", events: [invitation] } })
      .on("POST", "/api/notifications/{notificationId}/read", { body: { ...invitation, readAt: "2026-11-02T10:00:00Z" } });
    start({ api, as: People.mentor, at: "/notifications" });

    const event = (await screen.findByText("Invitation sent")).closest("li")!;
    expect(event).toHaveTextContent("to you as Mentor");
    expect(event).toHaveTextContent("Next: Respond to the invitation");
    expect(within(event).getByRole("link", { name: "Open" })).toHaveAttribute("href", "/candidates/c1");
    expect(screen.getByText("Simulated — shown in the application only")).toBeInTheDocument();

    await userEvent.click(within(event).getByRole("button", { name: "Mark as read" }));

    expect(await screen.findByText("Marked as read.")).toBeInTheDocument();
    expect(api.to("POST", "/api/notifications/{notificationId}/read")[0]?.params.notificationId).toBe("n1");
  });

  // The inbox is the caller's own: the server is asked for nothing but it, and nobody's identity is supplied but the caller's.
  it("asks the server only for the caller's own inbox", async () => {
    const api = simulatedApi();
    start({ api, as: People.protege, at: "/notifications" });

    expect(await screen.findByText("You have no notifications.")).toBeInTheDocument();
    for (const request of api.to("GET", "/api/notifications")) {
      expect(request.headers.get("X-Simulated-Identity")).toBe(People.protege.objectId);
      expect([...request.query.keys()].filter((key) => key !== "unread")).toEqual([]);
    }
  });

  it("opens the record each subject type names", () => {
    expect(subjectLink({ subjectType: "MENTOR_PROFILE", subjectId: "p1" })).toBe("/mentor-profile");
    expect(subjectLink({ subjectType: "DEVELOPMENT_GOAL", subjectId: "g1" })).toBe("/goals/g1");
    expect(subjectLink({ subjectType: "SOMETHING_ELSE", subjectId: "x" })).toBeNull();
  });

  it("lists what awaits the employee on the overview", async () => {
    const api = simulatedApi().on("GET", "/api/notifications", (request) =>
      request.query.get("unread") === "true" ? { body: { delivery: "Simulated", events: [invitation] } } : { body: { delivery: "Simulated", events: [] } },
    );
    start({ api, as: People.mentor });

    expect(await screen.findByText("Respond to the invitation")).toBeInTheDocument();
  });
});
