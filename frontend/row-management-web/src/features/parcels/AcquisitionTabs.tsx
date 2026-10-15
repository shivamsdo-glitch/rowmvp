import { useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Pencil, Trash2 } from 'lucide-react'
import {
  OUTCOME_LABEL, REPORT_KIND_LABEL, REVIEW_DISCIPLINES, REVIEW_OUTCOMES, ROE_STATUSES, acquisitionApi,
  type ReportKind, type ReviewDiscipline, type ReviewOutcome, type RightOfEntry, type RoeEffective, type RoeStatus, type StaffReport,
} from '@/api/acquisition'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { DataTable, dateText, fieldErrorsOf, FormError, messageOf, orNull, Pill, SelectField, TextField } from '../projects/form-kit'

const ROE_OPTIONS = ROE_STATUSES.map((s) => ({ value: s, label: s }))
const DISCIPLINE_OPTIONS = REVIEW_DISCIPLINES.map((d) => ({ value: d, label: d }))
const OUTCOME_OPTIONS = REVIEW_OUTCOMES.map((o) => ({ value: o, label: OUTCOME_LABEL[o] }))

export function roeTone(s: RoeEffective): 'ok' | 'warn' | 'neutral' {
  return s === 'Granted' ? 'ok' : s === 'Requested' || s === 'Expired' ? 'warn' : 'neutral'
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section aria-label={title} className="mb-6 rounded-lg border bg-card p-4">
      <h2 className="mb-3 text-sm font-semibold">{title}</h2>
      {children}
    </section>
  )
}

/** Runs a save or delete, keeping the form's errors and busy state and refreshing this parcel's lists. */
function useAction(parcelId: string) {
  const queryClient = useQueryClient()
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  return {
    error, fields, busy,
    clear: () => { setError(null); setFields({}) },
    async run(action: () => Promise<unknown>): Promise<boolean> {
      setError(null)
      setFields({})
      setBusy(true)
      try {
        await action()
        await queryClient.invalidateQueries({ queryKey: ['parcel-acquisition', parcelId] })
        await queryClient.invalidateQueries({ queryKey: ['roe-report'] })
        return true
      } catch (e) {
        const fe = fieldErrorsOf(e)
        setFields(fe)
        setError(Object.keys(fe).length ? 'Please correct the highlighted fields.' : messageOf(e))
        return false
      } finally {
        setBusy(false)
      }
    },
  }
}

// ── Right of entry (ROW-077) ────────────────────────────────────────────

export function RightOfEntryTab({ parcelId, editable }: { parcelId: string; editable: boolean }) {
  const { manager } = useAuth()
  const api = acquisitionApi(manager.api)
  const list = useQuery({ queryKey: ['parcel-acquisition', parcelId, 'roe'], queryFn: () => api.roes(parcelId) })
  const a = useAction(parcelId)
  const [editing, setEditing] = useState<RightOfEntry | null>(null)
  const [status, setStatus] = useState<RoeStatus | ''>('Requested')
  const [requestedOn, setRequestedOn] = useState('')
  const [grantedOn, setGrantedOn] = useState('')
  const [expiresOn, setExpiresOn] = useState('')
  const [grantedBy, setGrantedBy] = useState('')
  const [purpose, setPurpose] = useState('')
  const [notes, setNotes] = useState('')
  const [local, setLocal] = useState<Record<string, string>>({})
  const canAdd = editable && manager.can('parcels', 'create')
  const canEdit = editable && manager.can('parcels', 'update')
  const canRemove = editable && manager.can('parcels', 'delete')

  function reset() {
    setEditing(null); setStatus('Requested'); setRequestedOn(''); setGrantedOn(''); setExpiresOn(''); setGrantedBy(''); setPurpose(''); setNotes(''); setLocal({})
  }
  function begin(r: RightOfEntry) {
    a.clear(); setLocal({})
    setEditing(r); setStatus(r.status); setRequestedOn(r.requestedOn ?? ''); setGrantedOn(r.grantedOn ?? ''); setExpiresOn(r.expiresOn ?? '')
    setGrantedBy(r.grantedBy ?? ''); setPurpose(r.purpose ?? ''); setNotes(r.notes ?? '')
  }

  if (list.isPending) return <LoadingRows rows={3} />
  if (list.isError) return <ErrorState title="Rights of entry could not be loaded" description={list.error.message} retry={() => void list.refetch()} />

  async function submit(e: FormEvent) {
    e.preventDefault()
    const errs: Record<string, string> = {}
    if (!status) errs.status = 'Choose a status.'
    if (status === 'Granted' && !grantedOn) errs.grantedOn = 'Enter the date permission was given.'
    if (grantedOn && expiresOn && expiresOn < grantedOn) errs.expiresOn = 'Permission cannot end before it was given.'
    setLocal(errs)
    if (Object.keys(errs).length) return
    const body = {
      status: status as RoeStatus, requestedOn: orNull(requestedOn), grantedOn: orNull(grantedOn), expiresOn: orNull(expiresOn),
      grantedBy: orNull(grantedBy), purpose: orNull(purpose), notes: orNull(notes),
    }
    if (await a.run(() => (editing ? api.updateRoe(parcelId, editing.id, body) : api.addRoe(parcelId, body)))) reset()
  }
  const errors = { ...a.fields, ...local }
  const showActions = canEdit || canRemove

  return (
    <>
      <Section title="Rights of entry">
        <p className="mb-3 text-xs text-muted-foreground">
          Permission to enter this parcel before it is acquired, for surveys, inspections and testing. A granted right reads &ldquo;Expired&rdquo; the day after its end date.
        </p>
        {list.data.length === 0 ? (
          <EmptyState title="No rights of entry" description="Record a request for permission to enter, and whether the owner granted it." />
        ) : (
          <DataTable caption="Rights of entry" headers={['Status', 'Requested', 'Granted', 'Ends', 'Given by', 'Purpose', ...(showActions ? ['Actions'] : [])]}>
            {list.data.map((r) => (
              <tr key={r.id}>
                <td className="px-3 py-2"><Pill tone={roeTone(r.effectiveStatus)}>{r.effectiveStatus}</Pill></td>
                <td className="px-3 py-2">{dateText(r.requestedOn)}</td>
                <td className="px-3 py-2">{dateText(r.grantedOn)}</td>
                <td className="px-3 py-2">{dateText(r.expiresOn)}</td>
                <td className="px-3 py-2">{r.grantedBy ?? '—'}</td>
                <td className="px-3 py-2">{r.purpose ?? '—'}</td>
                {showActions && (
                  <td className="px-3 py-2">
                    <span className="inline-flex gap-1">
                      {canEdit && <button type="button" aria-label={`Edit the ${r.effectiveStatus.toLowerCase()} right of entry`} className={buttonClassName('ghost', 'sm')} onClick={() => begin(r)}><Pencil size={14} aria-hidden /></button>}
                      {canRemove && (
                        <button type="button" aria-label={`Remove the ${r.effectiveStatus.toLowerCase()} right of entry`} disabled={a.busy} className={buttonClassName('ghost', 'sm')} onClick={() => void a.run(() => api.removeRoe(parcelId, r.id))}>
                          <Trash2 size={14} aria-hidden />
                        </button>
                      )}
                    </span>
                  </td>
                )}
              </tr>
            ))}
          </DataTable>
        )}
      </Section>
      {((canAdd && !editing) || (canEdit && editing)) && (
        <form onSubmit={submit} noValidate aria-label={editing ? 'Change right of entry' : 'Record a right of entry'} className="max-w-3xl space-y-3 rounded-lg border bg-card p-4">
          <h3 className="text-sm font-semibold">{editing ? 'Change this right of entry' : 'Record a right of entry'}</h3>
          <div className="grid gap-3 sm:grid-cols-2">
            <SelectField label="Status" required value={status} onChange={setStatus} options={ROE_OPTIONS} error={errors.status} />
            <TextField label="Requested on" type="date" value={requestedOn} onChange={setRequestedOn} error={errors.requestedOn} />
            <TextField label="Permission given on" type="date" value={grantedOn} onChange={setGrantedOn} error={errors.grantedOn} hint="Needed when the status is Granted." />
            <TextField label="Permission ends on" type="date" value={expiresOn} onChange={setExpiresOn} error={errors.expiresOn} />
            <TextField label="Given by" value={grantedBy} onChange={setGrantedBy} maxLength={200} error={errors.grantedBy} hint="The owner or their representative." />
            <TextField label="Purpose" value={purpose} onChange={setPurpose} maxLength={500} error={errors.purpose} />
          </div>
          <TextField label="Notes" value={notes} onChange={setNotes} maxLength={1000} error={errors.notes} />
          <FormError error={a.error} />
          <div className="flex gap-2">
            <button type="submit" disabled={a.busy} className={buttonClassName('default')}>{editing ? 'Save changes' : 'Record'}</button>
            {editing && <button type="button" className={buttonClassName('outline')} onClick={reset}>Cancel</button>}
          </div>
        </form>
      )}
    </>
  )
}

// ── Plan reviews (ROW-239) and staff reports (ROW-240) ──────────────────

const STATUS_TONE: Record<StaffReport['status'], 'ok' | 'warn' | 'neutral'> = { Draft: 'neutral', Submitted: 'warn', Approved: 'ok', Rejected: 'warn' }

export function ReviewsReportsTab({ parcelId, editable }: { parcelId: string; editable: boolean }) {
  return (
    <>
      <PlanReviewsSection parcelId={parcelId} editable={editable} />
      <ReportsSection parcelId={parcelId} editable={editable} />
    </>
  )
}

function PlanReviewsSection({ parcelId, editable }: { parcelId: string; editable: boolean }) {
  const { manager } = useAuth()
  const api = acquisitionApi(manager.api)
  const reviews = useQuery({ queryKey: ['parcel-acquisition', parcelId, 'reviews'], queryFn: () => api.planReviews(parcelId) })
  const a = useAction(parcelId)
  const [discipline, setDiscipline] = useState<ReviewDiscipline | ''>('Engineer')
  const [outcome, setOutcome] = useState<ReviewOutcome | ''>('Approved')
  const [reviewedOn, setReviewedOn] = useState('')
  const [reviewer, setReviewer] = useState('')
  const [notes, setNotes] = useState('')
  const [local, setLocal] = useState<Record<string, string>>({})
  const canAdd = editable && manager.can('parcels', 'update')
  const canRemove = editable && manager.can('parcels', 'delete')

  if (reviews.isPending) return <LoadingRows rows={3} />
  if (reviews.isError) return <ErrorState title="Plan reviews could not be loaded" description={reviews.error.message} retry={() => void reviews.refetch()} />
  const s = reviews.data

  async function submit(e: FormEvent) {
    e.preventDefault()
    const errs: Record<string, string> = {}
    if (!reviewedOn) errs.reviewedOn = 'Choose the review date.'
    if (!reviewer.trim()) errs.reviewerName = 'Enter who reviewed the plans.'
    if (outcome === 'ChangesRequired' && !notes.trim()) errs.notes = 'Say what has to change.'
    setLocal(errs)
    if (Object.keys(errs).length) return
    const ok = await a.run(() => api.addPlanReview(parcelId, {
      discipline: discipline || 'Engineer', outcome: outcome || 'Approved', reviewedOn, reviewerName: reviewer.trim(), notes: orNull(notes),
    }))
    if (ok) { setReviewedOn(''); setReviewer(''); setNotes('') }
  }
  const errors = { ...a.fields, ...local }

  return (
    <Section title="Plan reviews">
      <div role="status" aria-label="Plan review gate" className={`mb-3 rounded-md border px-3 py-2 text-sm ${s.gateEnabled && !s.satisfied ? 'border-warn/60 bg-warn/10' : 'border-ok/50 bg-ok/10'}`}>
        {!s.gateEnabled ? (
          <>The agency does not require plan reviews to move a parcel on. Reviews recorded here are kept for the file.</>
        ) : s.satisfied ? (
          <><strong>Plan reviews complete.</strong> The engineer and the surveyor have approved the plans.</>
        ) : (
          <><strong>This parcel is held at Title &amp; survey.</strong> Waiting for an approved review by the {s.missing.map((m) => m.toLowerCase()).join(' and ')}.</>
        )}
      </div>
      {s.reviews.length === 0 ? (
        <EmptyState title="No plan reviews" description="Record the engineer's and the surveyor's review of the plans." />
      ) : (
        <DataTable caption="Plan reviews" headers={['Date', 'Reviewed by', 'Role', 'Outcome', 'Notes', ...(canRemove ? ['Actions'] : [])]}>
          {s.reviews.map((r) => (
            <tr key={r.id}>
              <td className="px-3 py-2">{dateText(r.reviewedOn)}</td>
              <td className="px-3 py-2">{r.reviewerName}</td>
              <td className="px-3 py-2">{r.discipline}</td>
              <td className="px-3 py-2"><Pill tone={r.outcome === 'Approved' ? 'ok' : 'warn'}>{OUTCOME_LABEL[r.outcome]}</Pill></td>
              <td className="whitespace-pre-wrap px-3 py-2">{r.notes ?? '—'}</td>
              {canRemove && (
                <td className="px-3 py-2">
                  <button type="button" aria-label={`Remove the ${r.discipline.toLowerCase()} review of ${dateText(r.reviewedOn)}`} disabled={a.busy} className={buttonClassName('ghost', 'sm')} onClick={() => void a.run(() => api.removePlanReview(parcelId, r.id))}>
                    <Trash2 size={14} aria-hidden />
                  </button>
                </td>
              )}
            </tr>
          ))}
        </DataTable>
      )}
      {canAdd && (
        <form onSubmit={submit} noValidate aria-label="Record a plan review" className="mt-4 max-w-3xl space-y-3">
          <h3 className="text-sm font-semibold">Record a plan review</h3>
          <div className="grid gap-3 sm:grid-cols-2">
            <SelectField label="Review by" required value={discipline} onChange={setDiscipline} options={DISCIPLINE_OPTIONS} error={errors.discipline} />
            <SelectField label="Outcome" required value={outcome} onChange={setOutcome} options={OUTCOME_OPTIONS} error={errors.outcome} />
            <TextField label="Date reviewed" required type="date" value={reviewedOn} onChange={setReviewedOn} error={errors.reviewedOn} />
            <TextField label="Reviewer" required value={reviewer} onChange={setReviewer} maxLength={200} error={errors.reviewerName} />
          </div>
          <TextField label="Notes" value={notes} onChange={setNotes} maxLength={1000} error={errors.notes} />
          <FormError error={a.error} />
          <button type="submit" disabled={a.busy} className={buttonClassName('default')}>Record review</button>
        </form>
      )}
    </Section>
  )
}

function ReportsSection({ parcelId, editable }: { parcelId: string; editable: boolean }) {
  const { manager } = useAuth()
  const api = acquisitionApi(manager.api)
  const reports = useQuery({ queryKey: ['parcel-acquisition', parcelId, 'reports'], queryFn: () => api.reports(parcelId) })

  if (reports.isPending) return <LoadingRows rows={3} />
  if (reports.isError) return <ErrorState title="Reports could not be loaded" description={reports.error.message} retry={() => void reports.refetch()} />
  // A rejected report is shown until a new one replaces it; any other report is the one in force.
  const live = (kind: ReportKind) =>
    reports.data.filter((r) => r.kind === kind).sort((x, y) => Number(x.status === 'Rejected') - Number(y.status === 'Rejected'))[0] ?? null

  return (
    <Section title="Staff report and certificate of necessity">
      <p className="mb-3 text-xs text-muted-foreground">
        Each is written here, put up for approval and decided by someone other than the person who submitted it.
      </p>
      <div className="grid gap-4 lg:grid-cols-2">
        {(['StaffReport', 'CertificateOfNecessity'] as ReportKind[]).map((kind) => (
          <ReportCard key={kind} parcelId={parcelId} kind={kind} report={live(kind)} editable={editable} />
        ))}
      </div>
    </Section>
  )
}

function ReportCard({ parcelId, kind, report, editable }: { parcelId: string; kind: ReportKind; report: StaffReport | null; editable: boolean }) {
  const { manager } = useAuth()
  const api = acquisitionApi(manager.api)
  const a = useAction(parcelId)
  const label = REPORT_KIND_LABEL[kind]
  const [open, setOpen] = useState(false)
  const [title, setTitle] = useState('')
  const [body, setBody] = useState('')
  const [note, setNote] = useState('')
  const [local, setLocal] = useState<Record<string, string>>({})
  const canCreate = editable && manager.can('parcels', 'create')
  const canUpdate = editable && manager.can('parcels', 'update')
  const canDelete = editable && manager.can('parcels', 'delete')
  const canDecide = editable && manager.can('approvals', 'update')

  function begin() {
    a.clear(); setLocal({})
    setTitle(report?.title ?? label); setBody(report?.body ?? ''); setOpen(true)
  }
  async function save(e: FormEvent) {
    e.preventDefault()
    const errs: Record<string, string> = {}
    if (!title.trim()) errs.title = 'Enter a title.'
    if (!body.trim()) errs.body = 'Write the report.'
    setLocal(errs)
    if (Object.keys(errs).length) return
    const editingLive = report && report.status !== 'Rejected' ? report : report && report.status === 'Rejected' ? report : null
    const ok = await a.run(() => (editingLive ? api.updateReport(parcelId, editingLive.id, { title: title.trim(), body: body.trim() }) : api.createReport(parcelId, { kind, title: title.trim(), body: body.trim() })))
    if (ok) setOpen(false)
  }
  async function reject() {
    if (!note.trim()) { setLocal({ note: 'Say why the report is rejected.' }); return }
    setLocal({})
    if (report && await a.run(() => api.rejectReport(parcelId, report.id, note.trim()))) setNote('')
  }
  const errors = { ...a.fields, ...local }

  return (
    <article aria-label={label} className="rounded-md border p-3">
      <h3 className="mb-2 flex items-center gap-2 text-sm font-semibold">{label} {report && <Pill tone={STATUS_TONE[report.status]}>{report.status}</Pill>}</h3>
      {!report && !open && (
        <>
          <p className="mb-2 text-sm text-muted-foreground">No {label.toLowerCase()} has been written for this parcel.</p>
          {canCreate && <button type="button" className={buttonClassName('outline', 'sm')} onClick={begin}>Write the {label.toLowerCase()}</button>}
        </>
      )}
      {report && !open && (
        <>
          <p className="text-sm font-medium">{report.title}</p>
          <p className="mb-2 mt-1 whitespace-pre-wrap text-sm">{report.body}</p>
          {report.decisionNote && <p className="mb-2 text-xs text-muted-foreground">Decision note: {report.decisionNote}</p>}
          {report.submittedAt && <p className="mb-2 text-xs text-muted-foreground">Submitted {dateText(report.submittedAt)}{report.submittedByMe ? ' by you' : ''}.</p>}
          <div className="flex flex-wrap gap-2">
            {report.canEdit && canUpdate && <button type="button" className={buttonClassName('outline', 'sm')} onClick={begin}>Change the {label.toLowerCase()}</button>}
            {report.canEdit && canUpdate && <button type="button" disabled={a.busy} className={buttonClassName('default', 'sm')} onClick={() => void a.run(() => api.submitReport(parcelId, report.id))}>Submit for approval</button>}
            {report.canEdit && canDelete && <button type="button" disabled={a.busy} className={buttonClassName('ghost', 'sm')} aria-label={`Delete the ${label.toLowerCase()}`} onClick={() => void a.run(() => api.deleteReport(parcelId, report.id))}><Trash2 size={14} aria-hidden /></button>}
          </div>
          {report.status === 'Rejected' && canCreate && <p className="mt-2 text-xs text-muted-foreground">Change it and submit it again.</p>}
          {report.status === 'Submitted' && canDecide && !report.submittedByMe && (
            <div className="mt-3 space-y-2 border-t pt-3">
              <TextField label="Note (required to reject)" value={note} onChange={setNote} maxLength={1000} error={errors.note} />
              <div className="flex gap-2">
                <button type="button" disabled={a.busy} className={buttonClassName('default', 'sm')} onClick={() => void a.run(() => api.approveReport(parcelId, report.id, orNull(note)))}>Approve</button>
                <button type="button" disabled={a.busy} className={buttonClassName('outline', 'sm')} onClick={() => void reject()}>Reject</button>
              </div>
            </div>
          )}
          {report.status === 'Submitted' && report.submittedByMe && <p className="mt-2 text-xs text-muted-foreground">You submitted this, so someone else has to approve or reject it.</p>}
        </>
      )}
      {open && (
        <form onSubmit={save} noValidate aria-label={`${report ? 'Change' : 'Write'} the ${label.toLowerCase()}`} className="space-y-3">
          <TextField label="Title" required value={title} onChange={setTitle} maxLength={200} error={errors.title} />
          <TextField label="Report" required value={body} onChange={setBody} maxLength={8000} error={errors.body} />
          <FormError error={a.error} />
          <div className="flex gap-2">
            <button type="submit" disabled={a.busy} className={buttonClassName('default', 'sm')}>Save</button>
            <button type="button" className={buttonClassName('outline', 'sm')} onClick={() => setOpen(false)}>Cancel</button>
          </div>
        </form>
      )}
      {!open && <FormError error={a.error} />}
    </article>
  )
}
