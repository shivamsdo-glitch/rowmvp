import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { canUpdate, devToken, level, newId, ROLES, sessionToken, signInViaUi } from './support'

/**
 * Phase 1.5 slice 8 (ADR 0020): a parcel's Condemnation, Property Management & Disposition and Relocation tabs.
 * Real browser, UI, API and Postgres. Case ids match vault/acceptance/Negotiator.md, ProjectManager.md, RelocationAgent.md and Everyone.md.
 */

const canCreate = (role: string, page: string) => ['S', 'P'].includes(level(role, page) ?? '')
const canDelete = (role: string, page: string) => level(role, page) === 'P'
const unique = (label: string) => `${label} ${newId().slice(0, 8)}`
const exact = { exact: true } as const

async function adminHeaders(request: APIRequestContext) {
  return { Authorization: `Bearer ${await devToken(request, 'Admin')}` }
}

async function ensureLibraryValue(request: APIRequestContext, key: string, value: string) {
  const headers = await adminHeaders(request)
  const fields = (await (await request.get('/api/v1/library/fields', { headers })).json()) as { id: string; key: string }[]
  const res = await request.post(`/api/v1/library/fields/${fields.find((f) => f.key === key)!.id}/values`, { headers, data: { value } })
  expect([200, 201, 409]).toContain(res.status())
}

async function makeAgent(request: APIRequestContext) {
  const name = unique('Life Agent')
  const res = await request.post('/api/v1/library/agents', { headers: await adminHeaders(request), data: { name, agentType: 'Negotiator', company: 'Acme', phone: '555-0100', email: 'a@example.com' } })
  expect([200, 201]).toContain(res.status())
  return name
}

async function makeParcel(request: APIRequestContext) {
  const headers = await adminHeaders(request)
  const project = await (await request.post('/api/v1/projects', { headers, data: { name: unique('Lifecycle scene'), oversightType: 'Local', phase: 'Phase 1' } })).json()
  return (await (await request.post('/api/v1/parcels', { headers, data: { projectId: project.id, areaSqFt: 1000, acquisitionType: 'Standard', address: 'Lifecycle Rd' } })).json()) as { id: string; number: string }
}

async function openTab(page: Page, parcelId: string, tab: string) {
  await page.goto(`/parcels/${parcelId}`)
  await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toBeVisible()
  await page.getByRole('tab', { name: tab, ...exact }).click()
}

const attach = async (page: Page, title: string, file: string) => {
  const panel = page.getByRole('region', { name: title })
  await panel.getByLabel('Upload document').setInputFiles({ name: file, mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4 test') })
  await expect(panel.getByText(file)).toBeVisible()
}

test.describe('Negotiator · condemnation', () => {
  test('AT-NE-43 records a condemnation with its dates, people, attorneys, commissioners and notices, attaches a file and changes it; only Admin can remove it', async ({ page, request }) => {
    const agent = await makeAgent(request)
    const parcel = await makeParcel(request)
    await signInViaUi(page, 'Negotiator')
    await openTab(page, parcel.id, 'Condemnation')
    const form = page.getByRole('form', { name: 'Condemnation' })
    await expect(form).toContainText('Assigned when saved')

    await form.getByLabel('Preliminary condemnation package submitted to ATP', exact).fill('2026-03-10')
    await form.getByLabel('Preliminary condemnation package approved by ATP', exact).fill('2026-03-01')
    await form.getByRole('button', { name: 'Save condemnation' }).click()
    await expect(form.getByText('The package cannot be approved before it is submitted.')).toBeVisible()
    await form.getByLabel('Preliminary condemnation package approved by ATP', exact).fill('2026-03-12')

    await form.getByLabel('ED coordinator', exact).selectOption(agent)
    await form.getByLabel('Probate cause no.', exact).fill('PC-100')
    await form.getByRole('checkbox', { name: 'Objections filed?' }).check()
    await form.getByLabel('Status note', exact).fill('Petition drafted.')
    await form.getByRole('button', { name: 'Add status note' }).click()

    await form.getByRole('button', { name: 'Add attorney' }).click()
    await expect(form.getByText('Enter the party represented.')).toBeVisible()
    await form.getByLabel('Party represented', exact).fill('Owner')
    await form.getByLabel('Attorney firm', exact).fill('Law LLP')
    await form.getByRole('button', { name: 'Add attorney' }).click()
    await form.getByLabel('Name of commissioner', exact).fill('C. Commissioner')
    await form.getByRole('button', { name: 'Add commissioner' }).click()
    await form.getByLabel('Party to serve', exact).fill('Owner')
    await form.getByRole('button', { name: 'Add notice of hearing' }).click()
    await form.getByRole('button', { name: 'Save condemnation' }).click()
    await expect(page.getByText('Condemnation saved.')).toBeVisible()
    await expect(form).toContainText(`CON-${parcel.number}`)

    await attach(page, `Attachments for CON-${parcel.number}`, 'petition.pdf')
    await expect(page.getByRole('button', { name: 'Remove condemnation record' })).toHaveCount(0)

    await page.reload()
    await page.getByRole('tab', { name: 'Condemnation', ...exact }).click()
    const again = page.getByRole('form', { name: 'Condemnation' })
    await expect(again.getByLabel('Probate cause no.', exact)).toHaveValue('PC-100')
    await again.getByRole('button', { name: 'Remove attorney Owner' }).click()
    await again.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByText('Condemnation saved.')).toBeVisible()

    const admin = await adminHeaders(request)
    const got = (await (await request.get(`/api/v1/parcels/${parcel.id}/condemnation`, { headers: admin })).json()) as { record: { attorneys: unknown[]; commissioners: unknown[]; statusNotes: unknown[]; details: { objectionsFiled: boolean } } }
    expect(got.record.attorneys).toHaveLength(0)
    expect(got.record.commissioners).toHaveLength(1)
    expect(got.record.statusNotes).toHaveLength(1)
    expect(got.record.details.objectionsFiled).toBe(true)
    expect((await request.delete(`/api/v1/parcels/${parcel.id}/condemnation`, { headers: admin })).status()).toBe(204)
  })
})

test.describe('Project Manager · property management and disposition', () => {
  test('AT-PM-43 records the property transfer, disposition and funding of a parcel, attaches a file, changes it and removes it', async ({ page, request }) => {
    for (const [k, v] of [['disposition_type', 'Sale'], ['fund_type', 'Federal'], ['fund_type', 'Local']]) await ensureLibraryValue(request, k, v)
    const parcel = await makeParcel(request)
    await signInViaUi(page, 'ProjectManager')
    await openTab(page, parcel.id, 'Property management & disposition')
    const form = page.getByRole('form', { name: 'Property management and disposition' })

    await form.getByLabel('Sale price / consideration value ($)', exact).fill('-5')
    await form.getByRole('button', { name: 'Save property management' }).click()
    await expect(form.getByText('The amount must be zero or more.')).toBeVisible()
    await form.getByLabel('Sale price / consideration value ($)', exact).fill('125000.5')
    await form.getByLabel('Disposition type', exact).selectOption('Sale')
    await form.getByLabel('Buyer / grantee', exact).fill('City of Austin')
    await form.getByRole('checkbox', { name: 'FTA concurrence required?' }).check()

    await form.getByRole('button', { name: 'Add funding' }).click()
    await expect(form.getByText('Choose the fund type.')).toBeVisible()
    await form.getByLabel(/^Fund type/).selectOption('Federal')
    await form.getByLabel('Funding percentage', exact).fill('60')
    await form.getByRole('button', { name: 'Add funding' }).click()
    await form.getByLabel(/^Fund type/).selectOption('Local')
    await form.getByLabel('Funding percentage', exact).fill('50')
    await form.getByRole('button', { name: 'Add funding' }).click()
    await expect(form.getByText('The funding percentages cannot add up to more than 100.')).toBeVisible()
    await form.getByLabel('Funding percentage', exact).fill('40')
    await form.getByRole('button', { name: 'Add funding' }).click()
    await form.getByLabel('Status note', exact).fill('Memo sent.')
    await form.getByRole('button', { name: 'Add status note' }).click()
    await form.getByRole('button', { name: 'Save property management' }).click()
    await expect(page.getByText('Property management saved.')).toBeVisible()
    await expect(form).toContainText(`PMD-${parcel.number}`)

    await attach(page, `Attachments for PMD-${parcel.number}`, 'memo.pdf')
    await page.reload()
    await page.getByRole('tab', { name: 'Property management & disposition', ...exact }).click()
    const again = page.getByRole('form', { name: 'Property management and disposition' })
    await expect(again.getByLabel('Buyer / grantee', exact)).toHaveValue('City of Austin')
    await again.getByRole('button', { name: 'Remove funding Local' }).click()
    await again.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByText('Property management saved.')).toBeVisible()

    const admin = await adminHeaders(request)
    const got = (await (await request.get(`/api/v1/parcels/${parcel.id}/property-management`, { headers: admin })).json()) as { record: { funding: unknown[]; details: { salePrice: number } } }
    expect(got.record.funding).toHaveLength(1)
    expect(got.record.details.salePrice).toBe(125000.5)

    await again.getByRole('button', { name: 'Remove property management record' }).click()
    await expect(page.getByText('Assigned when saved')).toBeVisible()
  })
})

test.describe('Relocation Agent · relocation', () => {
  test('AT-RA-41 records a relocation with its contacts, eligibility and contact log, attaches a file, changes it and removes it', async ({ page, request }) => {
    for (const [k, v] of [['relocation_type', 'Residential'], ['eligibility_type', 'Moving Expenses']]) await ensureLibraryValue(request, k, v)
    const parcel = await makeParcel(request)
    const ownerName = unique('Reloc Owner')
    expect((await request.post(`/api/v1/parcels/${parcel.id}/parties`, { headers: await adminHeaders(request), data: { newParty: { kind: 'Person', name: ownerName }, role: 'FeeOwner', ownerType: 'Fee Owner', leaseOnFile: false } })).status()).toBe(201)
    await signInViaUi(page, 'RelocationAgent')
    await openTab(page, parcel.id, 'Relocation')
    const form = page.getByRole('form', { name: 'Add a relocation' })

    await form.getByRole('button', { name: 'Add relocation', ...exact }).click()
    for (const m of ['Relocation number is required.', 'Displacee name is required.', 'Relocation type is required.']) await expect(form.getByText(m)).toBeVisible()
    await form.getByLabel(/^Relocation number/).fill('R-7')
    await form.getByLabel(/^Ownership interest/).selectOption({ label: `${ownerName} (Fee owner)` })
    await expect(form.getByLabel(/^Displacee name/)).toHaveValue(ownerName)
    await form.getByLabel(/^Relocation type/).selectOption('Residential')
    await form.getByLabel('GIN mailed date', exact).fill('2026-03-05')
    await form.getByLabel('GIN delivered date', exact).fill('2026-03-01')
    await form.getByRole('button', { name: 'Add relocation', ...exact }).click()
    await expect(form.getByText('The GIN cannot be delivered before it is mailed.')).toBeVisible()
    await form.getByLabel('GIN delivered date', exact).fill('2026-03-07')

    await form.getByLabel('Contact name', exact).fill('Alex')
    await form.getByRole('checkbox', { name: 'Primary contact' }).check()
    await form.getByRole('button', { name: 'Add relocation contact' }).click()
    await form.getByLabel('Contact name', exact).fill('Blair')
    await form.getByRole('checkbox', { name: 'Primary contact' }).check()
    await form.getByRole('button', { name: 'Add relocation contact' }).click()
    await expect(form.getByText('Only one contact can be the primary contact.')).toBeVisible()

    await form.getByLabel('Eligibility type', exact).selectOption('Moving Expenses')
    await form.getByLabel('Claim limit', exact).fill('5000')
    await form.getByRole('button', { name: 'Add eligibility' }).click()
    await form.getByLabel('Log date and time', exact).fill('2026-03-04T15:30')
    await form.getByLabel('Log discussion notes', exact).fill('Left a message.')
    await form.getByRole('button', { name: 'Add contact log entry' }).click()
    await form.getByLabel('Status note', exact).fill('Interview booked.')
    await form.getByRole('button', { name: 'Add status note' }).click()
    await form.getByRole('button', { name: 'Add relocation', ...exact }).click()

    const table = page.getByRole('table', { name: 'Relocations' })
    await expect(table).toContainText('R-7')
    await expect(table).toContainText(ownerName)

    await page.getByRole('button', { name: 'Edit relocation R-7' }).click()
    await attach(page, 'Attachments for relocation R-7', 'plan.pdf')
    const change = page.getByRole('form', { name: 'Change relocation' })
    await change.getByLabel('Target vacate date', exact).fill('2026-09-01')
    await change.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByRole('form', { name: 'Add a relocation' })).toBeVisible()
    await expect(table).toContainText('2026')

    const admin = await adminHeaders(request)
    const rows = (await (await request.get(`/api/v1/parcels/${parcel.id}/relocations`, { headers: admin })).json()) as { details: { targetVacateDate: string }; contacts: unknown[]; eligibilities: unknown[]; contactLog: unknown[]; statusNotes: unknown[] }[]
    expect(rows).toHaveLength(1)
    expect(rows[0].details.targetVacateDate).toBe('2026-09-01')
    expect(rows[0].contacts).toHaveLength(1)
    expect(rows[0].eligibilities).toHaveLength(1)
    expect(rows[0].contactLog).toHaveLength(1)
    expect(rows[0].statusNotes).toHaveLength(1)

    await page.getByRole('button', { name: 'Remove relocation R-7' }).click()
    await expect(page.getByText('No relocations yet')).toBeVisible()
  })
})

test.describe('Everyone · condemnation, property management and relocation', () => {
  for (const role of ROLES) {
    test(`AT-X-54 ${role}: the Condemnation tab, its buttons and the API follow the access spec`, async ({ page, request }) => {
      const parcel = await makeParcel(request)
      const other = await makeParcel(request)
      const admin = await adminHeaders(request)
      const body = { details: { probateCauseNo: 'SEED' } }
      expect((await request.put(`/api/v1/parcels/${parcel.id}/condemnation`, { headers: admin, data: body })).status()).toBe(200)

      await signInViaUi(page, role)
      await page.goto(`/parcels/${parcel.id}`)
      await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toBeVisible()
      const readable = level(role, 'parcelcondemnation') !== null
      await expect(page.getByRole('tab', { name: 'Condemnation', ...exact }), `${role} tab`).toHaveCount(readable ? 1 : 0)
      if (readable) {
        await page.getByRole('tab', { name: 'Condemnation', ...exact }).click()
        await expect(page.getByRole('form', { name: 'Condemnation' })).toBeVisible()
        await expect(page.getByRole('button', { name: 'Save changes' }), `${role} save`).toHaveCount(canUpdate(role, 'parcelcondemnation') ? 1 : 0)
        await expect(page.getByRole('button', { name: 'Remove condemnation record' }), `${role} remove`).toHaveCount(canDelete(role, 'parcelcondemnation') ? 1 : 0)
      }
      const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
      const url = (id: string) => `/api/v1/parcels/${id}/condemnation`
      const want = (ok: boolean, status: number) => (ok ? status : 403)
      expect((await request.get(url(parcel.id), { headers })).status()).toBe(want(readable, 200))
      expect((await request.put(url(other.id), { headers, data: body })).status()).toBe(want(canCreate(role, 'parcelcondemnation'), 200))
      expect((await request.put(url(parcel.id), { headers, data: { details: { probateCauseNo: `by ${role}` } } })).status()).toBe(want(canUpdate(role, 'parcelcondemnation'), 200))
      expect((await request.delete(url(parcel.id), { headers })).status()).toBe(want(canDelete(role, 'parcelcondemnation'), 204))
    })

    test(`AT-X-55 ${role}: the Property management tab, its buttons and the API follow the access spec`, async ({ page, request }) => {
      const parcel = await makeParcel(request)
      const other = await makeParcel(request)
      const admin = await adminHeaders(request)
      const body = { details: { buyerGrantee: 'SEED' } }
      expect((await request.put(`/api/v1/parcels/${parcel.id}/property-management`, { headers: admin, data: body })).status()).toBe(200)

      await signInViaUi(page, role)
      await page.goto(`/parcels/${parcel.id}`)
      await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toBeVisible()
      const readable = level(role, 'parcelproperty') !== null
      await expect(page.getByRole('tab', { name: 'Property management & disposition', ...exact }), `${role} tab`).toHaveCount(readable ? 1 : 0)
      if (readable) {
        await page.getByRole('tab', { name: 'Property management & disposition', ...exact }).click()
        await expect(page.getByRole('form', { name: 'Property management and disposition' })).toBeVisible()
        await expect(page.getByRole('button', { name: 'Save changes' }), `${role} save`).toHaveCount(canUpdate(role, 'parcelproperty') ? 1 : 0)
        await expect(page.getByRole('button', { name: 'Remove property management record' }), `${role} remove`).toHaveCount(canDelete(role, 'parcelproperty') ? 1 : 0)
      }
      const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
      const url = (id: string) => `/api/v1/parcels/${id}/property-management`
      const want = (ok: boolean, status: number) => (ok ? status : 403)
      expect((await request.get(url(parcel.id), { headers })).status()).toBe(want(readable, 200))
      expect((await request.put(url(other.id), { headers, data: body })).status()).toBe(want(canCreate(role, 'parcelproperty'), 200))
      expect((await request.put(url(parcel.id), { headers, data: { details: { buyerGrantee: `by ${role}` } } })).status()).toBe(want(canUpdate(role, 'parcelproperty'), 200))
      expect((await request.delete(url(parcel.id), { headers })).status()).toBe(want(canDelete(role, 'parcelproperty'), 204))
    })

    test(`AT-X-56 ${role}: the Relocation tab, its buttons and the API follow the access spec`, async ({ page, request }) => {
      const parcel = await makeParcel(request)
      const admin = await adminHeaders(request)
      const details = (n: string) => ({ details: { relocationNumber: n, displaceeName: 'Kamali', relocationType: 'Residential' } })
      const seed = (await (await request.post(`/api/v1/parcels/${parcel.id}/relocations`, { headers: admin, data: details('SEED') })).json()) as { id: string }
      const doomed = (await (await request.post(`/api/v1/parcels/${parcel.id}/relocations`, { headers: admin, data: details('DOOMED') })).json()) as { id: string }

      await signInViaUi(page, role)
      await page.goto(`/parcels/${parcel.id}`)
      await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toBeVisible()
      const readable = level(role, 'parcelrelocation') !== null
      await expect(page.getByRole('tab', { name: 'Relocation', ...exact }), `${role} tab`).toHaveCount(readable ? 1 : 0)
      if (readable) {
        await page.getByRole('tab', { name: 'Relocation', ...exact }).click()
        await expect(page.getByRole('table', { name: 'Relocations' })).toBeVisible()
        await expect(page.getByRole('form', { name: 'Add a relocation' }), `${role} add`).toHaveCount(canCreate(role, 'parcelrelocation') ? 1 : 0)
        await expect(page.getByRole('button', { name: 'Edit relocation SEED' }), `${role} edit`).toHaveCount(canUpdate(role, 'parcelrelocation') ? 1 : 0)
        await expect(page.getByRole('button', { name: 'Remove relocation SEED' }), `${role} remove`).toHaveCount(canDelete(role, 'parcelrelocation') ? 1 : 0)
      }
      const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
      const url = `/api/v1/parcels/${parcel.id}/relocations`
      const want = (ok: boolean, status: number) => (ok ? status : 403)
      expect((await request.get(url, { headers })).status()).toBe(want(readable, 200))
      expect((await request.post(url, { headers, data: details(`by-${role}`) })).status()).toBe(want(canCreate(role, 'parcelrelocation'), 201))
      expect((await request.put(`${url}/${seed.id}`, { headers, data: { details: { ...details('SEED').details, displaceeName: `by ${role}` } } })).status()).toBe(want(canUpdate(role, 'parcelrelocation'), 200))
      expect((await request.delete(`${url}/${doomed.id}`, { headers })).status()).toBe(want(canDelete(role, 'parcelrelocation'), 204))
    })
  }
})
