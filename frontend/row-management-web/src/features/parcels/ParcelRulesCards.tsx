import { useState, type FormEvent, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  ACQUISITION_LABEL, CUSTOM_FIELD_TYPES, parcelsApi, type CustomField, type CustomFieldType, type DeadlineRule, type ParcelStatus,
} from '@/api/parcels'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { ErrorState, LoadingRows } from '@/components/ui/states'
import { CheckboxField, DataTable, fieldErrorsOf, FormError, messageOf, Pill, SelectField, TextField } from '../projects/form-kit'

function Card({ title, intro, children }: { title: string; intro: ReactNode; children: ReactNode }) {
  return (
    <section aria-label={title} className="mt-6 max-w-4xl rounded-lg border bg-card p-4">
      <h2 className="text-sm font-semibold">{title}</h2>
      <p className="mb-3 text-xs text-muted-foreground">{intro}</p>
      {children}
    </section>
  )
}

/** Shared save plumbing: errors by field, a general error, busy, and a refresh of the card's list. */
function useCardForm(queryKey: string) {
  const queryClient = useQueryClient()
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [saved, setSaved] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  return {
    errors, formError, saved, busy,
    reset: () => { setErrors({}); setFormError(null); setSaved(null) },
    setLocal: (e: Record<string, string>) => { setErrors(e); setFormError(null); setSaved(null) },
    async run(action: () => Promise<unknown>, message: string): Promise<boolean> {
      setErrors({}); setFormError(null); setSaved(null); setBusy(true)
      try {
        await action()
        await queryClient.invalidateQueries({ queryKey: [queryKey] })
        setSaved(message)
        return true
      } catch (e) {
        const fe = fieldErrorsOf(e)
        setErrors(fe)
        setFormError(Object.keys(fe).length ? 'Please correct the highlighted fields.' : messageOf(e))
        return false
      } finally {
        setBusy(false)
      }
    },
  }
}

// ── Parcel statuses (ROW-005) ───────────────────────────────────────────

export function ParcelStatusesCard() {
  const { manager } = useAuth()
  const api = parcelsApi(manager.api)
  const canEdit = manager.can('administration', 'update')
  const list = useQuery({ queryKey: ['parcel-statuses'], queryFn: () => api.statuses() })
  const form = useCardForm('parcel-statuses')
  const [editing, setEditing] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [color, setColor] = useState('#64748b')
  const [sort, setSort] = useState('')
  const [active, setActive] = useState(true)
  const [initial, setInitial] = useState(false)

  const begin = (s: ParcelStatus | null) => {
    form.reset()
    setEditing(s?.id ?? 'new')
    setName(s?.name ?? ''); setColor(s?.color ?? '#64748b'); setSort(String(s?.sortOrder ?? (list.data?.length ?? 0) + 1))
    setActive(s?.isActive ?? true); setInitial(s?.isInitial ?? false)
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!name.trim()) { form.setLocal({ name: 'Status name is required.' }); return }
    const ok = await form.run(
      () => api.saveStatus(editing === 'new' ? null : editing, { name: name.trim(), color, sortOrder: Number(sort) || 0, isActive: active, isInitial: initial }),
      'Status saved.',
    )
    if (ok) setEditing(null)
  }

  return (
    <Card title="Parcel statuses" intro="The agency's own list of parcel statuses. The board is coloured from it; exactly one active status is where new parcels start.">
      {list.isPending ? <LoadingRows rows={3} /> : list.isError ? <ErrorState title="Statuses could not be loaded" retry={() => void list.refetch()} /> : (
        <DataTable caption="Parcel statuses" headers={canEdit ? ['Order', 'Status', 'Colour', 'State', 'Actions'] : ['Order', 'Status', 'Colour', 'State']}>
          {list.data.map((s) => (
            <tr key={s.id}>
              <td className="px-3 py-2">{s.sortOrder}</td>
              <td className="px-3 py-2 font-medium">{s.name}</td>
              <td className="px-3 py-2"><span aria-hidden className="mr-1.5 inline-block h-3 w-3 rounded-full border align-middle" style={{ backgroundColor: s.color }} />{s.color}</td>
              <td className="px-3 py-2">{s.isInitial && <Pill tone="ok">Initial</Pill>} {!s.isActive && <Pill tone="neutral">Inactive</Pill>}</td>
              {canEdit && <td className="px-3 py-2"><button type="button" aria-label={`Edit status ${s.name}`} className={buttonClassName('outline', 'sm')} onClick={() => begin(s)}>Edit</button></td>}
            </tr>
          ))}
        </DataTable>
      )}
      {canEdit && editing === null && <button type="button" className={`mt-3 ${buttonClassName('outline')}`} onClick={() => begin(null)}>Add status</button>}
      {canEdit && editing !== null && (
        <form onSubmit={submit} noValidate aria-label={editing === 'new' ? 'Add a status' : 'Edit a status'} className="mt-3 space-y-3">
          <div className="grid gap-3 sm:grid-cols-3">
            <TextField label="Status name" required value={name} onChange={setName} error={form.errors.name} maxLength={60} />
            <TextField label="Status colour" value={color} onChange={setColor} error={form.errors.color} hint="A #rrggbb colour." maxLength={7} />
            <TextField label="Status order" type="number" value={sort} onChange={setSort} error={form.errors.sortOrder} />
          </div>
          <div className="flex gap-6">
            <CheckboxField label="Active" checked={active} onChange={setActive} />
            <CheckboxField label="Initial status for new parcels" checked={initial} onChange={setInitial} />
          </div>
          <FormError error={form.formError} />
          <div className="flex gap-2">
            <button type="submit" disabled={form.busy} className={buttonClassName('default')}>Save status</button>
            <button type="button" className={buttonClassName('outline')} onClick={() => setEditing(null)}>Cancel</button>
          </div>
        </form>
      )}
      {form.saved && <p role="status" className="mt-2 text-sm">{form.saved}</p>}
    </Card>
  )
}

// ── Deadline rules (ROW-073) ────────────────────────────────────────────

export function DeadlineRulesCard() {
  const { manager } = useAuth()
  const api = parcelsApi(manager.api)
  const canEdit = manager.can('administration', 'update')
  const list = useQuery({ queryKey: ['deadline-rules'], queryFn: () => api.deadlineRules() })
  const form = useCardForm('deadline-rules')
  const [editing, setEditing] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [trigger, setTrigger] = useState('')
  const [target, setTarget] = useState('')
  const [days, setDays] = useState('')
  const [active, setActive] = useState(true)

  const begin = (r: DeadlineRule | null) => {
    form.reset()
    setEditing(r?.id ?? 'new')
    setName(r?.name ?? ''); setTrigger(r?.triggerMilestone ?? ''); setTarget(r?.targetMilestone ?? ''); setDays(r ? String(r.days) : ''); setActive(r?.isActive ?? true)
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    const local: Record<string, string> = {}
    if (!name.trim()) local.name = 'Rule name is required.'
    if (!trigger.trim()) local.triggerMilestone = 'The starting milestone is required.'
    if (!target.trim()) local.targetMilestone = 'The due milestone is required.'
    if (days.trim() === '' || !Number.isInteger(Number(days)) || Number(days) < 0) local.days = 'Days must be a whole number, zero or more.'
    if (Object.keys(local).length) { form.setLocal(local); return }
    const ok = await form.run(
      () => api.saveDeadlineRule(editing === 'new' ? null : editing, {
        name: name.trim(), triggerMilestone: trigger.trim(), targetMilestone: target.trim(), days: Number(days), isActive: active,
      }),
      'Rule saved.',
    )
    if (ok) setEditing(null)
  }

  return (
    <Card
      title="Statutory deadlines"
      intro="Each rule says a milestone is due a number of days after another is completed. Periods marked placeholder were seeded and have not been confirmed by the agency; saving a rule confirms it."
    >
      {list.isPending ? <LoadingRows rows={2} /> : list.isError ? <ErrorState title="Deadline rules could not be loaded" retry={() => void list.refetch()} /> : (
        <DataTable caption="Deadline rules" headers={canEdit ? ['Rule', 'Starts when completed', 'Due milestone', 'Days', 'State', 'Actions'] : ['Rule', 'Starts when completed', 'Due milestone', 'Days', 'State']}>
          {list.data.map((r) => (
            <tr key={r.id}>
              <td className="px-3 py-2 font-medium">{r.name}</td>
              <td className="px-3 py-2">{r.triggerMilestone}</td>
              <td className="px-3 py-2">{r.targetMilestone}</td>
              <td className="px-3 py-2">{r.days}</td>
              <td className="px-3 py-2">{r.isPlaceholder && <Pill tone="warn">Placeholder</Pill>} {!r.isActive && <Pill tone="neutral">Inactive</Pill>}</td>
              {canEdit && <td className="px-3 py-2"><button type="button" aria-label={`Edit rule ${r.name}`} className={buttonClassName('outline', 'sm')} onClick={() => begin(r)}>Edit</button></td>}
            </tr>
          ))}
        </DataTable>
      )}
      {canEdit && editing === null && <button type="button" className={`mt-3 ${buttonClassName('outline')}`} onClick={() => begin(null)}>Add deadline rule</button>}
      {canEdit && editing !== null && (
        <form onSubmit={submit} noValidate aria-label={editing === 'new' ? 'Add a deadline rule' : 'Edit a deadline rule'} className="mt-3 space-y-3">
          <div className="grid gap-3 sm:grid-cols-2">
            <TextField label="Rule name" required value={name} onChange={setName} error={form.errors.name} maxLength={150} />
            <TextField label="Days allowed" required type="number" value={days} onChange={setDays} error={form.errors.days} />
            <TextField label="Starts when this milestone is completed" required value={trigger} onChange={setTrigger} error={form.errors.triggerMilestone} maxLength={100} />
            <TextField label="This milestone is then due" required value={target} onChange={setTarget} error={form.errors.targetMilestone} maxLength={100} />
          </div>
          <CheckboxField label="Rule is active" checked={active} onChange={setActive} />
          <FormError error={form.formError} />
          <div className="flex gap-2">
            <button type="submit" disabled={form.busy} className={buttonClassName('default')}>Save rule</button>
            <button type="button" className={buttonClassName('outline')} onClick={() => setEditing(null)}>Cancel</button>
          </div>
        </form>
      )}
      {form.saved && <p role="status" className="mt-2 text-sm">{form.saved}</p>}
    </Card>
  )
}

// ── Custom fields (ROW-011) ─────────────────────────────────────────────

const FIELD_TYPE_OPTIONS = CUSTOM_FIELD_TYPES.map((t) => ({ value: t, label: t }))

export function CustomFieldsCard() {
  const { manager } = useAuth()
  const api = parcelsApi(manager.api)
  const canEdit = manager.can('administration', 'update')
  const list = useQuery({ queryKey: ['custom-fields'], queryFn: () => api.customFields() })
  const form = useCardForm('custom-fields')
  const [editing, setEditing] = useState<string | null>(null)
  const [key, setKey] = useState('')
  const [label, setLabel] = useState('')
  const [type, setType] = useState<CustomFieldType | ''>('Text')
  const [options, setOptions] = useState('')
  const [required, setRequired] = useState(false)
  const [active, setActive] = useState(true)
  const [sort, setSort] = useState('0')

  const begin = (f: CustomField | null) => {
    form.reset()
    setEditing(f?.id ?? 'new')
    setKey(f?.key ?? ''); setLabel(f?.label ?? ''); setType(f?.fieldType ?? 'Text'); setOptions(f?.options.join(', ') ?? '')
    setRequired(f?.isRequired ?? false); setActive(f?.isActive ?? true); setSort(String(f?.sortOrder ?? (list.data?.length ?? 0) + 1))
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    const local: Record<string, string> = {}
    if (!key.trim()) local.key = 'A key is required.'
    if (!label.trim()) local.label = 'A label is required.'
    if (type === 'List' && !options.trim()) local.options = 'A list field needs at least one option.'
    if (Object.keys(local).length) { form.setLocal(local); return }
    const ok = await form.run(
      () => api.saveCustomField(editing === 'new' ? null : editing, {
        key: key.trim(), label: label.trim(), fieldType: (type || 'Text') as CustomFieldType,
        options: type === 'List' ? options.split(',').map((o) => o.trim()).filter(Boolean) : [],
        isRequired: required, isActive: active, sortOrder: Number(sort) || 0,
      }),
      'Field saved.',
    )
    if (ok) setEditing(null)
  }

  return (
    <Card title="Parcel custom fields" intro="Extra details your agency tracks on every parcel. A field's key and type cannot change once created; turning a field off keeps the values already entered.">
      {list.isPending ? <LoadingRows rows={2} /> : list.isError ? <ErrorState title="Custom fields could not be loaded" retry={() => void list.refetch()} /> : list.data.length === 0 ? (
        <p className="text-sm text-muted-foreground">No custom fields yet.</p>
      ) : (
        <DataTable caption="Custom fields" headers={canEdit ? ['Label', 'Key', 'Type', 'State', 'Actions'] : ['Label', 'Key', 'Type', 'State']}>
          {list.data.map((f) => (
            <tr key={f.id}>
              <td className="px-3 py-2 font-medium">{f.label}</td>
              <td className="px-3 py-2">{f.key}</td>
              <td className="px-3 py-2">{f.fieldType}{f.fieldType === 'List' && ` (${f.options.join(', ')})`}</td>
              <td className="px-3 py-2">{f.isRequired && <Pill tone="neutral">Required</Pill>} {!f.isActive && <Pill tone="neutral">Inactive</Pill>}</td>
              {canEdit && <td className="px-3 py-2"><button type="button" aria-label={`Edit field ${f.label}`} className={buttonClassName('outline', 'sm')} onClick={() => begin(f)}>Edit</button></td>}
            </tr>
          ))}
        </DataTable>
      )}
      {canEdit && editing === null && <button type="button" className={`mt-3 ${buttonClassName('outline')}`} onClick={() => begin(null)}>Add custom field</button>}
      {canEdit && editing !== null && (
        <form onSubmit={submit} noValidate aria-label={editing === 'new' ? 'Add a custom field' : 'Edit a custom field'} className="mt-3 space-y-3">
          <div className="grid gap-3 sm:grid-cols-3">
            <TextField label="Field label" required value={label} onChange={setLabel} error={form.errors.label} maxLength={100} />
            <TextField label="Field key" required value={key} onChange={setKey} error={form.errors.key} disabled={editing !== 'new'} maxLength={40}
              hint={editing === 'new' ? 'Lower-case letters, digits and _.' : undefined} />
            <SelectField label="Field type" value={type} onChange={setType} options={FIELD_TYPE_OPTIONS} disabled={editing !== 'new'} error={form.errors.fieldType} />
          </div>
          {type === 'List' && <TextField label="List options" value={options} onChange={setOptions} error={form.errors.options} hint="Separate options with commas." />}
          <div className="grid gap-3 sm:grid-cols-3">
            <TextField label="Field order" type="number" value={sort} onChange={setSort} error={form.errors.sortOrder} />
            <CheckboxField label="Required" checked={required} onChange={setRequired} />
            <CheckboxField label="Field is active" checked={active} onChange={setActive} />
          </div>
          <FormError error={form.formError} />
          <div className="flex gap-2">
            <button type="submit" disabled={form.busy} className={buttonClassName('default')}>Save field</button>
            <button type="button" className={buttonClassName('outline')} onClick={() => setEditing(null)}>Cancel</button>
          </div>
        </form>
      )}
      {form.saved && <p role="status" className="mt-2 text-sm">{form.saved}</p>}
    </Card>
  )
}

// ── Who may override step order (ROW-072) ───────────────────────────────

export function StepOverrideCard() {
  const { manager } = useAuth()
  const api = parcelsApi(manager.api)
  const canEdit = manager.can('administration', 'update')
  const settings = useQuery({ queryKey: ['step-gate'], queryFn: () => api.stepGate() })
  const form = useCardForm('step-gate')
  const [chosen, setChosen] = useState<string[] | null>(null)

  const current = chosen ?? settings.data?.extraOverrideRoles ?? []
  const toggle = (role: string) => setChosen(current.includes(role) ? current.filter((r) => r !== role) : [...current, role])

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (await form.run(() => api.setStepGate(current), 'Override roles saved.')) setChosen(null)
  }

  return (
    <Card title="Step order overrides" intro="Parcels move through their steps in order. Skipping ahead, going back or repeating a step is an override: it needs a role that may override and a written reason, and is recorded.">
      {settings.isPending ? <LoadingRows rows={2} /> : settings.isError ? <ErrorState title="Override roles could not be loaded" retry={() => void settings.refetch()} /> : (
        <form onSubmit={submit} aria-label="Roles that may override step order" className="space-y-3">
          <p className="text-sm">Always allowed: <strong>{settings.data.alwaysRoles.join(', ')}</strong></p>
          <fieldset className="space-y-1" disabled={!canEdit}>
            <legend className="mb-1 text-xs font-medium">Also allowed</legend>
            {settings.data.assignableRoles.map((role) => (
              <div key={role} className="flex items-center gap-2">
                <input id={`override-${role}`} type="checkbox" className="h-4 w-4" checked={current.includes(role)} onChange={() => toggle(role)} />
                <label htmlFor={`override-${role}`} className="text-sm">{role}</label>
              </div>
            ))}
          </fieldset>
          <p className="text-xs text-muted-foreground">An added role can only override if the agency also lets it update parcels.</p>
          <FormError error={form.formError} />
          {form.saved && <p role="status" className="text-sm">{form.saved}</p>}
          {canEdit && <button type="submit" disabled={form.busy} className={buttonClassName('default')}>Save override roles</button>}
        </form>
      )}
    </Card>
  )
}

// ── Workflows (ROW-071) ─────────────────────────────────────────────────

export function WorkflowsCard() {
  const { manager } = useAuth()
  const list = useQuery({ queryKey: ['workflows'], queryFn: () => parcelsApi(manager.api).workflows() })
  return (
    <Card title="Acquisition workflows" intro="The ordered steps for each kind of acquisition. These are placeholders until each path is built out.">
      {list.isPending ? <LoadingRows rows={2} /> : list.isError ? <ErrorState title="Workflows could not be loaded" retry={() => void list.refetch()} /> : (
        <DataTable caption="Acquisition workflows" headers={['Acquisition type', 'Steps']}>
          {list.data.map((w) => (
            <tr key={w.acquisitionType}>
              <td className="px-3 py-2 font-medium">{ACQUISITION_LABEL[w.acquisitionType]}</td>
              <td className="px-3 py-2">{w.steps.map((s) => s.label).join(' → ')}</td>
            </tr>
          ))}
        </DataTable>
      )}
    </Card>
  )
}
