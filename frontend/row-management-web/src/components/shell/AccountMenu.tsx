import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { CircleUser } from 'lucide-react'
import { useAuth } from '@/auth/auth-context'

export function AccountMenu() {
  const { user, manager } = useAuth()
  const qc = useQueryClient()
  const [open, setOpen] = useState(false)

  function onSignOut() {
    setOpen(false)
    qc.clear() // never leave one user's cached data around for the next sign-in
    manager.signOut() // the shell sees the session end and returns to the login page
  }

  return (
    <div className="relative">
      <button
        type="button"
        aria-label="Account"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
        className="w-9 h-9 flex items-center justify-center rounded-md text-muted-foreground hover:bg-muted hover:text-foreground transition-colors"
      >
        <CircleUser size={22} />
      </button>

      {open && (
        <>
          <div className="fixed inset-0 z-30" onClick={() => setOpen(false)} />
          <div className="absolute right-0 mt-1.5 w-64 rounded-md border border-border bg-card shadow-lg z-40 overflow-hidden">
            <div className="flex justify-end px-3 py-2 border-b border-border">
              <button type="button" onClick={onSignOut} className="text-xs text-primary hover:underline">
                Log Out
              </button>
            </div>
            <div className="px-4 py-3 flex items-center gap-3 border-b border-border">
              <CircleUser size={36} className="text-muted-foreground shrink-0" />
              <div className="min-w-0">
                <div className="text-sm font-semibold text-foreground truncate">{user?.fullName ?? 'Account'}</div>
                <div className="text-xs text-muted-foreground truncate">{user?.email}</div>
                <div className="text-[11px] text-muted-foreground truncate">{user?.roles.join(', ')}</div>
              </div>
            </div>
            {/* Profile and password screens arrive with user management. */}
            <div className="flex divide-x divide-border">
              <button type="button" disabled className="flex-1 py-2.5 text-xs text-muted-foreground cursor-not-allowed">
                View Profile
              </button>
              <button type="button" disabled className="flex-1 py-2.5 text-xs text-muted-foreground cursor-not-allowed">
                Change Password
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  )
}
