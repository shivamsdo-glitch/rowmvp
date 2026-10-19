import { createFileRoute } from '@tanstack/react-router'
import { requirePageAccess } from '@/auth/guards'
import { LibraryPage } from '@/features/library/LibraryPage'

export const Route = createFileRoute('/_app/library')({
  beforeLoad: ({ context, location }) => requirePageAccess(context.auth, location.pathname, 'library'),
  component: LibraryPage,
})
