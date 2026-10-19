import { expect, test } from '@playwright/test'

/** The eleven named sign-ins from "Next requirements" (Landing page), all with the shared demo password. */
const LOGINS: [username: string, role: string][] = [
  ['rowprojectmanager', 'ProjectManager'],
  ['rowdesign', 'Designer'],
  ['rowdesignapprover', 'DesignApprover'],
  ['surveyor', 'Surveyor'],
  ['titleresearcher', 'TitleResearcher'],
  ['appraiser', 'Appraiser'],
  ['appraisalapprover', 'AppraisalApprover'],
  ['rownegotiator', 'Negotiator'],
  ['relocationagent', 'RelocationAgent'],
  ['rowfinance', 'Finance'],
  ['admin', 'Admin'],
]
const PASSWORD = 'qwertyuiop'

test.describe('AT-X · the Masterworks landing page and the named sign-ins', () => {
  test('AT-X-42 the landing page is the Masterworks Right of Way Management page with username and password', async ({ page }) => {
    await page.goto('/login')

    await expect(page).toHaveTitle('Masterworks Right of Way Management')
    await expect(page.getByRole('heading', { name: 'Masterworks Right of Way Management' })).toBeVisible()
    await expect(page.getByLabel('Username')).toBeVisible()
    await expect(page.getByLabel('Password')).toHaveAttribute('type', 'password')
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible()
  })

  for (const [username, role] of LOGINS) {
    test(`AT-X-43 ${username} signs in with the shared password and is the ${role}`, async ({ page }) => {
      await page.goto('/login')
      await page.getByLabel('Username').fill(username)
      await page.getByLabel('Password').fill(PASSWORD)
      await page.getByRole('button', { name: 'Sign in' }).click()

      await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
      await page.getByRole('button', { name: 'Account' }).click()
      await expect(page.getByText(role, { exact: true })).toBeVisible()
    })
  }

  test('AT-X-44 a wrong password or an unknown username is refused with the same message', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Username').fill('admin')
    await page.getByLabel('Password').fill('not-the-password')
    await page.getByRole('button', { name: 'Sign in' }).click()
    await expect(page.getByRole('alert')).toHaveText('Incorrect username or password.')

    await page.getByLabel('Username').fill('nobody')
    await page.getByLabel('Password').fill(PASSWORD)
    await page.getByRole('button', { name: 'Sign in' }).click()
    await expect(page.getByRole('alert')).toHaveText('Incorrect username or password.')
    await expect(page).toHaveURL(/\/login/)
  })

  test('AT-X-45 both fields are required, and the username is not case sensitive', async ({ page }) => {
    await page.goto('/login')
    await page.getByRole('button', { name: 'Sign in' }).click()
    await expect(page.getByRole('alert')).toHaveText('Enter your username and password.')

    await page.getByLabel('Username').fill('RowFinance')
    await page.getByLabel('Password').fill(PASSWORD)
    await page.getByRole('button', { name: 'Sign in' }).click()
    await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
  })
})
