import { createFileRoute } from '@tanstack/react-router'
import { requirePageAccess } from '@/auth/guards'
import { ProjectListPage } from '@/features/projects/ProjectListPage'

export const Route = createFileRoute('/_app/projects/')({
  beforeLoad: ({ context, location }) => requirePageAccess(context.auth, location.pathname, 'projects'),
  component: ProjectListPage,
})
