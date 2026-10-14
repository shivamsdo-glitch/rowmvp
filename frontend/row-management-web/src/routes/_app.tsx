import { useState } from 'react'
import { createFileRoute, Outlet, useRouterState } from '@tanstack/react-router'
import { requireSignedIn } from '@/auth/guards'
import { Sidebar } from '@/components/shell/Sidebar'
import { TopBar } from '@/components/shell/TopBar'

// The auth gate: anyone without a valid session is sent to /login (remembering where they were going).
export const Route = createFileRoute('/_app')({
  beforeLoad: ({ context, location }) => requireSignedIn(context.auth, location.pathname),
  component: AppShell,
})

function AppShell() {
  const [collapsed, setCollapsed] = useState(false)
  const path = useRouterState({ select: (s) => s.location.pathname })

  return (
    // h-screen (not min-h-screen) pins the shell to the viewport so the sidebar stays put and only <main> scrolls.
    <div className="h-screen flex flex-col bg-background text-foreground overflow-hidden">
      <TopBar />
      <div className="flex flex-1 min-h-0 overflow-hidden bg-card">
        <Sidebar collapsed={collapsed} onToggle={() => setCollapsed((c) => !c)} currentPath={path} />
        <div className="flex-1 min-w-0 flex flex-col overflow-hidden bg-background">
          <main className="flex-1 min-w-0 min-h-0 overflow-y-auto overflow-x-auto">
            <div className="px-6 py-6 max-w-[1720px] mx-auto w-full">
              <Outlet />
            </div>
          </main>
        </div>
      </div>
    </div>
  )
}
