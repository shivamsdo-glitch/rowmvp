import { createFileRoute } from '@tanstack/react-router'
import { requirePageAccess } from '@/auth/guards'
import { PageHeader } from '@/components/ui/page-header'
import { NumberSettingsCard } from '@/features/projects/NumberSettingsCard'

export const Route = createFileRoute('/_app/administration')({
  beforeLoad: ({ context, location }) => requirePageAccess(context.auth, location.pathname, 'administration'),
  component: AdministrationPage,
})

function AdministrationPage() {
  return (
    <>
      <PageHeader title="Administration" subtitle="Configure how the agency works. Roles and page permissions will be managed here too." />
      <NumberSettingsCard />
    </>
  )
}
