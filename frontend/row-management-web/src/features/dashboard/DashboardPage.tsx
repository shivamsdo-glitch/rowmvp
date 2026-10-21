import { useState, type ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { CheckCircle2, Clock, RefreshCw } from 'lucide-react'
import { dashboardApi, type ActivityDay, type ActivityKind, type MyDashboard, type PendingItem, type TeamDashboard, type Tone } from '@/api/dashboard'
import { useAuth } from '@/auth/auth-context'
import { cn } from '@/lib/cn'
import { PageHeader } from '@/components/ui/page-header'
import { relativeTime } from '@/components/shell/NotificationBell'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { DataTable } from '../projects/form-kit'
import { RemindersCard } from '../parcels/RemindersCard'

const TONE_TEXT: Record<Tone, string> = { danger: 'text-danger', warn: 'text-foreground', info: 'text-primary' }
const TONE_PILL: Record<Tone, string> = { danger: 'bg-danger/15 text-danger', warn: 'bg-warn/25 text-foreground', info: 'bg-primary-soft text-primary' }
const ACTION_WORD = { Create: 'Added', Update: 'Changed', Delete: 'Removed' } as const

/** A big number with a plain label, the way a tile reads at a glance. A tile with a link opens the screen behind it. */
export function KpiTile({ label, value, hint, tone = 'info', to }: { label: string; value: ReactNode; hint?: string; tone?: Tone; to?: string }) {
  const body = (
    <>
      <div className="eyebrow">{label}</div>
      <div className={cn('mt-1 text-3xl font-semibold leading-none tabular-nums', TONE_TEXT[tone])} data-testid={`tile-${label}`}>{value}</div>
      {hint && <div className="mt-2 text-xs text-muted-foreground">{hint}</div>}
    </>
  )
  const cls = 'block rounded-lg border bg-card p-4 shadow-sm'
  return to
    ? <Link to={to as never} className={cn(cls, 'transition-shadow hover:shadow-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring')}>{body}</Link>
    : <div className={cls}>{body}</div>
}

export function Card({ title, subtitle, children, className }: { title: string; subtitle?: string; children: ReactNode; className?: string }) {
  return (
    <section aria-label={title} className={cn('rounded-lg border bg-card p-4 shadow-sm', className)}>
      <h2 className="text-sm font-semibold">{title}</h2>
      {subtitle && <p className="mb-3 text-xs text-muted-foreground">{subtitle}</p>}
      {!subtitle && <div className="mb-3" />}
      {children}
    </section>
  )
}

const dayName = (iso: string) => new Date(`${iso}T12:00:00`).toLocaleDateString(undefined, { weekday: 'short' })

/** Seven small bars, one per day, with the number above each. Plain markup so it works everywhere and a screen reader gets one summary. */
export function ActivityChart({ days, label }: { days: ActivityDay[]; label: string }) {
  const max = Math.max(1, ...days.map((d) => d.count))
  const summary = `${label}: ${days.map((d) => `${dayName(d.date)} ${d.count}`).join(', ')}`
  return (
    <div role="img" aria-label={summary} className="flex h-32 items-end gap-2" data-testid="activity-chart">
      {days.map((d) => (
        <div key={d.date} className="flex flex-1 flex-col items-center justify-end gap-1" data-testid={`day-${d.date}`}>
          <span className="text-xs font-medium tabular-nums">{d.count}</span>
          <div className={cn('w-full rounded-t', d.count ? 'bg-primary' : 'bg-muted')} style={{ height: `${Math.max(4, (d.count / max) * 80)}px` }} />
          <span className="text-[11px] text-muted-foreground">{dayName(d.date)}</span>
        </div>
      ))}
    </div>
  )
}

function KindTable({ kinds, caption }: { kinds: ActivityKind[]; caption: string }) {
  if (kinds.length === 0) return <p className="text-sm text-muted-foreground">Nothing yet in the last 30 days.</p>
  return (
    <DataTable caption={caption} headers={['Record', 'Added', 'Changed', 'Removed']}>
      {kinds.slice(0, 8).map((k) => (
        <tr key={k.kind}>
          <td className="px-3 py-2">{k.kind}</td>
          <td className="px-3 py-2 tabular-nums">{k.created}</td>
          <td className="px-3 py-2 tabular-nums">{k.updated}</td>
          <td className="px-3 py-2 tabular-nums">{k.deleted}</td>
        </tr>
      ))}
    </DataTable>
  )
}

export function PendingList({ items }: { items: PendingItem[] }) {
  const open = items.filter((i) => i.count > 0)
  if (open.length === 0) {
    return <EmptyState icon={CheckCircle2} title="Nothing is waiting for you" description="New work shows up here as soon as it arrives." className="py-6" />
  }
  return (
    <ul className="divide-y text-sm" aria-label="Work waiting for you">
      {open.map((i) => (
        <li key={i.key}>
          <Link to={i.link as never} className="flex items-center justify-between gap-3 rounded px-1 py-2.5 hover:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">
            <span>{i.label}</span>
            <span className={cn('inline-flex min-w-8 justify-center rounded-full px-2 py-0.5 text-xs font-semibold tabular-nums', TONE_PILL[i.tone])} data-testid={`pending-${i.key}`}>{i.count}</span>
          </Link>
        </li>
      ))}
    </ul>
  )
}

function MyWork({ data }: { data: MyDashboard }) {
  const waiting = data.pending.filter((p) => p.tone !== 'info').reduce((n, p) => n + p.count, 0)
  const overdue = data.pending.find((p) => p.key === 'deadlines-overdue')?.count ?? 0
  const { done } = data
  return (
    <div className="space-y-4">
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <KpiTile label="Needs your attention" value={waiting} hint="Decisions, deadlines and closings" tone={waiting ? 'warn' : 'info'} />
        <KpiTile label="Overdue deadlines" value={overdue} hint="Parcel steps past their date" tone={overdue ? 'danger' : 'info'} to="/parcels" />
        <KpiTile label="Done in 30 days" value={done.total} hint={`${done.created} added · ${done.updated} changed · ${done.deleted} removed`} />
        <KpiTile label="Last active" value={<span className="text-xl">{done.lastActiveAt ? relativeTime(done.lastActiveAt) : '—'}</span>} hint={`Signed in as ${data.roles.join(', ')}`} />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="Waiting for you" subtitle="Work your role can open, with the number of records behind each line.">
          <PendingList items={data.pending} />
        </Card>
        <Card title="What you have done" subtitle="Records you added, changed or removed in the last 7 days.">
          <ActivityChart days={done.days} label="Your actions per day" />
        </Card>
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="Your work by record" subtitle="The last 30 days.">
          <KindTable kinds={done.byKind} caption="Your work by kind of record" />
        </Card>
        <Card title="Your recent activity" subtitle="The latest things you did.">
          {done.recent.length === 0 ? (
            <p className="text-sm text-muted-foreground">Nothing yet. Open a parcel and add a record, and it will show here.</p>
          ) : (
            <ul className="divide-y text-sm" aria-label="Your recent activity">
              {done.recent.map((a, i) => (
                <li key={`${a.at}-${i}`} className="flex items-center justify-between gap-2 py-2">
                  <span><span className="font-medium">{ACTION_WORD[a.action]}</span> {a.kind.toLowerCase()}</span>
                  <span className="text-xs text-muted-foreground">{relativeTime(a.at)}</span>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>
      <RemindersCard />
    </div>
  )
}

function TeamView({ data }: { data: TeamDashboard }) {
  const totalParcels = data.parcelsByStatus.reduce((n, s) => n + s.parcels, 0)
  return (
    <div className="space-y-4">
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-6">
        {data.tiles.map((t) => <KpiTile key={t.key} label={t.label} value={t.value} tone={t.value > 0 && t.tone !== 'info' ? t.tone : 'info'} to={t.link} />)}
      </div>
      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="Parcels by status" subtitle="Active parcels across every project.">
          {data.parcelsByStatus.length === 0 ? <p className="text-sm text-muted-foreground">No parcels yet.</p> : (
            <ul className="space-y-2 text-sm" aria-label="Parcels by status">
              {data.parcelsByStatus.map((s) => (
                <li key={s.status} className="flex items-center gap-3">
                  <span className="w-40 shrink-0 truncate">{s.status}</span>
                  <span className="h-3 flex-1 rounded bg-muted"><span className="block h-3 rounded" style={{ width: `${(s.parcels / totalParcels) * 100}%`, background: s.color }} /></span>
                  <span className="w-8 text-right tabular-nums">{s.parcels}</span>
                </li>
              ))}
            </ul>
          )}
        </Card>
        <Card title="Team activity" subtitle="Everything the team added, changed or removed in the last 7 days.">
          <ActivityChart days={data.days} label="Team actions per day" />
        </Card>
      </div>
      <Card title="People" subtitle={`What each person did in the last ${data.windowDays} days, and what they have waiting.`}>
        <DataTable caption="Team members" headers={['Person', 'Roles', 'Added', 'Changed', 'Removed', 'Total', 'Last active', 'Open requests', 'Draft reports']}>
          {data.members.map((m) => (
            <tr key={m.userId}>
              <td className="px-3 py-2 font-medium">{m.fullName}</td>
              <td className="px-3 py-2 text-muted-foreground">{m.roles.join(', ')}</td>
              <td className="px-3 py-2 tabular-nums">{m.created}</td>
              <td className="px-3 py-2 tabular-nums">{m.updated}</td>
              <td className="px-3 py-2 tabular-nums">{m.deleted}</td>
              <td className="px-3 py-2 font-semibold tabular-nums">{m.total}</td>
              <td className="px-3 py-2 text-muted-foreground">{m.lastActiveAt ? relativeTime(m.lastActiveAt) : 'No activity'}</td>
              <td className="px-3 py-2 tabular-nums">{m.openRequests}</td>
              <td className="px-3 py-2 tabular-nums">{m.draftReports}</td>
            </tr>
          ))}
        </DataTable>
      </Card>
      <Card title="Team work by record" subtitle={`The last ${data.windowDays} days.`}>
        <KindTable kinds={data.byKind} caption="Team work by kind of record" />
      </Card>
    </div>
  )
}

function Loaded<T>({ q, what, children }: { q: { isPending: boolean; isError: boolean; data: T | undefined; refetch: () => unknown }; what: string; children: (d: T) => ReactNode }) {
  if (q.isPending) return <LoadingRows rows={4} />
  if (q.isError || !q.data) return <ErrorState title={`${what} could not be loaded`} retry={() => void q.refetch()} />
  return <>{children(q.data)}</>
}

export function DashboardPage() {
  const { user, manager } = useAuth()
  const [view, setView] = useState<'me' | 'team'>('me')
  const me = useQuery({ queryKey: ['dashboard', 'me'], queryFn: () => dashboardApi(manager.api).me() })
  const isManager = me.data?.isManager === true
  const team = useQuery({ queryKey: ['dashboard', 'team'], queryFn: () => dashboardApi(manager.api).team(), enabled: isManager && view === 'team' })
  const active = view === 'team' && isManager ? team : me

  return (
    <>
      <PageHeader
        title="Dashboard"
        subtitle={`Welcome, ${user?.fullName ?? ''}. ${view === 'team' && isManager ? 'The whole team at a glance.' : 'Here is what you have done and what is waiting.'}`}
        actions={(
          <button type="button" onClick={() => void active.refetch()} disabled={active.isFetching} className="inline-flex items-center gap-1.5 rounded-md border bg-card px-3 py-1.5 text-xs font-medium hover:bg-muted disabled:opacity-60">
            <RefreshCw size={14} className={active.isFetching ? 'animate-spin' : ''} aria-hidden /> Refresh
            {active.dataUpdatedAt > 0 && <span className="text-muted-foreground">· {new Date(active.dataUpdatedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</span>}
          </button>
        )}
      />
      {isManager && (
        <Tabs className="mb-4">
          <TabsList role="tablist" aria-label="Dashboard view">
            <TabsTrigger role="tab" aria-selected={view === 'me'} active={view === 'me'} onClick={() => setView('me')}>My work</TabsTrigger>
            <TabsTrigger role="tab" aria-selected={view === 'team'} active={view === 'team'} onClick={() => setView('team')}>Team</TabsTrigger>
          </TabsList>
        </Tabs>
      )}
      {view === 'team' && isManager
        ? <TabsContent className="mt-0"><Loaded q={team} what="The team view">{(d) => <TeamView data={d} />}</Loaded></TabsContent>
        : <Loaded q={me} what="Your work">{(d) => <MyWork data={d} />}</Loaded>}
      <p className="mt-6 flex items-center gap-1.5 text-xs text-muted-foreground">
        <Clock size={12} aria-hidden />
        Counts come from the records themselves and update when you refresh.
      </p>
    </>
  )
}
