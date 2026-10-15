import { createFileRoute } from '@tanstack/react-router'
import { requirePageAccess } from '@/auth/guards'
import { PartyDetailPage } from '@/features/parties/PartyDetailPage'

export const Route = createFileRoute('/_app/parties/$partyId')({
  beforeLoad: ({ context, location }) => requirePageAccess(context.auth, location.pathname, 'parties'),
  component: PartyDetailRoute,
})

function PartyDetailRoute() {
  const { partyId } = Route.useParams()
  return <PartyDetailPage key={partyId} partyId={partyId} />
}
