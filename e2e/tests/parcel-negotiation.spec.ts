import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { canUpdate, devToken, level, newId, ROLES, sessionToken, signInViaUi } from './support'

/**
 * Phase 1.5 slice 7a (ADR 0019): a parcel's Negotiation tab. Real browser, UI, API and Postgres.
 * Case ids match vault/acceptance/Negotiator.md and Everyone.md. Every test makes its own parcel.
 */

const canCreate = (role: string, page: string) => ['S', 'P'].includes(level(role, page) ?? '')
const canDelete = (role: string, page: string) => level(role, page) === 'P'
const unique = (label: string) => `${label} ${newId().slice(0, 8)}`

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
  const name = unique('Neg Agent')
  const res = await request.post('/api/v1/library/agents', { headers: await adminHeaders(request), data: { name, agentType: 'Broker', company: 'Acme Realty', phone: '555-0100', email: 'a@example.com' } })
  expect([200, 201]).toContain(res.status())
  return name
}

async function makeParcel(request: APIRequestContext) {
  const headers = await adminHeaders(request)
  const project = await (await request.post('/api/v1/projects', { headers, data: { name: unique('Negotiation scene'), oversightType: 'Local', phase: 'Phase 1' } })).json()
  return (await (await request.post('/api/v1/parcels', { headers, data: { projectId: project.id, areaSqFt: 1000, acquisitionType: 'Standard', address: 'Negotiation Rd' } })).json()) as { id: string; number: string }
}

async function openTab(page: Page, parcelId: string) {
  await page.goto(`/parcels/${parcelId}`)
  await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toBeVisible()
  await page.getByRole('tab', { name: 'Negotiation' }).click()
}

test.describe('Negotiator · negotiation', () => {
  test('AT-NE-42 records the negotiation of a parcel with its notes, agent, right of entry, offer, settlement and PUA, attaches a file, changes it and removes it', async ({ page, request }) => {
    await ensureLibraryValue(request, 'offer_type', 'Initial Offer')
    await ensureLibraryValue(request, 'settlement_type', 'Administrative')
    await ensureLibraryValue(request, 'jat_response', 'Approved')
    const agent = await makeAgent(request)
    const parcel = await makeParcel(request)
    await signInViaUi(page, 'Negotiator')
    await openTab(page, parcel.id)
    const form = page.getByRole('form', { name: 'Negotiation' })

    await expect(form).toContainText(parcel.number)
    await expect(form).toContainText('Negotiation Rd')
    await expect(form).toContainText('Assigned when saved')

    await form.getByRole('button', { name: 'Add status note' }).click()
    await expect(form.getByText('Enter the status note.')).toBeVisible()
    await form.getByLabel('Status note', { exact: true }).fill('Owner called back.')
    await form.getByRole('button', { name: 'Add status note' }).click()
    await expect(form.getByRole('table', { name: 'Negotiation status notes' })).toContainText('Owner called back.')

    await form.getByLabel('Agent', { exact: true }).selectOption(agent)
    await form.getByRole('button', { name: 'Add agent' }).click()

    await form.getByLabel('Right of entry letter mailed').fill('2026-05-02')
    await form.getByLabel('Right of entry letter delivered').fill('2026-05-01')
    await form.getByRole('button', { name: 'Add right of entry' }).click()
    await expect(form.getByText('A letter cannot be delivered before it is mailed.')).toBeVisible()
    await form.getByLabel('Right of entry letter delivered').fill('2026-05-04')
    await form.getByRole('button', { name: 'Add right of entry' }).click()

    await form.getByLabel('Offer type', { exact: true }).selectOption('Initial Offer')
    await form.getByLabel('Offer amount', { exact: true }).fill('200000')
    await form.getByRole('button', { name: 'Add offer' }).click()
    await form.getByLabel('Settlement type', { exact: true }).selectOption('Administrative')
    await form.getByLabel('Counteroffer amount', { exact: true }).fill('260000')
    await form.getByRole('button', { name: 'Add settlement' }).click()

    await form.getByLabel('Total agreement amount').fill('250000.75')
    await form.getByLabel('Counteroffer submitted to JAT').fill('2026-06-10')
    await form.getByLabel('Counteroffer response from JAT').fill('2026-06-01')
    await form.getByRole('button', { name: 'Save negotiation' }).click()
    await expect(form.getByText('The JAT cannot respond before the counteroffer is submitted.')).toBeVisible()
    await form.getByLabel('Counteroffer response from JAT').fill('2026-06-12')
    await form.getByLabel('JAT response', { exact: true }).selectOption('Approved')
    await form.getByLabel('PUA requested').fill('2026-07-01')
    await form.getByLabel('PUA approved amount').fill('5000')
    await form.getByRole('button', { name: 'Save negotiation' }).click()
    await expect(page.getByText('Negotiation saved.')).toBeVisible()
    await expect(form).toContainText(`NEG-${parcel.number}`)
    await expect(form.getByRole('table', { name: 'Agents' })).toContainText('Acme Realty')

    const panel = page.getByRole('region', { name: `Attachments for NEG-${parcel.number}` })
    await panel.getByLabel('Upload document').setInputFiles({ name: 'offer.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4 test') })
    await expect(panel.getByText('offer.pdf')).toBeVisible()

    await page.reload()
    await page.getByRole('tab', { name: 'Negotiation' }).click()
    const again = page.getByRole('form', { name: 'Negotiation' })
    await expect(again.getByLabel('Total agreement amount')).toHaveValue('250000.75')
    await again.getByRole('button', { name: 'Remove offer 1' }).click()
    await again.getByLabel('Notes', { exact: true }).fill('Closed out the offer.')
    await again.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByText('Negotiation saved.')).toBeVisible()

    const headers = await adminHeaders(request)
    const got = (await (await request.get(`/api/v1/parcels/${parcel.id}/negotiation`, { headers })).json()) as { record: { notes: string; offers: unknown[]; settlements: unknown[]; statusNotes: unknown[] } }
    expect(got.record).toMatchObject({ notes: 'Closed out the offer.' })
    expect(got.record.offers).toHaveLength(0)
    expect(got.record.settlements).toHaveLength(1)
    expect(got.record.statusNotes).toHaveLength(1)

    await again.getByRole('button', { name: 'Remove negotiation record' }).click()
    await expect(page.getByText('Assigned when saved')).toBeVisible()
  })
})

test.describe('Everyone · negotiation', () => {
  for (const role of ROLES) {
    test(`AT-X-52 ${role}: the Negotiation tab, its buttons and the API follow the access spec`, async ({ page, request }) => {
      const parcel = await makeParcel(request)
      const other = await makeParcel(request)
      const admin = await adminHeaders(request)
      expect((await request.put(`/api/v1/parcels/${parcel.id}/negotiation`, { headers: admin, data: { notes: 'SEED' } })).status()).toBe(200)

      await signInViaUi(page, role)
      await page.goto(`/parcels/${parcel.id}`)
      await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toBeVisible()

      const readable = level(role, 'parcelnegotiation') !== null
      await expect(page.getByRole('tab', { name: 'Negotiation' }), `${role} tab`).toHaveCount(readable ? 1 : 0)
      if (readable) {
        await page.getByRole('tab', { name: 'Negotiation' }).click()
        await expect(page.getByRole('form', { name: 'Negotiation' })).toBeVisible()
        await expect(page.getByRole('button', { name: 'Save changes' }), `${role} save`).toHaveCount(canUpdate(role, 'parcelnegotiation') ? 1 : 0)
        await expect(page.getByRole('button', { name: 'Remove negotiation record' }), `${role} remove`).toHaveCount(canDelete(role, 'parcelnegotiation') ? 1 : 0)
      }

      const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
      const url = (id: string) => `/api/v1/parcels/${id}/negotiation`
      const want = (ok: boolean, status: number) => (ok ? status : 403)
      expect((await request.get(url(parcel.id), { headers })).status()).toBe(want(readable, 200))
      // The first save of a parcel's record needs Create, a later one Update.
      expect((await request.put(url(other.id), { headers, data: { notes: `by ${role}` } })).status()).toBe(want(canCreate(role, 'parcelnegotiation'), 200))
      expect((await request.put(url(parcel.id), { headers, data: { notes: `edited by ${role}` } })).status()).toBe(want(canUpdate(role, 'parcelnegotiation'), 200))
      expect((await request.delete(url(parcel.id), { headers })).status()).toBe(want(canDelete(role, 'parcelnegotiation'), 204))
    })
  }
})
