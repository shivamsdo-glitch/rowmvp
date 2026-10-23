import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { canUpdate, devToken, level, newId, ROLES, sessionToken, signInViaUi , sectionVisible } from './support'

/**
 * Phase 1.5 slice 7b (ADR 0019): a parcel's Questionnaire tab. Real browser, UI, API and Postgres.
 * Case ids match vault/acceptance/RelocationAgent.md and Everyone.md. Every test makes its own parcel.
 */

const canCreate = (role: string, page: string) => ['S', 'P'].includes(level(role, page) ?? '')
const canDelete = (role: string, page: string) => level(role, page) === 'P'
const unique = (label: string) => `${label} ${newId().slice(0, 8)}`

async function adminHeaders(request: APIRequestContext) {
  return { Authorization: `Bearer ${await devToken(request, 'Admin')}` }
}

async function makeParcel(request: APIRequestContext) {
  const headers = await adminHeaders(request)
  const project = await (await request.post('/api/v1/projects', { headers, data: { name: unique('Questionnaire scene'), oversightType: 'Local', phase: 'Phase 1' } })).json()
  return (await (await request.post('/api/v1/parcels', { headers, data: { projectId: project.id, areaSqFt: 1000, acquisitionType: 'Standard', address: 'Questionnaire Rd' } })).json()) as { id: string; number: string }
}

async function seed(request: APIRequestContext, parcelId: string) {
  const res = await request.post(`/api/v1/parcels/${parcelId}/questionnaires`, { headers: await adminHeaders(request), data: { relocationType: 'Residential', questions: [{ question: 'Do you rent?' }] } })
  expect(res.status()).toBe(201)
  return (await res.json()) as { id: string; number: string }
}

async function openTab(page: Page, parcelId: string) {
  await page.goto(`/parcels/${parcelId}`)
  await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toBeVisible()
  await page.getByRole('tab', { name: 'Relocation' }).click()
}

test.describe('Relocation Agent · questionnaire', () => {
  test('AT-RA-40 adds a questionnaire with interests and questions, attaches a file, changes it and removes it', async ({ page, request }) => {
    const parcel = await makeParcel(request)
    await signInViaUi(page, 'RelocationAgent')
    await openTab(page, parcel.id)
    const form = page.getByRole('form', { name: 'Add a questionnaire' })

    await form.getByRole('button', { name: 'Add questionnaire' }).click()
    await expect(form.getByText('Relocation type is required.')).toBeVisible()
    await form.getByLabel(/^Relocation type/).selectOption('Residential')
    await form.getByLabel('Due by').fill('2026-08-01')

    await form.getByRole('button', { name: 'Add ownership interest' }).click()
    await expect(form.getByText('Enter the name of the owner or interest holder.')).toBeVisible()
    await form.getByLabel('Interest holder name').fill('Kamali')
    await form.getByLabel('Interest holder email').fill('k@example.com')
    await form.getByRole('button', { name: 'Add ownership interest' }).click()

    await form.getByRole('button', { name: 'Add question', exact: true }).click()
    await expect(form.getByText('Enter the question.')).toBeVisible()
    await form.getByLabel('Question', { exact: true }).fill('Do you rent?')
    await form.getByLabel('Answer', { exact: true }).fill('No')
    await form.getByRole('button', { name: 'Add question', exact: true }).click()
    await form.getByRole('button', { name: 'Add questionnaire' }).click()

    const table = page.getByRole('table', { name: 'Questionnaires' })
    await expect(table).toContainText(`QST-${parcel.number}-001`)
    await expect(table).toContainText('Residential')

    await page.getByRole('button', { name: `Open questionnaire QST-${parcel.number}-001` }).click()
    const panel = page.getByRole('region', { name: `Attachments for QST-${parcel.number}-001` })
    await panel.getByLabel('Upload document').setInputFiles({ name: 'answers.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4 test') })
    await expect(panel.getByText('answers.pdf')).toBeVisible()

    const change = page.getByRole('form', { name: 'Change questionnaire' })
    await change.getByLabel(/^Relocation type/).selectOption('Non-residential')
    await change.getByRole('button', { name: 'Remove question 1' }).click()
    await change.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByRole('form', { name: 'Add a questionnaire' })).toBeVisible()
    await expect(table).toContainText('Non-residential')

    const rows = (await (await request.get(`/api/v1/parcels/${parcel.id}/questionnaires`, { headers: await adminHeaders(request) })).json()) as { relocationType: string; questions: unknown[]; interests: unknown[] }[]
    expect(rows).toHaveLength(1)
    expect(rows[0].relocationType).toBe('Non-residential')
    expect(rows[0].questions).toHaveLength(0)
    expect(rows[0].interests).toHaveLength(1)

    await page.getByRole('button', { name: `Remove questionnaire QST-${parcel.number}-001` }).click()
    await expect(page.getByText('No questionnaires yet')).toBeVisible()
  })
})

test.describe('Everyone · questionnaire', () => {
  for (const role of ROLES) {
    test(`AT-X-53 ${role}: the Questionnaire tab, its buttons and the API follow the access spec`, async ({ page, request }) => {
      const parcel = await makeParcel(request)
      const keep = await seed(request, parcel.id)
      const doomed = await seed(request, parcel.id)

      await signInViaUi(page, role)
      await page.goto(`/parcels/${parcel.id}`)
      await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toBeVisible()

      const readable = level(role, 'parcelquestionnaire') !== null
      expect(await sectionVisible(page, 'Questionnaire'), `${role} section`).toBe(readable)
      if (readable) {
        await page.getByRole('tab', { name: 'Relocation' }).click()
        await expect(page.getByRole('table', { name: 'Questionnaires' })).toBeVisible()
        await expect(page.getByRole('form', { name: 'Add a questionnaire' }), `${role} add`).toHaveCount(canCreate(role, 'parcelquestionnaire') ? 1 : 0)
        await expect(page.getByRole('button', { name: `Open questionnaire ${keep.number}` }), `${role} edit`).toHaveCount(1)
        await expect(page.getByRole('button', { name: `Remove questionnaire ${keep.number}` }), `${role} remove`).toHaveCount(canDelete(role, 'parcelquestionnaire') ? 1 : 0)
      }

      const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
      const url = `/api/v1/parcels/${parcel.id}/questionnaires`
      const want = (ok: boolean, status: number) => (ok ? status : 403)
      expect((await request.get(url, { headers })).status()).toBe(want(readable, 200))
      expect((await request.post(url, { headers, data: { relocationType: 'Residential' } })).status()).toBe(want(canCreate(role, 'parcelquestionnaire'), 201))
      expect((await request.put(`${url}/${keep.id}`, { headers, data: { relocationType: 'Non-residential', dueByDate: '2026-09-01' } })).status()).toBe(want(canUpdate(role, 'parcelquestionnaire'), 200))
      expect((await request.delete(`${url}/${doomed.id}`, { headers })).status()).toBe(want(canDelete(role, 'parcelquestionnaire'), 204))
    })
  }
})
