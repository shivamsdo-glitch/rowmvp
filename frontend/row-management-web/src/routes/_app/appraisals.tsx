import { createFileRoute } from '@tanstack/react-router'
import { requirePageAccess } from '@/auth/guards'
import { AppraisalWorkPage } from '@/features/work/WorkPages'

export const Route = createFileRoute('/_app/appraisals')({
  beforeLoad: ({ context, location }) => requirePageAccess(context.auth, location.pathname, 'appraisals'),
  component: AppraisalWorkPage,
})
