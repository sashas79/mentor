# Mentorship web frontend

The React and TypeScript single-page application of the mentorship service (COMP-WEB-FRONTEND, ADR-012). It is built with Vite and deployed as static assets, separately from the API. It provides role-appropriate screens for protégés, mentors and Talent Management administrators. Every rule is the API's: the frontend shows what the server returns, offers the actions the server reports the caller's roles reach, and shows the server's refusals as the outcome.

## Run it

Requires Node.js 20.17 or later.

```sh
npm install
npm run dev        # http://localhost:5173, proxying /api to MENTOR_API_URL (default http://localhost:5080)
npm run build      # generates the API client, type-checks the app and the tests, and writes dist/
npm test           # the tests in ../../tests/web
```

For a prototype run without Entra, start the API with `Identity:Mode=Simulated`. The sign-in screen then offers the API's demonstration roster, and a banner on every screen says that the identity is simulated and is not a production Microsoft Entra ID sign-in.

## Configuration

Copy `.env.example` to `.env.local`:

| Variable | Meaning |
|---|---|
| `VITE_API_BASE_URL` | Where the browser reaches the API. Empty means this origin. A separately hosted API must list this origin in `Cors:AllowedOrigins`. |
| `VITE_ENTRA_CLIENT_ID`, `VITE_ENTRA_AUTHORITY`, `VITE_ENTRA_API_SCOPE` | The Entra app registration used when the API reports identity mode Entra (MSAL, authorisation code flow with PKCE). |

The application reads `GET /api/capabilities` before sign-in and signs in the way the API authenticates. MSAL is loaded only in Entra mode.

## The API contract

`openapi/mentor-api.json` is the frontend's copy of CONTRACT-MENTORSHIP-API. Every `dev`, `build` and `test` run generates `src/api/schema.gen.ts` (the typed client's types) and `src/api/enums.gen.ts` (the values of every enumeration) from it. Neither generated file is committed. A request or response the contract does not define does not compile, and every value list a screen offers comes from the contract.

When the API's contract changes, refresh the copy from an API run with `ASPNETCORE_ENVIRONMENT=Development` and `Identity:Mode=Simulated`:

```sh
MENTOR_API_URL=http://localhost:5080 npm run openapi:fetch   # replace the copy
MENTOR_API_URL=http://localhost:5080 npm run openapi:check   # fail if the copy and the API differ
```

## How screens talk to the API

- `src/api/client.ts` is the generated client. Every request carries the identity headers: an Entra bearer token, or `X-Simulated-Identity` under simulation. `ifMatch(version)` names the version a change to a candidate or a relationship was decided on (ADR-005), and `expect()` turns a refusal into an `ApiProblem` that carries the server's title, detail and field errors.
- `src/session/SessionProvider.tsx` reads `GET /api/session`. Navigation and actions are offered by the permissions it reports. Participant-only actions are offered to the participant the record names. The server checks both again.
- `useCommand()` runs a command and shows its outcome. When the server answers 409 the record is read again, so the person decides on its current state.

## Layout

```
src/app/        shell: sign-in, simulated-identity banner, role-based navigation, routes
src/identity/   capabilities, Entra (MSAL) and simulated sign-in
src/session/    the caller as the server reports them
src/screens/    profile, mentors, requests, pairings, agreement draft, formation, mentorships, notifications, administration
src/ui/         shared hooks, components and labels
../../tests/web the tests (Vitest, Testing Library) on a fake of the API
```

Screens are ordinary source. They may be generated or refined with Vercel v0 and committed (ADR-012).
