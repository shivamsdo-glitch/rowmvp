import { beforeEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import axe from 'axe-core'
import { ROLES } from '@/api/auth'
import { permissionRowsFor } from '@/test/contract'
import { fakeAuthFetch } from '@/test/fake-api'
import { renderApp } from '@/test/render-app'

beforeEach(() => sessionStorage.clear())

/** Colour contrast needs real layout, which jsdom does not have; every other automated rule is checked. */
async function violations() {
  const result = await axe.run(document.body, { rules: { 'color-contrast': { enabled: false } } })
  return result.violations.map((v) => `${v.id} (${v.impact}): ${v.help} -> ${v.nodes.map((n) => n.target.join(' ')).join(' | ')}`)
}

describe('accessibility (automated axe rules)', () => {
  it('the sign-in page has no violations', async () => {
    renderApp({ path: '/login', fetchImpl: fakeAuthFetch({ readable: [] }) as typeof fetch })
    await screen.findByText(/Development sign-in/i)

    expect(await violations()).toEqual([])
  })

  it.each(ROLES.map((r) => [r]))('the signed-in shell has no violations for %s', async (role) => {
    renderApp({ path: '/', fetchImpl: fakeAuthFetch({ rows: permissionRowsFor(role), role }) as typeof fetch, signedIn: true })
    await screen.findByRole('heading', { name: 'Dashboard' })

    expect(await violations()).toEqual([])
  })

  it('the open account menu has no violations', async () => {
    const user = userEventModule.setup()
    renderApp({ path: '/', fetchImpl: fakeAuthFetch({ readable: ['dashboard'] }) as typeof fetch, signedIn: true })
    await user.click(await screen.findByRole('button', { name: 'Account' }))

    expect(await violations()).toEqual([])
  })

  it('the collapsed sidebar has no violations and every control still has a name', async () => {
    const user = userEventModule.setup()
    renderApp({ path: '/', fetchImpl: fakeAuthFetch({ rows: permissionRowsFor('Admin'), role: 'Admin' }) as typeof fetch, signedIn: true })
    await user.click(await screen.findByRole('button', { name: 'Collapse sidebar' }))

    expect(await violations()).toEqual([])
  })

  it('the no-access page has no violations', async () => {
    renderApp({ path: '/administration', fetchImpl: fakeAuthFetch({ readable: ['dashboard'] }) as typeof fetch, signedIn: true })
    await screen.findByText(/don't have access/i)

    expect(await violations()).toEqual([])
  })
})
