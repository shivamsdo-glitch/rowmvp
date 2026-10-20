import { useState, type FormEvent, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Pencil, Trash2 } from 'lucide-react'
import { ownershipApi, type Ownership, type OwnershipAddress, type OwnershipContact } from '@/api/parcel-tabs'
import type { Parcel } from '@/api/parcels'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { CheckboxField, DataTable, fieldErrorsOf, FormError, messageOf, orNull, SelectField, TextField } from '../projects/form-kit'
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

const BLANK_ADDRESS = { addressType: '', address: '', contactPerson: '', city: '', state: '', zip: '' }
const BLANK_CONTACT = { contactType: '', name: '', phone: '', email: '', address: '', isActive: true, isPrimary: false }

/** Ownership Details (Phase 1.5, ADR 0017): each owner of a parcel with their type, addresses, contacts and attachments. */
export function OwnershipTab({ parcelId, editable }: { parcelId: string; editable: boolean }) {
  const { manager } = useAuth()
  const api = ownershipApi(manager.api)
  const queryClient = useQueryClient()
  const key = ['parcel-tabs', parcelId, 'ownerships']
  const list = useQuery({ queryKey: key, queryFn: () => api.list(parcelId), enabled: manager.can('ownership') })
  const ownerTypes = useLibraryValues('owner_type')
  const addressTypes = useLibraryValues('address_type')
  const contactTypes = useLibraryValues('contact_type')
  const [editing, setEditing] = useState<Ownership | null>(null)
  const [name, setName] = useState('')
  const [type, setType] = useState('')
  const [addresses, setAddresses] = useState<OwnershipAddress[]>([])
  const [contacts, setContacts] = useState<OwnershipContact[]>([])
  const [addr, setAddr] = useState(BLANK_ADDRESS)
  const [contact, setContact] = useState(BLANK_CONTACT)
  const [local, setLocal] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const canAdd = editable && manager.can('ownership', 'create')
  const canEdit = editable && manager.can('ownership', 'update')
  const canRemove = editable && manager.can('ownership', 'delete')

  if (!manager.can('ownership')) return <p className="text-sm text-muted-foreground">Your role cannot see ownership details.</p>
  if (list.isPending) return <LoadingRows rows={3} />
  if (list.isError) return <ErrorState title="The ownership details could not be loaded" description={list.error.message} retry={() => void list.refetch()} />

  function reset() {
    setEditing(null); setName(''); setType(''); setAddresses([]); setContacts([]); setAddr(BLANK_ADDRESS); setContact(BLANK_CONTACT)
    setLocal({}); setError(null); setFields({})
  }

  function begin(o: Ownership) {
    setEditing(o); setName(o.ownerName); setType(o.ownerType ?? ''); setAddresses(o.addresses); setContacts(o.contacts)
    setLocal({}); setError(null); setFields({})
  }

  /** Runs a save or delete; the parcel's read-only "Owner name" comes from the ownerships, so it is refreshed too. */
  async function run(action: () => Promise<unknown>): Promise<boolean> {
    setError(null)
    setFields({})
    setBusy(true)
    try {
      await action()
      await Promise.all([queryClient.invalidateQueries({ queryKey: key }), queryClient.invalidateQueries({ queryKey: ['parcel', parcelId] })])
      return true
    } catch (e) {
      const fe = fieldErrorsOf(e)
      setFields(fe)
      setError(Object.keys(fe).length ? 'Please correct the highlighted fields.' : messageOf(e))
      return false
    } finally {
      setBusy(false)
    }
  }

  function addAddress() {
    if (!Object.values(addr).some((v) => v.trim())) { setLocal({ ...local, address: 'Enter at least part of the address.' }); return }
    setAddresses([...addresses, {
      addressType: orNull(addr.addressType), address: orNull(addr.address), contactPerson: orNull(addr.contactPerson),
      city: orNull(addr.city), state: orNull(addr.state), zip: orNull(addr.zip),
    }])
    setAddr(BLANK_ADDRESS)
    setLocal({ ...local, address: '' })
  }

  function addContact() {
    if (!contact.name.trim()) { setLocal({ ...local, contactName: 'Enter the contact name.' }); return }
    if (contact.isPrimary && contacts.some((c) => c.isPrimary)) { setLocal({ ...local, contactName: 'Only one contact can be the primary contact.' }); return }
    setContacts([...contacts, {
      contactType: orNull(contact.contactType), name: contact.name.trim(), phone: orNull(contact.phone), email: orNull(contact.email),
      address: orNull(contact.address), isActive: contact.isActive, isPrimary: contact.isPrimary,
    }])
    setContact(BLANK_CONTACT)
    setLocal({ ...local, contactName: '' })
  }

  async function submit(ev: FormEvent) {
    ev.preventDefault()
    const errs: Record<string, string> = {}
    if (!name.trim()) errs.ownerName = 'Owner name is required.'
    setLocal(errs)
    if (Object.keys(errs).length) return
    const body = { ownerName: name.trim(), ownerType: orNull(type), addresses, contacts }
    if (await run(() => (editing ? api.update(parcelId, editing.id, body) : api.add(parcelId, body)))) reset()
  }
  const errors = { ...fields, ...local }

  return (
    <>
      <Section title="Ownership details">
        {list.data.length === 0 ? (
          <EmptyState title="No ownerships recorded" description="Record each owner of this parcel with their addresses and contacts." />
        ) : (
          <DataTable caption="Ownership details" headers={['Ownership number', 'Owner name', 'Owner type', 'Addresses', 'Contacts', ...(canEdit || canRemove ? ['Actions'] : [])]}>
            {list.data.map((o) => (
              <tr key={o.id}>
                <td className="px-3 py-2 font-medium">{o.ownershipNumber}</td>
                <td className="px-3 py-2">{o.ownerName}</td>
                <td className="px-3 py-2">{o.ownerType ?? '—'}</td>
                <td className="px-3 py-2">{o.addresses.length}</td>
                <td className="px-3 py-2">{o.contacts.length}</td>
                {(canEdit || canRemove) && (
                  <td className="px-3 py-2">
                    <span className="inline-flex gap-1">
                      {canEdit && <button type="button" aria-label={`Edit ownership ${o.ownershipNumber}`} className={buttonClassName('ghost', 'sm')} onClick={() => begin(o)}><Pencil size={14} aria-hidden /></button>}
                      {canRemove && (
                        <button type="button" aria-label={`Remove ownership ${o.ownershipNumber}`} disabled={busy} className={buttonClassName('ghost', 'sm')}
                          onClick={() => void run(() => api.remove(parcelId, o.id)).then((ok) => { if (ok && editing?.id === o.id) reset() })}>
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
        <form onSubmit={submit} noValidate aria-label={editing ? 'Change ownership' : 'Add an ownership'} className="max-w-4xl space-y-3 rounded-lg border bg-card p-4">
          <h3 className="text-sm font-semibold">{editing ? `Change ownership ${editing.ownershipNumber}` : 'Add an ownership'}</h3>
          <div className="grid gap-3 sm:grid-cols-2">
            <TextField label="Owner name" required value={name} onChange={setName} maxLength={200} error={errors.ownerName} />
            <SelectField label="Owner type" value={type} onChange={setType} options={optionsWith(ownerTypes, type)} blankLabel="Select one" error={errors.ownerType} />
          </div>

          <fieldset className="rounded-md border p-3">
            <legend className="px-1 text-xs font-medium text-muted-foreground">Addresses</legend>
            {addresses.length > 0 && (
              <DataTable caption="Addresses" headers={['Address type', 'Address', 'Contact person', 'City', 'State', 'Zip', 'Actions']}>
                {addresses.map((x, i) => (
                  <tr key={`${x.address}-${i}`}>
                    <td className="px-3 py-2">{x.addressType ?? '—'}</td>
                    <td className="px-3 py-2">{x.address ?? '—'}</td>
                    <td className="px-3 py-2">{x.contactPerson ?? '—'}</td>
                    <td className="px-3 py-2">{x.city ?? '—'}</td>
                    <td className="px-3 py-2">{x.state ?? '—'}</td>
                    <td className="px-3 py-2">{x.zip ?? '—'}</td>
                    <td className="px-3 py-2">
                      <button type="button" aria-label={`Remove address ${i + 1}`} className={buttonClassName('ghost', 'sm')} onClick={() => setAddresses(addresses.filter((_, j) => j !== i))}><Trash2 size={14} aria-hidden /></button>
                    </td>
                  </tr>
                ))}
              </DataTable>
            )}
            <div className="mt-3 grid gap-3 sm:grid-cols-3">
              <SelectField label="Address type" value={addr.addressType} onChange={(v) => setAddr({ ...addr, addressType: v })} options={optionsWith(addressTypes, addr.addressType)} blankLabel="Select one" />
              <TextField label="Address" value={addr.address} onChange={(v) => setAddr({ ...addr, address: v })} maxLength={300} error={errors.address} />
              <TextField label="Address contact person" value={addr.contactPerson} onChange={(v) => setAddr({ ...addr, contactPerson: v })} maxLength={200} />
              <TextField label="City" value={addr.city} onChange={(v) => setAddr({ ...addr, city: v })} maxLength={100} />
              <TextField label="State" value={addr.state} onChange={(v) => setAddr({ ...addr, state: v })} maxLength={50} />
              <TextField label="Zip" value={addr.zip} onChange={(v) => setAddr({ ...addr, zip: v })} maxLength={20} />
            </div>
            <button type="button" className={`${buttonClassName('outline', 'sm')} mt-2`} onClick={addAddress}>Add address</button>
          </fieldset>

          <fieldset className="rounded-md border p-3">
            <legend className="px-1 text-xs font-medium text-muted-foreground">Contacts</legend>
            {contacts.length > 0 && (
              <DataTable caption="Contacts" headers={['Contact type', 'Name', 'Phone number', 'Email', 'Address', 'Active', 'Is primary', 'Actions']}>
                {contacts.map((c, i) => (
                  <tr key={`${c.name}-${i}`}>
                    <td className="px-3 py-2">{c.contactType ?? '—'}</td>
                    <td className="px-3 py-2">{c.name}</td>
                    <td className="px-3 py-2">{c.phone ?? '—'}</td>
                    <td className="px-3 py-2">{c.email ?? '—'}</td>
                    <td className="px-3 py-2">{c.address ?? '—'}</td>
                    <td className="px-3 py-2">{c.isActive ? 'Yes' : 'No'}</td>
                    <td className="px-3 py-2">{c.isPrimary ? 'Yes' : 'No'}</td>
                    <td className="px-3 py-2">
                      <button type="button" aria-label={`Remove contact ${c.name}`} className={buttonClassName('ghost', 'sm')} onClick={() => setContacts(contacts.filter((_, j) => j !== i))}><Trash2 size={14} aria-hidden /></button>
                    </td>
                  </tr>
                ))}
              </DataTable>
            )}
            <div className="mt-3 grid gap-3 sm:grid-cols-3">
              <SelectField label="Contact type" value={contact.contactType} onChange={(v) => setContact({ ...contact, contactType: v })} options={optionsWith(contactTypes, contact.contactType)} blankLabel="Select one" />
              <TextField label="Contact name" value={contact.name} onChange={(v) => setContact({ ...contact, name: v })} maxLength={200} error={errors.contactName} />
              <TextField label="Phone number" value={contact.phone} onChange={(v) => setContact({ ...contact, phone: v })} maxLength={40} />
              <TextField label="Email" value={contact.email} onChange={(v) => setContact({ ...contact, email: v })} maxLength={200} />
              <TextField label="Contact address" value={contact.address} onChange={(v) => setContact({ ...contact, address: v })} maxLength={300} />
              <div className="flex gap-4">
                <CheckboxField label="Active" checked={contact.isActive} onChange={(v) => setContact({ ...contact, isActive: v })} />
                <CheckboxField label="Is primary" checked={contact.isPrimary} onChange={(v) => setContact({ ...contact, isPrimary: v })} />
              </div>
            </div>
            <button type="button" className={`${buttonClassName('outline', 'sm')} mt-2`} onClick={addContact}>Add contact</button>
          </fieldset>

          <FormError error={error} />
          <div className="flex gap-2">
            <button type="submit" disabled={busy} className={buttonClassName('default')}>{editing ? 'Save changes' : 'Add ownership'}</button>
            {editing && <button type="button" className={buttonClassName('outline')} onClick={reset}>Cancel</button>}
          </div>
        </form>
      )}

      {editing && <AttachmentsPanel entityType="Ownership" entityId={editing.id} page="ownership" title={`Attachments for ownership ${editing.ownershipNumber}`} />}
    </>
  )
}

/**
 * Parcel Location (read-only): the parcel's id, CAD number and whether its shape has been saved to ESRI. There is no map or GIS link
 * yet (ADR 0017), so nothing has been saved and the status says so.
 */
export function ParcelLocationTab({ parcel }: { parcel: Parcel }) {
  const status = 'Not saved'
  function exportCsv() {
    const q = (v: string) => `"${v.replace(/"/g, '""')}"`
    const csv = ['ID,CAD Number,ESRI Save Status', [parcel.number, parcel.cadNumber ?? '', status].map(q).join(',')].join('\r\n')
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }))
    const a = document.createElement('a')
    a.href = url
    a.download = `parcel-location-${parcel.number}.csv`
    a.click()
    URL.revokeObjectURL(url)
  }
  return (
    <Section title="Parcel location">
      <div className="mb-3 flex items-center justify-between">
        <p className="text-xs text-muted-foreground">Map and GIS are not connected yet, so no parcel shape has been saved to ESRI.</p>
        <button type="button" className={buttonClassName('outline', 'sm')} onClick={exportCsv}>Export to CSV</button>
      </div>
      <DataTable caption="Parcel location" headers={['ID', 'CAD number', 'ESRI save status']}>
        <tr>
          <td className="px-3 py-2 font-medium">{parcel.number}</td>
          <td className="px-3 py-2">{parcel.cadNumber ?? '—'}</td>
          <td className="px-3 py-2">{status}</td>
        </tr>
      </DataTable>
    </Section>
  )
}
