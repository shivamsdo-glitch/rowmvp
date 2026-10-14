# Everyone — cases that apply to every signed-in person

Run once **per role** (Project Manager, Title Examiner, Appraiser, Negotiator, Finance, Administrator). Automated in `e2e/tests/everyone.spec.ts` (real browser) and, underneath, in the backend and frontend suites.

| Id | Job / situation | What the person does and sees | What the server and data must do | Status | Run by |
|---|---|---|---|---|---|
| AT-X-01 | Sign in and know who I am | Pick my role on the sign-in page; land on the Dashboard; the account menu shows my name and role | `/auth/me` returns exactly my role | ✅ | E2E `AT-X-01` ×6 roles |
| AT-X-02 | See only my work | The sidebar lists exactly the pages my role may open; screens not built yet are greyed with "Soon" and cannot be clicked; pages I may not open do not appear at all | `/auth/permissions` equals the access spec for my role | ✅ | E2E `AT-X-02` ×6; frontend `roles.test.tsx`; API `SchemaAndContractTests` (78 role×page checks) |
| AT-X-03 | Open a page I should not | Typing the Configuration address gives a clear "You don't have access" page, never a blank screen or someone else's data. Administrators get in | The server independently answers 403 to the same person | ✅ | E2E `AT-X-03` ×6; frontend `roles.test.tsx` |
| AT-X-04 | Stay signed in, and sign out for real | Reload keeps me signed in. "Log Out" returns to sign-in; the Back button and typing an address cannot get back in | The session token is gone from the browser | ✅ | E2E `AT-X-04` ×6 |
| AT-X-05 | Arrive via a link while signed out | I am sent to sign in, then land on the page the link pointed to (only a same-site address is ever honoured) | – | ✅ | E2E `AT-X-05`; frontend `session.test.tsx` |
| AT-X-06 | Old or tampered session | A stale or fake token just shows sign-in again; nothing crashes | A forged, unsigned, expired, wrong-key or wrong-audience token is always a 401 on every endpoint | ✅ | E2E `AT-X-06`; API `SecurityTests` |
| AT-X-07 | The server is unreachable | A plain message ("Cannot reach the server…"); I can try again | – | ✅ | E2E `AT-X-07`; frontend `session.test.tsx` |
| AT-X-08 | My session expires while I work | I am returned to sign-in with "Your session has expired", and after signing in land back where I was | – | ✅ | E2E `AT-X-08`; frontend `session.test.tsx` |
| AT-X-09 | Use the sidebar comfortably | Collapse to icons and expand again; links keep working and keep their names (screen readers) | – | ✅ | E2E `AT-X-09`; frontend `a11y.test.tsx` |
| AT-X-10 | Know what product this is | Tab title and top bar show "ROW Management" | – | ✅ | E2E `AT-X-10` |
| AT-X-11 | Another agency cannot see my agency's files | – | A second agency (even its Administrator, even with the same user id) gets nothing: list empty or refused, open and delete are "not found"; my own files are untouched | ✅ | E2E `AT-X-11`; API `ResilienceTests` |
| AT-X-12 | Use a tablet | The shell fits at about 820 px wide with no sideways scrolling | – | ✅ | E2E `AT-X-12` |
| AT-X-13 | Accessible to all | Automated checks (labels, landmarks, names, roles) report nothing wrong on every screen for every role | – | ✅ | frontend `a11y.test.tsx` |
| AT-X-14 | Colour contrast and visual polish | Looks right at 1920×1080 and 1366×768 | – | 👁 | Manual list in README |
| AT-X-15 | Hostile or broken input never breaks anything | – | Malformed bodies, null characters, injection text, traversal file names, oversized or dangerous uploads are clean 4xx and never a 500; the database is intact | ✅ | API `InputHardeningTests` (86 cases) |
| AT-X-16 | Many people at once | – | 20 simultaneous identical saves give one success and clean conflicts; 25 parallel uploads all land; 200 parallel reads all answer; mixed bursts from every role cause no server error | ✅ | API `ResilienceTests` |
| AT-X-17 | The system is restarted | – | A restart changes no data, keeps an administrator's edited permissions and adds back a missing page's permissions | ✅ | API `ResilienceTests` |
| AT-X-18 | The database has a bad day | – | `/health` says 503 while the app keeps answering other requests; the app refuses to start if it must migrate and cannot reach the database | ✅ | API `ResilienceTests` |
| AT-X-19 | Everything I change is on the record | – | Every upload, removal and rename is in the audit trail with who and when; file contents, password hashes and tokens never are | ✅ | API `ResilienceTests` |
| AT-X-20 | Production is locked down | – | In Production the developer sign-in, Swagger and demo data do not exist, and a token signed with the development key is worthless | ✅ | API `SecurityTests`, `ResilienceTests` |
| AT-X-21 | Real sign-in with email and password | Invitation → activate → sign in → forgot/reset password | One-time links, single use, no hint whether an email exists, old password stops working | ✅ API only (no screens yet) | API `ResilienceTests`, `AccountFlowTests` |
| AT-X-22 | Sign in with email and password on a screen, change my password, view my profile | – | – | 🕓 Pending (user management) | – |
