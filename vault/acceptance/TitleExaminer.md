# Title Examiner — acceptance cases

**Who:** the Title Examiner and Surveyor. Researches and orders title, tracks defects and encumbrances, handles survey requests and CAD files, and assembles the package that unlocks appraisal.
**Sees:** full control of Title and Survey; read-only on Projects, Parcels, Parties and Appraisals. Nothing of Approvals, Negotiations, Agreements, Closing, Payments or Administration.
**Also run:** every case in [Everyone.md](Everyone.md) as this role.

## Jobs possible today

| Id | Job | Steps | Expected: on screen | Expected: server and data | Status | Run by |
|---|---|---|---|---|---|---|
| AT-TE-10 | Put the title package together | Attach the commitment and deed to the parcel's title record and the boundary CAD file to its survey request | – | Three files stored under the right records; the title list shows the commitment and deed | ✅ | E2E `AT-TE-10`; API `RoleAcceptanceApiTests` title hand-off |
| AT-TE-11 | Undo a mistake | Remove a document attached to the wrong parcel | – | It disappears from the list and can no longer be opened; the removal is in the audit trail | ✅ | E2E `AT-TE-11`; API `ResilienceTests` audit |
| AT-TE-12 | Stay in my lane | Try to attach to an appraisal, an offer and a payment; try to look at payments | – | All refused (403), including viewing payments | ✅ | E2E `AT-TE-12`; API 60-case matrix |
| AT-TE-13 | Keep a saved view for the title list | Save "Defects to clear" on Title; try the same on Payments | – | Title view saved; Payments refused (403) for both save and list | ✅ | E2E `AT-TE-13`; API matrix (78 cases) |
| AT-TE-14 | Colleagues see my package | An appraiser, project manager and negotiator open my title documents | – | They can read; none can attach to or remove from the title record; Finance cannot even list it | ✅ | API `RoleAcceptanceApiTests` |

## Jobs that arrive with later phases

| Id | Job | Phase | Success looks like |
|---|---|---|---|
| AT-TE-20 | Order title, receive it and finish QC | 2 | Order → received → QC dates tracked per parcel; the order tracker shows the state |
| AT-TE-21 | Log defects and encumbrances, then clear them | 2 | Alerts raised on creation; clearing records who and why; uncleared defects visible on the defect board |
| AT-TE-22 | Request a survey, attach CAD, review the legal description and sketch | 2 | Request status tracked; files versioned; review initials recorded; only roles allowed to edit survey plans can |
| AT-TE-23 | Complete the appraisal package | 2 | Checklist complete → parcel becomes eligible for an appraisal request; an incomplete package blocks the request with the missing items named |
| AT-TE-24 | Protect confidential title information | 2 | Other roles do not see confidential title fields on screen or in the API response |

## Changelog
- Initial: AT-TE-10..14 automated; 20..24 specified.
