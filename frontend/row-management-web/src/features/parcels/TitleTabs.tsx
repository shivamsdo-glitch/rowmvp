import { useState, type FormEvent, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Pencil, Trash2 } from 'lucide-react'
import { encumbrancesApi, type ContactLogLinkInput, type Encumbrance } from '@/api/parcel-tabs'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { CheckboxField, DataTable, dateText, fieldErrorsOf, FormError, messageOf, orNull, SelectField, TextField } from '../projects/form-kit'
import { AttachmentsPanel } from '../documents/AttachmentsPanel'
import { optionsWith, useLibraryValues } from '../library/library-kit'
import { LinkedRecordsEditor } from './LinkedRecordsEditor'

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section aria-label={title} className="mb-6 rounded-lg border bg-card p-4">
      <h2 className="mb-3 text-sm font-semibold">{title}</h2>
      {children}
    </section>
  )
}

const BLANK = {
  type: '', title: '', docDate: '', recorded: '', docNumber: '', unrecorded: false, grantor: '', grantee: '', submitted: '', approved: '',
  impact: '', cure: '', cureStatus: '', notes: '',
}

/** Title Encumbrances (Phase 1.5, ADR 0018): the liens, easements and other burdens found in a parcel's title research. */
export function EncumbrancesTab({ parcelId, editable }: { parcelId: string; editable: boolean }) {
  const { manager } = useAuth()
  const api = encumbrancesApi(manager.api)
  const queryClient = useQueryClient()
  const key = ['parcel-tabs', parcelId, 'encumbrances']
  const list = useQuery({ queryKey: key, queryFn: () => api.list(parcelId), enabled: manager.can('titleencumbrances') })
  const types = useLibraryValues('encumbrance_type')
  const cureStatuses = useLibraryValues('cure_status')
  const [editing, setEditing] = useState<Encumbrance | null>(null)
  const [f, setF] = useState(BLANK)
  const [links, setLinks] = useState<ContactLogLinkInput[]>([])
  const [local, setLocal] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const canAdd = editable && manager.can('titleencumbrances', 'create')
  const canEdit = editable && manager.can('titleencumbrances', 'update')
  const canRemove = editable && manager.can('titleencumbrances', 'delete')
  const set = <K extends keyof typeof BLANK>(k: K) => (v: (typeof BLANK)[K]) => setF((x) => ({ ...x, [k]: v }))

  if (!manager.can('titleencumbrances')) return <p className="text-sm text-muted-foreground">Your role cannot see title encumbrances.</p>
  if (list.isPending) return <LoadingRows rows={3} />
  if (list.isError) return <ErrorState title="The title encumbrances could not be loaded" description={list.error.message} retry={() => void list.refetch()} />

  function reset() {
    setEditing(null); setF(BLANK); setLinks([]); setLocal({}); setError(null); setFields({})
  }

  function begin(e: Encumbrance) {
    setEditing(e)
    setF({
      type: e.encumbranceType, title: e.documentTitle, docDate: e.documentDate ?? '', recorded: e.dateRecorded ?? '', docNumber: e.documentNumber ?? '', unrecorded: e.isUnrecorded,
      grantor: e.grantor, grantee: e.grantee, submitted: e.exceptionSubmittedDate ?? '', approved: e.exceptionApprovedDate ?? '', impact: e.impactAnalysis ?? '',
      cure: e.recommendedCure ?? '', cureStatus: e.cureStatus ?? '', notes: e.reviewNotes ?? '',
    })
    setLinks(e.links.map((l) => ({ recordIdentifier: l.recordIdentifier, moduleName: l.moduleName, notes: l.notes })))
    setLocal({}); setError(null); setFields({})
  }

  async function run(action: () => Promise<unknown>): Promise<boolean> {
    setError(null)
    setFields({})
    setBusy(true)
    try {
      await action()
      await queryClient.invalidateQueries({ queryKey: key })
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

  async function submit(ev: FormEvent) {
    ev.preventDefault()
    const errs: Record<string, string> = {}
    if (!f.type) errs.encumbranceType = 'Encumbrance type is required.'
    if (!f.title.trim()) errs.documentTitle = 'Document title is required.'
    if (!f.grantor.trim()) errs.grantor = 'Grantor / lessor is required.'
    if (!f.grantee.trim()) errs.grantee = 'Grantee / lessee is required.'
    if (f.submitted && f.approved && f.approved < f.submitted) errs.exceptionApprovedDate = 'The exception cannot be approved before it is submitted.'
    setLocal(errs)
    if (Object.keys(errs).length) return
    const body = {
      encumbranceType: f.type, documentTitle: f.title.trim(), documentDate: orNull(f.docDate), dateRecorded: f.unrecorded ? null : orNull(f.recorded),
      documentNumber: orNull(f.docNumber), isUnrecorded: f.unrecorded, grantor: f.grantor.trim(), grantee: f.grantee.trim(),
      exceptionSubmittedDate: orNull(f.submitted), exceptionApprovedDate: orNull(f.approved), impactAnalysis: orNull(f.impact), recommendedCure: orNull(f.cure),
      cureStatus: orNull(f.cureStatus), reviewNotes: orNull(f.notes), links,
    }
    if (await run(() => (editing ? api.update(parcelId, editing.id, body) : api.add(parcelId, body)))) reset()
  }
  const errors = { ...fields, ...local }

  return (
    <>
      <Section title="Title encumbrances">
        {list.data.length === 0 ? (
          <EmptyState title="No encumbrances recorded" description="Record each lien, easement or restriction found in the title research." />
        ) : (
          <DataTable caption="Title encumbrances" headers={['Encumbrance ID', 'Type', 'Document title', 'Recorded', 'Grantor / lessor', 'Grantee / lessee', 'Cure status', ...(canEdit || canRemove ? ['Actions'] : [])]}>
            {list.data.map((e) => (
              <tr key={e.id}>
                <td className="px-3 py-2 font-medium">{e.number}</td>
                <td className="px-3 py-2">{e.encumbranceType}</td>
                <td className="px-3 py-2">{e.documentTitle}</td>
                <td className="px-3 py-2">{e.isUnrecorded ? 'Unrecorded' : e.dateRecorded ? dateText(e.dateRecorded) : '—'}</td>
                <td className="whitespace-pre-wrap px-3 py-2">{e.grantor}</td>
                <td className="whitespace-pre-wrap px-3 py-2">{e.grantee}</td>
                <td className="px-3 py-2">{e.cureStatus ?? '—'}</td>
                {(canEdit || canRemove) && (
                  <td className="px-3 py-2">
                    <span className="inline-flex gap-1">
                      {canEdit && <button type="button" aria-label={`Edit encumbrance ${e.number}`} className={buttonClassName('ghost', 'sm')} onClick={() => begin(e)}><Pencil size={14} aria-hidden /></button>}
                      {canRemove && (
                        <button type="button" aria-label={`Remove encumbrance ${e.number}`} disabled={busy} className={buttonClassName('ghost', 'sm')}
                          onClick={() => void run(() => api.remove(parcelId, e.id)).then((ok) => { if (ok && editing?.id === e.id) reset() })}>
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
        <form onSubmit={submit} noValidate aria-label={editing ? 'Change encumbrance' : 'Add an encumbrance'} className="max-w-4xl space-y-3 rounded-lg border bg-card p-4">
          <h3 className="text-sm font-semibold">{editing ? `Change ${editing.number}` : 'Add an encumbrance'}</h3>
          <div className="grid gap-3 sm:grid-cols-2">
            <SelectField label="Encumbrance type" required value={f.type} onChange={set('type')} options={optionsWith(types, f.type)} blankLabel="Select one" error={errors.encumbranceType} />
            <TextField label="Document title" required value={f.title} onChange={set('title')} maxLength={300} error={errors.documentTitle} />
            <TextField label="Document date" type="date" value={f.docDate} onChange={set('docDate')} error={errors.documentDate} />
            <TextField label="Date recorded" type="date" value={f.recorded} onChange={set('recorded')} disabled={f.unrecorded} error={errors.dateRecorded} />
            <TextField label="Document number / vol. and pg." value={f.docNumber} onChange={set('docNumber')} maxLength={500} error={errors.documentNumber} />
            <CheckboxField label="Unrecorded document" checked={f.unrecorded} onChange={(v) => setF((x) => ({ ...x, unrecorded: v, recorded: v ? '' : x.recorded }))} />
            <TextField label="Grantor / lessor" required value={f.grantor} onChange={set('grantor')} maxLength={1000} error={errors.grantor} />
            <TextField label="Grantee / lessee" required value={f.grantee} onChange={set('grantee')} maxLength={1000} error={errors.grantee} />
            <TextField label="Exception to title submitted date" type="date" value={f.submitted} onChange={set('submitted')} error={errors.exceptionSubmittedDate} />
            <TextField label="Exception to title approved date" type="date" value={f.approved} onChange={set('approved')} error={errors.exceptionApprovedDate} />
            <SelectField label="Cure status" value={f.cureStatus} onChange={set('cureStatus')} options={optionsWith(cureStatuses, f.cureStatus)} blankLabel="Select one" error={errors.cureStatus} />
          </div>
          <TextField label="Impact analysis" value={f.impact} onChange={set('impact')} maxLength={4000} error={errors.impactAnalysis} />
          <TextField label="Recommended cure" value={f.cure} onChange={set('cure')} maxLength={4000} error={errors.recommendedCure} />
          <TextField label="Review notes" value={f.notes} onChange={set('notes')} maxLength={4000} error={errors.reviewNotes} />
          <LinkedRecordsEditor links={links} onChange={setLinks} />
          <FormError error={error} />
          <div className="flex gap-2">
            <button type="submit" disabled={busy} className={buttonClassName('default')}>{editing ? 'Save changes' : 'Add encumbrance'}</button>
            {editing && <button type="button" className={buttonClassName('outline')} onClick={reset}>Cancel</button>}
          </div>
        </form>
      )}

      {editing && <AttachmentsPanel entityType="Encumbrance" entityId={editing.id} page="titleencumbrances" title={`Attachments for ${editing.number}`} />}
    </>
  )
}
