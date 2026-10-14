import { beforeEach, describe, expect, it } from 'vitest'
import { fakeAuthFetch } from '@/test/fake-api'
import { AuthManager } from './auth-manager'

beforeEach(() => sessionStorage.clear())

describe('AuthManager', () => {
  it('starts anonymous', () => {
    const auth = new AuthManager({ fetchImpl: fakeAuthFetch({ readable: [] }) as typeof fetch })

    expect(auth.getState().status).toBe('anonymous')
    expect(auth.can('dashboard')).toBe(false)
  })

  it('signs in as a dev role and loads the user and permissions', async () => {
    const auth = new AuthManager({ fetchImpl: fakeAuthFetch({ readable: ['dashboard', 'title'], role: 'TitleExaminer' }) as typeof fetch })

    await auth.signInAsDevRole('TitleExaminer')

    expect(auth.getState().status).toBe('authenticated')
    expect(auth.getState().user?.fullName).toBe('Dev User')
    expect(auth.hasRole('TitleExaminer')).toBe(true)
    expect(auth.hasRole('Admin')).toBe(false)
    expect(auth.can('title')).toBe(true)
    expect(auth.can('payments')).toBe(false)
    expect(auth.can('title', 'delete')).toBe(false)
  })

  it('notifies subscribers on sign-in and sign-out', async () => {
    const auth = new AuthManager({ fetchImpl: fakeAuthFetch({ readable: ['dashboard'] }) as typeof fetch })
    let calls = 0
    auth.subscribe(() => calls++)

    await auth.signInAsDevRole('Admin')
    auth.signOut()

    expect(calls).toBe(2)
    expect(auth.getState().status).toBe('anonymous')
    expect(sessionStorage.getItem('row.auth.token')).toBeNull()
  })

  it('restores a stored session on a fresh page load', async () => {
    const fetchImpl = fakeAuthFetch({ readable: ['dashboard'] }) as typeof fetch
    await new AuthManager({ fetchImpl }).signInAsDevRole('Admin')

    const reloaded = new AuthManager({ fetchImpl })
    await reloaded.restore()

    expect(reloaded.getState().status).toBe('authenticated')
    expect(reloaded.can('dashboard')).toBe(true)
  })

  it('drops a stored token the server no longer accepts', async () => {
    sessionStorage.setItem('row.auth.token', 'expired-token')
    const auth = new AuthManager({ fetchImpl: fakeAuthFetch({ readable: [] }) as typeof fetch })

    await auth.restore()

    expect(auth.getState().status).toBe('anonymous')
    expect(sessionStorage.getItem('row.auth.token')).toBeNull()
  })

  it('signs out on any later 401', async () => {
    const auth = new AuthManager({ fetchImpl: fakeAuthFetch({ readable: ['dashboard'] }) as typeof fetch })
    await auth.signInAsDevRole('Admin')
    sessionStorage.setItem('row.auth.token', 'changed')
    // Force the manager's own token to be rejected by the fake server.
    ;(auth as unknown as { state: { token: string } }).state = { ...auth.getState(), token: 'rejected' }

    await auth.api.get('/api/v1/auth/me').catch(() => {})

    expect(auth.getState().status).toBe('anonymous')
  })
})
