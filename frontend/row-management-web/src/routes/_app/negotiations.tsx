import { createFileRoute } from '@tanstack/react-router'
import { requirePageAccess } from '@/auth/guards'
import { NegotiationClosingPage } from '@/features/work/WorkPages'

export const Route = createFileRoute('/_app/negotiations')({
  beforeLoad: ({ context, location }) => requirePageAccess(context.auth, location.pathname, 'negotiations'),
  component: NegotiationClosingPage,
})
