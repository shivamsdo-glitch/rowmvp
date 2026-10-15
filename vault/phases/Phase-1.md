# Phase 1 — Project & Parcel Setup

**Source diagram:** Diagram 1. **Primary roles:** ProjectManager, Admin.

## Scope
**Project:** create with unique ID (001), region/district (003), funding & estimate capture (004), versioned cost estimates (012), annual ROW budget (013), ROW acquisition plan & schedule (241), ROW Authorization preliminary/acquisition/supplemental with authorized amount and federal share (238), NEPA decision + acquisition gate by funding source (076), plan-review gate per parcel (239).
- WBS (002): MVP = fixed two-level hierarchy (project → parcel group/corridor); configurable depth deferred.

**Parcel:** parcel record & case tracking (005), grouping under project/corridor with roll-up counts/areas (006), combine/split + remnant creation (007), batch status/field updates (008), synopsis view + print-friendly page (009), milestones with dates (010), configurable attributes (011; MVP = per-tenant custom-field definitions stored as JSONB), parent tract with tax-parcel link (233), acquisition interests per parcel with areas by type (234), site visit record (236), color/corridor map (235; MVP = status-colored parcel list/board, no GIS), Notice to Owner issuance/tracking (237), staff report & certificate of necessity (240).

**People:** owners & ownership interests, multiple per parcel (014), contacts — attorneys/banks/designees (015), tenants/occupants (016), abutters (017), W-9/1099 tax ID capture (018; mask TIN at rest/in DTOs), team/role assignment per parcel (019), communication diary (020), notes with permission-controlled create/delete (021).

**Process rules (Diagram 1):** configurable workflow per acquisition type — Standard, Donation, Developer conveyance, Early (071; paths themselves built in Phase 4), ordered step-gating (072), statutory timelines/auto deadlines/delay flags (073), URA next-action reminders (074 — computed list, no email), URA-aligned process (075), right-of-entry (077), acquisition request intake & approval (080).

**Calculations:** `StatutoryDeadlines`, `StepGate.CanAdvance`, `ParcelRollup` — pure + tested.

**Frontend:** project list/detail, parcel table + board, parcel detail tabs (Summary, Parties, Interests, Milestones, Diary/Notes, Team), create/edit forms, batch-update dialog.

## Requirements
ROW-001–021, 071–077, 080, 233–241. (ROW-022 is Diagram 5 → deferred. ROW-078/079/081 → Phase 4.)

## Deferred / simplified
002 configurable WBS depth; 235 real map; 074 outbound notifications; 084 cross-module sharing is implicit (single DB).

## Delivery in slices
- **Slice 1 — project core (done):** ROW-001, 003, 004, 012, 013, 076, 238. ADR 0006.
- **Slice 2 — parcel core (done):** ROW-002, 005–011, 071–074, 233–237. Parcel, groups, parent tracts, interests, milestones, notice to owner, site visits, split/combine, batch update, status board, custom fields, step gating with override, statutory deadlines and reminders, synopsis. ADR 0008.
  - 009 (print-friendly synopsis) is the browser print of the parcel Summary tab; 235 is the status-coloured board (no map).
  - Seeded deadline periods and workflow steps are **placeholders** the agency must confirm (vault/README_Final.md "Yours to verify").
- **Slice 3a — parties (done):** ROW-014 owners and interests (shares, several per parcel), 015 contacts, 016 tenants and occupants (lease and insurance details), 017 abutters and interested parties, 018 W-9 / 1099 tax ID (encrypted, masked, write-only). ADR 0009. Owners are recorded once on the parent tract and inherited by its parcels; split and combine keep a parcel's own people.
  - Open questions for the user (flagged, not guessed): 018 which parties are 1099-reportable is a flag the agency sets per party; the retention/rotation policy for the tax-ID encryption key is the agency's (the key is `Security__TaxIdKey`; losing it makes stored numbers unreadable).
- **Library (done, between 3a and 3b):** administrator-defined fields (type, allowed values, defaults), CSV import/export, parcel status list managed there; the 16 default parcel statuses replace the earlier defaults. ADR 0010. The parcel form that uses these fields is a later slice.
- **Slice 3b — team, diary, notes (done):** ROW-019 (team by role per parcel; assignees are free text, consultant flag), 020 (negotiation diary; printable; kept by the Negotiations page, so the Negotiator writes and the Project Manager reads), 021 (notes; made by / made on; create and delete follow the Parcels page). ADR 0011.
- **Slice 3c — right of entry, requests, reviews, reports, plan (done):** ROW-077 right of entry per parcel plus a report of all of them (an ended right reads Expired), 080 acquisition request intake with a two-person approval, 239 plan-review gate (engineer and surveyor reviews; agency setting, off by default; adds a step-gate outcome no override can pass), 240 staff report and certificate of necessity (draft, submit, approve or reject by someone else), 241 acquisition plan and schedule computed per project with budget against estimate (print and CSV). ADR 0012. 075 (URA alignment) needs no code.
  - Open questions for the user (flagged, not guessed): what approving a request should start (project, parcel, nothing yet); which step the plan-review gate holds a parcel at (placeholder: Title & survey) and whether reviewers must be named users; the wording and template of the certificate of necessity; whether the Project Manager may decide the requests they enter (today: never).
  - Skipped on purpose: nothing in range is deferred in this slice.

## Exit checklist
- [ ] Create project → add parent tract → split into parcels → add owners/interests → set milestones, end to end in UI
- [ ] Step-gate blocks out-of-order advance; override requires Admin/PM + reason
- [ ] Tenant filter on every new aggregate; validators on every command
- [ ] Calculation unit tests ≥ happy path + boundary cases
