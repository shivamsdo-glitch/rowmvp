import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import { PAGE_KEYS, ROLES } from '@/api/auth'
import { NAV_SECTIONS } from '@/components/shell/nav'
import { canRead, contract, permissionRowsFor } from '@/test/contract'
import { fakeAuthFetch } from '@/test/fake-api'
import { renderApp } from '@/test/render-app'

beforeEach(() => sessionStorage.clear())

const ALL_NAV = NAV_SECTIONS.flatMap((s) => s.items)

describe('the contract with the backend', () => {
  it('knows exactly the same roles and pages as the API spec', () => {
    expect([...ROLES].sort()).toEqual([...contract.roles].sort())
    expect([...PAGE_KEYS].sort()).toEqual([...contract.pages].sort())
  })

  it('has a nav entry only for real pages', () => {
    for (const item of ALL_NAV) expect(PAGE_KEYS).toContain(item.page)
  })

  it('gives every role the dashboard, and only Admin the administration page', () => {
    for (const role of contract.roles) expect(canRead(role, 'dashboard')).toBe(true)
    expect(contract.roles.filter((r) => canRead(r, 'administration'))).toEqual(['Admin'])
  })
})

describe.each(ROLES.map((r) => [r]))('signing in as %s', (role) => {
  async function signIn() {
    const user = userEventModule.setup()
    const app = renderApp({ path: '/login', fetchImpl: fakeAuthFetch({ rows: permissionRowsFor(role), role }) as typeof fetch })
    await user.click(await screen.findByRole('button', { name: role }))
    await screen.findByRole('heading', { name: 'Dashboard' })
    return { user, ...app }
  }

  it('lands on the dashboard and tells them who they are', async () => {
    const { user } = await signIn()

    await user.click(screen.getByRole('button', { name: 'Account' }))

    expect(screen.getByText('Dev User')).toBeInTheDocument()
    expect(screen.getByText(role)).toBeInTheDocument()
  })

  it('sees exactly the navigation their role allows, with unbuilt screens shown as coming soon', async () => {
    await signIn()
    const nav = screen.getByRole('complementary', { name: 'Primary' })

    for (const item of ALL_NAV) {
      const allowed = canRead(role, item.page)
      const link = within(nav).queryByRole('link', { name: new RegExp(item.label.replace(/&/g, '\\&')) })
      const soon = within(nav).queryByTitle(`${item.label} (coming soon)`)

      if (!allowed) {
        expect(link, `${role} must not see ${item.label}`).toBeNull()
        expect(soon, `${role} must not see ${item.label}`).toBeNull()
      } else if (item.enabled === false) {
        expect(soon, `${role} sees ${item.label} as coming soon`).not.toBeNull()
        expect(link).toBeNull()
      } else {
        expect(link, `${role} can open ${item.label}`).not.toBeNull()
      }
    }
  })

  it(canRead(role, 'administration') ? 'can open Configuration' : 'is told they have no access to Configuration', async () => {
    const { router } = await signIn()

    await router.navigate({ to: '/administration' })

    if (canRead(role, 'administration')) {
      expect(await screen.findByRole('heading', { name: 'Administration' })).toBeInTheDocument()
    } else {
      expect(await screen.findByText(/don't have access/i)).toBeInTheDocument()
      expect(screen.queryByRole('heading', { name: 'Administration' })).not.toBeInTheDocument()
    }
  })

  it('can sign out and is then kept out of protected pages', async () => {
    const { user, router } = await signIn()

    await user.click(screen.getByRole('button', { name: 'Account' }))
    await user.click(screen.getByRole('button', { name: 'Log Out' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
    expect(sessionStorage.getItem('row.auth.token')).toBeNull()
    await router.navigate({ to: '/administration' })
    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
  })

  it('stays signed in across a page reload', async () => {
    renderApp({ path: '/', fetchImpl: fakeAuthFetch({ rows: permissionRowsFor(role), role }) as typeof fetch, signedIn: true })

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
  })
})
