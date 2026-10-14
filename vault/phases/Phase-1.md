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

## Exit checklist
- [ ] Create project → add parent tract → split into parcels → add owners/interests → set milestones, end to end in UI
- [ ] Step-gate blocks out-of-order advance; override requires Admin/PM + reason
- [ ] Tenant filter on every new aggregate; validators on every command
- [ ] Calculation unit tests ≥ happy path + boundary cases
