import { useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Download, Upload } from 'lucide-react'
import {
  CUSTOM_FIELD_TYPES, libraryApi, parseValues, valuesToCsv, type LibraryField, type LibraryFieldSummary, type LibraryFieldType,
} from '@/api/library'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { PageHeader } from '@/components/ui/page-header'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { DataTable, fieldErrorsOf, FormError, messageOf, Pill, SelectField, TextField } from '../projects/form-kit'

const TYPE_OPTIONS = CUSTOM_FIELD_TYPES.map((t) => ({ value: t, label: t === 'Boolean' ? 'Yes / No' : t }))
const typeName = (t: LibraryFieldType) => TYPE_OPTIONS.find((o) => o.value === t)?.label ?? t

/**
 * The Library: fields the agency defines once — a name, a type and, for a list, the values a user may choose — that forms then
 * use. Default fields and values are attached when the agency is set up (administrator only).
 */
export function LibraryPage() {
  const { manager } = useAuth()
  const api = libraryApi(manager.api)
  const queryClient = useQueryClient()
  const canCreate = manager.can('administration', 'create')
  const fields = useQuery({ queryKey: ['library', 'fields'], queryFn: () => api.fields() })
  const [selected, setSelected] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['library'] })

  return (
    <>
      <PageHeader title="Library" subtitle="Define fields once — their type and the values people can choose — and forms use them. Defaults are attached; change them here." />
      {fields.isPending ? (
        <LoadingRows />
      ) : fields.isError ? (
        <ErrorState title="The library could not be loaded" description={fields.error.message} retry={() => void fields.refetch()} />
      ) : (
        <div className="grid gap-6 lg:grid-cols-[18rem_1fr]">
          <nav aria-label="Library fields" className="space-y-2">
            <ul className="space-y-1">
              {fields.data.map((f) => (
                <li key={f.id}>
                  <button
                    type="button"
                    aria-current={(selected ?? fields.data[0]?.id) === f.id ? 'true' : undefined}
                    onClick={() => setSelected(f.id)}
                    className={`flex w-full items-center justify-between rounded-md border px-3 py-2 text-left text-sm hover:bg-muted/50 ${(selected ?? fields.data[0]?.id) === f.id ? 'border-primary bg-muted/60 font-medium' : ''}`}
                  >
                    <span>{f.label}</span>
                    <span className="text-xs text-muted-foreground">{f.fieldType === 'List' ? `${f.activeCount}/${f.valueCount}` : typeName(f.fieldType)}</span>
                  </button>
                </li>
              ))}
            </ul>
            {canCreate && !adding && <button type="button" className={buttonClassName('outline')} onClick={() => setAdding(true)}>New field</button>}
            {canCreate && adding && (
              <NewFieldForm
                onCancel={() => setAdding(false)}
                onCreated={async (f) => { setAdding(false); setSelected(f.id); await refresh() }}
              />
            )}
          </nav>
          <FieldPanel summary={fields.data.find((f) => f.id === (selected ?? fields.data[0]?.id))} onChanged={refresh} />
        </div>
      )}
    </>
  )
}

function NewFieldForm({ onCancel, onCreated }: { onCancel: () => void; onCreated: (f: LibraryField) => Promise<void> }) {
  const { manager } = useAuth()
  const api = libraryApi(manager.api)
  const [label, setLabel] = useState('')
  const [type, setType] = useState<LibraryFieldType | ''>('List')
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setFields({})
    if (!label.trim()) { setFields({ label: 'Name is required.' }); return }
    setBusy(true)
    try {
      await onCreated(await api.createField(label.trim(), type || 'List'))
    } catch (err) {
      const fe = fieldErrorsOf(err)
      setFields(fe)
      setError(Object.keys(fe).length ? 'Please correct the highlighted fields.' : messageOf(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} noValidate aria-label="Define a field" className="space-y-3 rounded-lg border bg-card p-3">
      <h2 className="text-sm font-semibold">Define a field</h2>
      <TextField label="Field name" value={label} onChange={setLabel} maxLength={100} error={fields.label} />
      <SelectField label="Field type" value={type} onChange={setType} options={TYPE_OPTIONS} error={fields.fieldType} />
      <FormError error={error} />
      <div className="flex gap-2">
        <button type="submit" disabled={busy} className={buttonClassName('default', 'sm')}>Create field</button>
        <button type="button" className={buttonClassName('outline', 'sm')} onClick={onCancel}>Cancel</button>
      </div>
    </form>
  )
}

function FieldPanel({ summary, onChanged }: { summary: LibraryFieldSummary | undefined; onChanged: () => Promise<unknown> }) {
  const { manager } = useAuth()
  const api = libraryApi(manager.api)
  const canCreate = manager.can('administration', 'create')
  const canUpdate = manager.can('administration', 'update')
  const field = useQuery({ queryKey: ['library', 'field', summary?.id], queryFn: () => api.field(summary!.id), enabled: !!summary })
  const queryClient = useQueryClient()
  const [filter, setFilter] = useState('')
  const [newValue, setNewValue] = useState('')
  const [showNew, setShowNew] = useState(false)
  const [editing, setEditing] = useState<string | null>(null)
  const [editText, setEditText] = useState('')
  const [editField, setEditField] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const fileInput = useRef<HTMLInputElement>(null)

  if (!summary) return <EmptyState title="No fields yet" description="Define the first field." />
  if (field.isPending) return <LoadingRows rows={4} />
  if (field.isError) return <ErrorState title="This field could not be opened" description={field.error.message} retry={() => void field.refetch()} />
  const f = field.data
  const isList = f.fieldType === 'List'

  async function run(action: () => Promise<unknown>, success?: string): Promise<boolean> {
    setError(null)
    setNotice(null)
    setBusy(true)
    try {
      await action()
      await Promise.all([onChanged(), queryClient.invalidateQueries({ queryKey: ['library', 'field', f.id] })])
      if (success) setNotice(success)
      return true
    } catch (e) {
      setError(messageOf(e))
      return false
    } finally {
      setBusy(false)
    }
  }

  async function addValue(e: FormEvent) {
    e.preventDefault()
    if (!newValue.trim()) { setError('Value is required.'); return }
    if (await run(() => api.addValue(f.id, newValue.trim()))) { setNewValue(''); setShowNew(false) }
  }

  async function saveEdit(valueId: string, isActive: boolean) {
    if (!editText.trim()) { setError('Value is required.'); return }
    if (await run(() => api.updateValue(f.id, valueId, editText.trim(), isActive))) setEditing(null)
  }

  async function onFile(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file) return
    const values = parseValues(await file.text(), f.label)
    if (values.length === 0) { setError('That file has no values to import.'); setNotice(null); return }
    await run(async () => {
      const result = await api.importValues(f.id, values)
      setNotice(`${result.added} added, ${result.alreadyThere} already there.`)
    })
  }

  function exportCsv() {
    const blob = new Blob([valuesToCsv(f.label, f.values)], { type: 'text/csv;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${f.key}.csv`
    a.click()
    URL.revokeObjectURL(url)
  }

  const shown = f.values.filter((v) => v.value.toLowerCase().includes(filter.trim().toLowerCase()))

  return (
    <section aria-label={f.label} className="min-w-0 space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="text-base font-semibold">{f.label}</h2>
          <p className="text-xs text-muted-foreground">
            {typeName(f.fieldType)}{f.isSystem && <> · <Pill tone="neutral">Used by the product</Pill></>}
            {f.key === 'parcel_status' && <> · These are the parcel statuses. New parcels start as the default (Inactive).</>}
          </p>
        </div>
        {canUpdate && <button type="button" className={buttonClassName('outline', 'sm')} onClick={() => setEditField(!editField)}>Edit field</button>}
      </div>

      {editField && canUpdate && (
        <EditFieldForm field={f} onClose={() => setEditField(false)} onSaved={async () => { setEditField(false); await Promise.all([onChanged(), queryClient.invalidateQueries({ queryKey: ['library', 'field', f.id] })]) }} />
      )}

      {!isList ? (
        <p className="rounded-md border bg-muted/40 px-3 py-2 text-sm">
          This is a {typeName(f.fieldType).toLowerCase()} field: people type it in on forms, so there are no values to choose from.
        </p>
      ) : (
        <>
          <div role="toolbar" aria-label={`${f.label} actions`} className="flex flex-wrap items-center gap-2">
            {canCreate && <button type="button" className={buttonClassName('default', 'sm')} onClick={() => setShowNew(!showNew)}>New</button>}
            {canUpdate && (
              <>
                <button type="button" className={buttonClassName('outline', 'sm')} disabled={busy} onClick={() => fileInput.current?.click()}>
                  <Upload size={14} aria-hidden /> Import
                </button>
                <input ref={fileInput} type="file" accept=".csv,.txt,text/csv,text/plain" aria-label="File to import" className="sr-only" tabIndex={-1} onChange={(e) => void onFile(e)} />
              </>
            )}
            <button type="button" className={buttonClassName('outline', 'sm')} onClick={exportCsv}><Download size={14} aria-hidden /> Export</button>
          </div>

          {showNew && canCreate && (
            <form onSubmit={addValue} noValidate aria-label="Add a value" className="flex max-w-xl flex-wrap items-end gap-3 rounded-lg border bg-card p-3">
              <TextField label="New value" value={newValue} onChange={setNewValue} maxLength={100} />
              <button type="submit" disabled={busy} className={buttonClassName('default')}>Add value</button>
            </form>
          )}
          <FormError error={error} />
          {notice && <p role="status" className="text-sm">{notice}</p>}

          <TextField label={`Filter ${f.label}`} value={filter} onChange={setFilter} placeholder="Type to filter" />
          {f.values.length === 0 ? (
            <EmptyState title="No values yet" description={canCreate ? 'Add the first value, or import a list.' : 'Nothing to choose from yet.'} />
          ) : shown.length === 0 ? (
            <p className="text-sm text-muted-foreground">No value matches your filter.</p>
          ) : (
            <DataTable caption={`${f.label} values`} headers={canUpdate ? [f.label, 'Is active', 'Actions'] : [f.label, 'Is active']}>
              {shown.map((v) => (
                <tr key={v.id}>
                  <td className="px-3 py-2">
                    {editing === v.id ? (
                      <input
                        aria-label={`Value for ${v.value}`} value={editText} maxLength={100} autoFocus
                        onChange={(e) => setEditText(e.target.value)}
                        className="h-8 w-full rounded-md border border-input bg-background px-2 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
                      />
                    ) : (
                      <>{v.value}{v.isDefault && <span className="ml-2"><Pill tone="neutral">Default</Pill></span>}</>
                    )}
                  </td>
                  <td className="px-3 py-2">
                    <input
                      type="checkbox" className="h-4 w-4" checked={v.isActive} disabled={!canUpdate || busy}
                      aria-label={`${v.value} is active`}
                      onChange={(e) => void run(() => api.updateValue(f.id, v.id, v.value, e.target.checked))}
                    />
                  </td>
                  {canUpdate && (
                    <td className="px-3 py-2">
                      {editing === v.id ? (
                        <span className="inline-flex gap-1">
                          <button type="button" disabled={busy} className={buttonClassName('default', 'sm')} onClick={() => void saveEdit(v.id, v.isActive)}>Save</button>
                          <button type="button" className={buttonClassName('outline', 'sm')} onClick={() => setEditing(null)}>Cancel</button>
                        </span>
                      ) : (
                        <button type="button" aria-label={`Rename ${v.value}`} className={buttonClassName('ghost', 'sm')} onClick={() => { setEditing(v.id); setEditText(v.value); setError(null) }}>Rename</button>
                      )}
                    </td>
                  )}
                </tr>
              ))}
            </DataTable>
          )}
        </>
      )}
    </section>
  )
}

function EditFieldForm({ field, onClose, onSaved }: { field: LibraryField; onClose: () => void; onSaved: () => Promise<void> }) {
  const { manager } = useAuth()
  const api = libraryApi(manager.api)
  const [label, setLabel] = useState(field.label)
  const [type, setType] = useState<LibraryFieldType | ''>(field.fieldType)
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const locked = field.isSystem

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setFields({})
    if (!label.trim()) { setFields({ label: 'Name is required.' }); return }
    setBusy(true)
    try {
      await api.updateField(field.id, label.trim(), type || field.fieldType)
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
    <form onSubmit={submit} noValidate aria-label="Edit field" className="max-w-xl space-y-3 rounded-lg border bg-card p-3">
      <div className="grid gap-3 sm:grid-cols-2">
        <TextField label="Field name" value={label} onChange={setLabel} maxLength={100} error={fields.label} />
        <SelectField label="Field type" value={type} onChange={setType} options={TYPE_OPTIONS} disabled={locked} error={fields.fieldType} />
      </div>
      {locked && <p className="text-xs text-muted-foreground">The type is fixed because the product uses this field.</p>}
      <FormError error={error} />
      <div className="flex gap-2">
        <button type="submit" disabled={busy} className={buttonClassName('default', 'sm')}>Save field</button>
        <button type="button" className={buttonClassName('outline', 'sm')} onClick={onClose}>Cancel</button>
      </div>
    </form>
  )
}
