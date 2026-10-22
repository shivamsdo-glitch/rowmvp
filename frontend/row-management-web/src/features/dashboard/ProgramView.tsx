import { Link } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { ArrowLeft } from 'lucide-react'
import { dashboardApi, type Health, type ParcelRow, type ProgramDashboard, type ProgramTile, type ProjectDashboard, type ProjectRow, type Stage, type StepCount, type TeamTime } from '@/api/dashboard'
import { useAuth } from '@/auth/auth-context'
import { cn } from '@/lib/cn'
import { ErrorState, LoadingRows } from '@/components/ui/states'
import { dateText, DataTable, money } from '../projects/form-kit'
import { RemindersCard } from '../parcels/RemindersCard'
import { Card, KpiTile } from './dashboard-kit'

/** $1.2M, $850K, $900: big money reads at a glance on a tile; tables use the full amount. */
export function compactMoney(v: number) {
  const abs = Math.abs(v)
  if (abs >= 1_000_000) return `$${(v / 1_000_000).toFixed(abs >= 10_000_000 ? 0 : 1)}M`
  if (abs >= 1_000) return `$${Math.round(v / 1_000)}K`
  return `$${Math.round(v)}`
}
const days = (v: number | null) => (v === null ? '—' : v < 10 ? v.toFixed(1) : Math.round(v).toString())

const HEALTH: Record<Health, { label: string; cls: string }> = {
  Red: { label: 'Off track', cls: 'bg-danger/15 text-danger' },
  Amber: { label: 'Watch', cls: 'bg-warn/25 text-foreground' },
  Green: { label: 'On track', cls: 'bg-ok/20 text-foreground' },
  None: { label: 'No parcels', cls: 'bg-muted text-muted-foreground' },
}
export function HealthPill({ health }: { health: Health }) {
  const h = HEALTH[health]
  return <span className={cn('inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-semibold', h.cls)} data-health={health}><span className="h-1.5 w-1.5 rounded-full bg-current" aria-hidden />{h.label}</span>
}

const SEGMENTS = ['bg-slate-300', 'bg-sky-300', 'bg-sky-500', 'bg-blue-600', 'bg-indigo-500', 'bg-violet-500', 'bg-emerald-600', 'bg-amber-500', 'bg-rose-400', 'bg-teal-500']

/** One thin bar: where this project's parcels are across the workflow, with a text summary. */
export function Pipeline({ steps, total }: { steps: StepCount[]; total: number }) {
  if (total === 0) return <span className="text-xs text-muted-foreground">No parcels</span>
  const summary = `Parcels by step: ${steps.filter((s) => s.count > 0).map((s) => `${s.label} ${s.count}`).join(', ')}`
  return (
    <div role="img" aria-label={summary} title={summary} className="flex h-2.5 w-40 overflow-hidden rounded bg-muted">
      {steps.map((s, i) => s.count > 0 && <span key={s.stepKey} className={SEGMENTS[i % SEGMENTS.length]} style={{ width: `${(s.count / total) * 100}%` }} />)}
    </div>
  )
}

function Progress({ percent }: { percent: number }) {
  return (
    <div className="flex items-center gap-2" role="img" aria-label={`${percent}% closed`}>
      <span className="h-2 w-20 overflow-hidden rounded bg-muted"><span className="block h-2 bg-ok" style={{ width: `${percent}%` }} /></span>
      <span className="text-xs tabular-nums">{percent}%</span>
    </div>
  )
}

const formatTile = (t: ProgramTile) => (t.format === 'money' ? compactMoney(t.value) : t.format === 'percent' ? `${t.value}%` : String(t.value))

export function TeamBars({ teams, stuckDays }: { teams: TeamTime[]; stuckDays: number }) {
  const max = Math.max(1, ...teams.map((t) => t.avgDaysPerStay ?? 0))
  if (teams.length === 0) return <p className="text-sm text-muted-foreground">No parcels have been through the workflow yet.</p>
  return (
    <ul className="space-y-2.5 text-sm" aria-label="Average days a parcel stays with each team">
      {teams.map((t) => (
        <li key={t.team} data-testid={`team-${t.team}`}>
          <div className="flex items-baseline justify-between gap-3">
            <span className="font-medium">{t.team}</span>
            <span className="text-xs text-muted-foreground">
              {t.parcelsNow} parcel{t.parcelsNow === 1 ? '' : 's'} now{t.stuck > 0 && <span className="font-semibold text-danger"> · {t.stuck} over {stuckDays} days</span>}
            </span>
          </div>
          <div className="mt-1 flex items-center gap-2">
            <span className="h-3 flex-1 overflow-hidden rounded bg-muted"><span className="block h-3 rounded bg-primary" style={{ width: `${((t.avgDaysPerStay ?? 0) / max) * 100}%` }} /></span>
            <span className="w-20 text-right text-xs tabular-nums">{days(t.avgDaysPerStay)} days avg</span>
          </div>
        </li>
      ))}
    </ul>
  )
}

export function StageTable({ stages, caption }: { stages: Stage[]; caption: string }) {
  return (
    <DataTable caption={caption} headers={['Step', 'Team', 'On it now', 'Avg days (finished)', 'Avg days (now)', 'Longest now', 'Stuck']}>
      {stages.map((s) => (
        <tr key={s.stepKey}>
          <td className="px-3 py-2 font-medium">{s.label}</td>
          <td className="px-3 py-2 text-muted-foreground">{s.team}</td>
          <td className="px-3 py-2 tabular-nums">{s.parcelsNow}</td>
          <td className="px-3 py-2 tabular-nums">{days(s.avgDaysCompleted)}</td>
          <td className="px-3 py-2 tabular-nums">{days(s.avgDaysOpen)}</td>
          <td className="px-3 py-2 tabular-nums">{days(s.longestOpenDays)}</td>
          <td className={cn('px-3 py-2 tabular-nums', s.stuck > 0 && 'font-semibold text-danger')}>{s.stuck}</td>
        </tr>
      ))}
    </DataTable>
  )
}

function clearText(p: ProjectRow) {
  if (!p.rowClearDate) return '—'
  const d = p.daysToClear ?? 0
  return `${dateText(p.rowClearDate)} (${d < 0 ? `${-d} days ago` : d === 0 ? 'today' : `in ${d} days`})`
}

function ProjectsTable({ projects, onOpen }: { projects: ProjectRow[]; onOpen: (p: ProjectRow) => void }) {
  if (projects.length === 0) return <p className="text-sm text-muted-foreground">No projects yet. Create one under Projects.</p>
  return (
    <DataTable caption="Projects" headers={['Project', 'Health', 'Parcels', 'Closed', 'Where the parcels are', 'Overdue', 'Stuck', 'Relocated', 'Estimate', 'Known cost', 'ROW clear']}>
      {projects.map((p) => (
        <tr key={p.id} className="align-top">
          <td className="px-3 py-2">
            <button type="button" onClick={() => onOpen(p)} aria-label={`Open project ${p.number}`} className="text-left font-medium text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">
              {p.number}
            </button>
            <div className="text-xs text-muted-foreground">{p.name}</div>
          </td>
          <td className="px-3 py-2">
            <HealthPill health={p.health} />
            {p.reasons.length > 0 && <div className="mt-1 max-w-xs text-xs text-muted-foreground">{p.reasons[0]}{p.reasons.length > 1 && ` (+${p.reasons.length - 1} more)`}</div>}
          </td>
          <td className="px-3 py-2 tabular-nums">{p.parcels}</td>
          <td className="px-3 py-2"><Progress percent={p.percentClosed} /></td>
          <td className="px-3 py-2"><Pipeline steps={p.byStep} total={p.parcels} /></td>
          <td className={cn('px-3 py-2 tabular-nums', p.overdueParcels > 0 && 'font-semibold text-danger')}>{p.overdueParcels}</td>
          <td className={cn('px-3 py-2 tabular-nums', p.stuckParcels > 0 && 'font-semibold text-danger')}>{p.stuckParcels}</td>
          <td className="px-3 py-2 tabular-nums">{(p.displaced ?? 0) > 0 ? `${p.relocated ?? 0}/${p.displaced}` : '—'}{(p.relocationsOpen ?? 0) > 0 && <div className="text-xs text-muted-foreground">{p.relocationsOpen} open</div>}</td>
          <td className="px-3 py-2 tabular-nums">{money(p.estimateTotal)}</td>
          <td className={cn('px-3 py-2 tabular-nums', p.estimateTotal && p.knownCost > p.estimateTotal && 'font-semibold text-danger')}>{money(p.knownCost)}</td>
          <td className="px-3 py-2 text-xs">{clearText(p)}</td>
        </tr>
      ))}
    </DataTable>
  )
}

function Legend({ steps }: { steps: StepCount[] }) {
  return (
    <ul className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground" aria-label="Step colours">
      {steps.map((s, i) => <li key={s.stepKey} className="flex items-center gap-1.5"><span className={cn('h-2.5 w-2.5 rounded-sm', SEGMENTS[i % SEGMENTS.length])} aria-hidden />{s.label}</li>)}
    </ul>
  )
}

function Program({ data, onOpen }: { data: ProgramDashboard; onOpen: (p: ProjectRow) => void }) {
  const legend = data.projects[0]?.byStep ?? []
  return (
    <div className="space-y-4">
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-7">
        {data.tiles.map((t) => <KpiTile key={t.key} label={t.label} value={formatTile(t)} tone={t.value > 0 && t.tone !== 'info' ? t.tone : 'info'} />)}
      </div>
      <Card title="Projects" subtitle="Where right of way stands on every project. Open one to see each parcel.">
        <ProjectsTable projects={data.projects} onOpen={onOpen} />
        {legend.length > 0 && <div className="mt-3"><Legend steps={legend} /></div>}
      </Card>
      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="Where the time goes" subtitle="Average days a parcel stays with each team, longest first. Time is counted from the moves recorded on each parcel.">
          <TeamBars teams={data.teams} stuckDays={data.stuckDays} />
        </Card>
        <Card title="Parcels stuck longest" subtitle={`Open parcels that have been on one step for more than ${data.stuckDays} days.`}>
          {data.stuck.length === 0 ? <p className="text-sm text-muted-foreground">No parcel has been on one step for more than {data.stuckDays} days.</p> : (
            <ul className="divide-y text-sm" aria-label="Parcels stuck longest">
              {data.stuck.map((s) => (
                <li key={s.parcelId} className="flex items-center justify-between gap-3 py-2">
                  <span>
                    <Link to="/parcels/$parcelId" params={{ parcelId: s.parcelId }} className="font-medium text-primary hover:underline">{s.projectNumber} · Parcel {s.parcelNumber}</Link>
                    <span className="text-muted-foreground"> — {s.stepLabel} ({s.team})</span>
                  </span>
                  <span className="rounded-full bg-danger/15 px-2 py-0.5 text-xs font-semibold text-danger tabular-nums">{Math.round(s.days)} days</span>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>
      <Card title="Time in each step" subtitle="How long parcels take at every step of the workflow, and which team owns it.">
        <StageTable stages={data.stages} caption="Time in each workflow step" />
      </Card>
      <RemindersCard />
    </div>
  )
}

function ParcelsTable({ parcels }: { parcels: ParcelRow[] }) {
  if (parcels.length === 0) return <p className="text-sm text-muted-foreground">This project has no parcels yet.</p>
  return (
    <DataTable caption="Parcels in this project" headers={['Parcel', 'Owner', 'Status', 'Step', 'Days on step', 'Appraisal', 'Approved just comp.', 'Agreed', 'Closing', 'Relocation', 'Attention']}>
      {parcels.map((p) => (
        <tr key={p.id} className="align-top">
          <td className="px-3 py-2">
            <Link to="/parcels/$parcelId" params={{ parcelId: p.id }} className="font-medium text-primary hover:underline" aria-label={`Open parcel ${p.number}`}>{p.number}</Link>
            <div className="text-xs text-muted-foreground">{p.address ?? ''}</div>
          </td>
          <td className="px-3 py-2">{p.ownerName ?? '—'}</td>
          <td className="px-3 py-2"><span className="inline-flex items-center gap-1.5 text-xs"><span className="h-2 w-2 rounded-full" style={{ background: p.statusColor }} aria-hidden />{p.status}</span></td>
          <td className="px-3 py-2">{p.stepLabel}<div className="text-xs text-muted-foreground">{p.team}</div></td>
          <td className={cn('px-3 py-2 tabular-nums', !p.closed && p.flags.some((f) => f.includes('on this step')) && 'font-semibold text-danger')}>{p.closed ? '—' : days(p.daysInStep)}</td>
          <td className="px-3 py-2 tabular-nums">{money(p.appraisalTotal)}</td>
          <td className="px-3 py-2 tabular-nums">{money(p.approvedJustCompensation)}</td>
          <td className="px-3 py-2 tabular-nums">{money(p.agreedAmount)}</td>
          <td className="px-3 py-2 text-xs">{p.closed ? <span className="font-semibold text-ok">Closed {dateText(p.actualClosing)}</span> : p.anticipatedClosing ? `Due ${dateText(p.anticipatedClosing)}` : '—'}</td>
          <td className="px-3 py-2 text-xs">{p.relocation ?? '—'}</td>
          <td className="px-3 py-2">
            {p.flags.length === 0 ? <span className="text-xs text-muted-foreground">—</span> : (
              <ul className="space-y-0.5">{p.flags.map((f) => <li key={f} className="text-xs font-medium text-danger">{f}</li>)}</ul>
            )}
          </td>
        </tr>
      ))}
    </DataTable>
  )
}

function ProjectDetail({ data, onBack }: { data: ProjectDashboard; onBack: () => void }) {
  const p = data.project
  return (
    <div className="space-y-4">
      <div>
        <button type="button" onClick={onBack} className="mb-2 inline-flex items-center gap-1 text-xs text-primary hover:underline"><ArrowLeft size={12} aria-hidden /> All projects</button>
        <div className="flex flex-wrap items-center gap-3">
          <h2 className="text-lg font-semibold">{p.number} · {p.name}</h2>
          <HealthPill health={p.health} />
        </div>
        <p className="text-xs text-muted-foreground">{[p.oversightType, p.phase, p.projectManager && `Managed by ${p.projectManager}`].filter(Boolean).join(' · ')}</p>
        {p.reasons.length > 0 && (
          <ul className="mt-2 list-disc space-y-0.5 pl-5 text-sm" aria-label="Why this project needs attention">{p.reasons.map((r) => <li key={r}>{r}</li>)}</ul>
        )}
      </div>
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
        <KpiTile label="Parcels" value={p.parcels} />
        <KpiTile label="Closed" value={`${p.percentClosed}%`} hint={`${p.closed} of ${p.parcels} parcels`} />
        {(p.displaced ?? 0) > 0 && <KpiTile label="Relocated" value={`${p.relocated ?? 0}/${p.displaced}`} hint="Displaced interests relocated" />}
        <KpiTile label="Estimate" value={p.estimateTotal === null ? '—' : compactMoney(p.estimateTotal)} hint="Latest ROW cost estimate" />
        <KpiTile label="Known cost" value={compactMoney(p.knownCost)} tone={p.estimateTotal && p.knownCost > p.estimateTotal ? 'danger' : 'info'} hint="Agreed amounts, else approved just compensation" />
        <KpiTile label="ROW clear" value={<span className="text-xl">{p.rowClearDate ? dateText(p.rowClearDate) : '—'}</span>} tone={p.daysToClear !== null && p.daysToClear < 0 && p.closed < p.parcels ? 'danger' : 'info'} hint={p.rowClearDate ? clearText(p).replace(/^.*\(/, '').replace(')', '') : 'No date set'} />
      </div>
      <Card title="Where the parcels are" subtitle="Parcels on each step of the workflow.">
        <ol className="flex flex-wrap gap-2" aria-label="Parcels by step">
          {p.byStep.map((s) => (
            <li key={s.stepKey} className={cn('min-w-28 flex-1 rounded-md border px-3 py-2', s.count > 0 ? 'bg-primary-soft' : 'bg-card')}>
              <div className="text-xs text-muted-foreground">{s.label}</div>
              <div className="text-xl font-semibold tabular-nums" data-testid={`step-${s.stepKey}`}>{s.count}</div>
            </li>
          ))}
        </ol>
      </Card>
      <Card title="Parcels" subtitle="Every active parcel: where it is, who owns it, the money and what needs attention.">
        <ParcelsTable parcels={data.parcels} />
      </Card>
      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="Where the time goes in this project" subtitle="Average days a parcel stays with each team.">
          <TeamBars teams={data.teams} stuckDays={data.stuckDays} />
        </Card>
        <Card title="Time in each step" subtitle="For this project’s parcels.">
          <StageTable stages={data.stages} caption="Time in each workflow step for this project" />
        </Card>
      </div>
    </div>
  )
}

export function ProgramView({ selected, onSelect }: { selected: ProjectRow | null; onSelect: (p: ProjectRow | null) => void }) {
  const { manager } = useAuth()
  const program = useQuery({ queryKey: ['dashboard', 'program'], queryFn: () => dashboardApi(manager.api).program() })
  const detail = useQuery({ queryKey: ['dashboard', 'project', selected?.id], queryFn: () => dashboardApi(manager.api).project(selected!.id), enabled: selected !== null })

  if (selected) {
    if (detail.isPending) return <LoadingRows rows={5} />
    if (detail.isError || !detail.data) return <ErrorState title="The project could not be loaded" retry={() => void detail.refetch()} />
    return <ProjectDetail data={detail.data} onBack={() => onSelect(null)} />
  }
  if (program.isPending) return <LoadingRows rows={5} />
  if (program.isError || !program.data) return <ErrorState title="The program view could not be loaded" retry={() => void program.refetch()} />
  return <Program data={program.data} onOpen={onSelect} />
}
