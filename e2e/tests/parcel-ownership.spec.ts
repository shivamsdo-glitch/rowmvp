import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { canUpdate, devToken, level, newId, ROLES, sessionToken, signInViaUi } from './support'

/**
 * Phase 1.5 slice 5c (ADR 0017): a parcel's Ownership Details (with its derived Owner name) and the read-only Parcel Location tab.
 * Real browser, UI, API and Postgres. Case ids match vault/acceptance/Designer.md and Everyone.md. Every test makes its own parcel.
 */

const canCreate = (role: string, page: string) => ['S', 'P'].includes(level(role, page) ?? '')
const canDelete = (role: string, page: string) => level(role, page) === 'P'
const unique = (label: string) => `${label} ${newId().slice(0, 8)}`

async function adminHeaders(request: APIRequestContext) {
  return { Authorization: `Bearer ${await devToken(request, 'Admin')}` }
}

async function makeParcel(request: APIRequestContext, extra: Record<string, unknown> = {}) {
  const headers = await adminHeaders(request)
  const project = await (await request.post('/api/v1/projects', { headers, data: { name: unique('Ownership scene'), oversightType: 'Local', phase: 'Phase 1' } })).json()
  const parcel = await (await request.post('/api/v1/parcels', { headers, data: { projectId: project.id, areaSqFt: 1000, acquisitionType: 'Standard', address: 'Owners Rd', ...extra } })).json()
  return parcel as { id: string; number: string }
}

async function openTab(page: Page, parcelId: string, tab: string) {
  await page.goto(`/parcels/${parcelId}`)
  await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toBeVisible()
  await page.getByRole('tab', { name: tab }).click()
}

test.describe('Designer · ownership details', () => {
  test('AT-DE-42 records an ownership with an address and contacts, sees the owner name on the parcel, changes and removes it', async ({ page, request }) => {
    const parcel = await makeParcel(request)
    await signInViaUi(page, 'Designer')
    await openTab(page, parcel.id, 'Ownership details')
    const form = page.getByRole('form', { name: 'Add an ownership' })

    await form.getByRole('button', { name: 'Add ownership' }).click()
    await expect(form.getByText('Owner name is required.')).toBeVisible()
    await form.getByLabel(/^Owner name/).fill('Kamali')
    await form.getByLabel(/^Owner type/).selectOption('Fee Owner')

    await form.getByLabel(/^Address type/).selectOption('Tax Card Mailing Address')
    await form.getByLabel(/^Address$/).fill('1 Main St')
    await form.getByLabel(/^Address contact person/).fill('Kamali, Devanshi')
    await form.getByLabel(/^City/).fill('Chennai')
    await form.getByLabel(/^Zip/).fill('123')
    await form.getByRole('button', { name: 'Add address' }).click()
    await expect(form.getByRole('table', { name: 'Addresses' })).toContainText('Chennai')

    await form.getByLabel(/^Contact type/).selectOption('Broker')
    await form.getByLabel(/^Contact name/).fill('Dev Kamali')
    await form.getByLabel(/^Phone number/).fill('(112) 345-6789')
    await form.getByLabel(/^Email/).fill('kamali.t@aurigo.com')
    await form.getByLabel('Is primary').check()
    await form.getByRole('button', { name: 'Add contact' }).click()
    await expect(form.getByRole('table', { name: 'Contacts' })).toContainText('Dev Kamali')

    await form.getByRole('button', { name: 'Add ownership' }).click()
    const row = page.getByRole('row', { name: /Fee Owner/ })
    await expect(row).toContainText('Kamali')
    await expect(row.getByRole('cell').nth(0)).toHaveText('1')

    // The parcel shows its owner name and cannot be given one by hand.
    await page.getByRole('tab', { name: 'Summary' }).click()
    await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toContainText('Kamali')
    await page.getByRole('button', { name: 'Edit parcel' }).click()
    const parcelForm = page.getByRole('form', { name: 'Edit parcel' })
    await expect(parcelForm.getByLabel(/^Owner name/)).toBeDisabled()
    await expect(parcelForm.getByLabel(/^Owner name/)).toHaveValue('Kamali')
    await parcelForm.getByRole('button', { name: 'Cancel' }).click()

    // Change it, attach a file, then remove everything.
    await page.getByRole('tab', { name: 'Ownership details' }).click()
    await page.getByRole('button', { name: 'Edit ownership 1' }).click()
    const edit = page.getByRole('form', { name: 'Change ownership' })
    await expect(edit.getByLabel(/^Owner name/)).toHaveValue('Kamali')
    await edit.getByLabel(/^Owner name/).fill('Kamali Family Trust')
    await edit.getByRole('button', { name: 'Remove contact Dev Kamali' }).click()
    await edit.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByRole('row', { name: /Kamali Family Trust/ })).toBeVisible()

    await page.getByRole('button', { name: 'Edit ownership 1' }).click()
    const panel = page.getByRole('region', { name: 'Attachments for ownership 1' })
    await panel.getByLabel('Upload document').setInputFiles({ name: 'deed.txt', mimeType: 'text/plain', buffer: Buffer.from('deed of record') })
    await expect(panel.getByText('deed.txt')).toBeVisible()

    const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
    const list = await (await request.get(`/api/v1/parcels/${parcel.id}/ownerships`, { headers })).json()
    expect(list).toHaveLength(1)
    expect(list[0]).toMatchObject({ ownershipNumber: 1, ownerName: 'Kamali Family Trust', ownerType: 'Fee Owner', contacts: [] })
    expect(list[0].addresses).toHaveLength(1)
    expect((await (await request.get(`/api/v1/parcels/${parcel.id}`, { headers })).json()).ownerName).toBe('Kamali Family Trust')

    await panel.getByRole('button', { name: 'Remove deed.txt' }).click()
    await expect(panel.getByText('No attachments')).toBeVisible()
    await page.getByRole('button', { name: 'Remove ownership 1' }).click()
    await expect(page.getByText('No ownerships recorded')).toBeVisible()
    await expect.poll(async () => (await (await request.get(`/api/v1/parcels/${parcel.id}`, { headers })).json()).ownerName).toBeNull()
  })

  test('AT-DE-43 the Parcel location tab is a read-only grid with the parcel id, CAD number and ESRI status, exportable to CSV', async ({ page, request }) => {
    const parcel = await makeParcel(request, { cadNumber: 'CAD-2026-77' })
    await signInViaUi(page, 'Designer')
    await openTab(page, parcel.id, 'Parcel location')

    const row = page.getByRole('table', { name: 'Parcel location' }).getByRole('row').nth(1)
    await expect(row).toContainText(parcel.number)
    await expect(row).toContainText('CAD-2026-77')
    await expect(row).toContainText('Not saved')
    await expect(page.getByRole('form')).toHaveCount(0)

    const [download] = await Promise.all([page.waitForEvent('download'), page.getByRole('button', { name: 'Export to CSV' }).click()])
    expect(download.suggestedFilename()).toBe(`parcel-location-${parcel.number}.csv`)
  })
})

test.describe('Everyone · ownership and location', () => {
  for (const role of ROLES) {
    test(`AT-X-48 ${role}: the Ownership details and Parcel location tabs, their buttons and the API follow the access spec`, async ({ page, request }) => {
      const parcel = await makeParcel(request)
      const admin = await adminHeaders(request)
      const seed = await (await request.post(`/api/v1/parcels/${parcel.id}/ownerships`, { headers: admin, data: { ownerName: 'Seed Owner' } })).json()

      await signInViaUi(page, role)
      await page.goto(`/parcels/${parcel.id}`)
      await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toBeVisible()

      await expect(page.getByRole('tab', { name: 'Parcel location' }), `${role} Parcel location tab`).toHaveCount(level(role, 'parcellocation') !== null ? 1 : 0)
      const readable = level(role, 'ownership') !== null
      await expect(page.getByRole('tab', { name: 'Ownership details' }), `${role} Ownership tab`).toHaveCount(readable ? 1 : 0)
      if (readable) {
        await page.getByRole('tab', { name: 'Ownership details' }).click()
        await expect(page.getByRole('table', { name: 'Ownership details' })).toBeVisible()
        await expect(page.getByRole('form', { name: 'Add an ownership' }), `${role} add`).toHaveCount(canCreate(role, 'ownership') ? 1 : 0)
        await expect(page.getByRole('button', { name: 'Edit ownership 1' }), `${role} edit`).toHaveCount(canUpdate(role, 'ownership') ? 1 : 0)
        await expect(page.getByRole('button', { name: 'Remove ownership 1' }), `${role} remove`).toHaveCount(canDelete(role, 'ownership') ? 1 : 0)
      }

      const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
      const url = `/api/v1/parcels/${parcel.id}/ownerships`
      const want = (action: 'read' | 'create' | 'update' | 'delete', ok: number) =>
        ({ read: readable, create: canCreate(role, 'ownership'), update: canUpdate(role, 'ownership'), delete: canDelete(role, 'ownership') })[action] ? ok : 403
      expect((await request.get(url, { headers })).status()).toBe(want('read', 200))
      expect((await request.post(url, { headers, data: { ownerName: `By ${role}` } })).status()).toBe(want('create', 201))
      expect((await request.put(`${url}/${seed.id}`, { headers, data: { ownerName: 'Seed Owner', ownerType: `edited by ${role}` } })).status()).toBe(want('update', 200))
      expect((await request.delete(`${url}/${seed.id}`, { headers })).status()).toBe(want('delete', 204))
    })
  }
})
