# ADR 0024 — The ROW Director's Program view

**Status:** accepted (Phase 1.5). Requested by the user on 2026-10-21: the dashboard must tell a ROW Director where right of way stands on every project and every parcel, and which teams take the most time, not only what each person has done.

## What it shows (dashboard tab **Program**, the default tab for Project Managers and Administrators)
- **Tiles:** projects, active parcels, % of parcels closed, projects needing attention (amber or red), parcels stuck on a step, estimated ROW cost, known cost to date.
- **Project table:** health (with the first reason), parcels, % closed, a one-bar pipeline of where its parcels are across the workflow, overdue and stuck parcels, estimate, known cost, ROW clear date with days to go or days late. Open a project for the detail.
- **Project page** (`GET /api/v1/dashboard/projects/{id}`): health reasons, tiles, parcels per workflow step, **one row per active parcel** (owner, status, step and owning team, days on the step, appraisal total, approved just compensation, agreed amount, anticipated or actual closing, attention flags), and that project's own time by team and step.
- **Where the time goes:** teams ranked by average days a parcel stays with them (longest first), time in every step (finished stays, current stays, longest, stuck), and the ten parcels stuck longest, each linking to the parcel.
- API: `GET /api/v1/dashboard/program` and `/projects/{id}`; 403 unless the caller holds `ProjectManager` or `Admin` (same rule as the Team tab). No new tables.

## Definitions (change them in `Application/Calculations/ProgramMetrics.cs`, with the tests)
- **Time in a step:** from the recorded workflow moves (`ParcelStepEvent`). Before a parcel's first move it is on the step that move left, since the parcel was set up. A step visited twice counts as two stays. Open stays count up to now. These are the dates people moved the parcel, so they are only as accurate as the moves are timely.
- **Stuck:** an open stay of more than **30 days** on a parcel that is not closed.
- **Team of a step (the default mapping; editable per ADR 0025):** setup and early approval → Project team; title & survey → Title & Survey; appraisal → Appraisal; offer, negotiation, agreement, donation review, conveyance terms → Negotiation; closing → Closing; any other step → Other.
- **Closed parcel:** an actual closing date on its Title and Closing record, or the "Closed" milestone completed.
- **Known cost:** per parcel the agreed amount (Negotiation) if there is one, else the latest approved just compensation; summed over the project. It is a floor (parcels with no figure add nothing). **Estimate:** the project's latest cost estimate version.
- **Health:** *Off track* (red) if the ROW clear date has passed with parcels not closed, or known cost is over the estimate, or at least 3 parcels and 25% of the project have an overdue deadline. *Watch* (amber) if any parcel has an overdue deadline, the clear date is within 90 days with under 75% closed, or any parcel is stuck. Otherwise *On track*; a project with no parcels shows *No parcels*. Every reason is shown in words.
- Overdue deadlines come from the existing reminders (ROW-074). That list is capped at 200 entries, so on a very large program the overdue counts may be understated.
- Dates and "today" are UTC; a parcel's own project time zone is not applied to these counts.

## Not built (flagged, not guessed)
- (Done in ADR 0025: the 30-day limit, the health thresholds and the step-to-team mapping are now editable in Configuration; the values below are the defaults.)
- Per-user (rather than per-team) timing, and a trend over time (the page shows the present state and the moves recorded so far).
- Budget by fiscal year against spend, relocation, condemnation and disposition progress (those processes are v2).
- Export or print of the program view.
