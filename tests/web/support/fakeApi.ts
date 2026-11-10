import { vi } from "vitest";

export const BaseUrl = "http://api.test";

/** One request the application sent, as the server received it. */
export interface Received {
  method: string;
  path: string;
  params: Record<string, string>;
  query: URLSearchParams;
  headers: Headers;
  body: unknown;
}

export interface Answer {
  status?: number;
  body?: unknown;
  headers?: Record<string, string>;
  /** A body sent as it is, such as a CSV file. */
  raw?: string;
}

type Handler = (request: Received) => Answer | Promise<Answer>;

/**
 * A stand-in for the API: it answers the routes a test declares, refuses every other one with 404 and records every
 * request, so a test asserts what the application sent (bodies, If-Match, identity headers) and shows the server's
 * answers, refusals included, as the outcome.
 */
export class FakeApi {
  readonly received: Received[] = [];
  private readonly routes: { method: string; pattern: RegExp; keys: string[]; handler: Handler }[] = [];

  on(method: string, template: string, answer: Handler | Answer | unknown[]): this {
    const keys: string[] = [];
    const pattern = new RegExp(
      `^${template.replace(/[.*+?^$()|[\]\\]/g, "\\$&").replace(/\{(\w+)\}/g, (_match, key: string) => {
        keys.push(key);
        return "([^/]+)";
      })}$`,
    );
    const handler: Handler = typeof answer === "function" ? (answer as Handler) : Array.isArray(answer) ? () => ({ body: answer }) : () => answer as Answer;
    // Later declarations win, so a test can replace an answer it set up before.
    this.routes.unshift({ method, pattern, keys, handler });
    return this;
  }

  /** The requests to a route template, oldest first. */
  to(method: string, template: string): Received[] {
    const route = new RegExp(`^${template.replace(/\{\w+\}/g, "[^/]+")}$`);
    return this.received.filter((request) => request.method === method && route.test(request.path));
  }

  install(): void {
    vi.stubGlobal("fetch", (input: Request) => this.handle(input));
  }

  private async handle(request: Request): Promise<Response> {
    const url = new URL(request.url);
    const text = await request.clone().text();
    const received: Received = {
      method: request.method,
      path: url.pathname,
      params: {},
      query: url.searchParams,
      headers: request.headers,
      body: text ? (JSON.parse(text) as unknown) : undefined,
    };
    this.received.push(received);
    for (const route of this.routes) {
      const match = route.method === request.method ? route.pattern.exec(url.pathname) : null;
      if (!match) continue;
      route.keys.forEach((key, index) => {
        received.params[key] = decodeURIComponent(match[index + 1] ?? "");
      });
      const answer = await route.handler(received);
      const status = answer.status ?? 200;
      if (answer.raw !== undefined) {
        return new Response(answer.raw, { status, headers: answer.headers });
      }

      return answer.body === undefined
        ? new Response(null, { status, headers: answer.headers })
        : new Response(JSON.stringify(answer.body), {
            status,
            headers: { "Content-Type": status >= 400 ? "application/problem+json" : "application/json", ...answer.headers },
          });
    }

    return new Response(null, { status: 404 });
  }
}

export function problem(status: number, title: string, detail?: string): Answer {
  return { status, body: { status, title, detail } };
}
