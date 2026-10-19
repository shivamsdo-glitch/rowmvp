import { useState, type FormEvent } from 'react'
import { createFileRoute, redirect, useNavigate } from '@tanstack/react-router'
import { ROLES, type Role } from '@/api/auth'
import { ApiError } from '@/api/client'
import { useAuthManager } from '@/auth/auth-context'
import { BRAND } from '@/brand/brand'
import { buttonClassName } from '@/components/ui/button'

export const Route = createFileRoute('/login')({
  validateSearch: (search: Record<string, unknown>): { redirect?: string; expired?: boolean } => ({
    expired: search.expired === true || search.expired === 'true' ? true : undefined,
    // Only same-site paths: never bounce a user to an attacker-supplied address.
    redirect: typeof search.redirect === 'string' && /^\/(?![/\\])/.test(search.redirect) && !search.redirect.startsWith('/login') ? search.redirect : undefined,
  }),
  beforeLoad: async ({ context }) => {
    await context.auth.restore()
    if (context.auth.getState().status === 'authenticated') throw redirect({ to: '/' })
  },
  component: LoginPage,
})

function LoginPage() {
  const auth = useAuthManager()
  const navigate = useNavigate()
  const { redirect: target, expired } = Route.useSearch()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState<Role | 'password' | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!username.trim() || !password) {
      setError('Enter your username and password.')
      return
    }
    setBusy('password')
    setError(null)
    try {
      await auth.signIn(username.trim(), password)
      await navigate({ to: target ?? '/' })
    } catch (err) {
      setError(
        err instanceof ApiError && err.status === 401
          ? 'Incorrect username or password.'
          : err instanceof Error ? err.message : 'Sign-in failed.',
      )
    } finally {
      setBusy(null)
    }
  }

  async function signInAsRole(role: Role) {
    setBusy(role)
    setError(null)
    try {
      await auth.signInAsDevRole(role)
      await navigate({ to: target ?? '/' })
    } catch (e) {
      setError(
        e instanceof ApiError && e.status === 404
          ? 'Dev sign-in is only available when the API runs in Development.'
          : e instanceof Error ? e.message : 'Sign-in failed.',
      )
    } finally {
      setBusy(null)
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-background p-6">
      <div className="w-full max-w-md rounded-lg border bg-card p-6 shadow-sm">
        <h1 className="page-title m-0">{BRAND.productName}</h1>
        <p className="page-subtitle">{BRAND.tagline}</p>

        <form className="mt-5 space-y-3" onSubmit={submit} noValidate>
          <div>
            <label htmlFor="username" className="mb-1 block text-sm font-medium">Username</label>
            <input
              id="username"
              name="username"
              autoComplete="username"
              autoFocus
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              className="h-9 w-full rounded-md border bg-background px-3 text-sm"
            />
          </div>
          <div>
            <label htmlFor="password" className="mb-1 block text-sm font-medium">Password</label>
            <input
              id="password"
              name="password"
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="h-9 w-full rounded-md border bg-background px-3 text-sm"
            />
          </div>
          <button type="submit" disabled={busy !== null} className={`${buttonClassName('default')} w-full`}>
            {busy === 'password' ? 'Signing in…' : 'Sign in'}
          </button>
        </form>

        {expired && !error && (
          <p role="status" className="mt-4 text-sm text-muted-foreground">Your session has expired. Please sign in again.</p>
        )}
        {error && (
          <p role="alert" className="mt-4 text-sm text-destructive">
            {error}
          </p>
        )}

        {import.meta.env.DEV && (
          <section aria-label="Development shortcuts" className="mt-6 border-t pt-4">
            <p className="text-xs text-muted-foreground">Development sign-in. Pick a role to continue without a password.</p>
            <div className="mt-2 grid grid-cols-2 gap-2">
              {ROLES.map((role) => (
                <button
                  key={role}
                  type="button"
                  disabled={busy !== null}
                  className={buttonClassName('outline')}
                  onClick={() => signInAsRole(role)}
                >
                  {busy === role ? 'Signing in…' : role}
                </button>
              ))}
            </div>
          </section>
        )}
      </div>
    </main>
  )
}
