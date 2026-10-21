import { useState } from 'react'
import { Trash2 } from 'lucide-react'
import type { PartyAddress, PartyContact } from '@/api/parties'
import { buttonClassName } from '@/components/ui/button'
import { CheckboxField, DataTable, SelectField, TextField, orNull } from '../projects/form-kit'
import { optionsWith, useLibraryValues } from '../library/library-kit'

const BLANK_ADDRESS = { addressType: '', address: '', contactPerson: '', city: '', state: '', zip: '' }
const BLANK_CONTACT = { contactType: '', name: '', phone: '', email: '', address: '', isActive: true, isPrimary: false }

/**
 * A party's addresses: a grid edited inside the party's form and saved with it (ADR 0023). The address type comes from the Library.
 * The grid is the same wherever a party is entered: on a parcel's Ownership interests tab, on a tract and on the Parties page.
 */
export function PartyAddressesEditor({ rows, onChange, disabled }: { rows: PartyAddress[]; onChange: (rows: PartyAddress[]) => void; disabled?: boolean }) {
  const addressTypes = useLibraryValues('address_type')
  const [addr, setAddr] = useState(BLANK_ADDRESS)
  const [error, setError] = useState<string | null>(null)

  function add() {
    if (!Object.values(addr).some((v) => v.trim())) { setError('Enter at least part of the address.'); return }
    onChange([...rows, {
      addressType: orNull(addr.addressType), address: orNull(addr.address), contactPerson: orNull(addr.contactPerson),
      city: orNull(addr.city), state: orNull(addr.state), zip: orNull(addr.zip),
    }])
    setAddr(BLANK_ADDRESS)
    setError(null)
  }

  return (
    <fieldset className="rounded-md border p-3 sm:col-span-2">
      <legend className="px-1 text-xs font-medium text-muted-foreground">Addresses</legend>
      {rows.length > 0 && (
        <DataTable caption="Addresses" headers={['Address type', 'Address', 'Contact person', 'City', 'State', 'Zip', ...(disabled ? [] : ['Actions'])]}>
          {rows.map((x, i) => (
            <tr key={`${x.address}-${i}`}>
              <td className="px-3 py-2">{x.addressType ?? '—'}</td>
              <td className="px-3 py-2">{x.address ?? '—'}</td>
              <td className="px-3 py-2">{x.contactPerson ?? '—'}</td>
              <td className="px-3 py-2">{x.city ?? '—'}</td>
              <td className="px-3 py-2">{x.state ?? '—'}</td>
              <td className="px-3 py-2">{x.zip ?? '—'}</td>
              {!disabled && (
                <td className="px-3 py-2">
                  <button type="button" aria-label={`Remove address ${i + 1}`} className={buttonClassName('ghost', 'sm')} onClick={() => onChange(rows.filter((_, j) => j !== i))}><Trash2 size={14} aria-hidden /></button>
                </td>
              )}
            </tr>
          ))}
        </DataTable>
      )}
      {!disabled && (
        <>
          <div className="mt-3 grid gap-3 sm:grid-cols-3">
            <SelectField label="Address type" value={addr.addressType} onChange={(v) => setAddr({ ...addr, addressType: v })} options={optionsWith(addressTypes, addr.addressType)} blankLabel="Select one" />
            <TextField label="Address" value={addr.address} onChange={(v) => setAddr({ ...addr, address: v })} maxLength={300} error={error ?? undefined} />
            <TextField label="Address contact person" value={addr.contactPerson} onChange={(v) => setAddr({ ...addr, contactPerson: v })} maxLength={200} />
            <TextField label="City" value={addr.city} onChange={(v) => setAddr({ ...addr, city: v })} maxLength={100} />
            <TextField label="State" value={addr.state} onChange={(v) => setAddr({ ...addr, state: v })} maxLength={50} />
            <TextField label="Zip" value={addr.zip} onChange={(v) => setAddr({ ...addr, zip: v })} maxLength={20} />
          </div>
          <button type="button" className={`${buttonClassName('outline', 'sm')} mt-2`} onClick={add}>Add address</button>
        </>
      )}
    </fieldset>
  )
}

/** A party's contact people (a spouse, an agent, a family contact): a grid edited inside the party's form and saved with it. */
export function PartyContactsEditor({ rows, onChange, disabled }: { rows: PartyContact[]; onChange: (rows: PartyContact[]) => void; disabled?: boolean }) {
  const contactTypes = useLibraryValues('contact_type')
  const [contact, setContact] = useState(BLANK_CONTACT)
  const [error, setError] = useState<string | null>(null)

  function add() {
    if (!contact.name.trim()) { setError('Enter the contact name.'); return }
    if (contact.isPrimary && rows.some((c) => c.isPrimary)) { setError('Only one contact can be the primary contact.'); return }
    onChange([...rows, {
      contactType: orNull(contact.contactType), name: contact.name.trim(), phone: orNull(contact.phone), email: orNull(contact.email),
      address: orNull(contact.address), isActive: contact.isActive, isPrimary: contact.isPrimary,
    }])
    setContact(BLANK_CONTACT)
    setError(null)
  }

  return (
    <fieldset className="rounded-md border p-3 sm:col-span-2">
      <legend className="px-1 text-xs font-medium text-muted-foreground">Contacts</legend>
      {rows.length > 0 && (
        <DataTable caption="Contacts" headers={['Contact type', 'Name', 'Phone number', 'Email', 'Address', 'Active', 'Is primary', ...(disabled ? [] : ['Actions'])]}>
          {rows.map((c, i) => (
            <tr key={`${c.name}-${i}`}>
              <td className="px-3 py-2">{c.contactType ?? '—'}</td>
              <td className="px-3 py-2">{c.name}</td>
              <td className="px-3 py-2">{c.phone ?? '—'}</td>
              <td className="px-3 py-2">{c.email ?? '—'}</td>
              <td className="px-3 py-2">{c.address ?? '—'}</td>
              <td className="px-3 py-2">{c.isActive ? 'Yes' : 'No'}</td>
              <td className="px-3 py-2">{c.isPrimary ? 'Yes' : 'No'}</td>
              {!disabled && (
                <td className="px-3 py-2">
                  <button type="button" aria-label={`Remove contact ${c.name}`} className={buttonClassName('ghost', 'sm')} onClick={() => onChange(rows.filter((_, j) => j !== i))}><Trash2 size={14} aria-hidden /></button>
                </td>
              )}
            </tr>
          ))}
        </DataTable>
      )}
      {!disabled && (
        <>
          <div className="mt-3 grid gap-3 sm:grid-cols-3">
            <SelectField label="Contact type" value={contact.contactType} onChange={(v) => setContact({ ...contact, contactType: v })} options={optionsWith(contactTypes, contact.contactType)} blankLabel="Select one" />
            <TextField label="Contact name" value={contact.name} onChange={(v) => setContact({ ...contact, name: v })} maxLength={200} error={error ?? undefined} />
            <TextField label="Contact phone number" value={contact.phone} onChange={(v) => setContact({ ...contact, phone: v })} maxLength={40} />
            <TextField label="Contact email" value={contact.email} onChange={(v) => setContact({ ...contact, email: v })} maxLength={200} />
            <TextField label="Contact address" value={contact.address} onChange={(v) => setContact({ ...contact, address: v })} maxLength={300} />
            <div className="flex gap-4">
              <CheckboxField label="Active" checked={contact.isActive} onChange={(v) => setContact({ ...contact, isActive: v })} />
              <CheckboxField label="Is primary" checked={contact.isPrimary} onChange={(v) => setContact({ ...contact, isPrimary: v })} />
            </div>
          </div>
          <button type="button" className={`${buttonClassName('outline', 'sm')} mt-2`} onClick={add}>Add contact</button>
        </>
      )}
    </fieldset>
  )
}
