import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { fileNameOf } from "@/screens/admin/Register";
import { problem } from "./support/fakeApi";
import { People, simulatedApi, start } from "./support/world";

const register = [
  {
    kind: "Relationship",
    relationshipId: "r-legacy",
    reviewExceptionId: null,
    provenance: "Legacy",
    mentor: { employeeId: People.mentor.employeeId, displayName: "Thandi Nkosi", level: "Principal Engineer" },
    protege: { employeeId: People.protege.employeeId, displayName: "Zoë Mokoena", level: null },
    state: "Complete",
    status: null,
    isActive: null,
    startDate: "2026-01-10",
    endDate: null,
    durationCategory: "SixMonths",
    source: {
      file: "Mentorship Tracker 2026.xlsx",
      row: 8,
      mentorText: "J. van der Merwe",
      protegeText: "Zoë Mokoena",
      activeText: "Successfully complete",
      durationText: "6 months",
      startText: "10/1/2026",
      endText: "ongoing",
    },
    reviewException: { reason: "UnresolvedValue", unresolvedAttributes: ["Status", "IsActive"] },
  },
  {
    kind: "HeldSourceRow",
    relationshipId: null,
    reviewExceptionId: "x-held",
    provenance: "Legacy",
    mentor: null,
    protege: null,
    state: null,
    status: null,
    isActive: null,
    startDate: null,
    endDate: null,
    durationCategory: null,
    source: {
      file: "Mentorship Tracker 2026.xlsx",
      row: 9,
      mentorText: null,
      protegeText: "S. Dlamini",
      activeText: "Yes",
      durationText: "ongoing",
      startText: "2025",
      endText: null,
    },
    reviewException: { reason: "NoMentor", unresolvedAttributes: [] },
  },
  {
    kind: "Relationship",
    relationshipId: "r-digital",
    reviewExceptionId: null,
    provenance: "Digital",
    mentor: { employeeId: People.mentor.employeeId, displayName: "Thandi Nkosi", level: "Principal Engineer" },
    protege: { employeeId: People.protege.employeeId, displayName: "Zoë Mokoena", level: null },
    state: "InProgress",
    status: "Blank",
    isActive: "N",
    startDate: "2026-12-01",
    endDate: "2027-11-30",
    durationCategory: "TwelveMonths",
    source: null,
    reviewException: null,
  },
];

describe("Relationship register and export (AC-FR-060-01, AC-FR-060-02)", () => {
  it("shows every row as stored: unknowns stay unknown beside their source text, and historical rows are marked", async () => {
    start({ api: simulatedApi().on("GET", "/api/register", register), as: People.administrator, at: "/admin/register" });

    const table = await screen.findByRole("table", { name: "Relationship register" });
    const [legacy, held, digital] = within(table).getAllByRole("row").slice(1);

    // The legacy row: Complete from its source, Status and IsActive unknown and flagged, no invented end date.
    const legacyCells = within(legacy!).getAllByRole("cell").map((cell) => cell.textContent);
    expect(legacyCells.slice(2, 8)).toEqual([
      "CompleteSource: “Successfully complete”",
      "Unknown",
      "Unknown",
      expect.stringContaining("Source: “10/1/2026”"),
      "UnknownSource: “ongoing”",
      "6 monthsSource: “6 months”",
    ]);
    expect(legacyCells[8]).toBe("Historical (entered from the tracker)Mentorship Tracker 2026.xlsx, row 8");
    expect(legacyCells[9]).toContain("Unresolved value: Status, IsActive");

    // The held row: its source names only, its reason, no resolved values.
    const heldCells = within(held!).getAllByRole("cell").map((cell) => cell.textContent);
    expect(heldCells[0]).toBe("Unknown");
    expect(heldCells[1]).toBe("UnknownSource: “S. Dlamini”");
    expect(heldCells[9]).toContain("Held for review");
    expect(heldCells[9]).toContain("No mentor in the source");
    expect(within(held!).queryByRole("link")).toBeNull();

    // The digital row: a BLANK Status is the business value Blank, not unknown, and no review is needed.
    const digitalCells = within(digital!).getAllByRole("cell").map((cell) => cell.textContent);
    expect(digitalCells.slice(2, 5)).toEqual(["In Progress", "Blank", "N"]);
    expect(digitalCells[8]).toBe("Formed in the application");
    expect(within(digital!).getByRole("link", { name: "Open" })).toHaveAttribute("href", "/relationships/r-digital");
  });

  it("downloads the server's CSV export under the name the server gives it", async () => {
    const csv = "﻿Mentor,Mentor level\r\n";
    const api = simulatedApi()
      .on("GET", "/api/register", register)
      .on("GET", "/api/register/export", {
        raw: csv,
        headers: { "Content-Type": "text/csv; charset=utf-8", "Content-Disposition": "attachment; filename=mentorship-register-20261110T080000Z.csv" },
      });
    const created = vi.fn((_file: Blob) => "blob:register");
    vi.stubGlobal("URL", Object.assign(URL, { createObjectURL: created, revokeObjectURL: vi.fn() }));
    let downloaded: string | undefined;
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) {
      downloaded = this.download;
    });
    start({ api, as: People.administrator, at: "/admin/register" });

    await userEvent.click(await screen.findByRole("button", { name: "Export as CSV" }));

    expect(await screen.findByRole("status")).toHaveTextContent("The register is exported.");
    expect(downloaded).toBe("mentorship-register-20261110T080000Z.csv");
    // The file is saved byte for byte, byte-order mark included, so a spreadsheet reads it as UTF-8.
    const saved = new Uint8Array(await created.mock.calls[0]![0].arrayBuffer());
    expect([...saved]).toEqual([...new TextEncoder().encode(csv)]);
    expect([...saved.slice(0, 3)]).toEqual([0xef, 0xbb, 0xbf]);
    expect(api.to("GET", "/api/register/export")).toHaveLength(1);
  });

  it("shows the server's refusal of an export", async () => {
    const api = simulatedApi().on("GET", "/api/register", register).on("GET", "/api/register/export", problem(409, "No employee profile yet"));
    start({ api, as: People.administrator, at: "/admin/register" });

    await userEvent.click(await screen.findByRole("button", { name: "Export as CSV" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("No employee profile yet");
  });

  it("reads the file name from either form of Content-Disposition", () => {
    expect(fileNameOf("attachment; filename=a.csv; filename*=UTF-8''mentorship%20register.csv")).toBe("mentorship register.csv");
    expect(fileNameOf('attachment; filename="b.csv"')).toBe("b.csv");
    expect(fileNameOf(null)).toBe("mentorship-register.csv");
  });
});
