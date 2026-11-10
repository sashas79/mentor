import { screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { People, simulatedApi, start, type Person } from "./support/world";

async function areas(person: Person): Promise<string[]> {
  start({ api: simulatedApi(), as: person });
  const navigation = await screen.findByRole("navigation", { name: "Main" });
  return within(navigation)
    .getAllByRole("heading")
    .map((heading) => heading.textContent ?? "");
}

describe("Role-appropriate screens (ADR-012, AC-FR-003-01, AC-FR-003-02, AC-NFR-006-01)", () => {
  it("offers an employee the protégé and mentoring areas, and no administration", async () => {
    expect(await areas(People.protege)).toEqual(["My mentoring", "Find a mentor", "Mentoring others"]);
    expect(screen.queryByRole("link", { name: "Relationship register" })).toBeNull();
  });

  // An employee may perform both mentor and protégé functions.
  it("offers a mentor the protégé area as well as their own", async () => {
    expect(await areas(People.mentor)).toEqual(["My mentoring", "Find a mentor", "Mentoring others"]);
    expect(screen.getByRole("link", { name: "Mentors" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Mentor profile" })).toBeInTheDocument();
  });

  it("offers Talent Management its administration area", async () => {
    expect(await areas(People.administrator)).toContain("Talent Management");
    for (const name of ["Mentor applications", "Pairings and formation", "Relationship register", "Goals and progress"]) {
      expect(screen.getByRole("link", { name })).toBeInTheDocument();
    }
  });

  // AC-FR-060-02: the register is not offered to anyone else through the interface, and is not even requested.
  it.each([
    ["/admin/register", "/api/register"],
    ["/admin/goals", "/api/register/goals"],
  ])("refuses %s to an employee without asking the server for it", async (route, api) => {
    const fake = simulatedApi();
    start({ api: fake, as: People.mentor, at: route });

    expect(await screen.findByRole("alert")).toHaveTextContent("This area is not available to you.");
    expect(fake.to("GET", api)).toHaveLength(0);
  });
});
