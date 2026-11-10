import { describe, expect, it } from "vitest";
import contract from "../../src/web/openapi/mentor-api.json";
import { ApiProblem, createApi, expect as expectData, ifMatch, problemOf } from "@/api/client";
import { contractEnums } from "@/api/enums.gen";
import { label } from "@/ui/labels";
import { BaseUrl, FakeApi, problem } from "./support/fakeApi";

describe("The generated API client (ADR-012, CONTRACT-MENTORSHIP-API)", () => {
  it("sends the caller's identity headers on every request", async () => {
    const api = new FakeApi().on("GET", "/api/session", { body: {} });
    api.install();
    const client = createApi(BaseUrl, () => Promise.resolve({ Authorization: "Bearer token" }));

    await expectData(client.GET("/api/session"));

    expect(api.received[0]?.headers.get("Authorization")).toBe("Bearer token");
  });

  it("names the version decided on as a strong entity tag", () => {
    expect(ifMatch(7)).toEqual({ "If-Match": '"7"' });
    expect(ifMatch("12")).toEqual({ "If-Match": '"12"' });
  });

  it("turns the server's problem into the message shown, with its field errors", async () => {
    const api = new FakeApi().on("PUT", "/api/employees/me", {
      status: 400,
      body: { title: "One or more validation errors occurred.", errors: { displayName: ["The display name is required."] } },
    });
    api.install();
    const client = createApi(BaseUrl, () => Promise.resolve({}));

    const refused = await expectData(client.PUT("/api/employees/me", { body: { displayName: "", levelTitle: null } })).catch((error: unknown) => error);

    expect(refused).toBeInstanceOf(ApiProblem);
    expect((refused as ApiProblem).fieldErrors).toEqual({ displayName: ["The display name is required."] });
    expect((refused as ApiProblem).isConflict).toBe(false);
  });

  it("describes a refusal without a body by its status", () => {
    expect(problemOf(403, undefined).title).toBe("You are not permitted to do this");
    expect(problemOf(409, problem(409, "Candidate changed").body).isConflict).toBe(true);
    expect(problemOf(428, {}).title).toBe("The version you decided on is required");
  });

  // The values the screens offer are the contract's, not the browser's.
  it("offers exactly the values the contract names", () => {
    const schemas = contract.components.schemas as Record<string, { enum?: (string | null)[] }>;
    for (const [name, values] of Object.entries(contractEnums)) {
      expect(values, name).toEqual(schemas[name]?.enum?.filter((value) => value !== null));
    }

    expect(contractEnums.RelationshipState).toEqual(["InProgress", "OnHold", "Complete"]);
    expect(contractEnums.RelationshipStatus).toEqual(["Blank", "NeedsReassigning"]);
    expect(contractEnums.ActiveFlag).toEqual(["Y", "N"]);
  });

  it("labels the business values as the BRS names them, and an absent value as unknown", () => {
    expect(["InProgress", "OnHold", "Complete", "NeedsReassigning", "TwelvePlusMonths"].map(label)).toEqual([
      "In Progress",
      "On hold",
      "Complete",
      "Needs reassigning",
      "12+ months",
    ]);
    expect(label(null)).toBe("Unknown");
    expect(label("SomethingNew")).toBe("SomethingNew");
  });

  it("uses only operations the contract publishes", () => {
    const operations = Object.entries(contract.paths as Record<string, Record<string, unknown>>).flatMap(([path, methods]) =>
      Object.keys(methods).map((method) => `${method.toUpperCase()} ${path}`),
    );
    for (const used of [
      "GET /api/capabilities",
      "GET /api/identity/simulated-roster",
      "GET /api/session",
      "PATCH /api/relationships/{relationshipId}",
      "POST /api/candidates/{candidateId}/draft/revisions/{revisionId}/acceptances",
      "GET /api/register/export",
      "GET /api/register/goals",
    ]) {
      expect(operations).toContain(used);
    }
  });
});
