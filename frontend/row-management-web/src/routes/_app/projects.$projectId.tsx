import { createFileRoute } from '@tanstack/react-router'
import { requirePageAccess } from '@/auth/guards'
import { ProjectDetailPage } from '@/features/projects/ProjectDetailPage'

export const Route = createFileRoute('/_app/projects/$projectId')({
  beforeLoad: ({ context, location }) => requirePageAccess(context.auth, location.pathname, 'projects'),
  component: ProjectDetailRoute,
})

function ProjectDetailRoute() {
  const { projectId } = Route.useParams()
  return <ProjectDetailPage key={projectId} projectId={projectId} />
}
