# ADR 0011 — Parcel team, negotiation diary and notes

**Status:** accepted (Phase 1, slice 3b). Scope: ROW-019, 020, 021.

## Team (ROW-019)
`ParcelTeamMember`: a role (project lead, appraisal coordinator, appraiser, reviewer, negotiator, counsel, relocation agent, FHWA, other), a name, an optional organization, a **consultant** flag, contact details and notes. The person is **free text**: there is no staff directory yet and consultants are assigned alongside agency staff. A person holds one role once per parcel, ignoring case and spacing (unique index on parcel, role and a normalised name; 409 `team_member_exists`). Roles are stored as text, so the list is ordered by role in code, not in SQL. There is **no Engineer or Surveyor role** here; plan reviewers are recorded in slice 3c. Governed by the Parcels page (create assigns, update changes, delete removes).

## Diary (ROW-020)
`ParcelDiaryEntry`: date, how (call, meeting, mailing, email, contact attempt, other), direction (we contacted them / they contacted us), who was contacted (free text, or a party, whose name is copied), what was said, what was promised or answered. **Append-only**: no edit; a mistaken entry is removed (audited) and logged again. A date after today **where the project is** is refused (ADR 0008 time zones). It is a **printable** summary (the browser's print of the Diary tab). It is governed by the **Negotiations** page (U: negotiators keep the diary, Diagram 4), so the Project Manager and Finance read it, the Title Examiner and Appraiser do not see it, and the Project Manager cannot add to it. Open question: should the Project Manager be able to write diary entries?

## Notes (ROW-021)
`ParcelNote`: free text up to 4000 characters. Who made it and when are the audit columns (shown by name when the account is known). Create and delete follow the Parcels page, so the **Negotiator cannot add notes** (the page is read-only for them); open question for the user, as noted in ADR 0008.

## Shared
Records can be read on a split or combined parcel but not added or changed (409 `parcel_not_active`). Another agency's parcel is simply not found. Removing is a real delete, recorded in the audit trail.
