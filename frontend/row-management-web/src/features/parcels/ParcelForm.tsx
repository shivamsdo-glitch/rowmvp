import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  ACQUISITION_LABEL, ACQUISITION_TYPES, parcelsApi, type AcquisitionType, type CustomField, type Parcel, type ParcelInput,
} from '@/api/parcels'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { CheckboxField, fieldErrorsOf, FormError, messageOf, numOrNull, orNull, SelectField, TextField } from '../projects/form-kit'
import { optionsWith, useLibraryValues } from '../library/library-kit'
import { useProjectOptions } from './parcel-kit'

const ACQ_OPTIONS = ACQUISITION_TYPES.map((t) => ({ value: t, label: ACQUISITION_LABEL[t] }))
const YES_NO = [{ value: 'true', label: 'Yes' }, { value: 'false', label: 'No' }] as const

/** One input for a tenant-defined field (ROW-011), by its type. */
function CustomFieldInput({ def, value, error, onChange }: { def: CustomField; value: string; error?: string; onChange: (v: string) => void }) {
  if (def.fieldType === 'Boolean')
    return <SelectField label={def.label} required={def.isRequired} value={value} onChange={onChange} options={YES_NO} blankLabel="Not set" error={error} />
  if (def.fieldType === 'List')
    return <SelectField label={def.label} required={def.isRequired} value={value} onChange={onChange} options={def.options.map((o) => ({ value: o, label: o }))} blankLabel="Not set" error={error} />
  return (
    <TextField
      label={def.label} required={def.isRequired} value={value} onChange={onChange} error={error}
      type={def.fieldType === 'Number' ? 'number' : def.fieldType === 'Date' ? 'date' : 'text'}
      inputMode={def.fieldType === 'Number' ? 'decimal' : undefined} maxLength={500}
    />
  )
}

/** Create (no `parcel`) or edit one parcel: number, location, area, status, tract, group and the agency's custom fields (ROW-005, 011, 233). */
export function ParcelForm({
  parcel, defaultProjectId, onSubmit, onCancel, submitLabel,
}: {
  parcel?: Parcel
  defaultProjectId?: string
  onSubmit: (input: ParcelInput) => Promise<void>
  onCancel: () => void
  submitLabel: string
}) {
  const { manager } = useAuth()
  const api = parcelsApi(manager.api)
  const [projectId, setProjectId] = useState(parcel?.projectId ?? defaultProjectId ?? '')
  const [number, setNumber] = useState(parcel?.number ?? '')
  const [tractId, setTractId] = useState(parcel?.parentTractId ?? '')
  const [groupId, setGroupId] = useState(parcel?.groupId ?? '')
  const [legal, setLegal] = useState(parcel?.legalDescription ?? '')
  const [address, setAddress] = useState(parcel?.address ?? '')
  const [area, setArea] = useState(parcel ? String(parcel.areaSqFt) : '')
  const [propertyType, setPropertyType] = useState(parcel?.propertyType ?? '')
  const [statusId, setStatusId] = useState(parcel?.statusId ?? '')
  const [acquisition, setAcquisition] = useState<AcquisitionType>(parcel?.acquisitionType ?? 'Standard')
  const [custom, setCustom] = useState<Record<string, string>>(parcel?.customValues ?? {})
  const [cad, setCad] = useState(parcel?.cadNumber ?? '')
  const [interestType, setInterestType] = useState(parcel?.acquisitionInterestType ?? '')
  const [possessionDate, setPossessionDate] = useState(parcel?.possessionDate ?? '')
  const [proposedDate, setProposedDate] = useState(parcel?.proposedPossessionDate ?? '')
  const [segment, setSegment] = useState(parcel?.segment ?? '')
  const [priority, setPriority] = useState(parcel?.priority ?? '')
  const [possessionType, setPossessionType] = useState(parcel?.possessionType ?? '')
  const [location, setLocation] = useState(parcel?.parcelLocation ?? '')
  const [active, setActive] = useState(parcel?.isActiveParcel ?? true)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const projectOptions = useProjectOptions(projectId, !parcel)
  const groups = useQuery({ queryKey: ['groups', projectId], queryFn: () => api.groups(projectId), enabled: !!projectId })
  const tracts = useQuery({ queryKey: ['tracts', projectId], queryFn: () => api.tracts(projectId), enabled: !!projectId })
  const statuses = useQuery({ queryKey: ['parcel-statuses'], queryFn: () => api.statuses() })
  const fields = useQuery({ queryKey: ['custom-fields'], queryFn: () => api.customFields() })

  const interestValues = useLibraryValues('interest_type')
  const possessionValues = useLibraryValues('possession_type')
  const activeFields = (fields.data ?? []).filter((f) => f.isActive)
  const statusOptions = (statuses.data ?? []).filter((s) => s.isActive || s.id === parcel?.statusId).map((s) => ({ value: s.id, label: s.name }))

  async function submit(e: FormEvent) {
    e.preventDefault()
    const local: Record<string, string> = {}
    if (!projectId) local.projectId = 'Project is required.'
    if (area.trim() === '' || Number.isNaN(Number(area)) || Number(area) < 0) local.areaSqFt = 'Area must be zero or more square feet.'
    setErrors(local)
    setFormError(null)
    if (Object.keys(local).length) return

    setBusy(true)
    try {
      await onSubmit({
        projectId: parcel ? undefined : projectId,
        number: orNull(number), parentTractId: tractId || null, groupId: groupId || null,
        legalDescription: orNull(legal), address: orNull(address), areaSqFt: numOrNull(area) ?? 0,
        propertyType: orNull(propertyType), statusId: statusId || null, acquisitionType: acquisition,
        customValues: Object.fromEntries(activeFields.map((f) => [f.key, orNull(custom[f.key] ?? '')])),
        cadNumber: orNull(cad), acquisitionInterestType: orNull(interestType), possessionDate: orNull(possessionDate),
        proposedPossessionDate: orNull(proposedDate), segment: orNull(segment), priority: orNull(priority),
        possessionType: orNull(possessionType), parcelLocation: orNull(location), isActiveParcel: active,
      })
    } catch (err) {
      const fe = fieldErrorsOf(err)
      setErrors(fe)
      setFormError(Object.keys(fe).length ? 'Please correct the highlighted fields.' : messageOf(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} noValidate aria-label={parcel ? 'Edit parcel' : 'New parcel'} className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {!parcel && (
          <SelectField label="Project" required value={projectId} onChange={(v) => { setProjectId(v); setGroupId(''); setTractId('') }}
            options={projectOptions} blankLabel="Choose a project" error={errors.projectId} />
        )}
        <TextField label="Parcel number" value={number} onChange={setNumber} error={errors.number} maxLength={50}
          hint={parcel ? 'Numbers are never reused within a project.' : 'Leave blank to take the next number in the project.'} />
        <TextField label="Area (sq ft)" required type="number" inputMode="decimal" value={area} onChange={setArea} error={errors.areaSqFt} />
        <TextField label="Address" value={address} onChange={setAddress} error={errors.address} maxLength={300} />
        <TextField label="Property type" value={propertyType} onChange={setPropertyType} error={errors.propertyType} maxLength={100} />
        <SelectField label="Acquisition type" value={acquisition} onChange={(v) => setAcquisition((v || 'Standard') as AcquisitionType)} options={ACQ_OPTIONS} error={errors.acquisitionType} />
        <SelectField label="Status" value={statusId} onChange={setStatusId} options={statusOptions} blankLabel={parcel ? 'Keep current' : 'Initial status'} error={errors.statusId} />
        <SelectField label="Group" value={groupId} onChange={setGroupId} options={(groups.data ?? []).map((g) => ({ value: g.id, label: g.name }))} blankLabel="No group" error={errors.groupId} />
        <SelectField label="Parent tract" value={tractId} onChange={setTractId} options={(tracts.data ?? []).map((t) => ({ value: t.id, label: t.number }))} blankLabel="No parent tract" error={errors.parentTractId} />
      </div>
      <fieldset className="grid gap-4 rounded-md border p-3 sm:grid-cols-2 lg:grid-cols-3">
        <legend className="px-1 text-xs font-medium text-muted-foreground">Parcel details</legend>
        <TextField label="CAD number" value={cad} onChange={setCad} error={errors.cadNumber} maxLength={100} />
        <SelectField label="Acquisition interest type" value={interestType} onChange={setInterestType} options={optionsWith(interestValues, interestType)}
          blankLabel="Select one" error={errors.acquisitionInterestType} />
        <SelectField label="Possession type" value={possessionType} onChange={setPossessionType} options={optionsWith(possessionValues, possessionType)}
          blankLabel="Select one" error={errors.possessionType} />
        <TextField label="Possession date" type="date" value={possessionDate} onChange={setPossessionDate} error={errors.possessionDate} />
        <TextField label="Proposed possession date" type="date" value={proposedDate} onChange={setProposedDate} error={errors.proposedPossessionDate} />
        <TextField label="Segment" value={segment} onChange={setSegment} error={errors.segment} maxLength={100} />
        <TextField label="Priority" value={priority} onChange={setPriority} error={errors.priority} maxLength={100} />
        <TextField label="Parcel location" value={location} onChange={setLocation} error={errors.parcelLocation} maxLength={500} />
        <CheckboxField label="Active parcel" checked={active} onChange={setActive} />
      </fieldset>
      <TextField label="Legal description" value={legal} onChange={setLegal} error={errors.legalDescription} maxLength={4000} />
      {activeFields.length > 0 && (
        <fieldset className="grid gap-4 rounded-md border p-3 sm:grid-cols-2 lg:grid-cols-3">
          <legend className="px-1 text-xs font-medium text-muted-foreground">Agency fields</legend>
          {activeFields.map((f) => (
            <CustomFieldInput key={f.id} def={f} value={custom[f.key] ?? ''} onChange={(v) => setCustom((c) => ({ ...c, [f.key]: v }))}
              error={errors[`customValues.${f.key}`] ?? errors.customValues} />
          ))}
        </fieldset>
      )}
      <FormError error={formError} />
      <div className="flex gap-2">
        <button type="submit" disabled={busy} className={buttonClassName('default')}>{busy ? 'Saving…' : submitLabel}</button>
        <button type="button" disabled={busy} onClick={onCancel} className={buttonClassName('outline')}>Cancel</button>
      </div>
    </form>
  )
}
