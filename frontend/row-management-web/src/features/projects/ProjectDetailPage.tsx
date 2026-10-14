import { useState, type FormEvent, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Trash2 } from 'lucide-react'
import {
  AUTHORIZATION_TYPES, EXCEPTION_TYPES, NEPA_LABEL, NEPA_TYPES, projectsApi,
  type AcquisitionExceptionType, type AuthorizationType, type NepaDecisionType, type Project,
} from '@/api/projects'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { PageHeader } from '@/components/ui/page-header'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import {
  CheckboxField, DataTable, dateText, fieldErrorsOf, FormError, messageOf, money, numOrNull, orNull, Pill, SelectField, TextField,
} from './form-kit'
import { ProjectParcelsTab } from '../parcels/ProjectParcelsTab'
import { ProjectForm } from './ProjectForm'

const TABS = [
  ['overview', 'Overview'], ['parcels', 'Parcels'], ['estimates', 'Cost estimates'], ['budget', 'Budget'], ['authorizations', 'ROW authorizations'], ['clearance', 'Clearance'],
] as const
type TabKey = (typeof TABS)[number][0]

function GateBanner({ project }: { project: Project }) {
  const g = project.acquisitionGate
  const why: Record<string, string> = {
    UngatedFunding: 'This funding source is not gated; agency rules apply.',
    ClearanceComplete: 'NEPA decision and an acquisition authorization are on file.',
    ExceptionApproved: `An ${project.exceptionType?.toLowerCase()} acquisition exception is approved.`,
  }
  return (
    <div
      role="status"
      aria-label="Acquisition status"
      className={`mb-5 rounded-lg border px-4 py-3 text-sm ${g.unlocked ? 'border-ok/50 bg-ok/10' : 'border-warn/60 bg-warn/10'}`}
    >
      {g.unlocked ? (
        <><strong>Offers and negotiations are open.</strong> {why[g.reason]}</>
      ) : (
        <><strong>Offers and negotiations are on hold.</strong> Missing: {g.missing.join(' and ')}. Title, survey and preliminary appraisal can continue.</>
      )}
    </div>
  )
}

export function ProjectDetailPage({ projectId }: { projectId: string }) {
  const { manager } = useAuth()
  const api = projectsApi(manager.api)
  const [tab, setTab] = useState<TabKey>('overview')
  const project = useQuery({ queryKey: ['project', projectId], queryFn: () => api.get(projectId) })

  if (project.isPending) return <LoadingRows />
  if (project.isError) {
    return (
      <ErrorState
        title="This project could not be opened"
        description={project.error.message}
        retry={() => void project.refetch()}
      />
    )
  }
  const p = project.data

  return (
    <>
      <PageHeader
        title={`${p.number} · ${p.name}`}
        subtitle={[p.region, p.district].filter(Boolean).join(' / ') || undefined}
        crumbs={[{ label: 'Projects', to: '/projects' }, { label: p.number }]}
      />
      <GateBanner project={p} />
      <Tabs>
        <TabsList role="tablist" aria-label="Project sections">
          {TABS.filter(([key]) => key !== 'parcels' || manager.can('parcels')).map(([key, label]) => (
            <TabsTrigger key={key} role="tab" id={`tab-${key}`} aria-selected={tab === key} aria-controls={`panel-${key}`} active={tab === key} onClick={() => setTab(key)}>
              {label}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`}>
          {tab === 'overview' && <OverviewTab project={p} />}
          {tab === 'parcels' && <ProjectParcelsTab projectId={p.id} />}
          {tab === 'estimates' && <EstimatesTab project={p} />}
          {tab === 'budget' && <BudgetTab project={p} />}
          {tab === 'authorizations' && <AuthorizationsTab project={p} />}
          {tab === 'clearance' && <ClearanceTab project={p} />}
        </TabsContent>
      </Tabs>
    </>
  )
}

function useProjectApi(projectId: string) {
  const { manager } = useAuth()
  const queryClient = useQueryClient()
  return {
    manager,
    api: projectsApi(manager.api),
    refreshProject: () => Promise.all([
      queryClient.invalidateQueries({ queryKey: ['project', projectId] }),
      queryClient.invalidateQueries({ queryKey: ['projects'] }),
    ]),
    queryClient,
  }
}

function Facts({ items }: { items: [string, ReactNode][] }) {
  return (
    <dl className="grid gap-x-8 gap-y-3 sm:grid-cols-2 lg:grid-cols-3">
      {items.map(([k, v]) => (
        <div key={k}>
          <dt className="text-xs text-muted-foreground">{k}</dt>
          <dd className="text-sm font-medium">{v ?? '—'}</dd>
        </div>
      ))}
    </dl>
  )
}

// ── Overview ─────────────────────────────────────────────────────────────

function OverviewTab({ project: p }: { project: Project }) {
  const { manager, api, refreshProject } = useProjectApi(p.id)
  const canUpdate = manager.can('projects', 'update')
  const [editing, setEditing] = useState(false)

  if (editing) {
    return (
      <section aria-label="Edit project">
        <ProjectForm
          project={p}
          submitLabel="Save changes"
          onCancel={() => setEditing(false)}
          onSubmit={async (input) => {
            await api.update(p.id, input)
            await refreshProject()
            setEditing(false)
          }}
        />
      </section>
    )
  }
  return (
    <section aria-label="Project details" className="space-y-4">
      <Facts items={[
        ['Project number', p.number], ['Name', p.name], ['Funding oversight', p.oversightType],
        ['Funding source', p.fundingSource], ['Funding amount', money(p.fundingAmount)], ['Design estimate', money(p.designEstimate)],
        ['Current estimate', money(p.currentEstimate)], ['Letting date', dateText(p.lettingDate)], ['Plan turn-in', dateText(p.planTurnInDate)],
        ['Construction start', dateText(p.constructionStartDate)], ['Construction end', dateText(p.constructionEndDate)],
      ]} />
      {p.description && <p className="text-sm text-muted-foreground">{p.description}</p>}
      {canUpdate && <button type="button" className={buttonClassName('outline')} onClick={() => setEditing(true)}>Edit project</button>}
    </section>
  )
}

// ── Cost estimates (ROW-012) ─────────────────────────────────────────────

function EstimatesTab({ project: p }: { project: Project }) {
  const { manager, api, refreshProject, queryClient } = useProjectApi(p.id)
  const canUpdate = manager.can('projects', 'update')
  const estimates = useQuery({ queryKey: ['project', p.id, 'estimates'], queryFn: () => api.estimates(p.id) })
  const [open, setOpen] = useState(false)
  const [v, setV] = useState({ landCost: '', improvementsCost: '', relocationCost: '', indirectCost: '', notes: '' })
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const add = useMutation({
    mutationFn: () => api.addEstimate(p.id, {
      landCost: numOrNull(v.landCost) ?? 0, improvementsCost: numOrNull(v.improvementsCost) ?? 0,
      relocationCost: numOrNull(v.relocationCost) ?? 0, indirectCost: numOrNull(v.indirectCost) ?? 0, notes: orNull(v.notes),
    }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['project', p.id, 'estimates'] })
      await refreshProject()
      setOpen(false)
      setV({ landCost: '', improvementsCost: '', relocationCost: '', indirectCost: '', notes: '' })
    },
    onError: (e) => { setErrors(fieldErrorsOf(e)); setFormError(messageOf(e)) },
  })
  const set = (k: keyof typeof v) => (x: string) => setV((s) => ({ ...s, [k]: x }))
  function submit(e: FormEvent) { e.preventDefault(); setErrors({}); setFormError(null); add.mutate() }

  return (
    <section aria-label="Cost estimates" className="space-y-4">
      <p className="text-sm text-muted-foreground">Each save creates a new version. Earlier versions are kept and never edited.</p>
      {estimates.isPending ? <LoadingRows rows={3} /> : estimates.isError ? (
        <ErrorState title="Estimates could not be loaded" retry={() => void estimates.refetch()} />
      ) : estimates.data.length === 0 ? (
        <EmptyState title="No estimate yet" description={canUpdate ? 'Add the first estimate version.' : undefined} />
      ) : (
        <DataTable caption="Cost estimate versions" headers={['Version', 'Land', 'Improvements', 'Relocation', 'Indirect', 'Total', 'Saved', 'Notes']}>
          {estimates.data.map((e, i) => (
            <tr key={e.id}>
              <td className="px-3 py-2 font-medium">v{e.version}{i === 0 && <span className="ml-2"><Pill tone="neutral">Current</Pill></span>}</td>
              <td className="px-3 py-2">{money(e.landCost)}</td><td className="px-3 py-2">{money(e.improvementsCost)}</td>
              <td className="px-3 py-2">{money(e.relocationCost)}</td><td className="px-3 py-2">{money(e.indirectCost)}</td>
              <td className="px-3 py-2 font-medium">{money(e.total)}</td><td className="px-3 py-2">{dateText(e.createdAt)}</td>
              <td className="px-3 py-2">{e.notes ?? '—'}</td>
            </tr>
          ))}
        </DataTable>
      )}
      {canUpdate && !open && <button type="button" className={buttonClassName('default')} onClick={() => setOpen(true)}>Add estimate version</button>}
      {canUpdate && open && (
        <form onSubmit={submit} noValidate aria-label="New estimate version" className="space-y-3 rounded-lg border bg-card p-4">
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <TextField label="Land ($)" type="number" value={v.landCost} onChange={set('landCost')} error={errors.landCost} />
            <TextField label="Improvements ($)" type="number" value={v.improvementsCost} onChange={set('improvementsCost')} error={errors.improvementsCost} />
            <TextField label="Relocation ($)" type="number" value={v.relocationCost} onChange={set('relocationCost')} error={errors.relocationCost} />
            <TextField label="Indirect ($)" type="number" value={v.indirectCost} onChange={set('indirectCost')} error={errors.indirectCost}
              hint="Appraisal, negotiation, closing, title insurance" />
          </div>
          <TextField label="Notes" value={v.notes} onChange={set('notes')} error={errors.notes} maxLength={1000} />
          <FormError error={formError} />
          <div className="flex gap-2">
            <button type="submit" className={buttonClassName('default')} disabled={add.isPending}>{add.isPending ? 'Saving…' : 'Save new version'}</button>
            <button type="button" className={buttonClassName('outline')} onClick={() => setOpen(false)}>Cancel</button>
          </div>
        </form>
      )}
    </section>
  )
}

// ── Annual budget (ROW-013) ──────────────────────────────────────────────

function BudgetTab({ project: p }: { project: Project }) {
  const { manager, api, queryClient } = useProjectApi(p.id)
  const canUpdate = manager.can('projects', 'update')
  const canDelete = manager.can('projects', 'delete')
  const budgets = useQuery({ queryKey: ['project', p.id, 'budgets'], queryFn: () => api.budgets(p.id) })
  const [v, setV] = useState({ fiscalYear: '', amount: '', notes: '' })
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['project', p.id, 'budgets'] })
  const save = useMutation({
    mutationFn: () => api.setBudget(p.id, Number(v.fiscalYear), numOrNull(v.amount) ?? NaN, orNull(v.notes)),
    onSuccess: async () => { await refresh(); setV({ fiscalYear: '', amount: '', notes: '' }) },
    onError: (e) => { setErrors(fieldErrorsOf(e)); setFormError(messageOf(e)) },
  })
  const remove = useMutation({
    mutationFn: (fy: number) => api.deleteBudget(p.id, fy),
    onSuccess: refresh,
    onError: (e) => setFormError(messageOf(e)),
  })
  function submit(e: FormEvent) {
    e.preventDefault()
    setErrors({}); setFormError(null)
    const local: Record<string, string> = {}
    if (!/^\d{4}$/.test(v.fiscalYear.trim())) local.fiscalYear = 'Enter a four-digit fiscal year.'
    if (v.amount.trim() === '' || Number.isNaN(Number(v.amount))) local.amount = 'Enter the budget amount.'
    setErrors(local)
    if (Object.keys(local).length === 0) save.mutate()
  }
  const total = budgets.data?.reduce((s, b) => s + b.amount, 0) ?? 0

  return (
    <section aria-label="Annual budget" className="space-y-4">
      <p className="text-sm text-muted-foreground">One budget per fiscal year. Setting a year again adjusts it; the change history is in the audit log.</p>
      {budgets.isPending ? <LoadingRows rows={3} /> : budgets.isError ? (
        <ErrorState title="Budgets could not be loaded" retry={() => void budgets.refetch()} />
      ) : budgets.data.length === 0 ? (
        <EmptyState title="No budget yet" description={canUpdate ? 'Set the first fiscal year below.' : undefined} />
      ) : (
        <>
          <DataTable caption="Annual ROW budgets" headers={canDelete ? ['Fiscal year', 'Amount', 'Notes', 'Actions'] : ['Fiscal year', 'Amount', 'Notes']}>
            {budgets.data.map((b) => (
              <tr key={b.id}>
                <td className="px-3 py-2 font-medium">FY{b.fiscalYear}</td><td className="px-3 py-2">{money(b.amount)}</td><td className="px-3 py-2">{b.notes ?? '—'}</td>
                {canDelete && (
                  <td className="px-3 py-2">
                    <button type="button" aria-label={`Delete FY${b.fiscalYear} budget`} className={buttonClassName('ghost', 'sm')} onClick={() => remove.mutate(b.fiscalYear)}>
                      <Trash2 size={14} />
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </DataTable>
          <p className="text-sm">Total budgeted: <strong>{money(total)}</strong></p>
        </>
      )}
      {canUpdate && (
        <form onSubmit={submit} noValidate aria-label="Set a fiscal-year budget" className="space-y-3 rounded-lg border bg-card p-4">
          <div className="grid gap-3 sm:grid-cols-3">
            <TextField label="Fiscal year" value={v.fiscalYear} onChange={(x) => setV((s) => ({ ...s, fiscalYear: x }))} error={errors.fiscalYear} inputMode="numeric" maxLength={4} />
            <TextField label="Budget ($)" type="number" value={v.amount} onChange={(x) => setV((s) => ({ ...s, amount: x }))} error={errors.amount} />
            <TextField label="Notes" value={v.notes} onChange={(x) => setV((s) => ({ ...s, notes: x }))} error={errors.notes} maxLength={1000} />
          </div>
          <FormError error={formError} />
          <button type="submit" className={buttonClassName('default')} disabled={save.isPending}>{save.isPending ? 'Saving…' : 'Set budget'}</button>
        </form>
      )}
      {!canUpdate && <FormError error={formError} />}
    </section>
  )
}

// ── ROW authorizations (ROW-238) ─────────────────────────────────────────

function AuthorizationsTab({ project: p }: { project: Project }) {
  const { manager, api, refreshProject, queryClient } = useProjectApi(p.id)
  const canUpdate = manager.can('projects', 'update')
  const canDelete = manager.can('projects', 'delete')
  const list = useQuery({ queryKey: ['project', p.id, 'authorizations'], queryFn: () => api.authorizations(p.id) })
  const blank = { type: 'Acquisition' as AuthorizationType | '', amount: '', share: '', on: '', ref: '', programmed: false, notes: '' }
  const [v, setV] = useState(blank)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const refresh = async () => { await queryClient.invalidateQueries({ queryKey: ['project', p.id, 'authorizations'] }); await refreshProject() }
  const add = useMutation({
    mutationFn: () => api.addAuthorization(p.id, {
      type: v.type as AuthorizationType, authorizedAmount: numOrNull(v.amount) ?? NaN, federalSharePercent: numOrNull(v.share),
      authorizedOn: v.on, referenceNumber: orNull(v.ref), programmingConfirmed: v.programmed, notes: orNull(v.notes),
    }),
    onSuccess: async () => { await refresh(); setV(blank) },
    onError: (e) => { setErrors(fieldErrorsOf(e)); setFormError(messageOf(e)) },
  })
  const remove = useMutation({
    mutationFn: (id: string) => api.deleteAuthorization(p.id, id),
    onSuccess: refresh,
    onError: (e) => setFormError(messageOf(e)),
  })
  function submit(e: FormEvent) {
    e.preventDefault()
    setErrors({}); setFormError(null)
    const local: Record<string, string> = {}
    if (v.amount.trim() === '' || Number.isNaN(Number(v.amount))) local.authorizedAmount = 'Enter the authorized amount.'
    if (!v.on) local.authorizedOn = 'Enter the authorization date.'
    setErrors(local)
    if (Object.keys(local).length === 0) add.mutate()
  }

  return (
    <section aria-label="ROW authorizations" className="space-y-4">
      <p className="text-sm text-muted-foreground">
        A preliminary authorization covers appraisal and title only. An acquisition or supplemental authorization is what allows offers and negotiations.
      </p>
      {list.isPending ? <LoadingRows rows={3} /> : list.isError ? (
        <ErrorState title="Authorizations could not be loaded" retry={() => void list.refetch()} />
      ) : list.data.length === 0 ? (
        <EmptyState title="No ROW authorizations yet" />
      ) : (
        <DataTable caption="ROW authorizations" headers={['Type', 'Authorized', 'Federal share', 'Date', 'Reference', 'Programmed', ...(canDelete ? ['Actions'] : [])]}>
          {list.data.map((a) => (
            <tr key={a.id}>
              <td className="px-3 py-2 font-medium">{a.type}</td>
              <td className="px-3 py-2">{money(a.authorizedAmount)}</td>
              <td className="px-3 py-2">{a.federalSharePercent === null ? '—' : `${a.federalSharePercent}% (${money(a.federalShareAmount)})`}</td>
              <td className="px-3 py-2">{dateText(a.authorizedOn)}</td>
              <td className="px-3 py-2">{a.referenceNumber ?? '—'}</td>
              <td className="px-3 py-2">{a.programmingConfirmed ? 'Yes' : 'No'}</td>
              {canDelete && (
                <td className="px-3 py-2">
                  <button type="button" aria-label={`Delete ${a.type} authorization`} className={buttonClassName('ghost', 'sm')} onClick={() => remove.mutate(a.id)}>
                    <Trash2 size={14} />
                  </button>
                </td>
              )}
            </tr>
          ))}
        </DataTable>
      )}
      {!canUpdate && <FormError error={formError} />}
      {canUpdate && (
        <form onSubmit={submit} noValidate aria-label="Add a ROW authorization" className="space-y-3 rounded-lg border bg-card p-4">
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            <SelectField label="Authorization type" required value={v.type} onChange={(x) => setV((s) => ({ ...s, type: x }))}
              options={AUTHORIZATION_TYPES.map((t) => ({ value: t, label: t }))} error={errors.type} />
            <TextField label="Authorized amount ($)" required type="number" value={v.amount} onChange={(x) => setV((s) => ({ ...s, amount: x }))} error={errors.authorizedAmount} />
            <TextField label="Federal share (%)" type="number" value={v.share} onChange={(x) => setV((s) => ({ ...s, share: x }))} error={errors.federalSharePercent} hint="Leave blank if not federal-aid" />
            <TextField label="Authorization date" required type="date" value={v.on} onChange={(x) => setV((s) => ({ ...s, on: x }))} error={errors.authorizedOn} />
            <TextField label="Reference number" value={v.ref} onChange={(x) => setV((s) => ({ ...s, ref: x }))} error={errors.referenceNumber} maxLength={100} />
            <CheckboxField label="Programming (STIP / TIP) confirmed" checked={v.programmed} onChange={(x) => setV((s) => ({ ...s, programmed: x }))} />
          </div>
          <FormError error={formError} />
          <button type="submit" className={buttonClassName('default')} disabled={add.isPending}>{add.isPending ? 'Saving…' : 'Add authorization'}</button>
        </form>
      )}
    </section>
  )
}

// ── Clearance: NEPA decision and acquisition exception (ROW-076) ─────────

function ClearanceTab({ project: p }: { project: Project }) {
  const { manager, api, refreshProject } = useProjectApi(p.id)
  const canUpdate = manager.can('projects', 'update')
  const [nepaType, setNepaType] = useState<NepaDecisionType | ''>(p.nepaDecisionType ?? '')
  const [nepaDate, setNepaDate] = useState(p.nepaDecisionDate ?? '')
  const [excType, setExcType] = useState<AcquisitionExceptionType | ''>(p.exceptionType ?? '')
  const [excDate, setExcDate] = useState(p.exceptionApprovedOn ?? '')
  const [excReason, setExcReason] = useState(p.exceptionReason ?? '')
  const [nepaErr, setNepaErr] = useState<Record<string, string>>({})
  const [excErr, setExcErr] = useState<Record<string, string>>({})
  const [msg, setMsg] = useState<{ form: 'nepa' | 'exception'; text: string; ok: boolean } | null>(null)

  async function saveNepa(e: FormEvent) {
    e.preventDefault()
    setNepaErr({}); setMsg(null)
    if (nepaType && !nepaDate) return setNepaErr({ decisionDate: 'A decision date is required.' })
    try {
      await api.recordNepa(p.id, nepaType || null, nepaType ? nepaDate : null)
      await refreshProject()
      setMsg({ form: 'nepa', text: 'NEPA decision saved.', ok: true })
    } catch (err) { setNepaErr(fieldErrorsOf(err)); setMsg({ form: 'nepa', text: messageOf(err), ok: false }) }
  }
  async function saveException(e: FormEvent) {
    e.preventDefault()
    setExcErr({}); setMsg(null)
    const local: Record<string, string> = {}
    if (excType && !excDate) local.approvedOn = 'An approval date is required.'
    if (excType && !excReason.trim()) local.reason = 'A reason is required for an exception.'
    if (Object.keys(local).length) return setExcErr(local)
    try {
      await api.setException(p.id, excType || null, excType ? excDate : null, excType ? excReason : null)
      await refreshProject()
      setMsg({ form: 'exception', text: 'Exception saved.', ok: true })
    } catch (err) { setExcErr(fieldErrorsOf(err)); setMsg({ form: 'exception', text: messageOf(err), ok: false }) }
  }

  return (
    <section aria-label="Clearance" className="grid gap-6 lg:grid-cols-2">
      <form onSubmit={saveNepa} noValidate aria-label="NEPA decision" className="space-y-3 rounded-lg border bg-card p-4">
        <h2 className="text-sm font-semibold">NEPA decision</h2>
        <SelectField label="Decision type" value={nepaType} onChange={setNepaType} blankLabel="Not recorded" disabled={!canUpdate}
          options={NEPA_TYPES.map((t) => ({ value: t, label: NEPA_LABEL[t] }))} />
        <TextField label="Decision date" type="date" value={nepaDate} onChange={setNepaDate} error={nepaErr.decisionDate} disabled={!canUpdate} />
        {msg?.form === 'nepa' && <p role={msg.ok ? 'status' : 'alert'} className={msg.ok ? 'text-sm' : 'text-sm text-danger'}>{msg.text}</p>}
        {canUpdate && <button type="submit" className={buttonClassName('default')}>Save NEPA decision</button>}
      </form>

      <form onSubmit={saveException} noValidate aria-label="Acquisition exception" className="space-y-3 rounded-lg border bg-card p-4">
        <h2 className="text-sm font-semibold">Early, hardship or protective acquisition</h2>
        <p className="text-xs text-muted-foreground">An approved exception allows offers before NEPA and an acquisition authorization are on file.</p>
        <SelectField label="Exception type" value={excType} onChange={setExcType} blankLabel="None" disabled={!canUpdate}
          options={EXCEPTION_TYPES.map((t) => ({ value: t, label: t }))} />
        <TextField label="Approval date" type="date" value={excDate} onChange={setExcDate} error={excErr.approvedOn} disabled={!canUpdate} />
        <TextField label="Reason" value={excReason} onChange={setExcReason} error={excErr.reason} maxLength={1000} disabled={!canUpdate} />
        {msg?.form === 'exception' && <p role={msg.ok ? 'status' : 'alert'} className={msg.ok ? 'text-sm' : 'text-sm text-danger'}>{msg.text}</p>}
        {canUpdate && <button type="submit" className={buttonClassName('default')}>Save exception</button>}
      </form>
    </section>
  )
}
