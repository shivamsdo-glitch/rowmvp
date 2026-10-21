import { expect, test, type APIRequestContext } from '@playwright/test'
import { devToken, ROLES, sessionToken, signInViaUi } from './support'

/**
 * ADR 0025: an administrator sets what the Program view judges by (the stuck limit, the health thresholds, and which team owns each step).
 * The settings are agency-wide, so these scenarios run alone, after every other spec (see playwright.config.ts).
 * Case ids match vault/acceptance/Everyone.md and Administrator.md (AT-X-63).
 */

const exact = { exact: true } as const
const auth = async (request: APIRequestContext, role: string) => ({ Authorization: `Bearer ${await devToken(request, role)}` })
const form = (page: import('@playwright/test').Page) => page.getByRole('form', { name: 'Program view settings' })

// One agency-wide setting, so this file's tests run one after another (not across workers), each leaving the defaults behind.
test.describe.configure({ mode: 'serial' })

test.afterEach(async ({ request }) => {
  await request.delete('/api/v1/dashboard/settings', { headers: await auth(request, 'Admin') }) // leave the agency on the defaults
})

test.describe('Administrator · Program view settings', () => {
  test('AT-X-63 the Configuration page shows the defaults: thresholds and every step with its team', async ({ page }) => {
    await signInViaUi(page, 'Admin')
    await page.goto('/administration')

    const f = form(page)
    await expect(f.getByLabel('Days on one step before a parcel is stuck')).toHaveValue('30')
    await expect(f.getByLabel('Warn this many days before the ROW clear date')).toHaveValue('90')
    await expect(f.getByLabel('Percent closed expected inside that window')).toHaveValue('75')
    await expect(f.getByLabel(/Overdue parcels before a project is "Off track"/)).toHaveValue('3')
    await expect(f.getByLabel(/Share of the project overdue/)).toHaveValue('25')
    const table = f.getByRole('table', { name: 'Team for each workflow step' })
    await expect(table.getByRole('row')).toHaveCount(11) // header + the standard steps + early approval, donation review, conveyance terms
    await expect(f.getByLabel('Team for Appraisal & just compensation')).toHaveValue('Appraisal')
    await expect(f.getByLabel('Team for Title & survey')).toHaveValue('Title & Survey')
  })

  test('AT-X-63 changing a step’s team and the thresholds takes effect on the Program view, and Reset puts them back', async ({ page, request }) => {
    await signInViaUi(page, 'Admin')
    await page.goto('/administration')
    const f = form(page)

    await f.getByLabel('Days on one step before a parcel is stuck').fill('45')
    await f.getByLabel('Warn this many days before the ROW clear date').fill('60')
    await f.getByLabel('Team for Appraisal & just compensation').fill('Valuation')
    await f.getByRole('button', { name: 'Save program settings' }).click()
    await expect(page.getByText('Saved. The Program view now uses these settings.')).toBeVisible()

    const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
    const saved = await (await request.get('/api/v1/dashboard/settings', { headers })).json()
    expect(saved.rules).toMatchObject({ stuckDays: 45, clearDateWarningDays: 60 })
    expect(saved.steps.find((s: { stepKey: string }) => s.stepKey === 'appraisal')).toMatchObject({ team: 'Valuation', defaultTeam: 'Appraisal' })

    await page.reload()
    await expect(form(page).getByLabel('Days on one step before a parcel is stuck')).toHaveValue('45')
    await expect(form(page).getByLabel('Team for Appraisal & just compensation')).toHaveValue('Valuation')

    // The Program view uses it: the table of steps shows the new team and the stuck limit is the new one.
    await page.getByRole('link', { name: 'Home', ...exact }).click()
    const stages = page.getByRole('table', { name: 'Time in each workflow step' })
    await expect(stages.getByRole('row', { name: /Appraisal & just compensation/ })).toContainText('Valuation')
    await expect(page.getByText(/more than 45 days/).first()).toBeVisible()
    const program = await (await request.get('/api/v1/dashboard/program', { headers })).json()
    expect(program.rules.stuckDays).toBe(45)
    expect(program.stages.find((s: { stepKey: string }) => s.stepKey === 'appraisal').team).toBe('Valuation')

    await page.goto('/administration')
    await form(page).getByRole('button', { name: 'Reset to defaults' }).click()
    await expect(page.getByText('Back to the default settings.')).toBeVisible()
    await expect(form(page).getByLabel('Days on one step before a parcel is stuck')).toHaveValue('30')
    await expect(form(page).getByLabel('Team for Appraisal & just compensation')).toHaveValue('Appraisal')
    expect((await (await request.get('/api/v1/dashboard/program', { headers })).json()).rules.stuckDays).toBe(30)
  })

  test('AT-X-63 two steps given one team are pooled, and a bad value is refused on the screen without being saved', async ({ page, request }) => {
    await signInViaUi(page, 'Admin')
    await page.goto('/administration')
    const f = form(page)

    await f.getByLabel('Days on one step before a parcel is stuck').fill('0')
    await f.getByRole('button', { name: 'Save program settings' }).click()
    await expect(f.getByRole('alert')).toContainText('from 1 to 365')
    await f.getByLabel('Days on one step before a parcel is stuck').fill('30')
    await f.getByLabel('Team for Closing').fill('')
    await f.getByRole('button', { name: 'Save program settings' }).click()
    await expect(page.getByText('Give "Closing" a team.')).toBeVisible()

    await f.getByLabel('Team for Closing').fill('Title & Survey')
    await f.getByRole('button', { name: 'Save program settings' }).click()
    await expect(page.getByText('Saved. The Program view now uses these settings.')).toBeVisible()
    const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
    const program = await (await request.get('/api/v1/dashboard/program', { headers })).json()
    expect(program.stages.find((s: { stepKey: string }) => s.stepKey === 'closing').team).toBe('Title & Survey')
    expect(program.teams.map((t: { team: string }) => t.team)).not.toContain('Closing')
  })
})

for (const role of ROLES) {
  test(`AT-X-63 ${role} ${role === 'Admin' ? 'can read and change the program settings' : 'cannot see or change the program settings'}`, async ({ page, request }) => {
    await signInViaUi(page, role)
    const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
    const body = { stuckDays: 30, clearDateWarningDays: 90, clearDateWarningPercentClosed: 75, redOverdueMinimumParcels: 3, redOverduePercent: 25, steps: [] }
    const allowed = role === 'Admin'

    expect((await request.get('/api/v1/dashboard/settings', { headers })).status()).toBe(allowed ? 200 : 403)
    expect((await request.put('/api/v1/dashboard/settings', { headers, data: body })).status()).toBe(allowed ? 200 : 403)
    expect((await request.delete('/api/v1/dashboard/settings', { headers })).status()).toBe(allowed ? 200 : 403)

    await page.goto('/administration')
    if (allowed) await expect(form(page)).toBeVisible()
    else {
      await expect(page.getByText(/don't have access/i)).toBeVisible()
      await expect(form(page)).toHaveCount(0)
    }
  })
}

test('AT-X-63 signed out, the settings are not reachable', async ({ request }) => {
  expect((await request.get('/api/v1/dashboard/settings')).status()).toBe(401)
  expect((await request.put('/api/v1/dashboard/settings', { data: {} })).status()).toBe(401)
})
