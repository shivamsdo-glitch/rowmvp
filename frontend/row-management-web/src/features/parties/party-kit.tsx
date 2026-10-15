import { useState } from 'react'
import { PARTY_KINDS, PARTY_ROLES, ROLE_LABEL, CATEGORY_OF, type Party, type PartyInput, type PartyKind, type PartyRole } from '@/api/parties'
import { SelectField, TextField, orNull } from '../projects/form-kit'

export const KIND_LABEL: Record<PartyKind, string> = { Person: 'Person', Organization: 'Organization' }
export const KIND_OPTIONS = PARTY_KINDS.map((k) => ({ value: k, label: KIND_LABEL[k] }))

/** The roles that can be chosen where owners may not be entered (a parcel that belongs to a tract). */
export function roleOptions(allowOwnership: boolean) {
  return PARTY_ROLES.filter((r) => allowOwnership || CATEGORY_OF[r] !== 'Ownership').map((r) => ({ value: r, label: ROLE_LABEL[r] }))
}

export interface PartyDraft {
  kind: PartyKind
  name: string
  legalName: string
  email: string
  phone: string
  addressLine1: string
  addressLine2: string
  city: string
  state: string
  postalCode: string
  notes: string
}

export const emptyDraft = (): PartyDraft => ({
  kind: 'Person', name: '', legalName: '', email: '', phone: '', addressLine1: '', addressLine2: '', city: '', state: '', postalCode: '', notes: '',
})

export const draftOf = (p: Party): PartyDraft => ({
  kind: p.kind, name: p.name, legalName: p.legalName ?? '', email: p.email ?? '', phone: p.phone ?? '', addressLine1: p.addressLine1 ?? '',
  addressLine2: p.addressLine2 ?? '', city: p.city ?? '', state: p.state ?? '', postalCode: p.postalCode ?? '', notes: p.notes ?? '',
})

export const inputOf = (d: PartyDraft): PartyInput => ({
  kind: d.kind, name: d.name.trim(), legalName: orNull(d.legalName), email: orNull(d.email), phone: orNull(d.phone),
  addressLine1: orNull(d.addressLine1), addressLine2: orNull(d.addressLine2), city: orNull(d.city), state: orNull(d.state),
  postalCode: orNull(d.postalCode), notes: orNull(d.notes),
})

/** The server's field messages for a party typed inside another request (`NewParty.Name` → `name`). */
export function partyFieldErrors(fields: Record<string, string>, prefix = 'newParty.'): Record<string, string> {
  const out: Record<string, string> = {}
  for (const [k, v] of Object.entries(fields)) {
    if (k.startsWith(prefix)) out[k.slice(prefix.length).replace(/^./, (c) => c.toLowerCase())] = v
  }
  return out
}

export function usePartyDraft(initial?: PartyDraft) {
  const [draft, setDraft] = useState<PartyDraft>(initial ?? emptyDraft())
  const set = <K extends keyof PartyDraft>(key: K) => (value: PartyDraft[K]) => setDraft((d) => ({ ...d, [key]: value }))
  return { draft, set, reset: () => setDraft(initial ?? emptyDraft()) }
}

/** The fields every way of entering a party shares (ROW-014). */
export function PartyFields({
  draft, set, errors = {}, idPrefix,
}: {
  draft: PartyDraft
  set: <K extends keyof PartyDraft>(key: K) => (value: PartyDraft[K]) => void
  errors?: Record<string, string>
  idPrefix?: string
}) {
  const p = idPrefix ? `${idPrefix} ` : ''
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      <SelectField label={`${p}Type`} value={draft.kind} onChange={(v) => v && set('kind')(v)} options={KIND_OPTIONS} error={errors.kind} />
      <TextField label={`${p}Name`} required value={draft.name} onChange={set('name')} maxLength={200} error={errors.name} />
      <TextField label={`${p}Legal name`} value={draft.legalName} onChange={set('legalName')} maxLength={300} hint="As written on the vesting deed, if different." error={errors.legalName} />
      <TextField label={`${p}Email`} value={draft.email} onChange={set('email')} maxLength={200} error={errors.email} />
      <TextField label={`${p}Phone`} value={draft.phone} onChange={set('phone')} maxLength={40} error={errors.phone} />
      <TextField label={`${p}Address line 1`} value={draft.addressLine1} onChange={set('addressLine1')} maxLength={200} error={errors.addressLine1} />
      <TextField label={`${p}Address line 2`} value={draft.addressLine2} onChange={set('addressLine2')} maxLength={200} error={errors.addressLine2} />
      <TextField label={`${p}City`} value={draft.city} onChange={set('city')} maxLength={100} error={errors.city} />
      <TextField label={`${p}State`} value={draft.state} onChange={set('state')} maxLength={2} hint="Two-letter code." error={errors.state} />
      <TextField label={`${p}ZIP code`} value={draft.postalCode} onChange={set('postalCode')} maxLength={10} error={errors.postalCode} />
      <div className="sm:col-span-2">
        <TextField label={`${p}Notes`} value={draft.notes} onChange={set('notes')} maxLength={1000} error={errors.notes} />
      </div>
    </div>
  )
}

export const percentText = (v: number | null | undefined) => (v === null || v === undefined ? '—' : `${v.toLocaleString('en-US', { maximumFractionDigits: 4 })}%`)

export function describeRole(role: PartyRole) {
  return ROLE_LABEL[role]
}
