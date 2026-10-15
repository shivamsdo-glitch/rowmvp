import { createFileRoute } from '@tanstack/react-router'
import { requirePageAccess } from '@/auth/guards'
import { RequestsPage } from '@/features/acquisition/RequestsPage'

export const Route = createFileRoute('/_app/requests')({
  beforeLoad: ({ context, location }) => requirePageAccess(context.auth, location.pathname, 'approvals'),
  component: RequestsPage,
})
