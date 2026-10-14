# ADR 0005 — Frontend API client, auth session and route guards

**Status:** accepted (Phase 0).

## API client: hand-rolled fetch wrapper
`src/api/client.ts` is a ~100-line typed wrapper (bearer token, JSON/multipart, ProblemDetails → `ApiError` with the API's stable `code`, 401 hook). We did not generate a client from OpenAPI: the surface is small and changing quickly through the phases, the DTO types are written next to the code that uses them, and generation adds a build step and tooling to maintain. Revisit when the API passes roughly 60 endpoints or a second consumer appears.

## Session: `AuthManager` outside React
The session (token, user, page permissions) lives in a plain class so TanStack Router guards can use it in `beforeLoad`; React reads it with `useSyncExternalStore` (`useAuth`). The token is kept in `sessionStorage` (cleared when the tab closes; every access is try/catch-guarded). A 401 on any authenticated request signs the user out.

## Guards
- `requireSignedIn` (the `_app` layout) redirects to `/login?redirect=<path>`; the login page accepts only same-site paths as redirect targets.
- `requirePageAccess(page)` sends users without Read on the page to `/forbidden` inside the shell, never a blank page.
- Guards and the sidebar are **convenience, not security**: the API enforces every permission. They only decide what to show.

## Dev login
The login screen offers one button per role and calls `GET /api/v1/auth/dev-token` (Development only; 404 elsewhere, which the UI explains). Real email/password login screens come with the admin/user phases.

## Routing
TanStack Router file-based; the generator runs in `vite`/`vite build` and `src/routeTree.gen.ts` is committed so type-checking and tests do not depend on it. Vite proxies `/api` to the API in dev, so no CORS is needed locally.

## Consequences
- A page is added with a route file, a `NAV` entry and a page key; see `SKILLS.md`.
- The Aurigo component bundle makes the production chunk large (~2 MB); code-splitting is a later optimisation.
