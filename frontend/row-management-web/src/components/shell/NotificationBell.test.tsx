import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createMemoryHistory, RouterProvider } from '@tanstack/react-router'
import { AuthProvider } from '@/auth/auth-context'
import { AuthManager } from '@/auth/auth-manager'
import { FEATURES } from '@/config/features'
import { createAppRouter } from '@/router'
import { fakeAuthFetch } from '@/test/fake-api'

const note = (over: object) => ({
  id: 'n1', kind: 'info', title: 'Offer approved', body: 'Parcel 12 offer was approved.',
  link: '/administration', isRead: false, createdAt: new Date().toISOString(), ...over,
})

function setup(notifications: object[]) {
  const calls: string[] = []
  const base = fakeAuthFetch({ readable: ['dashboard', 'administration'] })
  const fetchImpl = async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url.startsWith('/api/v1/notifications')) {
      calls.push(`${init?.method ?? 'GET'} ${url}`)
      return url === '/api/v1/notifications'
        ? new Response(JSON.stringify(notifications), { status: 200 })
        : new Response(null, { status: 204 })
    }
    return base(input, init)
  }
  sessionStorage.setItem('row.auth.token', 'good-token')
  const auth = new AuthManager({ fetchImpl: fetchImpl as typeof fetch })
  const router = createAppRouter(auth, createMemoryHistory({ initialEntries: ['/'] }))
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <AuthProvider auth={auth}>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>,
  )
  return { calls, router }
}

describe('NotificationBell (module enabled)', () => {
  beforeEach(() => {
    sessionStorage.clear()
    FEATURES.notifications = true
  })
  afterEach(() => {
    FEATURES.notifications = false
  })

  it('shows the unread count and the list', async () => {
    const user = userEventModule.setup()
    setup([note({}), note({ id: 'n2', title: 'Old', isRead: true })])

    const bell = await screen.findByRole('button', { name: 'Notifications' })
    await waitFor(() => expect(bell).toHaveTextContent('1'))
    await user.click(bell)

    expect(screen.getByText('Offer approved')).toBeInTheDocument()
    expect(screen.getByText('Old')).toBeInTheDocument()
  })

  it('says everything is read when there is nothing', async () => {
    const user = userEventModule.setup()
    setup([])

    await user.click(await screen.findByRole('button', { name: 'Notifications' }))

    expect(await screen.findByText(/all caught up/i)).toBeInTheDocument()
  })

  it('marks one read and follows its in-app link', async () => {
    const user = userEventModule.setup()
    const { calls, router } = setup([note({})])

    await user.click(await screen.findByRole('button', { name: 'Notifications' }))
    await user.click(await screen.findByText('Offer approved'))

    await waitFor(() => expect(calls).toContain('POST /api/v1/notifications/n1/read'))
    await waitFor(() => expect(router.state.location.pathname).toBe('/administration'))
  })

  it('never navigates off-site from a notification link', async () => {
    const user = userEventModule.setup()
    const { router } = setup([note({ link: '//evil.example.com' })])

    await user.click(await screen.findByRole('button', { name: 'Notifications' }))
    await user.click(await screen.findByText('Offer approved'))

    await waitFor(() => expect(screen.queryByText('Offer approved')).not.toBeInTheDocument())
    expect(router.state.location.pathname).toBe('/')
  })

  it('marks everything read', async () => {
    const user = userEventModule.setup()
    const { calls } = setup([note({}), note({ id: 'n2' })])

    await user.click(await screen.findByRole('button', { name: 'Notifications' }))
    await user.click(await screen.findByRole('button', { name: 'Mark all read' }))

    await waitFor(() => expect(calls).toContain('POST /api/v1/notifications/read-all'))
  })

  it('makes no notification requests while the module is disabled', async () => {
    FEATURES.notifications = false
    const spy = vi.fn()
    const { calls } = setup([])
    await screen.findByRole('button', { name: 'Notifications' })
    spy(calls.length)

    expect(spy).toHaveBeenCalledWith(0)
  })
})
