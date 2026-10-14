import { test } from '@playwright/test'

/**
 * Jobs each user will do once their phase ships. They are listed here as tracked placeholders so they
 * show up as "skipped" in every report and cannot be forgotten. When a phase delivers a job:
 *   1. replace `test.fixme` with a real journey (UI + API) in jobs.spec.ts,
 *   2. flip its status in vault/acceptance/<Role>.md from Pending to Automated,
 *   3. the phase is not done until every case for every role in that phase is green.
 */
const pending: Record<string, [id: string, job: string][]> = {
  'Project Manager': [
    ['AT-PM-20', 'Phase 1: creates a project with a unique ID, region/district, funding and a versioned estimate'],
    ['AT-PM-21', 'Phase 1: adds a parent tract and splits it into parcels, with remnant'],
    ['AT-PM-22', 'Phase 1: adds owners and ownership interests to a parcel'],
    ['AT-PM-23', 'Phase 1: sets milestones and sees roll-up counts for the project'],
    ['AT-PM-24', 'Phase 1: is blocked by the step gate; overriding needs a reason'],
    ['AT-PM-25', 'Phase 1: updates the status of many parcels at once'],
    ['AT-PM-26', 'Phase 3: approves just compensation inside their approval band'],
    ['AT-PM-27', 'Phase 5: sees the pipeline, budget and overdue items on the dashboard'],
  ],
  'Title Examiner': [
    ['AT-TE-20', 'Phase 2: orders title, receives it and completes QC'],
    ['AT-TE-21', 'Phase 2: logs defects and encumbrances and clears them'],
    ['AT-TE-22', 'Phase 2: requests a survey, attaches CAD files, reviews the legal and sketch'],
    ['AT-TE-23', 'Phase 2: completes the appraisal package; an incomplete package blocks the appraisal request'],
    ['AT-TE-24', 'Phase 2: confidential title fields are hidden from other roles'],
  ],
  Appraiser: [
    ['AT-AP-20', 'Phase 3: receives an appraisal request and is assigned the parcel'],
    ['AT-AP-21', 'Phase 3: submits an appraisal with type, dates and valuation'],
    ['AT-AP-22', 'Phase 3: reviews an appraisal; rejection needs a justification'],
    ['AT-AP-23', 'Phase 3: establishes just compensation through the approval chain for its amount'],
    ['AT-AP-24', 'Phase 3: an approved appraisal cannot be edited; a revision creates a new version'],
  ],
  Negotiator: [
    ['AT-NG-20', 'Phase 4: builds an offer package from approved just compensation and delivers it'],
    ['AT-NG-21', 'Phase 4: records a counter-offer and the settlement analysis'],
    ['AT-NG-22', 'Phase 4: an administrative settlement needs a justification and approval'],
    ['AT-NG-23', 'Phase 4: sends the agreement through its approval chain and records the signature'],
    ['AT-NG-24', 'Phase 4: completes the closing checklist until the parcel is Closed'],
    ['AT-NG-25', 'Phase 4: records an impasse, which blocks closing'],
  ],
  Finance: [
    ['AT-FN-20', 'Phase 4: creates a payment request; multi-payee splits must add up to the total'],
    ['AT-FN-21', 'Phase 4: approves a payment per threshold; cannot approve their own'],
    ['AT-FN-22', 'Phase 4: marks a payment paid and the parcel closes'],
    ['AT-FN-23', 'Phase 4: works the queue of pending payments'],
  ],
  Administrator: [
    ['AT-AD-20', 'Phase 5: edits a role’s page permissions and the change applies on the next request'],
    ['AT-AD-21', 'Phase 3: configures approval-chain thresholds'],
    ['AT-AD-22', 'Phase 1: configures statutory deadlines and the workflow per acquisition type'],
    ['AT-AD-23', 'Phase 5: invites, deactivates and re-activates users'],
    ['AT-AD-24', 'Phase 5: maintains document and report templates in the Library'],
  ],
}

for (const [role, jobs] of Object.entries(pending)) {
  test.describe(`${role} · pending`, () => {
    for (const [id, job] of jobs) test.fixme(`${id} ${job}`, async () => {})
  })
}
