# Phase 3 — Appraisal & Just Compensation

**Source diagram:** Diagram 3. **Primary roles:** Appraiser, ProjectManager (approving official), Admin.

## Scope
- Intake from title package + request for appraisal (248); ROW map adequacy check before partial-take appraisal (249); scoping per parent tract + pre-appraisal planning meeting (250).
- Assignment & tracking, internal/external appraiser (042); appraisal data capture — type, dates, valuation, basis (043); multiple appraisals per parcel with history & change documentation (045); linked to parcel and proposed acquisition (048); document storage with versions + audit log (047).
- Owner invitation to accompany appraiser + response record (251); specialty/cure report request, review, approval (252).
- **Review workflow** with accept/reject + justification (044); Reviewer Determination of value (253); uneconomic remnant determination (254).
- **Just-compensation approval** (046; electronic) through the Phase 0 approval chain, thresholds configurable; **`JustCompensation.Compute`** (parts + damages/cures − benefits rules per source) and `AppraisalVariance` (reviewer vs. appraiser) as Calculations with tests.
- Approved appraisal hand-off to Title and the agent (255): status change + notification record; unlocks Phase 4 offer creation.

**Frontend:** appraisal queue, parcel Appraisal tab (assign, submit, review, approve), approval timeline, compensation breakdown view.

## Requirements
ROW-042–048, 248–255.

## Open scope question (needs your call)
ROW-049–062 fall *between* your listed ranges but are appraisal-domain. Proposed: include **049 owner-submitted appraisals** and **053 waiver valuations** (core URA paths, cheap on this model) as stretch items; defer 050–052, 054–062 (templates, scoring, payments to appraisers, comps DB, GIS drill-through).

## Exit checklist
- [ ] Request → assign → submit → review → determine value → approve, with correct approver set by amount
- [ ] Approved JC is immutable; revisions create a new appraisal version
- [ ] Rejection requires justification; history retained
- [ ] Calculations covered by unit tests incl. threshold boundaries
