import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import { ApiError } from '@/api/client'
import { fakeAuthFetch } from '@/test/fake-api'
import { renderApp } from '@/test/render-app'
import { AuthManager } from './auth-manager'

beforeEach(() => sessionStorage.clear())
afterEach(() => vi.restoreAllMocks())

const ok = fakeAuthFetch({ readable: ['dashboard', 'administration'] }) as typeof fetch

describe('a session that ends while the app is open', () => {
  it('returns to the login page with a clear message and remembers where the user was', async () => {
    let rejectAll = false
    const flaky = (async (input: RequestInfo | URL, init?: RequestInit) =>
      rejectAll && String(input).includes('permissions') ? new Response('{"title":"unauthorized"}', { status: 401 }) : ok(input, init)) as typeof fetch
    const { auth, router } = renderApp({ path: '/administration', fetchImpl: flaky, signedIn: true })
    await screen.findByRole('heading', { name: 'Administration' })

    rejectAll = true
    await auth.api.get('/api/v1/auth/permissions').catch(() => {}) // any call the server now rejects

    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
    expect(await screen.findByText(/session has expired/i)).toBeInTheDocument()
    expect(router.state.location.search).toMatchObject({ redirect: '/administration', expired: true })
  })

  it('after signing in again, lands back on the page they were on', async () => {
    const user = userEventModule.setup()
    const { router } = renderApp({ path: '/login?redirect=/administration&expired=true', fetchImpl: ok })

    await user.click(await screen.findByRole('button', { name: 'Admin' }))

    expect(await screen.findByRole('heading', { name: 'Administration' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/administration')
  })
})

describe('storage and token problems', () => {
  it('a garbage token left in storage just means "signed out", not a crash', async () => {
    sessionStorage.setItem('row.auth.token', 'not-a-real-token')
    renderApp({ path: '/', fetchImpl: ok })

    expect(await screen.findByText(/Development sign-in/i)).toBeInTheDocument()
    expect(sessionStorage.getItem('row.auth.token')).toBeNull()
  })

  it('works when the browser blocks storage entirely (private mode, policy)', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked') })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked') })
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => { throw new Error('blocked') })
    const user = userEventModule.setup()
    renderApp({ path: '/login', fetchImpl: ok })

    await user.click(await screen.findByRole('button', { name: 'Admin' }))

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
  })

  it('keeps the stored token when the server is merely unreachable during a reload', async () => {
    sessionStorage.setItem('row.auth.token', 'good-token')
    const down = (async () => { throw new TypeError('Failed to fetch') }) as typeof fetch
    const auth = new AuthManager({ fetchImpl: down })

    await auth.restore()

    expect(auth.getState().status).toBe('anonymous')
    expect(sessionStorage.getItem('row.auth.token')).toBe('good-token')
  })

  it('a dev token whose user row does not exist still signs in using the token itself', async () => {
    const user = userEventModule.setup()
    renderApp({ path: '/login', fetchImpl: fakeAuthFetch({ readable: ['dashboard'], meFails: true }) as typeof fetch })

    await user.click(await screen.findByRole('button', { name: 'Admin' }))

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
  })
})

describe('the login screen when things go wrong', () => {
  it('explains that dev sign-in is unavailable when the API says 404', async () => {
    const notDev = (async () => new Response('{}', { status: 404 })) as typeof fetch
    const user = userEventModule.setup()
    renderApp({ path: '/login', fetchImpl: notDev })

    await user.click(await screen.findByRole('button', { name: 'Admin' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/only available when the API runs in Development/i)
    expect(screen.getByRole('button', { name: 'Admin' })).toBeEnabled() // can try again
  })

  it('says the server cannot be reached rather than showing a raw error', async () => {
    const down = (async () => { throw new TypeError('Failed to fetch') }) as typeof fetch
    const user = userEventModule.setup()
    renderApp({ path: '/login', fetchImpl: down })

    await user.click(await screen.findByRole('button', { name: 'Admin' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/cannot reach the server/i)
    expect(screen.getByRole('alert')).not.toHaveTextContent(/TypeError|Failed to fetch/)
  })

  it('disables every role button while signing in so a double click cannot sign in twice', async () => {
    let release!: () => void
    const gate = new Promise<void>((r) => (release = r))
    const slow = (async (input: RequestInfo | URL, init?: RequestInit) => {
      if (String(input).includes('dev-token')) await gate
      return ok(input, init)
    }) as typeof fetch
    const user = userEventModule.setup()
    renderApp({ path: '/login', fetchImpl: slow })

    await user.click(await screen.findByRole('button', { name: 'Admin' }))

    for (const b of screen.getAllByRole('button')) expect(b).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Signing in…' })).toBeInTheDocument()
    release()
    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
  })

  it('a signed-in visitor opening the login page is sent straight to the dashboard', async () => {
    const { router } = renderApp({ path: '/login', fetchImpl: ok, signedIn: true })

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/')
  })

  it.each(['//evil.example.com', 'https://evil.example.com', 'javascript:alert(1)', '\\\\evil'])(
    'never redirects to %s after sign-in', async (target) => {
      const user = userEventModule.setup()
      const { router } = renderApp({ path: `/login?redirect=${encodeURIComponent(target)}`, fetchImpl: ok })

      await user.click(await screen.findByRole('button', { name: 'Admin' }))

      await screen.findByRole('heading', { name: 'Dashboard' })
      expect(router.state.location.pathname).toBe('/')
    })
})

describe('API failures', () => {
  it('a hung server becomes a timeout error instead of waiting forever', async () => {
    const hang = ((_: RequestInfo | URL, init?: RequestInit) =>
      new Promise((_resolve, reject) => init?.signal?.addEventListener('abort', () => reject(init.signal!.reason)))) as typeof fetch
    const auth = new AuthManager({ fetchImpl: hang })
    // shorten the timeout for the test through a dedicated client
    const { createApiClient } = await import('@/api/client')
    const api = createApiClient({ getToken: () => null, fetchImpl: hang, timeoutMs: 30 })

    const err = (await api.get('/x').catch((e: unknown) => e)) as ApiError

    expect(err).toBeInstanceOf(ApiError)
    expect(err.code).toBe('timeout')
    expect(auth.getState().status).toBe('anonymous')
  })
})
