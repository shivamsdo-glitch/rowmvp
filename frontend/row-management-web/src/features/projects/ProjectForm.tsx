import { Explained } from '@/components/ui/abbr'
import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  CATALOG_SOURCES, OVERSIGHT_TYPES, projectsApi, type CatalogSource, type OversightType, type Project, type ProjectInput,
} from '@/api/projects'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { optionsWith, useLibraryValues } from '../library/library-kit'
import { fieldErrorsOf, FormError, messageOf, numOrNull, orNull, SelectField, TextField } from './form-kit'

const OVERSIGHT_OPTIONS = OVERSIGHT_TYPES.map((o) => ({ value: o, label: o }))
const SOURCE_OPTIONS = CATALOG_SOURCES.map((s) => ({ value: s, label: s === 'Planned' ? 'Planned project' : 'Build project' }))

interface State {
  number: string
  name: string
  phase: string
  location: string
  projectManagerUserId: string
  description: string
  documentFolderStructure: string
  documentProperties: string
  region: string
  district: string
  oversightType: OversightType | ''
  fundingSource: string
  fundingAmount: string
  designEstimate: string
  constructionProjectManager: string
  lettingDate: string
  rightOfWayClearDate: string
  planTurnInDate: string
  constructionStartDate: string
  constructionEndDate: string
  linkedSource: CatalogSource | ''
  linkedProjectNumber: string
}

const blank: State = {
  number: '', name: '', phase: 'Phase 1', location: '', projectManagerUserId: '', description: '', documentFolderStructure: '', documentProperties: '',
  region: '', district: '', oversightType: 'Federal', fundingSource: '', fundingAmount: '', designEstimate: '', constructionProjectManager: '',
  lettingDate: '', rightOfWayClearDate: '', planTurnInDate: '', constructionStartDate: '', constructionEndDate: '', linkedSource: '', linkedProjectNumber: '',
}

const fromProject = (p: Project): State => ({
  number: p.number, name: p.name, phase: p.phase ?? '', location: p.location ?? '', projectManagerUserId: p.projectManagerUserId ?? '',
  description: p.description ?? '', documentFolderStructure: p.documentFolderStructure ?? '', documentProperties: p.documentProperties ?? '',
  region: p.region ?? '', district: p.district ?? '', oversightType: p.oversightType, fundingSource: p.fundingSource ?? '',
  fundingAmount: p.fundingAmount?.toString() ?? '', designEstimate: p.designEstimate?.toString() ?? '',
  constructionProjectManager: p.constructionProjectManager ?? '', lettingDate: p.lettingDate ?? '', rightOfWayClearDate: p.rightOfWayClearDate ?? '',
  planTurnInDate: p.planTurnInDate ?? '', constructionStartDate: p.constructionStartDate ?? '', constructionEndDate: p.constructionEndDate ?? '',
  linkedSource: p.linkedSource ?? '', linkedProjectNumber: p.linkedProjectNumber ?? '',
})

/**
 * Create (no `project`) or edit one project's record: the ROW project details (phase, location, manager, documents), funding,
 * construction dates and the link to a Planned or Build project (ROW-001, 003, 004; Phase 1.5 slice 4).
 */
export function ProjectForm({
  project, onSubmit, onCancel, submitLabel,
}: {
  project?: Project
  onSubmit: (input: ProjectInput) => Promise<void>
  onCancel: () => void
  submitLabel: string
}) {
  const { manager } = useAuth()
  const api = projectsApi(manager.api)
  const [s, setS] = useState<State>(project ? fromProject(project) : blank)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [linkSearch, setLinkSearch] = useState('')
  const set = <K extends keyof State>(k: K) => (v: State[K]) => setS((prev) => ({ ...prev, [k]: v }))

  const managers = useQuery({ queryKey: ['project-managers'], queryFn: () => api.managers(), retry: false })
  const catalog = useQuery({
    queryKey: ['project-catalog', s.linkedSource, linkSearch],
    queryFn: () => api.catalog(s.linkedSource as CatalogSource, linkSearch),
    enabled: !!s.linkedSource, retry: false,
  })
  const folderValues = useLibraryValues('document_folder_structure')
  const propertyValues = useLibraryValues('document_properties')

  const managerOptions = [
    ...(managers.data ?? []).map((m) => ({ value: m.id, label: m.name })),
    ...(project?.projectManagerUserId && !(managers.data ?? []).some((m) => m.id === project.projectManagerUserId)
      ? [{ value: project.projectManagerUserId, label: project.projectManagerName ?? 'Former user' }] : []),
  ]
  const catalogOptions = [
    ...(catalog.data ?? []).map((c) => ({ value: c.number, label: `${c.number} · ${c.name}` })),
    ...(s.linkedProjectNumber && !(catalog.data ?? []).some((c) => c.number === s.linkedProjectNumber)
      ? [{ value: s.linkedProjectNumber, label: `${s.linkedProjectNumber}${project?.linkedProjectName ? ` · ${project.linkedProjectName}` : ''}` }] : []),
  ]

  async function submit(e: FormEvent) {
    e.preventDefault()
    const local: Record<string, string> = {}
    if (!s.name.trim()) local.name = 'Name is required.'
    // Required for a new project; a project made before the field existed can be edited without one.
    if (!project && !s.phase.trim()) local.phase = 'Phase is required.'
    if (!s.oversightType) local.oversightType = 'Funding oversight is required.'
    if (s.linkedSource && !s.linkedProjectNumber) local.linkedProjectNumber = 'Choose the project to link to.'
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
        phase: orNull(s.phase), location: orNull(s.location), projectManagerUserId: s.projectManagerUserId || null,
        documentFolderStructure: orNull(s.documentFolderStructure), documentProperties: orNull(s.documentProperties),
        constructionProjectManager: orNull(s.constructionProjectManager), rightOfWayClearDate: orNull(s.rightOfWayClearDate),
        linkedSource: s.linkedSource || null, linkedProjectNumber: s.linkedSource ? s.linkedProjectNumber || null : null,
      })
    } catch (err) {
      const fields = fieldErrorsOf(err)
      setErrors(Object.fromEntries(Object.entries(fields).map(([k, v]) => [k.charAt(0).toLowerCase() + k.slice(1), v])))
      setFormError(Object.keys(fields).length ? 'Please correct the highlighted fields.' : messageOf(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} noValidate aria-label={project ? 'Edit project' : 'New project'} className="space-y-6">
      <fieldset className="space-y-3">
        <legend className="text-sm font-semibold"><Explained text="ROW project details" /></legend>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <TextField label="Project number" value={s.number} onChange={set('number')} error={errors.number} maxLength={50}
            hint={project ? 'Change only if the agency number changed.' : 'Leave blank to generate the next number.'} />
          <TextField label="Name" required value={s.name} onChange={set('name')} error={errors.name} maxLength={200} />
          <TextField label="Phase" required={!project} value={s.phase} onChange={set('phase')} error={errors.phase} maxLength={100} />
          <TextField label="Location" value={s.location} onChange={set('location')} error={errors.location} maxLength={500} />
          <SelectField label="Project manager" value={s.projectManagerUserId} onChange={set('projectManagerUserId')}
            options={managerOptions} blankLabel="Select one" error={errors.projectManagerUserId} />
          <SelectField label="Funding oversight" required value={s.oversightType} onChange={set('oversightType')}
            options={OVERSIGHT_OPTIONS} error={errors.oversightType} />
          <TextField label="Region" value={s.region} onChange={set('region')} error={errors.region} maxLength={100} />
          <TextField label="District" value={s.district} onChange={set('district')} error={errors.district} maxLength={100} />
          <SelectField label="Document folder structure" value={s.documentFolderStructure} onChange={set('documentFolderStructure')}
            options={optionsWith(folderValues, s.documentFolderStructure)} blankLabel="Select one" error={errors.documentFolderStructure} />
          <SelectField label="Document properties" value={s.documentProperties} onChange={set('documentProperties')}
            options={optionsWith(propertyValues, s.documentProperties)} blankLabel="Select one" error={errors.documentProperties} />
        </div>
        <TextField label="Project description" value={s.description} onChange={set('description')} error={errors.description} maxLength={2000} />
      </fieldset>

      <fieldset className="space-y-3">
        <legend className="text-sm font-semibold">Link to a Planned or Build project</legend>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <SelectField label="Link to" value={s.linkedSource} onChange={(v) => { setS((prev) => ({ ...prev, linkedSource: v, linkedProjectNumber: '' })); setLinkSearch('') }}
            options={SOURCE_OPTIONS} blankLabel="Not linked" error={errors.linkedSource} />
          {s.linkedSource && (
            <>
              <TextField label="Search linked projects" value={linkSearch} onChange={setLinkSearch} placeholder="Number or name" />
              <SelectField label="Linked project" value={s.linkedProjectNumber} onChange={set('linkedProjectNumber')}
                options={catalogOptions} blankLabel="Select one" error={errors.linkedProjectNumber} />
            </>
          )}
        </div>
      </fieldset>

      <fieldset className="space-y-3">
        <legend className="text-sm font-semibold">Funding information</legend>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <TextField label="Funding source" value={s.fundingSource} onChange={set('fundingSource')} error={errors.fundingSource} maxLength={200} />
          <TextField label="Funding amount ($)" type="number" inputMode="decimal" value={s.fundingAmount} onChange={set('fundingAmount')} error={errors.fundingAmount} />
          <TextField label="Design estimate ($)" type="number" inputMode="decimal" value={s.designEstimate} onChange={set('designEstimate')} error={errors.designEstimate} />
        </div>
        <p className="text-xs text-muted-foreground">Board approved funds are the annual budgets added up, the cost estimate is the latest estimate and the actual cost is paid so far; they are kept on the Budget and Cost estimates tabs.</p>
      </fieldset>

      <fieldset className="space-y-3">
        <legend className="text-sm font-semibold">Construction details</legend>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <TextField label="Construction project manager" value={s.constructionProjectManager} onChange={set('constructionProjectManager')} error={errors.constructionProjectManager} maxLength={200} />
          <TextField label="Letting date" type="date" value={s.lettingDate} onChange={set('lettingDate')} error={errors.lettingDate} />
          <TextField label="Right of way clear date" type="date" value={s.rightOfWayClearDate} onChange={set('rightOfWayClearDate')} error={errors.rightOfWayClearDate} />
          <TextField label="Plan turn-in date" type="date" value={s.planTurnInDate} onChange={set('planTurnInDate')} error={errors.planTurnInDate} />
          <TextField label="Construction start" type="date" value={s.constructionStartDate} onChange={set('constructionStartDate')} error={errors.constructionStartDate} hint="Projected" />
          <TextField label="Construction end" type="date" value={s.constructionEndDate} onChange={set('constructionEndDate')} error={errors.constructionEndDate} hint="Projected" />
        </div>
      </fieldset>

      <FormError error={formError} />
      <div className="flex gap-2">
        <button type="submit" disabled={busy} className={buttonClassName('default')}>{busy ? 'Saving…' : submitLabel}</button>
        <button type="button" disabled={busy} onClick={onCancel} className={buttonClassName('outline')}>Cancel</button>
      </div>
    </form>
  )
}
