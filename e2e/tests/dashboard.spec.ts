import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { canRead, devToken, newId, ROLES, sessionToken, signInViaUi } from './support'

/**
 * Phase 1.5 (ADR 0022): each person's dashboard (what they did, what is waiting), the manager's Team roll-up, and the Help center behind the
 * question-mark button. Real browser, UI, API and Postgres. Case ids match vault/acceptance/Everyone.md (AT-X-58, AT-X-59, AT-X-60).
 */

const MANAGERS = ['ProjectManager', 'Admin']
const exact = { exact: true } as const
const unique = (label: string) => `${label} ${newId().slice(0, 8)}`
const auth = async (request: APIRequestContext, role: string) => ({ Authorization: `Bearer ${await devToken(request, role)}` })

/** The number a tile shows. */
async function tile(page: Page, label: string) {
  const text = await page.getByTestId(`tile-${label}`).innerText()
  return Number(text.replace(/[^0-9]/g, ''))
}

async function parcelFor(request: APIRequestContext) {
  const headers = await auth(request, 'Admin')
  const project = await (await request.post('/api/v1/projects', { headers, data: { name: unique('Dash scene'), oversightType: 'Local', phase: 'Phase 1' } })).json()
  return (await (await request.post('/api/v1/parcels', { headers, data: { projectId: project.id, areaSqFt: 1000, acquisitionType: 'Standard' } })).json()) as { id: string; number: string }
}

for (const role of ROLES) {
  test.describe(`Dashboard · ${role}`, () => {
    test(`AT-X-58 ${role} has a dashboard of their own, and only managers get the Team tab`, async ({ page, request }) => {
      await signInViaUi(page, role)

      await expect(page.getByRole('region', { name: 'Waiting for you' })).toBeVisible()
      await expect(page.getByRole('region', { name: 'What you have done' })).toBeVisible()
      await expect(page.getByRole('img', { name: /Your actions per day/ })).toBeVisible()
      await expect(page.getByTestId('tile-Done in 30 days')).toBeVisible()
      await expect(page.getByTestId('tile-Last active')).toBeVisible()

      const team = page.getByRole('tab', { name: 'Team', ...exact })
      if (MANAGERS.includes(role)) await expect(team).toBeVisible()
      else await expect(team).toHaveCount(0)

      const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
      const mine = await request.get('/api/v1/dashboard/me', { headers })
      expect(mine.status()).toBe(200)
      expect((await mine.json()).roles).toEqual([role])
      expect((await request.get('/api/v1/dashboard/team', { headers })).status()).toBe(MANAGERS.includes(role) ? 200 : 403) // the server agrees with the screen
    })

    test(`AT-X-60 ${role} opens Help from the question mark and finds a topic`, async ({ page }) => {
      await signInViaUi(page, role)

      await page.getByRole('link', { name: 'Help', ...exact }).click()
      await expect(page).toHaveURL(/\/help/)
      await expect(page.getByRole('heading', { name: 'Help center' })).toBeVisible()

      await page.getByRole('searchbox', { name: 'Search topics' }).fill('just compensation')
      const results = page.getByRole('region', { name: 'Search results' })
      await expect(results).toContainText('topic')
      await results.getByRole('link', { name: /Key concepts/ }).click()
      await expect(page.getByRole('heading', { name: 'Key concepts', ...exact })).toBeVisible()
      await expect(page.getByRole('table', { name: /Term/ })).toContainText('Just compensation')
    })
  })
}

test.describe('Dashboard · what I did', () => {
  test('AT-X-58 a negotiator’s dashboard counts their own work, and not somebody else’s', async ({ page, request }) => {
    const parcel = await parcelFor(request)
    await signInViaUi(page, 'Negotiator')
    await expect(page.getByRole('region', { name: 'What you have done' })).toBeVisible()
    const before = await tile(page, 'Done in 30 days')
    const negotiator = { Authorization: `Bearer ${await sessionToken(page)}` }

    // Another person's work must not move this person's number.
    const other = await parcelFor(request)
    expect(other.id).not.toBe(parcel.id)
    await page.reload()
    expect(await tile(page, 'Done in 30 days')).toBe(before)

    const log = await request.post(`/api/v1/parcels/${parcel.id}/contact-log`, { headers: negotiator, data: { contactedAt: new Date().toISOString(), mode: 'Phone', notes: unique('Dashboard call') } })
    expect(log.status()).toBe(201)
    await page.getByRole('button', { name: /Refresh/ }).click()

    await expect.poll(() => tile(page, 'Done in 30 days')).toBeGreaterThan(before)
    await expect(page.getByRole('table', { name: 'Your work by kind of record' })).toContainText('Parcel contact log entry')
    await expect(page.getByRole('list', { name: 'Your recent activity' })).toContainText('Added parcel contact log entry')
    const today = new Date()
    const key = `${today.getUTCFullYear()}-${String(today.getUTCMonth() + 1).padStart(2, '0')}-${String(today.getUTCDate()).padStart(2, '0')}`
    await expect(page.getByTestId(`day-${key}`)).toBeVisible()
  })
})

test.describe('Dashboard · what is waiting', () => {
  test('AT-X-58 a request waits for somebody else: the manager sees it as their own request, finance sees it to decide, and the link opens it', async ({ page, request, browser }) => {
    const pm = await auth(request, 'ProjectManager')
    const created = await request.post('/api/v1/acquisition-requests', { headers: pm, data: { requesterType: 'Internal', requesterName: 'Design', property: unique('Lot'), estimatedAmount: 5000 } })
    expect(created.status()).toBe(201)

    await signInViaUi(page, 'Finance')
    const decide = page.getByRole('link', { name: /Acquisition requests waiting for your decision/ })
    await expect(decide).toBeVisible()
    expect(Number(await decide.getByTestId('pending-requests-to-decide').innerText())).toBeGreaterThan(0)

    const pmPage = await browser.newPage()
    await signInViaUi(pmPage, 'ProjectManager')
    await expect(pmPage.getByRole('link', { name: /Your acquisition requests still waiting/ })).toBeVisible()
    await pmPage.close()

    await decide.click()
    await expect(page).toHaveURL(/\/requests/)
  })

  test('AT-X-58 a role that cannot open a page is never shown work that lives on it', async ({ page }) => {
    await signInViaUi(page, 'Surveyor')

    expect(canRead('Surveyor', 'approvals')).toBe(false)
    await expect(page.getByRole('link', { name: /Acquisition requests/ })).toHaveCount(0)
    await expect(page.getByRole('link', { name: /Appraisals without/ })).toHaveCount(0)
  })
})

test.describe('Dashboard · the manager roll-up', () => {
  for (const role of MANAGERS) {
    test(`AT-X-59 ${role} switches to the Team tab and sees the portfolio, the status mix and each person`, async ({ page }) => {
      await signInViaUi(page, role)

      await page.getByRole('tab', { name: 'Team', ...exact }).click()

      await expect(page.getByTestId('tile-Projects')).toBeVisible()
      await expect(page.getByTestId('tile-Active parcels')).toBeVisible()
      await expect(page.getByTestId('tile-Overdue deadlines')).toBeVisible()
      await expect(page.getByRole('img', { name: /Team actions per day/ })).toBeVisible()
      const people = page.getByRole('table', { name: 'Team members' })
      await expect(people).toBeVisible()
      expect(await people.getByRole('row').count()).toBeGreaterThan(1)
      await expect(people.getByRole('columnheader', { name: 'Last active' })).toBeVisible()

      await page.getByRole('tab', { name: 'My work', ...exact }).click()
      await expect(page.getByRole('region', { name: 'Waiting for you' })).toBeVisible()
    })
  }

  test('AT-X-59 the team numbers add up work from several people', async ({ page, request }) => {
    const parcel = await parcelFor(request)
    expect(parcel.id).toBeTruthy()
    await signInViaUi(page, 'ProjectManager')
    await page.getByRole('tab', { name: 'Team', ...exact }).click()

    const api = await request.get('/api/v1/dashboard/team', { headers: { Authorization: `Bearer ${await sessionToken(page)}` } })
    const team = await api.json()
    expect(team.tiles.find((t: { key: string }) => t.key === 'projects').value).toBeGreaterThan(0)
    expect(team.byKind.some((k: { kind: string }) => k.kind === 'Parcel')).toBe(true)
    await expect(page.getByTestId('tile-Active parcels')).toHaveText(String(team.tiles.find((t: { key: string }) => t.key === 'parcels').value))
  })
})

test.describe('Help center', () => {
  test('AT-X-60 the topics follow the person: the access list for a surveyor leaves out the pages they cannot open', async ({ page }) => {
    await signInViaUi(page, 'Surveyor')
    await page.getByRole('link', { name: 'Help', ...exact }).click()

    await page.getByRole('navigation', { name: 'Help contents' }).getByRole('link', { name: 'What your role can do' }).click()

    const table = page.getByRole('table', { name: 'Pages your role can open' })
    await expect(table).toContainText('Dashboard')
    await expect(table).toContainText('Parcel tab: Easements')
    await expect(table).not.toContainText('Configuration and Settings')
  })

  test('AT-X-60 help can be opened by address, steps through topics, and says so when nothing matches', async ({ page }) => {
    await signInViaUi(page, 'Negotiator')
    await page.goto('/help?topic=quick-start')
    await expect(page.getByRole('heading', { name: 'Quick start', ...exact })).toBeVisible()
    await expect(page.getByRole('navigation', { name: 'Breadcrumb' })).toContainText('Getting started')

    await page.getByRole('link', { name: /What your role can do →/ }).click()
    await expect(page.getByRole('heading', { name: 'What your role can do', ...exact })).toBeVisible()

    await page.getByRole('searchbox', { name: 'Search topics' }).fill('qqqqzzzz')
    await expect(page.getByText('No topics match.')).toBeVisible()
  })

  test('AT-X-60 signed out, help is not reachable: the person is sent to sign in', async ({ page }) => {
    await page.goto('/help')
    await expect(page).toHaveURL(/\/login/)
  })
})
