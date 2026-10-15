import { useState, type FormEvent, type ReactNode } from 'react'
import { Link, useNavigate } from '@tanstack/react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ShieldCheck } from 'lucide-react'
import {
  CATEGORY_LABEL, ROLE_LABEL, TAX_ID_LABEL, TAX_ID_TYPES, partiesApi, type Party, type TaxIdType,
} from '@/api/parties'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { PageHeader } from '@/components/ui/page-header'
import { ErrorState, LoadingRows } from '@/components/ui/states'
import { CheckboxField, DataTable, Field, FormError, Pill, SelectField, TextField, dateText, fieldErrorsOf, messageOf, orNull } from '../projects/form-kit'
import { KIND_LABEL, PartyFields, draftOf, inputOf, usePartyDraft } from './party-kit'

const TAX_OPTIONS = TAX_ID_TYPES.map((t) => ({ value: t, label: TAX_ID_LABEL[t] }))

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section aria-label={title} className="mb-6 rounded-lg border bg-card p-4">
      <h2 className="mb-3 text-sm font-semibold">{title}</h2>
      {children}
    </section>
  )
}

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-sm">{children}</dd>
    </div>
  )
}

/** One party: details, W-9 tax information (write-only number), and everywhere they appear (ROW-014..018). */
export function PartyDetailPage({ partyId }: { partyId: string }) {
  const { manager } = useAuth()
  const api = partiesApi(manager.api)
  const party = useQuery({ queryKey: ['party', partyId], queryFn: () => api.get(partyId) })
  const usage = useQuery({ queryKey: ['party', partyId, 'usage'], queryFn: () => api.usage(partyId) })
  const [editing, setEditing] = useState(false)

  if (party.isPending) return <LoadingRows />
  if (party.isError) return <ErrorState title="This party could not be opened" description={party.error.message} retry={() => void party.refetch()} />
  const p = party.data
  const canUpdate = manager.can('parties', 'update')
  const canDelete = manager.can('parties', 'delete')

  return (
    <>
      <PageHeader
        title={p.name}
        subtitle={p.legalName ? `Legal name: ${p.legalName}` : KIND_LABEL[p.kind]}
        crumbs={[{ label: 'Parties', to: '/parties' }, { label: p.name }]}
        actions={canUpdate && (
          <button type="button" className={buttonClassName('default')} onClick={() => setEditing(!editing)}>Edit details</button>
        )}
      />

      {editing && canUpdate ? (
        <EditPanel party={p} onClose={() => setEditing(false)} />
      ) : (
        <Section title="Details">
          <dl className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <Fact label="Type">{KIND_LABEL[p.kind]}</Fact>
            <Fact label="Email">{p.email ?? '—'}</Fact>
            <Fact label="Phone">{p.phone ?? '—'}</Fact>
            <Fact label="Address">
              {[p.addressLine1, p.addressLine2, [p.city, p.state].filter(Boolean).join(', '), p.postalCode].filter(Boolean).join(' · ') || '—'}
            </Fact>
            <Fact label="Last updated">{dateText(p.updatedAt)}</Fact>
          </dl>
          {p.notes && <p className="mt-3 whitespace-pre-wrap text-sm"><span className="text-xs text-muted-foreground">Notes: </span>{p.notes}</p>}
        </Section>
      )}

      <TaxSection party={p} canUpdate={canUpdate} />

      <Section title="Where this party appears">
        {usage.isPending ? (
          <LoadingRows rows={2} />
        ) : usage.isError ? (
          <ErrorState title="Usage could not be loaded" retry={() => void usage.refetch()} />
        ) : usage.data.length === 0 ? (
          <p className="text-sm text-muted-foreground">Not linked to any tract or parcel yet.</p>
        ) : (
          <DataTable caption="Where this party appears" headers={['Project', 'Record', 'Role', 'List']}>
            {usage.data.map((u) => (
              <tr key={u.linkId}>
                <td className="px-3 py-2">
                  <Link to="/projects/$projectId" params={{ projectId: u.projectId }} className="text-primary hover:underline">{u.projectNumber}</Link>
                </td>
                <td className="px-3 py-2">
                  {u.targetKind === 'Parcel'
                    ? <Link to="/parcels/$parcelId" params={{ parcelId: u.targetId }} className="text-primary hover:underline">{u.targetLabel}</Link>
                    : u.targetLabel}
                </td>
                <td className="px-3 py-2">{ROLE_LABEL[u.role]}</td>
                <td className="px-3 py-2">{CATEGORY_LABEL[u.category]}</td>
              </tr>
            ))}
          </DataTable>
        )}
      </Section>

      {canDelete && <DeletePanel party={p} />}
    </>
  )
}

function EditPanel({ party, onClose }: { party: Party; onClose: () => void }) {
  const { manager } = useAuth()
  const api = partiesApi(manager.api)
  const queryClient = useQueryClient()
  const { draft, set } = usePartyDraft(draftOf(party))
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)

  async function save(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setFields({})
    if (!draft.name.trim()) { setFields({ name: 'Name is required.' }); return }
    setBusy(true)
    try {
      await api.update(party.id, inputOf(draft))
      await Promise.all([queryClient.invalidateQueries({ queryKey: ['party'] }), queryClient.invalidateQueries({ queryKey: ['parties'] }), queryClient.invalidateQueries({ queryKey: ['parties-of'] })])
      onClose()
    } catch (err) {
      const fe = fieldErrorsOf(err)
      setFields(fe)
      setError(Object.keys(fe).length ? 'Please correct the highlighted fields.' : messageOf(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={save} noValidate aria-label="Edit party details" className="mb-6 space-y-3 rounded-lg border bg-card p-4">
      <h2 className="text-sm font-semibold">Edit details</h2>
      <PartyFields draft={draft} set={set} errors={fields} />
      <FormError error={error} />
      <div className="flex gap-2">
        <button type="submit" disabled={busy} className={buttonClassName('default')}>Save details</button>
        <button type="button" className={buttonClassName('outline')} onClick={onClose}>Cancel</button>
      </div>
    </form>
  )
}

/**
 * W-9 information for payees (ROW-018). The number goes to the server and is never shown again: the page only ever has the last
 * four digits, so there is nothing to reveal, copy or leave on screen.
 */
function TaxSection({ party: p, canUpdate }: { party: Party; canUpdate: boolean }) {
  const { manager } = useAuth()
  const api = partiesApi(manager.api)
  const queryClient = useQueryClient()
  const [type, setType] = useState<TaxIdType | ''>(p.taxIdType ?? 'Ssn')
  const [number, setNumber] = useState('')
  const [w9, setW9] = useState(p.w9ReceivedOn ?? '')
  const [r1099, setR1099] = useState(p.reportable1099)
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [saved, setSaved] = useState(false)
  const [busy, setBusy] = useState(false)

  const refresh = () => Promise.all([queryClient.invalidateQueries({ queryKey: ['party'] }), queryClient.invalidateQueries({ queryKey: ['parties'] })])

  async function save(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setFields({})
    setSaved(false)
    setBusy(true)
    try {
      await api.setTax(p.id, { type: type || null, taxId: orNull(number), w9ReceivedOn: orNull(w9), reportable1099: r1099 })
      setNumber('')
      setSaved(true)
      await refresh()
    } catch (err) {
      const fe = fieldErrorsOf(err)
      setFields(fe)
      setError(Object.keys(fe).length ? 'Please correct the highlighted fields.' : messageOf(err))
    } finally {
      setBusy(false)
    }
  }

  async function clear() {
    setError(null)
    setSaved(false)
    setBusy(true)
    try {
      await api.clearTaxId(p.id)
      setNumber('')
      await refresh()
    } catch (err) {
      setError(messageOf(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Section title="Tax information (W-9)">
      <dl className="mb-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Fact label="Tax ID on file">
          {p.hasTaxId && p.taxIdType && p.taxIdMasked
            ? <span className="inline-flex items-center gap-1.5"><ShieldCheck size={14} aria-hidden /> <span data-testid="tax-id-masked">{p.taxIdMasked}</span> <span className="text-xs text-muted-foreground">({p.taxIdType.toUpperCase()})</span></span>
            : 'None'}
        </Fact>
        <Fact label="W-9 received">{p.w9ReceivedOn ? dateText(p.w9ReceivedOn) : 'Not received'}</Fact>
        <Fact label="1099 reportable">{p.reportable1099 ? <Pill tone="ok">Yes</Pill> : 'No'}</Fact>
      </dl>
      {canUpdate ? (
        <form onSubmit={save} noValidate aria-label="Tax information" autoComplete="off" className="max-w-2xl space-y-3">
          <div className="grid gap-3 sm:grid-cols-2">
            <SelectField label="Type of tax ID" value={type} onChange={setType} options={TAX_OPTIONS} error={fields.type} />
            <Field
              label={p.hasTaxId ? 'Replace tax ID number' : 'Tax ID number'}
              hint={p.hasTaxId ? 'Leave empty to keep the number on file. It is never shown again after saving.' : '9 digits. It is encrypted when saved and never shown again.'}
              error={fields.taxId}
            >
              {(a) => (
                <input
                  {...a}
                  type="password"
                  autoComplete="off"
                  inputMode="numeric"
                  maxLength={20}
                  value={number}
                  onChange={(e) => setNumber(e.target.value)}
                  className="h-9 w-full rounded-md border border-input bg-background px-3 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
                />
              )}
            </Field>
            <TextField label="W-9 received on" type="date" value={w9} onChange={setW9} error={fields.w9ReceivedOn} />
            <CheckboxField label="Reportable on a 1099" checked={r1099} onChange={setR1099} />
          </div>
          <FormError error={error} />
          {saved && <p role="status" className="text-sm text-foreground">Tax information saved.</p>}
          <div className="flex gap-2">
            <button type="submit" disabled={busy} className={buttonClassName('default')}>Save tax information</button>
            {p.hasTaxId && (
              <button type="button" disabled={busy} className={buttonClassName('outline')} onClick={() => void clear()}>Remove tax ID number</button>
            )}
          </div>
        </form>
      ) : (
        <p className="text-xs text-muted-foreground">Your role can see that a tax ID is on file but not change it.</p>
      )}
    </Section>
  )
}

function DeletePanel({ party }: { party: Party }) {
  const { manager } = useAuth()
  const api = partiesApi(manager.api)
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const inUse = party.linkCount > 0

  async function remove() {
    setError(null)
    setBusy(true)
    try {
      await api.remove(party.id)
      await queryClient.invalidateQueries({ queryKey: ['parties'] })
      await navigate({ to: '/parties' })
    } catch (err) {
      setError(messageOf(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Section title="Delete this party">
      <p className="mb-3 text-sm text-muted-foreground">
        {inUse
          ? `This party is linked to ${party.linkCount} record${party.linkCount === 1 ? '' : 's'}. Remove those links first.`
          : 'Nothing uses this party, so it can be deleted.'}
      </p>
      <FormError error={error} />
      <button type="button" disabled={busy || inUse} className={buttonClassName('outline')} onClick={() => void remove()}>Delete party</button>
    </Section>
  )
}
