import { useState, type FormEvent } from 'react'
import { Link } from '@tanstack/react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Pencil, Trash2 } from 'lucide-react'
import {
  CARRIES_SHARE, CATEGORY_LABEL, CATEGORY_OF, HAS_LEASE, ROLE_LABEL, partiesApi,
  type ParcelParties, type PartyCategory, type PartyLink, type PartyRole,
} from '@/api/parties'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { ErrorState, LoadingRows } from '@/components/ui/states'
import { CheckboxField, DataTable, FormError, SelectField, TextField, fieldErrorsOf, messageOf, numOrNull, orNull } from '../projects/form-kit'
import { optionsWith, useLibraryValues } from '../library/library-kit'
import { PartyFields, draftOf, inputOf, partyFieldErrors, percentText, roleOptions, usePartyDraft } from './party-kit'

/** Where the panel reads and writes: one parcel, or one parent tract of a project. */
export type PartyTarget = { kind: 'parcel'; parcelId: string } | { kind: 'tract'; projectId: string; tractId: string }

const LISTS: { key: keyof Pick<ParcelParties, 'ownership' | 'contacts' | 'occupants' | 'abutters'>; category: PartyCategory; empty: string }[] = [
  { key: 'ownership', category: 'Ownership', empty: 'No owners or interest holders recorded.' },
  { key: 'contacts', category: 'Contact', empty: 'No contacts recorded.' },
  { key: 'occupants', category: 'Occupant', empty: 'No tenants or occupants recorded.' },
  { key: 'abutters', category: 'Abutter', empty: 'No abutters or interested parties recorded.' },
]

const queryKeyOf = (t: PartyTarget) => (t.kind === 'parcel' ? ['parties-of', 'parcel', t.parcelId] : ['parties-of', 'tract', t.tractId])

/**
 * The people around a parcel or a tract in four lists: owners and interests, contacts, tenants and abutters
 * (ROW-014..017). A parcel that belongs to a tract shows the tract's owners (read-only here) with its own contacts, tenants
 * and abutters; owners are recorded once, on the tract.
 */
export function PartyLinksPanel({ target, editable = true, headingLevel = 'h2' }: { target: PartyTarget; editable?: boolean; /** The level of the four list headings, so the page's outline stays in order. */ headingLevel?: 'h2' | 'h4' }) {
  const Heading = headingLevel
  const { manager } = useAuth()
  const api = partiesApi(manager.api)
  const queryClient = useQueryClient()
  const q = useQuery({
    queryKey: queryKeyOf(target),
    queryFn: () => (target.kind === 'parcel' ? api.forParcel(target.parcelId) : api.forTract(target.projectId, target.tractId)),
  })
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const canAdd = editable && manager.can('parties', 'create')
  const canEdit = editable && manager.can('parties', 'update')
  const canRemove = editable && manager.can('parties', 'delete')

  if (q.isPending) return <LoadingRows rows={4} />
  if (q.isError) return <ErrorState title="The people on this record could not be loaded" description={q.error.message} retry={() => void q.refetch()} />
  const data = q.data
  const inherits = target.kind === 'parcel' && data.parentTractId !== null

  const refresh = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['parties-of'] }),
    queryClient.invalidateQueries({ queryKey: ['parties'] }),
    queryClient.invalidateQueries({ queryKey: ['party'] }),
    // The parcel's Owner name and the Title & Closing vested owner name are derived from these links (ADR 0023).
    queryClient.invalidateQueries({ queryKey: ['parcel'] }),
    queryClient.invalidateQueries({ queryKey: ['parcels'] }),
  ])

  async function remove(link: PartyLink) {
    setError(null)
    setBusy(true)
    try {
      await api.removeLink(link.id)
      await refresh()
    } catch (e) {
      setError(messageOf(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-6">
      {inherits && (
        <p role="note" className="rounded-md border bg-muted/40 px-3 py-2 text-sm">
          This parcel is part of tract <strong>{data.tractNumber}</strong>. Owners and interest holders are recorded once, on the tract, and shown here for reference.
        </p>
      )}
      {LISTS.map(({ key, category, empty }) => {
        const rows = data[key]
        const own = (l: PartyLink) => l.source === (target.kind === 'parcel' ? 'Parcel' : 'Tract')
        const withActions = (canEdit || canRemove) && rows.some(own)
        const headers = ['Party', 'Role', ...(category === 'Ownership' ? ['Owner type', 'Share'] : []), 'Contact', 'Details', ...(inherits ? ['Recorded on'] : []), ...(withActions ? ['Actions'] : [])]
        return (
          <section key={key} aria-label={CATEGORY_LABEL[category]}>
            <Heading className="mb-2 text-sm font-semibold">{CATEGORY_LABEL[category]}</Heading>
            {rows.length === 0 ? (
              <p className="text-sm text-muted-foreground">{empty}</p>
            ) : (
              <DataTable caption={CATEGORY_LABEL[category]} headers={headers}>
                {rows.map((l) => (
                  <LinkRows
                    key={l.id}
                    link={l}
                    category={category}
                    showSource={inherits}
                    showActions={withActions}
                    own={own(l)}
                    canEdit={canEdit}
                    canRemove={canRemove}
                    busy={busy}
                    isEditing={editing === l.id}
                    onEdit={() => { setEditing(editing === l.id ? null : l.id); setError(null) }}
                    onRemove={() => void remove(l)}
                    onSaved={async () => { setEditing(null); await refresh() }}
                    columns={headers.length}
                  />
                ))}
              </DataTable>
            )}
          </section>
        )
      })}
      <FormError error={error} />

      {canAdd && !adding && (
        <button type="button" className={buttonClassName('default')} onClick={() => setAdding(true)}>Add a person or organization</button>
      )}
      {canAdd && adding && (
        <AddLinkForm
          target={target}
          allowOwnership={!inherits}
          onCancel={() => setAdding(false)}
          onAdded={async () => { setAdding(false); await refresh() }}
        />
      )}
    </div>
  )
}

function LinkRows({
  link: l, category, showSource, showActions, own, canEdit, canRemove, busy, isEditing, onEdit, onRemove, onSaved, columns,
}: {
  link: PartyLink
  category: PartyCategory
  showSource: boolean
  showActions: boolean
  own: boolean
  canEdit: boolean
  canRemove: boolean
  busy: boolean
  isEditing: boolean
  onEdit: () => void
  onRemove: () => void
  onSaved: () => Promise<void>
  columns: number
}) {
  const details = [
    l.notes,
    l.role === 'Tenant' ? (l.leaseOnFile ? `Lease on file${l.leaseDetails ? `: ${l.leaseDetails}` : ''}` : 'No lease on file') : null,
    l.insuranceDetails ? `Insurance: ${l.insuranceDetails}` : null,
  ].filter(Boolean)
  const name = `${l.party.name}, ${ROLE_LABEL[l.role]}`
  return (
    <>
      <tr>
        <td className="px-3 py-2 font-medium">
          <Link to="/parties/$partyId" params={{ partyId: l.party.id }} className="text-primary hover:underline">{l.party.name}</Link>
        </td>
        <td className="px-3 py-2">{ROLE_LABEL[l.role]}</td>
        {category === 'Ownership' && <td className="px-3 py-2">{l.ownerType ?? '—'}{l.displacementRequired ? <span className="ml-2 rounded bg-warn/15 px-1.5 py-0.5 text-xs font-medium">Displacement required</span> : null}</td>}
        {category === 'Ownership' && <td className="px-3 py-2">{percentText(l.sharePercent)}</td>}
        <td className="px-3 py-2">{[l.party.phone, l.party.email].filter(Boolean).join(' · ') || '—'}</td>
        <td className="px-3 py-2">{details.length ? details.join(' · ') : '—'}</td>
        {showSource && <td className="px-3 py-2">{l.source === 'Tract' ? `Tract ${l.tractNumber}` : 'This parcel'}</td>}
        {showActions && (
          <td className="px-3 py-2">
            {own && (
              <span className="inline-flex gap-1">
                {canEdit && (
                  <button type="button" aria-label={`Edit ${name}`} aria-expanded={isEditing} disabled={busy} className={buttonClassName('ghost', 'sm')} onClick={onEdit}>
                    <Pencil size={14} aria-hidden />
                  </button>
                )}
                {canRemove && (
                  <button type="button" aria-label={`Remove ${name}`} disabled={busy} className={buttonClassName('ghost', 'sm')} onClick={onRemove}>
                    <Trash2 size={14} aria-hidden />
                  </button>
                )}
              </span>
            )}
          </td>
        )}
      </tr>
      {isEditing && (
        <tr>
          <td colSpan={columns} className="bg-muted/30 px-3 py-3">
            <EditLinkForm link={l} onSaved={onSaved} onCancel={onEdit} />
          </td>
        </tr>
      )}
    </>
  )
}

/**
 * Edits one link and the person or organization behind it in a single form (ADR 0023): the role details (owner type, share, notes,
 * lease) and the party's own details with its addresses and contacts. Saving changes both.
 */
function EditLinkForm({ link, onSaved, onCancel }: { link: PartyLink; onSaved: () => Promise<void>; onCancel: () => void }) {
  const { manager } = useAuth()
  const api = partiesApi(manager.api)
  const canEditParty = manager.can('parties', 'update')
  const ownerTypes = useLibraryValues('owner_type')
  const party = useQuery({ queryKey: ['party', link.party.id], queryFn: () => api.get(link.party.id) })
  const [share, setShare] = useState(link.sharePercent === null ? '' : String(link.sharePercent))
  const [ownerType, setOwnerType] = useState(link.ownerType ?? '')
  const [displaced, setDisplaced] = useState(link.displacementRequired === true)
  const [notes, setNotes] = useState(link.notes ?? '')
  const [lease, setLease] = useState(link.leaseOnFile)
  const [leaseDetails, setLeaseDetails] = useState(link.leaseDetails ?? '')
  const [insurance, setInsurance] = useState(link.insuranceDetails ?? '')
  const { draft, set } = usePartyDraft()
  const [loaded, setLoaded] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const label = `${link.party.name}, ${ROLE_LABEL[link.role]}`
  const isOwnership = CATEGORY_OF[link.role] === 'Ownership'

  // The party's details arrive after the form opens; they fill the draft once.
  if (party.data && !loaded) {
    const d = draftOf(party.data)
    ;(Object.keys(d) as (keyof typeof d)[]).forEach((k) => set(k)(d[k] as never))
    setLoaded(true)
  }

  async function save(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setFields({})
    if (loaded && canEditParty && !draft.name.trim()) { setFields({ name: 'Name is required.' }); return }
    setBusy(true)
    try {
      if (loaded && canEditParty) await api.update(link.party.id, inputOf(draft))
      await api.updateLink(link.id, {
        sharePercent: CARRIES_SHARE.includes(link.role) ? numOrNull(share) : null, ownerType: isOwnership ? orNull(ownerType) : null, notes: orNull(notes),
        ...(isOwnership ? { displacementRequired: displaced } : {}),
        leaseOnFile: HAS_LEASE.includes(link.role) && lease, leaseDetails: HAS_LEASE.includes(link.role) ? orNull(leaseDetails) : null,
        insuranceDetails: HAS_LEASE.includes(link.role) ? orNull(insurance) : null,
      })
      await onSaved()
    } catch (err) {
      const fe = fieldErrorsOf(err)
      setFields(fe)
      setError(Object.keys(fe).length ? 'Please correct the highlighted fields.' : messageOf(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={save} noValidate aria-label={`Edit ${label}`} className="space-y-3">
      <div className="grid gap-3 sm:grid-cols-2">
        {isOwnership && <SelectField label="Owner type" value={ownerType} onChange={setOwnerType} options={optionsWith(ownerTypes, ownerType)} blankLabel="Select one" error={fields.ownerType} />}
        {isOwnership && <CheckboxField label="Displacement required" checked={displaced} onChange={setDisplaced} />}
        {CARRIES_SHARE.includes(link.role) && (
          <TextField label="Share (%)" type="number" inputMode="decimal" value={share} onChange={setShare} error={fields.sharePercent} hint="Up to four decimals; shares of one kind cannot pass 100%." />
        )}
        <TextField label="Role notes" value={notes} onChange={setNotes} maxLength={500} error={fields.notes} />
        {HAS_LEASE.includes(link.role) && (
          <>
            <CheckboxField label="Lease on file" checked={lease} onChange={setLease} />
            <TextField label="Lease details" value={leaseDetails} onChange={setLeaseDetails} maxLength={1000} error={fields.leaseDetails} />
            <TextField label="Insurance details" value={insurance} onChange={setInsurance} maxLength={1000} error={fields.insuranceDetails} />
          </>
        )}
      </div>
      {canEditParty && (
        <fieldset className="rounded-md border p-3">
          <legend className="px-1 text-xs font-medium text-muted-foreground">Person or organization</legend>
          {party.isError ? <p className="text-sm text-danger">The details could not be loaded.</p> : !loaded ? <p className="text-sm text-muted-foreground">Loading details…</p> : (
            <PartyFields draft={draft} set={set} errors={fields} idPrefix="Party" />
          )}
          <p className="mt-2 text-xs text-muted-foreground">
            Tax information (W-9) is kept on the party&rsquo;s own page: <Link to="/parties/$partyId" params={{ partyId: link.party.id }} className="text-primary hover:underline">open {link.party.name}</Link>.
          </p>
        </fieldset>
      )}
      <FormError error={error} />
      <div className="flex gap-2">
        <button type="submit" disabled={busy} className={buttonClassName('default', 'sm')}>Save changes</button>
        <button type="button" className={buttonClassName('outline', 'sm')} onClick={onCancel}>Cancel</button>
      </div>
    </form>
  )
}

function AddLinkForm({
  target, allowOwnership, onCancel, onAdded,
}: { target: PartyTarget; allowOwnership: boolean; onCancel: () => void; onAdded: () => Promise<void> }) {
  const { manager } = useAuth()
  const api = partiesApi(manager.api)
  const [mode, setMode] = useState<'existing' | 'new'>('existing')
  const [search, setSearch] = useState('')
  const [partyId, setPartyId] = useState('')
  const [role, setRole] = useState<PartyRole | ''>(allowOwnership ? 'FeeOwner' : 'Attorney')
  const [share, setShare] = useState('')
  const [notes, setNotes] = useState('')
  const [lease, setLease] = useState(false)
  const [leaseDetails, setLeaseDetails] = useState('')
  const [insurance, setInsurance] = useState('')
  const [ownerType, setOwnerType] = useState('')
  const [displaced, setDisplaced] = useState(false)
  const ownerTypes = useLibraryValues('owner_type')
  const { draft, set } = usePartyDraft()
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const people = useQuery({ queryKey: ['parties', 'picker', search], queryFn: () => api.list({ search, pageSize: 50 }), enabled: mode === 'existing' })
  const shareable = role !== '' && CARRIES_SHARE.includes(role)
  const tenant = role !== '' && HAS_LEASE.includes(role)

  async function add(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setFields({})
    if (!role) { setFields({ role: 'Choose the role.' }); return }
    if (mode === 'existing' && !partyId) { setFields({ party: 'Choose a person or organization, or enter a new one.' }); return }
    if (mode === 'new' && !draft.name.trim()) { setFields({ 'newParty.Name': 'Name is required.' }); return }
    const body = {
      partyId: mode === 'existing' ? partyId : null, newParty: mode === 'new' ? inputOf(draft) : null, role,
      sharePercent: shareable ? numOrNull(share) : null, ownerType: CATEGORY_OF[role] === 'Ownership' ? orNull(ownerType) : null, notes: orNull(notes),
      ...(CATEGORY_OF[role] === 'Ownership' && displaced ? { displacementRequired: true } : {}),
      leaseOnFile: tenant && lease, leaseDetails: tenant ? orNull(leaseDetails) : null, insuranceDetails: tenant ? orNull(insurance) : null,
    }
    setBusy(true)
    try {
      if (target.kind === 'parcel') await api.addToParcel(target.parcelId, body)
      else await api.addToTract(target.projectId, target.tractId, body)
      await onAdded()
    } catch (err) {
      const fe = fieldErrorsOf(err)
      setFields(fe)
      setError(Object.keys(fe).length ? 'Please correct the highlighted fields.' : messageOf(err))
    } finally {
      setBusy(false)
    }
  }

  const partyErrors = partyFieldErrors(fields)
  return (
    <form onSubmit={add} noValidate aria-label="Add a person or organization" className="max-w-3xl space-y-4 rounded-lg border bg-card p-4">
      <h3 className="text-sm font-semibold">Add a person or organization</h3>
      <fieldset className="flex gap-4">
        <legend className="sr-only">Who to add</legend>
        <label className="flex items-center gap-2 text-sm">
          <input type="radio" name="who" checked={mode === 'existing'} onChange={() => setMode('existing')} /> Someone already in the system
        </label>
        <label className="flex items-center gap-2 text-sm">
          <input type="radio" name="who" checked={mode === 'new'} onChange={() => setMode('new')} /> A new person or organization
        </label>
      </fieldset>

      {mode === 'existing' ? (
        <div className="grid gap-3 sm:grid-cols-2">
          <TextField label="Find a party" value={search} onChange={setSearch} maxLength={100} hint="Search by name, city, phone or email." />
          <SelectField
            label="Party" required value={partyId} onChange={(v) => setPartyId(v)} error={fields.party ?? fields.partyId} blankLabel="Choose…"
            options={(people.data?.items ?? []).map((p) => ({ value: p.id, label: `${p.name}${p.city ? ` (${p.city})` : ''}` }))}
          />
        </div>
      ) : (
        <PartyFields draft={draft} set={set} errors={partyErrors} idPrefix="New party" />
      )}

      <div className="grid gap-3 sm:grid-cols-2">
        <SelectField label="Role" required value={role} onChange={setRole} options={roleOptions(allowOwnership)} error={fields.role} />
        {role !== '' && CATEGORY_OF[role] === 'Ownership' && <SelectField label="Owner type" value={ownerType} onChange={setOwnerType} options={optionsWith(ownerTypes, ownerType)} blankLabel="Select one" error={fields.ownerType} />}
        {role !== '' && CATEGORY_OF[role] === 'Ownership' && <CheckboxField label="Displacement required" checked={displaced} onChange={setDisplaced} />}
        {shareable && <TextField label="Share (%)" type="number" inputMode="decimal" value={share} onChange={setShare} error={fields.sharePercent} hint="Leave empty if not known." />}
        <TextField label="Role notes" value={notes} onChange={setNotes} maxLength={500} error={fields.notes} />
        {tenant && (
          <>
            <CheckboxField label="Lease on file" checked={lease} onChange={setLease} />
            <TextField label="Lease details" value={leaseDetails} onChange={setLeaseDetails} maxLength={1000} error={fields.leaseDetails} />
            <TextField label="Insurance details" value={insurance} onChange={setInsurance} maxLength={1000} error={fields.insuranceDetails} />
          </>
        )}
      </div>
      {!allowOwnership && <p className="text-xs text-muted-foreground">Owners and interest holders are added on the tract, so they are not offered here.</p>}
      <FormError error={error} />
      <div className="flex gap-2">
        <button type="submit" disabled={busy} className={buttonClassName('default')}>Add</button>
        <button type="button" className={buttonClassName('outline')} onClick={onCancel}>Cancel</button>
      </div>
    </form>
  )
}

