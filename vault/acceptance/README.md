# Acceptance cases — what each kind of user must be able to do

This is the contract for "it works". Every case is written as a **job a person does**, not a function that exists. A phase is **not done** until every case for every user that the phase touches is **green**, and the whole suite below still passes.

## The users

| Role in the app | Real-world job (Diagram 00 lane) | Cases |
|---|---|---|
| Project Manager | ROW Project Lead: creates projects and parcels, owns budget, team and milestones, approves | [ProjectManager.md](ProjectManager.md) |
| Designer | ROW Designer: adds and updates parcels and their details from the plans | [Designer.md](Designer.md) |
| Surveyor | Right-of-way limits, field data and easements | [Surveyor.md](Surveyor.md) |
| Title Researcher | Title research (was Title Examiner; Surveyor is now its own role) | [TitleExaminer.md](TitleExaminer.md) |
| Appraiser | Appraisal team and approving official: appraise, review, establish just compensation | [Appraiser.md](Appraiser.md) |
| Appraisal Approver | Approving official for appraisals: reviews and sets the approved just compensation | [AppraisalApprover.md](AppraisalApprover.md) |
| Relocation Agent | Relocation specialist: questionnaires and (later) relocation cases | [RelocationAgent.md](RelocationAgent.md) |
| Negotiator | ROW Agent: offers, negotiation, agreements, closing | [Negotiator.md](Negotiator.md) |
| Finance | Finance / Accounts Payable: payments and expenditure | [Finance.md](Finance.md) |
| Administrator | System Administrator: configures rules, roles, approvals, users | [Administrator.md](Administrator.md) |
| **Everyone** | what any signed-in person relies on: sign-in, navigation, access, session, failure | [Everyone.md](Everyone.md) |

## Who can do what (the access spec)

R = read · C = create · U = update · D = delete. This table is generated from `permission-matrix.json`, which the backend tests publish and the frontend tests read, so the screen, the API and this document cannot disagree.

| Page | Project Manager | Title Researcher | Appraiser | Negotiator | Finance | Administrator |
|---|---|---|---|---|---|---|
| Dashboard | R | R | R | R | R | CRUD |
| Projects | CRUD | R | R | R | R | CRUD |
| Parcels | CRUD | R | R | R | R | CRUD |
| Parties (owners) | CRUD | R | R | CRUD | R | CRUD |
| Title | R | CRUD | R | R | none | CRUD |
| Survey | R | CRUD | R | none | none | CRUD |
| Appraisals | R | R | CRUD | R | none | CRUD |
| Approvals | CRU | none | CRU | R | CRU | CRUD |
| Negotiations | R | none | none | CRUD | R | CRUD |
| Agreements | R | none | none | CRUD | R | CRUD |
| Closing | R | none | none | CRU | R | CRUD |
| Payments | R | none | none | CRU | CRUD | CRUD |
| Administration | none | none | none | none | none | CRUD |

Files follow the page that owns the record: reading or opening needs **R**, attaching needs **U**, removing needs **D**. An agency's administrator may change these per agency; the table is the out-of-the-box default.

## Case ids and status

`AT-<ROLE>-<nn>`: `AT-X` everyone, `AT-PM`, `AT-TE`, `AT-AP`, `AT-NG`, `AT-FN`, `AT-AD`. Numbers 01–19 are jobs possible **today**; 20+ are jobs that arrive with later phases.

| Status | Meaning |
|---|---|
| ✅ Automated | A test runs it on every execution. The "Run by" column names where. |
| 🕓 Pending, Phase N | Specified now so it cannot be forgotten. A skipped placeholder in `e2e/tests/future.spec.ts` keeps it visible. |
| 👁 Manual | Needs human eyes (look and feel). Done before every customer demo, see below. |

## How the cases are executed

Five layers, cheapest first. Each protects against a different kind of failure; none replaces another.

| Layer | What it proves | Command (from the repo root) |
|---|---|---|
| Backend unit | rules and calculations | `dotnet test backend/Aurigo.RowManagement/tests/Aurigo.RowManagement.UnitTests` |
| Backend integration (real Postgres) | the real API on the real database: migrations, security, hostile input, concurrency, restart, every role against the access spec | `ROW_REQUIRE_PG=1 dotnet test backend/Aurigo.RowManagement/tests/Aurigo.RowManagement.IntegrationTests` |
| Frontend | each screen per role, session and failure behaviour, accessibility | `cd frontend/row-management-web && npx vitest run` |
| End-to-end acceptance | a real browser, the real UI, the real API and a fresh database, one run per role | `cd e2e && npm test` |
| Everything, in order | the release gate | `node scripts/verify.mjs` |

Prerequisite for the real-database layers: `docker compose up -d`. With `ROW_REQUIRE_PG=1` an unreachable database **fails** the run instead of skipping tests, so the gate can never pass by not testing.

## Rules for adding to this suite

1. **Every new feature adds cases for every role it touches**, including the roles that must be *refused*. A permission that is not tested in both directions is not tested.
2. A case is a job in the user's words: who, what they do, what they see, what the server did. Not "endpoint returns 200".
3. When a phase delivers a pending case, replace its `test.fixme` with a real journey (UI **and** API), flip its status here, and run the full gate.
4. New entity or page? The tests in `SchemaAndContractTests`, `EntityConventionTests` and the permission matrix fail until it follows the platform rules (tenant, audit, migration, permissions). That is intended.
5. A flaky test is a defect. Retries are off on purpose.
6. A bug found anywhere gets a regression test at the lowest layer that can catch it **and** a mention in the changelog of the relevant case file.

## Before showing the product to a customer

Run `node scripts/verify.mjs` (everything above), then walk this by eye once, on the machine used for the demo, at full-screen 1920×1080 and at 1366×768:

- [ ] Fresh database: start the app from scratch; the sign-in page appears with the product name.
- [ ] Sign in as each of the six roles in turn: dashboard appears, sidebar looks right, no console errors (F12).
- [ ] Sign out and sign in as another role without reloading the page: nothing from the previous user is visible.
- [ ] Collapse and expand the sidebar; hover a "Soon" item (tooltip says coming soon).
- [ ] Open Configuration as Administrator and as someone else (access message reads clearly).
- [ ] Unplug the network (or stop the API) and try to sign in: the message is plain English.
- [ ] Nothing says "undefined", "null", a stack trace, or a raw error code anywhere.

## Known limits (not hidden)

- The UI does not yet have document or saved-view screens; those jobs are verified through the same API calls the future screens will use, with the browser's own session token. Their UI journeys are the pending 2x cases.
- Phone-sized screens are not a supported target for this release; tablet width (about 820 px) is checked.
- The notification bell is intentionally inactive until the notifications module exists.
- Colour-contrast cannot be measured in the automated accessibility check (no real rendering); it is on the manual list.
