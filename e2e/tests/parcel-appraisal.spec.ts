import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { canUpdate, devToken, level, newId, ROLES, sessionToken, signInViaUi } from './support'

/**
 * Phase 1.5 slice 6c (ADR 0018): a parcel's Appraisal tab. Real browser, UI, API and Postgres.
 * Case ids match vault/acceptance/Appraiser.md, AppraisalApprover.md and Everyone.md. Every test makes its own parcel.
 */

const canCreate = (role: string, page: string) => ['S', 'P'].includes(level(role, page) ?? '')
const canDelete = (role: string, page: string) => level(role, page) === 'P'
const unique = (label: string) => `${label} ${newId().slice(0, 8)}`

type AppraisalRow = { id: string; number: string; approvedJustCompensation: number | null; appraisalTotal: number; assignedTo: string | null }

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
  const project = await (await request.post('/api/v1/projects', { headers, data: { name: unique('Appraisal scene'), oversightType: 'Local', phase: 'Phase 1' } })).json()
  return (await (await request.post('/api/v1/parcels', { headers, data: { projectId: project.id, areaSqFt: 1000, acquisitionType: 'Standard', address: 'Appraisal Rd' } })).json()) as { id: string; number: string }
}

async function seedAppraisal(request: APIRequestContext, parcelId: string, extra: Record<string, unknown> = {}) {
  const res = await request.post(`/api/v1/parcels/${parcelId}/appraisals`, {
    headers: await adminHeaders(request),
    data: { appraisalType: 'Full Appraisal', amounts: [{ amountType: 'Land', amount: 1500 }], ...extra },
  })
  expect(res.status()).toBe(201)
  return (await res.json()) as AppraisalRow
}

async function openTab(page: Page, parcelId: string) {
  await page.goto(`/parcels/${parcelId}`)
  await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toBeVisible()
  await page.getByRole('tab', { name: 'Appraisal' }).click()
}

test.describe('Appraiser · appraisals', () => {
  test('AT-AP-40 adds an appraisal with amounts, attaches the report, changes it and removes it', async ({ page, request }) => {
    await ensureLibraryValue(request, 'appraisal_type', 'Full Appraisal')
    await ensureLibraryValue(request, 'appraisal_amount_type', 'Land')
    await ensureLibraryValue(request, 'appraisal_amount_type', 'Damages')
    const parcel = await makeParcel(request)
    await signInViaUi(page, 'Appraiser')
    await openTab(page, parcel.id)
    const form = page.getByRole('form', { name: 'Add an appraisal' })

    await form.getByLabel(/^Appraisal type/).selectOption('Full Appraisal')
    await form.getByLabel(/^Appraisal requested date/).fill('2026-03-01')
    await form.getByLabel(/^Appraisal received date$/).fill('2026-02-01')
    await form.getByRole('button', { name: 'Add amount' }).click()
    await expect(form.getByText('Choose the amount type.')).toBeVisible()
    await form.getByLabel(/^Amount type/).selectOption('Land')
    await form.getByLabel(/^Amount$/).fill('100000')
    await form.getByRole('button', { name: 'Add amount' }).click()
    await form.getByLabel(/^Amount type/).selectOption('Damages')
    await form.getByLabel(/^Amount$/).fill('2500.25')
    await form.getByRole('button', { name: 'Add amount' }).click()
    await expect(form.getByText('$102,500.25')).toBeVisible()

    await form.getByRole('button', { name: 'Add appraisal' }).click()
    await expect(form.getByText('The appraisal cannot be received before it is requested.')).toBeVisible()
    await form.getByLabel(/^Appraisal received date$/).fill('2026-03-10')
    await form.getByRole('button', { name: 'Add appraisal' }).click()

    const table = page.getByRole('table', { name: 'Appraisals' })
    await expect(table).toContainText(`APR-${parcel.number}-001`)
    await expect(table).toContainText('$102,500.25')

    await page.getByRole('button', { name: `Open appraisal APR-${parcel.number}-001` }).click()
    const panel = page.getByRole('region', { name: `Attachments for APR-${parcel.number}-001` })
    await panel.getByLabel('Upload document').setInputFiles({ name: 'report.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4 test') })
    await expect(panel.getByText('report.pdf')).toBeVisible()

    const change = page.getByRole('form', { name: 'Change appraisal' })
    await change.getByLabel(/^Appraisal due date/).fill('2026-04-01')
    await change.getByRole('checkbox', { name: 'FTA concurrence needed' }).check()
    await change.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByRole('form', { name: 'Add an appraisal' })).toBeVisible()

    const rows = (await (await request.get(`/api/v1/parcels/${parcel.id}/appraisals`, { headers: await adminHeaders(request) })).json()) as { dueDate: string; ftaConcurrenceNeeded: boolean }[]
    expect(rows).toHaveLength(1)
    expect(rows[0]).toMatchObject({ dueDate: '2026-04-01', ftaConcurrenceNeeded: true })

    await page.getByRole('button', { name: `Remove appraisal APR-${parcel.number}-001` }).click()
    await expect(page.getByText('No appraisals yet')).toBeVisible()
  })
})

test.describe('Appraisal Approver · review', () => {
  test('AT-AA-40 the approver sets the approved just compensation', async ({ page, request }) => {
    const parcel = await makeParcel(request)
    const seed = await seedAppraisal(request, parcel.id)

    await signInViaUi(page, 'AppraisalApprover')
    await openTab(page, parcel.id)
    await page.getByRole('button', { name: `Open appraisal ${seed.number}` }).click()
    const form = page.getByRole('form', { name: 'Change appraisal' })
    await form.getByLabel(/^Approved just compensation/).fill('-5')
    await form.getByRole('button', { name: 'Save changes' }).click()
    await expect(form.getByText('The amount must be zero or more.')).toBeVisible()
    await form.getByLabel(/^Approved just compensation/).fill('1400')
    await form.getByLabel(/^Review completed date/).fill('2026-05-01')
    await form.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByRole('table', { name: 'Appraisals' })).toContainText('$1,400.00')
  })

  test('AT-AA-40 the appraiser sees the review read-only and cannot change it, but can still save the appraisal', async ({ page, request }) => {
    const parcel = await makeParcel(request)
    const seed = await seedAppraisal(request, parcel.id, { approvedJustCompensation: 1400, reviewCompletedDate: '2026-05-01' })

    await signInViaUi(page, 'Appraiser')
    await openTab(page, parcel.id)
    await page.getByRole('button', { name: `Open appraisal ${seed.number}` }).click()
    const form = page.getByRole('form', { name: 'Change appraisal' })
    await expect(form.getByLabel(/^Approved just compensation/)).toBeDisabled()
    await expect(form.getByLabel(/^Approved just compensation/)).toHaveValue('1400')

    const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
    const url = `/api/v1/parcels/${parcel.id}/appraisals/${seed.id}`
    expect((await request.put(url, { headers, data: { appraisalType: 'Full Appraisal', amounts: [], approvedJustCompensation: 1, reviewCompletedDate: '2026-05-01' } })).status()).toBe(403)
    const same = { appraisalType: 'Full Appraisal', amounts: [], approvedJustCompensation: 1400, reviewCompletedDate: '2026-05-01', assignedTo: 'Dana' }
    expect((await request.put(url, { headers, data: same })).status()).toBe(200)
    const after = (await (await request.get(`/api/v1/parcels/${parcel.id}/appraisals`, { headers })).json()) as AppraisalRow[]
    expect(after[0]).toMatchObject({ approvedJustCompensation: 1400, assignedTo: 'Dana' })
  })
})

test.describe('Everyone · appraisals', () => {
  for (const role of ROLES) {
    test(`AT-X-51 ${role}: the Appraisal tab, its buttons and the API follow the access spec`, async ({ page, request }) => {
      const parcel = await makeParcel(request)
      const seed = await seedAppraisal(request, parcel.id)
      const doomed = await seedAppraisal(request, parcel.id)

      await signInViaUi(page, role)
      await page.goto(`/parcels/${parcel.id}`)
      await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toBeVisible()

      const readable = level(role, 'parcelappraisal') !== null
      await expect(page.getByRole('tab', { name: 'Appraisal' }), `${role} tab`).toHaveCount(readable ? 1 : 0)
      if (readable) {
        await page.getByRole('tab', { name: 'Appraisal' }).click()
        await expect(page.getByRole('table', { name: 'Appraisals' })).toBeVisible()
        await expect(page.getByRole('form', { name: 'Add an appraisal' }), `${role} add`).toHaveCount(canCreate(role, 'parcelappraisal') ? 1 : 0)
        await expect(page.getByRole('button', { name: `Open appraisal ${seed.number}` }), `${role} edit`).toHaveCount(1)
        await expect(page.getByRole('button', { name: `Remove appraisal ${seed.number}` }), `${role} remove`).toHaveCount(canDelete(role, 'parcelappraisal') ? 1 : 0)
      }

      const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
      const url = `/api/v1/parcels/${parcel.id}/appraisals`
      const want = (ok: boolean, status: number) => (ok ? status : 403)
      expect((await request.get(url, { headers })).status()).toBe(want(readable, 200))
      expect((await request.post(url, { headers, data: { appraisalType: 'Review', amounts: [] } })).status()).toBe(want(canCreate(role, 'parcelappraisal'), 201))
      expect((await request.put(`${url}/${seed.id}`, { headers, data: { appraisalType: 'Full Appraisal', amounts: [], assignedTo: `by ${role}` } })).status()).toBe(want(canUpdate(role, 'parcelappraisal'), 200))
      expect((await request.delete(`${url}/${doomed.id}`, { headers })).status()).toBe(want(canDelete(role, 'parcelappraisal'), 204))
    })
  }
})
