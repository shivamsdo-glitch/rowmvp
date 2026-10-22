import { createFileRoute } from '@tanstack/react-router'
import { requirePageAccess } from '@/auth/guards'
import { RelocationWorkPage } from '@/features/work/WorkPages'

export const Route = createFileRoute('/_app/relocations')({
  beforeLoad: ({ context, location }) => requirePageAccess(context.auth, location.pathname, 'relocations'),
  component: RelocationWorkPage,
})
