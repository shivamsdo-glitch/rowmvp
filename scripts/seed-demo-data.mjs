#!/usr/bin/env node
// Seeds a rich DEMO portfolio so the dashboards have something to show: 8 projects, ~90 parcels at every step of the workflow, appraisals,
// negotiations, closings, contact logs, requests, staff reports and rights of entry, done by different people through the real API (so every
// record is audited to the person who made it), then moves the timestamps back so parcels have realistic ages, some are stuck, and the
// activity charts have a history.
//
//   node scripts/seed-demo-data.mjs                 seed the Development API on http://localhost:5100 and database row_management
//   node scripts/seed-demo-data.mjs --force         add the portfolio again even if it is already there
//   node scripts/seed-demo-data.mjs --enrich                fill every tab of the parcels already there (and the Library's Agents list); adds no projects
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
const ENRICH = process.argv.includes('--enrich') // fill in every tab of the parcels that are already there (an earlier, thinner run), then stop
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

// ── rich records: every tab of a parcel, filled from the Library's values ────────────────────────────────────────
// One code path for a fresh run and for `--enrich` (which fills in parcels an earlier, thinner run left half empty): lists are matched by position
// and updated in place when they already exist, single records are saved over, so running it twice gives the same result.
const SUBDIVISIONS = ['Oak Hollow', 'Mill Creek', 'Riverbend', 'Cedar Park Estates', 'Lakeview Terrace', 'Heritage Ranch']
const CITIES = [['Austin', 'TX', '787'], ['Round Rock', 'TX', '786'], ['Pflugerville', 'TX', '786'], ['Manor', 'TX', '786'], ['Buda', 'TX', '786']]
const FIRMS = ['Marsh & Lowe LLP', 'Salazar Land Law', 'Brightwater Legal', 'Keller Property Counsel']
const BANKS = ['Capital Ridge Bank', 'First Texas Savings', 'Lone Star Credit Union', 'Frontier Mortgage Co.']
// Values of the Library's lists (backend Domain/Common/DefaultLibrary.cs): what the dropdowns offer.
const LIB = {
  interestTypes: ['Full Fee', 'Partial Fee', 'Permanent Easement', 'Temporary Construction Easement', 'Drainage Easement', 'Public Utility Easement', 'Sidewalk Easement'],
  possession: ['PUA', 'Closing', 'Notice of Deposit', 'Judgement'],
  easementTypes: ['Permanent Easement', 'Temporary Construction Easement', 'Drainage Easement', 'Public Utility Easement', 'Access Easement', 'Slope Easement', 'Sidewalk Easement'],
  encumbrances: [['Mortgage Lien', 'Deed of Trust'], ['Easement - Public Utility', 'Utility easement'], ['Mechanic Lien', 'Mechanic lien'], ['Easement - Access', 'Access easement'], ['Deed of Trust', 'Deed of Trust'], ['Restrictive Covenants', 'Declaration of covenants'], ['Lease', 'Ground lease']],
  cure: ['Not started', 'In progress', 'Cured', 'Waived'],
  modes: ['Phone', 'In person', 'Email', 'Letter', 'Certified mail', 'Site visit'],
  conveyance: ['Warranty Deed', 'Special Warranty Deed', 'Easement Deed', 'Right-of-Way Deed'],
}
const PEOPLE = [
  ['Dana Whitfield', 'Project Lead', 'Agency ROW office'], ['Devon Park', 'Row Designer', 'Agency Design Group'], ['Elena Voss', 'Row Designer', 'Agency Design Group'],
  ['Sam Okafor', 'Surveyor', 'Okafor Survey Co.'], ['Wei Chen', 'Surveyor', 'Pioneer Surveying'], ['Tamara Quinn', 'Title Researcher', 'Capitol Abstract'], ['Isaac Brooks', 'Title Researcher', 'Lone Star Title'],
  ['Priya Natarajan', 'Appraisal Coordinator', 'Agency ROW office'], ['Ann Appraiser', 'Appraiser', 'Cohen Valuation'], ['Marcus Hale', 'Appraiser', 'Hale & Partners Appraisal'], ['Allison Jackson', 'Appraiser', 'CBRE, Inc.'],
  ['Rita Reviewer', 'Appraisal Reviewer', 'Agency ROW office'], ['Casey Closer', 'Negotiator', 'Agency ROW office'], ['Morgan Reyes', 'Negotiator', 'Okafor Land Services'], ['Riley Okafor', 'Negotiator', 'Okafor Land Services'],
  ['Jordan Blake', 'Broker', 'Blake Realty Group'], ['Helen Marsh', 'Counsel', 'Marsh & Lowe LLP'], ['Victor Salazar', 'Counsel', 'Salazar Land Law'], ['Lena Ortiz', 'Relocation', 'Agency Relocation Unit'],
  ['Marcus Bell', 'Relocation', 'Agency Relocation Unit'], ['Grace Tan', 'FHWA Representative', 'FHWA Texas Division'], ['Noor Haddad', 'Land Planner', 'Haddad Planning'],
  ['Owen Price', 'Community Engagement', 'Agency Outreach Office'], ['Felix Mora', 'Site Work', 'Mora Site Services'],
].map(([name, type, company], i) => ({ name, type, company, phone: `(512) 555-${String(1100 + i * 37).padStart(4, '0')}`, email: `${name.toLowerCase().replace(/[^a-z]+/g, '.')}@example.com` }))
const byType = (t) => PEOPLE.filter((p) => p.type === t)
const library = { agents: {} } // person name -> Library agent id

const phone = () => `(512) 555-${int(1000, 9999)}`
const emailOf = (name) => `${name.toLowerCase().replace(/[^a-z]+/g, '.').replace(/^\.|\.$/g, '')}@example.com`
const addr = () => { const [city, state, z] = pick(CITIES); return { line: `${int(100, 9800)} ${pick(STREETS)}`, city, state, zip: `${z}${int(10, 99)}` } }

/** The people the agency assigns to parcels, entered once in the Library's Agents list (add-only). */
async function ensureLibraryAgents() {
  const have = await call(people.admin.token, 'GET', '/api/v1/library/agents')
  for (const a of have) library.agents[a.name] = a.id
  for (const p of PEOPLE) {
    if (library.agents[p.name]) continue
    const made = await call(people.admin.token, 'POST', '/api/v1/library/agents', { name: p.name, agentType: p.type, company: p.company, phone: p.phone, email: p.email, isActive: true })
    library.agents[p.name] = made.id
  }
}

async function upsertList(token, base, bodies, numberKey) {
  const have = await call(token, 'GET', base)
  const out = []
  for (const [i, body] of bodies.entries()) {
    if (have[i]) out.push(await call(token, 'PUT', `${base}/${have[i].id}`, numberKey ? { ...body, [numberKey]: have[i][numberKey] } : body))
    else out.push(await call(token, 'POST', base, body))
  }
  return out
}

function partyBody(name, kind, { parcelAddress, contactType, contactName, notes } = {}) {
  const a = addr()
  const who = contactName ?? owner()
  return {
    kind, name, legalName: name, email: emailOf(name), phone: phone(), addressLine1: a.line, addressLine2: chance(0.2) ? `Suite ${int(100, 400)}` : null,
    city: a.city, state: a.state, postalCode: a.zip, notes: notes ?? pick(['Prefers email in the evening.', 'Owner lives out of state; use the mailing address.', 'Best reached by phone before 9 am.', 'Family-held property since the 1980s.']),
    addresses: [
      { addressType: 'Mailing Address', address: a.line, contactPerson: name, city: a.city, state: a.state, zip: a.zip },
      ...(parcelAddress ? [{ addressType: 'Property Address', address: parcelAddress, contactPerson: name, city: 'Austin', state: 'TX', zip: `787${int(10, 99)}` }] : []),
    ],
    contacts: [{ contactType: contactType ?? (kind === 'Organization' ? 'Representative' : 'Family member'), name: who, phone: phone(), email: emailOf(who), address: a.line, isActive: true, isPrimary: true }],
  }
}

async function setTax(partyId, org) {
  const taxId = org ? `${int(10, 98)}-${int(1000000, 9999999)}` : `${pick([int(100, 665), int(667, 899)])}-${int(10, 99)}-${int(1000, 9999)}`
  await call(people.designer.token, 'PUT', `/api/v1/parties/${partyId}/tax-info`, { type: org ? 'Ein' : 'Ssn', taxId, w9ReceivedOn: day(-int(20, 200)), reportable1099: org || chance(0.3) })
}

async function fillParties(c) {
  const tok = people.designer.token
  const pp = await call(tok, 'GET', `/api/v1/parcels/${c.id}/parties`)
  if (pp.parentTractId) return // a tract's child parcels inherit the tract's owners (ADR 0023)
  const own = pp.ownership.filter((l) => l.source === 'Parcel' && l.role === 'FeeOwner')
  const wantOwners = Math.max(own.length, c.twoOwners ? 2 : 1)
  const share = Math.floor(100 / wantOwners)
  let n = 0
  // Clear the shares first, so setting them one by one never passes 100 percent on the way.
  for (const l of own) if (l.sharePercent !== null) await call(tok, 'PUT', `/api/v1/party-links/${l.id}`, { sharePercent: null, ownerType: l.ownerType, displacementRequired: l.displacementRequired ?? false, notes: l.notes, leaseOnFile: false, leaseDetails: null, insuranceDetails: null })
  for (const l of own) {
    const isOrg = /Trust|LLC/.test(l.party.name)
    await call(tok, 'PUT', `/api/v1/parties/${l.party.id}`, partyBody(l.party.name, isOrg ? 'Organization' : 'Person', { parcelAddress: c.address }))
    await call(tok, 'PUT', `/api/v1/party-links/${l.id}`, { sharePercent: share, ownerType: n === 0 ? 'Vested Owner' : 'Fee Owner', displacementRequired: l.displacementRequired ?? false, notes: pick(['Deed on file.', 'Confirmed against the tax record.', 'Verified at the site visit.']), leaseOnFile: false, leaseDetails: null, insuranceDetails: null })
    if (!l.party.hasTaxId) await setTax(l.party.id, isOrg)
    n++
  }
  for (const l of pp.ownership.filter((x) => x.source === 'Parcel' && x.role !== 'FeeOwner')) {
    await call(tok, 'PUT', `/api/v1/parties/${l.party.id}`, partyBody(l.party.name, 'Organization', { parcelAddress: c.address, contactType: 'Lender' }))
    await call(tok, 'PUT', `/api/v1/party-links/${l.id}`, { sharePercent: null, ownerType: l.ownerType, displacementRequired: false, notes: l.notes ?? 'Holder of record interest; payoff letter requested.', leaseOnFile: false, leaseDetails: null, insuranceDetails: null })
  }
  while (n < wantOwners) {
    const name = owner()
    const made = await call(tok, 'POST', `/api/v1/parcels/${c.id}/parties`, { partyId: null, newParty: { kind: 'Person', ...partyBody(name, 'Person', { parcelAddress: c.address }) }, role: 'FeeOwner', sharePercent: share, ownerType: n === 0 ? 'Vested Owner' : 'Fee Owner', displacementRequired: false, notes: 'Co-owner of record.', leaseOnFile: false, leaseDetails: null, insuranceDetails: null })
    await setTax(made.party.id, false)
    n++
  }
  if (pp.contacts.length + pp.occupants.length + pp.abutters.length === 0) {
    const add = (role, name, kind, extra = {}) => call(tok, 'POST', `/api/v1/parcels/${c.id}/parties`, { partyId: null, newParty: partyBody(name, kind, extra.party), role, sharePercent: null, ownerType: null, displacementRequired: false, notes: extra.notes ?? null, leaseOnFile: !!extra.lease, leaseDetails: extra.lease ?? null, insuranceDetails: extra.insurance ?? null })
    await add('Lienholder', pick(BANKS), 'Organization', { notes: 'Mortgage holder; payoff letter requested.', party: { contactType: 'Lender' } })
    if (chance(0.5)) await add('Attorney', `${pick(FIRST)} ${pick(LAST)}, Esq.`, 'Person', { notes: `Represents the owner (${pick(FIRMS)}).`, party: { contactType: 'Attorney' } })
    if (chance(0.3)) await add('OwnerRepresentative', owner(), 'Person', { notes: 'Acts for the owner under a signed authorization.' })
    if (chance(0.35)) await add('Tenant', owner(), 'Person', { lease: `Month-to-month lease at $${int(900, 2400)}/mo; deposit on file.`, insurance: "Renter's policy on file, renews in March.", notes: 'Occupies the main building.', party: { contactType: 'Tenant' } })
    if (chance(0.4)) await add('Abutter', `${pick(LAST)} Properties LLC`, 'Organization', { notes: 'Adjoining owner to the east; notified of the project.', party: { contactType: 'Property manager' } })
  }
}

async function fillTeam(c) {
  const tok = people.pm.token
  const have = await call(tok, 'GET', `/api/v1/parcels/${c.id}/team`)
  const want = [pick(byType('Project Lead')), pick(byType('Row Designer'))]
  if (c.stage >= 1) want.push(pick(byType('Surveyor')), pick(byType('Title Researcher')))
  if (c.stage >= 2) want.push(c.appraisalCoordinator, c.appraiserPerson)
  if (c.stage >= 3) want.push(c.negotiator)
  if (c.stage >= 5 || c.impasse) want.push(pick(byType('Counsel')))
  if (c.stage >= 3 && chance(0.3)) want.push(pick(byType('Broker')))
  for (const p of want) {
    if (have.some((m) => m.name === p.name)) continue
    have.push({ name: p.name })
    await call(tok, 'POST', `/api/v1/parcels/${c.id}/team`, { agentId: library.agents[p.name], notes: pick(['Primary contact for this parcel.', 'Assigned at kickoff.', 'Covers site work and follow-ups.', null]) })
  }
}

async function fillDiaryNotes(c) {
  const tok = people.neg.token
  const diary = await call(tok, 'GET', `/api/v1/parcels/${c.id}/diary`)
  if (diary.length === 0 && c.stage >= 1) {
    const rows = [
      ['Call', 'Outgoing', 'Introduced the project and explained the acquisition process.', 'Owner asked for the plans by email.'],
      ['Mailing', 'Outgoing', 'Mailed the notice to owner with the project brochure.', 'Delivery receipt returned.'],
      ['Meeting', 'Incoming', 'Owner met us on site to walk the property line.', 'Agreed to a survey visit next week.'],
      ['Email', 'Incoming', 'Owner sent questions about access during construction.', 'Answered with the construction staging plan.'],
    ]
    for (const [i, [method, direction, summary, outcome]] of rows.slice(0, Math.min(4, 1 + c.stage)).entries()) {
      await call(tok, 'POST', `/api/v1/parcels/${c.id}/diary`, { occurredOn: day(-Math.max(2, c.base - 8 * i - 6)), method, direction, contactName: c.ownerName, partyId: null, summary, outcome })
    }
  }
  const notes = await call(people.pm.token, 'GET', `/api/v1/parcels/${c.id}/notes`)
  if (notes.length === 0) {
    const texts = ['Owner prefers contact by email in the evening.', 'Frontage along the corridor is fenced; the gate code is with the surveyor.', c.impasse ? 'Owner rejected the last two offers; counsel looped in.' : 'No issues raised at the last visit.']
    for (const t of texts.slice(0, c.stage > 3 ? 3 : 2)) await call(people.pm.token, 'POST', `/api/v1/parcels/${c.id}/notes`, { text: t })
  }
}

async function fillSiteVisits(c) {
  if (c.stage < 1) return
  const have = (await call(people.pm.token, 'GET', `/api/v1/parcels/${c.id}`)).siteVisits.length
  for (const [i, notes] of ['Walked the boundary with the surveyor; stakes set.', 'Met the owner on site and reviewed the take area.'].entries()) {
    if (i < have) continue
    await call(people.pm.token, 'POST', `/api/v1/parcels/${c.id}/site-visits`, { visitedOn: day(-Math.max(1, c.base - 12 - i * 9)), notes, ownerContactConfirmed: i === 1 || chance(0.5) })
  }
}

/** The parcel form: legal description, CAD number, interest and possession types, dates, segment, priority and location. */
async function fillParcelForm(c, d) {
  const body = {
    number: d.number, parentTractId: d.parentTractId, groupId: d.groupId ?? c.groupId ?? null, legalDescription: d.legalDescription ?? `Lot ${int(1, 40)}, Block ${String.fromCharCode(65 + int(0, 7))}, ${pick(SUBDIVISIONS)} Subdivision, Travis County, Texas`,
    address: d.address, areaSqFt: d.areaSqFt, propertyType: d.propertyType ?? pick(['Residential', 'Commercial', 'Agricultural', 'Vacant land']), statusId: c.statusId ?? d.statusId, acquisitionType: d.acquisitionType, customValues: d.customValues ?? null,
    cadNumber: d.cadNumber ?? `CAD-${int(100000, 999999)}`, acquisitionInterestType: d.acquisitionInterestType ?? pick(LIB.interestTypes), possessionDate: d.possessionDate ?? (c.closed ? day(-int(5, 60)) : null),
    proposedPossessionDate: d.proposedPossessionDate ?? day(int(20, 200)), segment: d.segment ?? `Segment ${int(1, 4)}`, priority: d.priority ?? pick(['High', 'Medium', 'Medium', 'Low']), possessionType: d.possessionType ?? pick(LIB.possession),
    parcelLocation: d.parcelLocation ?? `${pick(['North', 'South', 'East', 'West'])} side of ${pick(STREETS)}, station ${int(10, 90)}+${int(10, 99)}`, isActiveParcel: true,
  }
  await call(people.pm.token, 'PUT', `/api/v1/parcels/${c.id}`, body)
}

async function fillTitle(c) {
  if (c.stage < 1) return
  const d = c.d
  const pairs = [pick(LIB.encumbrances), pick(LIB.encumbrances), pick(LIB.encumbrances)].slice(0, 1 + int(0, 2))
  const encs = pairs.map(([type, title], i) => ({
    encumbranceType: type, documentTitle: title, documentDate: d(20 + i * 3), dateRecorded: d(24 + i * 3), documentNumber: `DOC-${int(2000000, 2999999)}`, isUnrecorded: false,
    grantor: c.ownerName, grantee: pick([...BANKS, 'City of Austin', 'Austin Energy', 'Travis County']), exceptionSubmittedDate: d(52 + i), exceptionApprovedDate: d(58 + i * 2),
    impactAnalysis: pick(['Lien must be released before closing.', 'Easement crosses the take area; relocate or acquire.', 'No effect on the acquisition area.', 'Covenant restricts the proposed use.']),
    recommendedCure: pick(['Obtain a release at closing from the lender.', 'Subordination agreement from the holder.', 'Pay off from the closing proceeds.', 'No cure needed; accept as an exception.']),
    cureStatus: pick(LIB.cure), reviewNotes: pick(['Reviewed by the title researcher.', 'Cure letter sent to the holder.', 'Waiting for the payoff statement.']),
    links: [{ recordIdentifier: `TC-${c.number}`, moduleName: 'Title and closing', notes: 'Cure tracked on the closing record.' }],
  }))
  await upsertList(people.title.token, `/api/v1/parcels/${c.id}/encumbrances`, encs)
  const station = (lo, hi) => `${int(lo, hi)}+${String(int(0, 99)).padStart(2, '0')}`
  const eas = [
    { easementType: pick(LIB.easementTypes), description: pick(['Strip along the frontage for the widened shoulder.', 'Temporary workspace for the culvert replacement.', 'Drainage channel across the rear of the lot.', 'Utility relocation corridor.']), startDate: d(66), endDate: day(int(180, 900)), startStation: station(10, 60), endStation: station(61, 99) },
    ...(chance(0.3) ? [{ easementType: pick(LIB.easementTypes), description: 'Second easement for the sidewalk tie-in.', startDate: d(70), endDate: null, startStation: station(10, 60), endStation: station(61, 99) }] : []),
  ]
  const haveE = await call(people.surveyor.token, 'GET', `/api/v1/parcels/${c.id}/easements`)
  await upsertList(people.surveyor.token, `/api/v1/parcels/${c.id}/easements`, eas.map((e, i) => ({ ...e, number: haveE[i]?.number ?? `E-${c.number}-${i + 1}-${int(100, 999)}` })), 'number')
}

async function fillAppraisal(c) {
  if (c.stage < 2) return
  const d = c.d
  const base = c.perParcel * between(0.55, 1.5)
  const land = Math.round(base * 0.7 / 100) * 100, impr = Math.round(base * 0.12 / 100) * 100, dmg = Math.round(base * 0.12 / 100) * 100, fx = Math.round(base * 0.06 / 100) * 100
  const total = land + impr + dmg + fx
  const body = {
    appraisalType: pick(['Full Appraisal', 'Full Appraisal', 'Before and After', 'Waiver Valuation']), appraiser: c.appraiserPerson.name, landPlannerRequired: chance(0.2), assignedTo: c.appraisalCoordinator.name,
    requestedDate: d(92), receivedDate: d(125), reportDate: d(124), dueDate: day(-(c.base - 120)), submittedToAtpDate: d(127), effectiveDate: d(118),
    amounts: [{ amountType: 'Land', description: 'Fee simple, take area', amount: land }, { amountType: 'Site improvements', description: 'Fencing and paving', amount: impr }, { amountType: 'Damages', description: 'Severance damages to the remainder', amount: dmg }, { amountType: 'Fixtures', description: 'Signage and lighting', amount: fx }],
    reviewAppraiser: null, reviewType: null, appraisalReceivedDate: null, approvedJustCompensation: null, reviewCompletedDate: null, reviewSubmittedDate: null, reviewRecommendation: null, reviewRecommendedValue: null, submittedToJatDate: null, approvedByJatDate: null, ftaConcurrenceNeeded: false,
  }
  // The review fields are the Approver's: when they are already set, the appraiser's save sends them back unchanged.
  const [saved] = await call(people.appraiser.token, 'GET', `/api/v1/parcels/${c.id}/appraisals`)
  if (saved) for (const k of ['reviewAppraiser', 'reviewType', 'appraisalReceivedDate', 'approvedJustCompensation', 'reviewCompletedDate', 'reviewSubmittedDate', 'reviewRecommendation', 'reviewRecommendedValue', 'submittedToJatDate', 'approvedByJatDate', 'ftaConcurrenceNeeded']) body[k] = saved[k]
  const made = (await upsertList(people.appraiser.token, `/api/v1/parcels/${c.id}/appraisals`, [body]))[0]
  c.approved = null
  if (c.stage >= 3 || (c.stage === 2 && chance(0.4))) {
    c.approved = Math.round(total * between(0.93, 1.08) / 50) * 50
    await call(people.approver.token, 'PUT', `/api/v1/parcels/${c.id}/appraisals/${made.id}`, {
      ...body, reviewAppraiser: byType('Appraisal Reviewer')[0].name, reviewType: pick(['Technical review', 'Desk review', 'Field review']), appraisalReceivedDate: d(126), approvedJustCompensation: c.approved, reviewSubmittedDate: d(128), reviewCompletedDate: d(132),
      reviewRecommendation: pick(['Approved', 'Approved', 'Approved with changes']), reviewRecommendedValue: c.approved, submittedToJatDate: d(133), approvedByJatDate: d(134), ftaConcurrenceNeeded: c.oversight === 'Federal',
    })
  }
}

async function fillNegotiation(c) {
  if (c.stage < 3) return
  const d = c.d
  const approved = c.approved ?? Math.round(c.perParcel / 50) * 50
  const agreed = c.stage >= 5 ? Math.round(approved * between(0.98, 1.16) * c.over / 50) * 50 : null
  const offers = [{ offerType: 'Initial Offer', mailedDate: d(140), deliveredDate: d(143), amount: approved, notes: 'Offer package with the appraisal summary and the Uniform Act brochure.' }]
  if (c.stage >= 4) offers.push({ offerType: 'Revised Offer', mailedDate: d(170), deliveredDate: d(173), amount: Math.round(approved * 1.04 / 50) * 50, notes: "Revised after the owner's counter and the review." })
  if (c.stage >= 5) offers.push({ offerType: 'Final Offer', mailedDate: d(200), deliveredDate: d(203), amount: agreed, notes: 'Final offer accepted in principle.' })
  const settlements = c.stage >= 4 ? [{ settlementType: pick(['Administrative', 'Negotiated']), counterofferAmount: Math.round(approved * 1.12 / 50) * 50, dateReceived: d(160), dateSubmitted: d(164), counterofferResponse: c.stage >= 5 ? 'Accepted' : pick(['Under review', 'Countered']), responseDateFromAtp: d(172), responseDateToOwner: d(175), settlementNotes: 'Owner asked for a higher figure citing recent comparable sales.' }] : []
  const notes = [{ noteDate: d(143), text: 'Offer delivered; owner has 30 days to respond.' }, { noteDate: d(165), text: 'Counteroffer received and sent for review.' }, { noteDate: d(204), text: 'Agreement reached; preparing the purchase and sale agreement.' }].filter((n) => n.noteDate && (n.noteDate <= day(0)))
  await call(people.neg.token, 'PUT', `/api/v1/parcels/${c.id}/negotiation`, {
    assignedTo: c.negotiator.name, totalAgreementAmount: agreed, notes: c.impasse ? 'Owner has rejected the last two offers.' : pick(['Owner is open to a deal.', 'Counsel is reviewing the offer.', 'Awaiting owner response.', 'Site visit completed; owner asked for a higher figure.']),
    counterofferSubmittedToJatDate: c.stage >= 4 ? d(166) : null, counterofferResponseFromJatDate: c.stage >= 4 ? d(171) : null, jatResponse: c.stage >= 4 ? (c.stage >= 5 ? 'Accepted' : 'Under review') : null, ftaApprovalRequiredForCounteroffer: c.oversight === 'Federal',
    puaRequestedDate: c.stage >= 5 ? d(205) : null, puaApprovedDate: c.stage >= 5 ? d(210) : null, puaApprovedAmount: c.stage >= 5 ? agreed : null, puaExecutedDate: c.stage >= 5 ? d(214) : null, puaClosedDate: c.closed ? d(300) : null,
    statusNotes: notes, offers: offers.filter((o) => o.mailedDate), settlements,
  })
  const who = [byType('Negotiator')[0].name, c.negotiator.name]
  const texts = ['Discussed the offer.', 'Left a voicemail.', 'Owner requested a copy of the appraisal.', 'Explained the closing steps and timeline.', 'Owner confirmed the mailing address.']
  const log = []
  for (let i = 0; i < 2 + int(0, 2); i++) log.push({ contactedAt: new Date(started.getTime() - Math.max(1, c.base - 140 - i * 11) * 86_400_000).toISOString(), mode: pick(LIB.modes), contactedBy: who[i % 2], contactPerson: c.ownerName, notes: texts[i % texts.length], links: i === 0 ? [{ recordIdentifier: `NEG-${c.number}`, moduleName: 'Negotiation', notes: 'Offer discussed' }] : [] })
  await upsertList(people.neg.token, `/api/v1/parcels/${c.id}/contact-log`, log)
}

async function fillClosing(c) {
  if (c.stage < 5) return
  const d = c.d
  await call(people.neg.token, 'PUT', `/api/v1/parcels/${c.id}/title-closing`, {
    assignedTo: c.negotiator.name, titleCompany: pick(TITLE_COS), titleClosingAgent: pick(byType('Title Researcher')).name, gfNumber: `GF-${int(2026000, 2026999)}`, finalTitlePolicyNo: c.stage >= 6 ? `TP-${int(300000, 399999)}` : null,
    psaSignedByOwnerDate: d(230), psaSignedByAtpDate: d(234), psaAmount: c.approved ? Math.round(c.approved * between(1, 1.1) / 50) * 50 : null, settlementReceivedDate: d(245), settlementExecutedDate: d(250),
    curativeItemsCompleted: c.stage >= 6, closingPackageSubmittedDate: d(275), closingPackageApprovedDate: d(280), closingInstructionDate: d(285), anticipatedClosingDate: c.closed ? d(295) : day(int(-25, 40)), actualClosingDate: c.closed ? d(305) : null,
    conveyanceDocuments: [{ conveyanceType: pick(LIB.conveyance), signedDate: d(252), recordedDate: c.closed ? d(308) : null, recordingNumber: c.closed ? `${int(2026100000, 2026999999)}` : null }],
    titleCommitments: [{ vestedOwnerName: c.ownerName, requestedDate: d(50), receivedDate: d(62), effectiveDate: d(58), issuedDate: d(64), curativePlanSubmittedDate: d(70), curativePlanApprovedDate: d(78) }],
  })
}

async function fillRoe(c) {
  if (c.stage < 1 || !c.roe) return
  const d = c.d
  await upsertList(people.neg.token, `/api/v1/parcels/${c.id}/rights-of-entry`, [{
    status: 'Granted', requestedOn: d(48), grantedOn: d(55), expiresOn: day(int(4, 120)), grantedBy: c.ownerName, purpose: pick(['Survey', 'Appraisal access', 'Environmental testing', 'Geotechnical borings']),
    notes: 'Call the owner 24 hours before each visit; keep gates closed.', letterMailedDate: d(49), letterDeliveredDate: d(52), sentToAtpDate: d(53),
  }])
}

async function fillQuestionnaire(c) {
  if (c.stage < 2 || !c.questionnaire) return
  const d = c.d
  await upsertList(people.ra.token, `/api/v1/parcels/${c.id}/questionnaires`, [{
    relocationType: pick(['Residential', 'Non-residential', 'Personal Property only']), dueByDate: d(100), sentToOwnerDate: d(88),
    interests: [{ name: c.ownerName, phone: phone(), contact: 'Owner', email: emailOf(c.ownerName), address: c.address }],
    questions: [
      { question: 'Does anyone live on the property?', answer: pick(['Yes', 'No']), notes: null }, { question: 'Number of people in the household', answer: String(int(1, 5)), notes: null },
      { question: 'Is any business operated from the property?', answer: pick(['Yes', 'No', 'No']), notes: 'Verified on the site visit.' }, { question: 'Any personal property to be moved?', answer: pick(['Yes', 'No']), notes: pick([null, 'Vehicles and a storage shed.']) },
    ],
  }])
}

async function fillCondemnation(c) {
  if (!c.condemn) return
  const d = c.d
  await call(people.neg.token, 'PUT', `/api/v1/parcels/${c.id}/condemnation`, {
    details: {
      prelimPackageSubmittedToAtp: d(190), prelimPackageApprovedByAtp: d(196), prelimConcurrenceMemoSentToFta: d(198), jatApprovalOfPrelimPackage: d(202), jatApprovedPackageToCityLegal: d(204), cityLegalReviewCompleted: d(210),
      prelimPackageSubmittedForCouncil: d(214), cityCouncilApproval: d(220), edCoordinator: byType('Negotiator')[0].name, outsideCounsel: pick(byType('Counsel')).name, appraisalWitness: byType('Appraiser')[0].name, engineeringWitness: byType('Row Designer')[0].name,
      expertWitness1: 'Dr. Alan Pierce, real estate economist', expertWitness2: 'Hollis Grant, PE', petitionPackageReceived: d(222), petitionPackageReviewedByAtp: d(225), probateCauseNo: null, petitionFiledDate: d(228), lisPendensRecordedDate: d(229),
      lisPendensLettersMailedDate: d(231), commissionerStrikePeriodExpires: d(236), commissionerNotificationLettersMailed: d(238), orderSettingHearingDate: d(240), oathsOfCommissionersDate: d(244), prehearingScheduled: d(246),
      commissionerFeesDeposited: d(248), objectionDeadline: d(262), objectionsFiled: false,
    },
    statusNotes: [{ noteDate: d(222), text: 'Petition package received from outside counsel.' }, { noteDate: d(240), text: 'Hearing set by the court.' }].filter((n) => n.noteDate),
    attorneys: [{ partyRepresented: 'Agency', attorneyName: 'Helen Marsh', firm: 'Marsh & Lowe LLP', address: '500 Congress Ave, Austin, TX 78701', phone: phone(), email: 'helen.marsh@example.com' }, { partyRepresented: 'Owner', attorneyName: 'Victor Salazar', firm: 'Salazar Land Law', address: '210 W 6th St, Austin, TX 78701', phone: phone(), email: 'victor.salazar@example.com' }],
    commissioners: [1, 2, 3].map((i) => ({ appointmentDate: d(240), appointmentType: i === 3 ? 'Alternate' : 'Special commissioner', name: owner(), address: `${int(100, 9000)} ${pick(STREETS)}, Austin, TX`, email: `commissioner${i}@example.com`, phone: phone(), struck: i === 3 })),
    notices: [{ partyToServe: c.ownerName, serviceSubmitted: d(229), serviceCompleted: d(234), nohFiled: d(246), comments: 'Served at the mailing address on file.' }],
  })
}

async function fillProperty(c) {
  if (!c.property) return
  const d = c.d
  await call(people.pm.token, 'PUT', `/api/v1/parcels/${c.id}/property-management`, {
    details: {
      transferMemoSubmittedToAtp: d(310), transferMemoApprovedByAtp: d(312), transferMemoToPdb: d(314), propertySecured: d(316), demoStatus: pick(['Scheduled', 'In progress', 'Completed']), demoCompleted: null,
      dispositionType: pick(['Sale', 'Transfer to another agency', 'Retention for public use']), dispositionMethod: pick(['Direct transfer', 'Sealed bid', 'Public auction']), dispositionStatus: pick(['Pending approval', 'Approved', 'Listed']),
      buyerGrantee: pick(['City of Austin Parks Department', 'Travis County', 'Capital Metro']), dispositionJustification: 'Remainder not needed for the project; transfer returns it to productive use.', ftaConcurrenceRequired: c.oversight === 'Federal',
      salePrice: Math.round(c.perParcel * 0.18 / 100) * 100, legalReviewCompleteDate: d(318), boardApprovalDate: d(320), closingTransferDate: null, recordingDate: null, documentRecordingNo: null,
    },
    statusNotes: [{ noteDate: d(311), text: 'Transfer memo submitted for approval.' }, { noteDate: d(316), text: 'Property secured and fenced.' }].filter((n) => n.noteDate),
    funding: [{ fundType: 'Federal', fundSource: 'FHWA STBG', amount: Math.round(c.perParcel * 0.8), percentage: 80 }, { fundType: 'Local', fundSource: 'Local bond', amount: Math.round(c.perParcel * 0.2), percentage: 20 }],
  })
}

/** Fills every tab of one parcel. `c` says what the story is for it: stage 0-6, closed, impasse, and its project's figures. */
async function fillParcel(c) {
  // Every random choice for a parcel comes from its own project and parcel number, so filling it again (or with --enrich) changes nothing.
  const outer = seed
  try { await fillParcelWith(c) } finally { seed = outer }
}
async function fillParcelWith(c) {
  const head = await call(people.pm.token, 'GET', `/api/v1/parcels/${c.id}`)
  seed = [...`${head.projectNumber}/${head.number}`].reduce((h, ch) => (Math.imul(h, 31) + ch.charCodeAt(0)) | 0, 7)
  c.base = 40 + c.stage * 45 + (c.closed ? 30 : 0)
  c.d = (k) => (k <= c.base - 5 ? day(k - c.base) : null) // the date of an event k days after the parcel started; null while it is still in the future
  c.negotiator = pick(byType('Negotiator')); c.appraiserPerson = pick(byType('Appraiser')); c.appraisalCoordinator = byType('Appraisal Coordinator')[0]
  c.twoOwners = chance(0.35); c.roe = chance(0.5); c.questionnaire = chance(0.5)
  c.condemn = c.impasse && c.stage >= 4; c.property = c.closed && chance(0.4)
  const d = await call(people.pm.token, 'GET', `/api/v1/parcels/${c.id}`)
  c.address = d.address; c.ownerName = d.ownerName ?? owner(); c.number = d.number
  // Each tab draws its random choices from the parcel and the tab's name, so a tab's content does not depend on what the others found already there.
  const key = `${head.projectNumber}/${head.number}`
  const section = async (name, fn) => { seed = [...`${key}#${name}`].reduce((h, ch) => (Math.imul(h, 31) + ch.charCodeAt(0)) | 0, 7); await fn() }
  await section('form', () => fillParcelForm(c, d))
  await section('parties', () => fillParties(c))
  c.ownerName = (await call(people.pm.token, 'GET', `/api/v1/parcels/${c.id}`)).ownerName ?? c.ownerName
  for (const [name, fn] of [['team', fillTeam], ['diary', fillDiaryNotes], ['visits', fillSiteVisits], ['title', fillTitle], ['appraisal', fillAppraisal], ['negotiation', fillNegotiation], ['closing', fillClosing], ['roe', fillRoe], ['questionnaire', fillQuestionnaire], ['condemnation', fillCondemnation], ['property', fillProperty]]) await section(name, () => fn(c))
}

/** `--enrich`: every active parcel already in the database gets every tab filled (status, parcel form, owners, team, title, appraisal, negotiation, closing...). */
async function enrichExisting() {
  const statuses = Object.fromEntries((await call(people.admin.token, 'GET', '/api/v1/parcel-rules/statuses')).map((s) => [s.name, s.id]))
  const spec = (name) => PROJECTS.find((x) => x.name === name)
  const items = []
  for (let page = 1; ; page++) {
    const r = await call(people.admin.token, 'GET', `/api/v1/parcels?pageSize=200&page=${page}`)
    items.push(...r.items)
    if (items.length >= r.total || r.items.length === 0) break
  }
  const parcels = items.filter((p) => p.state === 'Active')
  console.log(`Filling every tab of ${parcels.length} parcels…`)
  const STATUS_BY_STEP = { setup: 'In Design', title_survey: 'Pre-Acquisition', appraisal: 'Appraisal', offer: 'Initial Offer', negotiation: 'Final Offer', agreement: 'In Possession - PUA', closing: 'In Possession - PUA' }
  let done = 0
  for (const p of parcels) {
    const stage = Math.max(0, STEPS.indexOf(p.currentStepKey))
    const tc = await call(people.admin.token, 'GET', `/api/v1/parcels/${p.id}/title-closing`)
    const closed = !!tc.record?.actualClosingDate
    const sp = spec(p.projectName)
    const keepStatus = p.statusName && !['Inactive', 'Identified'].includes(p.statusName) && statuses[p.statusName]
    const impasse = /Condemnation|Petition|Hearing|Objections/.test(p.statusName ?? '')
    const statusId = keepStatus ? p.statusId : statuses[closed ? 'In Possession - CLOSED' : STATUS_BY_STEP[STEPS[stage]]] ?? p.statusId
    await fillParcel({ id: p.id, projectId: p.projectId, stage: closed ? 6 : stage, closed, impasse, perParcel: sp?.perParcel ?? 150_000, over: sp?.over ?? 1, oversight: sp?.oversight ?? 'State', statusId, groupId: p.groupId })
    if (++done % 10 === 0) console.log(`  ${done}/${parcels.length}`)
  }
  console.log(`
Done: ${done} parcels filled in.`)
}

async function main() {
  console.log(`Seeding ${API} (database ${DB})…`)
  for (const [key, username] of Object.entries({ pm: 'rowprojectmanager', designer: 'rowdesign', surveyor: 'surveyor', title: 'titleresearcher', appraiser: 'appraiser', approver: 'appraisalapprover', neg: 'rownegotiator', ra: 'relocationagent', finance: 'rowfinance', admin: 'admin' })) await login(key, username)
  await ensureLibraryAgents()
  if (ENRICH) { await enrichExisting(); return }

  const existing = await call(people.admin.token, 'GET', '/api/v1/projects?pageSize=200')
  if (!FORCE && !RESUME && (existing.items ?? []).some((p) => PROJECTS.some((x) => x.name === p.name))) {
    console.log('The demo portfolio is already there. Use --force to add it again.')
    return
  }
  const statuses = Object.fromEntries((await call(people.admin.token, 'GET', '/api/v1/parcel-rules/statuses')).map((s) => [s.name, s.id]))
  const STATUS_BY_STEP = { setup: 'In Design', title_survey: 'Pre-Acquisition', appraisal: 'Appraisal', offer: 'Initial Offer', negotiation: 'Final Offer', agreement: 'In Possession - PUA', closing: 'In Possession - PUA' }
  const statusFor = (step, closed, impasse) => statuses[impasse ? 'Pending Condemnation' : closed ? 'In Possession - CLOSED' : STATUS_BY_STEP[step]] ?? statuses.Inactive ?? Object.values(statuses)[0]

  const managers = await call(people.pm.token, 'GET', '/api/v1/projects/managers', undefined, { allow: [403, 404] }) ?? []
  const parcelPlans = [] // for the timestamp pass
  let parcelCount = 0
  for (const spec of RESUME ? [] : PROJECTS) {
    const pm = people.pm.token
    const project = await call(pm, 'POST', '/api/v1/projects', {
      name: spec.name, oversightType: spec.oversight, phase: spec.phase, region: spec.region, district: spec.district, description: `${spec.name}: right of way for the ${spec.phase.toLowerCase()} improvements.`,
      fundingSource: spec.oversight === 'Federal' ? 'FHWA STBG' : spec.oversight === 'State' ? 'State Highway Fund' : 'Local bond', fundingAmount: Math.round(spec.land * 1.25),
      rightOfWayClearDate: spec.clear === null ? null : day(spec.clear), lettingDate: spec.clear === null ? null : day(spec.clear + 75),
      location: `${spec.region} region, ${spec.district}`, designEstimate: Math.round(spec.land * 1.15), planTurnInDate: spec.clear === null ? null : day(spec.clear - 60), constructionStartDate: spec.clear === null ? null : day(spec.clear + 120), constructionEndDate: spec.clear === null ? null : day(spec.clear + 520),
      constructionProjectManager: pick(AGENTS), projectManagerUserId: managers.length ? pick(managers).id : null, documentFolderStructure: 'By parcel number', documentProperties: 'Document type',
    })
    if (spec.oversight !== 'Local') {
      await call(pm, 'PUT', `/api/v1/projects/${project.id}/nepa`, { decisionType: 'CategoricalExclusion', decisionDate: day(-400) })
      await call(pm, 'POST', `/api/v1/projects/${project.id}/authorizations`, { type: 'Acquisition', authorizedAmount: Math.round(spec.land * 1.1), federalSharePercent: 80, authorizedOn: day(-380), programmingConfirmed: true })
    }
    await call(pm, 'POST', `/api/v1/projects/${project.id}/cost-estimates`, {
      landCost: Math.round(spec.land * 0.8), improvementsCost: Math.round(spec.land * 0.1), relocationCost: Math.round(spec.land * 0.06), indirectCost: Math.round(spec.land * 0.04), notes: 'Programming estimate',
    })
    for (const [i, fy] of [2026, 2027, 2028].entries()) await call(pm, 'PUT', `/api/v1/projects/${project.id}/budgets/${fy}`, { amount: Math.round(spec.land * [0.3, 0.45, 0.35][i]), notes: ['Acquisition and appraisal', 'Negotiation and closings', 'Relocation and close-out'][i] })
    const groups = []
    for (const [g, d] of [['North segment', 'Parcels north of the river crossing'], ['South segment', 'Parcels along the southern approach']]) groups.push(await call(pm, 'POST', `/api/v1/projects/${project.id}/groups`, { name: g, description: d }))
    console.log(`  ${project.number} ${spec.name}`)

    for (let n = 0; n < spec.parcels; n++) {
      const where = pickWeighted(spec.spread) // 0..6 = step index, 7 = closed
      const closed = where === 7
      const stage = closed ? 6 : where
      const step = STEPS[stage]
      const impasse = !closed && stage >= 4 && chance(0.12)
      const parcel = await call(pm, 'POST', '/api/v1/parcels', {
        projectId: project.id, groupId: groups[n % groups.length].id, areaSqFt: int(2_000, 90_000), acquisitionType: 'Standard', address: `${int(100, 9800)} ${pick(STREETS)}`,
        propertyType: pick(['Residential', 'Commercial', 'Agricultural', 'Vacant land']), statusId: statusFor(step, closed, impasse), priority: pick(['High', 'Medium', 'Medium', 'Low']), segment: `Segment ${int(1, 4)}`,
      })
      parcelCount++
      for (let k = 1; k <= stage; k++) await call(pm, 'POST', `/api/v1/parcels/${parcel.id}/advance`, { targetStepKey: STEPS[k] })

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

      await fillParcel({ id: parcel.id, projectId: project.id, stage, closed, impasse, perParcel: spec.perParcel, over: spec.over, oversight: spec.oversight, statusId: statusFor(step, closed, impasse), groupId: groups.length ? groups[n % groups.length].id : null })
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
    for (const r of rows) parcelPlans.push({ id: r.id, stage: STEPS.indexOf(r.step), closed: r.closed, step: r.step, spec: PROJECTS.find((x) => x.name === r.project), impasse: r.status === 'Pending Condemnation', overdue: false })
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
