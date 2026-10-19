import { expect, test, type Page } from '@playwright/test'
import { canRead, newId, ROLES, signInViaUi, signOutViaUi } from './support'

/**
 * Settings: Users & Roles and the Permissions grid (Phase 1.5 slice 2, ADR 0014). Real browser, UI, API and Postgres.
 * Case ids match vault/acceptance/Administrator.md and Everyone.md. Tests share one database, so every role and user
 * they create has a unique name.
 */

const unique = (label: string) => `${label} ${newId().slice(0, 8)}`
const slug = () => `e2e${newId().replace(/-/g, '').slice(0, 10)}`
const PASSWORD = 'Passw0rd-e2e-1'

async function openSettings(page: Page) {
  await page.getByRole('complementary', { name: 'Primary' }).getByRole('link', { name: 'Settings' }).click()
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Settings')
}

async function createRole(page: Page, name: string) {
  await page.getByRole('button', { name: 'New role' }).click()
  const form = page.getByRole('form', { name: 'Create a role' })
  await form.getByLabel(/^Role name/).fill(name)
  await form.getByLabel(/^Description/).fill('Created by an end-to-end test')
  await form.getByRole('button', { name: 'Create role' }).click()
  await expect(page.getByRole('table', { name: 'Roles' }).getByRole('row', { name: new RegExp(name) })).toContainText('Custom')
}

async function createUser(page: Page, username: string, role: string) {
  await page.getByRole('button', { name: 'New user' }).click()
  const form = page.getByRole('form', { name: 'Create a user' })
  await form.getByLabel(/^Username/).fill(username)
  await form.getByLabel(/^First name/).fill('End')
  await form.getByLabel(/^Last name/).fill('ToEnd')
  await form.getByLabel(/^Email/).fill(`${username}@example.test`)
  await form.getByLabel(/^Password/).fill(PASSWORD)
  await form.getByLabel(/^Role/).selectOption({ label: role })
  await form.getByRole('button', { name: 'Create user' }).click()
  await expect(page.getByRole('table', { name: 'Users' }).getByRole('row', { name: new RegExp(username) })).toContainText(role)
}

async function signInWithPassword(page: Page, username: string, password: string) {
  await page.goto('/login')
  await page.getByLabel('Username').fill(username)
  await page.getByLabel('Password').fill(password)
  await page.getByRole('button', { name: 'Sign in' }).click()
}

for (const role of ROLES) {
  test.describe(`${role} · settings access`, () => {
    test(`AT-AD-30 ${role} ${canRead(role, 'administration') ? 'opens' : 'is kept out of'} Settings, and the server agrees`, async ({ page, request }) => {
      await signInViaUi(page, role)
      const nav = page.getByRole('complementary', { name: 'Primary' })
      if (role === 'Admin') {
        await expect(nav.getByRole('link', { name: 'Settings' })).toBeVisible()
        await openSettings(page)
        await expect(page.getByRole('tab', { name: 'Users & Roles' })).toBeVisible()
        await expect(page.getByRole('tab', { name: 'Permissions' })).toBeVisible()
      } else {
        await expect(nav.getByRole('link', { name: 'Settings' })).toHaveCount(0)
        await page.goto('/settings')
        await expect(page.getByText("You don't have access to that page")).toBeVisible()
      }

      const token = await page.evaluate(() => sessionStorage.getItem('row.auth.token'))
      const headers = { Authorization: `Bearer ${token}` }
      expect((await request.get('/api/v1/roles', { headers })).status()).toBe(role === 'Admin' ? 200 : 403)
      expect((await request.get('/api/v1/users', { headers })).status()).toBe(role === 'Admin' ? 200 : 403)
    })
  })
}

test.describe('Administrator · settings', () => {
  test('AT-AD-30 a signed-out visitor is sent to sign in', async ({ page, request }) => {
    await page.goto('/settings')
    await expect(page).toHaveURL(/\/login\?redirect=%2Fsettings/)
    expect((await request.get('/api/v1/roles')).status()).toBe(401)
  })

  test('AT-AD-31 the roles table lists the eleven default roles; Admin is locked and built-in roles cannot be deleted', async ({ page }) => {
    await signInViaUi(page, 'Admin')
    await openSettings(page)

    const roles = page.getByRole('table', { name: 'Roles' })
    for (const r of ['ProjectManager', 'Designer', 'DesignApprover', 'Surveyor', 'TitleResearcher', 'Appraiser', 'AppraisalApprover', 'Negotiator', 'RelocationAgent', 'Finance', 'Admin'])
      await expect(roles.getByRole('cell', { name: r, exact: true })).toBeVisible()
    await expect(roles.getByRole('row', { name: /^Admin/ })).toContainText('Locked')
    await expect(roles.getByRole('button', { name: 'Edit role Admin' })).toHaveCount(0)
    await expect(roles.getByRole('button', { name: /Delete role Finance/ })).toHaveCount(0)
    const users = page.getByRole('table', { name: 'Users' })
    for (const u of ['rowprojectmanager', 'rowdesign', 'rowdesignapprover', 'surveyor', 'titleresearcher', 'appraiser', 'appraisalapprover', 'rownegotiator', 'relocationagent', 'rowfinance', 'admin'])
      await expect(users.getByRole('cell', { name: u, exact: true })).toBeVisible()
  })

  test('AT-AD-32 an administrator makes a role, grants it pages in the grid, adds a person, and that person sees exactly that', async ({ page, browser }) => {
    const roleName = unique('E2E Reviewer')
    const username = slug()
    await signInViaUi(page, 'Admin')
    await openSettings(page)
    await createRole(page, roleName)
    await createUser(page, username, roleName)

    await page.getByRole('tab', { name: 'Permissions' }).click()
    const grid = page.getByRole('table', { name: 'Permissions by role and module' })
    // Starts with no access at all.
    await expect(grid.getByRole('checkbox', { name: `${roleName} Read Parcels` })).not.toBeChecked()
    await grid.getByRole('checkbox', { name: `${roleName} Read Dashboard` }).check()
    await grid.getByRole('checkbox', { name: `${roleName} Update Parcels` }).check()
    await expect(grid.getByRole('checkbox', { name: `${roleName} Read Parcels` })).toBeChecked() // update implies read
    await page.getByRole('button', { name: 'Save permissions' }).click()
    await expect(page.getByRole('status')).toContainText('Saved 1 role')

    // The grid keeps what was saved after a reload.
    await page.reload()
    await page.getByRole('tab', { name: 'Permissions' }).click()
    await expect(page.getByRole('table', { name: 'Permissions by role and module' }).getByRole('checkbox', { name: `${roleName} Update Parcels` })).toBeChecked()

    // The new person, in their own browser, signs in with their username and sees only what the role grants.
    const other = await browser.newPage()
    await signInWithPassword(other, username, PASSWORD)
    await expect(other.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
    const nav = other.getByRole('complementary', { name: 'Primary' })
    await expect(nav.getByRole('link', { name: 'Parcels' })).toBeVisible()
    await expect(nav.getByRole('link', { name: 'Projects' })).toHaveCount(0)
    await expect(nav.getByRole('link', { name: 'Settings' })).toHaveCount(0)
    await other.goto('/settings')
    await expect(other.getByText("You don't have access to that page")).toBeVisible()
    await other.close()
  })

  test('AT-AD-33 the Admin row of the grid is locked at full access', async ({ page }) => {
    await signInViaUi(page, 'Admin')
    await openSettings(page)
    await page.getByRole('tab', { name: 'Permissions' }).click()

    const grid = page.getByRole('table', { name: 'Permissions by role and module' })
    for (const name of ['Admin Create Projects', 'Admin Delete Administration']) {
      await expect(grid.getByRole('checkbox', { name })).toBeChecked()
      await expect(grid.getByRole('checkbox', { name })).toBeDisabled()
    }
    // The default roles show the approved defaults.
    await expect(grid.getByRole('checkbox', { name: 'Surveyor Delete Survey' })).toBeChecked()
    await expect(grid.getByRole('checkbox', { name: 'Surveyor Create Projects' })).not.toBeChecked()
    await expect(grid.getByRole('checkbox', { name: 'Finance Delete Payments' })).toBeChecked()
  })

  test('AT-AD-34 a custom role with people in it cannot be deleted; moving them out lets it go', async ({ page }) => {
    const roleName = unique('E2E Temp')
    const username = slug()
    await signInViaUi(page, 'Admin')
    await openSettings(page)
    await createRole(page, roleName)
    await createUser(page, username, roleName)

    await page.getByRole('button', { name: `Delete role ${roleName}` }).click()
    await expect(page.getByRole('alert').filter({ hasText: 'still has users' })).toBeVisible()

    await page.getByRole('button', { name: `Edit ${username}` }).click()
    const form = page.getByRole('form', { name: 'Edit user' })
    await form.getByLabel(/^Role/).selectOption({ label: 'Finance' })
    await form.getByRole('button', { name: 'Save user' }).click()
    await expect(page.getByRole('table', { name: 'Users' }).getByRole('row', { name: new RegExp(username) })).toContainText('Finance')

    await page.getByRole('button', { name: `Delete role ${roleName}` }).click()
    await expect(page.getByRole('table', { name: 'Roles' }).getByRole('row', { name: new RegExp(roleName) })).toHaveCount(0)
  })

  test('AT-AD-35 a taken username and a weak password are refused on the form', async ({ page }) => {
    await signInViaUi(page, 'Admin')
    await openSettings(page)
    await page.getByRole('button', { name: 'New user' }).click()
    const form = page.getByRole('form', { name: 'Create a user' })
    await form.getByRole('button', { name: 'Create user' }).click()
    await expect(form.getByText('Username is required.')).toBeVisible()
    await expect(form.getByText('Password is required.')).toBeVisible()

    await form.getByLabel(/^Username/).fill('admin')
    await form.getByLabel(/^First name/).fill('A')
    await form.getByLabel(/^Last name/).fill('B')
    await form.getByLabel(/^Email/).fill('dupe@example.test')
    await form.getByLabel(/^Role/).selectOption({ label: 'Finance' })
    await form.getByLabel(/^Password/).fill('Passw0rd-e2e-1')
    await form.getByRole('button', { name: 'Create user' }).click()
    await expect(form.getByRole('alert')).toHaveText("The username 'admin' is taken.")

    await form.getByLabel(/^Username/).fill(slug())
    await form.getByLabel(/^Password/).fill('short')
    await form.getByRole('button', { name: 'Create user' }).click()
    await expect(form.getByText('Password must be at least 8 characters.')).toBeVisible()
  })

  test('AT-AD-36 deactivating a person stops them signing in; a new password replaces the old one', async ({ page, browser }) => {
    const username = slug()
    await signInViaUi(page, 'Admin')
    await openSettings(page)
    await createUser(page, username, 'Finance')

    await page.getByRole('button', { name: `Edit ${username}` }).click()
    let form = page.getByRole('form', { name: 'Edit user' })
    await form.getByLabel(/New password/).fill('Replaced-Pass-2')
    await form.getByRole('button', { name: 'Save user' }).click()
    await expect(form).toHaveCount(0)

    const other = await browser.newPage()
    await signInWithPassword(other, username, PASSWORD)
    await expect(other.getByRole('alert')).toHaveText('Incorrect username or password.')
    await signInWithPassword(other, username, 'Replaced-Pass-2')
    await expect(other.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
    await signOutViaUi(other)

    await page.getByRole('button', { name: `Edit ${username}` }).click()
    form = page.getByRole('form', { name: 'Edit user' })
    await form.getByLabel('Active').uncheck()
    await form.getByRole('button', { name: 'Save user' }).click()
    await expect(page.getByRole('table', { name: 'Users' }).getByRole('row', { name: new RegExp(username) })).toContainText('Inactive')

    await signInWithPassword(other, username, 'Replaced-Pass-2')
    await expect(other.getByRole('alert')).toContainText('locked or inactive')
    await other.close()
  })
})
