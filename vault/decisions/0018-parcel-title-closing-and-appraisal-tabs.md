# ADR 0018 — The title, closing and appraisal tabs of a parcel

**Status:** accepted (Phase 1.5 slice 6, in sub-slices 6a, 6b, 6c). Decisions marked **U** came from "Next requirements.docx" and the answers of 2026-10-19. Same pattern as ADR 0017: a tab per record set, its own page key, the approved default access, attachments through the shared documents API, ids built from the parcel number.

## 6a — Title Encumbrances (U)
- **Many per parcel**, numbered `ENC-<parcel number>-<sequence>` (one more than the highest on the parcel; a per-parcel lock and a unique index keep ten at once apart).
- Fields as the reference screen: encumbrance type* (Library *Encumbrance type*, seeded with the screen's list), document title*, document date, date recorded, document number / volume and page, **unrecorded document** (then there is no recording date: 400 if both are sent), grantor / lessor*, grantee / lessee*, exception to title submitted and approved dates (approval cannot come before submission), impact analysis, recommended cure, cure status (Library *Cure status*, empty to fill), review notes, linked records (record identifier, module name, notes) and attachments.
- **Access:** the `titleencumbrances` page: Title Researcher CRUD, Admin CRUD, everyone else read.
- **Not built:** the screen's collapsed **Work log** (nothing in the document says what it records; flagged for the user), Link Document, Workflow and Select Actions.

## 6b — Title and Closing (U)
- **One record per parcel** (agreed 2026-10-19), numbered `TC-<parcel number>`, saved with one `PUT /parcels/{id}/title-closing`: the first save creates it (needs Create), later saves change it (need Update), and `GET` answers `{ "record": null }` until one exists. A unique index keeps it to one whatever races to create it (the loser gets 409 `title_closing_exists`).
- **General:** assigned to and title/closing agent (people from the Library *Agents* list, stored as the name chosen), title company (Library *Title company*), GF number, final title policy number. **Vested owner name is read-only and derived**: the parcel's ownerships of type "Vested Owner", joined with "; ".
- **Agreement and closing dates:** purchase and sale agreement signed by owner and by ATP, amount (zero or more), settlement statement received and executed (not before received), curative items completed, closing package submitted and approved (not before submitted), closing instruction to title company, anticipated and actual closing date.
- **Two grids**, replaced as sent on every save: *Conveyance documents* (type from Library *Conveyance type*, signed date, recorded date not before signed, recording number) and *Title commitments* (vested owner name*, requested, received, effective, issued, curative plan submitted and approved not before submitted); at most 50 rows each.
- **Access:** the `titleclosing` page: Title Researcher and Negotiator create, read and update; Admin full; everyone else read. Only Admin can remove the record. Attachments use entity type `TitleClosing`, shown once the record exists.
- Closing and recording as a process (Diagram 4's closing steps, payments) is not changed by this tab: these are the fields the agency keeps; nothing here moves a parcel through its workflow.

## 6c
Recorded below as it lands.
