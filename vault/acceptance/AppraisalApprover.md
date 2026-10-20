# Appraisal Approver — acceptance cases

**Who:** the approving official for appraisals. Reviews appraisals entered by the Appraiser and establishes the approved just compensation.
**Sees:** read and update on a parcel's Appraisal tab (not add or remove); the approved just compensation and the review block are theirs alone to change (with Admin). Read-only elsewhere, as set out in the access spec.
**Also run:** every case in [Everyone.md](Everyone.md) as this role.

## Jobs possible today

| Id | Job | Steps | Expected: on screen | Expected: server and data | Status | Run by |
|---|---|---|---|---|---|---|
| AT-AA-40 | Review an appraisal and set the approved just compensation | Open a parcel's Appraisal tab and edit an appraisal. Try a negative approved amount, then enter the approved just compensation and the review completed date and save. Sign in as the Appraiser and open the same appraisal | The negative amount is refused on the form; the approved amount shows in the list; for the Appraiser the whole review section is read-only | Only the Appraisal Approver and Admin can change the review fields; the Appraiser changing them gets 403 `appraisal_review_restricted` and the value is unchanged; the Appraiser may still save the appraisal sending the review back as it is | ✅ | E2E `AT-AA-40` (two tests); API `AppraisalApiTests`; frontend `parcel-tabs.test.tsx`; unit `AppraisalTests` |

## Changelog
- Phase 1.5 slice 6c: AT-AA-40 automated (the appraisal review). ADR 0018.
