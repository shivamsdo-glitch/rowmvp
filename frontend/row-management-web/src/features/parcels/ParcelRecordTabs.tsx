import { useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Pencil, Printer, Trash2 } from 'lucide-react'
import { Link } from '@tanstack/react-router'
import { libraryApi } from '@/api/library'
import {
  DIARY_METHODS, DIARY_METHOD_LABEL, parcelRecordsApi,
  type DiaryDirection, type DiaryMethod, type TeamMember,
} from '@/api/parcel-records'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { optionsWith, useLibraryValues } from '../library/library-kit'
import { CheckboxField, DataTable, dateText, fieldErrorsOf, FormError, messageOf, orNull, Pill, SelectField, TextField } from '../projects/form-kit'

const METHOD_OPTIONS = DIARY_METHODS.map((m) => ({ value: m, label: DIARY_METHOD_LABEL[m] }))
const DIRECTION_OPTIONS = [{ value: 'Outgoing' as const, label: 'We contacted them' }, { value: 'Incoming' as const, label: 'They contacted us' }]

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section aria-label={title} className="mb-6 rounded-lg border bg-card p-4">
      <h2 className="mb-3 text-sm font-semibold">{title}</h2>
      {children}
    </section>
  )
}

/** Runs a save or delete, keeping the form's errors and busy state and refreshing the lists. */
function useRecordAction(parcelId: string) {
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
        await queryClient.invalidateQueries({ queryKey: ['parcel-records', parcelId] })
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

// ── Team (ROW-019) ──────────────────────────────────────────────────────

/** The parcel team: people are picked from the Library's Agents list by agent type, never typed (ADR 0023). */
export function TeamTab({ parcelId, editable }: { parcelId: string; editable: boolean }) {
  const { manager } = useAuth()
  const api = parcelRecordsApi(manager.api)
  const team = useQuery({ queryKey: ['parcel-records', parcelId, 'team'], queryFn: () => api.team(parcelId) })
  const agents = useQuery({ queryKey: ['library', 'agents'], queryFn: () => libraryApi(manager.api).agents(), retry: false })
  const types = useLibraryValues('agent_type')
  const a = useRecordAction(parcelId)
  const [editing, setEditing] = useState<TeamMember | null>(null)
  const [type, setType] = useState('')
  const [agentId, setAgentId] = useState('')
  const [notes, setNotes] = useState('')
  const [pickError, setPickError] = useState<string | null>(null)
  const canAdd = editable && manager.can('parcels', 'create')
  const canEdit = editable && manager.can('parcels', 'update')
  const canRemove = editable && manager.can('parcels', 'delete')

  const active = (agents.data ?? []).filter((x) => x.isActive)
  const ofType = active.filter((x) => x.agentType === type)
  const chosen = active.find((x) => x.id === agentId)

  function reset() {
    setEditing(null); setType(''); setAgentId(''); setNotes(''); setPickError(null)
  }
  function begin(m: TeamMember) {
    a.clear(); setPickError(null)
    setEditing(m); setType(m.role); setAgentId(m.agentId ?? ''); setNotes(m.notes ?? '')
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!type) { setPickError('Choose the type of person.'); return }
    if (!agentId) { setPickError('Choose a person from the Library.'); return }
    setPickError(null)
    const body = { agentId, notes: orNull(notes) }
    if (await a.run(() => (editing ? api.updateTeam(parcelId, editing.id, body) : api.addTeam(parcelId, body)))) reset()
  }

  if (team.isPending) return <LoadingRows rows={3} />
  if (team.isError) return <ErrorState title="The team could not be loaded" description={team.error.message} retry={() => void team.refetch()} />
  const showActions = canEdit || canRemove

  return (
    <>
      <Section title="Parcel team">
        {team.data.length === 0 ? (
          <EmptyState title="No one is assigned yet" description="Assign the project lead, appraiser, negotiator and others to this parcel. People and their types are kept in the Library." />
        ) : (
          <DataTable caption="Parcel team" headers={['Type', 'Name', 'Company', 'Contact', 'Notes', ...(showActions ? ['Actions'] : [])]}>
            {team.data.map((m) => (
              <tr key={m.id}>
                <td className="px-3 py-2">{m.role}</td>
                <td className="px-3 py-2 font-medium">{m.name}</td>
                <td className="px-3 py-2">{m.organization ?? '—'}</td>
                <td className="px-3 py-2">{[m.phone, m.email].filter(Boolean).join(' · ') || '—'}</td>
                <td className="px-3 py-2">{m.notes ?? '—'}</td>
                {showActions && (
                  <td className="px-3 py-2">
                    <span className="inline-flex gap-1">
                      {canEdit && <button type="button" aria-label={`Edit ${m.name}, ${m.role}`} className={buttonClassName('ghost', 'sm')} onClick={() => begin(m)}><Pencil size={14} aria-hidden /></button>}
                      {canRemove && (
                        <button type="button" aria-label={`Remove ${m.name}, ${m.role}`} disabled={a.busy} className={buttonClassName('ghost', 'sm')} onClick={() => void a.run(() => api.removeTeam(parcelId, m.id))}>
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
        <form onSubmit={submit} noValidate aria-label={editing ? 'Change team member' : 'Assign a team member'} className="max-w-3xl space-y-3 rounded-lg border bg-card p-4">
          <h3 className="text-sm font-semibold">{editing ? `Change ${editing.name}` : 'Assign a team member'}</h3>
          <div className="grid gap-3 sm:grid-cols-2">
            <SelectField label="Type" required value={type} onChange={(v) => { setType(v); setAgentId('') }} options={optionsWith(types, type)} blankLabel="Select one" error={pickError && !type ? pickError : undefined} />
            <SelectField
              label="Person" required value={agentId} onChange={setAgentId} disabled={!type}
              options={ofType.map((x) => ({ value: x.id, label: x.company ? `${x.name} (${x.company})` : x.name }))}
              blankLabel={type ? 'Select one' : 'Choose a type first'} error={pickError && type ? pickError : a.fields.agentId}
            />
            <TextField label="Notes" value={notes} onChange={setNotes} maxLength={500} error={a.fields.notes} />
          </div>
          {type && ofType.length === 0 && (
            <p className="text-xs text-muted-foreground">No active {type.toLowerCase()} is in the Library yet. Add one under <Link to="/library" className="text-primary hover:underline">Library → Agents</Link>.</p>
          )}
          {chosen && (
            <p className="text-xs text-muted-foreground">From the Library: {[chosen.company, chosen.phone, chosen.email].filter(Boolean).join(' · ') || 'no company or contact details'}.</p>
          )}
          <FormError error={a.error} />
          <div className="flex gap-2">
            <button type="submit" disabled={a.busy} className={buttonClassName('default')}>{editing ? 'Save changes' : 'Assign'}</button>
            {editing && <button type="button" className={buttonClassName('outline')} onClick={reset}>Cancel</button>}
          </div>
        </form>
      )}
    </>
  )
}

// ── Diary (ROW-020) ─────────────────────────────────────────────────────

export function DiaryTab({ parcelId, editable }: { parcelId: string; editable: boolean }) {
  const { manager } = useAuth()
  const api = parcelRecordsApi(manager.api)
  const diary = useQuery({ queryKey: ['parcel-records', parcelId, 'diary'], queryFn: () => api.diary(parcelId), enabled: manager.can('negotiations') })
  const a = useRecordAction(parcelId)
  const [date, setDate] = useState('')
  const [method, setMethod] = useState<DiaryMethod | ''>('Call')
  const [direction, setDirection] = useState<DiaryDirection | ''>('Outgoing')
  const [contact, setContact] = useState('')
  const [summary, setSummary] = useState('')
  const [outcome, setOutcome] = useState('')
  const [local, setLocal] = useState<Record<string, string>>({})
  const canAdd = editable && manager.can('negotiations', 'create')
  const canRemove = editable && manager.can('negotiations', 'delete')

  if (!manager.can('negotiations')) return <p className="text-sm text-muted-foreground">Your role cannot see the negotiation diary.</p>
  if (diary.isPending) return <LoadingRows rows={3} />
  if (diary.isError) return <ErrorState title="The diary could not be loaded" description={diary.error.message} retry={() => void diary.refetch()} />

  async function submit(e: FormEvent) {
    e.preventDefault()
    const errs: Record<string, string> = {}
    if (!date) errs.occurredOn = 'Choose the date.'
    if (!contact.trim()) errs.contactName = 'Say who was contacted.'
    if (!summary.trim()) errs.summary = 'Summary is required.'
    setLocal(errs)
    if (Object.keys(errs).length) return
    const ok = await a.run(() => api.addDiary(parcelId, {
      occurredOn: date, method: method || 'Other', direction: direction || 'Outgoing', contactName: contact.trim(), partyId: null, summary: summary.trim(), outcome: orNull(outcome),
    }))
    if (ok) { setDate(''); setContact(''); setSummary(''); setOutcome('') }
  }
  const errors = { ...a.fields, ...local }

  return (
    <>
      <Section title="Negotiation diary">
        <div className="mb-3 flex items-center justify-between">
          <p className="text-xs text-muted-foreground">Every contact with an owner or other party, newest first. Entries cannot be edited; remove one made in error and add it again.</p>
          <button type="button" className={buttonClassName('outline', 'sm')} onClick={() => window.print()}><Printer size={14} aria-hidden /> Print diary</button>
        </div>
        {diary.data.length === 0 ? (
          <EmptyState title="No contacts logged" description="Log calls, meetings, mailings, emails and contact attempts here." />
        ) : (
          <DataTable caption="Negotiation diary" headers={['Date', 'How', 'Who', 'What was said', 'Promised or answered', 'Logged by', ...(canRemove ? ['Actions'] : [])]}>
            {diary.data.map((d) => (
              <tr key={d.id}>
                <td className="px-3 py-2">{dateText(d.occurredOn)}</td>
                <td className="px-3 py-2">{DIARY_METHOD_LABEL[d.method]} <span className="text-xs text-muted-foreground">({d.direction === 'Outgoing' ? 'out' : 'in'})</span></td>
                <td className="px-3 py-2">{d.contactName}</td>
                <td className="whitespace-pre-wrap px-3 py-2">{d.summary}</td>
                <td className="whitespace-pre-wrap px-3 py-2">{d.outcome ?? '—'}</td>
                <td className="px-3 py-2">{d.authorName ?? '—'}</td>
                {canRemove && (
                  <td className="px-3 py-2">
                    <button type="button" aria-label={`Remove the ${dateText(d.occurredOn)} entry with ${d.contactName}`} disabled={a.busy} className={buttonClassName('ghost', 'sm')} onClick={() => void a.run(() => api.removeDiary(parcelId, d.id))}>
                      <Trash2 size={14} aria-hidden />
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </DataTable>
        )}
      </Section>
      {canAdd && (
        <form onSubmit={submit} noValidate aria-label="Log a contact" className="max-w-3xl space-y-3 rounded-lg border bg-card p-4">
          <h3 className="text-sm font-semibold">Log a contact</h3>
          <div className="grid gap-3 sm:grid-cols-2">
            <TextField label="Date of contact" required type="date" value={date} onChange={setDate} error={errors.occurredOn} />
            <SelectField label="How" required value={method} onChange={setMethod} options={METHOD_OPTIONS} error={errors.method} />
            <SelectField label="Who started it" value={direction} onChange={setDirection} options={DIRECTION_OPTIONS} error={errors.direction} />
            <TextField label="Who was contacted" required value={contact} onChange={setContact} maxLength={200} error={errors.contactName} />
          </div>
          <TextField label="What was said" required value={summary} onChange={setSummary} maxLength={2000} error={errors.summary} />
          <TextField label="What was promised or answered" value={outcome} onChange={setOutcome} maxLength={1000} error={errors.outcome} />
          <FormError error={a.error} />
          <button type="submit" disabled={a.busy} className={buttonClassName('default')}>Add to diary</button>
        </form>
      )}
    </>
  )
}

// ── Notes (ROW-021) ─────────────────────────────────────────────────────

export function NotesTab({ parcelId, editable }: { parcelId: string; editable: boolean }) {
  const { manager } = useAuth()
  const api = parcelRecordsApi(manager.api)
  const notes = useQuery({ queryKey: ['parcel-records', parcelId, 'notes'], queryFn: () => api.notes(parcelId) })
  const a = useRecordAction(parcelId)
  const [text, setText] = useState('')
  const [local, setLocal] = useState<string | null>(null)
  const canAdd = editable && manager.can('parcels', 'create')
  const canRemove = editable && manager.can('parcels', 'delete')

  if (notes.isPending) return <LoadingRows rows={3} />
  if (notes.isError) return <ErrorState title="The notes could not be loaded" description={notes.error.message} retry={() => void notes.refetch()} />

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!text.trim()) { setLocal('Write the note first.'); return }
    setLocal(null)
    if (await a.run(() => api.addNote(parcelId, text.trim()))) setText('')
  }

  return (
    <>
      <Section title="Parcel notes">
        {notes.data.length === 0 ? (
          <EmptyState title="No notes yet" description="Notes record anything about the parcel that does not belong elsewhere." />
        ) : (
          <ul className="space-y-3">
            {notes.data.map((n) => (
              <li key={n.id} className="rounded-md border p-3">
                <p className="whitespace-pre-wrap text-sm">{n.text}</p>
                <p className="mt-2 flex items-center justify-between text-xs text-muted-foreground">
                  <span>Made by {n.authorName ?? 'a user'} on {dateText(n.createdAt)}</span>
                  {canRemove && (
                    <button type="button" aria-label={`Delete the note "${n.text.slice(0, 30)}"`} disabled={a.busy} className={buttonClassName('ghost', 'sm')} onClick={() => void a.run(() => api.removeNote(parcelId, n.id))}>
                      <Trash2 size={14} aria-hidden />
                    </button>
                  )}
                </p>
              </li>
            ))}
          </ul>
        )}
      </Section>
      {canAdd && (
        <form onSubmit={submit} noValidate aria-label="Add a note" className="max-w-3xl space-y-3 rounded-lg border bg-card p-4">
          <h3 className="text-sm font-semibold">Add a note</h3>
          <TextField label="Note" required value={text} onChange={setText} maxLength={4000} error={local ?? a.fields.text} />
          <FormError error={a.error} />
          <button type="submit" disabled={a.busy} className={buttonClassName('default')}>Add note</button>
        </form>
      )}
    </>
  )
}
