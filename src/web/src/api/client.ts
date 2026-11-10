import createClient from "openapi-fetch";
import type { components, paths } from "./schema.gen";

/** The contract's schemas (CONTRACT-MENTORSHIP-API), as generated from openapi/mentor-api.json. */
export type Schemas = components["schemas"];

/** The headers that identify the signed-in caller: a bearer token under Entra, the roster person under simulation. */
export type IdentityHeaders = () => Promise<Record<string, string>>;

/**
 * The API client every screen uses: generated from the contract, so a request or a response the contract does not
 * define does not compile. Each request carries the caller's identity headers.
 */
export function createApi(baseUrl: string, identity: IdentityHeaders) {
  const client = createClient<paths>({
    baseUrl,
    // Resolved on each request, so that a test or a polyfill replacing fetch is honoured.
    fetch: (request) => globalThis.fetch(request),
  });
  client.use({
    async onRequest({ request }) {
      for (const [name, value] of Object.entries(await identity())) {
        request.headers.set(name, value);
      }

      return request;
    },
  });
  return client;
}

export type Api = ReturnType<typeof createApi>;

/**
 * The If-Match of a change to a candidate or a relationship: the version the caller decided on, as a strong entity tag
 * (ADR-005). The server refuses a change without one (428) and a change on a version that is no longer current (409).
 */
export function ifMatch(version: number | string): Record<string, string> {
  return { "If-Match": `"${version}"` };
}

/** A refusal or failure as the server reported it, in words a person can act on. */
export class ApiProblem extends Error {
  constructor(
    readonly status: number,
    readonly title: string,
    readonly detail: string | undefined,
    readonly fieldErrors: Record<string, string[]>,
  ) {
    super(detail ? `${title}: ${detail}` : title);
    this.name = "ApiProblem";
  }

  /** The record changed since it was read (or the request was otherwise in conflict): it is read again before deciding. */
  get isConflict(): boolean {
    return this.status === 409;
  }
}

const statusTitles: Record<number, string> = {
  400: "The request was not accepted",
  401: "You are not signed in",
  403: "You are not permitted to do this",
  404: "Not found",
  409: "This changed or cannot be done now",
  428: "The version you decided on is required",
};

/** The problem a failed response describes; the server's own title and detail when it gave them. */
export function problemOf(status: number, body: unknown): ApiProblem {
  const problem = (body && typeof body === "object" ? body : {}) as {
    title?: unknown;
    detail?: unknown;
    errors?: unknown;
  };
  const title = typeof problem.title === "string" && problem.title ? problem.title : (statusTitles[status] ?? `The server answered ${status}`);
  const detail = typeof problem.detail === "string" && problem.detail ? problem.detail : undefined;
  const fieldErrors: Record<string, string[]> = {};
  if (problem.errors && typeof problem.errors === "object") {
    for (const [field, messages] of Object.entries(problem.errors as Record<string, unknown>)) {
      if (Array.isArray(messages)) {
        fieldErrors[field] = messages.filter((message): message is string => typeof message === "string");
      }
    }
  }

  return new ApiProblem(status, title, detail, fieldErrors);
}

/**
 * The data of a successful call, or the server's problem thrown as an {@link ApiProblem}. The server's answer is the
 * outcome: nothing is retried, defaulted or second-guessed here.
 */
export async function expect<T>(call: Promise<{ data?: T; error?: unknown; response: Response }>): Promise<T> {
  const { data, error, response } = await call;
  if (!response.ok) {
    throw problemOf(response.status, error);
  }

  return data as T;
}
