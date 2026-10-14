import { createFileRoute } from '@tanstack/react-router'
import { requirePageAccess } from '@/auth/guards'
import { ParcelDetailPage } from '@/features/parcels/ParcelDetailPage'

export const Route = createFileRoute('/_app/parcels/$parcelId')({
  beforeLoad: ({ context, location }) => requirePageAccess(context.auth, location.pathname, 'parcels'),
  component: ParcelDetailRoute,
})

function ParcelDetailRoute() {
  const { parcelId } = Route.useParams()
  return <ParcelDetailPage key={parcelId} parcelId={parcelId} />
}
