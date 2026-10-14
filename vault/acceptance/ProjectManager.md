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

## Jobs that arrive with later phases (pending, tracked in `e2e/tests/future.spec.ts`)

| Id | Job | Phase | Success looks like |
|---|---|---|---|
| AT-PM-20 | Create a project with a unique ID, region/district, funding source and a versioned cost estimate | 1 | Project appears in the list with its generated ID; estimate history is kept; a second project gets a different ID |
| AT-PM-21 | Add a parent tract and split it into parcels, creating a remnant | 1 | Parcels and remnant exist under the project with areas that add up to the tract |
| AT-PM-22 | Add owners and ownership interests (several per parcel) | 1 | Interests and shares are saved; tax IDs are masked everywhere on screen and in the API |
| AT-PM-23 | Set milestones and see roll-up counts and areas for the project | 1 | Counts and areas match the parcels; an overdue milestone is flagged |
| AT-PM-24 | Try to advance a parcel out of order | 1 | The step gate blocks it with the missing step named; overriding requires a reason and is recorded |
| AT-PM-25 | Update the status of many parcels at once | 1 | Only the selected parcels change; each change is audited |
| AT-PM-26 | Approve just compensation that falls in their approval band | 3 | They can approve only when their role is the next step in the chain for that amount; cannot approve out of turn |
| AT-PM-27 | Open the Dashboard to see pipeline, budget and overdue items | 5 | Numbers reconcile with the parcel lists; clicking a figure opens the matching filtered list |

## Changelog
- Initial: AT-PM-10..13 automated; 20..27 specified.
