import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { problem } from "./support/fakeApi";
import { People, simulatedApi, start } from "./support/world";

const candidateId = "c0000000-0000-4000-8000-000000000001";
const currentHash = "sha256:7d1f3c0a9e5b42d8c6a1f0e9b8d7c6a5f4e3d2c1b0a99887766554433221100";
const supersededHash = "sha256:0011223344556677889900aabbccddeeff00112233445566778899aabbccddee";

const content = {
  parties: {
    protege: { employeeId: People.protege.employeeId, displayName: "Zoë Mokoena" },
    mentor: { employeeId: People.mentor.employeeId, displayName: "Thandi Nkosi" },
  },
  policy: [
    { clause: "Voluntary partnership", text: "This mentorship is a voluntary partnership entered into by both parties." },
    { clause: "Confidentiality", text: "What is discussed in a session stays between the mentor and the protégé." },
  ],
  terms: {
    durationCategory: "TwelveMonths",
    startDate: "2026-12-01",
    endDate: null,
    meetingIntervalWeeks: 2,
    approximateMinutesPerMeeting: 45,
    betweenMeetingContact: { interval: 1, unit: "Weeks" },
    missedMeetingArrangements: "Tell the other a day ahead.",
    reviewDate: "2027-03-01",
  },
  goals: ["Lead a design review by June 2027"],
  barriers: ["Delivery deadlines"],
  contingencies: ["Move the meeting rather than skip it"],
  responsibilities: { mentor: ["Share experience"], protege: ["Prepare an agenda"] },
};

function draft(overrides: Record<string, unknown> = {}) {
  return {
    candidateId,
    candidateStatus: "Drafting",
    step: 2,
    protege: content.parties.protege,
    mentor: content.parties.mentor,
    currentRevision: {
      id: "rev-2",
      candidateId,
      revisionNumber: 2,
      parentRevisionId: "rev-1",
      contentHash: currentHash,
      createdByEmployeeId: People.protege.employeeId,
      createdAt: "2026-11-05T09:00:00Z",
      isCurrent: true,
      content,
      acceptances: [],
    },
    revisions: [
      { id: "rev-1", revisionNumber: 1, parentRevisionId: null, contentHash: supersededHash, createdByEmployeeId: People.protege.employeeId, createdAt: "2026-11-04T09:00:00Z", isCurrent: false },
      { id: "rev-2", revisionNumber: 2, parentRevisionId: "rev-1", contentHash: currentHash, createdByEmployeeId: People.protege.employeeId, createdAt: "2026-11-05T09:00:00Z", isCurrent: true },
    ],
    acceptances: [
      {
        id: "a1",
        revisionId: "rev-1",
        revisionNumber: 1,
        contentHash: supersededHash,
        participantEmployeeId: People.mentor.employeeId,
        participant: "Mentor",
        acceptedAt: "2026-11-04T10:00:00Z",
        applicability: "Superseded",
      },
    ],
    annex: {
      adminReference: "TM-2026-014",
      adminRemarks: null,
      changes: [],
      notice: "The administrative annex is kept by Talent Management and is not part of the content the participants accept.",
    },
    version: 7,
    ...overrides,
  };
}

describe("Agreement draft review and acceptance (AC-FR-022, AC-FR-027)", () => {
  // AC-FR-022-01, AC-FR-022-02: the complete content, fixed policy text included, is shown before acceptance.
  it("shows the complete current content with its policy text, its content identity and the labelled annex", async () => {
    start({ api: simulatedApi().on("GET", "/api/candidates/{candidateId}/draft", { body: draft() }), as: People.protege, at: `/candidates/${candidateId}/draft` });

    const agreement = await screen.findByRole("article", { name: "Mentorship agreement — revision 2" });
    for (const text of [
      "Voluntary partnership",
      "This mentorship is a voluntary partnership entered into by both parties.",
      "Confidentiality",
      "Lead a design review by June 2027",
      "Delivery deadlines",
      "Move the meeting rather than skip it",
      "Share experience",
      "Prepare an agenda",
      "12 months",
      "2 weeks",
      "45 minutes",
      "At least every 1 weeks",
      "Tell the other a day ahead.",
    ]) {
      expect(within(agreement).getByText(text)).toBeInTheDocument();
    }

    expect(within(agreement).getByText(currentHash)).toBeInTheDocument();
    // An unknown end date stays unknown.
    expect(within(agreement).getByText("End date").closest(".fact")).toHaveTextContent("Not set");
    const annex = screen.getByRole("complementary", { name: "Administrative annex" });
    expect(annex).toHaveTextContent("Administrative annex — not part of the accepted agreement");
    expect(annex).toHaveTextContent("TM-2026-014");
    expect(within(agreement).queryByText("TM-2026-014")).toBeNull();
  });

  // AC-FR-027-02, AC-FR-027-03: each participant accepts the exact current content, by its identity, for themselves.
  it("records the participant's own acceptance of exactly the current content, on the version read", async () => {
    const api = simulatedApi()
      .on("GET", "/api/candidates/{candidateId}/draft", { body: draft() })
      .on("POST", "/api/candidates/{candidateId}/draft/revisions/{revisionId}/acceptances", { body: draft() });
    start({ api, as: People.protege, at: `/candidates/${candidateId}/draft` });

    await userEvent.click(await screen.findByRole("button", { name: "I accept revision 2" }));

    expect(await screen.findByText("You accepted revision 2.")).toBeInTheDocument();
    const [accepted] = api.to("POST", "/api/candidates/{candidateId}/draft/revisions/{revisionId}/acceptances");
    expect(accepted?.params).toEqual({ candidateId, revisionId: "rev-2" });
    expect(accepted?.body).toEqual({ contentHash: currentHash });
    expect(accepted?.headers.get("If-Match")).toBe('"7"');
  });

  // AC-FR-027-03, AC-FR-027-04: an acceptance of superseded content is shown as not counting towards formation.
  it("lists every acceptance with the revision it was given to and whether it still counts", async () => {
    start({ api: simulatedApi().on("GET", "/api/candidates/{candidateId}/draft", { body: draft() }), as: People.protege, at: `/candidates/${candidateId}/draft` });

    const row = (await screen.findByRole("cell", { name: "Thandi Nkosi (Mentor)" })).closest("tr")!;
    expect(within(row).getAllByRole("cell").map((cell) => cell.textContent)).toEqual([
      "Thandi Nkosi (Mentor)",
      "1",
      expect.stringContaining("sha256:001122334"),
      expect.any(String),
      "Superseded by a replacement draft",
    ]);
  });

  it("does not offer acceptance or revision to Talent Management, who keeps the annex", async () => {
    const api = simulatedApi()
      .on("GET", "/api/candidates/{candidateId}/draft", { body: draft() })
      .on("POST", "/api/candidates/{candidateId}/draft/annex", { body: draft() });
    start({ api, as: People.administrator, at: `/candidates/${candidateId}/draft` });

    await screen.findByRole("article", { name: "Mentorship agreement — revision 2" });
    expect(screen.queryByRole("button", { name: "I accept revision 2" })).toBeNull();
    expect(screen.queryByRole("form", { name: "Agreement draft" })).toBeNull();

    const form = screen.getByRole("form", { name: "Change the administrative annex" });
    await userEvent.selectOptions(within(form).getByRole("combobox"), "AdministrativeRemark");
    await userEvent.type(within(form).getByRole("textbox"), "Filed with the register.");
    await userEvent.click(within(form).getByRole("button", { name: "Change annex" }));

    expect(await screen.findByText(/The annex is changed/)).toBeInTheDocument();
    const [changed] = api.to("POST", "/api/candidates/{candidateId}/draft/annex");
    expect(changed?.body).toEqual({ basis: "AdministrativeRemark", value: "Filed with the register." });
    expect(changed?.headers.get("If-Match")).toBe('"7"');
  });

  // FR-067: a replacement is a new draft; the values entered are sent as the contract names them.
  it("sends a replacement revision from the entered values", async () => {
    const api = simulatedApi()
      .on("GET", "/api/candidates/{candidateId}/draft", { body: draft() })
      .on("POST", "/api/candidates/{candidateId}/draft/revisions", { status: 201, body: draft() });
    start({ api, as: People.mentor, at: `/candidates/${candidateId}/draft` });

    await userEvent.click(await screen.findByRole("button", { name: "Revise" }));
    const form = screen.getByRole("form", { name: "Agreement draft" });
    const goals = within(form).getByRole("textbox", { name: /Development goals/ });
    await userEvent.clear(goals);
    await userEvent.type(goals, "Lead a design review{enter}{enter}Present at the forum");
    await userEvent.click(within(form).getByRole("button", { name: "Save draft revision" }));

    expect(await screen.findByText("The draft revision is saved.")).toBeInTheDocument();
    const [revised] = api.to("POST", "/api/candidates/{candidateId}/draft/revisions");
    expect(revised?.headers.get("If-Match")).toBe('"7"');
    expect(revised?.body).toMatchObject({
      terms: { durationCategory: "TwelveMonths", startDate: "2026-12-01", endDate: null, meetingIntervalWeeks: 2, betweenMeetingContact: { interval: 1, unit: "Weeks" } },
      goals: ["Lead a design review", "Present at the forum"],
      responsibilities: { mentor: ["Share experience"], protege: ["Prepare an agenda"] },
    });
  });

  it("shows the server's refusal of an acceptance as it gave it", async () => {
    const api = simulatedApi()
      .on("GET", "/api/candidates/{candidateId}/draft", { body: draft() })
      .on("POST", "/api/candidates/{candidateId}/draft/revisions/{revisionId}/acceptances", problem(409, "Revision superseded", "A newer revision is current."));
    start({ api, as: People.protege, at: `/candidates/${candidateId}/draft` });

    await userEvent.click(await screen.findByRole("button", { name: "I accept revision 2" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Revision superseded");
    expect(api.to("GET", "/api/candidates/{candidateId}/draft").length).toBe(2);
  });
});

describe("Formation (COMP-FORMATION)", () => {
  const formation = {
    candidateId,
    candidateStatus: "Drafting",
    step: 3,
    currentRevisionId: "rev-2",
    decisions: [],
    relationship: null,
    version: 7,
  };

  it("lets Talent Management approve the current revision, bound to its content identity and the version read", async () => {
    const api = simulatedApi()
      .on("GET", "/api/candidates/{candidateId}/draft", { body: draft() })
      .on("GET", "/api/candidates/{candidateId}/formation", { body: formation })
      .on("POST", "/api/candidates/{candidateId}/draft/revisions/{revisionId}/formation-decisions", { body: formation });
    start({ api, as: People.administrator, at: `/candidates/${candidateId}/formation` });

    await userEvent.click(await screen.findByRole("button", { name: "Approve formation" }));

    expect(await screen.findByText(/Formation approved/)).toBeInTheDocument();
    const [decided] = api.to("POST", "/api/candidates/{candidateId}/draft/revisions/{revisionId}/formation-decisions");
    expect(decided?.params.revisionId).toBe("rev-2");
    expect(decided?.body).toEqual({ outcome: "Approved", contentHash: currentHash });
    expect(decided?.headers.get("If-Match")).toBe('"7"');
  });

  it("does not offer participants a formation decision", async () => {
    const api = simulatedApi()
      .on("GET", "/api/candidates/{candidateId}/draft", { body: draft() })
      .on("GET", "/api/candidates/{candidateId}/formation", { body: formation });
    start({ api, as: People.protege, at: `/candidates/${candidateId}/formation` });

    expect(await screen.findByRole("heading", { name: "Formation" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Approve formation" })).toBeNull();
  });
});
