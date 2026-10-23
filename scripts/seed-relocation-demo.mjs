#!/usr/bin/env node
// Adds DEMO relocation data on top of the demo portfolio (scripts/seed-demo-data.mjs): displaced owners, relocation cases at every stage of
// Diagram 5, payments in every status (requested, half-signed, approved, rejected, paid), a couple of parcels that become Acquired, one
// ineligible displacee and one denied claim under appeal. Everything goes through the real API as the people who would do it (ADR 0026).
//
//   node scripts/seed-relocation-demo.mjs               the Development API on http://localhost:5100
//   node scripts/seed-relocation-demo.mjs --force       add it again even if relocations already exist
//   node scripts/seed-relocation-demo.mjs --api http://localhost:5101
//
// Needs the API running in Development with the demo users (password qwertyuiop). Development only: nothing in the product reads this script.
const arg = (name, fallback) => { const i = process.argv.indexOf(`--${name}`); return i > -1 ? process.argv[i + 1] : fallback }
const API = arg('api', 'http://localhost:5100')
const FORCE = process.argv.includes('--force')
const PASSWORD = 'qwertyuiop'

let seed = 20261023
const rnd = () => { seed |= 0; seed = (seed + 0x6D2B79F5) | 0; let t = Math.imul(seed ^ (seed >>> 15), 1 | seed); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296 }
const int = (a, b) => Math.floor(a + rnd() * (b - a + 1))
const pick = (list) => list[int(0, list.length - 1)]
const day = (offset) => new Date(Date.now() + offset * 86_400_000).toISOString().slice(0, 10)
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

async function call(token, method, path, body, { allow = [] } = {}) {
  for (let attempt = 1; ; attempt++) {
    const res = await fetch(API + path, { method, headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) }, body: body === undefined ? undefined : JSON.stringify(body) })
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

const FIRST = ['Maria', 'James', 'Linda', 'Robert', 'Patricia', 'Michael', 'Barbara', 'William', 'Elizabeth', 'David', 'Jennifer', 'Richard', 'Susan', 'Joseph', 'Karen']
const LAST = ['Garcia', 'Johnson', 'Nguyen', 'Williams', 'Martinez', 'Brown', 'Davis', 'Rodriguez', 'Wilson', 'Anderson', 'Thomas', 'Taylor', 'Moore', 'Jackson', 'Lee']
const AGENTS = ['Lena Ortiz', 'Marcus Bell'] // the Relocation people in the Library's Agents list (scripts/seed-demo-data.mjs adds them)
// Values of the Library's lists (backend Domain/Common/DefaultLibrary.cs)
const DISPLACEE_STATUS = ['Owner-occupant', 'Tenant-occupant', 'Business owner', 'Owner (not occupant)']
const DISPLACEE_INTEREST = ['Fee owner', 'Tenant', 'Business operator', 'Life estate holder']
const ELIGIBILITY = ['Residential owner (180-day)', 'Residential tenant (90-day)', 'Business', 'Personal property only']
const MODES = ['Phone', 'In person', 'Email', 'Letter', 'Certified mail', 'Site visit']
const sleepless = () => {}
const owner = () => `${pick(FIRST)} ${pick(LAST)}`

// How far a case has got: 0 nothing sent … 8 complete. Each stage adds the dates Diagram 5 asks for (the earlier ones stay).
function details(number, name, stage, extra = {}, ctx = {}) {
  const t = (n) => day(-n) // n days ago
  const base = 200 - stage * 20 // earlier stages happened longer ago
  const kind = pick(['Residential', 'Residential', 'Non-residential'])
  const d = {
    relocationNumber: number, displaceeName: name, relocationType: kind, assignedTo: pick(AGENTS), limitedEnglishProficiency: rnd() < 0.15,
    displaceeStatus: kind === 'Residential' ? pick(DISPLACEE_STATUS.slice(0, 2)) : 'Business owner', displaceeInterest: kind === 'Residential' ? pick(DISPLACEE_INTEREST.slice(0, 2)) : 'Business operator',
    displaceeMailingAddress: `${int(100, 9000)} ${pick(['Oak St', 'Maple Ave', 'Cedar Ln', 'Pine Dr'])}, Austin, TX 787${int(10, 99)}`, propertySiteAddress: ctx.site ?? `${int(100, 9000)} Harbor Rd, Austin, TX`,
    housingOfLastResort: false, ineligibleForRelocation: false, demolitionRequired: false,
  }
  if (stage >= 1) Object.assign(d, { planningStudyDate: t(base + 30), ginMailedDate: t(base), ginDeliveredDate: t(base - 3) })
  if (stage >= 2) Object.assign(d, { initialInterview: t(base - 12), advisoryNeeds: pick(['Needs a ground-floor unit; mobility limited.', 'Wants to stay in the same school district.', 'Small business: needs a site near the corridor.', 'Renting out the second unit; two tenants.']) })
  if (stage >= 2) Object.assign(d, { certificateOfEligibility: t(base - 18) })
  if (stage >= 3) Object.assign(d, { noticeOfEligibility: t(base - 25), deliveryOfNoticeOfEligibility: t(base - 27) })
  if (stage >= 4) Object.assign(d, { replacementHousingStudyDate: t(base - 40), comparableNoticeDate: t(base - 42), inspectionDate: t(base - 50), decentSafeSanitary: true })
  if (stage >= 5) Object.assign(d, { notice90Day: t(base - 60), deliveryOf90DayNotice: t(base - 62), vacateEligibleDate: t(base - 64) })
  if (stage >= 6) Object.assign(d, { notice30Day: undefined, notice30DayVacate: t(base - 75), notice30DayVacateDelivered: t(base - 77), targetVacateDate: day(int(5, 40) - 0), movePlan: t(base - 70), certificationOfInventory: t(base - 72) })
  if (stage >= 7) Object.assign(d, { actualVacateDate: t(Math.max(10, base - 100)), targetVacateDate: t(Math.max(12, base - 98)) })
  if (stage >= 7) Object.assign(d, { releaseOfProperty: t(8) })
  if (stage >= 8) Object.assign(d, { certificateOfCompletion: t(5), fileCloseOutSubmitted: t(4), relocationComplete: t(3), relocationComplete1: t(3), benefitsExpirationLetterDate: t(6), benefitExpiration: day(300) })
  return { ...d, ...extra }
}

const PAYMENT_TYPES = ['Moving expense', 'Replacement housing - rental assistance', 'Replacement housing - purchase supplement', 'Down payment assistance', 'Reestablishment expense', 'Fixed payment']
// state of a payment after the story: requested | half (big, PM signed) | approved | rejected | paid
async function payment(parcelId, relocId, kind, amount, advance = false) {
  const url = `/api/v1/parcels/${parcelId}/relocations/${relocId}/payments`
  const after = await call(people.ra.token, 'POST', url, { paymentType: advance ? 'Advance payment' : pick(PAYMENT_TYPES), amount, isAdvance: advance, taxable: rnd() < 0.3, notes: pick(['Receipts attached.', 'Quote from the mover on file.', 'Per the replacement-housing study.', null]) })
  const p = after.payments.at(-1)
  const decide = (who, approve, note) => call(people[who].token, 'POST', `${url}/${p.id}/decision`, { approve, note })
  if (kind === 'requested') return
  if (kind === 'rejected') { await decide('pm', false, pick(['Receipts are missing.', 'Amount is above the comparable.'])); return }
  await decide('pm', true)
  if (kind === 'half') return
  if (amount >= 25000) await decide('finance', true)
  if (kind === 'approved') return
  await call(people.finance.token, 'POST', `${url}/${p.id}/paid`, { reference: `EFT-${int(100000, 999999)}` })
}

async function main() {
  console.log(`Seeding relocation data on ${API}…`)
  for (const [key, username] of Object.entries({ admin: 'admin', pm: 'rowprojectmanager', ra: 'relocationagent', finance: 'rowfinance', designer: 'rowdesign' })) await login(key, username)

  const existing = await call(people.admin.token, 'GET', '/api/v1/overview/relocations')
  if (!FORCE && existing.length > 0) { console.log(`There are already ${existing.length} relocation cases. Use --force to add more.`); return }

  // The status the parcel moves to when every displaced owner has relocated (ADR 0026). Existing agencies add it by hand; the demo does it here.
  const statuses = await call(people.admin.token, 'GET', '/api/v1/parcel-rules/statuses')
  if (!statuses.some((s) => s.name === 'Acquired')) {
    await call(people.admin.token, 'POST', '/api/v1/parcel-rules/statuses', { name: 'Acquired', color: '#16a34a', sortOrder: statuses.length + 1, isActive: true, isInitial: false })
    console.log('  added the "Acquired" parcel status')
  }

  const all = (await call(people.admin.token, 'GET', '/api/v1/parcels?pageSize=200')).items.filter((p) => p.state === 'Active' && !p.parentTractId)
  const eligible = all.filter((p) => ['offer', 'negotiation', 'agreement', 'closing'].includes(p.currentStepKey))
  const pool = [...eligible, ...all.filter((p) => !eligible.includes(p))]
  if (pool.length < 30) throw new Error('Run scripts/seed-demo-data.mjs first: the relocation demo needs at least 30 parcels.')
  let next = 0
  const take = () => pool[next++]

  const flagOwners = async (parcel, count, { newOwners = 0 } = {}) => {
    for (let i = 0; i < newOwners; i++) await call(people.designer.token, 'POST', `/api/v1/parcels/${parcel.id}/parties`, { newParty: { kind: 'Person', name: owner() }, role: 'FeeOwner', ownerType: 'Fee Owner', leaseOnFile: false })
    let parties = await call(people.admin.token, 'GET', `/api/v1/parcels/${parcel.id}/parties`)
    if (!parties.ownership.some((l) => l.role === 'FeeOwner')) { // a parcel with no owner of its own gets one, so there is someone to displace
      await call(people.designer.token, 'POST', `/api/v1/parcels/${parcel.id}/parties`, { newParty: { kind: 'Person', name: owner() }, role: 'FeeOwner', ownerType: 'Fee Owner', leaseOnFile: false })
      parties = await call(people.admin.token, 'GET', `/api/v1/parcels/${parcel.id}/parties`)
    }
    const links = parties.ownership.filter((l) => l.role === 'FeeOwner').slice(0, count)
    for (const l of links) await call(people.admin.token, 'PUT', `/api/v1/party-links/${l.id}`, { sharePercent: l.sharePercent, notes: l.notes, leaseOnFile: false, ownerType: l.ownerType, displacementRequired: true })
    return links.map((l) => ({ id: l.id, name: l.party.name }))
  }
  const caseBody = (parcel, link, number, stage, extra = {}) => {
    const kind = (extra.relocationType) ?? undefined
    const notes = [{ noteDate: day(-60), text: 'Case opened after the owner was flagged for displacement.' }, { noteDate: day(-30), text: pick(['Interview done; family prefers to rent.', 'Owner is cooperative.', 'Waiting for the comparable housing list.']) }, { noteDate: day(-9), text: pick(['Replacement housing options sent.', 'Moving quote requested.', 'Follow-up call scheduled.']) }]
    return {
      details: { ...details(number, link.name, stage, extra, { site: parcel.address }), partyLinkId: link.id },
      statusNotes: notes.slice(0, Math.min(3, 1 + stage)),
      agents: AGENTS.map((agentName) => ({ agentName })),
      contacts: [
        { name: link.name, contactType: 'Family member', contact: '(512) 555-0' + int(100, 199), email: 'owner@example.com', address: `${int(100, 9000)} Oak St, Austin, TX`, isPrimary: true },
        { name: owner(), contactType: pick(['Family member', 'Representative', 'Attorney']), contact: '(512) 555-0' + int(200, 299), email: 'contact@example.com', address: `${int(100, 9000)} Elm St, Austin, TX`, isPrimary: false },
      ],
      eligibilities: stage >= 2 ? [{ eligibilityType: pick(ELIGIBILITY), claimLimit: int(5, 40) * 1000, submittedForApproval: day(-int(60, 90)), approved: stage >= 3 ? day(-int(30, 55)) : null }] : [],
      contactLog: [0, 1, 2].slice(0, Math.min(3, 1 + stage)).map((i) => ({ contactedAt: new Date(Date.now() - int(2 + i * 12, 14 + i * 12) * 86_400_000).toISOString(), mode: pick(MODES), contactedBy: pick(AGENTS), contactPersons: link.name, notes: pick(['Explained the relocation benefits and next steps.', 'Reviewed the replacement housing list.', 'Confirmed the move date and mover quote.', 'Left a voicemail; will call again.']) })),
    }
  }
  const addCase = async (parcel, link, number, stage, extra = {}) => {
    await ensureOnTeam(parcel)
    return call(people.ra.token, 'POST', `/api/v1/parcels/${parcel.id}/relocations`, caseBody(parcel, link, number, stage, extra))
  }
  // The Relocation people go on the parcel's team so "Assigned to" has them to choose from.
  const agentIds = Object.fromEntries((await call(people.admin.token, 'GET', '/api/v1/library/agents')).map((a) => [a.name, a.id]))
  const onTeam = new Set()
  async function ensureOnTeam(parcel) {
    if (onTeam.has(parcel.id)) return
    const have = (await call(people.pm.token, 'GET', `/api/v1/parcels/${parcel.id}/team`)).map((m) => m.name)
    for (const name of AGENTS) if (agentIds[name] && !have.includes(name)) await call(people.pm.token, 'POST', `/api/v1/parcels/${parcel.id}/team`, { agentId: agentIds[name], notes: 'Relocation advisor for the displaced owners.' })
    onTeam.add(parcel.id)
  }

  let count = 0
  const note = (m) => console.log('  ' + m)

  // A. single owner, relocation complete → the parcel becomes Acquired
  for (let i = 0; i < 3; i++) {
    const parcel = take(); const [link] = await flagOwners(parcel, 1)
    const c = await addCase(parcel, link, `R-${parcel.number}-1`, 8); count++
    await payment(parcel.id, c.id, 'paid', int(4, 18) * 1000)
    note(`parcel ${parcel.number} (${parcel.projectNumber}): relocated, now Acquired`)
  }
  // C. two displaced owners, both complete → Acquired
  { const parcel = take(); const links = await flagOwners(parcel, 2, { newOwners: 1 })
    for (const [i, l] of links.entries()) { const c = await addCase(parcel, l, `R-${parcel.number}-${i + 1}`, 8); count++; await payment(parcel.id, c.id, 'paid', int(5, 30) * 1000) }
    note(`parcel ${parcel.number}: two owners, both relocated, now Acquired`) }
  // B. two displaced owners, one done and one still moving → "1 of 2"
  for (let i = 0; i < 2; i++) {
    const parcel = take(); const links = await flagOwners(parcel, 2, { newOwners: 1 })
    await addCase(parcel, links[0], `R-${parcel.number}-1`, 8); await payment(parcel.id, (await call(people.ra.token, 'GET', `/api/v1/parcels/${parcel.id}/relocations`))[0].id, 'paid', 6500)
    const c = await addCase(parcel, links[1], `R-${parcel.number}-2`, i === 0 ? 6 : 7); count += 2
    await payment(parcel.id, c.id, i === 0 ? 'half' : 'requested', 42000)
    note(`parcel ${parcel.number}: two owners, 1 of 2 relocated`)
  }
  // D. single owner at every stage; payments in every state for the later ones
  const stages = [0, 1, 2, 3, 4, 5, 5, 6, 6, 7, 7, 7]
  const kinds = ['requested', 'approved', 'half', 'rejected', 'requested', 'approved', 'paid']
  for (const [i, stage] of stages.entries()) {
    const parcel = take(); const [link] = await flagOwners(parcel, 1)
    // the second "vacated" case vacated about 17 months ago, so its 18-month claim period is nearly over
    const extra = i === 10 ? { ginMailedDate: day(-560), ginDeliveredDate: day(-557), initialInterview: day(-545), actualVacateDate: day(-515), targetVacateDate: day(-520) } : {}
    const c = await addCase(parcel, link, `R-${parcel.number}-1`, stage, extra); count++
    if (stage >= 5) await payment(parcel.id, c.id, kinds[(i + stage) % kinds.length], [1800, 4200, 9800, 18500, 32000, 54000][(i + stage) % 6], stage === 5 && i === 5)
    note(`parcel ${parcel.number}: relocation at stage ${stage}${i === 10 ? ' (claim period ending)' : ''}`)
  }
  // E. an ineligible displacee, and a denied claim under appeal
  { const parcel = take(); const [link] = await flagOwners(parcel, 1)
    await addCase(parcel, link, `R-${parcel.number}-1`, 3, { ineligibleForRelocation: true, eligibilityException: 'Eviction for cause' }); count++; note(`parcel ${parcel.number}: ineligible (eviction for cause)`) }
  { const parcel = take(); const [link] = await flagOwners(parcel, 1)
    await addCase(parcel, link, `R-${parcel.number}-1`, 7, { claimDeniedDate: day(-30), appealFiledDate: day(-12), appealOutcome: null }); count++; note(`parcel ${parcel.number}: claim denied, appeal filed`) }
  // F. flagged owners with no relocation case yet
  for (let i = 0; i < 2; i++) { const parcel = take(); await flagOwners(parcel, 1); note(`parcel ${parcel.number}: displaced owner, no case yet`) }

  console.log(`\nDone: ${count} relocation cases on ${next} parcels.`)
  console.log('Sign in as relocationagent (cases), rowprojectmanager (payments to sign, Program view), rowfinance (payments to pay) or admin; password qwertyuiop.')
}

main().catch((e) => { console.error(`\nSeeding stopped: ${e.message}`); process.exit(1) })
