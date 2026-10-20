# ADR 0018 — The title, closing and appraisal tabs of a parcel

**Status:** accepted (Phase 1.5 slice 6, in sub-slices 6a, 6b, 6c). Decisions marked **U** came from "Next requirements.docx" and the answers of 2026-10-19. Same pattern as ADR 0017: a tab per record set, its own page key, the approved default access, attachments through the shared documents API, ids built from the parcel number.

## 6a — Title Encumbrances (U)
- **Many per parcel**, numbered `ENC-<parcel number>-<sequence>` (one more than the highest on the parcel; a per-parcel lock and a unique index keep ten at once apart).
- Fields as the reference screen: encumbrance type* (Library *Encumbrance type*, seeded with the screen's list), document title*, document date, date recorded, document number / volume and page, **unrecorded document** (then there is no recording date: 400 if both are sent), grantor / lessor*, grantee / lessee*, exception to title submitted and approved dates (approval cannot come before submission), impact analysis, recommended cure, cure status (Library *Cure status*, empty to fill), review notes, linked records (record identifier, module name, notes) and attachments.
- **Access:** the `titleencumbrances` page: Title Researcher CRUD, Admin CRUD, everyone else read.
- **Not built:** the screen's collapsed **Work log** (nothing in the document says what it records; flagged for the user), Link Document, Workflow and Select Actions.

## 6b, 6c
Recorded below as each sub-slice lands.
