import { useState, type FormEvent, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Pencil, Trash2 } from 'lucide-react'
import { appraisalsApi, type Appraisal, type AppraisalAmount } from '@/api/parcel-tabs'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { CheckboxField, DataTable, dateText, fieldErrorsOf, FormError, messageOf, numOrNull, orNull, SelectField, TextField } from '../projects/form-kit'
import { AttachmentsPanel } from '../documents/AttachmentsPanel'
import { optionsWith, useAgentNames, useLibraryValues } from '../library/library-kit'

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <fieldset aria-label={title} className="mb-4 rounded-lg border bg-card p-4">
      <legend className="px-1 text-sm font-semibold">{title}</legend>
      {children}
    </fieldset>
  )
}

const BLANK = {
  type: '', appraiser: '', landPlanner: false, assignedTo: '', requested: '', received: '', report: '', due: '', submittedAtp: '', effective: '',
  reviewAppraiser: '', reviewType: '', appraisalReceived: '', approvedJc: '', reviewCompleted: '', reviewSubmitted: '', recommendation: '', recommendedValue: '',
  jatSubmitted: '', jatApproved: '', fta: false,
}
const BLANK_AMOUNT = { type: '', description: '', amount: '' }
const money = (n: number) => n.toLocaleString('en-US', { style: 'currency', currency: 'USD' })

function fromRecord(a: Appraisal) {
  return {
    type: a.appraisalType ?? '', appraiser: a.appraiser ?? '', landPlanner: a.landPlannerRequired, assignedTo: a.assignedTo ?? '', requested: a.requestedDate ?? '',
    received: a.receivedDate ?? '', report: a.reportDate ?? '', due: a.dueDate ?? '', submittedAtp: a.submittedToAtpDate ?? '', effective: a.effectiveDate ?? '',
    reviewAppraiser: a.reviewAppraiser ?? '', reviewType: a.reviewType ?? '', appraisalReceived: a.appraisalReceivedDate ?? '',
    approvedJc: a.approvedJustCompensation === null ? '' : String(a.approvedJustCompensation), reviewCompleted: a.reviewCompletedDate ?? '',
    reviewSubmitted: a.reviewSubmittedDate ?? '', recommendation: a.reviewRecommendation ?? '',
    recommendedValue: a.reviewRecommendedValue === null ? '' : String(a.reviewRecommendedValue), jatSubmitted: a.submittedToJatDate ?? '',
    jatApproved: a.approvedByJatDate ?? '', fta: a.ftaConcurrenceNeeded,
  }
}

/**
 * Appraisal (Phase 1.5, ADR 0018): many appraisals per parcel. The Appraiser records the appraisal and its amounts; the review section, with the
 * approved just compensation, belongs to the Appraisal Approver (the form shows it, read-only, to everyone else).
 */
export function AppraisalTab({ parcelId, editable }: { parcelId: string; editable: boolean }) {
  const { manager } = useAuth()
  const api = appraisalsApi(manager.api)
  const queryClient = useQueryClient()
  const key = ['parcel-tabs', parcelId, 'appraisals']
  const list = useQuery({ queryKey: key, queryFn: () => api.list(parcelId), enabled: manager.can('parcelappraisal') })
  const types = useLibraryValues('appraisal_type')
  const amountTypes = useLibraryValues('appraisal_amount_type')
  const reviewTypes = useLibraryValues('review_type')
  const recommendations = useLibraryValues('review_recommendation')
  const agents = useAgentNames()
  const [editing, setEditing] = useState<Appraisal | null>(null)
  const [f, setF] = useState(BLANK)
  const [amounts, setAmounts] = useState<AppraisalAmount[]>([])
  const [am, setAm] = useState(BLANK_AMOUNT)
  const [local, setLocal] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const canAdd = editable && manager.can('parcelappraisal', 'create')
  const canEdit = editable && manager.can('parcelappraisal', 'update')
  const canRemove = editable && manager.can('parcelappraisal', 'delete')
  const canReview = manager.hasRole('AppraisalApprover', 'Admin')
  const set = <K extends keyof typeof BLANK>(k: K) => (v: (typeof BLANK)[K]) => setF((x) => ({ ...x, [k]: v }))

  if (!manager.can('parcelappraisal')) return <p className="text-sm text-muted-foreground">Your role cannot see appraisals.</p>
  if (list.isPending) return <LoadingRows rows={3} />
  if (list.isError) return <ErrorState title="The appraisals could not be loaded" description={list.error.message} retry={() => void list.refetch()} />

  function reset() {
    setEditing(null); setF(BLANK); setAmounts([]); setAm(BLANK_AMOUNT); setLocal({}); setError(null); setFields({})
  }

  function begin(a: Appraisal) {
    setEditing(a)
    setF(fromRecord(a))
    setAmounts(a.amounts.map(({ amountType, description, amount }) => ({ amountType, description, amount })))
    setAm(BLANK_AMOUNT); setLocal({}); setError(null); setFields({})
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

  function addAmount() {
    if (!am.type) { setLocal({ ...local, amount: 'Choose the amount type.' }); return }
    const n = Number(am.amount)
    if (!am.amount.trim() || Number.isNaN(n) || n < 0) { setLocal({ ...local, amount: 'The amount must be zero or more.' }); return }
    setAmounts([...amounts, { amountType: am.type, description: orNull(am.description), amount: n }])
    setAm(BLANK_AMOUNT)
    setLocal({ ...local, amount: '' })
  }

  async function submit(ev: FormEvent) {
    ev.preventDefault()
    const errs: Record<string, string> = {}
    if (f.requested && f.received && f.received < f.requested) errs.receivedDate = 'The appraisal cannot be received before it is requested.'
    if (f.jatSubmitted && f.jatApproved && f.jatApproved < f.jatSubmitted) errs.approvedByJatDate = 'The appraisal cannot be approved by the JAT before it is submitted.'
    for (const [k, v] of [['approvedJustCompensation', f.approvedJc], ['reviewRecommendedValue', f.recommendedValue]] as const)
      if (v.trim() && (Number.isNaN(Number(v)) || Number(v) < 0)) errs[k] = 'The amount must be zero or more.'
    setLocal(errs)
    if (Object.keys(errs).length) return
    const body = {
      appraisalType: orNull(f.type), appraiser: orNull(f.appraiser), landPlannerRequired: f.landPlanner, assignedTo: orNull(f.assignedTo),
      requestedDate: orNull(f.requested), receivedDate: orNull(f.received), reportDate: orNull(f.report), dueDate: orNull(f.due),
      submittedToAtpDate: orNull(f.submittedAtp), effectiveDate: orNull(f.effective), amounts,
      reviewAppraiser: orNull(f.reviewAppraiser), reviewType: orNull(f.reviewType), appraisalReceivedDate: orNull(f.appraisalReceived),
      approvedJustCompensation: numOrNull(f.approvedJc), reviewCompletedDate: orNull(f.reviewCompleted), reviewSubmittedDate: orNull(f.reviewSubmitted),
      reviewRecommendation: orNull(f.recommendation), reviewRecommendedValue: numOrNull(f.recommendedValue),
      submittedToJatDate: orNull(f.jatSubmitted), approvedByJatDate: orNull(f.jatApproved), ftaConcurrenceNeeded: f.fta,
    }
    if (await run(() => (editing ? api.update(parcelId, editing.id, body) : api.add(parcelId, body)))) reset()
  }
  const errors = { ...fields, ...local }
  const total = amounts.reduce((s, a) => s + a.amount, 0)
  const reviewOff = !canReview

  return (
    <>
      <fieldset aria-label="Appraisals" className="mb-6 rounded-lg border bg-card p-4">
        <legend className="px-1 text-sm font-semibold">Appraisals</legend>
        {list.data.length === 0 ? (
          <EmptyState title="No appraisals yet" description="Record each appraisal ordered for this parcel." />
        ) : (
          <DataTable caption="Appraisals" headers={['Appraisal ID', 'Type', 'Appraiser', 'Company', 'Received', 'Appraisal total', 'Approved just compensation', ...(canEdit || canRemove ? ['Actions'] : [])]}>
            {list.data.map((a) => (
              <tr key={a.id}>
                <td className="px-3 py-2 font-medium">{a.number}</td>
                <td className="px-3 py-2">{a.appraisalType ?? '—'}</td>
                <td className="px-3 py-2">{a.appraiser ?? '—'}</td>
                <td className="px-3 py-2">{a.appraiserCompany ?? '—'}</td>
                <td className="px-3 py-2">{a.receivedDate ? dateText(a.receivedDate) : '—'}</td>
                <td className="px-3 py-2">{money(a.appraisalTotal)}</td>
                <td className="px-3 py-2">{a.approvedJustCompensation === null ? '—' : money(a.approvedJustCompensation)}</td>
                {(canEdit || canRemove) && (
                  <td className="px-3 py-2">
                    <span className="inline-flex gap-1">
                      {canEdit && <button type="button" aria-label={`Edit appraisal ${a.number}`} className={buttonClassName('ghost', 'sm')} onClick={() => begin(a)}><Pencil size={14} aria-hidden /></button>}
                      {canRemove && (
                        <button type="button" aria-label={`Remove appraisal ${a.number}`} disabled={busy} className={buttonClassName('ghost', 'sm')}
                          onClick={() => void run(() => api.remove(parcelId, a.id)).then((ok) => { if (ok && editing?.id === a.id) reset() })}>
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
      </fieldset>

      {((canAdd && !editing) || (canEdit && editing)) && (
        <form onSubmit={submit} noValidate aria-label={editing ? 'Change appraisal' : 'Add an appraisal'} className="max-w-5xl">
          <h3 className="mb-3 text-sm font-semibold">{editing ? `Change ${editing.number}` : 'Add an appraisal'}</h3>

          <Section title="Appraisal">
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
              <div className="text-sm"><span className="text-xs text-muted-foreground">ID</span><div>{editing?.number ?? 'Assigned when saved'}</div></div>
              <SelectField label="Appraisal type" value={f.type} onChange={set('type')} options={optionsWith(types, f.type)} blankLabel="Select one" error={errors.appraisalType} />
              <SelectField label="Appraiser" value={f.appraiser} onChange={set('appraiser')} options={optionsWith(agents, f.appraiser)} blankLabel="Select one" error={errors.appraiser} />
              <div className="text-sm"><span className="text-xs text-muted-foreground">Appraiser company</span><div>{editing?.appraiserCompany ?? '—'}</div></div>
              <SelectField label="Assigned to" value={f.assignedTo} onChange={set('assignedTo')} options={optionsWith(agents, f.assignedTo)} blankLabel="Select one" error={errors.assignedTo} />
              <CheckboxField label="Land planner required" checked={f.landPlanner} onChange={set('landPlanner')} />
              <TextField label="Appraisal requested date" type="date" value={f.requested} onChange={set('requested')} error={errors.requestedDate} />
              <TextField label="Appraisal received date" type="date" value={f.received} onChange={set('received')} error={errors.receivedDate} />
              <TextField label="Appraisal report date" type="date" value={f.report} onChange={set('report')} error={errors.reportDate} />
              <TextField label="Appraisal due date" type="date" value={f.due} onChange={set('due')} error={errors.dueDate} />
              <TextField label="Submitted to ATP date" type="date" value={f.submittedAtp} onChange={set('submittedAtp')} error={errors.submittedToAtpDate} />
              <TextField label="Effective date" type="date" value={f.effective} onChange={set('effective')} error={errors.effectiveDate} />
            </div>
          </Section>

          <Section title="Appraisal amounts">
            {amounts.length > 0 && (
              <DataTable caption="Appraisal amounts" headers={['Amount type', 'Description', 'Amount', 'Actions']}>
                {amounts.map((a, i) => (
                  <tr key={`${a.amountType}-${i}`}>
                    <td className="px-3 py-2">{a.amountType}</td>
                    <td className="px-3 py-2">{a.description ?? '—'}</td>
                    <td className="px-3 py-2">{money(a.amount)}</td>
                    <td className="px-3 py-2"><button type="button" aria-label={`Remove amount ${a.amountType} ${i + 1}`} className={buttonClassName('ghost', 'sm')} onClick={() => setAmounts(amounts.filter((_, j) => j !== i))}><Trash2 size={14} aria-hidden /></button></td>
                  </tr>
                ))}
              </DataTable>
            )}
            <p className="mt-2 text-sm">Appraisal total: <strong>{money(total)}</strong></p>
            <div className="mt-3 grid gap-3 sm:grid-cols-3">
              <SelectField label="Amount type" value={am.type} onChange={(v) => setAm({ ...am, type: v })} options={optionsWith(amountTypes, am.type)} blankLabel="Select one" error={errors.amount} />
              <TextField label="Amount description" value={am.description} onChange={(v) => setAm({ ...am, description: v })} maxLength={500} />
              <TextField label="Amount" type="number" inputMode="decimal" value={am.amount} onChange={(v) => setAm({ ...am, amount: v })} />
            </div>
            <button type="button" className={`${buttonClassName('outline', 'sm')} mt-2`} onClick={addAmount}>Add amount</button>
          </Section>

          <Section title="Appraisal review">
            {reviewOff && <p className="mb-2 text-xs text-muted-foreground">Only the Appraisal Approver sets the review and the approved just compensation.</p>}
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
              <SelectField label="Review appraiser" value={f.reviewAppraiser} onChange={set('reviewAppraiser')} options={optionsWith(agents, f.reviewAppraiser)} blankLabel="Select one" disabled={reviewOff} error={errors.reviewAppraiser} />
              <SelectField label="Review type" value={f.reviewType} onChange={set('reviewType')} options={optionsWith(reviewTypes, f.reviewType)} blankLabel="Select one" disabled={reviewOff} error={errors.reviewType} />
              <TextField label="Appraisal received date (review)" type="date" value={f.appraisalReceived} onChange={set('appraisalReceived')} disabled={reviewOff} error={errors.appraisalReceivedDate} />
              <TextField label="Approved just compensation" type="number" inputMode="decimal" value={f.approvedJc} onChange={set('approvedJc')} disabled={reviewOff} error={errors.approvedJustCompensation} />
              <TextField label="Review completed date" type="date" value={f.reviewCompleted} onChange={set('reviewCompleted')} disabled={reviewOff} error={errors.reviewCompletedDate} />
              <TextField label="Review submitted date" type="date" value={f.reviewSubmitted} onChange={set('reviewSubmitted')} disabled={reviewOff} error={errors.reviewSubmittedDate} />
              <SelectField label="Review recommendation" value={f.recommendation} onChange={set('recommendation')} options={optionsWith(recommendations, f.recommendation)} blankLabel="Select one" disabled={reviewOff} error={errors.reviewRecommendation} />
              <TextField label="Review recommended value" type="number" inputMode="decimal" value={f.recommendedValue} onChange={set('recommendedValue')} disabled={reviewOff} error={errors.reviewRecommendedValue} />
            </div>
          </Section>

          <Section title="Concurrence">
            <div className="grid gap-3 sm:grid-cols-3">
              <TextField label="Submitted to JAT date" type="date" value={f.jatSubmitted} onChange={set('jatSubmitted')} error={errors.submittedToJatDate} />
              <TextField label="Approved by JAT date" type="date" value={f.jatApproved} onChange={set('jatApproved')} error={errors.approvedByJatDate} />
              <CheckboxField label="FTA concurrence needed" checked={f.fta} onChange={set('fta')} />
            </div>
          </Section>

          <FormError error={error} />
          <div className="flex gap-2">
            <button type="submit" disabled={busy} className={buttonClassName('default')}>{editing ? 'Save changes' : 'Add appraisal'}</button>
            {editing && <button type="button" className={buttonClassName('outline')} onClick={reset}>Cancel</button>}
          </div>
        </form>
      )}

      {editing && <div className="mt-6"><AttachmentsPanel entityType="ParcelAppraisal" entityId={editing.id} page="parcelappraisal" title={`Attachments for ${editing.number}`} /></div>}
    </>
  )
}
