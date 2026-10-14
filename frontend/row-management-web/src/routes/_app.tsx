import { createFileRoute, Link, Outlet, useNavigate } from '@tanstack/react-router'
import { LayoutDashboard, LogOut, Shield } from 'lucide-react'
import type { PageKey } from '@/api/auth'
import { useAuth } from '@/auth/auth-context'
import { requireSignedIn } from '@/auth/guards'
import { buttonClassName } from '@/components/ui/button'

export const Route = createFileRoute('/_app')({
  beforeLoad: ({ context, location }) => requireSignedIn(context.auth, location.pathname),
  component: AppShell,
})

/** Pages that have a screen today. Later phases add their entries here; visibility follows page permissions. */
const NAV: { to: '/' | '/administration'; label: string; page: PageKey; icon: typeof Shield }[] = [
  { to: '/', label: 'Dashboard', page: 'dashboard', icon: LayoutDashboard },
  { to: '/administration', label: 'Administration', page: 'administration', icon: Shield },
]

function AppShell() {
  const { user, manager } = useAuth()
  const navigate = useNavigate()
  const items = NAV.filter((n) => manager.can(n.page, 'read'))

  return (
    <div className="flex min-h-screen bg-background text-foreground">
      <aside className="hidden w-60 shrink-0 flex-col bg-sidebar p-4 text-sidebar-foreground md:flex" aria-label="Primary">
        <div className="mb-6 text-sm font-semibold tracking-wide">ROW Management</div>
        <nav className="flex flex-col gap-1">
          {items.map(({ to, label, icon: Icon }) => (
            <Link
              key={to}
              to={to}
              className="flex items-center gap-2 rounded-md px-3 py-2 text-sm hover:bg-white/10"
              activeProps={{ className: 'bg-white/15' }}
              activeOptions={{ exact: true }}
            >
              <Icon size={16} /> {label}
            </Link>
          ))}
        </nav>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex items-center justify-between border-b bg-card px-6 py-3">
          <div className="text-sm text-muted-foreground">{user?.tenantName ?? 'Agency'}</div>
          <div className="flex items-center gap-3">
            <div className="text-right text-sm leading-tight">
              <div className="font-medium">{user?.fullName}</div>
              <div className="text-xs text-muted-foreground">{user?.roles.join(', ')}</div>
            </div>
            <button
              type="button"
              className={buttonClassName('outline', 'sm')}
              onClick={async () => {
                manager.signOut()
                await navigate({ to: '/login', search: {} })
              }}
            >
              <LogOut size={14} /> Sign out
            </button>
          </div>
        </header>
        <main className="flex-1 p-6">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
