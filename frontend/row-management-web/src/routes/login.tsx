import { useState } from 'react'
import { createFileRoute, redirect, useNavigate } from '@tanstack/react-router'
import { ROLES, type Role } from '@/api/auth'
import { ApiError } from '@/api/client'
import { useAuthManager } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'

export const Route = createFileRoute('/login')({
  validateSearch: (search: Record<string, unknown>): { redirect?: string } => ({
    // Only same-site paths: never bounce a user to an attacker-supplied address.
    redirect: typeof search.redirect === 'string' && /^\/(?!\/)/.test(search.redirect) ? search.redirect : undefined,
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
  const { redirect: target } = Route.useSearch()
  const [busy, setBusy] = useState<Role | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function signIn(role: Role) {
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
        <h1 className="page-title m-0">ROW Management</h1>
        <p className="page-subtitle">Development sign-in. Pick a role to continue.</p>
        <div className="mt-5 grid grid-cols-2 gap-2">
          {ROLES.map((role) => (
            <button
              key={role}
              type="button"
              disabled={busy !== null}
              className={buttonClassName('outline')}
              onClick={() => signIn(role)}
            >
              {busy === role ? 'Signing in…' : role}
            </button>
          ))}
        </div>
        {error && (
          <p role="alert" className="mt-4 text-sm text-destructive">
            {error}
          </p>
        )}
      </div>
    </main>
  )
}
