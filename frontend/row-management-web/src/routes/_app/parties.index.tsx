import { createFileRoute } from '@tanstack/react-router'
import { requirePageAccess } from '@/auth/guards'
import { PartyListPage } from '@/features/parties/PartyListPage'

export const Route = createFileRoute('/_app/parties/')({
  beforeLoad: ({ context, location }) => requirePageAccess(context.auth, location.pathname, 'parties'),
  component: PartyListPage,
})
