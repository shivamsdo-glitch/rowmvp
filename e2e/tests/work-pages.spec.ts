import { expect, test, type APIRequestContext } from '@playwright/test'
import { devToken, level, newId, ROLES, sessionToken, signInViaUi } from './support'

/**
 * Phase 1.5 (ADR 0021): the Title & Survey, Appraisal and Negotiation & Closing screens. Find a record by parcel, owner or ID, open its parcel
 * and add or update it there. Real browser, UI, API and Postgres. Case ids match vault/acceptance/Appraiser.md, Negotiator.md, TitleExaminer.md and Everyone.md.
 */

const unique = (label: string) => `${label} ${newId().slice(0, 8)}`
const exact = { exact: true } as const

async function adminHeaders(request: APIRequestContext) {
  return { Authorization: `Bearer ${await devToken(request, 'Admin')}` }
}

/** A parcel with an owner whose name is unique to this test, so a search for the owner finds only this scene. */
async function scene(request: APIRequestContext) {
  const headers = await adminHeaders(request)
  const project = await (await request.post('/api/v1/projects', { headers, data: { name: unique('Work scene'), oversightType: 'Local', phase: 'Phase 1' } })).json()
  const make = async () => {
    const address = `${newId().slice(0, 8)} Work Rd`
    return { ...((await (await request.post('/api/v1/parcels', { headers, data: { projectId: project.id, areaSqFt: 1000, acquisitionType: 'Standard', address } })).json()) as { id: string; number: string }), address }
  }
  const parcel = await make()
  const bare = await make()
  const owner = `Zebulon${newId().replace(/-/g, '').slice(0, 8)}`
  expect((await request.post(`/api/v1/parcels/${parcel.id}/ownerships`, { headers, data: { ownerName: owner, ownerType: 'Fee Owner' } })).status()).toBe(201)
  return { parcel, bare, owner, headers }
}

test.describe('Appraiser · appraisal screen', () => {
  test('AT-AP-41 finds an appraisal by its owner, updates it, then starts one for a parcel found by number', async ({ page, request }) => {
    const { parcel, bare, owner, headers } = await scene(request)
    const seed = await (await request.post(`/api/v1/parcels/${parcel.id}/appraisals`, { headers, data: { appraisalType: 'Full', appraiser: 'Ann', amounts: [{ amountType: 'Land', amount: 1500 }] } })).json()

    await signInViaUi(page, 'Appraiser')
    await page.getByRole('link', { name: 'Appraisal', ...exact }).click()
    await expect(page.getByRole('heading', { name: 'Appraisal', ...exact })).toBeVisible()
    await expect(page.getByRole('link', { name: 'Appraisal', ...exact })).not.toContainText('Soon')

    await page.getByLabel('Search', exact).fill(owner)
    const found = page.getByRole('table', { name: 'Appraisals found' })
    await expect(found).toContainText(seed.number)
    await expect(found).toContainText(owner)
    await expect(found.getByRole('row')).toHaveCount(2)

    await found.getByRole('button', { name: `Open ${seed.number}` }).click()
    const panel = page.getByRole('region', { name: 'Selected parcel' })
    await expect(panel).toContainText(`Parcel ${parcel.number}`)
    await panel.getByRole('button', { name: `Edit appraisal ${seed.number}` }).click()
    const form = panel.getByRole('form', { name: 'Change appraisal' })
    await form.getByLabel('Appraisal due date', exact).fill('2026-09-01')
    await form.getByRole('button', { name: 'Save changes' }).click()
    await expect(panel.getByRole('form', { name: 'Add an appraisal' })).toBeVisible()
    const rows = (await (await request.get(`/api/v1/parcels/${parcel.id}/appraisals`, { headers })).json()) as { dueDate: string }[]
    expect(rows[0].dueDate).toBe('2026-09-01')

    await panel.getByRole('button', { name: 'Close parcel' }).click()
    await page.getByLabel('Search', exact).fill(bare.address)
    await page.getByRole('button', { name: `Open parcel ${bare.number}`, ...exact }).click()
    const second = page.getByRole('region', { name: 'Selected parcel' })
    await second.getByRole('form', { name: 'Add an appraisal' }).getByRole('button', { name: 'Add appraisal' }).click()
    await expect(second.getByRole('table', { name: 'Appraisals' })).toContainText(`APR-${bare.number}-001`)
    const created = (await (await request.get(`/api/v1/parcels/${bare.id}/appraisals`, { headers })).json()) as unknown[]
    expect(created).toHaveLength(1)
  })
})

test.describe('Negotiator · negotiation and closing screen', () => {
  test('AT-NE-44 finds a negotiation by its owner, adds a note to it and switches to the closing records', async ({ page, request }) => {
    const { parcel, owner, headers } = await scene(request)
    expect((await request.put(`/api/v1/parcels/${parcel.id}/negotiation`, { headers, data: { assignedTo: 'Casey' } })).status()).toBe(200)
    expect((await request.put(`/api/v1/parcels/${parcel.id}/title-closing`, { headers, data: { titleCompany: 'Lone Star Title' } })).status()).toBe(200)

    await signInViaUi(page, 'Negotiator')
    await page.getByRole('link', { name: 'Negotiation & Closing', ...exact }).click()
    await page.getByLabel('Search', exact).fill(owner)
    const found = page.getByRole('table', { name: 'Negotiations found' })
    await expect(found.getByRole('row')).toHaveCount(2)
    await expect(found).toContainText(`NEG-${parcel.number}`)
    await found.getByRole('button', { name: `Open NEG-${parcel.number}` }).click()
    const form = page.getByRole('form', { name: 'Negotiation' })
    await form.getByLabel('Notes', exact).fill('Spoke to the owner.')
    await form.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByText('Negotiation saved.')).toBeVisible()
    const got = (await (await request.get(`/api/v1/parcels/${parcel.id}/negotiation`, { headers })).json()) as { record: { notes: string } }
    expect(got.record.notes).toBe('Spoke to the owner.')

    await page.getByRole('tab', { name: 'Title and closing', ...exact }).click()
    const closings = page.getByRole('table', { name: 'Title and closing found' })
    await expect(closings).toContainText('Lone Star Title')
    await closings.getByRole('button', { name: `Open TC-${parcel.number}` }).click()
    await expect(page.getByRole('form', { name: 'Title and closing' })).toBeVisible()
  })
})

test.describe('Title Researcher · title and survey screen', () => {
  test('AT-TE-42 finds encumbrances and easements by owner and adds an encumbrance from the screen', async ({ page, request }) => {
    const { parcel, owner, headers } = await scene(request)
    expect((await request.post(`/api/v1/parcels/${parcel.id}/encumbrances`, { headers, data: { encumbranceType: 'Lien', documentTitle: 'Deed of Trust', grantor: 'A', grantee: 'B' } })).status()).toBe(201)
    expect((await request.post(`/api/v1/parcels/${parcel.id}/easements`, { headers, data: { number: 'E-5', easementType: 'Drainage Easement' } })).status()).toBe(201)

    await signInViaUi(page, 'TitleResearcher')
    await page.getByRole('link', { name: 'Title & Survey', ...exact }).click()
    await page.getByLabel('Search', exact).fill(owner)
    const found = page.getByRole('table', { name: 'Title encumbrances found' })
    await expect(found.getByRole('row')).toHaveCount(2)
    await expect(found).toContainText('Deed of Trust')

    await page.getByRole('tab', { name: 'Easements', ...exact }).click()
    const easements = page.getByRole('table', { name: 'Easements found' })
    await expect(easements.getByRole('row')).toHaveCount(2)
    await expect(easements).toContainText('E-5')

    await page.getByRole('tab', { name: 'Title encumbrances', ...exact }).click()
    await found.getByRole('button', { name: `Open ENC-${parcel.number}-001` }).click()
    const panel = page.getByRole('region', { name: 'Selected parcel' })
    const form = panel.getByRole('form', { name: 'Add an encumbrance' })
    await form.getByLabel(/^Encumbrance type/).selectOption('Judgment')
    await form.getByLabel(/^Document title/).fill('Judgment 2024')
    await form.getByLabel(/^Grantor/).fill('C')
    await form.getByLabel(/^Grantee/).fill('D')
    await form.getByRole('button', { name: 'Add encumbrance' }).click()
    await expect(panel.getByRole('table', { name: 'Title encumbrances' })).toContainText('Judgment 2024')
    const rows = (await (await request.get(`/api/v1/parcels/${parcel.id}/encumbrances`, { headers })).json()) as unknown[]
    expect(rows).toHaveLength(2)
  })
})

test.describe('Everyone · work screens', () => {
  for (const role of ROLES) {
    test(`AT-X-57 ${role}: the three menu items, their screens and their searches follow the access spec`, async ({ page, request }) => {
      await signInViaUi(page, role)
      const items: [string, string, string][] = [['Title & Survey', 'title', '/title'], ['Appraisal', 'appraisals', '/appraisals'], ['Negotiation & Closing', 'negotiations', '/negotiations']]
      for (const [label, pageKey] of items) {
        await expect(page.getByRole('link', { name: label, ...exact }), `${role} ${label} menu`).toHaveCount(level(role, pageKey) !== null ? 1 : 0)
      }
      if (level(role, 'appraisals') !== null) {
        await page.getByRole('link', { name: 'Appraisal', ...exact }).click()
        await expect(page.getByRole('heading', { name: 'Appraisal', ...exact })).toBeVisible()
        await expect(page.getByLabel('Search', exact)).toBeVisible()
      }

      const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
      const searches: [string, string][] = [['appraisals', 'parcelappraisal'], ['negotiations', 'parcelnegotiation'], ['title-closings', 'titleclosing'], ['encumbrances', 'titleencumbrances'], ['easements', 'easements']]
      for (const [url, pageKey] of searches)
        expect((await request.get(`/api/v1/overview/${url}`, { headers })).status(), `${role} ${url}`).toBe(level(role, pageKey) !== null ? 200 : 403)
    })
  }
})
