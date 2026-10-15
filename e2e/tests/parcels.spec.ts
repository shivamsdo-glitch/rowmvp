import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { devToken, level, newId, ROLES, sessionToken, signInViaUi, signOutViaUi } from './support'

/**
 * Phase 1, slice 2 — parcel core (ROW-002, 005-008, 010, 011, 071-074, 233-237). Real browser, real UI, real API, real Postgres.
 * Case ids match vault/acceptance/<Role>.md. Tests share one database and run in parallel, so every test makes its own
 * project (and so its own parcel numbers) and finds things by that project. Tests that change agency-wide settings
 * (Administrator) run one at a time and put everything back.
 */

const canCreate = (role: string) => ['S', 'P'].includes(level(role, 'parcels') ?? '')
const canDelete = (role: string) => level(role, 'parcels') === 'P'
const mayOverride = (role: string) => role === 'Admin' || role === 'ProjectManager'
const unique = (label: string) => `${label} ${newId().slice(0, 8)}`

type Headers = { Authorization: string }
interface Made { id: string; number: string; name: string }

const sidebar = (page: Page) => page.getByRole('complementary', { name: 'Primary' })

/** An administrator's API session, used only to set scenes up. What is being tested always goes through the screens or the role's own token. */
async function adminHeaders(request: APIRequestContext): Promise<Headers> {
  return { Authorization: `Bearer ${await devToken(request, 'Admin')}` }
}

async function makeProject(request: APIRequestContext, headers: Headers, oversightType: 'Local' | 'Federal' = 'Local'): Promise<Made> {
  const name = unique(`Parcel scene ${oversightType}`)
  const res = await request.post('/api/v1/projects', { headers, data: { name, oversightType } })
  expect(res.status()).toBe(201)
  const p = await res.json()
  return { id: p.id, number: p.number, name }
}

interface ParcelFields { address?: string; areaSqFt?: number; groupId?: string | null; parentTractId?: string | null; number?: string | null; statusId?: string | null }
const parcelBody = (projectId: string | undefined, f: ParcelFields = {}) => ({
  projectId, number: f.number ?? null, parentTractId: f.parentTractId ?? null, groupId: f.groupId ?? null, legalDescription: null,
  address: f.address ?? null, areaSqFt: f.areaSqFt ?? 1000, propertyType: null, statusId: f.statusId ?? null, acquisitionType: 'Standard', customValues: null,
})

async function makeParcel(request: APIRequestContext, headers: Headers, project: Made, f: ParcelFields = {}) {
  const res = await request.post('/api/v1/parcels', { headers, data: parcelBody(project.id, f) })
  expect(res.status(), await res.text()).toBe(201)
  return (await res.json()) as { id: string; number: string; areaSqFt: number }
}

const getParcel = async (request: APIRequestContext, headers: Headers, id: string) => {
  const res = await request.get(`/api/v1/parcels/${id}`, { headers })
  expect(res.status()).toBe(200)
  return res.json()
}

async function openParcel(page: Page, id: string, heading: RegExp | string) {
  await page.goto(`/parcels/${id}`)
  await expect(page.getByRole('heading', { level: 1 })).toContainText(heading)
}

const stepList = (page: Page) => page.getByRole('list', { name: 'Workflow steps' })

// ───────────────────────────────── Project Manager ─────────────────────────────────
test.describe('Project Manager · parcels', () => {
  test('AT-PM-21 builds a project’s parcels: group, parent tract, a parcel, then splits it with a remnant', async ({ page, request }) => {
    const admin = await adminHeaders(request)
    const project = await makeProject(request, admin)
    await signInViaUi(page, 'ProjectManager')

    // Group and parent tract on the project's Parcels tab.
    await page.goto(`/projects/${project.id}`)
    await page.getByRole('tab', { name: 'Parcels' }).click()
    const group = page.getByRole('form', { name: 'Add a group' })
    await group.getByRole('button', { name: 'Add group' }).click()
    await expect(page.getByText('Group name is required.')).toBeVisible()
    await group.getByLabel('Group name').fill('North segment')
    await group.getByRole('button', { name: 'Add group' }).click()
    await expect(page.getByRole('region', { name: 'Parcel groups' }).getByRole('row', { name: /North segment/ })).toBeVisible()
    const tract = page.getByRole('form', { name: 'Add a parent tract' })
    await tract.getByLabel('Tract number').fill('T-1001')
    await tract.getByLabel('Tax parcel numbers').fill('APN-1, APN-2')
    await tract.getByRole('button', { name: 'Add tract' }).click()
    await expect(page.getByRole('row', { name: /T-1001/ })).toContainText('APN-1, APN-2')

    // A parcel from the project's own list: the project is already chosen, the number is generated.
    await page.getByRole('link', { name: /Open this project's parcels/ }).click()
    await expect(page).toHaveURL(new RegExp(`projectId=${project.id}`))
    await page.getByRole('button', { name: 'New parcel' }).click()
    const form = page.getByRole('form', { name: 'New parcel' })
    await form.getByRole('button', { name: 'Create parcel' }).click()
    await expect(form.getByText('Area must be zero or more square feet.')).toBeVisible()
    await expect(form.getByLabel(/^Project/)).toHaveValue(project.id)
    await form.getByLabel(/^Area/).fill('43560')
    await form.getByLabel('Address').fill('100 Harbor Rd')
    await form.getByLabel('Group', { exact: true }).selectOption({ label: 'North segment' })
    await form.getByLabel('Parent tract').selectOption({ label: 'T-1001' })
    await form.getByLabel('Legal description').fill('Lot 4, Block 2, Harbor Subdivision')
    await form.getByRole('button', { name: 'Create parcel' }).click()

    await expect(page.getByRole('heading', { level: 1 })).toContainText(`${project.number} · Parcel 001`)
    const synopsis = page.getByRole('region', { name: 'Parcel synopsis' })
    await expect(synopsis).toContainText('North segment')
    await expect(synopsis).toContainText('T-1001')
    await expect(synopsis).toContainText('APN-1, APN-2')
    await expect(synopsis).toContainText('43,560 sq ft (1 acres)')
    await expect(synopsis).toContainText('Lot 4, Block 2, Harbor Subdivision')

    // Split: two parts and the remainder becomes the remnant.
    await page.getByRole('button', { name: 'Split', exact: true }).click()
    const split = page.getByRole('form', { name: 'Split parts' })
    await split.getByRole('button', { name: 'Split parcel' }).click()
    await expect(split.getByText('Each part needs an area greater than zero.')).toBeVisible()
    await split.getByLabel('Part 1 area (sq ft)').fill('10000')
    await split.getByLabel('Part 2 area (sq ft)').fill('50000')
    await expect(split).toContainText('The parts exceed the parcel by 16,440 sq ft')
    await split.getByLabel('Part 2 area (sq ft)').fill('20000')
    await expect(split).toContainText('Remnant: 13,560 sq ft')
    await split.getByRole('button', { name: 'Split parcel' }).click()

    const result = page.getByRole('region', { name: 'Split result' })
    await expect(result).toContainText('3 new parcels created')
    await expect(result.getByRole('link', { name: '001-A' })).toBeVisible()
    await expect(result.getByRole('link', { name: '001-B' })).toBeVisible()
    await expect(result).toContainText('001-R')
    await expect(page.getByText(/This parcel was split and is kept on record only/)).toBeVisible()
    await expect(page.getByRole('button', { name: 'Edit parcel' })).toHaveCount(0)

    // The server agrees: the original is kept, the parts and remnant exist, and the area adds up.
    const token = await sessionToken(page)
    const headers = { Authorization: `Bearer ${token}` }
    const all = await (await request.get(`/api/v1/parcels?projectId=${project.id}&includeSuperseded=true&pageSize=50`, { headers })).json()
    const byNumber = Object.fromEntries(all.items.map((p: any) => [p.number, p]))
    expect(Object.keys(byNumber).sort()).toEqual(['001', '001-A', '001-B', '001-R'])
    expect(byNumber['001'].state).toBe('Split')
    expect(byNumber['001-R']).toMatchObject({ isRemnant: true, areaSqFt: 13560 })
    expect(byNumber['001-A'].areaSqFt + byNumber['001-B'].areaSqFt + byNumber['001-R'].areaSqFt).toBe(43560)

    // The project roll-up counts live parcels only: the split-away original is not double counted.
    await page.goto(`/projects/${project.id}`)
    await page.getByRole('tab', { name: 'Parcels' }).click()
    await expect(page.getByRole('region', { name: 'Parcel roll-up' })).toContainText('3 parcels')
    await expect(page.getByRole('region', { name: 'Parcel roll-up' })).toContainText('1 acres')
    await expect(page.getByRole('table', { name: 'By group' })).toContainText('North segment')
  })

  test('AT-PM-21 a large project pages through every parcel, and the board says when it shows only the first 200', async ({ page, request }) => {
    test.setTimeout(120_000)
    const admin = await adminHeaders(request)
    const project = await makeProject(request, admin)
    for (let start = 0; start < 201; start += 25) {
      await Promise.all(Array.from({ length: Math.min(25, 201 - start) }, (_, i) => makeParcel(request, admin, project, { address: `Bulk ${start + i + 1} Rd` })))
    }
    await signInViaUi(page, 'ProjectManager')
    await page.goto(`/parcels?projectId=${project.id}`)

    await expect(page.getByText('201 parcels')).toBeVisible()
    await expect(page.getByText('Page 1 of 9')).toBeVisible()

    await page.getByRole('button', { name: 'Board' }).click()
    await expect(page.getByText('Showing the first 200 of 201 parcels. Narrow the filters to see the rest.')).toBeVisible()
    await expect(page.getByRole('region', { name: 'Inactive column' }).getByRole('listitem')).toHaveCount(200)

    // Narrowing the filters brings the note back to nothing, and the board shows the whole result.
    await page.getByLabel('Search parcels').fill('Bulk 201 Rd')
    await expect(page.getByRole('region', { name: 'Inactive column' }).getByRole('listitem')).toHaveCount(1)
    await expect(page.getByText(/Showing the first/)).toHaveCount(0)
  })

  test('AT-PM-21 refuses a duplicate parcel number and an unbalanced area, and never reuses a number', async ({ page, request }) => {
    const admin = await adminHeaders(request)
    const project = await makeProject(request, admin)
    const existing = await makeParcel(request, admin, project, { address: '1 First St' })
    expect(existing.number).toBe('001')
    await signInViaUi(page, 'ProjectManager')
    await openParcel(page, existing.id, `Parcel 001`)

    await page.getByRole('button', { name: 'Edit parcel' }).click()
    const form = page.getByRole('form', { name: 'Edit parcel' })
    await form.getByLabel('Area (sq ft)').fill('-5')
    await form.getByRole('button', { name: 'Save parcel' }).click()
    await expect(form.getByText('Area must be zero or more square feet.')).toBeVisible()
    await form.getByLabel('Area (sq ft)').fill('1500')
    await form.getByLabel('Address').fill('1 First St, Unit B')
    await form.getByRole('button', { name: 'Save parcel' }).click()
    await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toContainText('1,500 sq ft')
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
    await expect(page.getByText('1 First St, Unit B').first()).toBeVisible()

    // A second parcel, then try to give it the first one's number (in different letters).
    const second = await makeParcel(request, admin, project, { address: '2 Second St' })
    expect(second.number).toBe('002')
    await openParcel(page, second.id, 'Parcel 002')
    await page.getByRole('button', { name: 'Edit parcel' }).click()
    const edit = page.getByRole('form', { name: 'Edit parcel' })
    await edit.getByLabel('Parcel number').fill('001')
    await edit.getByRole('button', { name: 'Save parcel' }).click()
    await expect(edit.getByRole('alert')).toContainText("This project already has a parcel numbered '001'.")

    // Splitting retires the original but its number is never handed out again.
    await openParcel(page, second.id, 'Parcel 002')
    await page.getByRole('button', { name: 'Split', exact: true }).click()
    const split = page.getByRole('form', { name: 'Split parts' })
    await split.getByLabel('Part 1 area (sq ft)').fill('400')
    await split.getByLabel('Part 2 area (sq ft)').fill('300')
    await split.getByRole('button', { name: 'Split parcel' }).click()
    await expect(page.getByRole('region', { name: 'Split result' })).toBeVisible()
    const third = await makeParcel(request, admin, project, { address: '3 Third St' })
    expect(third.number).toBe('003')
  })

  test('AT-PM-23 records interests, milestones, the notice to owner and a site visit; deadlines and Next actions follow', async ({ page, request }) => {
    const admin = await adminHeaders(request)
    const project = await makeProject(request, admin)
    const parcel = await makeParcel(request, admin, project, { address: '55 Milestone Way', areaSqFt: 8000 })
    await signInViaUi(page, 'ProjectManager')
    await openParcel(page, parcel.id, 'Parcel 001')

    // Interests (ROW-234).
    await page.getByRole('tab', { name: 'Interests' }).click()
    await expect(page.getByText('No interests recorded')).toBeVisible()
    const interest = page.getByRole('form', { name: 'Add an interest' })
    await interest.getByRole('button', { name: 'Add interest' }).click()
    await expect(interest.getByText('Area must be zero or more square feet.')).toBeVisible()
    await interest.getByLabel(/^Interest type/).selectOption('PermanentEasement')
    await interest.getByLabel(/^Interest area/).fill('2500')
    await interest.getByLabel('Interest notes').fill('Storm drain easement')
    await interest.getByRole('button', { name: 'Add interest' }).click()
    await expect(page.getByRole('row', { name: /Permanent easement/ })).toContainText('2,500 sq ft')

    // Milestones (ROW-010): the standard ones are there and cannot be removed; custom ones can be added and removed.
    await page.getByRole('tab', { name: 'Milestones & deadlines' }).click()
    for (const name of ['Parcel set up', 'Notice to owner', 'Offer delivered', 'Agreement signed', 'Closed']) {
      await expect(page.getByRole('row').filter({ hasText: name }).first()).toBeVisible()
      await expect(page.getByRole('button', { name: `Remove milestone ${name}` })).toHaveCount(0)
    }
    const add = page.getByRole('form', { name: 'Add a milestone' })
    await add.getByRole('button', { name: 'Add milestone' }).click()
    await expect(add.getByText('Milestone name is required.')).toBeVisible()
    await add.getByLabel(/^Milestone name/).fill('Right of entry')
    await add.getByLabel('Due on').fill('2026-12-01')
    await add.getByRole('button', { name: 'Add milestone' }).click()
    await expect(page.getByRole('row', { name: /Right of entry/ })).toContainText('Dec 1, 2026')
    await add.getByLabel(/^Milestone name/).fill('closed')
    await add.getByRole('button', { name: 'Add milestone' }).click()
    await expect(add.getByRole('alert')).toContainText("This parcel already has a milestone named 'closed'.")
    await page.getByRole('button', { name: 'Remove milestone Right of entry' }).click()
    await expect(page.getByRole('row', { name: /Right of entry/ })).toHaveCount(0)

    // Notice to owner (ROW-237): needs a method; saving it completes the milestone and starts the offer deadline.
    await page.getByRole('tab', { name: 'Site visits & notice' }).click()
    const notice = page.getByRole('form', { name: 'Notice to owner' })
    await notice.getByLabel('Notice issued on').fill('2026-01-05')
    await notice.getByRole('button', { name: 'Save notice' }).click()
    await expect(notice.getByText('Choose how the notice was delivered.')).toBeVisible()
    await notice.getByLabel('Delivery method').selectOption('CertifiedMail')
    await notice.getByRole('button', { name: 'Save notice' }).click()
    await expect(notice.getByText('Notice saved.')).toBeVisible()

    // Site visit (ROW-237).
    const visit = page.getByRole('form', { name: 'Record a site visit' })
    await visit.getByRole('button', { name: 'Record visit' }).click()
    await expect(visit.getByText('Visit date is required.')).toBeVisible()
    await visit.getByLabel('Visit date').fill('2026-10-01')
    await visit.getByLabel('Owner contact confirmed').check()
    await visit.getByLabel('Visit notes').fill('Walked the frontage with the owner')
    await visit.getByRole('button', { name: 'Record visit' }).click()
    await expect(page.getByRole('row', { name: /Oct 1, 2026/ })).toContainText('Yes')

    // Milestone dates are facts (ROW-010): the notice's date stays with the notice, and nothing completes in the future.
    await page.getByRole('tab', { name: 'Milestones & deadlines' }).click()
    await page.getByRole('button', { name: 'Edit milestone Notice to owner' }).click()
    const locked = page.getByRole('form', { name: 'Edit milestone Notice to owner' })
    await expect(locked.getByLabel('Completed on')).toBeDisabled()
    await expect(locked.getByLabel('Completed on')).toHaveValue('2026-01-05')
    await expect(locked).toContainText('Set by recording the notice on the Site visits & notice tab.')
    await locked.getByRole('button', { name: 'Cancel' }).click()
    await page.getByRole('button', { name: 'Edit milestone Offer delivered' }).click()
    const dated = page.getByRole('form', { name: 'Edit milestone Offer delivered' })
    await dated.getByLabel('Completed on').fill(new Date(Date.now() + 2 * 86_400_000).toISOString().slice(0, 10))
    await dated.getByRole('button', { name: 'Save milestone' }).click()
    await expect(dated.getByText('A milestone cannot be completed in the future.')).toBeVisible()
    await expect(dated.getByLabel('Completed on')).toHaveAttribute('aria-invalid', 'true')
    await dated.getByLabel('Completed on').fill('1899-01-01')
    await dated.getByRole('button', { name: 'Save milestone' }).click()
    await expect(dated.getByText('Enter a valid completion date.')).toBeVisible()
    await dated.getByRole('button', { name: 'Cancel' }).click()

    // Deadlines (ROW-073): 90 days after 5 Jan 2026 was 5 Apr 2026, and nothing has been offered.
    await page.getByRole('tab', { name: 'Milestones & deadlines' }).click()
    const deadline = page.getByRole('table', { name: 'Deadlines' }).getByRole('row', { name: /Offer within 90 days/ })
    await expect(deadline).toContainText('Apr 5, 2026')
    await expect(deadline).toContainText('Overdue')
    await expect(deadline).toContainText('days late')

    // Reminders (ROW-074): the Dashboard lists it among the next actions, linking back to the parcel.
    await sidebar(page).getByRole('link', { name: 'Home' }).click()
    const next = page.getByRole('region', { name: 'Next actions' })
    const item = next.getByRole('listitem').filter({ hasText: `${project.number} · Parcel 001` }).first() // one per overdue rule
    await expect(item).toContainText('Overdue by')
    await item.getByRole('link').click()
    await expect(page.getByRole('heading', { level: 1 })).toContainText(`${project.number} · Parcel 001`)

    // The synopsis prints (ROW-008).
    await page.evaluate(() => { (window as any).__printed = 0; window.print = () => { (window as any).__printed += 1 } })
    await page.getByRole('tab', { name: 'Summary' }).click()
    await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toContainText('by certified mail')
    await page.getByRole('button', { name: /Print synopsis/ }).click()
    expect(await page.evaluate(() => (window as any).__printed)).toBe(1)

    // The server holds the same facts.
    const token = await sessionToken(page)
    const headers = { Authorization: `Bearer ${token}` }
    const saved = await getParcel(request, headers, parcel.id)
    expect(saved.interests).toHaveLength(1)
    expect(saved.interests[0]).toMatchObject({ type: 'PermanentEasement', areaSqFt: 2500 })
    expect(saved.siteVisits).toHaveLength(1)
    expect(saved).toMatchObject({ noticeIssuedOn: '2026-01-05', noticeMethod: 'CertifiedMail' })
    expect(saved.milestones.find((m: any) => m.name === 'Notice to owner').completedOn).toBe('2026-01-05')
    expect(saved.milestones.some((m: any) => m.name === 'Right of entry')).toBe(false)
    const noticeMilestone = saved.milestones.find((m: any) => m.name === 'Notice to owner')
    const drift = await request.put(`/api/v1/parcels/${parcel.id}/milestones/${noticeMilestone.id}`, { headers, data: { completedOn: '2026-01-06' } })
    expect(drift.status()).toBe(400)
    expect((await drift.json()).title).toBe('milestone_notice_managed')
    expect((await getParcel(request, headers, parcel.id)).noticeIssuedOn).toBe('2026-01-05')
    const offer = saved.deadlines.find((d: any) => d.name.startsWith('Offer within 90 days'))
    expect(offer).toMatchObject({ dueOn: '2026-04-05', status: 'Overdue' })
    const reminders = await (await request.get('/api/v1/reminders', { headers })).json()
    expect(reminders.some((r: any) => r.parcelId === parcel.id && r.status === 'Overdue')).toBe(true)
  })

  test('AT-PM-24 is held at the gate for offers, can only be overridden with a reason, and moves on when the gate opens', async ({ page, request }) => {
    const admin = await adminHeaders(request)
    const project = await makeProject(request, admin, 'Federal') // held: no NEPA decision or acquisition authorization
    const parcel = await makeParcel(request, admin, project, { address: '7 Gate Rd' })
    await signInViaUi(page, 'ProjectManager')
    await openParcel(page, parcel.id, 'Parcel 001')
    await page.getByRole('tab', { name: 'Workflow' }).click()
    await expect(stepList(page).getByRole('listitem').filter({ hasText: 'Parcel set up' })).toHaveAttribute('aria-current', 'step')

    // In order is free.
    await page.getByRole('button', { name: 'Advance to Title & survey' }).click()
    await expect(page.getByRole('button', { name: 'Advance to Appraisal & just compensation' })).toBeVisible()
    await page.getByRole('button', { name: 'Advance to Appraisal & just compensation' }).click()

    // The offer step is held while the project is held, and says why.
    await expect(page.getByText('Offer is on hold.')).toBeVisible()
    await expect(page.getByText(/This cannot be overridden/)).toBeVisible()
    await expect(page.getByRole('button', { name: 'Advance to Offer' })).toBeDisabled()
    const move = page.getByRole('form', { name: 'Move to another step' })
    await move.getByLabel('Move to step').selectOption('closing')
    await expect(move.getByText(/on hold until the project's acquisition gate opens/)).toBeVisible()
    await expect(move.getByRole('button', { name: 'Move parcel' })).toBeDisabled()

    // The server refuses too, even with a reason from someone who may override.
    const token = await sessionToken(page)
    const headers = { Authorization: `Bearer ${token}` }
    const refused = await request.post(`/api/v1/parcels/${parcel.id}/advance`, { headers, data: { targetStepKey: 'offer', overrideReason: 'Please let me' } })
    expect(refused.status()).toBe(409)
    expect((await refused.json()).title).toBe('acquisition_gate_closed')

    // Going back is an override: a reason is required, and it is recorded.
    await move.getByLabel('Move to step').selectOption('setup')
    await move.getByRole('button', { name: 'Move parcel' }).click()
    await expect(move.getByText('A reason is required when moving out of order.')).toBeVisible()
    await move.getByLabel(/^Reason for the override/).fill('Survey must be redone')
    await move.getByRole('button', { name: 'Move parcel' }).click()
    const history = page.getByRole('table', { name: 'Step history' })
    await expect(history.getByRole('row', { name: /Survey must be redone/ })).toContainText('Override')
    await expect(stepList(page).getByRole('listitem').filter({ hasText: 'Parcel set up' })).toHaveAttribute('aria-current', 'step')

    // An approved hardship exception opens the project; the offer step then opens.
    await page.goto(`/projects/${project.id}`)
    await page.getByRole('tab', { name: 'Clearance' }).click()
    const exception = page.getByRole('form', { name: 'Acquisition exception' })
    await exception.getByLabel('Exception type').selectOption('Hardship')
    await exception.getByLabel('Approval date').fill('2026-03-01')
    await exception.getByLabel('Reason').fill('Owner medical hardship')
    await exception.getByRole('button', { name: 'Save exception' }).click()
    await expect(page.getByRole('status', { name: 'Acquisition status' })).toContainText('Offers and negotiations are open')

    await openParcel(page, parcel.id, 'Parcel 001')
    await page.getByRole('tab', { name: 'Workflow' }).click()
    await page.getByRole('button', { name: 'Advance to Title & survey' }).click()
    await page.getByRole('button', { name: 'Advance to Appraisal & just compensation' }).click()
    await expect(page.getByRole('button', { name: 'Advance to Offer' })).toBeEnabled()
    await page.getByRole('button', { name: 'Advance to Offer' }).click()
    await expect(page.getByRole('button', { name: 'Advance to Negotiation' })).toBeVisible()

    const saved = await getParcel(request, headers, parcel.id)
    expect(saved.workflow.currentKey).toBe('offer')
    const overrides = saved.stepHistory.filter((e: any) => e.isOverride)
    expect(overrides).toHaveLength(1)
    expect(overrides[0]).toMatchObject({ fromStepKey: 'appraisal', toStepKey: 'setup', reason: 'Survey must be redone' })
  })

  test('AT-PM-25 updates the status and group of many parcels at once, sees them on the board, and combines two', async ({ page, request }) => {
    const admin = await adminHeaders(request)
    const project = await makeProject(request, admin)
    const groupRes = await request.post(`/api/v1/projects/${project.id}/groups`, { headers: admin, data: { name: 'Segment A' } })
    expect(groupRes.status()).toBe(201)
    const parcels = [
      await makeParcel(request, admin, project, { address: '1 Batch Rd', areaSqFt: 1000 }),
      await makeParcel(request, admin, project, { address: '2 Batch Rd', areaSqFt: 2000 }),
      await makeParcel(request, admin, project, { address: '3 Batch Rd', areaSqFt: 3000 }),
    ]
    await signInViaUi(page, 'ProjectManager')
    await page.goto(`/parcels?projectId=${project.id}`)
    const table = page.getByRole('table', { name: 'Parcels' })
    await expect(table.getByRole('row')).toHaveCount(4)
    await expect(table.getByRole('row', { name: /1 Batch Rd/ })).toContainText('Inactive')

    // Nothing chosen to apply is explained, not silently ignored.
    await page.getByRole('button', { name: 'Select all on this page' }).click()
    await expect(page.getByText('3 parcels selected')).toBeVisible()
    await page.getByRole('button', { name: 'Apply to selected' }).click()
    await expect(page.getByText('Choose a status or a group to apply.')).toBeVisible()

    await page.getByLabel('Set status').selectOption({ label: 'Initial Offer' })
    await page.getByLabel('Set group').selectOption({ label: 'Segment A' })
    await page.getByRole('button', { name: 'Apply to selected' }).click()
    await expect(page.getByText('Updated 3 parcels.')).toBeVisible()
    for (const a of ['1 Batch Rd', '2 Batch Rd', '3 Batch Rd']) {
      await expect(table.getByRole('row', { name: new RegExp(a) })).toContainText('Initial Offer')
      await expect(table.getByRole('row', { name: new RegExp(a) })).toContainText('Segment A')
    }

    // The board groups them in the status column, coloured from the agency's status list.
    await page.getByRole('button', { name: 'Board' }).click()
    const column = page.getByRole('region', { name: 'Initial Offer column' })
    await expect(column.getByRole('listitem')).toHaveCount(3)
    await expect(page.getByRole('region', { name: 'Inactive column' })).toHaveCount(0)
    await page.getByRole('button', { name: 'Table' }).click()

    // Combine two: a new parcel with the summed area; the originals are retired and leave the list.
    await page.getByRole('checkbox', { name: 'Select parcel 001' }).check()
    await page.getByRole('checkbox', { name: 'Select parcel 002' }).check()
    await page.getByRole('button', { name: 'Combine selected' }).click()
    await expect(page.getByRole('heading', { level: 1 })).toContainText(`${project.number} · Parcel 004`)
    await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toContainText('3,000 sq ft')

    const token = await sessionToken(page)
    const headers = { Authorization: `Bearer ${token}` }
    const all = await (await request.get(`/api/v1/parcels?projectId=${project.id}&includeSuperseded=true`, { headers })).json()
    const byNumber = Object.fromEntries(all.items.map((p: any) => [p.number, p]))
    expect(byNumber['001'].state).toBe('Combined')
    expect(byNumber['002'].state).toBe('Combined')
    expect(byNumber['004']).toMatchObject({ state: 'Active', areaSqFt: 3000, statusName: 'Initial Offer', groupName: 'Segment A' })
    const live = await (await request.get(`/api/v1/parcels?projectId=${project.id}`, { headers })).json()
    expect(live.items.map((p: any) => p.number).sort()).toEqual(['003', '004'])
    const rollup = await (await request.get(`/api/v1/projects/${project.id}/parcel-rollup`, { headers })).json()
    expect(rollup).toMatchObject({ totalCount: 2, totalAreaSqFt: 6000 })
    expect(parcels).toHaveLength(3)
  })

  test('AT-PM-25 refuses to combine parcels of different projects, and says why', async ({ page, request }) => {
    const admin = await adminHeaders(request)
    const a = await makeProject(request, admin)
    const b = await makeProject(request, admin)
    const pa = await makeParcel(request, admin, a, { address: `A-${a.number} Cross Rd` })
    await makeParcel(request, admin, b, { address: `B-${b.number} Cross Rd` })
    await signInViaUi(page, 'ProjectManager')
    await page.goto('/parcels')
    await page.getByLabel('Search parcels').fill('Cross Rd')
    await expect(page.getByRole('table', { name: 'Parcels' }).getByRole('row')).toHaveCount(3) // header + the two parcels
    await page.getByRole('checkbox', { name: /Select parcel/ }).first().check()
    await page.getByRole('checkbox', { name: /Select parcel/ }).nth(1).check()
    await expect(page.getByText('2 parcels selected')).toBeVisible()
    await expect(page.getByLabel('Set group')).toBeDisabled()

    await page.getByRole('button', { name: 'Combine selected' }).click()

    await expect(page.getByText('Parcels from different projects cannot be combined.')).toBeVisible()
    expect((await getParcel(request, admin, pa.id)).state).toBe('Active')
  })
})

// ───────────────────────────────── Administrator ─────────────────────────────────
test.describe('Administrator · parcel rules', () => {
  // These change agency-wide rules, so they must not overlap each other. Each puts back what it changed.
  test.describe.configure({ mode: 'serial' })

  test('AT-AD-22 configures deadlines, a parcel field, a status and who may override; parcels follow the new rules', async ({ page, request }) => {
    const stamp = newId().slice(0, 8)
    const ruleName = `E2E rule ${stamp}`
    const fieldLabel = `E2E field ${stamp}`
    const fieldKey = `e2e_${stamp}`
    const statusName = `E2E status ${stamp}`
    const admin = await adminHeaders(request)
    const project = await makeProject(request, admin)
    const parcel = await makeParcel(request, admin, project, { address: '1 Config Rd' })
    expect((await request.put(`/api/v1/parcels/${parcel.id}/notice`, { headers: admin, data: { issuedOn: '2026-02-01', method: 'CertifiedMail' } })).status()).toBe(200)

    await signInViaUi(page, 'Admin')
    await sidebar(page).getByRole('link', { name: 'Configuration' }).click()
    try {
      // Deadline rule: checked before it is sent, then it is saved.
      const deadlines = page.getByRole('region', { name: 'Statutory deadlines' })
      await expect(deadlines.getByRole('row', { name: /Offer within 90 days/ })).toBeVisible()
      await deadlines.getByRole('button', { name: 'Add deadline rule' }).click()
      const rule = deadlines.getByRole('form', { name: 'Add a deadline rule' })
      await rule.getByRole('button', { name: 'Save rule' }).click()
      await expect(rule.getByText('Rule name is required.')).toBeVisible()
      await expect(rule.getByText('Days must be a whole number, zero or more.')).toBeVisible()
      await rule.getByLabel(/^Rule name/).fill(ruleName)
      await rule.getByLabel(/^Days allowed/).fill('45')
      await rule.getByLabel(/^Starts when this milestone is completed/).fill('Notice to owner')
      await rule.getByLabel(/^This milestone is then due/).fill('Agreement signed')
      await rule.getByRole('button', { name: 'Save rule' }).click()
      await expect(deadlines.getByText('Rule saved.')).toBeVisible()
      await expect(deadlines.getByRole('row', { name: new RegExp(ruleName) })).toContainText('45')

      // Custom field: a bad key is explained, then a list field is created and its key and type lock.
      const fields = page.getByRole('region', { name: 'Parcel custom fields' })
      await fields.getByRole('button', { name: 'Add custom field' }).click()
      const field = fields.getByRole('form', { name: 'Add a custom field' })
      await field.getByLabel(/^Field label/).fill(fieldLabel)
      await field.getByLabel(/^Field key/).fill('Bad Key!')
      await field.getByRole('button', { name: 'Save field' }).click()
      await expect(field.getByText(/The key starts with a lower-case letter/)).toBeVisible()
      await field.getByLabel(/^Field key/).fill(fieldKey)
      await field.getByLabel('Field type').selectOption('List')
      await field.getByRole('button', { name: 'Save field' }).click()
      await expect(field.getByText('A list field needs at least one option.')).toBeVisible()
      await field.getByLabel('List options').fill('Residential, Commercial')
      await field.getByRole('button', { name: 'Save field' }).click()
      await expect(fields.getByText('Field saved.')).toBeVisible()
      await fields.getByRole('button', { name: `Edit field ${fieldLabel}` }).click()
      await expect(fields.getByRole('form', { name: 'Edit a custom field' }).getByLabel(/^Field key/)).toBeDisabled()
      await expect(fields.getByRole('form', { name: 'Edit a custom field' }).getByLabel('Field type')).toBeDisabled()
      await fields.getByRole('button', { name: 'Cancel' }).click()

      // Status: a duplicate (in other letters) is refused, a new one is added.
      const statuses = page.getByRole('region', { name: 'Parcel statuses' })
      await statuses.getByRole('button', { name: 'Add status' }).click()
      const status = statuses.getByRole('form', { name: 'Add a status' })
      await status.getByLabel(/^Status name/).fill('INITIAL OFFER')
      await status.getByRole('button', { name: 'Save status' }).click()
      await expect(status.getByRole('alert')).toContainText("There is already a status named 'INITIAL OFFER'.")
      await status.getByLabel(/^Status name/).fill(statusName)
      await status.getByLabel(/^Status colour/).fill('#123456')
      await status.getByRole('button', { name: 'Save status' }).click()
      await expect(statuses.getByRole('row', { name: new RegExp(statusName) })).toContainText('#123456')

      // Who may override step order.
      const overrides = page.getByRole('region', { name: 'Step order overrides' })
      await expect(overrides).toContainText('ProjectManager, Admin')
      await overrides.getByLabel('Negotiator').check()
      await overrides.getByRole('button', { name: 'Save override roles' }).click()
      await expect(overrides.getByText('Override roles saved.')).toBeVisible()
      const gate = await (await request.get('/api/v1/parcel-rules/step-gate', { headers: admin })).json()
      expect(gate.extraOverrideRoles).toEqual(['Negotiator'])
      expect(gate.alwaysRoles.sort()).toEqual(['Admin', 'ProjectManager'])

      // The workflows are listed, read-only.
      const workflows = page.getByRole('region', { name: 'Acquisition workflows' })
      await expect(workflows.getByRole('row', { name: /Standard/ })).toContainText('Parcel set up → Title & survey')
      await expect(workflows.getByRole('button')).toHaveCount(0)

      // The rest of the app follows: a Project Manager's parcel shows the new rule, field and status.
      await signOutViaUi(page)
      await signInViaUi(page, 'ProjectManager')
      await openParcel(page, parcel.id, 'Parcel 001')
      await page.getByRole('tab', { name: 'Milestones & deadlines' }).click()
      const row = page.getByRole('table', { name: 'Deadlines' }).getByRole('row', { name: new RegExp(ruleName) })
      await expect(row).toContainText('Mar 18, 2026') // 1 Feb 2026 + 45 days
      await expect(row).toContainText('Overdue')

      await page.getByRole('tab', { name: 'Summary' }).click()
      await page.getByRole('button', { name: 'Edit parcel' }).click()
      const edit = page.getByRole('form', { name: 'Edit parcel' })
      await edit.getByLabel(fieldLabel).selectOption('Commercial')
      await edit.getByLabel('Status', { exact: true }).selectOption({ label: statusName })
      await edit.getByRole('button', { name: 'Save parcel' }).click()
      const synopsis = page.getByRole('region', { name: 'Parcel synopsis' })
      await expect(synopsis).toContainText(statusName)
      await expect(page.getByRole('region', { name: 'Agency fields' })).toContainText(`${fieldLabel}`)
      await expect(page.getByRole('region', { name: 'Agency fields' })).toContainText('Commercial')
      const saved = await getParcel(request, admin, parcel.id)
      expect(saved.customValues[fieldKey]).toBe('Commercial')
      expect(saved.statusName).toBe(statusName)
    } finally {
      // Put every agency-wide setting back (rules and fields cannot be deleted, so they are switched off).
      const rules = await (await request.get('/api/v1/parcel-rules/deadline-rules', { headers: admin })).json()
      for (const r of rules.filter((x: any) => x.name === ruleName))
        await request.put(`/api/v1/parcel-rules/deadline-rules/${r.id}`, { headers: admin, data: { name: r.name, triggerMilestone: r.triggerMilestone, targetMilestone: r.targetMilestone, days: r.days, isActive: false } })
      const defs = await (await request.get('/api/v1/parcel-rules/custom-fields', { headers: admin })).json()
      for (const f of defs.filter((x: any) => x.key === fieldKey))
        await request.put(`/api/v1/parcel-rules/custom-fields/${f.id}`, { headers: admin, data: { key: f.key, label: f.label, fieldType: f.fieldType, options: f.options, isRequired: false, isActive: false, sortOrder: f.sortOrder } })
      const sts = await (await request.get('/api/v1/parcel-rules/statuses', { headers: admin })).json()
      for (const s of sts.filter((x: any) => x.name === statusName))
        await request.put(`/api/v1/parcel-rules/statuses/${s.id}`, { headers: admin, data: { name: s.name, color: s.color, sortOrder: s.sortOrder, isActive: false, isInitial: false } })
      await request.put('/api/v1/parcel-rules/step-gate', { headers: admin, data: { extraOverrideRoles: [] } })
    }
  })

  test('AT-AD-22 an extra override role is recognised by the server, but read-only access still cannot move a parcel', async ({ request }) => {
    // The default access spec gives only ProjectManager and Admin both update and override, so the extra role's effect shows
    // as the `canOverride` flag a role is served; acting on it still needs update access (ADR 0008).
    const admin = await adminHeaders(request)
    const project = await makeProject(request, admin)
    const parcel = await makeParcel(request, admin, project)
    const negotiator = { Authorization: `Bearer ${await devToken(request, 'Negotiator')}` }
    try {
      expect((await getParcel(request, negotiator, parcel.id)).workflow.canOverride).toBe(false)
      expect((await request.put('/api/v1/parcel-rules/step-gate', { headers: admin, data: { extraOverrideRoles: ['Negotiator'] } })).status()).toBe(200)
      expect((await getParcel(request, negotiator, parcel.id)).workflow.canOverride).toBe(true)
      expect((await request.post(`/api/v1/parcels/${parcel.id}/advance`, { headers: negotiator, data: { targetStepKey: 'appraisal', overrideReason: 'skip' } })).status()).toBe(403)
      expect((await request.put('/api/v1/parcel-rules/step-gate', { headers: admin, data: { extraOverrideRoles: ['NotARole'] } })).status()).toBe(400)
    } finally {
      await request.put('/api/v1/parcel-rules/step-gate', { headers: admin, data: { extraOverrideRoles: [] } })
    }
    expect((await getParcel(request, negotiator, parcel.id)).workflow.canOverride).toBe(false)
  })

  test('AT-AD-26 sets the agency time zone; a project in another place can have its own, and parcels say which calendar they count in', async ({ page, request }) => {
    const admin = await adminHeaders(request)
    const original = (await (await request.get('/api/v1/agency/time-zone', { headers: admin })).json()).timeZoneId as string
    const project = await makeProject(request, admin)
    const parcel = await makeParcel(request, admin, project, { address: '9 Zone Rd' })

    try {
      // The administrator chooses the agency's time zone in Configuration.
      await signInViaUi(page, 'Admin')
      await sidebar(page).getByRole('link', { name: 'Configuration' }).click()
      const card = page.getByRole('region', { name: 'Time zone' })
      await card.getByRole('combobox', { name: 'Agency time zone' }).selectOption({ label: 'Central Time (Chicago)' })
      await card.getByRole('button', { name: 'Save time zone' }).click()
      await expect(card.getByText('Saved.')).toBeVisible()
      await expect(card).toContainText("Today's date there:")
      const agency = await (await request.get('/api/v1/agency/time-zone', { headers: admin })).json()
      expect(agency.timeZoneId).toBe('America/Chicago')
      expect(agency.options.length).toBeGreaterThan(10)

      // A Project Manager sees the project follow it, and the parcel says so.
      await signOutViaUi(page)
      await signInViaUi(page, 'ProjectManager')
      await page.goto(`/projects/${project.id}`)
      const section = page.getByRole('region', { name: 'Project time zone' })
      await expect(section).toContainText('Central Time (Chicago)')
      await expect(section).toContainText('follows the agency')
      await openParcel(page, parcel.id, 'Parcel 001')
      await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toContainText(/Central (Daylight|Standard) Time/)

      // The project is somewhere else: its own zone wins over the agency's.
      await page.goto(`/projects/${project.id}`)
      const form = page.getByRole('form', { name: 'Set the project time zone' })
      await expect(form.getByRole('option', { name: 'Follow the agency (Central Time (Chicago))' })).toBeAttached()
      await form.getByLabel('Project time zone').selectOption({ label: 'Chamorro Time (Guam, Northern Mariana Islands)' })
      await form.getByRole('button', { name: 'Save time zone' }).click()
      await expect(page.getByText('Time zone saved.')).toBeVisible()
      await expect(section).toContainText('set for this project')
      await expect(section).toContainText('Chamorro Time (Guam, Northern Mariana Islands)')
      await openParcel(page, parcel.id, 'Parcel 001')
      await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toContainText('Chamorro Standard Time')
      await page.getByRole('tab', { name: 'Milestones & deadlines' }).click()
      await expect(page.getByText('Days are counted in Chamorro Standard Time.')).toBeVisible()
      expect((await getParcel(request, admin, parcel.id)).timeZoneId).toBe('Pacific/Guam')

      // Back to following the agency.
      await page.goto(`/projects/${project.id}`)
      await form.getByLabel('Project time zone').selectOption({ label: 'Follow the agency (Central Time (Chicago))' })
      await form.getByRole('button', { name: 'Save time zone' }).click()
      await expect(section).toContainText('follows the agency')
      expect((await getParcel(request, admin, parcel.id)).timeZoneId).toBe('America/Chicago')
      const saved = await (await request.get(`/api/v1/projects/${project.id}`, { headers: admin })).json()
      expect(saved.timeZoneId).toBeNull()
    } finally {
      await request.put('/api/v1/agency/time-zone', { headers: admin, data: { timeZoneId: original } })
    }
  })
})

// ───────────────────────────────── Everyone ─────────────────────────────────
test.describe('Everyone · parcels', () => {
  test('AT-X-33 the demo agency opens with its sample parcels, one overdue, one split with a remnant', async ({ page, request }) => {
    await signInViaUi(page, 'ProjectManager')
    const token = await sessionToken(page)
    const found = await (await request.get('/api/v1/projects?search=DEMO-002', { headers: { Authorization: `Bearer ${token}` } })).json()
    const demo = found.items.find((p: any) => p.number === 'DEMO-002')

    await page.goto(`/parcels?projectId=${demo.id}`)
    const table = page.getByRole('table', { name: 'Parcels' })
    for (const number of ['001', '002', '003', '004-A', '004-R']) await expect(table.getByRole('link', { name: number, exact: true })).toBeVisible()
    await expect(table.getByRole('link', { name: '004', exact: true })).toHaveCount(0) // the split-away original is kept on record, not listed
    await expect(table.getByRole('row', { name: /112 Harbor Rd/ })).toContainText('Initial Offer')
    await expect(table.getByRole('row', { name: /9 Pier Ln/ })).toContainText('In Possession - CLOSED')

    // The overdue one is among the Next actions.
    await sidebar(page).getByRole('link', { name: 'Home' }).click()
    await expect(page.getByRole('region', { name: 'Next actions' }).getByRole('listitem').filter({ hasText: 'DEMO-002 · Parcel 002' }).first()).toContainText('Overdue by')
  })

  test('AT-X-33 a parcel that does not exist is simply not found, and anonymous visitors are sent to sign in', async ({ page, request }) => {
    await signInViaUi(page, 'ProjectManager')
    await page.goto('/parcels/00000000-0000-0000-0000-000000000abc')
    await expect(page.getByText('This parcel could not be opened')).toBeVisible()
    await signOutViaUi(page)

    await page.goto('/parcels')
    await expect(page).toHaveURL(/\/login/)
    for (const path of ['/api/v1/parcels', '/api/v1/reminders', '/api/v1/parcel-rules/statuses'])
      expect((await request.get(path)).status(), path).toBe(401)
  })
})

// ───────────────────────────────── Every role, both directions ─────────────────────────────────
for (const role of ROLES) {
  test.describe(`${role} · parcels access`, () => {
    test(`AT-X-34 ${role} gets exactly the parcel screens and controls the access spec gives their role`, async ({ page, request }) => {
      const admin = await adminHeaders(request)
      const project = await makeProject(request, admin)
      const parcel = await makeParcel(request, admin, project, { address: `Spec ${role} Rd` })
      expect((await request.post(`/api/v1/parcels/${parcel.id}/interests`, { headers: admin, data: { type: 'Remainder', areaSqFt: 10, notes: null } })).status()).toBe(201)

      await signInViaUi(page, role)

      // List.
      await sidebar(page).getByRole('link', { name: 'Parcels' }).click()
      await expect(page.getByRole('heading', { name: 'Parcels', exact: true })).toBeVisible()
      await page.goto(`/parcels?projectId=${project.id}`)
      await expect(page.getByRole('table', { name: 'Parcels' })).toBeVisible()
      await expect(page.getByRole('button', { name: 'New parcel' }), `${role} New parcel`).toHaveCount(canCreate(role) ? 1 : 0)
      await expect(page.getByRole('checkbox', { name: /Select parcel/ }), `${role} select`).toHaveCount(canCreate(role) ? 1 : 0)

      // Detail.
      await openParcel(page, parcel.id, 'Parcel 001')
      await expect(page.getByRole('button', { name: 'Edit parcel' }), `${role} Edit parcel`).toHaveCount(canCreate(role) ? 1 : 0)
      await expect(page.getByRole('button', { name: 'Split', exact: true }), `${role} Split`).toHaveCount(canCreate(role) ? 1 : 0)
      await page.getByRole('tab', { name: 'Interests' }).click()
      await expect(page.getByRole('row', { name: /Remainder/ })).toBeVisible()
      await expect(page.getByRole('form', { name: 'Add an interest' }), `${role} add interest`).toHaveCount(canCreate(role) ? 1 : 0)
      await expect(page.getByRole('button', { name: 'Remove Remainder interest' }), `${role} remove interest`).toHaveCount(canDelete(role) ? 1 : 0)
      await page.getByRole('tab', { name: 'Workflow' }).click()
      await expect(stepList(page)).toBeVisible()
      await expect(page.getByRole('button', { name: 'Advance to Title & survey' }), `${role} advance`).toHaveCount(canCreate(role) ? 1 : 0)
      await expect(page.getByRole('form', { name: 'Move to another step' }), `${role} override`).toHaveCount(canCreate(role) && mayOverride(role) ? 1 : 0)
      await page.getByRole('tab', { name: 'Site visits & notice' }).click()
      await expect(page.getByRole('form', { name: 'Notice to owner' }), `${role} notice`).toHaveCount(canCreate(role) ? 1 : 0)
      await expect(page.getByRole('form', { name: 'Record a site visit' }), `${role} site visit`).toHaveCount(canCreate(role) ? 1 : 0)

      // The project's Parcels tab: everyone sees it; only some change it.
      await page.goto(`/projects/${project.id}`)
      await page.getByRole('tab', { name: 'Parcels' }).click()
      await expect(page.getByRole('region', { name: 'Parcel roll-up' })).toBeVisible()
      await expect(page.getByRole('form', { name: 'Add a group' }), `${role} add group`).toHaveCount(canCreate(role) ? 1 : 0)
      await page.getByRole('tab', { name: 'Overview' }).click()
      await expect(page.getByRole('region', { name: 'Project time zone' })).toBeVisible()
      await expect(page.getByRole('form', { name: 'Set the project time zone' }), `${role} project time zone`).toHaveCount(canCreate(role) ? 1 : 0)

      // Configuration: the parcel rules are shown to, and editable by, the people who may open Configuration at all.
      await page.goto('/administration')
      if (role === 'Admin') {
        await expect(page.getByRole('region', { name: 'Parcel statuses' })).toBeVisible()
        await expect(page.getByRole('button', { name: 'Add status' })).toBeVisible()
      } else {
        await expect(page.getByText(/don't have access/i)).toBeVisible()
        await expect(page.getByRole('region', { name: 'Parcel statuses' })).toHaveCount(0)
      }

      // Server: the same answers, whatever the screen shows.
      const token = await sessionToken(page)
      const headers = { Authorization: `Bearer ${token}` }
      const code = (r: { status(): number }) => r.status()
      expect(code(await request.get(`/api/v1/parcels/${parcel.id}`, { headers }))).toBe(200)
      expect(code(await request.get('/api/v1/reminders', { headers }))).toBe(200)
      expect(code(await request.get('/api/v1/parcel-rules/workflows', { headers }))).toBe(200)
      expect(code(await request.post('/api/v1/parcels', { headers, data: parcelBody(project.id, { address: `By ${role}` }) }))).toBe(canCreate(role) ? 201 : 403)
      expect(code(await request.put(`/api/v1/parcels/${parcel.id}`, { headers, data: parcelBody(undefined, { number: '001', address: `Edited by ${role}`, areaSqFt: 1000 }) }))).toBe(canCreate(role) ? 200 : 403)
      expect(code(await request.post(`/api/v1/parcels/${parcel.id}/milestones`, { headers, data: { name: `By ${role}` } }))).toBe(canCreate(role) ? 201 : 403)
      expect(code(await request.post(`/api/v1/parcels/${parcel.id}/site-visits`, { headers, data: { visitedOn: '2026-10-01', notes: null, ownerContactConfirmed: false } }))).toBe(canCreate(role) ? 201 : 403)
      expect(code(await request.put(`/api/v1/parcels/${parcel.id}/notice`, { headers, data: { issuedOn: '2026-10-01', method: 'Email' } }))).toBe(canCreate(role) ? 200 : 403)
      const statusList = await (await request.get('/api/v1/parcel-rules/statuses', { headers })).json()
      expect(code(await request.post('/api/v1/parcels/batch-update', { headers, data: { parcelIds: [parcel.id], statusId: statusList[0].id, groupId: null, clearGroup: false } }))).toBe(canCreate(role) ? 200 : 403)
      expect(code(await request.post(`/api/v1/parcels/${parcel.id}/advance`, { headers, data: { targetStepKey: 'title_survey', overrideReason: null } }))).toBe(canCreate(role) ? 200 : 403)
      // Skipping ahead (here, past the in-order step) needs the override role as well as update access.
      expect(code(await request.post(`/api/v1/parcels/${parcel.id}/advance`, { headers, data: { targetStepKey: 'closing', overrideReason: 'Spec check' } })))
        .toBe(canCreate(role) && mayOverride(role) ? 200 : 403)

      const groupRes = await request.post(`/api/v1/projects/${project.id}/groups`, { headers, data: { name: `Group by ${role}` } })
      expect(code(groupRes)).toBe(canCreate(role) ? 201 : 403)
      const groupId = canCreate(role) ? (await groupRes.json()).id : (await (await request.post(`/api/v1/projects/${project.id}/groups`, { headers: admin, data: { name: `Admin group for ${role}` } })).json()).id
      expect(code(await request.delete(`/api/v1/projects/${project.id}/groups/${groupId}`, { headers }))).toBe(canDelete(role) ? 204 : 403)

      const interests = (await getParcel(request, admin, parcel.id)).interests
      expect(code(await request.delete(`/api/v1/parcels/${parcel.id}/interests/${interests[0].id}`, { headers }))).toBe(canDelete(role) ? 200 : 403)

      // Agency rules: anyone with Parcels can read them; only the Administrator can change them.
      expect(code(await request.get('/api/v1/agency/time-zone', { headers }))).toBe(200)
      expect(code(await request.put(`/api/v1/projects/${project.id}/time-zone`, { headers, data: { timeZoneId: null } }))).toBe(canCreate(role) ? 200 : 403)
      if (role !== 'Admin') expect(code(await request.put('/api/v1/agency/time-zone', { headers, data: { timeZoneId: 'America/Chicago' } }))).toBe(403)
      const first = statusList[0]
      expect(code(await request.put(`/api/v1/parcel-rules/statuses/${first.id}`, { headers, data: { name: first.name, color: first.color, sortOrder: first.sortOrder, isActive: first.isActive, isInitial: first.isInitial } })))
        .toBe(role === 'Admin' ? 200 : 403)
      if (role !== 'Admin') {
        expect(code(await request.put('/api/v1/parcel-rules/step-gate', { headers, data: { extraOverrideRoles: [] } }))).toBe(403)
        expect(code(await request.post('/api/v1/parcel-rules/deadline-rules', { headers, data: { name: 'x', triggerMilestone: 'a', targetMilestone: 'b', days: 1, isActive: true } }))).toBe(403)
      }
    })
  })
}
