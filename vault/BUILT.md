# BUILT — what exists in the app today

**Read this first in every new chat, then `CLAUDE.md` (rules) and `SKILLS.md` (recipes).** This file says *what has been built and where*; it does not repeat the rules. It is the map. **It must be updated in the same commit as every feature change** (add a row, a route, a page key, an ADR link, the acceptance IDs, bump "Last updated"). This is enforced: the release gate's first check, `scripts/check-built.mjs`, fails if `backend/*/src` or `frontend/*/src` changed and this file did not (`--skip-built-check` only for pure bug fixes and refactors).

Last updated: 2026-10-21, after the Program view settings (ADR 0025), the ROW Director's Program view (ADR 0024), the dashboard, Team roll-up and Help center (ADR 0022) and the one-form-per-process clean-up (ADR 0023). Commit history (`git log`) is the detailed record; ADRs in `vault/decisions/` hold the *why*; `vault/acceptance/` holds the per-role *proof*.

---

## 1. Status at a glance

| Phase | What | Status |
|---|---|---|
| 0 | Foundation: tenancy, audit, JWT auth, RBAC, seeding, documents, saved filters, app shell, approval-chain calculation | **Done** |
| 1 | Project & parcel setup (slices 1, 2, 3a, 3b, 3c) | **Done** |
| 1.5 | "Next requirements.docx": roles, login, settings, library, project details, parcel form and 12 parcel tabs, work screens | **Done** (slices 1–8 + work screens + tab style) |
| 2 | Title & Survey *package process* (Diagram 2) | **Not started** (only the Title/Easement *records* exist, see §5) |
| 3 | Appraisal & just compensation *process* (approval chains, step gating) | **Not started** (only the Appraisal *records* exist) |
| 4 | Negotiation → agreement *process* | **Not started** (only the Negotiation *record* exists) |
| 5 | Closing *process* | **Not started** (only the Title and Closing *record* exists) |

Important: the parcel tabs for Condemnation, Property Management & Disposition and Relocation are **records the agency fills in**, not the v2 processes. Workflow, statutory deadlines, hand-offs and benefit rules are still deferred (CLAUDE.md, ADR 0020).

Release gate: `node scripts/verify.mjs` (9 checks; the first fails if product code changed without this file changing). Every commit above was made only on a green gate.

---

## 2. Platform (Phase 0)

| Area | What | Where |
|---|---|---|
| Multi-tenancy + audit | `tenant_id` on every aggregate, EF global filter, audit columns stamped automatically | `EntityBase`, ADR 0001 |
| Auth | Own JWT, `AppUser`/`Role`/`UserRole`, dev-token endpoint, named demo sign-ins | ADR 0002, 0013 |
| API conventions | `/api/v1`, ProblemDetails with stable `title` codes, stubbed external systems | ADR 0003 |
| RBAC | Page-level `RolePagePermission` (create/read/update/delete flags per role and page key) | ADR 0004, 0014; `PageKeys`, `DefaultRolePermissions` |
| Documents | Generic attachments API (`/api/v1/documents`), soft delete, stub doc store; any record attaches via `AttachmentTargets` | ADR 0004 |
| Saved filters | Per user per page | ADR 0004 |
| Approval chain | Configurable threshold → required roles, pure calculation | `Application/Calculations/ApprovalChain` |
| Frontend shell | Top bar, collapsible sectioned sidebar, account menu, notifications bell; file-based TanStack Router, guards, fetch wrapper | ADR 0005 |
| Test tiers + gate | Unit, real-Postgres integration, frontend unit, Playwright E2E per role; spec table drives the permission matrix contract | ADR 0007 |

## 3. Phase 1 — Project & parcel setup

| Feature | What it does | ADR / ROW IDs |
|---|---|---|
| **Projects** | Create/list/edit projects, auto numbering that survives concurrent creates, status, oversight type, per-project time zone | ADR 0006 · ROW-001, 003, 004, 012, 013, 076, 238 |
| **Parcels** | Parcel core: statuses, groups, interests, milestones, site visits, notice to owner, split and combine (lineage kept), parcel board and detail | ADR 0008 · ROW-002, 005–011, 071–074, 233–237 |
| **Step gating + deadlines** | Configurable workflow steps with override roles; statutory deadlines counted in the agency/project time zone; reminders | ADR 0008 |
| **Parties** | One `Party` entered once, linked by role to tracts/parcels; W-9 tax ID write-only (encrypted, masked everywhere, redacted from audit); addresses and contacts on the party; a parcel's **Ownership interests** tab is this module (ADR 0023) | ADR 0009, 0023 · ROW-014–018 |
| **Library** | Admin-defined fields, types, allowed values and defaults; library-driven dropdowns across forms; default lists seeded add-only | ADR 0010, 0015 |
| **Parcel team, diary, notes** | Team = pick an agent type and a person from the **Library** Agents list (nothing typed); "Assigned to" on forms picks from the team; negotiation diary; free notes | ADR 0011, 0023 · ROW-019–021 |
| **Right of entry, requests, reviews** | Rights of entry (entered only in the parcel's **Negotiation** tab; reads on `parcels`, writes on `parcelnegotiation`) with a report page, acquisition requests with approval, plan-review gate, staff reports, acquisition plans | ADR 0012 |
| **Agency + project time zones** | Days are counted where the project is | ROW-073, 074 |

## 4. Phase 1.5 — "Next requirements" (all done)

| Slice | What | ADR |
|---|---|---|
| 1 | Username sign-in, eleven roles, named demo users, Masterworks branding | 0013 |
| 2 | **Settings**: users, roles, the role × page permission grid (administrators can add roles) | 0014 |
| 3 | **Library page** + **Agents list** (people assigned to parcels) and default lists | 0015 |
| 4 | **Project details** (ROW project form, Planned/Build link, add a parcel from a project) | 0016 |
| 5 | **Parcel form** (many added fields, read-and-update access level, deadlock-safe saves) and the Ownership, Parcel Location, Contact Log, Easements tabs | 0017 |
| 6 | Title Encumbrances, Title and Closing, Appraisal tabs | 0018 |
| 7 | Negotiation and Questionnaire tabs | 0019 |
| 8 | Condemnation, Property Management & Disposition, Relocation tabs | 0020 |
| — | **Work screens** across parcels (Title & Survey, Appraisal, Negotiation & Closing) | 0021 |
| — | **Dashboard** per person (what I did, what is waiting) + manager **Team** roll-up; in-app **Help center** behind the top-bar question mark | 0022 |
| — | **Program view settings** (Configuration page): stuck days, health thresholds and the step-to-team mapping, with the ADR 0024 values as defaults | 0025 |
| — | **ROW Director's Program view**: every project's health, % closed, pipeline and cost; drill-down to each parcel; time per step and per team; stuck parcels | 0024 |
| — | Tab strip restyle (active tab = blue label + full-width blue bottom border; wraps instead of scrolling) | `components/ui/tabs.tsx` |

---

## 5. The parcel tabs (inside a parcel)

Each tab has its own **page key** (so access is per tab), its own ADR section and acceptance IDs. All attach files through the shared documents API. IDs are shown on screen.

| Tab | Page key | Records | ID format | Who edits (default) | Notes |
|---|---|---|---|---|---|
| Ownership interests (Parties) | `parties` | many links | — | Designer/PM CRUD; Title Researcher RU; Negotiator CRU | Owners and interest holders with owner type, addresses and contacts in one form; the parcel's **Owner name** is derived from the owners (ADR 0023; the old `ownership` tab and key are gone) |
| Parcel location | `parcellocation` | 1 (read-only) | — | Designer CRUD; Surveyor CRU | CAD number + ESRI save status; CSV export; no map |
| Title encumbrances | `titleencumbrances` | many | `ENC-<parcel>-<seq>` | Title Researcher CRUD | Linked-records editor, cure status |
| Title and closing | `titleclosing` | 1 per parcel | `TC-<parcel>` | Title Researcher CRU, Negotiator CRU | Conveyance documents + title commitments grids; derived **vested owner name**; Admin alone removes |
| Appraisal | `parcelappraisal` | many | `APR-<parcel>-<seq>` | Appraiser CRUD; **Appraisal Approver RU** | Amounts grid (total computed); **review fields incl. approved just compensation writable only by Approver/Admin** (`AppraisalReviewAccess`) |
| Negotiation | `parcelnegotiation` | 1 per parcel | `NEG-<parcel>` | Negotiator CRUD | Status notes, assigned-to (from the team), **right of entry** (the one form for it), offers, administrative settlement, JAT counteroffer, PUA |
| Questionnaire | `parcelquestionnaire` | many | `QST-<parcel>-<seq>` | Relocation Agent CRUD | Ownership interests + questions/answers grids |
| Condemnation | `parcelcondemnation` | 1 per parcel | `CON-<parcel>` | Negotiator CRU (no delete) | Preliminary dates, support, attorneys, commissioners, notices of hearing |
| Property management & disposition | `parcelproperty` | 1 per parcel | `PMD-<parcel>` | Project Manager CRUD | Transfer memo, demo, disposition, funding distribution |
| Relocation | `parcelrelocation` | many | user-typed **Relocation Number**, unique per parcel | Relocation Agent CRUD | Contacts, eligibility, relocation contact log, milestones |
| Contact log | `contactlog` | many | `CL-<parcel>-<seq>` | Negotiator CRUD | Linked records, attachments |
| Easements | `easements` | many | user-typed number, unique per parcel | Surveyor CRUD | Station range, term |

Everyone else reads every tab (Basic). Admin does everything. The full matrix is `vault/acceptance/permission-matrix.json` (generated from `Support/Spec.cs`).

Older parcel tabs from Phase 1 (Summary, Team, Diary, Notes, Reviews & reports, Interests, Milestones & deadlines, Workflow, Site visits & notice) live in `features/parcels/` and are governed by the `parcels` page.

## 6. Screens (routes in `frontend/.../src/routes/_app/`)

| Route | Screen | Page key |
|---|---|---|
| `/` | **Dashboard**: *My work* for everyone (waiting, done, recent); managers (Project Manager, Admin) also get *Program* (their default: projects with health, drill-down to parcels, time by step and team) and *Team* | `dashboard` |
| **`/help`** | **Help center**: contents, search, topics, "What your role can do"; open to every signed-in person (`?topic=<id>`) | none (signed in) |
| `/projects`, `/projects/$projectId` | Project list and detail (tabs) | `projects` |
| `/parcels`, `/parcels/$parcelId` | Parcel board and detail (tabs in §5) | `parcels` |
| `/parties`, `/parties/$partyId` | Parties (owners, W-9) | `parties` |
| `/requests` | Acquisition requests and approvals | `approvals` |
| `/rights-of-entry` | Rights-of-entry report | `parcels` |
| **`/title`** (Title & Survey) | **Search across parcels** for title encumbrances / easements, open the parcel and add or update right there | `title` |
| **`/appraisals`** (Appraisal) | Same for appraisals | `appraisals` |
| **`/negotiations`** (Negotiation & Closing) | Same for negotiations and title-and-closing records | `negotiations` |
| `/administration` | Configuration (parcel statuses, groups, rules) | `administration` |
| `/settings` | Users, roles, permission grid | `administration` |
| `/library` | Library fields, values, agents | `library` |
| `/forbidden` | "No access" page | — |

**Work screens (ADR 0021):** one search box (parcel number, address, **owner name**, or record ID) → matching records table + matching parcels table → *Open* shows the parcel's own tab in a panel, so add/change/remove use the tab's real permissions. Backed by `GET /api/v1/overview/{appraisals|negotiations|title-closings|encumbrances|easements}?search=`. The Parcels page search also matches owner names.

---

## 7. Roles (11) and demo sign-ins

`ProjectManager`, `Designer`, `DesignApprover`, `Surveyor`, `TitleResearcher`, `Appraiser`, `AppraisalApprover`, `Negotiator`, `RelocationAgent`, `Finance`, `Admin`. Agencies can add roles. In Development the login page has one-click buttons for each role; named users (`rowprojectmanager`, `rowdesign`, `rowdesignapprover`, `surveyor`, `titleresearcher`, `appraiser`, `appraisalapprover`, `rownegotiator`, `relocationagent`, `rowfinance`, `admin`) use password `qwertyuiop`.

Special rules beyond the page grid: appraisal review fields (Approver/Admin only); only Admin removes a Title and Closing record; two-person approval rules (ADR 0012); tax IDs write-only.

## 8. Code map

| Concern | Backend (`backend/Aurigo.RowManagement/src/`) | Frontend (`frontend/row-management-web/src/`) |
|---|---|---|
| Parcel tab entities | `Domain/Entities/ParcelTitle.cs`, `ParcelTabs.cs`, `ParcelNegotiation.cs`, `ParcelQuestionnaire.cs`, `ParcelLifecycle.cs` | — |
| Parcel tab handlers | `Application/ParcelTabs/*Requests.cs`, `OverviewQueries.cs`, `StatusNotes.cs` | — |
| Endpoints | `Api/Controllers/ParcelTabsController.cs`, `OverviewController.cs`, `ParcelsController`… | `api/parcel-tabs.ts`, `api/overview.ts` |
| Tab components | — | `features/parcels/*Tab*.tsx`, `LifecycleTabs.tsx`, `lifecycle-kit.tsx`, `NegotiationTab.tsx` (exports the shared `Grid`) |
| Work screens | — | `features/work/ParcelWorkPage.tsx`, `WorkPages.tsx` |
| Program view | `Application/Dashboard/ProgramDashboard.cs`, `Application/Calculations/ProgramMetrics.cs` (`StageTimes`, `ProjectHealth`), endpoints `/api/v1/dashboard/program` and `/projects/{id}`; settings: `Application/Dashboard/ProgramSettingsRequests.cs`, `Domain/Entities/ProgramSettings.cs`, `/api/v1/dashboard/settings` | `features/dashboard/ProgramView.tsx`, `dashboard-kit.tsx`, `ProgramSettingsCard.tsx` (on `/administration`) |
| Dashboard | `Application/Dashboard/DashboardRequests.cs`, `Application/Calculations/ActivitySummary.cs` (audit log → "done"), `Api/Controllers/DashboardController.cs` (`/api/v1/dashboard/me`, `/team`) | `features/dashboard/DashboardPage.tsx`, `api/dashboard.ts`, `test/fake-dashboard.ts` |
| Help center | — | `features/help/help-content.ts` (all text, plain data), `HelpPage.tsx`, `routes/_app/help.tsx`; button in `components/shell/TopBar.tsx` |
| Access defaults | `Domain/Common/PageKeys.cs`, `DefaultRolePermissions.cs` | `api/auth.ts` (`PAGE_KEYS`), `components/shell/nav.ts` |
| Migrations | `Infrastructure/Persistence/Migrations/` (one per slice) | — |
| Ownership + team + right of entry (ADR 0023) | `Application/Parties/ParcelOwners.cs`, `PartyService.cs`, `Application/ParcelRecords/ParcelRecordRequests.cs` (team), `Application/RightsOfEntry/RightOfEntryRequests.cs`, `Domain/Entities/Party.cs` (`PartyAddress`, `PartyContact`) | `features/parties/PartyLinksPanel.tsx`, `PartyGrids.tsx`, `features/parcels/ParcelRecordTabs.tsx` (Team), `team-kit.ts`, `AcquisitionTabs.tsx` (right of entry, shown in `NegotiationTab.tsx`) |
| Test fakes | — | `test/fake-tabs.ts` (layered over `fake-records.ts`, `fake-parcels.ts`), `test/fake-api.ts` |
| Role spec | `tests/.../IntegrationTests/Support/Spec.cs` → `vault/acceptance/permission-matrix.json` | `test/contract.ts` |
| E2E | — | `e2e/tests/*.spec.ts` (one file per area; case IDs `AT-xx-nn` in test names) |

---

## 9. Working in parallel (several chats at once)

Conflicts happen in **shared registry files** that every new tab/page touches. If two chats add features at once, expect merge conflicts in:

- `Domain/Common/PageKeys.cs`, `DefaultRolePermissions.cs`, `Application/Documents/AttachmentTargets.cs`
- `tests/.../Support/Spec.cs` and `vault/acceptance/permission-matrix.json` (regenerate with `ROW_UPDATE_CONTRACT=1` after merging)
- `Infrastructure/Persistence/RowDbContext.cs`, `IRowDbContext.cs`, **EF migrations and `RowDbContextModelSnapshot.cs`** (two chats adding migrations clash; the second must rebase and regenerate its migration)
- `Api/Controllers/ParcelTabsController.cs`
- `frontend/.../api/auth.ts` (`PAGE_KEYS`), `features/parcels/ParcelDetailPage.tsx` (TABS lists), `routeTree.gen.ts` (generated; regenerate, don't hand-merge)
- `test/fake-tabs.ts`, `features/parcels/parcel-tabs.test.tsx`, `vault/acceptance/Everyone.md` and the per-role acceptance files

Practical rules:
1. **One chat per area**, and give each its own git worktree/branch; merge one at a time and rerun the gate after each merge.
2. **Run the gate alone.** `node scripts/verify.mjs` starts its own API/web servers and Postgres tests; don't run it while another chat's gate, `dotnet run` or `npm run dev` is using the same ports (5100, 5173, Postgres 5434) or while an API process is holding the build DLLs (that breaks `dotnet build`).
3. Docker Postgres must be up (`docker compose up -d`) before the gate.
   The E2E step uses API port 5101, web port 5174 and database `row_e2e`; if another project or chat holds them, set `E2E_API_PORT`, `E2E_WEB_PORT` and `E2E_DB` (for example `E2E_WEB_PORT=5184 node scripts/verify.mjs`).
4. A gate timeout/blank-page flake is not a pass: rerun, and fix the cause if it repeats (frontend tests have a 60 s per-test limit; the E2E `signInViaUi` helper retries a blank login page).
5. Re-read this file before you start and again before you commit: another chat may have changed it. When merging two chats' work, merge their edits to this file too.
6. Don't push unless asked. `origin` is the GitHub repository `shivamsdo-glitch/rowmvp`.

## 10. Not built / flagged (do not assume these exist)

- Phases 2–5 as *processes* (title & survey package workflow, appraisal approval chains and just-compensation establishment, negotiation→agreement flow, closing checklist/payments). Cross-parcel *records* exist; the workflow around them does not.
- Deferred v2 modules: condemnation/relocation/utility/property-disposal **processes**, clearance/certification, platform admin/SSO, document/GIS/reporting platform services.
- Not built from the requirements document because it gave no screenshot or detail: **Site Work** and **Form rZy9V** tabs; the **Land Planner** section of Appraisal; the **Work log** of Title Encumbrances; per-row file attachments on right-of-entry and settlement rows; Workflow / Select Actions / Save & Continue / Link Document buttons; map on Parcel Location.
- Open document questions answered by assumption (ADR 0019, 0020): Questionnaire "Sent to owner" is a date and the hidden ownership-interest column is "Phone"; Relocation Number is typed by the user; "Relocation complete" and "Relocation complete 1" are kept as two dates; "Survey" has no tab of its own (Title & Survey covers title encumbrances and easements).
- Relocation's own "Agents" grid still takes typed names (a different record from the parcel team); existing tenants must add agent types/people to the Library by hand (defaults seed only new tenants); tract-child parcels still record owners on the tract (ADR 0023).
- The git remote is `origin` (GitHub `rowmvp`); work is pushed to `master` when asked.
