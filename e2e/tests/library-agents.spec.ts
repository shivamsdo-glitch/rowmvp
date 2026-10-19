import { expect, test, type Page } from '@playwright/test'
import { newId, ROLES, sessionToken, signInViaUi } from './support'

/**
 * The Library's Agents list and the library-driven flag (Phase 1.5 slice 3, ADR 0015). Real browser, UI, API and Postgres. Case ids
 * match vault/acceptance/Administrator.md. Tests share one database, so every agent and field they create has a unique name.
 */

const unique = (label: string) => `${label} ${newId().slice(0, 8)}`

async function openAgents(page: Page) {
  await page.goto('/library')
  await page.getByRole('navigation', { name: 'Library fields' }).getByRole('button', { name: 'Agents' }).click()
  await expect(page.getByRole('region', { name: 'Agents' })).toBeVisible()
}

test.describe('Administrator · library agents', () => {
  test('AT-AD-38 adds an agent, finds them by job and by text, rejects a duplicate, and switches them off and on', async ({ page }) => {
    const name = unique('Casey E2E')
    await signInViaUi(page, 'Admin')
    await openAgents(page)

    await page.getByRole('button', { name: 'New agent' }).click()
    const form = page.getByRole('form', { name: 'Add an agent' })
    await form.getByRole('button', { name: 'Add agent' }).click()
    await expect(form.getByText('Agent name is required.')).toBeVisible()
    await expect(form.getByText('Agent type is required.')).toBeVisible()

    // The job list is the Agent type list, with the jobs the brief names.
    const jobs = await form.getByLabel(/^Agent type/).locator('option').allTextContents()
    for (const job of ['Row Designer', 'Surveyor', 'Title Researcher', 'Appraiser', 'Negotiator', 'Broker']) expect(jobs).toContain(job)

    await form.getByLabel(/^Agent name/).fill(name)
    await form.getByLabel(/^Agent type/).selectOption('Surveyor')
    await form.getByLabel(/^Company/).fill('E2E Survey Co')
    await form.getByLabel(/^Phone number/).fill('555-0142')
    await form.getByLabel(/^Email/).fill('casey.e2e@example.test')
    await form.getByRole('button', { name: 'Add agent' }).click()

    const table = page.getByRole('table', { name: 'Agents' })
    const row = table.getByRole('row', { name: new RegExp(name) })
    await expect(row).toContainText('Surveyor')
    await expect(row).toContainText('E2E Survey Co')
    await expect(row).toContainText('casey.e2e@example.test')

    // The same person, job and company again is refused.
    await page.getByRole('button', { name: 'New agent' }).click()
    const again = page.getByRole('form', { name: 'Add an agent' })
    await again.getByLabel(/^Agent name/).fill(name.toUpperCase())
    await again.getByLabel(/^Agent type/).selectOption('Surveyor')
    await again.getByLabel(/^Company/).fill('e2e survey co')
    await again.getByRole('button', { name: 'Add agent' }).click()
    await expect(again.getByRole('alert')).toContainText('is already listed as Surveyor')
    await again.getByRole('button', { name: 'Cancel' }).click()

    // Filters.
    await page.getByLabel('Filter agents').fill(name)
    await expect(table.getByRole('row')).toHaveCount(2)
    await page.getByLabel('Agent type filter').selectOption('Negotiator')
    await expect(table.getByRole('row', { name: new RegExp(name) })).toHaveCount(0)
    await page.getByLabel('Agent type filter').selectOption('')

    // Switch off, then on again; an edit keeps the job.
    const active = table.getByRole('checkbox', { name: `${name}, Surveyor is active` })
    await active.click() // the box follows the server, so it changes once the save returns
    await expect(active).not.toBeChecked()
    await page.reload()
    await page.getByRole('navigation', { name: 'Library fields' }).getByRole('button', { name: 'Agents' }).click()
    await page.getByLabel('Filter agents').fill(name)
    await expect(page.getByRole('table', { name: 'Agents' }).getByRole('checkbox', { name: `${name}, Surveyor is active` })).not.toBeChecked()
    await page.getByRole('table', { name: 'Agents' }).getByRole('checkbox', { name: `${name}, Surveyor is active` }).click()
    await expect(page.getByRole('table', { name: 'Agents' }).getByRole('checkbox', { name: `${name}, Surveyor is active` })).toBeChecked()
    await page.getByRole('button', { name: `Edit ${name}, Surveyor` }).click()
    const edit = page.getByRole('form', { name: 'Edit agent' })
    await edit.getByLabel(/^Phone number/).fill('555-0199')
    await edit.getByRole('button', { name: 'Save agent' }).click()
    await expect(page.getByRole('table', { name: 'Agents' }).getByRole('row', { name: new RegExp(name) })).toContainText('555-0199')
  })

  test('AT-AD-38 a new field asks whether it is library-driven: yes makes a dropdown, no offers the typed-in types', async ({ page }) => {
    const dropdown = unique('Hearing room')
    const typed = unique('Docket no')
    await signInViaUi(page, 'Admin')
    await page.goto('/library')

    await page.getByRole('button', { name: 'New field' }).click()
    let form = page.getByRole('form', { name: 'Define a field' })
    await expect(form.getByRole('checkbox', { name: /Library-driven/ })).toBeChecked()
    await expect(form.getByLabel('Field type')).toHaveCount(0)
    await form.getByLabel('Field name').fill(dropdown)
    await form.getByRole('button', { name: 'Create field' }).click()
    await expect(page.getByRole('region', { name: dropdown })).toContainText('Library-driven dropdown')

    await page.getByRole('button', { name: 'New field' }).click()
    form = page.getByRole('form', { name: 'Define a field' })
    await form.getByRole('checkbox', { name: /Library-driven/ }).uncheck()
    const types = await form.getByLabel('Field type').locator('option').allTextContents()
    expect(types).not.toContain('List')
    await form.getByLabel('Field name').fill(typed)
    await form.getByLabel('Field type').selectOption('Text')
    await form.getByRole('button', { name: 'Create field' }).click()
    await expect(page.getByRole('region', { name: typed })).toContainText('people type it in on forms')
  })

  test('AT-AD-38 the default lists the parcel forms need are there: owner, relocation and encumbrance types, and empty lists to fill', async ({ page }) => {
    await signInViaUi(page, 'Admin')
    await page.goto('/library')
    const nav = page.getByRole('navigation', { name: 'Library fields' })

    await nav.getByRole('button', { name: /Owner type/ }).click()
    const owners = page.getByRole('table', { name: 'Owner type values' })
    for (const v of ['CAD Owner', 'Commercial Sign Owner', 'Fee Owner', 'Prior Owner', 'Vested Owner']) await expect(owners.getByRole('cell', { name: v, exact: true })).toBeVisible()
    await nav.getByRole('button', { name: /Relocation type/ }).click()
    const relocation = page.getByRole('table', { name: 'Relocation type values' })
    for (const v of ['Residential', 'Non-residential', 'Personal Property only']) await expect(relocation.getByRole('cell', { name: v, exact: true })).toBeVisible()
    await nav.getByRole('button', { name: /Encumbrance type/ }).click()
    await expect(page.getByRole('table', { name: 'Encumbrance type values' }).getByRole('cell', { name: 'Mortgage Lien', exact: true })).toBeVisible()
    await nav.getByRole('button', { name: /Appraisal type/ }).click()
    await expect(page.getByText('No values yet')).toBeVisible()
  })
})

test.describe('Everyone · library agents', () => {
  for (const role of ROLES) {
    test(`AT-AD-38 ${role}: reads the agents; add, edit and switch-off controls and the API follow the Library permission`, async ({ page, request }) => {
      await signInViaUi(page, role)
      await openAgents(page)
      const mayChange = role === 'Admin'

      await expect(page.getByRole('table', { name: 'Agents' }).or(page.getByText('No agents yet'))).toBeVisible()
      await expect(page.getByRole('button', { name: 'New agent' })).toHaveCount(mayChange ? 1 : 0)
      if (!mayChange) await expect(page.getByRole('button', { name: /^Edit / })).toHaveCount(0)

      const mine = { Authorization: `Bearer ${await sessionToken(page)}` }
      expect((await request.get('/api/v1/library/agents', { headers: mine })).status()).toBe(200)
      const post = await request.post('/api/v1/library/agents', { headers: mine, data: { name: unique(`${role} agent`), agentType: 'Broker', company: null, phone: null, email: null } })
      expect(post.status()).toBe(mayChange ? 201 : 403)
      expect((await request.get('/api/v1/library/agents')).status()).toBe(401)
    })
  }
})
