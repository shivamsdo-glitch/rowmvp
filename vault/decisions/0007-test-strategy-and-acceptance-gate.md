# ADR 0007 — Test strategy and the acceptance gate

**Status:** accepted.

## Context
The product will be shown to prospective customers. A defect found there is far more expensive than one found in a test, and some defects (wrong access, lost data, a 500 on odd input) are only visible against the real database and a real browser. EF InMemory and component tests alone had already let several through (see "What the first run found").

## Decision
Five layers, each guarding a different failure, run together by one command, `node scripts/verify.mjs` (the **release gate**):

1. **Backend unit** — pure rules and handlers over InMemory.
2. **Backend integration on real Postgres** — the real API and the real migrations on a throwaway database per test class. `ROW_REQUIRE_PG=1` makes an unreachable database a *failure*, so the gate cannot pass by skipping. Covers schema, migrations-vs-model drift, security, hostile input, concurrency, restarts, database outages, tenant isolation, audit, and every role against the access spec.
3. **Frontend** — per-role screens, session and failure behaviour, automated accessibility (axe).
4. **End-to-end acceptance** (Playwright, installed Edge) — the real browser, real UI, real API, fresh Postgres, one run per role.
5. **Written acceptance cases** (`vault/acceptance/`) — the jobs each user does, with ids that the tests carry, plus pending cases for every later phase and a manual pre-demo checklist.

### One source of truth for access
`IntegrationTests/Support/Spec.cs` is a hand-written role × page table, independent of the production defaults. The backend tests assert the API against it and publish it as `vault/acceptance/permission-matrix.json`; the frontend and E2E tests read that file. Changing a permission therefore means changing the spec on purpose, in one visible place, and the UI/API/docs follow or fail.

### Rules
- Retries are off; a flaky test is a defect.
- Every feature adds cases for every role it touches, including the roles that must be refused.
- New entities must derive `EntityBase`, be on `IRowDbContext`, have a migration, and a tenant filter, or `EntityConventionTests` / `SchemaAndContractTests` fail.
- A phase is done only when the gate is green and its acceptance cases (all roles) are automated.

## What the first run found (all fixed, each with a regression test)
- A validly signed token with no/invalid tenant or user was accepted (200). Now 401.
- U+0000 in any text, filter JSON or file metadata was a 500 on Postgres. Now 400.
- A unique-constraint race (two identical saves at once) was a 500. Now 409.
- Npgsql's default pool (100) equalled Postgres' `max_connections` (100); under load requests failed with "too many clients". The pool is capped at 40 and queues.
- Listing saved filters for a page without Read access answered 200 while saving answered 403. Now both 403.
- A bare `.pdf` name stored as an empty name. Now `file.pdf`.
- Frontend: no request timeout or network-error handling (a dead server left the button "Signing in…" forever); a session that expired mid-use left a stale screen; the redirect after sign-in could overwrite itself with `/login`; the sidebar had several unlabelled navigation landmarks.
- Tooling: Docker Compose named the project after the checkout directory, so a second checkout or git worktree collided with the database container. The project name is now fixed.

## Consequences
- The gate takes about three minutes and needs Docker and Edge. `--skip-e2e` gives a faster check; it is not a release check.
- Real-database tests share one Postgres; each test class gets its own database and small connection pools so the suite does not exhaust the server.
- Known gaps, written down rather than hidden: no screens yet for documents/saved views (verified through the API with the browser's own session), phone-width layout, colour contrast (manual), and load beyond a few hundred concurrent requests.
