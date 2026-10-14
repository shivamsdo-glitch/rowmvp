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

## Before calling work done
- Backend: `dotnet build` with 0 warnings, `dotnet test`.
- Frontend: `npm run build` (type-check + bundle), `npx vitest run`.
- Real database check when persistence changed: `docker compose up -d`, run the API in Development, hit the endpoint.
- Locked decision? Write an ADR. Open domain question? Flag it, don't guess.

## Environment gotchas
- A permission rule blocks creating files named `Token*` / `Password*`; pick another name.
- Frontend installs need the Aurigo private feed login; set `NODE_OPTIONS=--use-system-ca` on this machine's corporate proxy.
- Starting the API with `--no-launch-profile` needs `ASPNETCORE_ENVIRONMENT=Development`, otherwise the JWT key check refuses to start (by design).
