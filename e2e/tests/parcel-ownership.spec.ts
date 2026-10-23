import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { canUpdate, devToken, level, newId, ROLES, sessionToken, signInViaUi, PARCEL_TAB_OF } from './support'

/**
 * A parcel's Ownership interests tab (ADR 0023: Ownership Details and Parties combined, with the derived Owner name) and the read-only
 * Parcel Location tab (ADR 0017). Real browser, UI, API and Postgres. Case ids match vault/acceptance/Designer.md and Everyone.md.
 * Every test makes its own parcel.
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
  await page.getByRole('tab', { name: PARCEL_TAB_OF[tab] ?? tab, exact: true }).click()
}

test.describe('Designer · ownership interests', () => {
  test('AT-DE-42 records an owner with its type, an address and contacts in one form, sees the owner name on the parcel, changes and removes it', async ({ page, request }) => {
    const parcel = await makeParcel(request)
    await signInViaUi(page, 'Designer')
    await openTab(page, parcel.id, 'Ownership interests')
    await page.getByRole('button', { name: 'Add a person or organization' }).click()
    const form = page.getByRole('form', { name: 'Add a person or organization' })
    await form.getByLabel('A new person or organization').check()

    await form.getByRole('button', { name: 'Add', exact: true }).click()
    await expect(form.getByText('Name is required.')).toBeVisible()
    await form.getByLabel(/^New party Name/).fill('Kamali')
    await form.getByLabel(/^Owner type/).selectOption('Fee Owner')

    await form.getByLabel(/^Address type/).selectOption('Tax Card Mailing Address')
    await form.getByLabel(/^Address$/).fill('1 Main St')
    await form.getByLabel(/^Address contact person/).fill('Kamali, Devanshi')
    await form.getByLabel(/^City$/).fill('Chennai')
    await form.getByLabel(/^Zip/).fill('123')
    await form.getByRole('button', { name: 'Add address' }).click()
    await expect(form.getByRole('table', { name: 'Addresses' })).toContainText('Chennai')

    await form.getByLabel(/^Contact type/).selectOption('Broker')
    await form.getByLabel(/^Contact name/).fill('Dev Kamali')
    await form.getByLabel(/^Contact phone number/).fill('(112) 345-6789')
    await form.getByLabel(/^Contact email/).fill('kamali.t@aurigo.com')
    await form.getByLabel('Is primary').check()
    await form.getByRole('button', { name: 'Add contact' }).click()
    await expect(form.getByRole('table', { name: 'Contacts' })).toContainText('Dev Kamali')

    await form.getByRole('button', { name: 'Add', exact: true }).click()
    const row = page.getByRole('row').filter({ hasText: 'Kamali' }).filter({ hasText: 'Fee owner' })
    await expect(row).toContainText('Fee owner')
    await expect(row).toContainText('Fee Owner')

    // The parcel shows its owner name and cannot be given one by hand.
    await page.getByRole('tab', { name: 'Summary' }).click()
    await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toContainText('Kamali')
    await page.getByRole('button', { name: 'Edit parcel' }).click()
    const parcelForm = page.getByRole('form', { name: 'Edit parcel' })
    await expect(parcelForm.getByLabel(/^Owner name/)).toBeDisabled()
    await expect(parcelForm.getByLabel(/^Owner name/)).toHaveValue('Kamali')
    await parcelForm.getByRole('button', { name: 'Cancel' }).click()

    // Change the owner and the person behind it in one form.
    await page.getByRole('tab', { name: 'Ownership interests' }).click()
    await page.getByRole('button', { name: 'Edit Kamali, Fee owner' }).click()
    const edit = page.getByRole('form', { name: 'Edit Kamali, Fee owner' })
    await expect(edit.getByLabel(/^Party Name/)).toHaveValue('Kamali')
    await edit.getByLabel(/^Party Name/).fill('Kamali Family Trust')
    await edit.getByRole('button', { name: 'Remove contact Dev Kamali' }).click()
    await edit.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByRole('row', { name: /Kamali Family Trust/ })).toBeVisible()

    const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
    const people = await (await request.get(`/api/v1/parcels/${parcel.id}/parties`, { headers })).json()
    expect(people.ownership).toHaveLength(1)
    expect(people.ownership[0]).toMatchObject({ role: 'FeeOwner', ownerType: 'Fee Owner' })
    const party = await (await request.get(`/api/v1/parties/${people.ownership[0].party.id}`, { headers })).json()
    expect(party).toMatchObject({ name: 'Kamali Family Trust', contacts: [] })
    expect(party.addresses).toHaveLength(1)
    expect((await (await request.get(`/api/v1/parcels/${parcel.id}`, { headers })).json()).ownerName).toBe('Kamali Family Trust')

    await page.getByRole('button', { name: 'Remove Kamali Family Trust, Fee owner' }).click()
    await expect(page.getByText('No owners or interest holders recorded.')).toBeVisible()
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
    await expect(page.locator('[data-section="parcellocation"]').getByRole('form')).toHaveCount(0)

    const [download] = await Promise.all([page.waitForEvent('download'), page.getByRole('button', { name: 'Export to CSV' }).click()])
    expect(download.suggestedFilename()).toBe(`parcel-location-${parcel.number}.csv`)
  })
})

test.describe('Everyone · ownership and location', () => {
  for (const role of ROLES) {
    test(`AT-X-48 ${role}: the Ownership interests and Parcel location tabs, their buttons and the API follow the access spec`, async ({ page, request }) => {
      const parcel = await makeParcel(request)
      const admin = await adminHeaders(request)
      const owner = (name: string) => ({ newParty: { kind: 'Person', name }, role: 'FeeOwner', leaseOnFile: false })
      const seed = await (await request.post(`/api/v1/parcels/${parcel.id}/parties`, { headers: admin, data: owner('Seed Owner') })).json()

      await signInViaUi(page, role)
      await page.goto(`/parcels/${parcel.id}`)
      await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toBeVisible()

      await page.getByRole('tab', { name: 'Summary', exact: true }).click()
      await expect(page.locator('[data-section="parcellocation"]'), `${role} Parcel location section`).toHaveCount(level(role, 'parcellocation') !== null ? 1 : 0)
      await expect(page.getByRole('tab', { name: 'Ownership details' }), `${role}: the old Ownership details tab is gone`).toHaveCount(0)
      const readable = level(role, 'parties') !== null
      await expect(page.getByRole('tab', { name: 'Ownership interests' }), `${role} Ownership interests tab`).toHaveCount(readable ? 1 : 0)
      if (readable) {
        await page.getByRole('tab', { name: 'Ownership interests' }).click()
        await expect(page.getByRole('region', { name: 'Owners and interests' })).toBeVisible()
        await expect(page.getByRole('button', { name: 'Add a person or organization' }), `${role} add`).toHaveCount(canCreate(role, 'parties') ? 1 : 0)
        await expect(page.getByRole('button', { name: 'Edit Seed Owner, Fee owner' }), `${role} edit`).toHaveCount(canUpdate(role, 'parties') ? 1 : 0)
        await expect(page.getByRole('button', { name: 'Remove Seed Owner, Fee owner' }), `${role} remove`).toHaveCount(canDelete(role, 'parties') ? 1 : 0)
      }

      const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
      const url = `/api/v1/parcels/${parcel.id}/parties`
      const want = (action: 'read' | 'create' | 'update' | 'delete', ok: number) =>
        ({ read: readable, create: canCreate(role, 'parties'), update: canUpdate(role, 'parties'), delete: canDelete(role, 'parties') })[action] ? ok : 403
      expect((await request.get(url, { headers })).status()).toBe(want('read', 200))
      expect((await request.post(url, { headers, data: owner(`By ${role}`) })).status()).toBe(want('create', 201))
      expect((await request.put(`/api/v1/party-links/${seed.id}`, { headers, data: { ownerType: `edited by ${role}`, leaseOnFile: false } })).status()).toBe(want('update', 200))
      expect((await request.delete(`/api/v1/party-links/${seed.id}`, { headers })).status()).toBe(want('delete', 204))
    })
  }
})
