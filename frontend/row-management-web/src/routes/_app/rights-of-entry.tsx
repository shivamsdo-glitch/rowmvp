import { createFileRoute } from '@tanstack/react-router'
import { requirePageAccess } from '@/auth/guards'
import { RightsOfEntryReportPage } from '@/features/acquisition/RightsOfEntryReportPage'

export const Route = createFileRoute('/_app/rights-of-entry')({
  beforeLoad: ({ context, location }) => requirePageAccess(context.auth, location.pathname, 'parcels'),
  component: RightsOfEntryReportPage,
})
