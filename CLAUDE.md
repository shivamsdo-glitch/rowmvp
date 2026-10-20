# ROW MVP — Right-of-Way Process Management

MVP for US State DOTs and City/County agencies. Greenfield. Domain research lives in `vault/` (10-stage ROW lifecycle, 323 sourced requirements ROW-001..ROW-323). **Read `vault/Feature_Index_Final.md` and the relevant `vault/diagrams/DiagramNN_Final.md` before building any feature.** Phase plans are in `vault/phases/`.

## Domain in one paragraph
A transportation project needs parcels of land. Each parcel has owners/interests, a title and survey package, an appraisal that establishes **just compensation**, then a negotiation (offer → counter → agreement) and closing/recording. Federal-aid projects follow the Uniform Act (URA); approvals are **configurable chains by dollar threshold** because agencies differ. Evidence codes in vault: V = source-stated, A = agency SOP, O = original list, D = design decision, U = user decision.

Also in the vault: `vault/features/` (feature workbook dumps — priority, release R01–R35, build sequence, `Depends on`, verbatim source traceability; see its README) and `vault/previews/` (rendered diagram PNGs). Check `Depends on` in `vault/features/ROW_Features.csv` before building a feature.

## MVP scope (v1): straight-through acquisition only
Project & parcel setup → title & survey → appraisal & just compensation → negotiation → signed agreement → closing.
Requirements: ROW-001–021, 023–048, 063–081, 107–110, 233–268 (see per-phase mapping in `vault/phases/`).

**Deferred to v2 — do NOT build:** condemnation (Diagram 6), relocation (5), utility/railroad (9), property mgmt/disposal (8), clearance/certification (10), platform admin/SSO/security hardening (11), document/GIS/reporting platform services (12–13). Where an in-range requirement lives in a deferred diagram (e.g. ROW-033, 038, 040, 041, 069), skip it and note it in the phase doc. Hand-offs to deferred modules (e.g. impasse → condemnation) are recorded as a status/flag only.
**Parcel-record exception (Phase 1.5, ADR 0020):** the Condemnation, Property Management & Disposition and Relocation *tabs* on a parcel exist, as records the agency fills in (dates, people, grids, notes). The processes behind them (workflow, statutory deadlines, hand-offs, relocation benefit rules) are still v2.

## Locked tech stack — do not deviate
| Layer | Choice |
|---|---|
| Backend | .NET 8 Web API, Clean Architecture (Api / Application / Domain / Infrastructure) |
| ORM / DB | EF Core 8 + Npgsql, PostgreSQL 16 (no PostGIS; geometry as plain text/GeoJSON column if at all) |
| Mediator | MediatR + FluentValidation pipeline behavior |
| Auth | Self-contained JWT: own `AppUser`/`Role`/`UserRole` entities + dev-token endpoint. No external Aurigo service |
| Frontend | React 19 + Vite + TypeScript + Tailwind + shadcn/ui |
| Routing / server state | TanStack Router (file-based) / TanStack Query |
| Tests | BE: xUnit + FluentAssertions. FE: Vitest + React Testing Library |

## Layout
```
row-mvp/                       (this repo root)
├── CLAUDE.md, SKILLS.md, docker-compose.yml (Postgres only)
├── vault/                     domain research, phases/, decisions/ (ADRs)
├── backend/Aurigo.RowManagement/
│   ├── Aurigo.RowManagement.sln
│   ├── src/{Api,Application,Domain,Infrastructure}   (prefix Aurigo.RowManagement.)
│   └── tests/{UnitTests,IntegrationTests}
└── frontend/row-management-web/src/{api,routes,features,components/ui,lib}
```
Application is organised by feature (Commands/Queries/Validators/Mappers per feature) plus `Mappers/` and `Calculations/`. Domain has no dependencies. Infrastructure holds EF, JWT, file storage.

## Conventions
- **DTOs are never entities.** Map in `Application/Mappers/`; controllers never return entities.
- **FluentValidation at the API boundary only** (MediatR pipeline behavior). No duplicate validation in Domain; Domain enforces invariants/state transitions only.
- **Multi-tenancy:** `tenant_id` on every aggregate root, enforced by an EF global query filter and set on save from `ITenantContext` (from JWT claim). MVP demo runs one tenant.
- **Roles** (Phase 1.5, ADR 0013): `ProjectManager`, `Designer`, `DesignApprover`, `Surveyor`, `TitleResearcher`, `Appraiser`, `AppraisalApprover`, `Negotiator`, `RelocationAgent`, `Finance`, `Admin`. Agencies can add roles; access is the role x page grid (`RolePagePermission`).
- **Pure calculations** (offer/compensation thresholds, approval-chain gating, statutory deadlines, settlement variance) go in `Application/Calculations/` as stateless static functions with dedicated unit tests. No DB, clock, or DI inside them (pass `now` in).
- **Approval chains** are configurable data (threshold bands → required roles), evaluated by a Calculations function; never hard-coded dollar amounts.
- **Parcel status and agreement/interest status** are manually-set lists at both levels (user decision, README); ordered step-gating (ROW-072) is a separate, configurable check.
- **Documents:** attachments are metadata rows + bytes behind a doc-store interface (stub today); `IFileStorage` (local disk, `Storage:RootPath`) is the byte-storage abstraction for a real store. No full document platform.
- Locked-in design decisions → ADR in `vault/decisions/NNNN-title.md`.
- Every feature references its ROW-IDs in commit messages and test names so traceability to the vault holds.

## Backend platform (built in Phase 0; see ADRs 0001–0007 in `vault/decisions/`)
- **Run:** `docker compose up -d` (Postgres 16 on host port 5434), then `dotnet run --project backend/Aurigo.RowManagement/src/Aurigo.RowManagement.Api` → http://localhost:5100/swagger. In Development it migrates and seeds the demo tenant, the eleven default roles and eleven named sign-ins (`rowprojectmanager`, `rowdesign`, `rowdesignapprover`, `surveyor`, `titleresearcher`, `appraiser`, `appraisalapprover`, `rownegotiator`, `relocationagent`, `rowfinance`, `admin`; shared demo password `qwertyuiop`, Development only).
- **Test:** `dotnet test backend/Aurigo.RowManagement` (unit tests use EF InMemory; integration tests host the real API with `WebApplicationFactory`; neither needs Docker).
- **Migrations:** `dotnet tool restore`, then `dotnet ef migrations add <Name> --project src/Aurigo.RowManagement.Infrastructure --startup-project src/Aurigo.RowManagement.Api --output-dir Persistence/Migrations` from `backend/Aurigo.RowManagement`.
- **Tenancy/audit are automatic.** New entities derive `EntityBase`; never write tenant or audit code in handlers. Only anonymous auth flows use `IgnoreQueryFilters()`.
- **Auth:** own JWTs (claims `sub`, `email`, `tenant_id`, `role`). `GET /api/v1/auth/dev-token?role=Admin` is Development-only (404 elsewhere); without `user=` it signs in one stable user per role (different roles are different people, which the two-person approval rules in ADR 0012 rely on). Set `Jwt__SigningKey` in any other environment or the app refuses to start.
- **Routes** are `/api/v1/...`; errors are ProblemDetails with a stable `title` code.
- **External systems are stubbed** (`Application/ExternalServices` interface + `Infrastructure/ExternalClients/Stub*`). Never add a real HTTP client to an outside system without a decision from the user.
- **Page-level RBAC:** `RolePagePermission` rows per (role, page key); defaults in `Domain/Common/DefaultRolePermissions`, provisioned per tenant by `TenantProvisioner` (add-only, never overwrites admin edits). Check access with `IPagePermissionService`; the UI reads `GET /api/v1/auth/permissions`. New page = add to `PageKeys` + the matrix.
- **Seeding:** `SeedRunner` runs ordered `ISeedStep`s after migrations (`Database:SeedOnStartup`). Demo tenant/users/data are Development-only; default roles run everywhere. Phase 1 fills `DemoDataSeedStep`.
- **Parties (ADR 0009):** `Party` (entered once) + `PartyLink` (role on one tract or parcel). Owners are recorded on the parent tract and inherited by its parcels; split/combine carry a parcel's own links. Tax IDs are write-only: AES-GCM encrypted (`ITaxIdProtector`), masked in every DTO, redacted from audit. **`Security__TaxIdKey`** (base64 of 32 bytes) is required outside Development or the app will not start.
- **Library (ADR 0010, 0015):** its own `library` page key (everyone reads, administrators change); the Agents list (`LibraryAgent`) holds the people assigned to parcels; a library-driven field is a List (dropdown).
- **Library, original note (ADR 0010):** `LibraryField`/`LibraryValue` per tenant, seeded add-only by `TenantProvisioner.EnsureLibraryAsync` from `Domain/Common/DefaultLibrary`; the `parcel_status` field *is* the parcel status list (writes delegate to `SaveParcelStatusCommand`). Controlled by the Administration page.
- **Documents:** any module attaches files via `/api/v1/documents` (multipart; register the entity type in `Application/Documents/AttachmentTargets` with its page key). Soft delete; bytes go to the stub doc store. **Saved filters:** `/api/v1/saved-filters`, per user per page.
- **File names:** an environment permission rule blocks creating files whose names start with `Token` or `Password`. Use a different name (e.g. `OneTimeTokenService`) rather than fighting it.

## Frontend (ADR 0005)
`frontend/row-management-web`: file-based TanStack Router (`src/routes`, generated `routeTree.gen.ts` is committed), `AuthManager` session + guards in `src/auth`, fetch wrapper in `src/api`. `npm run dev` proxies `/api` to http://localhost:5100. Recipes for common jobs are in `SKILLS.md`.

## Testing and acceptance (ADR 0007)
- **Release gate: `node scripts/verify.mjs`** (needs Docker running). Builds with zero warnings, then unit, real-Postgres integration (`ROW_REQUIRE_PG=1`), frontend type-check/tests/build, and the Playwright end-to-end run in Edge. `--skip-e2e` is for quick checks only.
- **Acceptance cases per user live in `vault/acceptance/`** (README, Everyone, one file per role). Case ids (`AT-TE-10`...) appear in the E2E test names. Pending jobs for later phases are `test.fixme` placeholders in `e2e/tests/future.spec.ts`.
- **Access spec:** `backend/.../IntegrationTests/Support/Spec.cs` is the hand-written role x page table; it publishes `vault/acceptance/permission-matrix.json` (regenerate with `ROW_UPDATE_CONTRACT=1`) which the frontend and E2E tests read.
- Real-Postgres tests skip when Docker is down unless `ROW_REQUIRE_PG=1`; never treat a skipped run as a pass.

## Working agreements
- Work one phase at a time, in order; each phase doc has an exit checklist. Don't start a phase before the user confirms the prior one.
- A phase is done only when `node scripts/verify.mjs` is green **and** its acceptance cases are automated for every role it touches (including the roles that must be refused). Add the cases as you build, not after.
- Backend: `dotnet test` green + `dotnet build` warning-free. Frontend: `npm run build`, `vitest`, and type-check green. (The gate runs all of it.)
- Open domain questions from vault/README_Final.md ("Yours to verify") are not ours to resolve; flag, don't guess.
