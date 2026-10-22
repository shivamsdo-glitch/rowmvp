# SKILLS — repeatable recipes for this repo

Short, checked recipes for the jobs that come up in every phase. Read `CLAUDE.md` first; ADRs are in `vault/decisions/`.

## Add a domain entity
1. `Domain/Entities/X.cs` deriving `EntityBase` (tenant + audit columns come free). Add its table config in `Infrastructure/Persistence/Configurations/` and a `DbSet` on both `RowDbContext` and `IRowDbContext`.
2. `dotnet ef migrations add <Name> ...` (command in CLAUDE.md), read the generated SQL-shaped C# once.
3. `EntityConventionTests` fails if the entity forgot `EntityBase` or the tenant filter. Do not exempt entities to make it pass.

## Add a feature (command or query)
Folder per feature under `Application/<Feature>/`: request record, `FluentValidation` validator, handler, DTO. Map entity → DTO in `Application/Mappers/`. Controller is a thin `mediator.Send`. Validation only at this boundary. Reference the ROW-IDs in test names and the commit message.

## Add a calculation
Static class in `Application/Calculations/`, no DB/clock/DI (pass `now`). Dedicated tests in `UnitTests/Calculations/` covering both sides of every threshold. Handlers call it; they never hold the arithmetic. Example: `ApprovalChain`.

## Add a page (permissions)
Add the key to `Domain/Common/PageKeys`, set every role's template in `DefaultRolePermissions`, mirror it in `frontend/.../src/api/auth.ts` `PAGE_KEYS`. `TenantProvisioner` back-fills existing tenants on next start. Guard handlers with `IPagePermissionService`, guard the route with `requirePageAccess`.

## Attach files to a new module's entity
Register the entity type and its page key in `Application/Documents/AttachmentTargets`. The upload/list/download/delete API and permission mapping already exist; do not build a per-module upload.

## Add a seed step
Implement `ISeedStep` (unique `Order`, set `DevelopmentOnly` for anything demo), register it in `Infrastructure/DependencyInjection.cs`. Steps must be idempotent and add-only. Phase 1 fills `DemoDataSeedStep`.

## Add a frontend page
Create `src/routes/_app/<name>.tsx` with `createFileRoute('/_app/<name>')` and a `beforeLoad` using `requirePageAccess`. Add its entry to `NAV` in `src/routes/_app.tsx`. `npm run dev` (or `npm run build`) regenerates `src/routeTree.gen.ts`; commit it. Server calls go through `auth.api` / `src/api/*`, never raw `fetch`.

## Add acceptance cases for a feature
1. For every role the feature touches, write the job in `vault/acceptance/<Role>.md` (Pending until built), including the roles that must be *refused*.
2. API side: add the cases to `RoleAcceptanceApiTests` against the spec table in `Support/Spec.cs`; extend the spec if the feature adds a page or record type.
3. UI side: add the journey to `e2e/tests/jobs.spec.ts` (sign in through the real screen, do the job, check what the server stored), replace the matching `test.fixme` in `future.spec.ts`, and flip the status in the role file.
4. Run `node scripts/verify.mjs`. Fix bugs at the lowest layer that can catch them and add a regression test there.

## Fill the dev database with a demo portfolio
`node scripts/seed-demo-data.mjs` (API on :5100 in Development, Docker Postgres up) adds 8 projects and ~80 parcels at every workflow step, with appraisals, negotiations, closings, contact logs, requests and rights of entry, done by different demo users through the real API, then moves the timestamps back so parcels have ages, some are stuck and the dashboards have a history. `--force` adds it again, `--resume` finishes an interrupted run. Start the API with `RateLimiting__WritePermitLimit=100000 RateLimiting__AuthPermitLimit=100000` for a quick run. Development data only; the product does not read the script.

## Before calling work done
- **`node scripts/verify.mjs`** (the release gate: build, unit, real-Postgres integration, frontend, Playwright).
- Quick loops while developing: `dotnet test`, `npx vitest run`, `cd e2e && npx playwright test -g "AT-TE"`.
- Locked decision? Write an ADR. Open domain question? Flag it, don't guess.

## Environment gotchas
- A permission rule blocks creating files named `Token*` / `Password*`; pick another name.
- Frontend installs need the Aurigo private feed login; set `NODE_OPTIONS=--use-system-ca` on this machine's corporate proxy.
- Starting the API with `--no-launch-profile` needs `ASPNETCORE_ENVIRONMENT=Development`, otherwise the JWT key check refuses to start (by design).

## Keep BUILT.md current
Whenever a change adds or alters something a new chat would need to know about, update `vault/BUILT.md` in the same commit: add or amend the row (parcel tab table, screens table, Phase 1 table, "not built" list), the status table if a phase moved, the code map if a new area was added, and bump the "Last updated" line. The gate's first check (`scripts/check-built.mjs`) fails otherwise. Use `--skip-built-check` only for a pure bug fix or refactor and say so in the commit message.
