import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { canUpdate, devToken, level, newId, ROLES, sessionToken, signInViaUi } from './support'

/**
 * Phase 1.5 slice 5a (ADR 0017): the parcel details form (CAD number, acquisition interest type, possession dates, segment, priority,
 * possession type, parcel location, Active parcel) and the Acres column of the interests grid. Real browser, UI, API and Postgres.
 * Tests share one database, so every project they create has a unique name.
 */

const canCreate = (role: string) => ['S', 'P'].includes(level(role, 'parcels') ?? '')
const unique = (label: string) => `${label} ${newId().slice(0, 8)}`

async function makeProject(request: APIRequestContext) {
  const token = await devToken(request, 'Admin')
  const res = await request.post('/api/v1/projects', {
    headers: { Authorization: `Bearer ${token}` }, data: { name: unique('Details scene'), oversightType: 'Local', phase: 'Phase 1' },
  })
  expect(res.status()).toBe(201)
  return (await res.json()) as { id: string; number: string }
}

async function openParcels(page: Page) {
  await page.getByRole('complementary', { name: 'Primary' }).getByRole('link', { name: 'Parcels' }).click()
  await expect(page.getByRole('heading', { name: 'Parcels', exact: true })).toBeVisible()
}

test.describe('Designer · parcel details', () => {
  test('AT-DE-40 adds a parcel with every details field, reads them on the summary, then edits and clears some', async ({ page, request }) => {
    const project = await makeProject(request)
    await signInViaUi(page, 'Designer')
    await openParcels(page)
    await page.getByRole('button', { name: 'New parcel' }).click()
    const form = page.getByRole('form', { name: 'New parcel' })

    await form.getByLabel(/^Project/).selectOption(project.id)
    await form.getByLabel(/^Area \(sq ft\)/).fill('87120')
    await form.getByLabel(/^Address/).fill('77 Details Way')
    await form.getByLabel(/^CAD number/).fill('CAD-2026-77')
    await form.getByLabel(/^Acquisition interest type/).selectOption('Permanent Easement')
    await form.getByLabel(/^Possession type/).selectOption('PUA')
    await form.getByLabel(/^Possession date/).fill('2026-06-01')
    await form.getByLabel(/^Proposed possession date/).fill('2026-05-01')
    await form.getByLabel(/^Segment/).fill('Segment A')
    await form.getByLabel(/^Priority/).fill('High')
    await form.getByLabel(/^Parcel location/).fill('North side of Main St')
    await expect(form.getByLabel('Active parcel')).toBeChecked()
    await form.getByRole('button', { name: 'Create parcel' }).click()

    await expect(page).toHaveURL(/\/parcels\/[0-9a-f-]{36}$/)
    const synopsis = page.getByRole('region', { name: 'Parcel synopsis' })
    for (const [label, value] of [
      ['CAD number', 'CAD-2026-77'], ['Acquisition interest type', 'Permanent Easement'], ['Possession type', 'PUA'], ['Segment', 'Segment A'],
      ['Priority', 'High'], ['Parcel location', 'North side of Main St'], ['Active parcel', 'Yes'],
    ]) await expect(synopsis.locator('div', { has: page.getByText(label, { exact: true }) }).last()).toContainText(value)

    // The server holds what the screen showed.
    const id = page.url().split('/').pop()!
    const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
    const saved = await (await request.get(`/api/v1/parcels/${id}`, { headers })).json()
    expect(saved).toMatchObject({ cadNumber: 'CAD-2026-77', possessionDate: '2026-06-01', proposedPossessionDate: '2026-05-01', isActiveParcel: true })

    // Edit: change one, clear one, untick Active parcel.
    await page.getByRole('button', { name: 'Edit parcel' }).click()
    const edit = page.getByRole('form', { name: 'Edit parcel' })
    await expect(edit.getByLabel(/^CAD number/)).toHaveValue('CAD-2026-77')
    await edit.getByLabel(/^CAD number/).fill('CAD-2026-78')
    await edit.getByLabel(/^Segment/).fill('')
    await edit.getByLabel('Active parcel').uncheck()
    await edit.getByRole('button', { name: 'Save parcel' }).click()
    await expect(synopsis.locator('div', { has: page.getByText('Active parcel', { exact: true }) }).last()).toContainText('No')
    await expect.poll(async () => (await (await request.get(`/api/v1/parcels/${id}`, { headers })).json()).cadNumber).toBe('CAD-2026-78')
    const after = await (await request.get(`/api/v1/parcels/${id}`, { headers })).json()
    expect(after.segment).toBeNull()
    expect(after.isActiveParcel).toBe(false)
  })

  test('AT-DE-40 the interests grid shows the acres next to the square feet', async ({ page, request }) => {
    const project = await makeProject(request)
    const headers = { Authorization: `Bearer ${await devToken(request, 'Admin')}` }
    const parcel = await (await request.post('/api/v1/parcels', {
      headers, data: { projectId: project.id, areaSqFt: 100000, acquisitionType: 'Standard' },
    })).json()
    const added = await request.post(`/api/v1/parcels/${parcel.id}/interests`, { headers, data: { type: 'PermanentEasement', areaSqFt: 43560 } })
    expect(added.status()).toBe(201)

    await signInViaUi(page, 'Designer')
    await page.goto(`/parcels/${parcel.id}`)
    await page.getByRole('tab', { name: 'Interests' }).click()
    const table = page.getByRole('table', { name: 'Interests' })
    await expect(table.getByRole('columnheader', { name: 'Acres' })).toBeVisible()
    await expect(table.getByRole('row').nth(1)).toContainText('43,560 sq ft')
    await expect(table.getByRole('row').nth(1).getByRole('cell').nth(2)).toHaveText('1')
  })
})

test.describe('Everyone · parcel details', () => {
  for (const role of ROLES) {
    test(`AT-DE-41 ${role}: New parcel follows create, Edit parcel follows update, and the API agrees`, async ({ page, request }) => {
      const project = await makeProject(request)
      const admin = { Authorization: `Bearer ${await devToken(request, 'Admin')}` }
      const parcel = await (await request.post('/api/v1/parcels', {
        headers: admin, data: { projectId: project.id, areaSqFt: 1000, acquisitionType: 'Standard' },
      })).json()

      await signInViaUi(page, role)
      const mine = { Authorization: `Bearer ${await sessionToken(page)}` }
      const body = { areaSqFt: 1000, acquisitionType: 'Standard', priority: `by ${role}` }

      const update = await request.put(`/api/v1/parcels/${parcel.id}`, { headers: mine, data: body })
      expect(update.status(), `${role} update`).toBe(canUpdate(role, 'parcels') ? 200 : 403)
      const create = await request.post('/api/v1/parcels', { headers: mine, data: { ...body, projectId: project.id } })
      expect(create.status(), `${role} create`).toBe(canCreate(role) ? 201 : 403)

      if (level(role, 'parcels') === null) return
      await page.goto(`/parcels/${parcel.id}`)
      await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toBeVisible()
      await expect(page.getByRole('button', { name: 'Edit parcel' })).toHaveCount(canUpdate(role, 'parcels') ? 1 : 0)
      await openParcels(page)
      await expect(page.getByRole('button', { name: 'New parcel' })).toHaveCount(canCreate(role) ? 1 : 0)
    })
  }
})
