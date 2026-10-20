# Relocation Agent — acceptance cases

**Who:** the relocation specialist. Interviews displaced owners and tenants and records what they need.
**Sees:** full control of a parcel's Questionnaire tab; read-only on everything else a relocation agent needs, as set out in the access spec.
**Also run:** every case in [Everyone.md](Everyone.md) as this role.

## Jobs possible today

| Id | Job | Steps | Expected: on screen | Expected: server and data | Status | Run by |
|---|---|---|---|---|---|---|
| AT-RA-40 | Keep the relocation questionnaires of a parcel | Open a parcel's Questionnaire tab. Try to add without a relocation type, then pick one, set a due date, add an ownership interest and a question with its answer, add the questionnaire, attach the answers, change the type, remove a question, remove the questionnaire | Each questionnaire gets an ID; the list shows type, dates and number of questions; the attachments panel appears on the edit form | Ten adds at once are numbered one to ten without a gap or repeat; a missing type, an empty name or question and more than 50 rows are refused (400); the grids are replaced as sent | ✅ | E2E `AT-RA-40`; API `QuestionnaireApiTests`; frontend `parcel-tabs.test.tsx`; unit `QuestionnaireTests` |
| AT-RA-41 | Keep a displacee's relocation on a parcel | Open a parcel's Relocation tab. Try to add with nothing filled in, then enter the relocation number, displacee name and type, a GIN delivered before it was mailed, two primary contacts, an eligibility row, a contact log entry and a status note; add, attach the plan, change the target vacate date, remove it | Each relocation is listed by its number with the displacee and type; the required fields and the backwards date are shown as errors; the vested owner name is derived | The relocation number is unique on the parcel (409); a missing number, name or type, a second primary contact, a negative claim limit and more than 50 rows are refused (400); the grids are replaced as sent | ✅ | E2E `AT-RA-41`; API `RelocationApiTests`; frontend `parcel-tabs.test.tsx`; unit `LifecycleTabsTests` |

## Changelog
- Phase 1.5 slice 8c: AT-RA-41 automated (the Relocation tab). ADR 0020.
- Phase 1.5 slice 7b: AT-RA-40 automated (the Questionnaire tab). ADR 0019.
