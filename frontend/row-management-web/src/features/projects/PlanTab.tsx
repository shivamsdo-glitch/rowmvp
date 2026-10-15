import { useQuery } from '@tanstack/react-query'
import { Download, Printer } from 'lucide-react'
import { acquisitionApi, type AcquisitionPlan } from '@/api/acquisition'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { DataTable, dateText, money, Pill } from './form-kit'

const csvCell = (v: string | number | boolean | null | undefined) => {
  const s = v === null || v === undefined ? '' : String(v)
  // Quote anything with a delimiter or quote, and defuse text a spreadsheet would run as a formula (numbers stay numbers).
  const safe = typeof v === 'string' && /^[=+\-@]/.test(s) ? `'${s}` : s
  return /[",\n\r]/.test(safe) ? `"${safe.replace(/"/g, '""')}"` : safe
}

/** The plan as a spreadsheet file: one row per parcel, with the project's budget figures repeated in a header block. */
export function planToCsv(p: AcquisitionPlan): string {
  const b = p.plan.budget
  const lines: (string | number | boolean | null)[][] = [
    ['Project', p.projectNumber, p.projectName],
    ['As of', p.today],
    ['Total budget', b.totalBudget],
    ['Current estimate', b.currentEstimate],
    ['Authorized', b.authorizedTotal],
    ['Budget less estimate', b.variance],
    [],
    ['Parcel', 'Address', 'Area (sq ft)', 'Status', 'Step', 'Next milestone', 'Next due', 'Overdue', 'Planned close', 'Closed', 'Milestones done', 'Milestones total'],
    ...p.plan.parcels.map((r) => [r.number, r.address, r.areaSqFt, r.statusName, r.stepLabel, r.nextMilestone, r.nextDueOn, r.isOverdue, r.plannedCloseOn, r.isClosed, r.milestonesDone, r.milestonesTotal]),
  ]
  return lines.map((l) => l.map(csvCell).join(',')).join('\r\n') + '\r\n'
}

function download(name: string, text: string) {
  if (typeof URL.createObjectURL !== 'function') return
  const url = URL.createObjectURL(new Blob([text], { type: 'text/csv;charset=utf-8' }))
  const a = document.createElement('a')
  a.href = url
  a.download = name
  a.click()
  URL.revokeObjectURL(url)
}

/** The ROW acquisition plan and schedule for a project, computed from its parcels, milestones, budget and estimate (ROW-241). */
export function PlanTab({ projectId }: { projectId: string }) {
  const { manager } = useAuth()
  const plan = useQuery({ queryKey: ['acquisition-plan', projectId], queryFn: () => acquisitionApi(manager.api).plan(projectId) })

  if (plan.isPending) return <LoadingRows rows={4} />
  if (plan.isError) return <ErrorState title="The plan could not be loaded" description={plan.error.message} retry={() => void plan.refetch()} />
  const { plan: s, projectNumber } = plan.data
  const b = s.budget

  return (
    <section aria-label="Acquisition plan">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <p className="text-xs text-muted-foreground">Worked out from the live parcels, their milestone dates, the budget and the latest estimate. Nothing here is typed in.</p>
        <span className="flex gap-2">
          <button type="button" className={buttonClassName('outline', 'sm')} onClick={() => window.print()}><Printer size={14} aria-hidden /> Print plan</button>
          <button type="button" className={buttonClassName('outline', 'sm')} onClick={() => download(`acquisition-plan-${projectNumber}.csv`, planToCsv(plan.data))}><Download size={14} aria-hidden /> Export CSV</button>
        </span>
      </div>

      <dl aria-label="Plan summary" className="mb-4 grid gap-x-8 gap-y-3 rounded-lg border bg-card p-4 sm:grid-cols-2 lg:grid-cols-4">
        <Stat label="Parcels" value={String(s.parcelCount)} />
        <Stat label="Closed" value={`${s.closedCount} of ${s.parcelCount}`} />
        <Stat label="Overdue" value={String(s.overdueCount)} />
        <Stat label="Total area" value={`${s.areaSqFt.toLocaleString('en-US')} sq ft`} />
        <Stat label="First milestone due" value={dateText(s.firstDueOn)} />
        <Stat label="Last planned close" value={dateText(s.lastPlannedCloseOn)} />
      </dl>

      <div role="status" aria-label="Budget against estimate" className={`mb-4 rounded-lg border px-4 py-3 text-sm ${b.overBudget ? 'border-warn/60 bg-warn/10' : 'border-border bg-card'}`}>
        <dl className="grid gap-x-8 gap-y-2 sm:grid-cols-2 lg:grid-cols-4">
          <Stat label="Total budget" value={money(b.totalBudget)} />
          <Stat label="Current estimate" value={money(b.currentEstimate)} />
          <Stat label="Authorized" value={money(b.authorizedTotal)} />
          <Stat label={b.overBudget ? 'Over budget by' : 'Budget less estimate'} value={b.variance === null ? 'Not available' : money(Math.abs(b.variance))} />
        </dl>
        {b.variance === null && <p className="mt-2 text-xs text-muted-foreground">Enter a budget and a cost estimate on this project to see the difference.</p>}
        {b.overBudget && <p className="mt-2 text-sm"><strong>The estimate is over the budget.</strong></p>}
      </div>

      {s.parcels.length === 0 ? (
        <EmptyState title="No parcels in this project yet" description="Add parcels to see the schedule." />
      ) : (
        <DataTable caption="Parcel schedule" headers={['Parcel', 'Status', 'Step', 'Next milestone', 'Due', 'Planned close', 'Progress']}>
          {s.parcels.map((r) => (
            <tr key={r.id}>
              <td className="px-3 py-2 font-medium">{r.number}{r.address ? <span className="block text-xs font-normal text-muted-foreground">{r.address}</span> : null}</td>
              <td className="px-3 py-2">{r.statusName}</td>
              <td className="px-3 py-2">{r.stepLabel}</td>
              <td className="px-3 py-2">{r.isClosed ? 'All done' : r.nextMilestone ?? '—'}</td>
              <td className="px-3 py-2">{r.nextDueOn ? <>{dateText(r.nextDueOn)} {r.isOverdue && <Pill tone="warn">Overdue</Pill>}</> : '—'}</td>
              <td className="px-3 py-2">{dateText(r.plannedCloseOn)} {r.isClosed && <Pill tone="ok">Closed</Pill>}</td>
              <td className="px-3 py-2">{r.milestonesDone} of {r.milestonesTotal}</td>
            </tr>
          ))}
        </DataTable>
      )}
    </section>
  )
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-sm font-medium">{value}</dd>
    </div>
  )
}
