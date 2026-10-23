import { expect, test, type APIRequestContext } from '@playwright/test'
import { devToken, level, newId, ROLES, signInViaUi } from './support'

/**
 * Phase 1, slice 3c — the plan-review gate (ROW-239). The setting is agency-wide, so these scenarios turn it on and off for
 * everyone: they run in their own Playwright project, after every other spec has finished (see playwright.config.ts).
 * Case ids match vault/acceptance/Administrator.md and ProjectManager.md.
 */

const unique = (label: string) => `${label} ${newId().slice(0, 8)}`
const today = (add = 0) => new Date(Date.now() + add * 86_400_000).toISOString().slice(0, 10)
type Headers = { Authorization: string }
const as = async (request: APIRequestContext, role: string): Promise<Headers> => ({ Authorization: `Bearer ${await devToken(request, role)}` })

async function parcelAtReview(request: APIRequestContext, headers: Headers) {
  const project = await (await request.post('/api/v1/projects', { headers, data: { name: unique('Gate scene'), oversightType: 'Local' } })).json()
  const parcel = await (await request.post('/api/v1/parcels', { headers, data: { projectId: project.id, areaSqFt: 1000, acquisitionType: 'Standard' } })).json()
  expect((await request.post(`/api/v1/parcels/${parcel.id}/advance`, { headers, data: { targetStepKey: 'title_survey' } })).status()).toBe(200)
  return parcel as { id: string; number: string }
}

const setGate = (request: APIRequestContext, headers: Headers, enabled: boolean) => request.put('/api/v1/agency/plan-review-gate', { headers, data: { enabled } })

test.describe('Administrator and Project Manager · plan-review gate', () => {
  test('AT-AD-29 / AT-PM-32 the administrator turns the gate on; a parcel is held at Title & survey until the engineer and surveyor approve; then it moves', async ({ page, request, browser }) => {
    const admin = await as(request, 'Admin')
    await signInViaUi(page, 'Admin')
    await page.goto('/administration')
    const box = page.getByRole('checkbox', { name: 'Require engineer and surveyor plan reviews' })
    await expect(box).not.toBeChecked()
    try {
      await box.click() // the box follows the saved setting, so it ticks once the server has answered
      await expect(box).toBeChecked()
      await page.reload()
      await expect(page.getByRole('checkbox', { name: 'Require engineer and surveyor plan reviews' })).toBeChecked()

      const parcel = await parcelAtReview(request, admin)
      const context = await browser.newContext({ baseURL: new URL(page.url()).origin })
      const pm = await context.newPage()
      await signInViaUi(pm, 'ProjectManager')
      await pm.goto(`/parcels/${parcel.id}`)

      // Held: the button is refused with the reason, and an override cannot get past it.
      await pm.getByRole('tab', { name: 'Workflow & milestones' }).click()
      await pm.getByRole('button', { name: /^Advance to / }).click()
      await expect(pm.getByText(/requires an approved plan review by the engineer and surveyor/)).toBeVisible()
      const refused = await request.post(`/api/v1/parcels/${parcel.id}/advance`, { headers: admin, data: { targetStepKey: 'offer', overrideReason: 'Cleared by the director' } })
      expect(refused.status()).toBe(409)
      expect(await refused.text()).toContain('plan_review_required')

      // The reviews are recorded on the parcel.
      await pm.getByRole('tab', { name: 'Reviews & site visits' }).click()
      const banner = pm.getByRole('status', { name: 'Plan review gate' })
      await expect(banner).toContainText('held at Title & survey')
      const form = pm.getByRole('form', { name: 'Record a plan review' })
      await form.getByRole('button', { name: 'Record review' }).click()
      await expect(form.getByText('Choose the review date.')).toBeVisible()
      await expect(form.getByText('Enter who reviewed the plans.')).toBeVisible()
      await form.getByLabel(/^Date reviewed/).fill(today())
      await form.getByLabel(/^Reviewer/).fill('Morgan Ellis, P.E.')
      await form.getByRole('button', { name: 'Record review' }).click()
      await expect(banner).toContainText('review by the surveyor')

      // A review asking for changes does not release it; an approval does.
      await form.getByLabel(/^Review by/).selectOption('Surveyor')
      await form.getByLabel(/^Outcome/).selectOption('ChangesRequired')
      await form.getByLabel(/^Date reviewed/).fill(today())
      await form.getByLabel(/^Reviewer/).fill('Sam Ortiz, PLS')
      await form.getByRole('button', { name: 'Record review' }).click()
      await expect(form.getByText('Say what has to change.')).toBeVisible()
      await form.getByLabel(/^Notes/).fill('Boundary off by two feet.')
      await form.getByRole('button', { name: 'Record review' }).click()
      await expect(pm.getByRole('table', { name: 'Plan reviews' }).getByRole('row')).toHaveCount(3) // saved (the form empties its date when it is)
      await expect(banner).toContainText('review by the surveyor')
      await form.getByLabel(/^Outcome/).selectOption('Approved')
      await form.getByLabel(/^Date reviewed/).fill(today())
      await form.getByLabel(/^Reviewer/).fill('Sam Ortiz, PLS')
      await form.getByLabel(/^Notes/).fill('')
      await form.getByRole('button', { name: 'Record review' }).click()
      await expect(banner).toContainText('Plan reviews complete')
      await expect(pm.getByRole('table', { name: 'Plan reviews' }).getByRole('row')).toHaveCount(4)

      await pm.getByRole('tab', { name: 'Workflow & milestones' }).click()
      await pm.getByRole('button', { name: /^Advance to / }).click()
      await expect(pm.getByRole('listitem').filter({ hasText: '(current step)' })).not.toContainText('Title & survey')
      await context.close()
    } finally {
      expect((await setGate(request, admin, false)).status()).toBe(200)
    }

    // Off again: another parcel moves with no reviews at all.
    const free = await parcelAtReview(request, admin)
    expect((await request.post(`/api/v1/parcels/${free.id}/advance`, { headers: admin, data: { targetStepKey: 'appraisal' } })).status()).toBe(200)
    await page.reload()
    await expect(page.getByRole('checkbox', { name: 'Require engineer and surveyor plan reviews' })).not.toBeChecked()
  })

  for (const role of ROLES) {
    test(`AT-AD-29 ${role}: only an administrator can change the gate; everyone who reads parcels can see it`, async ({ request }) => {
      const headers = await as(request, role)
      const read = await request.get('/api/v1/agency/plan-review-gate', { headers })
      expect(read.status()).toBe(level(role, 'parcels') === null ? 403 : 200)
      const write = await setGate(request, headers, false)
      expect(write.status()).toBe(role === 'Admin' ? 200 : 403)
    })
  }
})
