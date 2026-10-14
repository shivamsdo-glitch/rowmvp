# ADR 0006 — Project core: numbering, acquisition gate, and Phase 1 rule configurability

**Status:** accepted (Phase 1, slice 1). Decisions marked **U** were made by the user on 2026-10-14.

## Project number (ROW-001) — U
- Configurable per tenant (`ProjectNumberSettings`): a pattern of literal text plus `{FY}`, `{YY}` and exactly one `{SEQ:n}`, and a fiscal-year start month. Default `{FY}-{SEQ:4}`, calendar year.
- The system generates the number when none is supplied. A user may type one, edit it later, or supply it through bulk import (`POST /api/v1/projects/import`, max 500 rows, each row succeeds or fails alone). Numbers are unique per tenant, case-insensitive; generation skips numbers already taken.
- The sequence restarts each fiscal year when the pattern contains a year token, otherwise it runs once per tenant. It **continues** when the pattern changes within the same fiscal year, so a number is never reused (the settings screen shows the actual next number).
- Issuing is serialised per agency inside the server (a lock), so simultaneous creates queue and all succeed. The counter row also carries a Postgres `xmin` concurrency token and the create retries, which protects several servers sharing one database; only after repeated losses does a caller see 409 `project_number_conflict`. Real-Postgres tests create 20 projects at once and 10 identical typed numbers at once. The migration deliberately omits the `xmin` column (a Postgres system column the model merely maps).
- Changing the pattern affects only later projects. Existing numbers are never rewritten.
- Pure rules live in `Calculations/ProjectNumbering`.

## Acquisition gate (ROW-076, 238, 072)
- Offers and negotiations unlock when the funding source is not gated, or NEPA decision **and** a non-preliminary (acquisition or supplemental) ROW authorization are on file, or an early/hardship/protective exception has been approved with a date and reason (Diagram 1, GT_a–GT_e). Title, survey and preliminary appraisal are never held.
- Which oversight types are gated is an input to `AcquisitionGate.Evaluate`; the default is Federal and State, as drawn in Diagram 1.
- The gate is computed on read, never stored, so it cannot drift from the facts.
- **Open question (flag, do not guess):** Diagram 1 gates *state* oversight the same as federal. Whether state-funded projects need a federal-style NEPA decision is an agency/legal matter — keep on the "Yours to verify" list.

## Estimates, budgets, authorizations
- Cost estimates (ROW-012) are immutable versions (land, improvements, relocation, indirect; total stored). A change is a new version; the project shows the latest total as the current estimate. Quantity-based area-by-interest estimating is deferred until parcels carry interest areas (slice 2).
- Annual budget (ROW-013) is one row per project and fiscal year; adjustments overwrite and the audit log keeps the history.
- ROW authorization (ROW-238) stores the federal share as a percentage; the federal dollar amount is derived.

## Phase 1 rule configurability — U (applies to slices 2 and 3)
- **Statutory timelines (ROW-073):** deadline rules are tenant data (trigger event, number of days, what it applies to). `StatutoryDeadlines` takes the rules as input. Seeded rules are clearly labelled placeholders; no legal period is hard-coded. The actual periods remain on the "Yours to verify" list.
- **Step-gate override (ROW-072):** Admin and ProjectManager can always override, with a written reason that is audited. An Admin can grant override to additional roles per tenant.

## Consequences
- Region and district are free text in v1; a managed list can replace them later without changing the filter API.
- ROW-002 (two-level WBS), 241 (acquisition plan: parcel list and timeline) and 239 (plan-review gate) need parcels and move to slice 2.
