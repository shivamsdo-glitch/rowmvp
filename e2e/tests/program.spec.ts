import { expect, test, type APIRequestContext } from '@playwright/test'
import { devToken, newId, ROLES, sessionToken, signInViaUi } from './support'

/**
 * ADR 0024: the ROW Director's view. Where every project stands, what is going on with each parcel, and which team the time goes to.
 * Real browser, UI, API and Postgres. Case ids match vault/acceptance/Everyone.md and ProjectManager.md (AT-X-61, AT-X-62).
 */

const MANAGERS = ['ProjectManager', 'Admin']
const exact = { exact: true } as const
const whole = (text: string) => new RegExp(String.raw`\b${text}\b`)
const unique = (label: string) => `${label} ${newId().slice(0, 8)}`
const auth = async (request: APIRequestContext, role: string) => ({ Authorization: `Bearer ${await devToken(request, role)}` })

/** A project that is past its ROW clear date and over its estimate, with parcels on different steps and one closed. */
async function scene(request: APIRequestContext) {
  const headers = await auth(request, 'Admin')
  const clear = new Date(Date.now() - 10 * 86_400_000).toISOString().slice(0, 10)
  const project = await (await request.post('/api/v1/projects', { headers, data: { name: unique('Program scene'), oversightType: 'Local', phase: 'Phase 1', rightOfWayClearDate: clear } })).json()
  expect((await request.post(`/api/v1/projects/${project.id}/cost-estimates`, { headers, data: { landCost: 50000, improvementsCost: 0, relocationCost: 0, indirectCost: 0 } })).status()).toBe(201)
  const parcel = async () => (await (await request.post('/api/v1/parcels', { headers, data: { projectId: project.id, areaSqFt: 1000, acquisitionType: 'Standard', address: `${newId().slice(0, 5)} Program Rd` } })).json()) as { id: string; number: string }
  const a = await parcel()
  const b = await parcel()
  const c = await parcel()
  for (const step of ['title_survey', 'appraisal']) expect((await request.post(`/api/v1/parcels/${a.id}/advance`, { headers, data: { targetStepKey: step } })).status()).toBe(200)
  expect((await request.post(`/api/v1/parcels/${b.id}/advance`, { headers, data: { targetStepKey: 'title_survey' } })).status()).toBe(200)
  expect((await request.post(`/api/v1/parcels/${a.id}/appraisals`, { headers, data: { appraisalType: 'Full Appraisal', appraiser: 'Ann', amounts: [{ amountType: 'Land', amount: 100000 }], approvedJustCompensation: 90000, reviewCompletedDate: '2026-02-25', reviewSubmittedDate: '2026-02-26' } })).status()).toBe(201)
  expect((await request.put(`/api/v1/parcels/${a.id}/negotiation`, { headers, data: { assignedTo: 'Casey', totalAgreementAmount: 95000 } })).status()).toBe(200)
  expect((await request.put(`/api/v1/parcels/${a.id}/title-closing`, { headers, data: { titleCompany: 'Lone Star', anticipatedClosingDate: '2026-05-01', actualClosingDate: '2026-05-02' } })).status()).toBe(200)
  return { project, a, b, c }
}

for (const role of ROLES) {
  test(`AT-X-61 ${role} ${MANAGERS.includes(role) ? 'lands on the Program view' : 'has no Program view, and the server refuses them'}`, async ({ page, request }) => {
    await signInViaUi(page, role)
    const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
    const program = page.getByRole('tab', { name: 'Program', ...exact })

    if (MANAGERS.includes(role)) {
      await expect(program).toHaveAttribute('aria-selected', 'true')
      await expect(page.getByTestId('tile-Projects')).toBeVisible()
      await expect(page.getByTestId('tile-Active parcels')).toBeVisible()
      await expect(page.getByRole('table', { name: 'Projects' })).toBeVisible()
      await expect(page.getByRole('list', { name: 'Average days a parcel stays with each team' })).toBeVisible()
      await expect(page.getByRole('table', { name: 'Time in each workflow step' })).toBeVisible()
    } else {
      await expect(program).toHaveCount(0)
      await expect(page.getByTestId('tile-Active parcels')).toHaveCount(0)
      await expect(page.getByRole('table', { name: 'Projects' })).toHaveCount(0)
    }
    expect((await request.get('/api/v1/dashboard/program', { headers })).status()).toBe(MANAGERS.includes(role) ? 200 : 403)
  })
}

test.describe('Program view · where ROW stands', () => {
  test('AT-X-61 a project shows its health and why, how much is closed, where the parcels are and its money', async ({ page, request }) => {
    const { project } = await scene(request)
    await signInViaUi(page, 'ProjectManager')

    const row = page.getByRole('table', { name: 'Projects' }).getByRole('row', { name: whole(project.number) })
    await expect(row).toBeVisible()
    await expect(row).toContainText('Off track')
    await expect(row).toContainText('has passed with 2 parcels not closed')
    await expect(row.getByRole('img', { name: '33% closed' })).toBeVisible()
    await expect(row.getByRole('img', { name: /Parcels by step: Parcel set up 1, Title & survey 1, Appraisal & just compensation 1/ })).toBeVisible()
    await expect(row).toContainText('$50,000')
    await expect(row).toContainText('$95,000')
    await expect(row).toContainText('10 days ago')
  })

  test('AT-X-62 opening a project lists every parcel with its step, owner, money, closing and what needs attention', async ({ page, request }) => {
    const { project, a, b, c } = await scene(request)
    await signInViaUi(page, 'ProjectManager')

    await page.getByRole('button', { name: `Open project ${project.number}` }).click()
    await expect(page.getByRole('heading', { name: whole(project.number) })).toBeVisible()
    await expect(page.getByRole('list', { name: 'Why this project needs attention' })).toContainText('over the estimate')
    await expect(page.getByTestId('tile-Closed')).toContainText('33%')
    await expect(page.getByTestId('tile-Known cost')).toContainText('$95K')
    await expect(page.getByTestId('step-setup')).toHaveText('1')
    await expect(page.getByTestId('step-title_survey')).toHaveText('1')
    await expect(page.getByTestId('step-appraisal')).toHaveText('1')

    const parcels = page.getByRole('table', { name: 'Parcels in this project' })
    await expect(parcels.getByRole('row')).toHaveCount(4)
    const closed = parcels.getByRole('row').filter({ has: page.getByRole('link', { name: `Open parcel ${a.number}`, ...exact }) })
    await expect(closed).toContainText('Appraisal & just compensation')
    await expect(closed).toContainText('$100,000')
    await expect(closed).toContainText('$90,000')
    await expect(closed).toContainText('$95,000')
    await expect(closed).toContainText('Closed May 2, 2026')
    await expect(parcels.getByRole('row').filter({ has: page.getByRole('link', { name: `Open parcel ${b.number}`, ...exact }) })).toContainText('Title & survey')
    await expect(parcels.getByRole('row').filter({ has: page.getByRole('link', { name: `Open parcel ${c.number}`, ...exact }) })).toContainText('Parcel set up')

    await parcels.getByRole('link', { name: `Open parcel ${b.number}`, ...exact }).click()
    await expect(page).toHaveURL(new RegExp(`/parcels/${b.id}`))
  })

  test('AT-X-62 the project page goes back to all projects, and refresh keeps the view', async ({ page, request }) => {
    const { project } = await scene(request)
    await signInViaUi(page, 'Admin')

    await page.getByRole('button', { name: `Open project ${project.number}` }).click()
    await expect(page.getByRole('table', { name: 'Parcels in this project' })).toBeVisible()
    await page.getByRole('button', { name: /Refresh/ }).click()
    await expect(page.getByRole('table', { name: 'Parcels in this project' })).toBeVisible()
    await page.getByRole('button', { name: /All projects/ }).click()
    await expect(page.getByRole('table', { name: 'Projects' })).toBeVisible()
  })
})

test.describe('Program view · which teams take the time', () => {
  test('AT-X-61 time is measured from the real moves and ranked by team, longest first', async ({ page, request }) => {
    await scene(request)
    await signInViaUi(page, 'Admin')

    const bars = page.getByRole('list', { name: 'Average days a parcel stays with each team' })
    await expect(bars.getByTestId('team-Title & Survey')).toBeVisible()
    await expect(bars.getByTestId('team-Appraisal')).toBeVisible()
    const stages = page.getByRole('table', { name: 'Time in each workflow step' })
    await expect(stages.getByRole('row', { name: /Title & survey/ })).toContainText('Title & Survey')
    await expect(stages.getByRole('row', { name: /Negotiation/ }).first()).toContainText('Negotiation')

    const api = await request.get('/api/v1/dashboard/program', { headers: { Authorization: `Bearer ${await sessionToken(page)}` } })
    const body = await api.json()
    const averages = body.teams.map((t: { avgDaysPerStay: number | null }) => t.avgDaysPerStay ?? 0)
    expect(averages).toEqual([...averages].sort((x: number, y: number) => y - x))
    await expect(bars.getByRole('listitem').first()).toHaveAttribute('data-testid', `team-${body.teams[0].team}`)
  })

  test('AT-X-61 the Program view and a project page are for managers only, even when the address is known', async ({ request }) => {
    const { project } = await scene(request)
    for (const role of ROLES) {
      const headers = await auth(request, role)
      const allowed = MANAGERS.includes(role)
      expect((await request.get('/api/v1/dashboard/program', { headers })).status(), role).toBe(allowed ? 200 : 403)
      expect((await request.get(`/api/v1/dashboard/projects/${project.id}`, { headers })).status(), role).toBe(allowed ? 200 : 403)
    }
    expect((await request.get('/api/v1/dashboard/program')).status()).toBe(401)
  })
})
