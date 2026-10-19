# Designer — acceptance cases

**Who:** the ROW Designer. Adds and updates parcels from the plans and ROW maps: the parcel details, ownership, location, contacts and easements. The Design Approver (read + update on Parcels) reviews and approves them.
**Sees:** everything in the lifecycle; creates, changes and removes Parcels and Ownership details; Contact Log and Easements are create/read/update; cannot touch title, appraisal, negotiation or payments.
**Also run:** every case in [Everyone.md](Everyone.md) as this role.

## Jobs possible today

| Id | Job | Steps (what the person does) | Expected: on screen | Expected: server and data | Status | Run by |
|---|---|---|---|---|---|---|
| AT-DE-40 | Add a parcel with the parcel-details form and change it later | Parcels > New parcel: project, area, address, CAD number, acquisition interest type and possession type (Library lists), possession and proposed possession dates, segment, priority, parcel location, Active parcel. Save, read the summary, edit, clear a field, untick Active parcel. Open the Interests tab | The summary shows every field; the Interests grid shows square feet and acres | Text is trimmed; blanks are stored as nothing; an update that leaves a field out clears it; over-long values are refused (400); Active parcel is separate from the split/combine state | ✅ | E2E `AT-DE-40` ×2; API `ParcelDetailsApiTests`; frontend `parcel-details.test.tsx` |
| AT-DE-41 | Parcel buttons and the API follow the Parcels permission | Every role opens a parcel and the parcel list | New parcel only with create; Edit parcel only with update (the Design Approver can edit but not create) | 403 on create or update without the matching box ticked | ✅ | E2E `AT-DE-41` ×11 roles; API `ParcelDetailsApiTests`; unit `PermissionTemplateCalculatorTests` |

## Changelog
- Phase 1.5 slice 5a: AT-DE-40, 41 automated. The "read and update" access level (Design Approver on Parcels, matrix of 2026-10-19) is now a real default. ADR 0017.
