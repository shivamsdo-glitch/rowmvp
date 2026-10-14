import { expect, test, type Page } from '@playwright/test'
import { level, newId, ROLES, sessionToken, signInViaUi, signOutViaUi } from './support'

/**
 * Phase 1, slice 1 — project core (ROW-001, 003, 004, 012, 013, 076, 238). Real browser, real UI, real API, real Postgres.
 * Case ids match vault/acceptance/<Role>.md. Tests share one database and run in parallel, so every test creates its own
 * uniquely named projects and finds them by name.
 */

const canCreate = (role: string) => ['S', 'P'].includes(level(role, 'projects') ?? '')
const canDelete = (role: string) => level(role, 'projects') === 'P'
const unique = (label: string) => `${label} ${newId().slice(0, 8)}`
/** Generated numbers are `<year>-<4 or 5 digits>`: the Administrator journey briefly widens the sequence for the whole agency. */
const GENERATED = /^\d{4}-\d{4,5}$/

const banner = (page: Page) => page.getByRole('status', { name: 'Acquisition status' })

async function openProjects(page: Page) {
  await page.getByRole('complementary', { name: 'Primary' }).getByRole('link', { name: 'Projects' }).click()
  await expect(page.getByRole('heading', { name: 'Projects', exact: true })).toBeVisible()
}

async function createViaUi(page: Page, fields: { name: string; number?: string; region?: string; district?: string; oversight?: string; funding?: string }) {
  await page.getByRole('button', { name: 'New project' }).click()
  const form = page.getByRole('form', { name: 'New project' })
  await form.getByLabel('Name').fill(fields.name)
  if (fields.number) await form.getByLabel('Project number').fill(fields.number)
  if (fields.region) await form.getByLabel('Region').fill(fields.region)
  if (fields.district) await form.getByLabel('District').fill(fields.district)
  if (fields.funding) await form.getByLabel('Funding amount').fill(fields.funding)
  if (fields.oversight) await form.getByLabel('Funding oversight').selectOption(fields.oversight)
  await form.getByRole('button', { name: 'Create project' }).click()
}

async function findInList(page: Page, name: string) {
  await page.getByLabel('Search projects').fill(name)
  return page.getByRole('row', { name: new RegExp(name) })
}

// ───────────────────────────────── Project Manager ─────────────────────────────────
test.describe('Project Manager · projects', () => {
  test('AT-PM-20 creates a project, versions its estimate, budgets it and clears it to acquire, end to end', async ({ page, request }) => {
    await signInViaUi(page, 'ProjectManager')
    await openProjects(page)
    const name = unique('Journey Highway')

    // Create: the number is generated, the project opens and is held at the gate (federal, nothing on file).
    await createViaUi(page, { name, region: 'North', district: 'D7', funding: '2500000' })
    await expect(page.getByRole('heading', { name: new RegExp(name) })).toBeVisible()
    const heading = await page.getByRole('heading', { level: 1 }).innerText()
    const number = heading.split(' · ')[0]
    expect(number).toMatch(GENERATED)
    await expect(banner(page)).toContainText('on hold')
    await expect(banner(page)).toContainText('Missing: NEPA decision and Acquisition authorization')

    // Estimate: two versions, the newest is current, the first is kept.
    await page.getByRole('tab', { name: 'Cost estimates' }).click()
    await expect(page.getByText('No estimate yet')).toBeVisible()
    for (const [land, note] of [['1000000', 'first estimate'], ['1200000', 'revised after survey']]) {
      await page.getByRole('button', { name: 'Add estimate version' }).click()
      const form = page.getByRole('form', { name: 'New estimate version' })
      await form.getByLabel('Land').fill(land)
      await form.getByLabel('Improvements').fill('100000')
      await form.getByLabel('Notes').fill(note)
      await form.getByRole('button', { name: 'Save new version' }).click()
      await expect(page.getByRole('row', { name: new RegExp(note) })).toBeVisible()
    }
    const versions = page.getByRole('table', { name: 'Cost estimate versions' }).getByRole('row')
    await expect(versions.nth(1)).toContainText('v2')
    await expect(versions.nth(1)).toContainText('Current')
    await expect(versions.nth(1)).toContainText('$1,300,000')
    await expect(versions.nth(2)).toContainText('v1')
    await expect(versions.nth(2)).toContainText('$1,100,000')

    // Budget: one row per fiscal year; setting a year again adjusts it.
    await page.getByRole('tab', { name: 'Budget' }).click()
    const budgetForm = page.getByRole('form', { name: 'Set a fiscal-year budget' })
    await budgetForm.getByLabel('Fiscal year').fill('2027')
    await budgetForm.getByLabel(/^Budget/).fill('900000')
    await budgetForm.getByRole('button', { name: 'Set budget' }).click()
    await expect(page.getByRole('row', { name: /FY2027/ })).toContainText('$900,000')
    await budgetForm.getByLabel('Fiscal year').fill('2027')
    await budgetForm.getByLabel(/^Budget/).fill('950000')
    await budgetForm.getByRole('button', { name: 'Set budget' }).click()
    await expect(page.getByRole('row', { name: /FY2027/ })).toContainText('$950,000')
    await expect(page.getByRole('row', { name: /FY2027/ })).toHaveCount(1)

    // Clearance: NEPA alone is not enough; a preliminary authorization is not enough; an acquisition authorization is.
    await page.getByRole('tab', { name: 'Clearance' }).click()
    const nepa = page.getByRole('form', { name: 'NEPA decision' })
    await nepa.getByLabel('Decision type').selectOption('CategoricalExclusion')
    await nepa.getByRole('button', { name: 'Save NEPA decision' }).click()
    await expect(nepa.getByText('A decision date is required.')).toBeVisible()
    await nepa.getByLabel('Decision date').fill('2026-01-10')
    await nepa.getByRole('button', { name: 'Save NEPA decision' }).click()
    await expect(nepa.getByText('NEPA decision saved.')).toBeVisible()
    await expect(banner(page)).toContainText('Missing: Acquisition authorization')

    await page.getByRole('tab', { name: 'ROW authorizations' }).click()
    const auth = page.getByRole('form', { name: 'Add a ROW authorization' })
    await auth.getByLabel(/^Authorization type/).selectOption('Preliminary')
    await auth.getByLabel(/^Authorized amount/).fill('100000')
    await auth.getByLabel(/^Authorization date/).fill('2026-01-20')
    await auth.getByRole('button', { name: 'Add authorization' }).click()
    await expect(page.getByRole('row', { name: /Preliminary/ })).toBeVisible()
    await expect(banner(page)).toContainText('on hold')

    await auth.getByLabel(/^Authorization type/).selectOption('Acquisition')
    await auth.getByLabel(/^Authorized amount/).fill('500000')
    await auth.getByLabel(/^Federal share/).fill('80')
    await auth.getByLabel(/^Authorization date/).fill('2026-02-01')
    await auth.getByRole('button', { name: 'Add authorization' }).click()
    await expect(page.getByRole('row', { name: /Acquisition/ })).toContainText('80% ($400,000)')
    await expect(banner(page)).toContainText('Offers and negotiations are open')

    // What the screens showed is what the server holds.
    const token = await sessionToken(page)
    const headers = { Authorization: `Bearer ${token}` }
    const found = await (await request.get(`/api/v1/projects?search=${encodeURIComponent(name)}`, { headers })).json()
    expect(found.items).toHaveLength(1)
    expect(found.items[0]).toMatchObject({ number, name, region: 'North', district: 'D7', currentEstimate: 1300000 })
    expect(found.items[0].acquisitionGate).toMatchObject({ unlocked: true, reason: 'ClearanceComplete' })

    // The list agrees, and the number is the unique key.
    await openProjects(page)
    const row = await findInList(page, name)
    await expect(row).toContainText('Open for offers')
    await expect(row).toContainText('$1,300,000')
    await row.getByRole('link', { name: number }).click()
    await expect(page.getByRole('heading', { level: 1 })).toContainText(number)
  })

  test('AT-PM-20 a hand-typed number is kept, and a duplicate is refused with a clear message', async ({ page }) => {
    await signInViaUi(page, 'ProjectManager')
    await openProjects(page)
    const number = `TYPED-${newId().slice(0, 8)}`

    await createViaUi(page, { name: unique('Typed number'), number })
    await expect(page.getByRole('heading', { level: 1 })).toContainText(number)

    await openProjects(page)
    await createViaUi(page, { name: unique('Duplicate number'), number: number.toLowerCase() })
    await expect(page.getByRole('alert')).toContainText(`A project numbered '${number.toLowerCase()}' already exists.`)
    await expect(page.getByRole('form', { name: 'New project' })).toBeVisible()
  })

  test('AT-PM-20 asks for a name and reports backwards construction dates next to the field', async ({ page }) => {
    await signInViaUi(page, 'ProjectManager')
    await openProjects(page)
    await page.getByRole('button', { name: 'New project' }).click()
    const form = page.getByRole('form', { name: 'New project' })

    await form.getByRole('button', { name: 'Create project' }).click()
    await expect(form.getByText('Name is required.')).toBeVisible()

    await form.getByLabel('Name').fill(unique('Backwards dates'))
    await form.getByLabel('Construction start').fill('2027-05-01')
    await form.getByLabel('Construction end').fill('2027-04-01')
    await form.getByRole('button', { name: 'Create project' }).click()
    await expect(form.getByText('Construction end cannot be before construction start.')).toBeVisible()
    await expect(form.getByLabel('Construction end')).toHaveAttribute('aria-invalid', 'true')
  })

  test('AT-PM-20 edits a project record and finds projects by region, district, oversight and text', async ({ page }) => {
    await signInViaUi(page, 'ProjectManager')
    await openProjects(page)
    const stamp = newId().slice(0, 8)
    const federal = `Filter fed ${stamp}`, local = `Filter loc ${stamp}`
    await createViaUi(page, { name: federal, region: `R-${stamp}`, district: 'D1', oversight: 'Federal' })
    await expect(page.getByRole('heading', { name: new RegExp(federal) })).toBeVisible()
    await openProjects(page)
    await createViaUi(page, { name: local, region: `R-${stamp}`, district: 'D2', oversight: 'Local' })
    await expect(page.getByRole('heading', { name: new RegExp(local) })).toBeVisible()

    await openProjects(page)
    await page.getByLabel('Region').fill(`R-${stamp}`)
    await expect(page.getByRole('row', { name: new RegExp(federal) })).toBeVisible()
    await expect(page.getByRole('row', { name: new RegExp(local) })).toBeVisible()
    await page.getByLabel('Funding oversight').selectOption('Local')
    await expect(page.getByRole('row', { name: new RegExp(federal) })).toHaveCount(0)
    await expect(page.getByRole('row', { name: new RegExp(local) })).toContainText('Open for offers')
    await page.getByLabel('Funding oversight').selectOption('')
    await page.getByLabel('District').fill('D1')
    await expect(page.getByRole('row', { name: new RegExp(local) })).toHaveCount(0)
    await page.getByLabel('Search projects').fill('no-such-project-anywhere')
    await expect(page.getByText('No projects match your filters')).toBeVisible()
    await page.getByLabel('Search projects').fill('')
    await page.getByLabel('Region').fill('')
    await page.getByLabel('District').fill('')

    // Edit one of them.
    await (await findInList(page, federal)).getByRole('link').first().click()
    await page.getByRole('button', { name: 'Edit project' }).click()
    const edit = page.getByRole('form', { name: 'Edit project' })
    await edit.getByLabel('Name').fill(`${federal} renamed`)
    await edit.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByRole('heading', { level: 1 })).toContainText(`${federal} renamed`)
  })

  test('AT-PM-20 an approved hardship exception opens a held project, and withdrawing it holds it again', async ({ page }) => {
    await signInViaUi(page, 'ProjectManager')
    await openProjects(page)
    await createViaUi(page, { name: unique('Hardship case') })
    await expect(banner(page)).toContainText('on hold')

    await page.getByRole('tab', { name: 'Clearance' }).click()
    const form = page.getByRole('form', { name: 'Acquisition exception' })
    await form.getByLabel('Exception type').selectOption('Hardship')
    await form.getByRole('button', { name: 'Save exception' }).click()
    await expect(form.getByText('An approval date is required.')).toBeVisible()
    await expect(form.getByText('A reason is required for an exception.')).toBeVisible()
    await form.getByLabel('Approval date').fill('2026-03-01')
    await form.getByLabel('Reason').fill('Owner medical hardship')
    await form.getByRole('button', { name: 'Save exception' }).click()
    await expect(banner(page)).toContainText('hardship acquisition exception is approved')

    await form.getByLabel('Exception type').selectOption('')
    await form.getByRole('button', { name: 'Save exception' }).click()
    await expect(banner(page)).toContainText('on hold')
  })

  test('AT-PM-20 imports projects from a CSV file; good rows are created and bad ones are explained', async ({ page, request }) => {
    const token = await (async () => { await signInViaUi(page, 'ProjectManager'); return sessionToken(page) })()
    await openProjects(page)
    const stamp = newId().slice(0, 8)
    const csv = [
      'Number,Name,Region,Oversight,Funding amount',
      `IMP-${stamp}-1,Imported one ${stamp},East,State,1500000`,
      `,Imported generated ${stamp},East,Local,`,
      `IMP-${stamp}-1,Same number again ${stamp},East,Local,`,
      `IMP-${stamp}-2, ,East,Local,`,
    ].join('\n')

    await page.getByRole('button', { name: 'Import CSV' }).click()
    await page.getByLabel('CSV file').setInputFiles({ name: 'projects.csv', mimeType: 'text/csv', buffer: Buffer.from(csv) })
    await expect(page.getByText('4 projects ready to import.')).toBeVisible()
    await page.getByRole('button', { name: 'Import', exact: true }).click()

    await expect(page.getByText('2 created, 2 failed.')).toBeVisible()
    const failures = page.getByRole('table', { name: 'Rows that could not be imported' })
    await expect(failures).toContainText('appears more than once in this file')
    await expect(failures).toContainText('Name is required')

    await page.getByRole('button', { name: 'Done' }).click()
    await expect(await findInList(page, `Imported one ${stamp}`)).toContainText(`IMP-${stamp}-1`)
    await expect(await findInList(page, `Imported generated ${stamp}`)).toContainText('Open for offers')

    const found = await (await request.get(`/api/v1/projects?search=${stamp}`, { headers: { Authorization: `Bearer ${token}` } })).json()
    expect(found.total).toBe(2)
  })

  test('AT-PM-20 the demo agency opens with its three sample projects in the right gate state', async ({ page }) => {
    await signInViaUi(page, 'ProjectManager')
    await openProjects(page)
    await page.getByLabel('Search projects').fill('DEMO-')

    await expect(page.getByRole('row', { name: /DEMO-001/ })).toContainText('Held: NEPA decision, Acquisition authorization')
    await expect(page.getByRole('row', { name: /DEMO-002/ })).toContainText('Open for offers')
    await expect(page.getByRole('row', { name: /DEMO-002/ })).toContainText('$6,400,000')
    await expect(page.getByRole('row', { name: /DEMO-003/ })).toContainText('Open for offers')
  })

  test('AT-PM-20 someone else’s agency data is not visible: a project id from another agency is simply not found', async ({ page }) => {
    await signInViaUi(page, 'ProjectManager')
    await page.goto('/projects/00000000-0000-0000-0000-000000000abc')

    await expect(page.getByText('This project could not be opened')).toBeVisible()
  })
})

// ───────────────────────────────── Administrator ─────────────────────────────────
test.describe('Administrator · project numbering', () => {
  // Changes the agency-wide format, so it must not overlap with itself; it restores the default in `finally`.
  test.describe.configure({ mode: 'serial' })

  test('AT-AD-25 sets the project number format; new projects follow it, old numbers never change, bad patterns are refused', async ({ page }) => {
    await signInViaUi(page, 'Admin')
    await page.getByRole('complementary', { name: 'Primary' }).getByRole('link', { name: 'Configuration' }).click()
    const card = page.getByRole('region', { name: 'Project numbers' })
    const pattern = card.getByLabel('Number pattern')
    await expect(pattern).toHaveValue(/\{SEQ:\d\}/)
    const original = await pattern.inputValue()
    const originalMonth = await card.getByLabel('Fiscal year starts in').inputValue()

    try {
      await pattern.fill('no-sequence-token')
      await card.getByRole('button', { name: 'Save format' }).click()
      await expect(card.getByRole('alert').first()).toBeVisible()
      await expect(pattern).toHaveValue('no-sequence-token')

      await pattern.fill('{FY}-{SEQ:5}')
      await card.getByRole('button', { name: 'Save format' }).click()
      await expect(card.getByText('Saved.')).toBeVisible()
      await expect(card.getByText(/Next project number/)).toContainText(/^Next project number: \d{4}-\d{5}$/)

      await page.getByRole('complementary', { name: 'Primary' }).getByRole('link', { name: 'Projects' }).click()
      await createViaUi(page, { name: unique('Five digit number') })
      await expect(page.getByRole('heading', { level: 1 })).toContainText(/^\d{4}-\d{5} · /)
    } finally {
      await page.getByRole('complementary', { name: 'Primary' }).getByRole('link', { name: 'Configuration' }).click()
      await card.getByLabel('Number pattern').fill(original)
      await card.getByLabel('Fiscal year starts in').selectOption(originalMonth)
      await card.getByRole('button', { name: 'Save format' }).click()
      await expect(card.getByText('Saved.')).toBeVisible()
    }
  })
})

// ───────────────────────────────── Every role, both directions ─────────────────────────────────
for (const role of ROLES) {
  test.describe(`${role} · projects access`, () => {
    test(`AT-X-30 ${role} can open Projects and sees create/import controls only if their role can create`, async ({ page }) => {
      await signInViaUi(page, role)
      await openProjects(page)
      await expect(page.getByRole('table', { name: 'Projects' })).toBeVisible()

      for (const name of ['New project', 'Import CSV']) {
        await expect(page.getByRole('button', { name }), `${role} ${name}`).toHaveCount(canCreate(role) ? 1 : 0)
      }
    })

    test(`AT-X-31 ${role} gets exactly the project edit controls the access spec gives their role, and the server agrees`, async ({ page, request }) => {
      // An administrator makes a project for everyone to look at.
      await signInViaUi(page, 'Admin')
      const adminToken = await sessionToken(page)
      const seeded = await (await request.post('/api/v1/projects', {
        headers: { Authorization: `Bearer ${adminToken}` },
        data: { name: unique(`Spec target for ${role}`), oversightType: 'Federal' },
      })).json()

      await signOutViaUi(page)
      await signInViaUi(page, role)
      const token = await sessionToken(page)
      const headers = { Authorization: `Bearer ${token}` }

      // Screen
      await page.goto(`/projects/${seeded.id}`)
      await expect(page.getByRole('heading', { level: 1 })).toContainText(seeded.number)
      await expect(page.getByRole('button', { name: 'Edit project' })).toHaveCount(canCreate(role) ? 1 : 0)
      await page.getByRole('tab', { name: 'Cost estimates' }).click()
      await expect(page.getByRole('button', { name: 'Add estimate version' })).toHaveCount(canCreate(role) ? 1 : 0)
      await page.getByRole('tab', { name: 'Clearance' }).click()
      await expect(page.getByRole('button', { name: 'Save NEPA decision' })).toHaveCount(canCreate(role) ? 1 : 0)

      // Server
      const status = (r: { status(): number }) => r.status()
      expect(status(await request.get(`/api/v1/projects/${seeded.id}`, { headers }))).toBe(200)
      expect(status(await request.post('/api/v1/projects', { headers, data: { name: unique('By role'), oversightType: 'Local' } }))).toBe(canCreate(role) ? 201 : 403)
      expect(status(await request.put(`/api/v1/projects/${seeded.id}/nepa`, { headers, data: { decisionType: 'CategoricalExclusion', decisionDate: '2026-01-01' } }))).toBe(canCreate(role) ? 200 : 403)
      expect(status(await request.put(`/api/v1/projects/${seeded.id}/budgets/2031`, { headers, data: { amount: 1 } }))).toBe(canCreate(role) ? 200 : 403)
      expect(status(await request.delete(`/api/v1/projects/${seeded.id}/budgets/2031`, { headers }))).toBe(canDelete(role) ? 204 : 403)
      expect(status(await request.put('/api/v1/projects/number-settings', { headers, data: { pattern: '{FY}-{SEQ:4}', fiscalYearStartMonth: 1 } })))
        .toBe(role === 'Admin' ? 200 : 403)
    })
  })
}

test('AT-X-32 anonymous visitors are sent to sign in, and the projects API refuses them', async ({ page, request }) => {
  await page.goto('/projects')
  await expect(page).toHaveURL(/\/login/)
  expect((await request.get('/api/v1/projects')).status()).toBe(401)
})
