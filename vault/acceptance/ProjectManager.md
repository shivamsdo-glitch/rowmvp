# Project Manager — acceptance cases

**Who:** the ROW Project Lead. Creates the project and its parcels, owns funding, budget, team and milestones, keeps the acquisition on track, and approves at project level.
**Sees:** everything in the acquisition lifecycle; edits Projects, Parcels and Parties; approves (Approvals: create/read/update). Cannot change title, survey, appraisal, negotiation or payment records, and has no Administration.
**Also run:** every case in [Everyone.md](Everyone.md) as this role.

## Jobs possible today

| Id | Job | Steps (what the person does) | Expected: on screen | Expected: server and data | Status | Run by |
|---|---|---|---|---|---|---|
| AT-PM-10 | Keep the paperwork for a project and its parcels | Sign in. Attach the funding approval to a project and a parcel sketch to a parcel. Find them in the list, open the sketch | Signed in on the Dashboard | Both files stored; the list shows them; the sketch downloads byte-for-byte as an attachment | ✅ | E2E `AT-PM-10`; API `RoleAcceptanceApiTests` |
| AT-PM-11 | Review what specialists produced | Open title, appraisal and offer documents attached by others. Try to attach to or remove from them | – | Can read all three; every attach and remove is refused (403) | ✅ | E2E `AT-PM-11`; API role × record matrix (60 cases) |
| AT-PM-12 | Keep my own saved views | Save "My open parcels" on Parcels, reload the page, look again; a colleague with the same role looks | – | My view persists; my colleague sees none of mine | ✅ | E2E `AT-PM-12`; API `RoleAcceptanceApiTests` |
| AT-PM-13 | Stay out of administration | Open Configuration directly | "You don't have access" | The administrator endpoint answers 403 | ✅ | E2E `AT-PM-13` |
| AT-PM-20 | Create a project and clear it to acquire | Open Projects, create a project (number generated, or typed). Add two estimate versions, set a fiscal-year budget twice, record the NEPA decision, add a preliminary then an acquisition authorization with federal share. Also: import a CSV, search and filter, edit the record, approve and withdraw a hardship exception, type a duplicate number | Project opens held ("Missing: NEPA decision and Acquisition authorization"); estimate history keeps v1 and marks v2 current; one row per fiscal year; a preliminary authorization does not open it, an acquisition authorization does; the list shows "Open for offers" and the current estimate; duplicates and backwards dates are explained next to the field; CSV result lists each bad row | Number unique per agency, case-insensitive; estimates immutable and versioned; gate computed from NEPA, authorization type and exception; 20 simultaneous creates all succeed with distinct numbers; 10 people typing the same number produce exactly one project; every change audited | ✅ | E2E `AT-PM-20` ×8; API `ProjectApiTests`; frontend `projects.test.tsx`; unit `ProjectTests`, `ProjectNumberingTests`, `AcquisitionGateTests` |

## Jobs that arrive with later phases (pending, tracked in `e2e/tests/future.spec.ts`)

| Id | Job | Phase | Success looks like |
|---|---|---|---|
| AT-PM-21 | Add a parent tract and split it into parcels, creating a remnant | 1 | Parcels and remnant exist under the project with areas that add up to the tract |
| AT-PM-22 | Add owners and ownership interests (several per parcel) | 1 | Interests and shares are saved; tax IDs are masked everywhere on screen and in the API |
| AT-PM-23 | Set milestones and see roll-up counts and areas for the project | 1 | Counts and areas match the parcels; an overdue milestone is flagged |
| AT-PM-24 | Try to advance a parcel out of order | 1 | The step gate blocks it with the missing step named; overriding requires a reason and is recorded |
| AT-PM-25 | Update the status of many parcels at once | 1 | Only the selected parcels change; each change is audited |
| AT-PM-26 | Approve just compensation that falls in their approval band | 3 | They can approve only when their role is the next step in the chain for that amount; cannot approve out of turn |
| AT-PM-27 | Open the Dashboard to see pipeline, budget and overdue items | 5 | Numbers reconcile with the parcel lists; clicking a figure opens the matching filtered list |

## Changelog
- Phase 1 slice 1: AT-PM-20 automated (project core). Found by the browser run: the server's empty-name message was a validator default ('Name' must not be empty); now "Name is required.".
- Initial: AT-PM-10..13 automated; 20..27 specified.
