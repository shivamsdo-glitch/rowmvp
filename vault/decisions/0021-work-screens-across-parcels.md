# ADR 0021 — Work screens across parcels: Title & Survey, Appraisal, Negotiation & Closing

**Status:** accepted (Phase 1.5, after slice 8). Decided with the user on 2026-10-21: the three left-menu items that were greyed out as "Soon" become working screens, and they are **not read-only**: a person goes there, searches for the record they are responsible for, and adds to it or updates it.

## What each screen does
- **Search** (one box): by parcel number, parcel address, **owner name** (the parcel's Ownership Details), or the record's own ID (`APR-...`, `NEG-...`, `TC-...`, `ENC-...`, an easement number); case-insensitive, matches anywhere in the text, plain text only (wildcards and quotes mean themselves). With no text the screen lists the most recent records. At most 200 rows come back.
- **Matching records** table: the record's ID, parcel, owner and address and the few fields that identify it. **Open** opens its parcel on the same screen.
- **Matching parcels** table (shown when there is search text): so a record can be **started** for a parcel that has none yet (for example a first negotiation).
- **Selected parcel** panel: the parcel's own tab for that record kind, exactly as inside the parcel, with the same add, change, remove and attachment controls and the same permissions. Nothing about editing is re-implemented, so the screen and the parcel can never disagree about what a role may do. A parcel that is no longer active (split, combined) opens read-only.
- **Record kinds per screen:** *Appraisal* → appraisals. *Negotiation & Closing* → negotiations and title and closing records (a switch between the two). *Title & Survey* → title encumbrances and easements (a switch between the two).

## Rules
- The menu items and routes use the existing page keys `appraisals`, `negotiations` and `title` (as the greyed-out items always did), so they appear for the roles that can read those pages.
- Each search is governed by the **page of the parcel tab it searches** (`parcelappraisal`, `parcelnegotiation`, `titleclosing`, `titleencumbrances`, `easements`); a role without read gets 403. Changing a record uses the tab's own endpoints, so create, update and delete are checked there.
- API: `GET /api/v1/overview/{appraisals|negotiations|title-closings|encumbrances|easements}?search=`. No new tables.
- The parcel list search (`GET /api/v1/parcels?search=`) now also finds a parcel by the name of an owner in its Ownership Details, so the Parcels page benefits too.

## Not built
- Survey data itself: the document describes no Survey tab, so "Title & Survey" covers the title and easement records only.
- Paging beyond 200 rows, sorting and export: the screens are for finding one's own record, and the full lists stay on the parcel pages.
