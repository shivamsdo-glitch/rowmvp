import { createFileRoute } from '@tanstack/react-router'
import { requirePageAccess } from '@/auth/guards'
import { TitleSurveyPage } from '@/features/work/WorkPages'

export const Route = createFileRoute('/_app/title')({
  beforeLoad: ({ context, location }) => requirePageAccess(context.auth, location.pathname, 'title'),
  component: TitleSurveyPage,
})
