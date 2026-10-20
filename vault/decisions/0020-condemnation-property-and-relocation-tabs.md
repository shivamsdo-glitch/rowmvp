# ADR 0020 — The condemnation, property management and relocation tabs of a parcel

**Status:** accepted (Phase 1.5 slice 8). Decisions marked **U** came from "Next requirements.docx" and the answers of 2026-10-19. Same pattern as ADR 0017–0019: a tab per record, its own page key, the approved default access, attachments through the shared documents API.

**Scope note.** CLAUDE.md defers condemnation (Diagram 6), relocation (Diagram 5) and property management and disposal (Diagram 8) as *processes*. These three tabs are the **parcel records the agency keeps** (dates, people, grids and notes, as the document lays them out). Nothing here moves a parcel through a workflow, raises a hand-off or applies a statutory rule; the process modules stay v2.

## Shared
- **Status notes** on all three are plain dated notes (a date and text, at most 50), kept in one table `record_status_notes` keyed by the kind of record and its id, and replaced as sent with the record.
- **Vested owner name** (Condemnation and Relocation) is read-only and derived: the parcel's ownerships of type "Vested Owner", as on Title and Closing.
- Agents are chosen by name from the Library *Agents* list; type, company, phone and email are read from the Library, never stored.
- Grids are replaced as sent on every save, at most 50 rows each (the relocation contact log 200).

## 8a — Condemnation (U)
- **One record per parcel**, numbered `CON-<parcel number>`, saved with one `PUT /parcels/{id}/condemnation` (first save needs Create, later saves Update; `GET` answers `{ "record": null }` until one exists; a unique index keeps it to one, the loser gets 409 `condemnation_exists`). Page key `parcelcondemnation`; attachments use entity type `ParcelCondemnation`.
- **Preliminary condemnation:** eight dates from the package submitted to ATP to the city council approval. **Condemnation support:** ED coordinator, outside counsel, appraisal and engineering witnesses (Library agents), two expert witness names, probate cause number, objections filed, and the petition, lis pendens, commissioner, hearing, fee and objection dates.
- **Grids:** attorneys of record (party represented, name, firm, address, phone, email), special commissioners (appointment date and type, name, address, email, phone, struck) and notices of hearing (party to serve, service submitted and completed, NOH filed, comments).
- **Rules:** package approved not before submitted; petition package reviewed not before received; objection deadline not before the petition is filed; service completed not before submitted.
- **Access:** Negotiator creates, reads and updates, **Admin alone removes** (approved matrix); everyone else reads.

## 8b — Property Management & Disposition (U)
- **One record per parcel**, numbered `PMD-<parcel number>`, same upsert as above (`/property-management`, 409 `property_record_exists`). Page key `parcelproperty`; attachments `ParcelProperty`.
- **Property management:** transfer memo submitted and approved by ATP (approved not before submitted) and sent to PDB, property secured, demo status (Library) and demo completed. **Disposition:** type, method and status (Library), justification, buyer / grantee, FTA concurrence required, sale price (zero or more), legal review, board approval, closing / transfer, recording date (not before closing) and document recording number.
- **Funding distribution grid:** fund type (Library), fund source, amount (zero or more) and percentage (0–100; the rows together may not exceed 100).
- **Access:** Project Manager and Admin create, read, update and delete; everyone else reads.

## 8c — Relocation (U)
- **Many per parcel.** The document shows *Relocation number* as a required field the user types, so it is **entered by the user and unique on the parcel** (409 `relocation_number_exists`), like an easement number, rather than auto-numbered. Page key `parcelrelocation`; attachments `ParcelRelocation`.
- **Relocation block:** assigned to, displacee name (required), limited English proficiency, displacee status and interest (Library), mailing and site addresses, relocation type (required, Library). **Milestones:** the GIN, interview, eligibility, 90 day and 30 day notices, vacate and completion dates; demolition required and ineligible flags. GIN delivered is not before mailed; actual vacate not before the initial interview; the 30 day notice delivered not before issued.
- **Grids:** agents, relocation contacts (name, type, contact, email, address, one primary at most), relocation eligibility (type from the Library, claim limit, submitted for approval, approved not before submitted) and a **contact log** (date and time, mode, contacted by, contact persons, discussion notes). That contact log is rows kept on the relocation, separate from the parcel's Contact Log tab (ADR 0017); the document's note about saving the contacts first so their names appear in the contact log is not needed because the persons are typed.
- **Access:** Relocation Agent and Admin create, read, update and delete; everyone else reads.
- **Flagged:** *Relocation complete* and *Relocation complete 1* are two separate dates in the document and are kept as written; the agency should say what the second one is for.
