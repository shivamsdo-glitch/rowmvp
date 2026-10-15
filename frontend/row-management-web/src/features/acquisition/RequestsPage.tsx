import { useState, type FormEvent } from 'react'
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query'
import { REQUEST_STATUSES, acquisitionApi, type AcquisitionRequest, type RequesterType, type RequestStatus } from '@/api/acquisition'
import { projectsApi } from '@/api/projects'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { PageHeader } from '@/components/ui/page-header'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { DataTable, dateText, fieldErrorsOf, FormError, messageOf, money, numOrNull, orNull, Pill, SelectField, TextField } from '../projects/form-kit'

const PAGE_SIZE = 25
const STATUS_OPTIONS = REQUEST_STATUSES.map((s) => ({ value: s, label: s }))
const TYPE_OPTIONS: { value: RequesterType; label: string }[] = [{ value: 'Internal', label: 'Inside the agency' }, { value: 'External', label: 'Outside the agency' }]
const TONE: Record<RequestStatus, 'ok' | 'warn' | 'neutral'> = { Submitted: 'warn', Approved: 'ok', Rejected: 'neutral', Withdrawn: 'neutral' }

/** Requests to acquire a property, from inside or outside the agency, waiting for someone else to approve (ROW-080). */
export function RequestsPage() {
  const { manager } = useAuth()
  const api = acquisitionApi(manager.api)
  const queryClient = useQueryClient()
  const canSubmit = manager.can('projects', 'create')
  const [status, setStatus] = useState<RequestStatus | ''>('')
  const [page, setPage] = useState(1)
  const list = useQuery({ queryKey: ['acquisition-requests', status, page], queryFn: () => api.requests(status, page, PAGE_SIZE), placeholderData: keepPreviousData })
  const totalPages = list.data ? Math.max(1, Math.ceil(list.data.total / PAGE_SIZE)) : 1
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['acquisition-requests'] })

  return (
    <>
      <PageHeader title="Acquisition requests" subtitle="Requests to acquire a property. Someone other than the person who entered a request approves or rejects it." />
      <div className="mb-4 max-w-xs">
        <SelectField label="Status" value={status} onChange={(v) => { setStatus(v); setPage(1) }} options={STATUS_OPTIONS} blankLabel="Any status" />
      </div>

      {list.isPending ? <LoadingRows rows={4} /> : list.isError ? (
        <ErrorState title="Requests could not be loaded" description={list.error.message} retry={() => void list.refetch()} />
      ) : list.data.items.length === 0 ? (
        <EmptyState title={status ? `No ${status.toLowerCase()} requests` : 'No requests yet'} description={canSubmit ? 'Enter one below.' : 'Requests entered by the project team appear here.'} />
      ) : (
        <>
          <ul aria-label="Requests" className="space-y-3">
            {list.data.items.map((r) => <RequestCard key={r.id} request={r} onChanged={refresh} />)}
          </ul>
          <nav aria-label="Pages" className="mt-3 flex items-center justify-between text-sm">
            <span className="text-muted-foreground">{list.data.total} request{list.data.total === 1 ? '' : 's'}</span>
            <span className="flex items-center gap-2">
              <button type="button" className={buttonClassName('outline', 'sm')} disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
              <span aria-live="polite">Page {page} of {totalPages}</span>
              <button type="button" className={buttonClassName('outline', 'sm')} disabled={page >= totalPages} onClick={() => setPage(page + 1)}>Next</button>
            </span>
          </nav>
        </>
      )}

      {canSubmit && <NewRequestForm onSubmitted={refresh} />}
    </>
  )
}

function RequestCard({ request: r, onChanged }: { request: AcquisitionRequest; onChanged: () => Promise<unknown> }) {
  const { manager } = useAuth()
  const api = acquisitionApi(manager.api)
  const [note, setNote] = useState('')
  const [local, setLocal] = useState<string | undefined>()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const waiting = r.status === 'Submitted'
  const canDecide = waiting && !r.submittedByMe && manager.can('approvals', 'update')
  const canWithdraw = waiting && r.submittedByMe && manager.can('projects', 'update')

  async function act(action: () => Promise<unknown>) {
    setError(null); setBusy(true)
    try {
      await action()
      setNote('')
      await onChanged()
    } catch (e) {
      setError(messageOf(e))
    } finally {
      setBusy(false)
    }
  }
  function reject() {
    if (!note.trim()) { setLocal('Say why the request is rejected.'); return }
    setLocal(undefined)
    void act(() => api.rejectRequest(r.id, note.trim()))
  }

  return (
    <li className="rounded-lg border bg-card p-4">
      <article aria-label={`Request for ${r.property}`}>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="text-sm font-semibold">{r.property}</h2>
          <Pill tone={TONE[r.status]}>{r.status}</Pill>
        </div>
        <p className="mt-1 text-sm">
          From {r.requesterName} ({r.requesterType === 'Internal' ? 'inside the agency' : 'outside the agency'}){r.requesterEmail ? `, ${r.requesterEmail}` : ''}
          {r.projectNumber ? <> · Project {r.projectNumber}</> : null}
        </p>
        {r.desiredParameters && <p className="mt-1 whitespace-pre-wrap text-sm">{r.desiredParameters}</p>}
        <p className="mt-1 text-xs text-muted-foreground">
          Entered {dateText(r.createdAt)}{r.submittedByMe ? ' by you' : ''}{r.estimatedAmount !== null ? ` · Estimated ${money(r.estimatedAmount)}` : ''}
          {r.decidedAt ? ` · Decided ${dateText(r.decidedAt)}` : ''}
        </p>
        {r.decisionNote && <p className="mt-1 text-xs text-muted-foreground">Decision note: {r.decisionNote}</p>}
        {canDecide && (
          <div className="mt-3 max-w-md space-y-2 border-t pt-3">
            <TextField label="Note (required to reject)" value={note} onChange={setNote} maxLength={1000} error={local} />
            <div className="flex gap-2">
              <button type="button" disabled={busy} className={buttonClassName('default', 'sm')} onClick={() => void act(() => api.approveRequest(r.id, orNull(note)))}>Approve</button>
              <button type="button" disabled={busy} className={buttonClassName('outline', 'sm')} onClick={reject}>Reject</button>
            </div>
          </div>
        )}
        {waiting && r.submittedByMe && !canWithdraw && <p className="mt-2 text-xs text-muted-foreground">Waiting for someone else to decide.</p>}
        {canWithdraw && (
          <div className="mt-3">
            <button type="button" disabled={busy} className={buttonClassName('outline', 'sm')} onClick={() => void act(() => api.withdrawRequest(r.id))}>Withdraw this request</button>
          </div>
        )}
        <FormError error={error} />
      </article>
    </li>
  )
}

function NewRequestForm({ onSubmitted }: { onSubmitted: () => Promise<unknown> }) {
  const { manager } = useAuth()
  const api = acquisitionApi(manager.api)
  const projects = useQuery({ queryKey: ['projects', 'options'], queryFn: () => projectsApi(manager.api).list({ pageSize: 100 }) })
  const [type, setType] = useState<RequesterType | ''>('Internal')
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [property, setProperty] = useState('')
  const [wanted, setWanted] = useState('')
  const [amount, setAmount] = useState('')
  const [projectId, setProjectId] = useState('')
  const [fields, setFields] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setDone(false); setError(null)
    const errs: Record<string, string> = {}
    if (!name.trim()) errs.requesterName = 'Enter who is asking.'
    if (!property.trim()) errs.property = 'Say which property is wanted.'
    setFields(errs)
    if (Object.keys(errs).length) return
    setBusy(true)
    try {
      await api.submitRequest({
        projectId: projectId || null, requesterType: type || 'Internal', requesterName: name.trim(), requesterEmail: orNull(email),
        property: property.trim(), desiredParameters: orNull(wanted), estimatedAmount: numOrNull(amount),
      })
      setName(''); setEmail(''); setProperty(''); setWanted(''); setAmount(''); setProjectId('')
      setDone(true)
      await onSubmitted()
    } catch (err) {
      const fe = fieldErrorsOf(err)
      setFields(fe)
      setError(Object.keys(fe).length ? 'Please correct the highlighted fields.' : messageOf(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} noValidate aria-label="Enter a request" className="mt-6 max-w-3xl space-y-3 rounded-lg border bg-card p-4">
      <h2 className="text-sm font-semibold">Enter a request</h2>
      <div className="grid gap-3 sm:grid-cols-2">
        <SelectField label="Asked by" required value={type} onChange={setType} options={TYPE_OPTIONS} error={fields.requesterType} />
        <TextField label="Who is asking" required value={name} onChange={setName} maxLength={200} error={fields.requesterName} />
        <TextField label="Their email" value={email} onChange={setEmail} maxLength={200} error={fields.requesterEmail} />
        <SelectField
          label="Project" value={projectId} onChange={setProjectId} blankLabel="Not tied to a project yet"
          options={(projects.data?.items ?? []).map((p) => ({ value: p.id, label: `${p.number} · ${p.name}` }))} error={fields.projectId}
        />
      </div>
      <TextField label="Property wanted" required value={property} onChange={setProperty} maxLength={500} error={fields.property} hint="An address, a parcel number or a description." />
      <TextField label="What they want" value={wanted} onChange={setWanted} maxLength={2000} error={fields.desiredParameters} hint="The interest, the timing and the purpose." />
      <TextField label="Estimated amount" type="number" value={amount} onChange={setAmount} error={fields.estimatedAmount} />
      <FormError error={error} />
      {done && <p role="status" className="text-sm text-ok">Request entered. Someone else will approve or reject it.</p>}
      <button type="submit" disabled={busy} className={buttonClassName('default')}>Submit request</button>
    </form>
  )
}
