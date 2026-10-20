# Surveyor — acceptance cases

**Who:** the Surveyor. Verifies right-of-way limits and interest type, records field data, and keeps each parcel's easements.
**Sees:** everything in the lifecycle; creates, changes and removes Easements; Parcel Location and Contact Log are create/read/update; cannot touch title, appraisal, negotiation or payments.
**Also run:** every case in [Everyone.md](Everyone.md) as this role.

## Jobs possible today

| Id | Job | Steps (what the person does) | Expected: on screen | Expected: server and data | Status | Run by |
|---|---|---|---|---|---|---|
| AT-SU-40 | Keep the easements of a parcel | Open a parcel's Easements tab. Try an empty form, an end date before the start date, then add an easement with type, term, description and stations; add the same number again; change it; remove it | The form says what is wrong beside the field; the list shows number, type, dates and stations; the duplicate number is explained | A number is unique within a parcel (409), though another parcel may reuse it; ten at once with the same number produce one; end before start is refused (400), the same day is allowed; an easement can carry attachments | ✅ | E2E `AT-SU-40`; API `ParcelTabsApiTests`; frontend `parcel-tabs.test.tsx`; unit `ParcelTabsTests` |

## Changelog
- Phase 1.5 slice 5b: AT-SU-40 automated (the Easements tab). ADR 0017.
