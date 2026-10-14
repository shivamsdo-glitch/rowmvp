import { beforeEach, describe, expect, it } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createMemoryHistory, RouterProvider } from '@tanstack/react-router'
import { AuthProvider } from '@/auth/auth-context'
import { AuthManager } from '@/auth/auth-manager'
import { fakeAuthFetch } from '@/test/fake-api'
import { createAppRouter } from '@/router'

beforeEach(() => sessionStorage.clear())

export function mount(path: string, readable: string[], role = 'Admin') {
  const auth = new AuthManager({ fetchImpl: fakeAuthFetch({ readable, role }) as typeof fetch })
  const router = createAppRouter(auth, createMemoryHistory({ initialEntries: [path] }))
  render(
    <QueryClientProvider client={new QueryClient()}>
      <AuthProvider auth={auth}>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>,
  )
  return { auth, router }
}

describe('route guards', () => {
  it('sends a signed-out visitor to the login page and remembers where they were going', async () => {
    const { router } = mount('/administration', ['dashboard', 'administration'])

    expect(await screen.findByText(/Development sign-in/i)).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/login')
    expect(router.state.location.search).toMatchObject({ redirect: '/administration' })
  })

  it('signs in as a role, lands on the dashboard, and shows only the pages that role can read', async () => {
    const user = userEventModule.setup()
    mount('/login', ['dashboard'], 'Finance')

    await user.click(await screen.findByRole('button', { name: 'Finance' }))

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Home/ })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Configuration/ })).not.toBeInTheDocument()
  })

  it('shows the forbidden screen, not a blank page, when the role cannot read the page', async () => {
    sessionStorage.setItem('row.auth.token', 'good-token')
    mount('/administration', ['dashboard'])

    expect(await screen.findByText(/don't have access/i)).toBeInTheDocument()
  })

  it('lets a role with access open the page and shows the nav link as active', async () => {
    sessionStorage.setItem('row.auth.token', 'good-token')
    mount('/administration', ['dashboard', 'administration'])

    expect(await screen.findByRole('heading', { name: 'Administration' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Configuration/ })).toHaveAttribute('aria-current', 'page')
  })

  it('signing out from the account menu returns to login and blocks protected pages again', async () => {
    const user = userEventModule.setup()
    sessionStorage.setItem('row.auth.token', 'good-token')
    const { router } = mount('/', ['dashboard'])

    await user.click(await screen.findByRole('button', { name: 'Account' }))
    expect(screen.getByText('Dev User')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Log Out' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
    await router.navigate({ to: '/' })
    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
  })

  it('ignores an off-site redirect target on the login page', async () => {
    const { router } = mount('/login?redirect=//evil.example.com', [])

    await screen.findByText(/Development sign-in/i)
    expect(router.state.location.search).toEqual({})
  })
})
