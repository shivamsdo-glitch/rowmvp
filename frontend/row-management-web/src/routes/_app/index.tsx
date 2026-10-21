import { createFileRoute } from '@tanstack/react-router'
import { requirePageAccess } from '@/auth/guards'
import { DashboardPage } from '@/features/dashboard/DashboardPage'

export const Route = createFileRoute('/_app/')({
  beforeLoad: ({ context, location }) => requirePageAccess(context.auth, location.pathname, 'dashboard'),
  component: DashboardPage,
})
