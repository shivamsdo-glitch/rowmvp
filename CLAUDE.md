# ROW MVP — Right-of-Way Process Management

MVP for US State DOTs and City/County agencies. Greenfield. Domain research lives in `vault/` (10-stage ROW lifecycle, 323 sourced requirements ROW-001..ROW-323). **Read `vault/Feature_Index_Final.md` and the relevant `vault/diagrams/DiagramNN_Final.md` before building any feature.** Phase plans are in `vault/phases/`.

## Domain in one paragraph
A transportation project needs parcels of land. Each parcel has owners/interests, a title and survey package, an appraisal that establishes **just compensation**, then a negotiation (offer → counter → agreement) and closing/recording. Federal-aid projects follow the Uniform Act (URA); approvals are **configurable chains by dollar threshold** because agencies differ. Evidence codes in vault: V = source-stated, A = agency SOP, O = original list, D = design decision, U = user decision.

## MVP scope (v1): straight-through acquisition only
Project & parcel setup → title & survey → appraisal & just compensation → negotiation → signed agreement → closing.
Requirements: ROW-001–021, 023–048, 063–081, 107–110, 233–268 (see per-phase mapping in `vault/phases/`).

**Deferred to v2 — do NOT build:** condemnation (Diagram 6), relocation (5), utility/railroad (9), property mgmt/disposal (8), clearance/certification (10), platform admin/SSO/security hardening (11), document/GIS/reporting platform services (12–13). Where an in-range requirement lives in a deferred diagram (e.g. ROW-033, 038, 040, 041, 069), skip it and note it in the phase doc. Hand-offs to deferred modules (e.g. impasse → condemnation) are recorded as a status/flag only.

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
- **Roles** for `[Authorize(Roles=...)]`: `ProjectManager`, `TitleExaminer`, `Appraiser`, `Negotiator`, `Finance`, `Admin` (from Diagram00 swimlanes).
- **Pure calculations** (offer/compensation thresholds, approval-chain gating, statutory deadlines, settlement variance) go in `Application/Calculations/` as stateless static functions with dedicated unit tests. No DB, clock, or DI inside them (pass `now` in).
- **Approval chains** are configurable data (threshold bands → required roles), evaluated by a Calculations function; never hard-coded dollar amounts.
- **Parcel status and agreement/interest status** are manually-set lists at both levels (user decision, README); ordered step-gating (ROW-072) is a separate, configurable check.
- **Documents:** MVP stores attachments via an `IFileStorage` abstraction (local disk) with metadata + versioning; no full document platform.
- Locked-in design decisions → ADR in `vault/decisions/NNNN-title.md`.
- Every feature references its ROW-IDs in commit messages and test names so traceability to the vault holds.

## Working agreements
- Work one phase at a time, in order; each phase doc has an exit checklist. Don't start a phase before the user confirms the prior one.
- Backend: `dotnet test` green + `dotnet build` warning-free before a phase is done. Frontend: `npm run build`, `vitest`, and type-check green.
- Open domain questions from vault/README_Final.md ("Yours to verify") are not ours to resolve; flag, don't guess.
