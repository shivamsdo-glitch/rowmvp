import { useState, type FormEvent, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Pencil, Trash2 } from 'lucide-react'
import {
  parcelTabsApi, type ContactLogEntry, type ContactLogLinkInput, type Easement,
} from '@/api/parcel-tabs'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { DataTable, dateText, fieldErrorsOf, FormError, messageOf, orNull, SelectField, TextField } from '../projects/form-kit'
import { AttachmentsPanel } from '../documents/AttachmentsPanel'
import { optionsWith, useLibraryValues } from '../library/library-kit'

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section aria-label={title} className="mb-6 rounded-lg border bg-card p-4">
      <h2 className="mb-3 text-sm font-semibold">{title}</h2>
      {children}
    </section>
  )
}

/** Runs a save or delete for one tab, keeping the form's errors and busy state and refreshing the tab's list. */
function useTabAction(queryKey: unknown[]) {
  const queryClient = useQueryClient()
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  return {
    error, fields, busy,
    clear: () => { setError(null); setFields({}) },
    run: async (action: () => Promise<unknown>): Promise<boolean> => {
      setError(null)
      setFields({})
      setBusy(true)
      try {
        await action()
        await queryClient.invalidateQueries({ queryKey })
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

/** "2026-10-01T15:30:00Z" as the value of a date-time input in the person's own time zone. */
function toLocalInput(iso: string): string {
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

const dateTimeText = (iso: string) => new Date(iso).toLocaleString('en-US', { dateStyle: 'medium', timeStyle: 'short' })

// ── Contact Log ─────────────────────────────────────────────────────────

export function ContactLogTab({ parcelId, editable }: { parcelId: string; editable: boolean }) {
  const { manager, user } = useAuth()
  const api = parcelTabsApi(manager.api)
  const key = ['parcel-tabs', parcelId, 'contact-log']
  const log = useQuery({ queryKey: key, queryFn: () => api.contactLog(parcelId), enabled: manager.can('contactlog') })
  const a = useTabAction(key)
  const modes = useLibraryValues('mode_of_contact')
  const [editing, setEditing] = useState<ContactLogEntry | null>(null)
  const [at, setAt] = useState('')
  const [mode, setMode] = useState('')
  const [by, setBy] = useState(user?.fullName ?? '')
  const [person, setPerson] = useState('')
  const [notes, setNotes] = useState('')
  const [links, setLinks] = useState<ContactLogLinkInput[]>([])
  const [linkId, setLinkId] = useState('')
  const [linkModule, setLinkModule] = useState('')
  const [linkNotes, setLinkNotes] = useState('')
  const [local, setLocal] = useState<Record<string, string>>({})
  const canAdd = editable && manager.can('contactlog', 'create')
  const canEdit = editable && manager.can('contactlog', 'update')
  const canRemove = editable && manager.can('contactlog', 'delete')

  if (!manager.can('contactlog')) return <p className="text-sm text-muted-foreground">Your role cannot see the contact log.</p>
  if (log.isPending) return <LoadingRows rows={3} />
  if (log.isError) return <ErrorState title="The contact log could not be loaded" description={log.error.message} retry={() => void log.refetch()} />

  function reset() {
    setEditing(null); setAt(''); setMode(''); setBy(user?.fullName ?? ''); setPerson(''); setNotes(''); setLinks([]); setLinkId(''); setLinkModule(''); setLinkNotes(''); setLocal({}); a.clear()
  }

  function begin(e: ContactLogEntry) {
    setEditing(e); setAt(toLocalInput(e.contactedAt)); setMode(e.mode ?? ''); setBy(e.contactedBy ?? ''); setPerson(e.contactPerson ?? ''); setNotes(e.notes ?? '')
    setLinks(e.links.map((l) => ({ recordIdentifier: l.recordIdentifier, moduleName: l.moduleName, notes: l.notes }))); setLocal({}); a.clear()
  }

  function addLink() {
    if (!linkId.trim()) { setLocal({ ...local, linkId: 'Enter the record identifier.' }); return }
    setLinks([...links, { recordIdentifier: linkId.trim(), moduleName: orNull(linkModule), notes: orNull(linkNotes) }])
    setLinkId(''); setLinkModule(''); setLinkNotes(''); setLocal({ ...local, linkId: '' })
  }

  async function submit(ev: FormEvent) {
    ev.preventDefault()
    const errs: Record<string, string> = {}
    if (!at || Number.isNaN(new Date(at).getTime())) errs.contactedAt = 'Enter the date and time of the contact.'
    setLocal(errs)
    if (Object.keys(errs).length) return
    const body = {
      contactedAt: new Date(at).toISOString(), mode: orNull(mode), contactedBy: orNull(by), contactPerson: orNull(person), notes: orNull(notes), links,
    }
    if (await a.run(() => (editing ? api.updateContactLog(parcelId, editing.id, body) : api.addContactLog(parcelId, body)))) reset()
  }
  const errors = { ...a.fields, ...local }

  return (
    <>
      <Section title="Contact log">
        {log.data.length === 0 ? (
          <EmptyState title="No contacts logged" description="Log each call, meeting, letter or visit with the owner or other parties." />
        ) : (
          <DataTable caption="Contact log" headers={['Contact ID', 'Date and time', 'Mode', 'Contacted by', 'Contact person', 'Notes', 'Linked', ...(canEdit || canRemove ? ['Actions'] : [])]}>
            {log.data.map((e) => (
              <tr key={e.id}>
                <td className="px-3 py-2 font-medium">{e.number}</td>
                <td className="px-3 py-2">{dateTimeText(e.contactedAt)}</td>
                <td className="px-3 py-2">{e.mode ?? '—'}</td>
                <td className="px-3 py-2">{e.contactedBy ?? '—'}</td>
                <td className="px-3 py-2">{e.contactPerson ?? '—'}</td>
                <td className="whitespace-pre-wrap px-3 py-2">{e.notes ?? '—'}</td>
                <td className="px-3 py-2">{e.links.length}</td>
                {(canEdit || canRemove) && (
                  <td className="px-3 py-2">
                    <span className="inline-flex gap-1">
                      {canEdit && <button type="button" aria-label={`Edit contact ${e.number}`} className={buttonClassName('ghost', 'sm')} onClick={() => begin(e)}><Pencil size={14} aria-hidden /></button>}
                      {canRemove && (
                        <button type="button" aria-label={`Remove contact ${e.number}`} disabled={a.busy} className={buttonClassName('ghost', 'sm')}
                          onClick={() => void a.run(() => api.removeContactLog(parcelId, e.id)).then((ok) => { if (ok && editing?.id === e.id) reset() })}>
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
        <form onSubmit={submit} noValidate aria-label={editing ? 'Change contact' : 'Log a contact'} className="max-w-3xl space-y-3 rounded-lg border bg-card p-4">
          <h3 className="text-sm font-semibold">{editing ? `Change ${editing.number}` : 'Log a contact'}</h3>
          <div className="grid gap-3 sm:grid-cols-2">
            <TextField label="Date and time of contact" required type="datetime-local" value={at} onChange={setAt} error={errors.contactedAt} />
            <SelectField label="Mode of contact" value={mode} onChange={setMode} options={optionsWith(modes, mode)} blankLabel="Select one" error={errors.mode} />
            <TextField label="Contacted by" value={by} onChange={setBy} maxLength={200} error={errors.contactedBy} />
            <TextField label="Contact person(s)" value={person} onChange={setPerson} maxLength={200} error={errors.contactPerson} />
          </div>
          <TextField label="Discussion notes" value={notes} onChange={setNotes} maxLength={4000} error={errors.notes} />

          <fieldset className="rounded-md border p-3">
            <legend className="px-1 text-xs font-medium text-muted-foreground">Linked records</legend>
            {links.length > 0 && (
              <DataTable caption="Linked records" headers={['Record identifier', 'Module name', 'Notes', 'Actions']}>
                {links.map((l, i) => (
                  <tr key={`${l.recordIdentifier}-${i}`}>
                    <td className="px-3 py-2">{l.recordIdentifier}</td>
                    <td className="px-3 py-2">{l.moduleName ?? '—'}</td>
                    <td className="px-3 py-2">{l.notes ?? '—'}</td>
                    <td className="px-3 py-2">
                      <button type="button" aria-label={`Remove linked record ${l.recordIdentifier}`} className={buttonClassName('ghost', 'sm')} onClick={() => setLinks(links.filter((_, j) => j !== i))}>
                        <Trash2 size={14} aria-hidden />
                      </button>
                    </td>
                  </tr>
                ))}
              </DataTable>
            )}
            <div className="mt-3 grid gap-3 sm:grid-cols-3">
              <TextField label="Record identifier" value={linkId} onChange={setLinkId} maxLength={100} error={errors.linkId} />
              <TextField label="Module name" value={linkModule} onChange={setLinkModule} maxLength={100} />
              <TextField label="Link notes" value={linkNotes} onChange={setLinkNotes} maxLength={500} />
            </div>
            <button type="button" className={`${buttonClassName('outline', 'sm')} mt-2`} onClick={addLink}>Add linked record</button>
          </fieldset>

          <FormError error={a.error} />
          <div className="flex gap-2">
            <button type="submit" disabled={a.busy} className={buttonClassName('default')}>{editing ? 'Save changes' : 'Add contact'}</button>
            {editing && <button type="button" className={buttonClassName('outline')} onClick={reset}>Cancel</button>}
          </div>
        </form>
      )}

      {editing && <AttachmentsPanel entityType="ContactLog" entityId={editing.id} page="contactlog" title={`Attachments for ${editing.number}`} />}
    </>
  )
}

// ── Easements ───────────────────────────────────────────────────────────

export function EasementsTab({ parcelId, editable }: { parcelId: string; editable: boolean }) {
  const { manager } = useAuth()
  const api = parcelTabsApi(manager.api)
  const key = ['parcel-tabs', parcelId, 'easements']
  const list = useQuery({ queryKey: key, queryFn: () => api.easements(parcelId), enabled: manager.can('easements') })
  const a = useTabAction(key)
  const types = useLibraryValues('easement_type')
  const [editing, setEditing] = useState<Easement | null>(null)
  const [number, setNumber] = useState('')
  const [type, setType] = useState('')
  const [description, setDescription] = useState('')
  const [start, setStart] = useState('')
  const [end, setEnd] = useState('')
  const [startStation, setStartStation] = useState('')
  const [endStation, setEndStation] = useState('')
  const [local, setLocal] = useState<Record<string, string>>({})
  const canAdd = editable && manager.can('easements', 'create')
  const canEdit = editable && manager.can('easements', 'update')
  const canRemove = editable && manager.can('easements', 'delete')

  if (!manager.can('easements')) return <p className="text-sm text-muted-foreground">Your role cannot see easements.</p>
  if (list.isPending) return <LoadingRows rows={3} />
  if (list.isError) return <ErrorState title="The easements could not be loaded" description={list.error.message} retry={() => void list.refetch()} />

  function reset() {
    setEditing(null); setNumber(''); setType(''); setDescription(''); setStart(''); setEnd(''); setStartStation(''); setEndStation(''); setLocal({}); a.clear()
  }

  function begin(e: Easement) {
    setEditing(e); setNumber(e.number); setType(e.easementType ?? ''); setDescription(e.description ?? ''); setStart(e.startDate ?? ''); setEnd(e.endDate ?? '')
    setStartStation(e.startStation ?? ''); setEndStation(e.endStation ?? ''); setLocal({}); a.clear()
  }

  async function submit(ev: FormEvent) {
    ev.preventDefault()
    const errs: Record<string, string> = {}
    if (!number.trim()) errs.number = 'Easement number is required.'
    if (start && end && end < start) errs.endDate = 'The end date cannot be before the start date.'
    setLocal(errs)
    if (Object.keys(errs).length) return
    const body = {
      number: number.trim(), easementType: orNull(type), description: orNull(description), startDate: orNull(start), endDate: orNull(end),
      startStation: orNull(startStation), endStation: orNull(endStation),
    }
    if (await a.run(() => (editing ? api.updateEasement(parcelId, editing.id, body) : api.addEasement(parcelId, body)))) reset()
  }
  const errors = { ...a.fields, ...local }

  return (
    <>
      <Section title="Easements">
        {list.data.length === 0 ? (
          <EmptyState title="No easements recorded" description="Record each easement on this parcel with its term and station range." />
        ) : (
          <DataTable caption="Easements" headers={['Easement number', 'Type', 'Description', 'Start date', 'End date', 'Start station', 'End station', ...(canEdit || canRemove ? ['Actions'] : [])]}>
            {list.data.map((e) => (
              <tr key={e.id}>
                <td className="px-3 py-2 font-medium">{e.number}</td>
                <td className="px-3 py-2">{e.easementType ?? '—'}</td>
                <td className="whitespace-pre-wrap px-3 py-2">{e.description ?? '—'}</td>
                <td className="px-3 py-2">{e.startDate ? dateText(e.startDate) : '—'}</td>
                <td className="px-3 py-2">{e.endDate ? dateText(e.endDate) : '—'}</td>
                <td className="px-3 py-2">{e.startStation ?? '—'}</td>
                <td className="px-3 py-2">{e.endStation ?? '—'}</td>
                {(canEdit || canRemove) && (
                  <td className="px-3 py-2">
                    <span className="inline-flex gap-1">
                      {canEdit && <button type="button" aria-label={`Edit easement ${e.number}`} className={buttonClassName('ghost', 'sm')} onClick={() => begin(e)}><Pencil size={14} aria-hidden /></button>}
                      {canRemove && (
                        <button type="button" aria-label={`Remove easement ${e.number}`} disabled={a.busy} className={buttonClassName('ghost', 'sm')}
                          onClick={() => void a.run(() => api.removeEasement(parcelId, e.id)).then((ok) => { if (ok && editing?.id === e.id) reset() })}>
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
        <form onSubmit={submit} noValidate aria-label={editing ? 'Change easement' : 'Add an easement'} className="max-w-3xl space-y-3 rounded-lg border bg-card p-4">
          <h3 className="text-sm font-semibold">{editing ? `Change easement ${editing.number}` : 'Add an easement'}</h3>
          <div className="grid gap-3 sm:grid-cols-2">
            <TextField label="Easement number" required value={number} onChange={setNumber} maxLength={50} error={errors.number} />
            <SelectField label="Easement type" value={type} onChange={setType} options={optionsWith(types, type)} blankLabel="Select one" error={errors.easementType} />
            <TextField label="Start date" type="date" value={start} onChange={setStart} error={errors.startDate} />
            <TextField label="End date" type="date" value={end} onChange={setEnd} error={errors.endDate} />
          </div>
          <TextField label="Description" value={description} onChange={setDescription} maxLength={2000} error={errors.description} />
          <fieldset className="grid gap-3 rounded-md border p-3 sm:grid-cols-2">
            <legend className="px-1 text-xs font-medium text-muted-foreground">Easement location</legend>
            <TextField label="Start station" value={startStation} onChange={setStartStation} maxLength={100} error={errors.startStation} />
            <TextField label="End station" value={endStation} onChange={setEndStation} maxLength={100} error={errors.endStation} />
          </fieldset>
          <FormError error={a.error} />
          <div className="flex gap-2">
            <button type="submit" disabled={a.busy} className={buttonClassName('default')}>{editing ? 'Save changes' : 'Add easement'}</button>
            {editing && <button type="button" className={buttonClassName('outline')} onClick={reset}>Cancel</button>}
          </div>
        </form>
      )}

      {editing && <AttachmentsPanel entityType="Easement" entityId={editing.id} page="easements" title={`Attachments for easement ${editing.number}`} />}
    </>
  )
}
