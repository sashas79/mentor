import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { problem } from "./support/fakeApi";
import { People, simulatedApi, start } from "./support/world";

const mentorProfile = {
  id: "mp1",
  employeeId: People.mentor.employeeId,
  displayName: "Thandi Nkosi",
  levelTitle: "Principal Engineer",
  biography: "Fifteen years building payment platforms.",
  roleTitle: "Principal Engineer",
  industryExperience: "Fintech and retail banking",
  mentoringExperience: "Mentored six engineers",
  approvalStatus: "Approved",
  approvalDecidedByEmployeeId: People.administrator.employeeId,
  approvalDecidedAt: "2026-10-30T09:00:00Z",
  createdAt: "2026-10-29T09:00:00Z",
  updatedAt: "2026-10-30T09:00:00Z",
};

describe("Employee profile (AC-FR-005-01, AC-FR-005-02)", () => {
  it("saves the employee's own profile, kept in the application", async () => {
    const api = simulatedApi().on("PUT", "/api/employees/me", (request) => ({ body: { ...(request.body as object), id: People.protege.employeeId } }));
    start({ api, as: People.protege, at: "/profile" });

    const level = await screen.findByRole("textbox", { name: /Level or title/ });
    await userEvent.type(level, "Software Engineer");
    await userEvent.click(screen.getByRole("button", { name: "Save profile" }));

    expect(await screen.findByText("Your profile is saved.")).toBeInTheDocument();
    expect(api.to("PUT", "/api/employees/me")[0]?.body).toEqual({ displayName: "Zoë Mokoena", levelTitle: "Software Engineer" });
  });
});

describe("Mentor application and profile (AC-FR-007-01, AC-FR-010-01)", () => {
  it("offers the application to an employee who has not applied", async () => {
    const api = simulatedApi()
      .on("GET", "/api/mentor-profiles/me", { status: 404 })
      .on("POST", "/api/mentor-applications", { status: 201, body: { ...mentorProfile, approvalStatus: "Pending" } });
    start({ api, as: People.protege, at: "/mentor-profile" });

    await userEvent.type(await screen.findByRole("textbox", { name: /Biography/ }), "Ten years in cloud platforms.");
    await userEvent.type(screen.getByRole("textbox", { name: /Role or title/ }), "Senior Engineer");
    await userEvent.click(screen.getByRole("button", { name: "Apply to mentor" }));

    expect(await screen.findByText(/Your application is submitted/)).toBeInTheDocument();
    expect(api.to("POST", "/api/mentor-applications")[0]?.body).toEqual({
      biography: "Ten years in cloud platforms.",
      roleTitle: "Senior Engineer",
      industryExperience: null,
      mentoringExperience: null,
    });
  });

  it("lets a mentor maintain their biography and attributes, and shows their standing", async () => {
    const api = simulatedApi()
      .on("GET", "/api/mentor-profiles/me", { body: mentorProfile })
      .on("PUT", "/api/mentor-profiles/me", { body: mentorProfile });
    start({ api, as: People.mentor, at: "/mentor-profile" });

    expect((await screen.findByText("Standing")).closest(".fact")).toHaveTextContent("Approved");
    const experience = screen.getByRole("textbox", { name: /Mentoring experience/ });
    await userEvent.clear(experience);
    await userEvent.type(experience, "Mentored eight engineers");
    await userEvent.click(screen.getByRole("button", { name: "Save mentor profile" }));

    expect(await screen.findByText("Your mentor profile is saved.")).toBeInTheDocument();
    expect(api.to("PUT", "/api/mentor-profiles/me")[0]?.body).toMatchObject({ mentoringExperience: "Mentored eight engineers" });
  });
});

describe("Mentor discovery, requests and invitations (AC-FR-010-02, AC-FR-012-01, AC-FR-014 to AC-FR-018)", () => {
  it("lists the approved mentors and opens a profile with its attributes", async () => {
    const other = { ...mentorProfile, id: "mp2", employeeId: "e9", displayName: "Sipho Dlamini", biography: "Data engineering lead." };
    const api = simulatedApi()
      .on("GET", "/api/mentors", [mentorProfile, other])
      .on("GET", "/api/mentor-profiles/{id}", (request) => ({ body: request.params.id === "mp2" ? other : mentorProfile }))
      .on("GET", "/api/mentorship-requests", []);
    start({ api, as: People.protege, at: "/mentors" });

    const list = await screen.findByRole("list", { name: "Approved mentors" });
    expect(within(list).getAllByRole("link").map((link) => link.textContent)).toEqual(["Thandi Nkosi", "Sipho Dlamini"]);

    await userEvent.click(within(list).getByRole("link", { name: "Thandi Nkosi" }));

    expect(await screen.findByRole("heading", { name: "Thandi Nkosi", level: 1 })).toBeInTheDocument();
    expect(screen.getByText("Fintech and retail banking")).toBeInTheDocument();
    expect(screen.getByText("Mentored six engineers")).toBeInTheDocument();
  });

  it("submits a request with its motivation, goals and nontechnical expectations as separate fields", async () => {
    const api = simulatedApi()
      .on("GET", "/api/mentorship-requests", [])
      .on("POST", "/api/mentorship-requests", { status: 201, body: {} });
    start({ api, as: People.protege, at: "/requests" });

    await userEvent.type(await screen.findByRole("textbox", { name: /Why are you seeking mentorship/ }), "To grow as an engineer");
    await userEvent.type(screen.getByRole("textbox", { name: /Technical and career development goals/ }), "Lead a design review");
    await userEvent.type(screen.getByRole("textbox", { name: /beyond technical skills/ }), "Confidence presenting");
    await userEvent.click(screen.getByRole("button", { name: "Submit request" }));

    expect(await screen.findByText(/Your request is submitted/)).toBeInTheDocument();
    expect(api.to("POST", "/api/mentorship-requests")[0]?.body).toEqual({
      motivation: "To grow as an engineer",
      developmentGoals: "Lead a design review",
      nontechnicalExpectations: "Confidence presenting",
    });
  });

  // AC-FR-018-01: the selected mentor is associated with the protégé's request.
  it("invites the mentor for the protégé's chosen request and opens the pairing", async () => {
    const request = {
      id: "q1",
      protegeEmployeeId: People.protege.employeeId,
      protegeDisplayName: "Zoë Mokoena",
      motivation: "To grow as an engineer",
      developmentGoals: "Lead a design review",
      nontechnicalExpectations: "Confidence",
      disposition: "Open",
      submittedAt: "2026-11-01T09:00:00Z",
      updatedAt: "2026-11-01T09:00:00Z",
    };
    const api = simulatedApi()
      .on("GET", "/api/mentor-profiles/{id}", { body: mentorProfile })
      .on("GET", "/api/mentorship-requests", [request])
      .on("POST", "/api/mentorship-requests/{requestId}/candidates", { status: 201, body: { id: "c-new" } })
      .on("GET", "/api/candidates/{candidateId}", problem(404, "Not found"))
      .on("GET", "/api/candidates/{candidateId}/meetings", []);
    start({ api, as: People.protege, at: "/mentors/mp1" });

    await userEvent.selectOptions(await screen.findByRole("combobox", { name: "For your request" }), "q1");
    await userEvent.click(screen.getByRole("button", { name: "Invite this mentor" }));

    const [invited] = api.to("POST", "/api/mentorship-requests/{requestId}/candidates");
    await screen.findByText("Not found");
    expect(invited?.params.requestId).toBe("q1");
    expect(invited?.body).toEqual({ mentorEmployeeId: People.mentor.employeeId });
    expect(api.to("GET", "/api/candidates/{candidateId}")[0]?.params.candidateId).toBe("c-new");
  });
});
