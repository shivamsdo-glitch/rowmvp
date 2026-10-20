# ADR 0019 — The negotiation and questionnaire tabs of a parcel

**Status:** accepted (Phase 1.5 slice 7, in sub-slices 7a and 7b). Decisions marked **U** came from "Next requirements.docx" and the answers of 2026-10-19. Same pattern as ADR 0017 and 0018: a tab per record, its own page key, the approved default access, attachments through the shared documents API.

## 7a — Negotiation (U)
- **One record per parcel**, numbered `NEG-<parcel number>`, saved with one `PUT /parcels/{id}/negotiation`: the first save creates it (needs Create), later saves change it (need Update); `GET` answers `{ "record": null }` until one exists; a unique index keeps it to one whatever races to create it (the loser gets 409 `negotiation_exists`). Page key `parcelnegotiation` (the older `negotiations` key belongs to the earlier Negotiations module); attachments use entity type `ParcelNegotiation`.
- **Parcel details are read-only here.** Parcel number, owner name, possession type, possession and proposed possession dates, parcel status, location, address and CAD number already live on the parcel (ADR 0017); the tab shows them, it does not copy them. Only *assigned to* (a Library agent) is the negotiation's own.
- **Five grids, replaced as sent on every save** (at most 50 rows each): *status notes* (a date and plain text), *agents* (a Library agent chosen by name; type, company, phone and email are read from the Library, never stored), *right of entry* (letter mailed, delivered not before mailed, signed received, sent to ATP, expiration date, special instructions), *offers* (Library *Offer type*, mailed, delivered not before mailed, amount of zero or more, notes) and *administrative settlement* (Library *Settlement type*, counteroffer amount, received, submitted not before received, Library *Counteroffer response*, response dates from ATP and to the owner, notes).
- **Agreement and JAT:** total agreement amount and notes, counteroffer submitted to the JAT, the response (not before submitted), Library *JAT response* and whether FTA approval is required for the counteroffer.
- **Possession and use agreement (PUA):** requested, approved (not before requested), approved amount, executed and closed (not before executed).
- **Access:** Negotiator and Admin create, read, update and delete; everyone else reads.
- **Not built:** the paperclip column on the right-of-entry and settlement rows (a file per row). Files attach to the record as a whole; the document does not say what a per-row file should be tied to, so it is flagged rather than guessed. Workflow, Select Actions, Save & Continue and Link Document are skipped as in every other tab.

## 7b
Recorded below as it lands.
