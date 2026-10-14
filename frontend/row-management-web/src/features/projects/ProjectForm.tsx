import { useState, type FormEvent } from 'react'
import { OVERSIGHT_TYPES, type OversightType, type Project, type ProjectInput } from '@/api/projects'
import { buttonClassName } from '@/components/ui/button'
import { fieldErrorsOf, FormError, messageOf, numOrNull, orNull, SelectField, TextField } from './form-kit'

const OVERSIGHT_OPTIONS = OVERSIGHT_TYPES.map((o) => ({ value: o, label: o }))

interface State {
  number: string
  name: string
  description: string
  region: string
  district: string
  oversightType: OversightType | ''
  fundingSource: string
  fundingAmount: string
  designEstimate: string
  lettingDate: string
  planTurnInDate: string
  constructionStartDate: string
  constructionEndDate: string
}

const blank: State = {
  number: '', name: '', description: '', region: '', district: '', oversightType: 'Federal', fundingSource: '',
  fundingAmount: '', designEstimate: '', lettingDate: '', planTurnInDate: '', constructionStartDate: '', constructionEndDate: '',
}

const fromProject = (p: Project): State => ({
  number: p.number, name: p.name, description: p.description ?? '', region: p.region ?? '', district: p.district ?? '',
  oversightType: p.oversightType, fundingSource: p.fundingSource ?? '', fundingAmount: p.fundingAmount?.toString() ?? '',
  designEstimate: p.designEstimate?.toString() ?? '', lettingDate: p.lettingDate ?? '', planTurnInDate: p.planTurnInDate ?? '',
  constructionStartDate: p.constructionStartDate ?? '', constructionEndDate: p.constructionEndDate ?? '',
})

/** Create (no `project`) or edit one project's record: number, location, funding and dates (ROW-001, 003, 004). */
export function ProjectForm({
  project, onSubmit, onCancel, submitLabel,
}: {
  project?: Project
  onSubmit: (input: ProjectInput) => Promise<void>
  onCancel: () => void
  submitLabel: string
}) {
  const [s, setS] = useState<State>(project ? fromProject(project) : blank)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const set = <K extends keyof State>(k: K) => (v: State[K]) => setS((prev) => ({ ...prev, [k]: v }))

  async function submit(e: FormEvent) {
    e.preventDefault()
    const local: Record<string, string> = {}
    if (!s.name.trim()) local.name = 'Name is required.'
    if (!s.oversightType) local.oversightType = 'Funding oversight is required.'
    setErrors(local)
    setFormError(null)
    if (Object.keys(local).length) return

    setBusy(true)
    try {
      await onSubmit({
        number: orNull(s.number), name: s.name.trim(), description: orNull(s.description), region: orNull(s.region),
        district: orNull(s.district), oversightType: s.oversightType as OversightType, fundingSource: orNull(s.fundingSource),
        fundingAmount: numOrNull(s.fundingAmount), designEstimate: numOrNull(s.designEstimate),
        lettingDate: orNull(s.lettingDate), planTurnInDate: orNull(s.planTurnInDate),
        constructionStartDate: orNull(s.constructionStartDate), constructionEndDate: orNull(s.constructionEndDate),
      })
    } catch (err) {
      const fields = fieldErrorsOf(err)
      setErrors(fields)
      setFormError(Object.keys(fields).length ? 'Please correct the highlighted fields.' : messageOf(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} noValidate aria-label={project ? 'Edit project' : 'New project'} className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <TextField label="Project number" value={s.number} onChange={set('number')} error={errors.number} maxLength={50}
          hint={project ? 'Change only if the agency number changed.' : 'Leave blank to generate the next number.'} />
        <TextField label="Name" required value={s.name} onChange={set('name')} error={errors.name} maxLength={200} />
        <SelectField label="Funding oversight" required value={s.oversightType} onChange={set('oversightType')}
          options={OVERSIGHT_OPTIONS} error={errors.oversightType} />
        <TextField label="Region" value={s.region} onChange={set('region')} error={errors.region} maxLength={100} />
        <TextField label="District" value={s.district} onChange={set('district')} error={errors.district} maxLength={100} />
        <TextField label="Funding source" value={s.fundingSource} onChange={set('fundingSource')} error={errors.fundingSource} maxLength={200} />
        <TextField label="Funding amount ($)" type="number" inputMode="decimal" value={s.fundingAmount} onChange={set('fundingAmount')} error={errors.fundingAmount} />
        <TextField label="Design estimate ($)" type="number" inputMode="decimal" value={s.designEstimate} onChange={set('designEstimate')} error={errors.designEstimate} />
        <TextField label="Letting date" type="date" value={s.lettingDate} onChange={set('lettingDate')} error={errors.lettingDate} />
        <TextField label="Plan turn-in date" type="date" value={s.planTurnInDate} onChange={set('planTurnInDate')} error={errors.planTurnInDate} />
        <TextField label="Construction start" type="date" value={s.constructionStartDate} onChange={set('constructionStartDate')} error={errors.constructionStartDate} />
        <TextField label="Construction end" type="date" value={s.constructionEndDate} onChange={set('constructionEndDate')} error={errors.constructionEndDate} />
      </div>
      <TextField label="Description" value={s.description} onChange={set('description')} error={errors.description} maxLength={2000} />
      <FormError error={formError} />
      <div className="flex gap-2">
        <button type="submit" disabled={busy} className={buttonClassName('default')}>{busy ? 'Saving…' : submitLabel}</button>
        <button type="button" disabled={busy} onClick={onCancel} className={buttonClassName('outline')}>Cancel</button>
      </div>
    </form>
  )
}
