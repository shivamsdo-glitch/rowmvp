#!/usr/bin/env node
// Seeds a rich DEMO portfolio so the dashboards have something to show: 8 projects, ~90 parcels at every step of the workflow, appraisals,
// negotiations, closings, contact logs, requests, staff reports and rights of entry, done by different people through the real API (so every
// record is audited to the person who made it), then moves the timestamps back so parcels have realistic ages, some are stuck, and the
// activity charts have a history.
//
//   node scripts/seed-demo-data.mjs                 seed the Development API on http://localhost:5100 and database row_management
//   node scripts/seed-demo-data.mjs --force         add the portfolio again even if it is already there
//   node scripts/seed-demo-data.mjs --resume        finish an interrupted run (requests and timestamps) without adding the portfolio again
//   node scripts/seed-demo-data.mjs --api http://localhost:5101 --db row_e2e --container row-mvp-postgres
//
// Needs: the API running in Development with the demo users (password qwertyuiop), Docker with the Postgres container (to move timestamps back).
// Development only: nothing in the product reads this script. Run the API with RateLimiting__WritePermitLimit=100000 and
// RateLimiting__AuthPermitLimit=100000 for a quick run; otherwise the script waits out the limits.
import { execFileSync } from 'node:child_process'

const arg = (name, fallback) => { const i = process.argv.indexOf(`--${name}`); return i > -1 ? process.argv[i + 1] : fallback }
const API = arg('api', 'http://localhost:5100')
const DB = arg('db', 'row_management')
const CONTAINER = arg('container', 'row-mvp-postgres')
const FORCE = process.argv.includes('--force')
const RESUME = process.argv.includes('--resume') // the portfolio is in, but a run stopped before the requests and timestamps: finish those
const PASSWORD = 'qwertyuiop'
const started = new Date()

// ── a small deterministic random generator, so two runs tell the same story ─────────────────────────────────
let seed = 20261022
const rnd = () => { seed |= 0; seed = (seed + 0x6D2B79F5) | 0; let t = Math.imul(seed ^ (seed >>> 15), 1 | seed); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296 }
const between = (a, b) => a + rnd() * (b - a)
const int = (a, b) => Math.floor(between(a, b + 1))
const pick = (list) => list[int(0, list.length - 1)]
const chance = (p) => rnd() < p
const pickWeighted = (weights) => { const total = weights.reduce((a, b) => a + b, 0); let r = rnd() * total; for (let i = 0; i < weights.length; i++) { r -= weights[i]; if (r <= 0) return i } return weights.length - 1 }
const day = (offset) => new Date(started.getTime() + offset * 86_400_000).toISOString().slice(0, 10)
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

// ── talking to the API ────────────────────────────────────────────────────────────────────────────────────────
async function call(token, method, path, body, { allow = [] } = {}) {
  for (let attempt = 1; ; attempt++) {
    const res = await fetch(API + path, {
      method, headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) }, body: body === undefined ? undefined : JSON.stringify(body),
    })
    if (res.status === 429 && attempt < 40) { await sleep(Number(res.headers.get('retry-after') ?? 5) * 1000 + 250); continue }
    const text = await res.text()
    if (!res.ok && !allow.includes(res.status)) throw new Error(`${method} ${path} -> ${res.status} ${text.slice(0, 300)}`)
    return text ? JSON.parse(text) : null
  }
}

const people = {}
async function login(key, username) {
  const r = await call(null, 'POST', '/api/v1/auth/login', { username, password: PASSWORD })
  people[key] = { token: r.token, id: r.user.id, name: r.user.fullName }
}

const psql = (sql, tuplesOnly = false) => execFileSync('docker', ['exec', '-i', CONTAINER, 'psql', '-U', 'row', '-d', DB, '-v', 'ON_ERROR_STOP=1', '-q', ...(tuplesOnly ? ['-At'] : [])], { input: sql, encoding: 'utf8' })

// ── the story ─────────────────────────────────────────────────────────────────────────────────────────────────
const STEPS = ['setup', 'title_survey', 'appraisal', 'offer', 'negotiation', 'agreement', 'closing']
const MEAN_DAYS = { setup: 6, title_survey: 14, appraisal: 34, offer: 9, negotiation: 30, agreement: 12, closing: 16 }

// weights for where a parcel is: setup, title, appraisal, offer, negotiation, agreement, closing, CLOSED
const PROJECTS = [
  { name: 'I-35 Capacity Expansion', oversight: 'Federal', phase: 'Phase 2', region: 'Central', district: 'District 4', parcels: 14, clear: -20, land: 4_800_000, perParcel: 330_000, spread: [0, 1, 3, 2, 4, 2, 1, 1], slow: { appraisal: 1.8 }, over: 1.12, overdue: 0.45 },
  { name: 'SR-71 Bridge Replacement', oversight: 'State', phase: 'Phase 3', region: 'East', district: 'District 2', parcels: 8, clear: 45, land: 1_900_000, perParcel: 215_000, spread: [0, 0, 0, 1, 1, 1, 1, 4], slow: {}, over: 0.97, overdue: 0.05 },
  { name: 'FM-1826 Safety Upgrade', oversight: 'Local', phase: 'Phase 1', region: 'South', district: 'District 6', parcels: 10, clear: 200, land: 980_000, perParcel: 95_000, spread: [3, 3, 2, 1, 1, 0, 0, 0], slow: {}, over: 0.95, overdue: 0.1 },
  { name: 'Downtown Transit Corridor', oversight: 'Federal', phase: 'Phase 2', region: 'Central', district: 'District 1', parcels: 16, clear: 60, land: 6_200_000, perParcel: 410_000, spread: [1, 2, 6, 3, 2, 1, 1, 0], slow: { appraisal: 2.4, offer: 1.5 }, over: 1.18, overdue: 0.3 },
  { name: 'US-290 Interchange', oversight: 'Federal', phase: 'Phase 4', region: 'West', district: 'District 3', parcels: 12, clear: -90, land: 3_100_000, perParcel: 255_000, spread: [0, 0, 0, 0, 1, 1, 1, 9], slow: { negotiation: 1.4 }, over: 1.02, overdue: 0.15 },
  { name: 'Riverside Trail Connector', oversight: 'Local', phase: 'Phase 1', region: 'North', district: 'District 5', parcels: 6, clear: null, land: 420_000, perParcel: 68_000, spread: [2, 2, 1, 1, 0, 0, 0, 0], slow: {}, over: 0.9, overdue: 0.1 },
  { name: 'Airport Access Road', oversight: 'State', phase: 'Phase 2', region: 'East', district: 'District 2', parcels: 11, clear: 120, land: 2_500_000, perParcel: 235_000, spread: [0, 1, 1, 2, 5, 1, 1, 0], slow: { negotiation: 2.1 }, over: 1.05, overdue: 0.25 },
  { name: 'Harbor Rd Drainage', oversight: 'Local', phase: 'Phase 3', region: 'South', district: 'District 6', parcels: 5, clear: 15, land: 730_000, perParcel: 130_000, spread: [0, 0, 1, 1, 2, 1, 0, 0], slow: { title_survey: 1.6 }, over: 1.0, overdue: 0.2 },
]

const FIRST = ['Maria', 'James', 'Linda', 'Robert', 'Patricia', 'Michael', 'Barbara', 'William', 'Elizabeth', 'David', 'Jennifer', 'Richard', 'Susan', 'Joseph', 'Karen', 'Thomas', 'Nancy', 'Charles', 'Lisa', 'Daniel', 'Betty', 'Matthew', 'Helen', 'Anthony', 'Sandra']
const LAST = ['Garcia', 'Johnson', 'Nguyen', 'Williams', 'Martinez', 'Brown', 'Davis', 'Rodriguez', 'Wilson', 'Anderson', 'Thomas', 'Taylor', 'Moore', 'Jackson', 'Martin', 'Lee', 'Perez', 'Thompson', 'White', 'Harris', 'Clark', 'Lewis', 'Walker', 'Hall', 'Young']
const STREETS = ['Harbor Rd', 'Oak St', 'Maple Ave', 'River Rd', 'Cedar Ln', 'Mill St', 'Pine Dr', 'Lakeview Blvd', 'Industrial Pkwy', 'Church St', 'Market St', 'Ranch Rd 12', 'County Rd 41']
const AGENTS = ['Casey Closer', 'Morgan Reyes', 'Priya Natarajan', 'Jordan Blake', 'Sam Okafor']
const APPRAISERS = ['Ann Appraiser', 'Marcus Hale', 'Dana Whitfield']
const TITLE_COS = ['Lone Star Title', 'Heritage Title Co.', 'Capitol Abstract', 'Gulf Coast Title']
const owner = () => `${pick(FIRST)} ${pick(LAST)}${chance(0.18) ? ' Family Trust' : ''}`

async function main() {
  console.log(`Seeding ${API} (database ${DB})…`)
  for (const [key, username] of Object.entries({ pm: 'rowprojectmanager', designer: 'rowdesign', surveyor: 'surveyor', title: 'titleresearcher', appraiser: 'appraiser', approver: 'appraisalapprover', neg: 'rownegotiator', finance: 'rowfinance', admin: 'admin' })) await login(key, username)

  const existing = await call(people.admin.token, 'GET', '/api/v1/projects?pageSize=200')
  if (!FORCE && !RESUME && (existing.items ?? []).some((p) => PROJECTS.some((x) => x.name === p.name))) {
    console.log('The demo portfolio is already there. Use --force to add it again.')
    return
  }
  const statuses = Object.fromEntries((await call(people.admin.token, 'GET', '/api/v1/parcel-rules/statuses')).map((s) => [s.name, s.id]))
  const statusFor = (step, closed, impasse) => impasse && statuses.Impasse ? statuses.Impasse : closed ? statuses.Closed : ({ setup: statuses.Identified, title_survey: statuses['Title & survey'], appraisal: statuses.Appraisal, offer: statuses.Negotiation, negotiation: statuses.Negotiation, agreement: statuses['Agreement signed'], closing: statuses['Agreement signed'] })[step]

  const parcelPlans = [] // for the timestamp pass
  let parcelCount = 0
  for (const spec of RESUME ? [] : PROJECTS) {
    const pm = people.pm.token
    const project = await call(pm, 'POST', '/api/v1/projects', {
      name: spec.name, oversightType: spec.oversight, phase: spec.phase, region: spec.region, district: spec.district, description: `${spec.name}: right of way for the ${spec.phase.toLowerCase()} improvements.`,
      fundingSource: spec.oversight === 'Federal' ? 'FHWA STBG' : spec.oversight === 'State' ? 'State Highway Fund' : 'Local bond', fundingAmount: Math.round(spec.land * 1.25),
      rightOfWayClearDate: spec.clear === null ? null : day(spec.clear), lettingDate: spec.clear === null ? null : day(spec.clear + 75),
    })
    if (spec.oversight !== 'Local') {
      await call(pm, 'PUT', `/api/v1/projects/${project.id}/nepa`, { decisionType: 'CategoricalExclusion', decisionDate: day(-400) })
      await call(pm, 'POST', `/api/v1/projects/${project.id}/authorizations`, { type: 'Acquisition', authorizedAmount: Math.round(spec.land * 1.1), federalSharePercent: 80, authorizedOn: day(-380), programmingConfirmed: true })
    }
    await call(pm, 'POST', `/api/v1/projects/${project.id}/cost-estimates`, {
      landCost: Math.round(spec.land * 0.8), improvementsCost: Math.round(spec.land * 0.1), relocationCost: Math.round(spec.land * 0.06), indirectCost: Math.round(spec.land * 0.04), notes: 'Programming estimate',
    })
    console.log(`  ${project.number} ${spec.name}`)

    for (let n = 0; n < spec.parcels; n++) {
      const where = pickWeighted(spec.spread) // 0..6 = step index, 7 = closed
      const closed = where === 7
      const stage = closed ? 6 : where
      const step = STEPS[stage]
      const impasse = !closed && stage >= 4 && chance(0.12)
      const parcel = await call(pm, 'POST', '/api/v1/parcels', {
        projectId: project.id, areaSqFt: int(2_000, 90_000), acquisitionType: 'Standard', address: `${int(100, 9800)} ${pick(STREETS)}`,
        propertyType: pick(['Residential', 'Commercial', 'Agricultural', 'Vacant land']), statusId: statusFor(step, closed, impasse), priority: pick(['High', 'Medium', 'Medium', 'Low']), segment: `Segment ${int(1, 4)}`,
      })
      parcelCount++
      for (let k = 1; k <= stage; k++) await call(pm, 'POST', `/api/v1/parcels/${parcel.id}/advance`, { targetStepKey: STEPS[k] })
      await call(people.designer.token, 'POST', `/api/v1/parcels/${parcel.id}/parties`, { newParty: { kind: 'Person', name: owner() }, role: 'FeeOwner', ownerType: 'Fee Owner', leaseOnFile: false })
      if (stage >= 1 && chance(0.25)) await call(people.surveyor.token, 'POST', `/api/v1/parcels/${parcel.id}/easements`, { number: `E-${int(100, 999)}` })
      if (stage >= 1 && chance(0.35)) await call(people.title.token, 'POST', `/api/v1/parcels/${parcel.id}/encumbrances`, { encumbranceType: pick(['Lien', 'Easement', 'Deed of Trust']), documentTitle: pick(['Deed of Trust', 'Utility easement', 'Mechanic lien', 'Drainage easement']), grantor: owner(), grantee: pick(TITLE_COS) })

      // Notice to owner and the offer clock (ROW-074): a share of the early parcels have waited too long for an offer.
      let overdue = false
      if (stage <= 2 && chance(spec.overdue)) { await call(pm, 'PUT', `/api/v1/parcels/${parcel.id}/notice`, { issuedOn: day(-int(100, 170)), method: 'CertifiedMail' }); overdue = true }
      else if (stage === 3 && chance(spec.overdue)) { await call(pm, 'PUT', `/api/v1/parcels/${parcel.id}/notice`, { issuedOn: day(-int(100, 150)), method: 'CertifiedMail' }); overdue = true }
      else if (stage >= 4) {
        await call(pm, 'PUT', `/api/v1/parcels/${parcel.id}/notice`, { issuedOn: day(-int(70, 200)), method: pick(['CertifiedMail', 'HandDelivery', 'Email']) })
        const detail = await call(pm, 'GET', `/api/v1/parcels/${parcel.id}`)
        const offer = detail.milestones.find((m) => m.name === 'Offer delivered')
        await call(pm, 'PUT', `/api/v1/parcels/${parcel.id}/milestones/${offer.id}`, { name: offer.name, dueOn: offer.dueOn, completedOn: day(-int(20, 120)) })
      } else if (chance(0.5)) await call(pm, 'PUT', `/api/v1/parcels/${parcel.id}/notice`, { issuedOn: day(-int(5, 60)), method: 'FirstClassMail' })

      // Appraisal (the appraiser) and its approval (the approver).
      let approved = null
      let total = null
      if (stage >= 2) {
        const base = spec.perParcel * between(0.55, 1.5)
        const land = Math.round(base * 0.82 / 100) * 100
        const damages = Math.round(base * 0.18 / 100) * 100
        total = land + damages
        const body = { appraisalType: pick(['Full Appraisal', 'Full Appraisal', 'Waiver Valuation']), appraiser: pick(APPRAISERS), landPlannerRequired: false, assignedTo: pick(AGENTS), requestedDate: day(-int(60, 200)), dueDate: day(-int(5, 40)), amounts: [{ amountType: 'Land', description: 'Fee simple', amount: land }, { amountType: 'Damages', description: null, amount: damages }] }
        const made = await call(people.appraiser.token, 'POST', `/api/v1/parcels/${parcel.id}/appraisals`, body)
        if (stage >= 3 || (stage === 2 && chance(0.25))) {
          approved = Math.round(total * between(0.93, 1.08) / 50) * 50
          await call(people.approver.token, 'PUT', `/api/v1/parcels/${parcel.id}/appraisals/${made.id}`, { ...body, reviewAppraiser: 'Rita Reviewer', reviewType: 'Desk Review', approvedJustCompensation: approved, reviewCompletedDate: day(-int(3, 30)), reviewSubmittedDate: day(-int(4, 31)), reviewRecommendation: 'Approve', reviewRecommendedValue: approved })
        }
      }
      // Negotiation (the negotiator): an agreed amount once an agreement is reached.
      if (stage >= 4) {
        const agreed = stage >= 5 && approved ? Math.round(approved * between(0.98, 1.16) * spec.over / 50) * 50 : null
        await call(people.neg.token, 'PUT', `/api/v1/parcels/${parcel.id}/negotiation`, { assignedTo: pick(AGENTS), totalAgreementAmount: agreed, notes: impasse ? 'Owner has rejected the last two offers.' : pick(['Owner is open to a deal.', 'Counsel is reviewing the offer.', 'Awaiting owner response.', 'Site visit completed; owner asked for a higher figure.']) })
        for (let c = 0, count = int(1, 4); c < count; c++) await call(people.neg.token, 'POST', `/api/v1/parcels/${parcel.id}/contact-log`, { contactedAt: new Date(started.getTime() - int(1, 90) * 86_400_000).toISOString(), mode: pick(['Phone', 'Email', 'In person', 'Letter']), contactedBy: people.neg.name, contactPerson: owner(), notes: pick(['Discussed the offer.', 'Left a voicemail.', 'Owner requested a copy of the appraisal.', 'Scheduled a meeting.', 'Owner has questions about relocation.']) })
      }
      if (stage >= 6) {
        await call(people.neg.token, 'PUT', `/api/v1/parcels/${parcel.id}/title-closing`, { titleCompany: pick(TITLE_COS), titleClosingAgent: pick(AGENTS), anticipatedClosingDate: closed ? day(-int(10, 120)) : day(int(-25, 40)), actualClosingDate: closed ? day(-int(5, 100)) : null })
      }
      if (stage >= 2 && chance(0.12)) await call(people.neg.token, 'POST', `/api/v1/parcels/${parcel.id}/rights-of-entry`, { status: 'Granted', requestedOn: day(-100), grantedOn: day(-80), expiresOn: day(int(4, 28)), grantedBy: owner(), purpose: pick(['Survey', 'Appraisal access', 'Environmental testing']) })
      if (chance(0.1)) {
        const report = await call(people.pm.token, 'POST', `/api/v1/parcels/${parcel.id}/staff-reports`, { kind: 'StaffReport', title: `Staff report for parcel ${parcel.number}`, body: 'Summary of the acquisition, comparable sales and the recommended offer.' })
        if (chance(0.6)) await call(people.pm.token, 'POST', `/api/v1/parcels/${parcel.id}/staff-reports/${report.id}/submit`, {})
      }
      parcelPlans.push({ id: parcel.id, stage, closed, step, spec, impasse, overdue })
    }
  }

  if (RESUME) {
    const rows = JSON.parse(psql(`SELECT coalesce(json_agg(r), '[]') FROM (SELECT p.id, p.current_step_key AS step, pr.name AS project,
        (EXISTS (SELECT 1 FROM parcel_title_closings t WHERE t.parcel_id = p.id AND t.actual_closing_date IS NOT NULL)) AS closed,
        (SELECT s.name FROM parcel_statuses s WHERE s.id = p.status_id) AS status
      FROM parcels p JOIN projects pr ON pr.id = p.project_id WHERE pr.name IN (${PROJECTS.map((x) => `'${x.name}'`).join(',')})) r;`, true))
    for (const r of rows) parcelPlans.push({ id: r.id, stage: STEPS.indexOf(r.step), closed: r.closed, step: r.step, spec: PROJECTS.find((x) => x.name === r.project), impasse: r.status === 'Impasse', overdue: false })
    parcelCount = rows.length
  }

  // Requests: submitted by two people, some decided by finance.
  const requests = []
  for (let i = 0; i < 9; i++) {
    const by = i % 3 === 0 ? people.admin : people.pm
    requests.push(await call(by.token, 'POST', '/api/v1/acquisition-requests', { requesterType: i % 4 === 0 ? 'External' : 'Internal', requesterName: pick(['Design team', 'Utilities coordination', 'City of Riverside', 'County Roads']), requesterEmail: 'requests@example.com', property: `${int(100, 9800)} ${pick(STREETS)} (${pick(['strip take', 'full take', 'temporary easement'])})`, desiredParameters: 'Fee simple, 40 ft strip', estimatedAmount: int(30, 600) * 1000 }))
  }
  for (const [i, r] of requests.entries()) {
    if (i % 3 === 1) await call(people.finance.token, 'POST', `/api/v1/acquisition-requests/${r.id}/approve`, { note: 'Funded from the current program.' })
    else if (i === 5) await call(people.finance.token, 'POST', `/api/v1/acquisition-requests/${r.id}/reject`, { note: 'Out of the funded corridor.' })
  }

  // ── move the timestamps back so the portfolio has a history ─────────────────────────────────────────────────
  console.log('Giving parcels realistic ages…')
  const now = Date.now()
  const iso = (ms) => new Date(ms).toISOString()
  let sql = 'BEGIN;\n'
  for (const p of parcelPlans) {
    const slow = p.spec.slow
    const dur = (k) => MEAN_DAYS[STEPS[k]] * (slow[STEPS[k]] ?? 1) * between(0.5, 1.6)
    const mean = MEAN_DAYS[p.step] * (slow[p.step] ?? 1)
    const stuck = !p.closed && chance(p.impasse ? 0.9 : 0.2)
    const openDays = p.closed ? between(3, 60) : stuck ? between(32, 95) : between(0.5, Math.min(28, mean * 1.2))
    let start = now - openDays * 86_400_000
    const starts = new Array(p.stage + 1)
    starts[p.stage] = start
    for (let k = p.stage - 1; k >= 0; k--) { start -= dur(k) * 86_400_000; starts[k] = start }
    sql += `UPDATE parcels SET created_at='${iso(starts[0])}', updated_at='${iso(starts[p.stage])}' WHERE id='${p.id}';\n`
    for (let k = 1; k <= p.stage; k++) sql += `UPDATE parcel_step_events SET created_at='${iso(starts[k])}', updated_at='${iso(starts[k])}' WHERE parcel_id='${p.id}' AND to_step_key='${STEPS[k]}';\n`
  }
  // The audit trail: everything this script did spreads over the last 30 days, busier lately, so "what each person did" has a history.
  sql += `UPDATE audit_log SET changed_at = '${iso(started.getTime())}'::timestamptz - (power(random(), 1.6) * interval '30 days') WHERE changed_at >= '${iso(started.getTime() - 5000)}';\n`
  sql += 'COMMIT;\n'
  psql(sql)

  const closedCount = parcelPlans.filter((p) => p.closed).length
  console.log(`\nDone: ${PROJECTS.length} projects, ${parcelCount} parcels (${closedCount} closed), ${requests.length} requests.`)
  console.log('Sign in as rowprojectmanager or admin (password qwertyuiop) and open the dashboard.')
}

main().catch((e) => { console.error(`\nSeeding stopped: ${e.message}`); process.exit(1) })
