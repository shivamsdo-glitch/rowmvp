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

## Jobs that arrive with later phases

| Id | Job | Phase | Success looks like |
|---|---|---|---|
| AT-AD-20 | Edit a role's page permissions | 5 | A permission edited on screen applies on that role's next request; the change is audited; other agencies unaffected |
| AT-AD-21 | Configure approval-chain thresholds | 3 | Amount bands → required roles; invalid bands (gaps, overlaps, unknown roles) are rejected with a clear message |
| AT-AD-22 | Configure statutory deadlines and the workflow per acquisition type | 1 | Rules saved per agency and used for deadlines and step gating |
| AT-AD-23 | Invite, deactivate and re-activate users | 5 | An invited person activates by one-time link; a deactivated person can no longer sign in or act |
| AT-AD-24 | Maintain document and report templates | 5 | Library entries versioned and used by title and offer documents |

## Changelog
- Phase 1 slice 1: AT-AD-25 automated (project number format).
- Initial: AT-AD-10..14 automated; 20..24 specified.
