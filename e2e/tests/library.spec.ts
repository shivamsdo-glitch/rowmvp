import { readFileSync } from 'node:fs'
import { expect, test, type Page } from '@playwright/test'
import { canRead, devToken, newId, ROLES, sessionToken, signInViaUi } from './support'

/**
 * The Library: fields the agency defines once (name, type, allowed values), with defaults attached. Real browser, real UI,
 * real API, real Postgres. Case ids match vault/acceptance/Administrator.md and Everyone.md. Tests share one database, so each
 * adds uniquely named values and fields and finds them by name.
 */

const unique = (label: string) => `${label} ${newId().slice(0, 8)}`
const fieldButton = (page: Page, name: string | RegExp) => page.getByRole('navigation', { name: 'Library fields' }).getByRole('button', { name })
const valuesTable = (page: Page, label: string) => page.getByRole('table', { name: `${label} values` })

async function openLibrary(page: Page) {
  await page.getByRole('complementary', { name: 'Primary' }).getByRole('link', { name: 'Library' }).click()
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Library')
}

test.describe('Administrator · library', () => {
  test('AT-AD-28 opens the Library with its defaults: fifteen-plus parcel statuses, possession and interest types, text fields', async ({ page }) => {
    await signInViaUi(page, 'Admin')
    await openLibrary(page)

    // The first field is the parcel status list, with Inactive as the default.
    const status = valuesTable(page, 'Parcel status')
    await expect(status.getByRole('row', { name: /^Inactive/ })).toContainText('Default')
    for (const name of ['In Design', 'Pre-Acquisition', 'Appraisal', 'Initial Offer', 'Final Offer', 'In Possession - PUA', 'In Possession - CLOSED', 'In Possession - NOD',
      'Pending Condemnation', 'Petition Filed', 'Hearing Scheduled', 'Objections Filed', 'Deleted', 'Disposition In Progress', 'Disposition Complete'])
      await expect(status.getByRole('cell', { name: name, exact: true })).toBeVisible()

    await fieldButton(page, /Possession type/).click()
    const possession = valuesTable(page, 'Possession type')
    for (const name of ['PUA', 'Closing', 'Notice of Deposit', 'Judgement']) await expect(possession.getByRole('cell', { name, exact: true })).toBeVisible()

    await fieldButton(page, /Interest type/).click()
    const interest = valuesTable(page, 'Interest type')
    for (const name of ['Partial Fee', 'Full Fee', 'Permanent Easement', 'Drainage Easement', 'Temporary Construction Easement'])
      await expect(interest.getByRole('cell', { name, exact: true })).toBeVisible()

    await fieldButton(page, /CAD number/).click()
    await expect(page.getByText(/people type it in on forms/)).toBeVisible()
    await expect(page.getByRole('button', { name: 'New', exact: true })).toHaveCount(0)
  })

  test('AT-AD-28 adds, renames, switches off, filters, imports and exports values', async ({ page }) => {
    await signInViaUi(page, 'Admin')
    await openLibrary(page)
    await fieldButton(page, /Agent type/).click()
    const table = valuesTable(page, 'Agent type')
    await expect(table).toBeVisible()
    const surveyor = unique('Surveyor')

    // Add: blank refused, then a value, then a duplicate (any case) refused.
    await page.getByRole('button', { name: 'New', exact: true }).click()
    const form = page.getByRole('form', { name: 'Add a value' })
    await form.getByRole('button', { name: 'Add value' }).click()
    await expect(page.getByText('Value is required.')).toBeVisible()
    await form.getByLabel('New value').fill(surveyor)
    await form.getByRole('button', { name: 'Add value' }).click()
    await expect(table.getByRole('row', { name: new RegExp(surveyor) })).toBeVisible()
    await page.getByRole('button', { name: 'New', exact: true }).click()
    await page.getByRole('form', { name: 'Add a value' }).getByLabel('New value').fill(surveyor.toUpperCase())
    await page.getByRole('form', { name: 'Add a value' }).getByRole('button', { name: 'Add value' }).click()
    await expect(page.getByText(`'${surveyor.toUpperCase()}' is already in Agent type.`)).toBeVisible()

    // Rename and switch off.
    await table.getByRole('button', { name: `Rename ${surveyor}` }).click()
    const renamed = `${surveyor} (land)`
    await table.getByLabel(`Value for ${surveyor}`).fill(renamed)
    await table.getByRole('button', { name: 'Save' }).click()
    await expect(table.getByRole('cell', { name: renamed, exact: true })).toBeVisible()
    await table.getByRole('checkbox', { name: `${renamed} is active` }).click()
    await expect(table.getByRole('checkbox', { name: `${renamed} is active` })).not.toBeChecked()

    // The switch stays after a reload (it is saved, not just shown).
    await page.reload()
    await fieldButton(page, /Agent type/).click()
    await expect(valuesTable(page, 'Agent type').getByRole('checkbox', { name: `${renamed} is active` })).not.toBeChecked()

    // Filter.
    await page.getByLabel('Filter Agent type').fill(renamed.slice(0, 12))
    await expect(valuesTable(page, 'Agent type').getByRole('row')).toHaveCount(2)
    await page.getByLabel('Filter Agent type').fill('zzzzzz')
    await expect(page.getByText('No value matches your filter.')).toBeVisible()
    await page.getByLabel('Filter Agent type').fill('')

    // Import a file: new values are added, existing ones (any case) are skipped.
    const a = unique('Imported A')
    const b = unique('Imported B')
    await page.getByLabel('File to import').setInputFiles({
      name: 'agents.csv', mimeType: 'text/csv', buffer: Buffer.from(`Agent type\r\n${a}\r\n"${b}"\r\nappraiser\r\n`),
    })
    await expect(page.getByText('2 added, 1 already there.')).toBeVisible()
    await expect(valuesTable(page, 'Agent type').getByRole('cell', { name: a, exact: true })).toBeVisible()
    await expect(valuesTable(page, 'Agent type').getByRole('cell', { name: b, exact: true })).toBeVisible()

    // Export: a CSV with every value and whether it is active.
    const download = page.waitForEvent('download')
    await page.getByRole('button', { name: /Export/ }).click()
    const file = await download
    expect(file.suggestedFilename()).toBe('agent_type.csv')
    const text = readFileSync((await file.path())!, 'utf8')
    expect(text).toContain('"Agent type","Is active"')
    expect(text).toContain(`"${renamed}",No`)
    expect(text).toContain(`"${a}",Yes`)
    expect(text).toContain('"Appraiser",Yes')
  })

  test('AT-AD-28 defines a field and changes it; a field the product uses keeps its type', async ({ page }) => {
    await signInViaUi(page, 'Admin')
    await openLibrary(page)
    const label = unique('Zone')

    await page.getByRole('button', { name: 'New field' }).click()
    const form = page.getByRole('form', { name: 'Define a field' })
    await form.getByRole('button', { name: 'Create field' }).click()
    await expect(form.getByText('Name is required.')).toBeVisible()
    await form.getByLabel(/^Field name/).fill('possession TYPE')
    await form.getByRole('button', { name: 'Create field' }).click()
    await expect(form.getByText("There is already a field named 'possession TYPE'.")).toBeVisible()
    await form.getByLabel(/^Field name/).fill(label)
    await form.getByRole('button', { name: 'Create field' }).click()

    const panel = page.getByRole('region', { name: label })
    await expect(panel).toBeVisible()
    await expect(panel.getByText('No values yet')).toBeVisible()
    await page.getByRole('button', { name: 'New', exact: true }).click()
    await page.getByRole('form', { name: 'Add a value' }).getByLabel('New value').fill('R1')
    await page.getByRole('form', { name: 'Add a value' }).getByRole('button', { name: 'Add value' }).click()
    await expect(valuesTable(page, label).getByRole('cell', { name: 'R1', exact: true })).toBeVisible()

    // With values it stays a list; renaming is fine.
    await page.getByRole('button', { name: 'Edit field' }).click()
    const edit = page.getByRole('form', { name: 'Edit field' })
    await edit.getByLabel(/^Field type/).selectOption('Number')
    await edit.getByRole('button', { name: 'Save field' }).click()
    await expect(edit).toContainText('already has values')
    await edit.getByLabel(/^Field type/).selectOption('List')
    await edit.getByLabel(/^Field name/).fill(`${label} renamed`)
    await edit.getByRole('button', { name: 'Save field' }).click()
    await expect(page.getByRole('region', { name: `${label} renamed` })).toBeVisible()
    await expect(fieldButton(page, new RegExp(`${label} renamed`))).toContainText('1/1')

    // A system field's type is locked.
    await fieldButton(page, /Segment/).click()
    await page.getByRole('button', { name: 'Edit field' }).click()
    await expect(page.getByRole('form', { name: 'Edit field' }).getByLabel(/^Field type/)).toBeDisabled()
  })

  test('AT-AD-28 the parcel status list is the Library list: a new value is a real status, and the default cannot be switched off', async ({ page, request }) => {
    await signInViaUi(page, 'Admin')
    const admin = { Authorization: `Bearer ${await sessionToken(page)}` }
    await openLibrary(page)
    const table = valuesTable(page, 'Parcel status')
    const name = unique('Staked')

    await page.getByRole('button', { name: 'New', exact: true }).click()
    await page.getByRole('form', { name: 'Add a value' }).getByLabel('New value').fill(name)
    await page.getByRole('form', { name: 'Add a value' }).getByRole('button', { name: 'Add value' }).click()
    await expect(table.getByRole('row', { name: new RegExp(name) })).toBeVisible()

    // It is a parcel status (neutral colour, active, not initial) and shows on Configuration too.
    const statuses = await (await request.get('/api/v1/parcel-rules/statuses', { headers: admin })).json()
    expect(statuses.find((s: any) => s.name === name)).toMatchObject({ isActive: true, isInitial: false, color: '#64748b' })
    await page.goto('/administration')
    await expect(page.getByRole('region', { name: 'Parcel statuses' }).getByRole('row', { name: new RegExp(name) })).toBeVisible()

    // The default cannot be switched off; the reason is shown and nothing changes.
    await openLibrary(page)
    await valuesTable(page, 'Parcel status').getByRole('checkbox', { name: 'Inactive is active' }).click()
    await expect(page.getByText('The initial status must be active.')).toBeVisible()
    await expect(valuesTable(page, 'Parcel status').getByRole('checkbox', { name: 'Inactive is active' })).toBeChecked()

    // A new parcel starts as Inactive.
    const project = await (await request.post('/api/v1/projects', { headers: admin, data: { name: unique('Library project'), oversightType: 'Local' } })).json()
    const parcel = await (await request.post('/api/v1/parcels', { headers: admin, data: { projectId: project.id, areaSqFt: 1, acquisitionType: 'Standard' } })).json()
    expect(parcel.statusName).toBe('Inactive')
  })

  test('AT-AD-28 the API refuses bad input cleanly and a second agency cannot reach this one', async ({ page, request }) => {
    await signInViaUi(page, 'Admin')
    const admin = { Authorization: `Bearer ${await sessionToken(page)}` }
    const fields = await (await request.get('/api/v1/library/fields', { headers: admin })).json()
    const agent = fields.find((f: any) => f.key === 'agent_type')
    const cad = fields.find((f: any) => f.key === 'cad_number')
    const post = (id: string, data: unknown) => request.post(`/api/v1/library/fields/${id}/values`, { headers: admin, data })

    expect((await post(agent.id, { value: '' })).status()).toBe(400)
    expect((await post(agent.id, { value: 'x'.repeat(101) })).status()).toBe(400)
    expect((await post(agent.id, { value: 'null\u0000char' })).status()).toBe(400)
    expect((await post(cad.id, { value: 'x' })).status()).toBe(400)
    expect((await request.post(`/api/v1/library/fields/${agent.id}/import`, { headers: admin, data: { values: [] } })).status()).toBe(400)
    expect((await request.post('/api/v1/library/fields', { headers: admin, data: { label: 'Odd', fieldType: 'Spreadsheet' } })).status()).toBe(400)
    const markup = unique('<b>bold</b>')
    expect((await post(agent.id, { value: markup })).status()).toBe(201)
    await page.goto('/library')
    await fieldButton(page, /Agent type/).click()
    await expect(valuesTable(page, 'Agent type').getByRole('cell', { name: markup, exact: true })).toBeVisible()

    const other = { Authorization: `Bearer ${await devToken(request, 'Admin', { tenant: newId() })}` }
    expect([403, 404]).toContain((await request.get(`/api/v1/library/fields/${agent.id}`, { headers: other })).status())
    expect([403, 404]).toContain((await request.post(`/api/v1/library/fields/${agent.id}/values`, { headers: other, data: { value: 'Intruder' } })).status())
  })
})

test.describe('Everyone · library', () => {
  for (const role of ROLES) {
    test(`AT-X-37 ${role}: the Library opens only for roles with Administration access, and the server agrees`, async ({ page, request }) => {
      await signInViaUi(page, role)
      const mine = { Authorization: `Bearer ${await sessionToken(page)}` }
      const link = page.getByRole('complementary', { name: 'Primary' }).getByRole('link', { name: 'Library' })
      const admin = { Authorization: `Bearer ${await devToken(request, 'Admin')}` }
      const agent = (await (await request.get('/api/v1/library/fields', { headers: admin })).json()).find((f: any) => f.key === 'agent_type')

      await page.goto('/library')
      if (canRead(role, 'administration')) {
        await expect(page.getByRole('heading', { level: 1 })).toHaveText('Library')
        await expect(link).toBeVisible()
        await expect(page.getByRole('button', { name: 'New field' })).toBeVisible()
      } else {
        await expect(page.getByText(/don't have access/i)).toBeVisible()
        await expect(link).toHaveCount(0)
      }
      const allowed = canRead(role, 'administration')
      const tag = `${role}-${newId().slice(0, 5)}`
      expect((await request.get('/api/v1/library/fields', { headers: mine })).status()).toBe(allowed ? 200 : 403)
      expect((await request.post(`/api/v1/library/fields/${agent.id}/values`, { headers: mine, data: { value: `Value ${tag}` } })).status()).toBe(allowed ? 201 : 403)
      expect((await request.post('/api/v1/library/fields', { headers: mine, data: { label: `Field ${tag}`, fieldType: 'Text' } })).status()).toBe(allowed ? 201 : 403)
      expect((await request.get('/api/v1/library/fields')).status()).toBe(401)
    })
  }
})
