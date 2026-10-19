# ADR 0017 — The parcel details form and the parcel tabs

**Status:** accepted (Phase 1.5 slice 5, in sub-slices 5a, 5b, 5c). Decisions marked **U** came from "Next requirements.docx" and the answers of 2026-10-19. The reference screens are a guide to fields, not a new look: the existing design is kept.

## 5a — The parcel details form (U)
A parcel gains `CadNumber`, `AcquisitionInterestType` (the Library's *Interest type* list), `PossessionDate`, `ProposedPossessionDate`, `Segment`, `Priority`, `PossessionType` (the Library's *Possession type* list), `ParcelLocation` (text) and `IsActiveParcel` (the "Active Parcel" tick, true by default). All are optional; an update that leaves one out clears it (the form always sends them all).
- **Active parcel is not the parcel state.** `State` (Active/Split/Combined) records split/combine history and drives behaviour; `IsActiveParcel` is the agency's own tick.
- **Library values are not enforced server-side**, as with the project lists (ADR 0016): the form offers the active values and keeps one already saved, the server only limits length.
- **Status, Address (the screen's "Parcel Address"), Interests (with a new Acres column), Agents (the parcel Team) and Parcel Contacts (Parties)** already exist and are reused. Parcel Owner Name is derived from Ownership Details in 5c.
- Workflow, Select Actions and the Save & Continue buttons of the reference screens are not built (answered 2026-10-19).

## A "read and update" access level
The approved matrix (2026-10-19) gives the Design Approver read + update on Parcels, so it can set the approval flag without creating or deleting parcels. Until now defaults could only be Basic (R), Standard (CRU) or Pro (CRUD). A default may now also be written as the letters of the boxes ticked (`"RU"`); `PermissionTemplateCalculator.FlagsFor` reads them, and the stored label becomes `Custom` unless the flags match a template. The test spec has a matching level `U`. Existing agencies keep their rows (seeding is add-only); only new agencies, and rows not yet present, get the new default.

## 5b, 5c
Recorded below as each sub-slice lands.
