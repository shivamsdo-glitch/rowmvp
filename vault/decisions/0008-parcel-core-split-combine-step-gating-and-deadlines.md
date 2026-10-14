# ADR 0008 — Parcel core: structure, split/combine, step gating, deadlines and custom fields

**Status:** accepted (Phase 1, slice 2). Decisions marked **U** were made by the user on 2026-10-14 (see ADR 0006).

## Scope of slice 2
In: ROW-002 (two-level structure), 005, 006, 007, 008, 009, 010, 011, 071, 072, 073, 074, 233, 234, 235 (status-coloured board), 236, 237.
Moved to slice 3 (not parcel-core, or needs people): ROW-014..021 (owners, contacts, diary, notes, team), 077 right of entry, 080 request intake, 239 plan-review gate, 240 staff report / certificate, 241 acquisition plan.

## Structure
- **Project → group → parcel** is the fixed two-level hierarchy (ROW-002). A group (`ParcelGroup`) is a corridor or segment; deleting needs it to be empty (including split or combined parcels that still point at it).
- **Parent tract** (`ParentTract`, ROW-233) carries the tax parcel numbers (Postgres `text[]`). Parcels are the takes from a tract.
- **Parcel numbers** are unique per project, case-insensitive, and **never reused**, even after a split or combine supersedes the parcel. A blank number takes the highest all-digit number plus one (`001`, `002` ...); creation is serialised per project in the server and guarded by the unique index.
- **Areas** are square feet with acres derived; roll-ups (`ParcelRollup`) count only live parcels.

## Status list is data (ROW-005, README decision)
Parcel statuses are a per-agency, administrator-managed list with a colour and order; exactly one active status is "initial". The board is coloured from it (ROW-235). The default list (Identified, Title & survey, Appraisal, Negotiation, Agreement signed, Closed, Impasse) is provisioned add-only per agency; "Impasse" is the hand-off flag to the deferred condemnation module. Status is separate from the ordered workflow step.

## Split and combine (ROW-007)
- Splitting supersedes the original (`State = Split`, kept on record with its documents and history) and creates numbered parts (`012-A`, `012-B`; letter `R` is skipped) plus a remnant (`012-R`) holding any unused area. The parts must not exceed the parcel's area.
- Combining supersedes the sources (`State = Combined`), sums their areas, merges interests by type, keeps a shared status/group/tract (otherwise the initial status and no group), and starts at the **least advanced** source's step. Parcels of different projects or acquisition types cannot be combined.
- Interests and milestones are not copied onto split parts (each part is a new take and is re-entered); combine merges interests.
- Both operations are one transaction. Parcels carry a Postgres `xmin` optimistic-concurrency token, so two people combining overlapping parcels, or splitting and editing one at once, cannot both succeed (found by a real-Postgres race test; the migration deliberately omits the `xmin` column, as in ADR 0006).

## Ordered step gating with override (ROW-072) — U
- Each acquisition type (Standard, Donation, Developer conveyance, Early — ROW-071) has an ordered workflow template per agency. These step lists are **placeholders** until Phase 4 builds those paths.
- A parcel moves to the next step freely. Skipping ahead, going back or repeating is an **override**: it needs a role that may override and a written reason, and is recorded (who, when, from, to, reason) in `parcel_step_events` and the audit log.
- **Who may override:** Admin and ProjectManager always can; an administrator can add roles per agency (`StepGateSettings`). Because the override only matters to someone who may update parcels, the extra roles take effect for any role the agency also grants update on Parcels.
- **The acquisition gate is not overridable.** Steps that start offers and negotiations (flagged `RequiresAcquisitionGate`) are held while the project's NEPA / authorization gate is closed (ADR 0006); an override cannot bypass it. The route past it is the project's approved early/hardship/protective exception, which is recorded with a date and reason. Going backwards is never held.
- The acquisition type can change only while the parcel is on its first step.

## Statutory deadlines and reminders (ROW-073, 074) — U
- Deadline rules are **agency data**: "target milestone is due N days after the trigger milestone is completed". `StatutoryDeadlines.Compute` takes the rules as input; nothing in code knows a legal period.
- Seeded rules are **placeholders** and are flagged as such until an administrator saves them: *Offer within 90 days of the Notice to Owner* and *Close within 24 months of parcel set up (agency parcel clock)* (730 days). The real periods remain on the "Yours to verify" list (vault/README_Final.md).
- A deadline is Not started → Pending → Due soon (today or within the window, default 7 days) → Overdue (delay flag) or Met / Met late. Due today is not yet late.
- Reminders (`GET /api/v1/reminders`) are the overdue and due-soon deadlines across live parcels, computed on request. **Nothing is emailed**; outbound notification stays a stub.
- Standard milestones (Parcel set up, Notice to owner, Offer delivered, Agreement signed, Closed) exist on every parcel and cannot be removed; custom milestones can be added and removed. Recording the Notice to Owner (date and method, ROW-237) completes its milestone.

## Custom fields (ROW-011)
Per-agency definitions (key, label, type Text/Number/Date/Boolean/List, options, required, active); values are stored on the parcel as a JSON object (`jsonb`). A field's key and type never change after creation; deactivating a field keeps existing values but stops new ones. Validation is a pure function (`CustomFieldValidation`).

## Platform changes made for this slice
- The null-character guard (`ValidationBehavior`) now inspects the whole request — nested objects, lists, dictionary keys and values — not only top-level strings, because split parts, custom values and tax-parcel lists are nested.
- Handlers add new child rows through their `DbSet`, never only through a parent's navigation list (a child with a preassigned id reached via a loaded parent is treated as already existing).
- Enum columns hold the enum's name, so the database orders them alphabetically; handlers that need declaration order sort in memory.

## Open questions (flag, do not guess)
- The statutory periods above, and which timelines apply to which acquisition type (vault "Yours to verify").
- Whether non-standard acquisition paths need different step lists than the placeholders (Phase 4).
