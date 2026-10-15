# Administrator — acceptance cases

**Who:** the agency's System Administrator. Configures rules, workflows, statuses, approval chains, roles and users.
**Sees:** everything, with full control, including the Administration page. The only role that can open Configuration.
**Also run:** every case in [Everyone.md](Everyone.md) as this role.

## Jobs possible today

| Id | Job | Steps | Expected: on screen | Expected: server and data | Status | Run by |
|---|---|---|---|---|---|---|
| AT-AD-10 | Do anything on any record | For each of the ten record types: attach, open, remove | – | Every attach is 201, every file opens byte-for-byte, every removal is 204 | ✅ | E2E `AT-AD-10`; API matrix |
| AT-AD-11 | Open Configuration | Click Configuration in the sidebar | The Administration page opens | – | ✅ | E2E `AT-AD-11` |
| AT-AD-12 | Use administrator-only functions | Call the administrator endpoint | – | 200 for me; 403 for every other role | ✅ | E2E `AT-AD-12`; API `SecurityTests` |
| AT-AD-13 | See my full access | Look at my page permissions | – | All 13 pages with create, read, update and delete | ✅ | E2E `AT-AD-13` |
| AT-AD-14 | My agency's customised access applies at once, only to us | An agency's permission for a role is changed in the database; that role's next request reflects it; another agency is unaffected; a restart keeps the change | – | Immediate, isolated, and restart-safe | ✅ (data-level; no editing screen yet) | API `ResilienceTests` |
| AT-AD-25 | Set how project numbers are generated | Open Configuration. Try a pattern with no sequence, then set `{FY}-{SEQ:5}`; create a project; restore the original | The bad pattern is refused and what I typed is kept; "Next project number" shows the new shape; the new project gets a five-digit number; existing numbers are unchanged | Only an administrator can change the format (every other role 403); the sequence continues within the fiscal year and never repeats a number | ✅ | E2E `AT-AD-25`; API `ProjectApiTests`; frontend `projects.test.tsx` |
| AT-AD-22 | Configure statutory deadlines, parcel statuses and fields, and who may override step order | Open Configuration. Add a deadline rule (blank, then valid), a list custom field (bad key, then valid; key and type lock afterwards), a status (a duplicate is refused), tick an extra override role; open the workflows. Then, as a Project Manager, open a parcel | Mistakes are explained next to the field; saved items appear in their tables; seeded deadline periods are marked "Placeholder" until saved; workflows are read-only; the parcel shows the new deadline with its due date, the new field and status | Rules are per agency data, never code; unknown or always-allowed roles are refused; key and type never change; everything is restored in the test | ✅ | E2E `AT-AD-22` ×2; API `ParcelApiTests`; frontend `parcels.test.tsx`; unit `ParcelCalculationTests`, `CustomFieldValidationTests` |
| AT-AD-26 | Set the agency's time zone | Open Configuration > Time zone, choose a zone, save; then (as a Project Manager) look at a project and a parcel; give the project its own zone, then let it follow the agency again | "Saved." and today's date in that zone; the project says "follows the agency" with the zone's name; the parcel's synopsis and the deadlines note name the calendar days are counted on; a project's own zone wins and is labelled "set for this project" | One setting per agency, only an Administrator can change it (every other role 403), every role can read it; an unknown zone is refused (400); deadlines, "due today", the set-up date, the no-future-completion rule and reminders all use the project's zone, else the agency's; the day changes at that zone's own midnight, with daylight saving | ✅ | E2E `AT-AD-26`; API `ParcelApiTests`; frontend `timezone.test.tsx`; unit `AgencyTimeTests`, `ParcelTests` |
| AT-AD-27 | Use every party control, and see what the product refuses everyone | Create a party; try shares of 100.00001, a share on a lienholder and a share that overfills the tract; add an owner on a tract's parcel; save a name with markup, a null character and 201 letters, and malformed JSON; open the party (Delete is disabled while it is used), remove its link, delete it | Markup is shown as text; Delete enabled only when unused | Share errors are 400/409 with stable codes; owners on a tract's parcel 400; hostile input is stored as text or refused 400, never a 500 | ✅ | E2E `AT-AD-27`; API `PartyApiTests` |
| AT-AD-28 | Define fields and their allowed values in the Library | Open Library from the sidebar. See the defaults (16 parcel statuses with Inactive as the default; possession types PUA, Closing, Notice of Deposit, Judgement; interest types; agent and contact types; CAD number, segment, priority as text). Add a value (blank refused, then added, then a duplicate in another case refused), rename it, switch it off (it stays after reload), filter, import a CSV (new values added, existing skipped), export a CSV. Define a new field, try an existing name, add a value, try to change its type once it has values; open a field the product uses and see its type locked. Add a parcel status here | Counts show active/total; Default tag on Inactive; plain messages beside the field | The parcel status list is the real status list (neutral colour, active, not initial) and also appears on Configuration; the default status cannot be switched off; a new parcel starts as Inactive; duplicates are refused (409) even when added at once; markup is stored as text; bad input is 400; only this agency's library is reachable | ✅ | E2E `AT-AD-28` ×5; API `LibraryApiTests`; frontend `library.test.tsx`; unit `LibraryTests` |

## Jobs that arrive with later phases

| Id | Job | Phase | Success looks like |
|---|---|---|---|
| AT-AD-20 | Edit a role's page permissions | 5 | A permission edited on screen applies on that role's next request; the change is audited; other agencies unaffected |
| AT-AD-21 | Configure approval-chain thresholds | 3 | Amount bands → required roles; invalid bands (gaps, overlaps, unknown roles) are rejected with a clear message |
| AT-AD-23 | Invite, deactivate and re-activate users | 5 | An invited person activates by one-time link; a deactivated person can no longer sign in or act |
| AT-AD-24 | Maintain document and report templates | 5 | Library entries versioned and used by title and offer documents |

## Changelog
- Library: AT-AD-28 automated (fields, types, allowed values, defaults; parcel status list managed here; CSV import and export).
- Phase 1 slice 3a: AT-AD-27 automated (parties).
- Phase 1 slice 2 follow-up: AT-AD-26 automated (agency and project time zones).
- Phase 1 slice 2: AT-AD-22 automated (parcel rules).
- Phase 1 slice 1: AT-AD-25 automated (project number format).
- Initial: AT-AD-10..14 automated; 20..24 specified.
