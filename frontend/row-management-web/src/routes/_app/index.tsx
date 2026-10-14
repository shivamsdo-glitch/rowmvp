import { createFileRoute } from '@tanstack/react-router'
import { useAuth } from '@/auth/auth-context'
import { requirePageAccess } from '@/auth/guards'
import { RemindersCard } from '@/features/parcels/RemindersCard'
import { PageHeader } from '@/components/ui/page-header'

export const Route = createFileRoute('/_app/')({
  beforeLoad: ({ context, location }) => requirePageAccess(context.auth, location.pathname, 'dashboard'),
  component: DashboardPage,
})

function DashboardPage() {
  const { user, permissions } = useAuth()
  const readable = permissions.filter((p) => p.canRead).map((p) => p.pageKey)

  return (
    <>
      <PageHeader title="Dashboard" subtitle={`Welcome, ${user?.fullName ?? ''}.`} />
      <p className="text-sm text-muted-foreground">
        Your role can open {readable.length} page{readable.length === 1 ? '' : 's'}: {readable.join(', ') || 'none'}.
        Title, appraisal and negotiation screens arrive in the next phases.
      </p>
      <RemindersCard />
    </>
  )
}
