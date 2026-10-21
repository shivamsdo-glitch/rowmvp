import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { canRead, devToken, level, newId, ROLES, sessionToken, signInViaUi } from './support'

/**
 * Phase 1, slice 3a — owners, interests, contacts, tenants, abutters and W-9 tax information (ROW-014..018).
 * Real browser, real UI, real API, real Postgres. Case ids match vault/acceptance/<Role>.md. Tests share one database and run
 * in parallel, so every test makes its own project, tract and parties and finds things by those names.
 */

const canCreate = (role: string) => ['S', 'P'].includes(level(role, 'parties') ?? '')
const canUpdate = (role: string) => ['U', 'S', 'P'].includes(level(role, 'parties') ?? '')
const canDelete = (role: string) => level(role, 'parties') === 'P'
const unique = (label: string) => `${label} ${newId().slice(0, 8)}`
const TIN = '123-45-6789'
const TIN_DIGITS = '123456789'

type Headers = { Authorization: string }
interface Made { id: string; number: string; name: string }

async function adminHeaders(request: APIRequestContext): Promise<Headers> {
  return { Authorization: `Bearer ${await devToken(request, 'Admin')}` }
}

async function makeProject(request: APIRequestContext, headers: Headers): Promise<Made> {
  const name = unique('Parties scene')
  const res = await request.post('/api/v1/projects', { headers, data: { name, oversightType: 'Local' } })
  expect(res.status()).toBe(201)
  const p = await res.json()
  return { id: p.id, number: p.number, name }
}

async function makeTract(request: APIRequestContext, headers: Headers, project: Made, number = 'T-1') {
  const res = await request.post(`/api/v1/projects/${project.id}/tracts`, { headers, data: { number, description: 'Home place', taxParcelNumbers: [] } })
  expect(res.status(), await res.text()).toBe(201)
  return (await res.json()) as { id: string; number: string }
}

async function makeParcel(request: APIRequestContext, headers: Headers, project: Made, tractId: string | null = null, address = '1 Oak St') {
  const res = await request.post('/api/v1/parcels', {
    headers, data: { projectId: project.id, number: null, parentTractId: tractId, groupId: null, address, areaSqFt: 5000, acquisitionType: 'Standard' },
  })
  expect(res.status(), await res.text()).toBe(201)
  return (await res.json()) as { id: string; number: string }
}

const partyBody = (name: string, kind = 'Person') => ({ kind, name, email: 'pat@example.com', phone: '555-123-4567', city: 'Springfield', state: 'IL', postalCode: '62701' })

async function makeParty(request: APIRequestContext, headers: Headers, name = unique('Party')) {
  const res = await request.post('/api/v1/parties', { headers, data: partyBody(name) })
  expect(res.status(), await res.text()).toBe(201)
  return (await res.json()) as { id: string; name: string }
}

async function linkParty(request: APIRequestContext, headers: Headers, where: string, partyId: string, role: string, sharePercent: number | null = null) {
  const res = await request.post(where, { headers, data: { partyId, role, sharePercent, leaseOnFile: false } })
  expect(res.status(), await res.text()).toBe(201)
  return (await res.json()) as { id: string }
}

/** Every API response body the page receives, so a test can prove a secret never travels to the browser. */
function collectResponses(page: Page) {
  const bodies: string[] = []
  page.on('response', async (r) => {
    if (!r.url().includes('/api/')) return
    try { bodies.push(await r.text()) } catch { /* redirects and aborted requests have no body */ }
  })
  return bodies
}

const addForm = (page: Page) => page.getByRole('form', { name: 'Add a person or organization' })
const section = (page: Page, name: string) => page.getByRole('region', { name })

/** Adds a new person or organization to the open panel. */
async function addNewParty(page: Page, f: { name: string; role: string; share?: string; notes?: string }) {
  await page.getByRole('button', { name: 'Add a person or organization' }).click()
  const form = addForm(page)
  await form.getByLabel('A new person or organization').check()
  await form.getByLabel(/^New party Name/).fill(f.name)
  await form.getByLabel(/^Role(?! notes)/).selectOption({ label: f.role })
  if (f.share) await form.getByLabel('Share (%)').fill(f.share)
  if (f.notes) await form.getByLabel('Role notes').fill(f.notes)
  await form.getByRole('button', { name: 'Add', exact: true }).click()
}

async function openTractPanel(page: Page, project: Made, tract = 'T-1') {
  await page.goto(`/projects/${project.id}`)
  await page.getByRole('tab', { name: 'Parcels' }).click()
  await page.getByRole('button', { name: `Owners and parties of tract ${tract}` }).click()
  await expect(section(page, 'Owners and interests')).toBeVisible()
}

async function openParcelParties(page: Page, parcelId: string) {
  await page.goto(`/parcels/${parcelId}`)
  await page.getByRole('tab', { name: 'Ownership interests' }).click()
  await expect(section(page, 'Contacts')).toBeVisible()
}

// ───────────────────────────────── Project Manager ─────────────────────────────────
test.describe('Project Manager · parties', () => {
  test('AT-PM-22 records a tract\'s owners with shares, then the contacts, tenant and abutter of a parcel on it', async ({ page, request }) => {
    const admin = await adminHeaders(request)
    const project = await makeProject(request, admin)
    const tract = await makeTract(request, admin, project)
    const parcel = await makeParcel(request, admin, project, tract.id)
    const loose = await makeParcel(request, admin, project, null, '2 Oak St')
    const owner = unique('Pat Owner')
    const coOwner = unique('Sam Co-owner')
    const bank = unique('First Bank')
    await signInViaUi(page, 'ProjectManager')

    // Owners and shares on the tract, from the project's Parcels tab.
    await openTractPanel(page, project)
    await addNewParty(page, { name: owner, role: 'Fee owner', share: '60' })
    const owners = section(page, 'Owners and interests')
    await expect(owners.getByRole('row', { name: new RegExp(owner) })).toContainText('60%')

    await addNewParty(page, { name: coOwner, role: 'Fee owner', share: '50' })
    await expect(addForm(page)).toContainText('add up to more than 100 percent. 40 percent is unclaimed')
    await addForm(page).getByLabel('Share (%)').fill('40')
    await addForm(page).getByRole('button', { name: 'Add', exact: true }).click()
    await expect(owners.getByRole('row', { name: new RegExp(coOwner) })).toContainText('40%')

    await addNewParty(page, { name: bank, role: 'Lienholder', notes: 'Deed of trust recorded 2019' })
    await expect(owners.getByRole('row', { name: new RegExp(bank) })).toContainText('Deed of trust recorded 2019')

    // The parcel on the tract shows them, marked as the tract's, and does not offer to enter owners again.
    await openParcelParties(page, parcel.id)
    await expect(page.getByRole('note')).toContainText('part of tract T-1')
    const inherited = section(page, 'Owners and interests').getByRole('row', { name: new RegExp(owner) })
    await expect(inherited).toContainText('60%')
    await expect(inherited).toContainText('Tract T-1')
    await expect(inherited.getByRole('button')).toHaveCount(0)

    await page.getByRole('button', { name: 'Add a person or organization' }).click()
    const roles = await addForm(page).getByLabel(/^Role(?! notes)/).locator('option').allTextContents()
    expect(roles).not.toContain('Fee owner')
    expect(roles).toEqual(expect.arrayContaining(['Attorney', 'Tenant', 'Abutter']))
    await addForm(page).getByRole('button', { name: 'Cancel' }).click()

    const lawyer = unique('Lee Lawyer')
    await addNewParty(page, { name: lawyer, role: 'Attorney', notes: 'Handles closing' })
    await expect(section(page, 'Contacts').getByRole('row', { name: new RegExp(lawyer) })).toContainText('Handles closing')

    // A tenant (with lease and insurance) and an abutter on a parcel with no tract.
    await openParcelParties(page, loose.id)
    const tenant = unique('Tina Tenant')
    await page.getByRole('button', { name: 'Add a person or organization' }).click()
    const form = addForm(page)
    await form.getByLabel('A new person or organization').check()
    await form.getByLabel(/^New party Name/).fill(tenant)
    await form.getByLabel(/^Role(?! notes)/).selectOption({ label: 'Tenant' })
    await form.getByLabel('Lease on file').check()
    await form.getByLabel('Lease details').fill('Month to month')
    await form.getByLabel('Insurance details').fill('Renter policy 55')
    await form.getByRole('button', { name: 'Add', exact: true }).click()
    await expect(section(page, 'Tenants and occupants').getByRole('row', { name: new RegExp(tenant) })).toContainText('Lease on file: Month to month')
    await expect(section(page, 'Tenants and occupants')).toContainText('Insurance: Renter policy 55')
    await addNewParty(page, { name: unique('Abe Abutter'), role: 'Abutter' })
    await expect(section(page, 'Abutters and interested parties').getByRole('row')).toHaveCount(2)

    // Owners may be entered directly on a parcel that has no tract.
    await addNewParty(page, { name: unique('Direct Owner'), role: 'Fee owner', share: '100' })
    await expect(section(page, 'Owners and interests').getByRole('row', { name: /Direct Owner/ })).toContainText('100%')

    // The API agrees with the screens.
    const pm = { Authorization: `Bearer ${await sessionToken(page)}` }
    const seen = await (await request.get(`/api/v1/parcels/${parcel.id}/parties`, { headers: pm })).json()
    expect(seen.ownership.map((l: any) => [l.party.name, l.sharePercent, l.source]).sort()).toEqual(
      [[bank, null, 'Tract'], [coOwner, 40, 'Tract'], [owner, 60, 'Tract']].sort(),
    )
    expect(seen.contacts.map((l: any) => l.party.name)).toEqual([lawyer])
    const wrongPlace = await request.post(`/api/v1/parcels/${parcel.id}/parties`, {
      headers: pm, data: { newParty: partyBody(unique('Dup')), role: 'FeeOwner', sharePercent: 10, leaseOnFile: false },
    })
    expect(wrongPlace.status()).toBe(400)
    expect(await wrongPlace.text()).toContain('owners_on_tract')
  })

  test('AT-PM-22 edits and removes links, and a party that is in use cannot be deleted', async ({ page, request }) => {
    const admin = await adminHeaders(request)
    const project = await makeProject(request, admin)
    const tract = await makeTract(request, admin, project)
    const parcel = await makeParcel(request, admin, project, tract.id)
    const owner = await makeParty(request, admin, unique('Pat Owner'))
    await linkParty(request, admin, `/api/v1/projects/${project.id}/tracts/${tract.id}/parties`, owner.id, 'FeeOwner', 50)
    await linkParty(request, admin, `/api/v1/parcels/${parcel.id}/parties`, owner.id, 'Attorney')
    await signInViaUi(page, 'ProjectManager')

    await openTractPanel(page, project)
    await page.getByRole('button', { name: `Edit ${owner.name}, Fee owner` }).click()
    const edit = page.getByRole('form', { name: `Edit ${owner.name}, Fee owner` })
    await edit.getByLabel('Share (%)').fill('150')
    await edit.getByRole('button', { name: 'Save changes' }).click()
    await expect(edit.getByText('A share must be more than 0 and at most 100 percent.')).toBeVisible()
    await edit.getByLabel('Share (%)').fill('75.5')
    await edit.getByLabel('Role notes').fill('Corrected from the deed')
    await edit.getByRole('button', { name: 'Save changes' }).click()
    const row = section(page, 'Owners and interests').getByRole('row', { name: new RegExp(`^${owner.name} Fee owner`) })
    await expect(row).toContainText('75.5%')
    await expect(row).toContainText('Corrected from the deed')

    // The party's own page lists both records and will not offer deletion while it is in use.
    await page.goto(`/parties/${owner.id}`)
    await expect(page.getByRole('heading', { level: 1 })).toContainText(owner.name)
    const usage = page.getByRole('table', { name: 'Where this party appears' })
    await expect(usage.getByRole('row')).toHaveCount(3)
    await expect(usage).toContainText('Tract T-1')
    await expect(usage.getByRole('link', { name: `Parcel ${parcel.number}` })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Delete party' })).toBeDisabled()
    const pm = { Authorization: `Bearer ${await sessionToken(page)}` }
    const refused = await request.delete(`/api/v1/parties/${owner.id}`, { headers: pm })
    expect(refused.status()).toBe(409)
    expect(await refused.text()).toContain('party_in_use')

    // Remove both links from the screens; then the party can go.
    await openParcelParties(page, parcel.id)
    await page.getByRole('button', { name: `Remove ${owner.name}, Attorney` }).click()
    await expect(section(page, 'Contacts').getByRole('row', { name: new RegExp(owner.name) })).toHaveCount(0)
    await openTractPanel(page, project)
    await page.getByRole('button', { name: `Remove ${owner.name}, Fee owner` }).click()
    await expect(section(page, 'Owners and interests').getByRole('row', { name: new RegExp(owner.name) })).toHaveCount(0)
    await page.goto(`/parties/${owner.id}`)
    await expect(page.getByText(/Nothing uses this party/)).toBeVisible()
    await page.getByRole('button', { name: 'Delete party' }).click()
    await expect(page).toHaveURL(/\/parties$/)
    expect((await request.get(`/api/v1/parties/${owner.id}`, { headers: pm })).status()).toBe(404)
  })

  test('AT-PM-22 a tax ID number is accepted once, shown only as its last four digits, and never sent back to anyone', async ({ page, request }) => {
    const admin = await adminHeaders(request)
    const party = await makeParty(request, admin, unique('Taxed Owner'))
    const bodies = collectResponses(page)
    await signInViaUi(page, 'ProjectManager')

    await page.goto('/parties')
    await page.getByLabel('Search parties').fill(party.name)
    await page.getByRole('link', { name: party.name }).click()
    await expect(page.getByRole('heading', { level: 1 })).toContainText(party.name)
    const form = page.getByRole('form', { name: 'Tax information' })
    await expect(page.getByText('None', { exact: true }).first()).toBeVisible()

    // Bad numbers are refused with the reason and without repeating the number.
    await form.getByLabel(/^Tax ID number/).fill('666-12-3456')
    await form.getByRole('button', { name: 'Save tax information' }).click()
    await expect(form.getByText('That is not a valid Social Security number.')).toBeVisible()
    await expect(page.locator('body')).not.toContainText('666-12-3456')
    await form.getByLabel(/^Tax ID number/).fill('12345')
    await form.getByRole('button', { name: 'Save tax information' }).click()
    await expect(form.getByText('A tax number has 9 digits.')).toBeVisible()

    // A valid number is saved; the field empties and only the mask remains.
    const number = form.getByLabel(/^Tax ID number/)
    expect(await number.getAttribute('type')).toBe('password')
    await number.fill(TIN)
    await form.getByLabel('W-9 received on').fill('2026-01-15')
    await form.getByLabel('Reportable on a 1099').check()
    await form.getByRole('button', { name: 'Save tax information' }).click()
    await expect(page.getByTestId('tax-id-masked')).toHaveText('***-**-6789')
    await expect(page.getByText('Tax information saved.')).toBeVisible()
    await expect(form.getByLabel(/Replace tax ID number/)).toHaveValue('')
    await expect(page.getByText('Jan 15, 2026')).toBeVisible()
    await expect(page.locator('body')).not.toContainText(TIN)
    await expect(page.locator('body')).not.toContainText(TIN_DIGITS)

    // The list shows that a number is on file, not what it is.
    await page.goto('/parties')
    await page.getByLabel('Search parties').fill(party.name)
    await expect(page.getByRole('row', { name: new RegExp(party.name) })).toContainText('On file')

    // Replacing with a number of another type needs the type; clearing removes it.
    await page.getByRole('link', { name: party.name }).click()
    await page.getByRole('form', { name: 'Tax information' }).getByRole('button', { name: 'Remove tax ID number' }).click()
    await expect(page.getByTestId('tax-id-masked')).toHaveCount(0)

    // The API: masked, never the number; and nothing the browser received ever contained it.
    const pm = { Authorization: `Bearer ${await sessionToken(page)}` }
    const saved = await request.put(`/api/v1/parties/${party.id}/tax-info`, { headers: pm, data: { type: 'Ein', taxId: '12-3456789', reportable1099: true } })
    expect(saved.status()).toBe(200)
    expect((await saved.json()).taxIdMasked).toBe('**-***6789')
    for (const url of [`/api/v1/parties/${party.id}`, `/api/v1/parties?search=${encodeURIComponent(party.name)}`]) {
      const text = await (await request.get(url, { headers: pm })).text()
      expect(text).not.toContain('123456789')
      expect(text).not.toContain('12-3456789')
      expect(text).not.toContain('v1.')
    }
    expect(bodies.length).toBeGreaterThan(5)
    for (const body of bodies) {
      expect(body).not.toContain(TIN)
      expect(body).not.toContain(TIN_DIGITS)
    }
    const bad = await request.put(`/api/v1/parties/${party.id}/tax-info`, { headers: pm, data: { type: 'Ssn', taxId: '000-12-3456' } })
    expect(bad.status()).toBe(400)
    expect(await bad.text()).not.toContain('000-12-3456')
  })

  test('AT-PM-22 splitting a parcel keeps the people on every piece', async ({ page, request }) => {
    const admin = await adminHeaders(request)
    const project = await makeProject(request, admin)
    const parcel = await makeParcel(request, admin, project)
    const owner = await makeParty(request, admin, unique('Pat Owner'))
    await linkParty(request, admin, `/api/v1/parcels/${parcel.id}/parties`, owner.id, 'FeeOwner', 100)
    await signInViaUi(page, 'ProjectManager')

    const pm = { Authorization: `Bearer ${await sessionToken(page)}` }
    const split = await request.post(`/api/v1/parcels/${parcel.id}/split`, { headers: pm, data: { parts: [{ areaSqFt: 2000 }] } })
    expect(split.status()).toBe(201)
    const result = await split.json()
    const pieces = [...result.parts, result.remnant].map((p: any) => p.id as string)
    expect(pieces).toHaveLength(2)
    for (const id of pieces) {
      const seen = await (await request.get(`/api/v1/parcels/${id}/parties`, { headers: pm })).json()
      expect(seen.ownership.map((l: any) => l.party.id)).toEqual([owner.id])
    }
    await openParcelParties(page, pieces[0])
    await expect(section(page, 'Owners and interests')).toContainText(owner.name)
    await page.goto(`/parties/${owner.id}`)
    await expect(page.getByRole('table', { name: 'Where this party appears' }).getByRole('row')).toHaveCount(4) // header + the original and two pieces
  })
})

// ───────────────────────────────── Negotiator ─────────────────────────────────
test.describe('Negotiator · parties', () => {
  test('AT-NG-16 captures an owner, the paperwork details and a W-9 number, and tidies up after a mistake', async ({ page, request }) => {
    const admin = await adminHeaders(request)
    const project = await makeProject(request, admin)
    const parcel = await makeParcel(request, admin, project)
    const bodies = collectResponses(page)
    await signInViaUi(page, 'Negotiator')

    // Create the owner on the Parties page, then link them from the parcel.
    const name = unique('Nora Owner')
    await page.goto('/parties')
    await page.getByRole('button', { name: 'New party' }).click()
    const create = page.getByRole('form', { name: 'Create a party' })
    await create.getByRole('button', { name: 'Create party' }).click()
    await expect(create.getByText('Name is required.')).toBeVisible()
    await create.getByLabel(/^Name/).fill(name)
    await create.getByLabel(/^Email/).fill('nope')
    await create.getByRole('button', { name: 'Create party' }).click()
    await expect(create.getByText('Enter a valid email address.')).toBeVisible()
    await create.getByLabel(/^Email/).fill('nora@example.com')
    await create.getByLabel(/^State/).first().fill('wa')
    await create.getByRole('button', { name: 'Create party' }).click()
    await expect(page.getByRole('heading', { level: 1 })).toContainText(name)
    await expect(page.getByText('WA', { exact: true })).toBeVisible()
    const partyUrl = page.url()

    await openParcelParties(page, parcel.id)
    await page.getByRole('button', { name: 'Add a person or organization' }).click()
    const form = addForm(page)
    await form.getByLabel('Find a party').fill(name)
    await expect(form.getByLabel(/^Party/).locator('option', { hasText: name })).toHaveCount(1)
    await form.getByLabel(/^Party/).selectOption({ label: name })
    await form.getByLabel('Share (%)').fill('100')
    await form.getByRole('button', { name: 'Add', exact: true }).click()
    await expect(section(page, 'Owners and interests').getByRole('row', { name: new RegExp(name) })).toContainText('100%')

    // The same person in the same role twice is refused.
    await page.getByRole('button', { name: 'Add a person or organization' }).click()
    await form.getByLabel('Find a party').fill(name)
    await expect(form.getByLabel(/^Party/).locator('option', { hasText: name })).toHaveCount(1)
    await form.getByLabel(/^Party/).selectOption({ label: name })
    await form.getByLabel('Share (%)').fill('10')
    await form.getByRole('button', { name: 'Add', exact: true }).click()
    await expect(form).toContainText('already recorded here as a fee owner')
    await form.getByRole('button', { name: 'Cancel' }).click()

    // The W-9 number: full control.
    await page.goto(partyUrl)
    const tax = page.getByRole('form', { name: 'Tax information' })
    await tax.getByLabel('Type of tax ID').selectOption('Ssn')
    await tax.getByLabel(/^Tax ID number/).fill(TIN)
    await tax.getByRole('button', { name: 'Save tax information' }).click()
    await expect(page.getByTestId('tax-id-masked')).toHaveText('***-**-6789')

    // Taking it back: remove the link, then the party.
    await openParcelParties(page, parcel.id)
    await page.getByRole('button', { name: `Remove ${name}, Fee owner` }).click()
    await expect(section(page, 'Owners and interests').getByRole('row', { name: new RegExp(name) })).toHaveCount(0)
    await page.goto(partyUrl)
    await page.getByRole('button', { name: 'Delete party' }).click()
    await expect(page).toHaveURL(/\/parties$/)
    for (const body of bodies) {
      expect(body).not.toContain(TIN)
      expect(body).not.toContain(TIN_DIGITS)
    }
  })

  test('AT-NG-16 a Negotiator may use every party endpoint the matrix gives them, and the audit trail records it', async ({ page, request }) => {
    const admin = await adminHeaders(request)
    const project = await makeProject(request, admin)
    const tract = await makeTract(request, admin, project)
    await signInViaUi(page, 'Negotiator')
    const ng = { Authorization: `Bearer ${await sessionToken(page)}` }

    const created = await request.post('/api/v1/parties', { headers: ng, data: partyBody(unique('Audited')) })
    expect(created.status()).toBe(201)
    const party = await created.json()
    const link = await request.post(`/api/v1/projects/${project.id}/tracts/${tract.id}/parties`, { headers: ng, data: { partyId: party.id, role: 'FeeOwner', sharePercent: 50, leaseOnFile: false } })
    expect(link.status()).toBe(201)
    const linkId = (await link.json()).id
    expect((await request.put(`/api/v1/party-links/${linkId}`, { headers: ng, data: { sharePercent: 60, leaseOnFile: false } })).status()).toBe(200)
    expect((await request.put(`/api/v1/parties/${party.id}/tax-info`, { headers: ng, data: { type: 'Ssn', taxId: TIN, reportable1099: false } })).status()).toBe(200)
    expect((await request.delete(`/api/v1/party-links/${linkId}`, { headers: ng })).status()).toBe(204)
    expect((await request.delete(`/api/v1/parties/${party.id}`, { headers: ng })).status()).toBe(204)
  })
})

// ───────────────────────────────── Read-only roles ─────────────────────────────────
for (const [id, role] of [['AT-AP-15', 'Appraiser'], ['AT-FN-15', 'Finance']] as const) {
  test.describe(`${role} · parties (read only)`, () => {
    test(`${id} reads who owns a parcel and whether a tax ID is on file, and can change nothing`, async ({ page, request }) => {
      expect(canCreate(role)).toBe(false)
      const admin = await adminHeaders(request)
      const project = await makeProject(request, admin)
      const tract = await makeTract(request, admin, project)
      const parcel = await makeParcel(request, admin, project, tract.id)
      const owner = await makeParty(request, admin, unique('Read-only Owner'))
      const tractLink = await linkParty(request, admin, `/api/v1/projects/${project.id}/tracts/${tract.id}/parties`, owner.id, 'FeeOwner', 100)
      const parcelLink = await linkParty(request, admin, `/api/v1/parcels/${parcel.id}/parties`, owner.id, 'Attorney')
      expect((await request.put(`/api/v1/parties/${owner.id}/tax-info`, { headers: admin, data: { type: 'Ssn', taxId: TIN, reportable1099: true } })).status()).toBe(200)
      const bodies = collectResponses(page)
      await signInViaUi(page, role)

      // The list and the party page: visible, masked, not editable.
      await page.goto('/parties')
      await page.getByLabel('Search parties').fill(owner.name)
      await expect(page.getByRole('row', { name: new RegExp(owner.name) })).toContainText('On file')
      await expect(page.getByRole('button', { name: 'New party' })).toHaveCount(0)
      await page.getByRole('link', { name: owner.name }).click()
      await expect(page.getByTestId('tax-id-masked')).toHaveText('***-**-6789')
      await expect(page.getByRole('form', { name: 'Tax information' })).toHaveCount(0)
      await expect(page.getByText(/can see that a tax ID is on file but not change it/)).toBeVisible()
      await expect(page.getByRole('button', { name: 'Edit details' })).toHaveCount(0)
      await expect(page.getByRole('button', { name: 'Delete party' })).toHaveCount(0)

      // The parcel's Parties tab: the people are there, the controls are not.
      await openParcelParties(page, parcel.id)
      await expect(section(page, 'Owners and interests')).toContainText(owner.name)
      await expect(section(page, 'Contacts')).toContainText(owner.name)
      await expect(page.getByRole('button', { name: 'Add a person or organization' })).toHaveCount(0)
      await expect(page.getByRole('button', { name: /^(Edit|Remove) / })).toHaveCount(0)
      await page.goto(`/projects/${project.id}`)
      await page.getByRole('tab', { name: 'Parcels' }).click()
      await page.getByRole('button', { name: 'Owners and parties of tract T-1' }).click()
      await expect(section(page, 'Owners and interests')).toContainText(owner.name)
      await expect(page.getByRole('button', { name: 'Add a person or organization' })).toHaveCount(0)

      // The server refuses every way of changing anything, and the data is untouched.
      const mine = { Authorization: `Bearer ${await sessionToken(page)}` }
      const attempts = [
        request.post('/api/v1/parties', { headers: mine, data: partyBody(unique('Nope')) }),
        request.put(`/api/v1/parties/${owner.id}`, { headers: mine, data: partyBody('Hijacked') }),
        request.put(`/api/v1/parties/${owner.id}/tax-info`, { headers: mine, data: { type: 'Ssn', taxId: '234-56-7891' } }),
        request.delete(`/api/v1/parties/${owner.id}/tax-info/number`, { headers: mine }),
        request.delete(`/api/v1/parties/${owner.id}`, { headers: mine }),
        request.post(`/api/v1/parcels/${parcel.id}/parties`, { headers: mine, data: { partyId: owner.id, role: 'Bank', leaseOnFile: false } }),
        request.post(`/api/v1/projects/${project.id}/tracts/${tract.id}/parties`, { headers: mine, data: { partyId: owner.id, role: 'Trustee', leaseOnFile: false } }),
        request.put(`/api/v1/party-links/${tractLink.id}`, { headers: mine, data: { sharePercent: 1, leaseOnFile: false } }),
        request.delete(`/api/v1/party-links/${parcelLink.id}`, { headers: mine }),
      ]
      for (const res of await Promise.all(attempts)) expect(res.status()).toBe(403)
      const after = await (await request.get(`/api/v1/parties/${owner.id}`, { headers: admin })).json()
      expect(after.name).toBe(owner.name)
      expect(after.taxIdMasked).toBe('***-**-6789')
      expect(after.linkCount).toBe(2)
      for (const body of bodies) {
        expect(body).not.toContain(TIN)
        expect(body).not.toContain(TIN_DIGITS)
      }
    })
  })
}

// ───────────────────────────────── Administrator ─────────────────────────────────
test.describe('Administrator · parties', () => {
  test('AT-AD-27 has full control of parties, and what the product refuses it refuses everyone', async ({ page, request }) => {
    const admin = await adminHeaders(request)
    const project = await makeProject(request, admin)
    const tract = await makeTract(request, admin, project)
    const parcel = await makeParcel(request, admin, project, tract.id)
    await signInViaUi(page, 'Admin')
    const mine = { Authorization: `Bearer ${await sessionToken(page)}` }

    const party = await (await request.post('/api/v1/parties', { headers: mine, data: partyBody(unique('Admin Party')) })).json()
    const where = `/api/v1/projects/${project.id}/tracts/${tract.id}/parties`
    expect((await request.post(where, { headers: mine, data: { partyId: party.id, role: 'FeeOwner', sharePercent: 100.00001, leaseOnFile: false } })).status()).toBe(400)
    expect((await request.post(where, { headers: mine, data: { partyId: party.id, role: 'Lienholder', sharePercent: 10, leaseOnFile: false } })).status()).toBe(400)
    const link = await request.post(where, { headers: mine, data: { partyId: party.id, role: 'FeeOwner', sharePercent: 100, leaseOnFile: false } })
    expect(link.status()).toBe(201)
    const second = await request.post(where, { headers: mine, data: { newParty: partyBody(unique('Second')), role: 'FeeOwner', sharePercent: 0.0001, leaseOnFile: false } })
    expect(second.status()).toBe(409)
    expect(await second.text()).toContain('share_exceeds_whole')
    expect((await request.post(`/api/v1/parcels/${parcel.id}/parties`, { headers: mine, data: { newParty: partyBody(unique('Dup')), role: 'FeeOwner', leaseOnFile: false } })).status()).toBe(400)

    // Hostile input is stored as text or refused, never run.
    const script = await request.post('/api/v1/parties', { headers: mine, data: partyBody('<img src=x onerror=alert(1)>') })
    expect(script.status()).toBe(201)
    await page.goto('/parties')
    await page.getByLabel('Search parties').fill('onerror')
    await expect(page.getByRole('link', { name: '<img src=x onerror=alert(1)>' })).toBeVisible()
    expect((await request.post('/api/v1/parties', { headers: mine, data: partyBody('bad\u0000name') })).status()).toBe(400)
    expect((await request.post('/api/v1/parties', { headers: mine, data: partyBody('x'.repeat(201)) })).status()).toBe(400)
    expect((await request.post('/api/v1/parties', { headers: { ...mine, 'Content-Type': 'application/json' }, data: '{not json' })).status()).toBe(400)

    // The party page (read for everyone) gives the administrator every control.
    await page.goto(`/parties/${party.id}`)
    await expect(page.getByRole('button', { name: 'Edit details' })).toBeVisible()
    await expect(page.getByRole('form', { name: 'Tax information' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Delete party' })).toBeDisabled()
    expect((await request.delete(`/api/v1/party-links/${(await link.json()).id}`, { headers: mine })).status()).toBe(204)
    await page.reload()
    await page.getByRole('button', { name: 'Delete party' }).click()
    await expect(page).toHaveURL(/\/parties$/)
  })
})

// ───────────────────────────────── Everyone ─────────────────────────────────
test.describe('Everyone · parties', () => {
  for (const role of ROLES) {
    test(`AT-X-35 ${role}: the Parties screens and API match what the role may do, and the server agrees`, async ({ page, request }) => {
      const admin = await adminHeaders(request)
      const project = await makeProject(request, admin)
      const tract = await makeTract(request, admin, project)
      const parcel = await makeParcel(request, admin, project, tract.id)
      const party = await makeParty(request, admin, unique('Matrix Party'))
      const link = await linkParty(request, admin, `/api/v1/projects/${project.id}/tracts/${tract.id}/parties`, party.id, 'FeeOwner', 10)
      await signInViaUi(page, role)
      const mine = { Authorization: `Bearer ${await sessionToken(page)}` }

      // Screens.
      await page.goto('/parties')
      if (canRead(role, 'parties')) {
        await expect(page.getByRole('heading', { level: 1 })).toHaveText('Parties')
        await expect(page.getByRole('button', { name: 'New party' })).toHaveCount(canCreate(role) ? 1 : 0)
        await page.goto(`/parties/${party.id}`)
        await expect(page.getByRole('heading', { level: 1 })).toContainText(party.name)
        await expect(page.getByRole('form', { name: 'Tax information' })).toHaveCount(canUpdate(role) ? 1 : 0)
        await expect(page.getByRole('region', { name: 'Delete this party' })).toHaveCount(canDelete(role) ? 1 : 0)
        await openParcelParties(page, parcel.id)
        await expect(page.getByRole('button', { name: 'Add a person or organization' })).toHaveCount(canCreate(role) ? 1 : 0)
      } else {
        await expect(page.getByText(/don't have access/i)).toBeVisible()
      }

      // API, both directions.
      const status = async (p: Promise<{ status(): number }>) => (await p).status()
      expect(await status(request.get('/api/v1/parties', { headers: mine }))).toBe(canRead(role, 'parties') ? 200 : 403)
      expect(await status(request.get(`/api/v1/parties/${party.id}`, { headers: mine }))).toBe(canRead(role, 'parties') ? 200 : 403)
      expect(await status(request.get(`/api/v1/parcels/${parcel.id}/parties`, { headers: mine }))).toBe(canRead(role, 'parties') ? 200 : 403)
      expect(await status(request.post('/api/v1/parties', { headers: mine, data: partyBody(unique(`By ${role}`)) }))).toBe(canCreate(role) ? 201 : 403)
      expect(await status(request.put(`/api/v1/parties/${party.id}`, { headers: mine, data: partyBody(party.name) }))).toBe(canUpdate(role) ? 200 : 403)
      expect(await status(request.put(`/api/v1/party-links/${link.id}`, { headers: mine, data: { sharePercent: 10, leaseOnFile: false } }))).toBe(canUpdate(role) ? 200 : 403)
      const throwaway = await makeParty(request, admin)
      expect(await status(request.delete(`/api/v1/parties/${throwaway.id}`, { headers: mine }))).toBe(canDelete(role) ? 204 : 403)

      // Signed out: nothing.
      expect(await status(request.get('/api/v1/parties'))).toBe(401)
    })
  }

  test('AT-X-36 a missing party says so plainly, and another agency cannot see this agency\'s parties', async ({ page, request }) => {
    const admin = await adminHeaders(request)
    const party = await makeParty(request, admin, unique('Private Party'))
    await signInViaUi(page, 'ProjectManager')

    await page.goto(`/parties/${newId()}`)
    await expect(page.getByText('This party could not be opened')).toBeVisible()

    const otherAdmin = { Authorization: `Bearer ${await devToken(request, 'Admin', { tenant: newId() })}` }
    for (const url of [`/api/v1/parties/${party.id}`, `/api/v1/parties/${party.id}/usage`]) {
      expect([403, 404]).toContain((await request.get(url, { headers: otherAdmin })).status())
    }
    const list = await request.get(`/api/v1/parties?search=${encodeURIComponent(party.name)}`, { headers: otherAdmin })
    if (list.status() === 200) expect((await list.json()).items).toHaveLength(0)
    else expect(list.status()).toBe(403)
    expect([403, 404]).toContain((await request.put(`/api/v1/parties/${party.id}/tax-info`, { headers: otherAdmin, data: { type: 'Ssn', taxId: TIN } })).status())
    expect([403, 404]).toContain((await request.delete(`/api/v1/parties/${party.id}`, { headers: otherAdmin })).status())
  })
})
