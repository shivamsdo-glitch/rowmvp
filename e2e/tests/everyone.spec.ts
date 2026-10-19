import { expect, test } from '@playwright/test'
import { NAV, ROLES, canRead, devToken, documents, newId, openAccountMenu, sessionToken, signInViaUi, signOutViaUi } from './support'

/**
 * AT-X: what EVERY kind of user must be able to rely on, run once per role against the real browser,
 * API and database. See vault/acceptance/README.md.
 */
for (const role of ROLES) {
  test.describe(`AT-X · ${role}`, () => {
    test(`AT-X-01 ${role} signs in and sees who they are`, async ({ page, request }) => {
      await signInViaUi(page, role)

      await openAccountMenu(page)
      await expect(page.getByText(role, { exact: true })).toBeVisible()
      const me = await request.get('/api/v1/auth/me', { headers: { Authorization: `Bearer ${await sessionToken(page)}` } })
      expect((await me.json()).roles).toEqual([role])
    })

    test(`AT-X-02 ${role} sees exactly the navigation their role allows`, async ({ page }) => {
      await signInViaUi(page, role)
      const nav = page.getByRole('complementary', { name: 'Primary' })

      for (const item of NAV) {
        const link = nav.getByRole('link', { name: item.label })
        const soon = nav.getByTitle(`${item.label} (coming soon)`)
        if (!canRead(role, item.page)) {
          await expect(link).toHaveCount(0)
          await expect(soon).toHaveCount(0)
        } else if (item.built) {
          await expect(link).toBeVisible()
        } else {
          await expect(soon).toBeVisible()
          await expect(link).toHaveCount(0) // not clickable: a placeholder, never a dead link
        }
      }
    })

    test(`AT-X-03 ${role} opening the Configuration page directly gets in only if allowed`, async ({ page, request }) => {
      await signInViaUi(page, role)
      await page.goto('/administration')

      if (canRead(role, 'administration')) {
        await expect(page.getByRole('heading', { name: 'Administration' })).toBeVisible()
      } else {
        await expect(page.getByText(/don't have access/i)).toBeVisible()
        await expect(page.getByRole('heading', { name: 'Administration' })).toHaveCount(0)
      }
      const ping = await request.get('/api/v1/admin/ping', { headers: { Authorization: `Bearer ${await sessionToken(page)}` } })
      expect(ping.status()).toBe(canRead(role, 'administration') ? 200 : 403) // the server agrees with the screen
    })

    test(`AT-X-04 ${role} stays signed in on reload, and sign-out really ends the session`, async ({ page }) => {
      await signInViaUi(page, role)

      await page.reload()
      await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()

      await signOutViaUi(page)
      expect(await page.evaluate(() => sessionStorage.getItem('row.auth.token'))).toBeNull()
      await page.goto('/administration')
      await expect(page).toHaveURL(/\/login/)
      await page.goBack()
      await expect(page.getByRole('heading', { name: 'Dashboard' })).toHaveCount(0)
    })
  })
}

test.describe('AT-X · session and failure handling (any user)', () => {
  test('AT-X-05 a visitor who is not signed in is sent to sign in, then lands where they were going', async ({ page }) => {
    await page.goto('/administration')
    await expect(page).toHaveURL(/\/login\?redirect=%2Fadministration/)

    await page.getByRole('button', { name: 'Admin', exact: true }).click()

    await expect(page.getByRole('heading', { name: 'Administration' })).toBeVisible()
    await expect(page).toHaveURL(/\/administration$/)
  })

  test('AT-X-06 a stale or tampered session token just means "sign in again"', async ({ page }) => {
    await page.goto('/login')
    await page.evaluate(() => sessionStorage.setItem('row.auth.token', 'eyJhbGciOiJub25lIn0.e30.'))

    await page.goto('/')

    await expect(page.getByText(/Development sign-in/i)).toBeVisible()
    await expect(page.getByRole('button', { name: 'Admin', exact: true })).toBeEnabled()
  })

  test('AT-X-07 if the server cannot be reached the user is told plainly and can try again', async ({ page }) => {
    await page.route('**/api/v1/**', (route) => route.abort('connectionrefused'))
    await page.goto('/login')

    await page.getByRole('button', { name: 'Finance', exact: true }).click()

    await expect(page.getByRole('alert')).toContainText(/cannot reach the server/i)
    await expect(page.getByRole('button', { name: 'Finance', exact: true })).toBeEnabled()
  })

  test('AT-X-08 a session the server stops accepting sends the user back to sign in', async ({ page }) => {
    await signInViaUi(page, 'Finance')
    await page.route('**/api/v1/auth/permissions', (route) => route.fulfill({ status: 401, contentType: 'application/json', body: '{"title":"unauthorized"}' }))

    await page.reload()

    await expect(page).toHaveURL(/\/login/)
  })

  test('AT-X-09 the sidebar collapses to icons and expands again', async ({ page }) => {
    await signInViaUi(page, 'Admin')

    await page.getByRole('button', { name: 'Collapse sidebar' }).click()
    await expect(page.getByText('Acquisition', { exact: true })).toHaveCount(0)
    await expect(page.getByRole('complementary', { name: 'Primary' }).getByRole('link', { name: 'Home' })).toBeVisible()
    await page.getByRole('button', { name: 'Expand sidebar' }).click()
    await expect(page.getByText('Acquisition', { exact: true })).toBeVisible()
  })

  test('AT-X-10 the browser tab and top bar carry the product name', async ({ page }) => {
    await signInViaUi(page, 'Admin')

    await expect(page).toHaveTitle('Masterworks Right of Way Management')
    await expect(page.getByRole('link', { name: 'Masterworks Right of Way Management home' })).toBeVisible()
  })

  test('AT-X-11 a second agency can never see the first agency’s files, even as its Admin', async ({ request }) => {
    const admin = await devToken(request, 'Admin')
    const entity = newId()
    const up = await documents(request, admin).upload('Parcel', entity, 'agency-a-secret.pdf', 'A only')
    expect(up.status()).toBe(201)
    const id = (await up.json()).id as string

    const otherAdmin = await devToken(request, 'Admin', { tenant: newId() })
    const other = documents(request, otherAdmin)

    // An agency with no roles of its own is refused outright; one with roles sees an empty list. Never the other agency's file.
    const listed = await other.list('Parcel', entity)
    expect([200, 403]).toContain(listed.status())
    if (listed.status() === 200) expect(((await listed.json()) as unknown[]).length).toBe(0)
    expect((await other.download(id)).status()).toBe(404)
    expect((await other.remove(id)).status()).toBe(404)
    expect((await documents(request, admin).download(id)).status()).toBe(200)
  })

  test('AT-X-12 the shell fits a tablet-width screen without sideways scrolling', async ({ page }) => {
    await page.setViewportSize({ width: 820, height: 1180 })
    await signInViaUi(page, 'Admin')

    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
    expect(overflow).toBeLessThanOrEqual(0)
  })
})
