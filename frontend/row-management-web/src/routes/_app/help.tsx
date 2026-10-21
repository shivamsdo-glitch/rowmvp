import { createFileRoute } from '@tanstack/react-router'
import { requireSignedIn } from '@/auth/guards'
import { HelpPage } from '@/features/help/HelpPage'

// Help is for everyone who is signed in: it needs no page permission.
export const Route = createFileRoute('/_app/help')({
  validateSearch: (s: Record<string, unknown>): { topic?: string } => (typeof s.topic === 'string' ? { topic: s.topic } : {}),
  beforeLoad: ({ context, location }) => requireSignedIn(context.auth, location.pathname),
  component: HelpRoute,
})

function HelpRoute() {
  const { topic } = Route.useSearch()
  return <HelpPage topicId={topic} />
}
