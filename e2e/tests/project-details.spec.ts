import { expect, test, type Page } from '@playwright/test'
import { level, newId, ROLES, sessionToken, signInViaUi } from './support'

/**
 * Phase 1.5 slice 4 (ADR 0016): the ROW project details, the link to a Planned or Build project and Add parcel on a project.
 * Real browser, UI, API and Postgres. Case ids match vault/acceptance/ProjectManager.md and Everyone.md. Tests share one database,
 * so every project they create has a unique name.
 */

const canCreateProject = (role: string) => ['S', 'P'].includes(level(role, 'projects') ?? '')
const canCreateParcel = (role: string) => ['S', 'P'].includes(level(role, 'parcels') ?? '')
const unique = (label: string) => `${label} ${newId().slice(0, 8)}`

async function openProjects(page: Page) {
  await page.getByRole('complementary', { name: 'Primary' }).getByRole('link', { name: 'Projects' }).click()
  await expect(page.getByRole('heading', { name: 'Projects', exact: true })).toBeVisible()
}

test.describe('Project Manager · project details', () => {
  test('AT-PM-40 enters the ROW project details, links a Build project, and reads them all back', async ({ page }) => {
    const name = unique('Premium Hub')
    await signInViaUi(page, 'ProjectManager')
    await openProjects(page)
    await page.getByRole('button', { name: 'New project' }).click()
    const form = page.getByRole('form', { name: 'New project' })

    await expect(form.getByLabel(/^Phase/)).toHaveValue('Phase 1')
    await form.getByLabel(/^Name/).fill(name)
    await form.getByLabel(/^Location/).fill('Premium Residential and Commercial Hub, Bangalore')
    await form.getByLabel(/^Project manager/).selectOption({ label: 'ROW Project Manager' })
    await form.getByLabel(/^Project description/).fill('Residential and commercial hub')
    await form.getByLabel(/^Funding source/).fill('Board')
    await form.getByLabel(/^Funding amount/).fill('477770.44')
    await form.getByLabel(/^Construction project manager/).fill('ritz')
    await form.getByLabel(/^Letting date/).fill('2026-05-01')
    await form.getByLabel(/^Right of way clear date/).fill('2027-05-31')
    await form.getByLabel(/^Construction start/).fill('2026-05-01')
    await form.getByLabel(/^Construction end/).fill('2028-05-23')

    // Link to a Build project from the other module: pick the source, search, choose.
    await expect(form.getByLabel(/^Linked project/)).toHaveCount(0)
    await form.getByLabel(/^Link to/).selectOption('Build')
    await form.getByLabel(/^Search linked projects/).fill('harbor')
    await form.getByLabel(/^Linked project/).selectOption({ label: 'BLD-2025-017 · Harbor Bridge Replacement' })
    await form.getByRole('button', { name: 'Create project' }).click()

    await expect(page.getByRole('heading', { level: 1 })).toContainText(name)
    const details = page.getByRole('region', { name: 'Project details' })
    await expect(details).toContainText('Phase 1')
    await expect(details).toContainText('Premium Residential and Commercial Hub, Bangalore')
    await expect(details).toContainText('ROW Project Manager')
    await expect(details).toContainText('Build: BLD-2025-017 · Harbor Bridge Replacement')
    const construction = page.getByRole('region', { name: 'Construction details' })
    await expect(construction).toContainText('ritz')
    await expect(construction).toContainText('2027')
    await expect(page.getByRole('region', { name: 'Funding information' })).toContainText('$477,770')

    // The funding block follows the budgets: the board-approved funds are the annual budgets added up.
    await page.getByRole('tab', { name: 'Budget' }).click()
    const budget = page.getByRole('form', { name: 'Set a fiscal-year budget' })
    await budget.getByLabel(/^Fiscal year/).fill('2026')
    await budget.getByLabel(/^Budget/).fill('125000')
    await budget.getByRole('button', { name: 'Set budget' }).click()
    await expect(page.getByRole('table', { name: /budget/i })).toContainText('2026')
    await page.getByRole('tab', { name: 'Overview' }).click()
    await expect(page.getByRole('region', { name: 'Funding information' })).toContainText('$125,000')

    // And it survives a reload and an edit that clears the link.
    await page.reload()
    await expect(page.getByRole('region', { name: 'Project details' })).toContainText('Build: BLD-2025-017')
    await page.getByRole('button', { name: 'Edit project' }).click()
    const edit = page.getByRole('form', { name: 'Edit project' })
    await expect(edit.getByLabel(/^Link to/)).toHaveValue('Build')
    await edit.getByLabel(/^Link to/).selectOption('')
    await edit.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByRole('region', { name: 'Project details' })).not.toContainText('Build: BLD-2025-017')
  })

  test('AT-PM-40 a new project needs a phase, and the API refuses a manager or linked project that does not exist', async ({ page, request }) => {
    await signInViaUi(page, 'ProjectManager')
    await openProjects(page)
    await page.getByRole('button', { name: 'New project' }).click()
    const form = page.getByRole('form', { name: 'New project' })
    await form.getByLabel(/^Name/).fill(unique('No Phase'))
    await form.getByLabel(/^Phase/).fill('')
    await form.getByRole('button', { name: 'Create project' }).click()
    await expect(form.getByText('Phase is required.')).toBeVisible()
    await form.getByLabel(/^Link to/).selectOption('Planned')
    await form.getByLabel(/^Phase/).fill('Phase 1')
    await form.getByRole('button', { name: 'Create project' }).click()
    await expect(form.getByText('Choose the project to link to.')).toBeVisible()

    const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
    const body = { name: unique('API'), oversightType: 'Federal' }
    const noManager = await request.post('/api/v1/projects', { headers, data: { ...body, projectManagerUserId: newId() } })
    expect(noManager.status()).toBe(400)
    expect((await noManager.json()).title).toBe('unknown_project_manager')
    const noLink = await request.post('/api/v1/projects', { headers, data: { ...body, linkedSource: 'Build', linkedProjectNumber: 'BLD-0000-000' } })
    expect(noLink.status()).toBe(400)
    expect((await noLink.json()).title).toBe('unknown_linked_project')
  })

  test('AT-PM-42 adds a parcel from inside a project: the form is for this project and the new parcel opens', async ({ page, request }) => {
    const name = unique('Parcel Host')
    await signInViaUi(page, 'ProjectManager')
    const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
    const project = await (await request.post('/api/v1/projects', { headers, data: { name, oversightType: 'Local', phase: 'Phase 1' } })).json()

    await page.goto(`/projects/${project.id}`)
    await page.getByRole('button', { name: 'Add parcel' }).click()
    const panel = page.getByRole('region', { name: 'Add a parcel to this project' })
    await expect(panel).toContainText(`Add a parcel to ${project.number}`)
    await expect(panel.getByLabel(/^Project/)).toHaveValue(project.id)
    await panel.getByLabel(/^Address/).fill('9 Add Parcel Way')
    await panel.getByLabel(/^Area \(sq ft\)/).fill('4300')
    await panel.getByRole('button', { name: 'Create parcel' }).click()

    await expect(page).toHaveURL(/\/parcels\/[0-9a-f-]{36}$/)
    await expect(page.getByRole('heading', { level: 1 })).toContainText(project.number)
    const list = await (await request.get(`/api/v1/parcels?projectId=${project.id}`, { headers })).json()
    expect(list.items).toHaveLength(1)
    expect(list.items[0].address).toBe('9 Add Parcel Way')
  })
})

test.describe('Everyone · project details', () => {
  for (const role of ROLES) {
    test(`AT-PM-41 ${role}: the managers list and the Planned/Build catalog follow the Projects permission, and Add parcel follows Parcels`, async ({ page, request }) => {
      await signInViaUi(page, role)
      const mine = { Authorization: `Bearer ${await sessionToken(page)}` }
      const admin = (await (await request.get('/api/v1/auth/dev-token?role=Admin')).json()).token as string
      const project = await (await request.post('/api/v1/projects', {
        headers: { Authorization: `Bearer ${admin}` }, data: { name: unique(`Visible to ${role}`), oversightType: 'Local', phase: 'Phase 1' },
      })).json()

      const readable = level(role, 'projects') !== null
      expect((await request.get('/api/v1/projects/managers', { headers: mine })).status()).toBe(readable ? 200 : 403)
      expect((await request.get('/api/v1/projects/catalog?source=Planned', { headers: mine })).status()).toBe(readable ? 200 : 403)
      expect((await request.get('/api/v1/projects/catalog?source=Planned')).status()).toBe(401)

      if (!readable) return
      await page.goto(`/projects/${project.id}`)
      await expect(page.getByRole('heading', { level: 1 })).toContainText(project.number)
      await expect(page.getByRole('button', { name: 'Add parcel' })).toHaveCount(canCreateParcel(role) ? 1 : 0)
      await expect(page.getByRole('button', { name: 'Edit project' })).toHaveCount(canCreateProject(role) ? 1 : 0)
    })
  }
})
