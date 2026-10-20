# Designer — acceptance cases

**Who:** the ROW Designer. Adds and updates parcels from the plans and ROW maps: the parcel details, ownership, location, contacts and easements. The Design Approver (read + update on Parcels) reviews and approves them.
**Sees:** everything in the lifecycle; creates, changes and removes Parcels and Ownership details; Contact Log and Easements are create/read/update; cannot touch title, appraisal, negotiation or payments.
**Also run:** every case in [Everyone.md](Everyone.md) as this role.

## Jobs possible today

| Id | Job | Steps (what the person does) | Expected: on screen | Expected: server and data | Status | Run by |
|---|---|---|---|---|---|---|
| AT-DE-40 | Add a parcel with the parcel-details form and change it later | Parcels > New parcel: project, area, address, CAD number, acquisition interest type and possession type (Library lists), possession and proposed possession dates, segment, priority, parcel location, Active parcel. Save, read the summary, edit, clear a field, untick Active parcel. Open the Interests tab | The summary shows every field; the Interests grid shows square feet and acres | Text is trimmed; blanks are stored as nothing; an update that leaves a field out clears it; over-long values are refused (400); Active parcel is separate from the split/combine state | ✅ | E2E `AT-DE-40` ×2; API `ParcelDetailsApiTests`; frontend `parcel-details.test.tsx` |
| AT-DE-41 | Parcel buttons and the API follow the Parcels permission | Every role opens a parcel and the parcel list | New parcel only with create; Edit parcel only with update (the Design Approver can edit but not create) | 403 on create or update without the matching box ticked | ✅ | E2E `AT-DE-41` ×11 roles; API `ParcelDetailsApiTests`; unit `PermissionTemplateCalculatorTests` |
| AT-DE-42 | Record who owns a parcel | Open a parcel's Ownership details tab. Try an empty form; add an ownership with owner name, owner type, an address and contacts (a second primary contact is refused); look at the parcel summary; open Edit parcel; change the ownership, attach a deed, remove them | Ownerships are numbered 1, 2...; the parcel shows the owner names joined by "; " and the parcel form shows them greyed out; the attachments panel appears while an ownership is being changed | The owner name is derived and cannot be sent; one primary contact at most (400); bad phone or email, over-long text and a missing owner name are refused (400); ten added at once get different numbers; removing the last ownership clears the owner name | ✅ | E2E `AT-DE-42`; API `OwnershipApiTests`; frontend `parcel-tabs.test.tsx`; unit `ParcelTabsTests` |
| AT-DE-43 | See where a parcel is | Open a parcel's Parcel location tab; export it | A grid with the parcel id, CAD number and ESRI save status ("Not saved": no map or GIS is connected yet); no form; the export is a one-row CSV | Only roles that can read the Parcel Location page see the tab | ✅ | E2E `AT-DE-43`; frontend `parcel-tabs.test.tsx` |

## Changelog
- Phase 1.5 slice 5a: AT-DE-40, 41 automated. The "read and update" access level (Design Approver on Parcels, matrix of 2026-10-19) is now a real default. ADR 0017.
- Phase 1.5 slice 5c: AT-DE-42, 43 automated (Ownership details with the derived owner name; the read-only Parcel location tab). ADR 0017.
