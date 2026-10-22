import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { devToken, newId, signInViaUi, signOutViaUi } from './support'

/**
 * ADR 0026: the relocation case workflow. A relocation is for one ownership interest; the parcel becomes Acquired when every displaced interest
 * is relocated; payments go through the approval chain; the Relocations work screen. Real browser, UI, API and Postgres.
 * Case ids match vault/acceptance/RelocationAgent.md, ProjectManager.md, Finance.md and Everyone.md.
 */

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

async function makeParcel(request: APIRequestContext) {
  const headers = await adminHeaders(request)
  const project = await (await request.post('/api/v1/projects', { headers, data: { name: unique('Relocation scene'), oversightType: 'Local', phase: 'Phase 1' } })).json()
  return (await (await request.post('/api/v1/parcels', { headers, data: { projectId: project.id, areaSqFt: 1000, acquisitionType: 'Standard', address: `${newId().slice(0, 8)} Move Rd` } })).json()) as { id: string; number: string }
}

async function addOwner(request: APIRequestContext, parcelId: string, name: string, displaced: boolean) {
  const res = await request.post(`/api/v1/parcels/${parcelId}/parties`, {
    headers: await adminHeaders(request), data: { newParty: { kind: 'Person', name }, role: 'FeeOwner', ownerType: 'Fee Owner', leaseOnFile: false, displacementRequired: displaced },
  })
  expect(res.status()).toBe(201)
  return (await res.json()) as { id: string }
}

async function statusOf(request: APIRequestContext, parcelId: string) {
  return ((await (await request.get(`/api/v1/parcels/${parcelId}`, { headers: await adminHeaders(request) })).json()) as { statusName: string }).statusName
}

async function openTab(page: Page, parcelId: string, tab: string) {
  await page.goto(`/parcels/${parcelId}`)
  await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toBeVisible()
  await page.getByRole('tab', { name: tab, ...exact }).click()
}

async function addRelocation(page: Page, number: string, owner: string, complete?: string) {
  const form = page.getByRole('form', { name: 'Add a relocation' })
  await form.getByLabel(/^Relocation number/).fill(number)
  await form.getByLabel(/^Ownership interest/).selectOption({ label: `${owner} (Fee owner, displacement required)` })
  await form.getByLabel(/^Relocation type/).selectOption('Residential')
  if (complete) await form.getByLabel('Relocation complete', exact).fill(complete)
  await form.getByRole('button', { name: 'Add relocation', ...exact }).click()
  await expect(page.getByRole('table', { name: 'Relocations' })).toContainText(number)
}

test.describe('Relocation Agent · relocation cases', () => {
  test('AT-RA-42 a parcel becomes Acquired once every displaced ownership interest has finished relocating', async ({ page, request }) => {
    await ensureLibraryValue(request, 'relocation_type', 'Residential')
    const parcel = await makeParcel(request)
    const a = unique('Owner A')
    const b = unique('Owner B')
    await addOwner(request, parcel.id, a, true)
    await addOwner(request, parcel.id, b, true)
    await addOwner(request, parcel.id, unique('Owner C'), false)
    const before = await statusOf(request, parcel.id)

    await signInViaUi(page, 'RelocationAgent')
    await openTab(page, parcel.id, 'Relocation')
    const form = page.getByRole('form', { name: 'Add a relocation' })
    await form.getByLabel(/^Relocation number/).fill('R-X')
    await form.getByRole('button', { name: 'Add relocation', ...exact }).click()
    await expect(form.getByText('Choose the ownership interest this relocation is for.')).toBeVisible()

    await addRelocation(page, 'R-A', a, '2026-06-01')
    await expect(page.getByRole('status', { name: 'Relocation progress' })).toContainText('1 of 2 displaced interests relocated')
    expect(await statusOf(request, parcel.id)).toBe(before)

    await addRelocation(page, 'R-B', b, '2026-06-15')
    await expect(page.getByRole('status', { name: 'Relocation progress' })).toContainText('2 of 2 displaced interests relocated')
    expect(await statusOf(request, parcel.id)).toBe('Acquired')
  })

  test('AT-RA-43 a payment is requested, signed by the Project Manager and paid by Finance', async ({ page, request }) => {
    for (const [k, v] of [['relocation_type', 'Residential'], ['relocation_payment_type', 'Moving expense']]) await ensureLibraryValue(request, k, v)
    const admin = await adminHeaders(request)
    expect((await request.put('/api/v1/relocation-payment-bands', {
      headers: admin, data: { bands: [{ minAmount: 0, maxAmount: 25000, roles: ['ProjectManager'] }, { minAmount: 25000, maxAmount: null, roles: ['ProjectManager', 'Finance'] }] },
    })).ok()).toBeTruthy()
    const parcel = await makeParcel(request)
    const owner = unique('Pay Owner')
    await addOwner(request, parcel.id, owner, true)

    await signInViaUi(page, 'RelocationAgent')
    await openTab(page, parcel.id, 'Relocation')
    await addRelocation(page, 'R-PAY', owner)
    await page.getByRole('button', { name: 'Payments for relocation R-PAY' }).click()
    const request1 = page.getByRole('form', { name: 'Request a payment' })
    await request1.getByLabel(/^Payment type/).selectOption('Moving expense')
    await request1.getByLabel(/^Payment amount/).fill('1500')
    await request1.getByRole('button', { name: 'Request payment' }).click()
    const table = page.getByRole('table', { name: 'Relocation payments' })
    await expect(table).toContainText('$1,500.00')
    await expect(table).toContainText('Requested')
    await expect(table.getByRole('button', { name: /Approve payment/ })).toBeDisabled()
    await signOutViaUi(page)

    await signInViaUi(page, 'ProjectManager')
    await openTab(page, parcel.id, 'Relocation')
    await page.getByRole('button', { name: 'Payments for relocation R-PAY' }).click()
    await page.getByRole('button', { name: /Approve payment Moving expense/ }).click()
    await expect(page.getByRole('table', { name: 'Relocation payments' })).toContainText('Approved')
    await signOutViaUi(page)

    await signInViaUi(page, 'Finance')
    await openTab(page, parcel.id, 'Relocation')
    await page.getByRole('button', { name: 'Payments for relocation R-PAY' }).click()
    await page.getByLabel('Payment reference').fill('EFT-1')
    await page.getByRole('button', { name: /Mark paid Moving expense/ }).click()
    await expect(page.getByRole('table', { name: 'Relocation payments' })).toContainText('Paid')
    await expect(page.getByRole('table', { name: 'Relocation payments' })).toContainText('Ref EFT-1')
  })
})

test.describe('Everyone · Relocations work screen', () => {
  test('AT-X-64 finds a relocation by its owner and opens its parcel to work on it', async ({ page, request }) => {
    await ensureLibraryValue(request, 'relocation_type', 'Residential')
    const parcel = await makeParcel(request)
    const owner = `Quillfeather${newId().replace(/-/g, '').slice(0, 8)}`
    const link = await addOwner(request, parcel.id, owner, true)
    const made = await request.post(`/api/v1/parcels/${parcel.id}/relocations`, {
      headers: await adminHeaders(request), data: { details: { relocationNumber: 'RW-1', displaceeName: owner, relocationType: 'Residential', partyLinkId: link.id } },
    })
    expect(made.status()).toBe(201)

    await signInViaUi(page, 'RelocationAgent')
    await page.getByRole('link', { name: 'Relocations', ...exact }).click()
    await expect(page.getByRole('heading', { name: 'Relocations', ...exact })).toBeVisible()
    await page.getByLabel('Search', exact).fill(owner)
    const found = page.getByRole('table', { name: 'Relocations found' })
    await expect(found).toContainText('RW-1')
    await expect(found).toContainText('Not started')
    await page.getByRole('button', { name: 'Open RW-1' }).click()
    const panel = page.getByRole('region', { name: 'Selected parcel' })
    await expect(panel).toContainText(`Parcel ${parcel.number}`)
    await expect(panel.getByRole('button', { name: 'Edit relocation RW-1' })).toBeVisible()
  })
})
