import { createFileRoute } from '@tanstack/react-router'
import { requirePageAccess } from '@/auth/guards'
import { isTabKey, ParcelDetailPage } from '@/features/parcels/ParcelDetailPage'

export const Route = createFileRoute('/_app/parcels/$parcelId')({
  beforeLoad: ({ context, location }) => requirePageAccess(context.auth, location.pathname, 'parcels'),
  // `tab` opens a parcel tab; `record` opens one record of that tab on its own page (ADR 0028).
  validateSearch: (search: Record<string, unknown>): { tab?: string; record?: string } =>
    (isTabKey(search.tab) ? { tab: search.tab, ...(typeof search.record === 'string' ? { record: search.record } : {}) } : {}),
  component: ParcelDetailRoute,
})

function ParcelDetailRoute() {
  const { parcelId } = Route.useParams()
  const { tab, record } = Route.useSearch()
  return <ParcelDetailPage key={`${parcelId}|${tab ?? ''}|${record ?? ''}`} parcelId={parcelId} initialTab={tab} initialRecord={record} />
}
