# ADR 0025 — Program view settings: stuck limit, health thresholds and the step-to-team mapping

**Status:** accepted (Phase 1.5). Decided with the user on 2026-10-21 after ADR 0024: the stuck limit, the health thresholds and the team that owns each workflow step are the agency's to set, **with today's values as the defaults**.

## What an administrator can set (Configuration page, "Program view settings")
- **Days on one step before a parcel is stuck** (default 30, 1 to 365).
- **Health thresholds:** days before the ROW clear date to start warning (90, 1 to 365); percent closed expected inside that window (75, 1 to 100); overdue parcels before a project is Off track, at least (3, 1 to 1000); share of the project overdue before it is Off track (25 percent, 1 to 100). Both of the last two must be reached.
- **Team per step:** every step of every workflow (the standard steps, then early approval, donation review, conveyance terms) with its team and its default. A team is free text up to 50 characters, picked from a list or newly typed. Steps given the same team are pooled in the "Where the time goes" view.
- **Reset to defaults** removes everything the agency saved.

## How it is stored and used
- Two tables (migration `AddProgramSettings`): `program_settings` (one row per agency; no row means the defaults) and `program_step_teams` (unique per agency and step key). **Only differences from the default mapping are stored**, so a later change to a default still reaches the steps nobody chose.
- API: `GET/PUT/DELETE /api/v1/dashboard/settings`, governed by the **Configuration page** (`administration`: read to see, update to change; today that is Administrator only). Validation at the API boundary: whole numbers within the ranges above, every team named, no step twice, and a step key that exists in some workflow (`unknown_step` otherwise). Nothing is saved when any value is refused.
- The Program and project endpoints read the saved values on every request and return the rules they used (`rules`, `stuckDays`), so the screen can say what it judged by. Pure calculations take the rules as arguments (`ProgramRules`, the optional team map), so the defaults stay in one place and are tested against the stored defaults.
- Changes are audited like every other record (who and when).

## Not built
- Per-project overrides of the thresholds, a history of what the settings used to be, and renaming a team across the history (a team is only a label on a step).
- The project manager cannot change these (the page is Administrator only today); widening that is a permission-matrix decision, not a code change.
