import { createFileRoute } from '@tanstack/react-router'
import { requirePageAccess } from '@/auth/guards'
import { ParcelListPage } from '@/features/parcels/ParcelListPage'

export const Route = createFileRoute('/_app/parcels/')({
  beforeLoad: ({ context, location }) => requirePageAccess(context.auth, location.pathname, 'parcels'),
  // `?projectId=` opens the list already narrowed to one project (linked from the project's Parcels tab).
  validateSearch: (search: Record<string, unknown>): { projectId?: string } =>
    typeof search.projectId === 'string' && search.projectId ? { projectId: search.projectId } : {},
  component: ParcelListRoute,
})

function ParcelListRoute() {
  const { projectId } = Route.useSearch()
  return <ParcelListPage key={projectId ?? ''} initialProjectId={projectId} />
}
