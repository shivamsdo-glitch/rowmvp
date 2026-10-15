import { createFileRoute } from '@tanstack/react-router'
import { requirePageAccess } from '@/auth/guards'
import { PageHeader } from '@/components/ui/page-header'
import { useAuth } from '@/auth/auth-context'
import { PlanReviewGateCard } from '@/features/acquisition/PlanReviewGateCard'
import { CustomFieldsCard, DeadlineRulesCard, ParcelStatusesCard, StepOverrideCard, WorkflowsCard } from '@/features/parcels/ParcelRulesCards'
import { NumberSettingsCard } from '@/features/projects/NumberSettingsCard'
import { TimeZoneCard } from '@/features/projects/TimeZoneCard'

export const Route = createFileRoute('/_app/administration')({
  beforeLoad: ({ context, location }) => requirePageAccess(context.auth, location.pathname, 'administration'),
  component: AdministrationPage,
})

function AdministrationPage() {
  const { manager } = useAuth()
  const parcelRules = manager.can('parcels')
  return (
    <>
      <PageHeader title="Administration" subtitle="Configure how the agency works. Roles and page permissions will be managed here too." />
      <NumberSettingsCard />
      <TimeZoneCard />
      {parcelRules && (
        <>
          <ParcelStatusesCard />
          <DeadlineRulesCard />
          <CustomFieldsCard />
          <StepOverrideCard />
          <PlanReviewGateCard />
          <WorkflowsCard />
        </>
      )}
    </>
  )
}
