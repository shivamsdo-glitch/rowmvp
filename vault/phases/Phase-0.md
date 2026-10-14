# Phase 0 — Foundation

**Goal:** a running, authenticated, multi-tenant skeleton: API + DB + SPA that can log in with a dev token and call one protected endpoint. No domain features.

## Scope
- Repo: `docker-compose.yml` (Postgres 16 only), `.gitignore`, `SKILLS.md`, solution + 4 src + 2 test projects with correct references (Domain ← Application ← Infrastructure/Api).
- Domain base: `Entity`, `AggregateRoot` (has `TenantId`, audit fields, `RowVersion`), `ITenantContext`.
- Infrastructure: `AppDbContext` with global tenant query filter + SaveChanges stamping (tenant, created/modified by/at); first migration; `IFileStorage` (local disk) interface + impl stub.
- Auth (self-contained): `AppUser`, `Role`, `UserRole`; seeded roles `ProjectManager, TitleExaminer, Appraiser, Negotiator, Finance, Admin`; JWT issue/validate with `tenant_id` + role claims; `POST /auth/dev-token` (Development env only); `GET /auth/me`.
- Application: MediatR + `ValidationBehavior` (FluentValidation), `Result`/problem-details mapping, `Mappers/` and `Calculations/` folders with a README note on the rules.
- Generic **approval-chain** primitives (`ApprovalChainConfig`, `ApprovalRequest`, `ApprovalStep`) + `ApprovalChain.Evaluate(amount, config)` calculation with tests — reused by Phases 3 and 4 (ROW-046, 258, 261, 108).
- Api: Swagger with JWT bearer, CORS for Vite, health endpoint, ProblemDetails, Serilog console.
- Frontend: Vite + React 19 + TS + Tailwind + shadcn/ui init; TanStack Router file-based routes; TanStack Query provider; typed API client in `src/api` (generated from OpenAPI or hand-rolled fetch wrapper — decide in ADR); auth context + dev role-switcher login; app shell (sidebar, header), route guards by role.
- Tests: unit (ApprovalChain, tenant stamping), integration (Testcontainers or compose Postgres: tenant filter isolates data; dev-token → protected endpoint 200/401/403), FE (route guard, API client).
- ADRs written: 0001 tenancy & audit; 0002 self-contained auth; 0003 API conventions & stubs; 0004 RBAC/seeding/documents/saved filters; 0005 FE API client & auth (numbering differs from the original plan).

**Status: complete.** Deviations: integration tests use WebApplicationFactory + EF InMemory (no Testcontainers); the approval-chain primitives are pure calculations (`ApprovalChain`), with persisted `ApprovalChainConfig`/`ApprovalRequest`/`ApprovalStep` entities deferred to Phase 3 where they are first used.

## Requirements touched
None directly (enables ROW-019 team/role assignment, ROW-021 permissions later).

## Exit checklist
- [x] `docker compose up -d` → `dotnet run` migrates DB and serves Swagger (verified on Postgres 16: migrations, seed, health, auth, documents, saved filters)
- [x] Dev token for each of 6 roles; wrong role gets 403
- [x] Two-tenant integration test proves isolation
- [x] `npm run dev` logs in and renders shell; `vitest` + build green (build and 82 tests verified; shell exercised through router tests)
