# ADR 0022 — Per-person dashboard, manager roll-up and in-app Help center

**Status:** accepted (Phase 1.5, after the work screens). Requested by the user on 2026-10-21: a dashboard that is separate for each person (what they have done, what is pending), a roll-up for managers, UI changes for visibility, and a Help section behind the question-mark icon.

## The dashboard (`/`, page key `dashboard`, read by every role)
- **My work** (everyone): tiles (needs your attention, overdue deadlines, done in 30 days, last active), a **Waiting for you** list, a seven-day **What you have done** chart, **work by record** for 30 days, **recent activity**, and the existing Next actions (deadline reminders).
- **Team** (project managers and administrators only): portfolio tiles (projects, active parcels, awaiting a decision, overdue deadlines, rights of entry expiring, closings due), parcels by status, a seven-day team chart, work by record, and a **People** table (what each person added, changed and removed, last active, open requests and draft reports). A tab bar appears only for managers.
- API: `GET /api/v1/dashboard/me` (governed by `dashboard` read) and `GET /api/v1/dashboard/team` (also 403 unless the caller holds `ProjectManager` or `Admin`). No new tables.

## How "done" is counted
- From the **audit log**, which already records who changed what and when. Nothing new is stored, so every module is covered automatically, now and later.
- One save that changes several fields writes several audit rows; they collapse so **one action = one record + one kind of change + one minute**. Bookkeeping records (`UserToken`, `SavedFilterProfile`, `ProjectNumberCounter`, `AppUser`, `UserRole`) are not work and are ignored.
- Pure calculation `Application/Calculations/ActivitySummary` (window 30 days, chart 7 days, `now` passed in), with unit tests. At most 20,000 audit rows are read per request.

## How "pending" is counted
Live counts, computed per request, listed only when the role can read the page the work lives on, and linked to that screen: requests and staff reports waiting for the caller's decision (never their own), the caller's own requests still waiting, their draft reports, overdue and due-in-7-days deadlines, rights of entry ending within 30 days, appraisals without an approved just compensation, closings due within 30 days or past due, and encumbrances with no cure status. Nothing is emailed.

## Help center (`/help`, the question-mark button in the top bar)
- Open to every signed-in person (no page key), because help must never be hidden by a permission. A signed-out visitor is sent to sign in.
- Content is plain data in `features/help/help-content.ts` (sections, topics, blocks), so the page, the search and the tests read one source. Left contents tree, **Search topics** (every word must match; title matches first), breadcrumb, previous/next, print.
- **What your role can do** is built from the person's own permissions; other topics are static and describe only what is built today.

## Visibility changes
Tiles with large numbers and a tone (danger/warn/info), count pills on every waiting line, charts with a text summary for screen readers, a Refresh button that shows when the numbers were loaded, and a help button in the top bar.

## Not built
Per-person goals or targets, email or push reminders, drill-down from a person to their records, exporting the team table, and editable help content (it ships with the application).
