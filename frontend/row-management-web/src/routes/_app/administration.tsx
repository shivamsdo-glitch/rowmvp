import { createFileRoute } from '@tanstack/react-router'
import { requirePageAccess } from '@/auth/guards'
import { PageHeader } from '@/components/ui/page-header'

export const Route = createFileRoute('/_app/administration')({
  beforeLoad: ({ context, location }) => requirePageAccess(context.auth, location.pathname, 'administration'),
  component: () => <PageHeader title="Administration" subtitle="Roles and page permissions will be managed here." />,
})
