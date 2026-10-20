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

## 5b — Contact Log and Easements (U)
Each tab is a record set on a parcel with **its own page key** (`contactlog`, `easements`), so the approved matrix applies exactly: Contact Log CRU for Designer, Surveyor, Title Researcher and Appraiser, CRUD for Negotiator and Relocation Agent; Easements CRU for Designer, CRUD for Surveyor; read for everyone else. A role with no read on a page does not see its tab.
- **Contact Log is not the negotiation diary (ROW-020).** The diary stays as built (read-only entries, Negotiations page). The Contact Log follows the new form: date and time, mode (Library *Mode of contact*), contacted by, contact person(s), discussion notes, linked records (identifier, module name, notes). Entries can be changed. Contact person(s) is free text: the reference screen's picker draws on Parcel Contacts, which the Parties module owns, and the link between the two is left to 5c.
- **Ids:** `CL-<parcel number>-<sequence>`, sequence = highest on the parcel + 1, issued under a per-parcel lock with the unique (parcel, sequence) index as the cross-instance backstop. Removing an earlier entry never reissues a number; removing the latest frees its number.
- **Times** are stored in UTC; the form shows and takes the person's own time zone.
- **Easements:** number (typed, required, unique per parcel), type (Library *Easement type*), description, start and end date (end may equal start), start and end station. Both lists start empty for the agency to fill.
- **Attachments** use the existing documents API (`ContactLog` and `Easement` entity types, governed by their pages: read to see, update to attach, delete to remove). The screens' "Link Document" is not built; Upload Document is, in a new reusable attachments panel shown while a record is being changed.
- **Save failures:** EF wraps a database deadlock in a generic `InvalidOperationException`, which the "someone saved at the same moment" handlers did not catch (a 500 on overlapping Library imports). `RowDbContext.SaveChangesAsync` now hands them the real `DbUpdateException`.

## 5c
Recorded below as it lands.
