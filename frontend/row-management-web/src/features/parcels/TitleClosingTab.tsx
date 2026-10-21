import { useEffect, useState, type FormEvent, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Trash2 } from 'lucide-react'
import { titleClosingApi, type ConveyanceDocument, type TitleClosing, type TitleCommitment } from '@/api/parcel-tabs'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { ErrorState, LoadingRows } from '@/components/ui/states'
import { CheckboxField, DataTable, dateText, fieldErrorsOf, FormError, messageOf, numOrNull, orNull, SelectField, TextField } from '../projects/form-kit'
import { AttachmentsPanel } from '../documents/AttachmentsPanel'
import { useTeamNames } from './team-kit'
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
  assignedTo: '', titleCompany: '', agent: '', gf: '', policy: '',
  psaOwner: '', psaAtp: '', psaAmount: '', settlementReceived: '', settlementExecuted: '', curative: false,
  pkgSubmitted: '', pkgApproved: '', instruction: '', anticipated: '', actual: '',
}
const BLANK_CONVEYANCE = { type: '', signed: '', recorded: '', number: '' }
const BLANK_COMMITMENT = { owner: '', requested: '', received: '', effective: '', issued: '', planSubmitted: '', planApproved: '' }

function fromRecord(t: TitleClosing) {
  return {
    assignedTo: t.assignedTo ?? '', titleCompany: t.titleCompany ?? '', agent: t.titleClosingAgent ?? '', gf: t.gfNumber ?? '', policy: t.finalTitlePolicyNo ?? '',
    psaOwner: t.psaSignedByOwnerDate ?? '', psaAtp: t.psaSignedByAtpDate ?? '', psaAmount: t.psaAmount === null ? '' : String(t.psaAmount),
    settlementReceived: t.settlementReceivedDate ?? '', settlementExecuted: t.settlementExecutedDate ?? '', curative: t.curativeItemsCompleted,
    pkgSubmitted: t.closingPackageSubmittedDate ?? '', pkgApproved: t.closingPackageApprovedDate ?? '', instruction: t.closingInstructionDate ?? '',
    anticipated: t.anticipatedClosingDate ?? '', actual: t.actualClosingDate ?? '',
  }
}

/**
 * Title and Closing (Phase 1.5, ADR 0018): the one record of a parcel that tracks its title company, purchase and sale agreement dates,
 * closing dates, conveyance documents and title commitments. The first save creates it; later saves change it.
 */
export function TitleClosingTab({ parcelId, editable }: { parcelId: string; editable: boolean }) {
  const { manager } = useAuth()
  const api = titleClosingApi(manager.api)
  const queryClient = useQueryClient()
  const key = ['parcel-tabs', parcelId, 'title-closing']
  const q = useQuery({ queryKey: key, queryFn: () => api.get(parcelId), enabled: manager.can('titleclosing') })
  const companies = useLibraryValues('title_company')
  const conveyanceTypes = useLibraryValues('conveyance_type')
  const agents = useAgentNames()
  const teamNames = useTeamNames(parcelId)
  const record = q.data?.record ?? null
  const [f, setF] = useState(BLANK)
  const [conveyances, setConveyances] = useState<ConveyanceDocument[]>([])
  const [commitments, setCommitments] = useState<TitleCommitment[]>([])
  const [cv, setCv] = useState(BLANK_CONVEYANCE)
  const [cm, setCm] = useState(BLANK_COMMITMENT)
  const [local, setLocal] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)
  const canSave = editable && manager.can('titleclosing', record ? 'update' : 'create')
  const canRemove = editable && !!record && manager.can('titleclosing', 'delete')
  const set = <K extends keyof typeof BLANK>(k: K) => (v: (typeof BLANK)[K]) => { setSaved(false); setF((x) => ({ ...x, [k]: v })) }

  // Load the saved record into the form (and clear the form when there is none or it was removed).
  useEffect(() => {
    if (q.isPending) return
    setF(record ? fromRecord(record) : BLANK)
    setConveyances(record?.conveyanceDocuments ?? [])
    setCommitments(record?.titleCommitments ?? [])
  }, [q.isPending, record?.id, record?.updatedAt]) // eslint-disable-line react-hooks/exhaustive-deps

  if (!manager.can('titleclosing')) return <p className="text-sm text-muted-foreground">Your role cannot see title and closing.</p>
  if (q.isPending) return <LoadingRows rows={4} />
  if (q.isError) return <ErrorState title="Title and closing could not be loaded" description={q.error.message} retry={() => void q.refetch()} />

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

  function addConveyance() {
    if (!Object.values(cv).some((v) => v.trim())) { setLocal({ ...local, conveyance: 'Enter at least one detail of the conveyance document.' }); return }
    if (cv.signed && cv.recorded && cv.recorded < cv.signed) { setLocal({ ...local, conveyance: 'A conveyance cannot be recorded before it is signed.' }); return }
    setConveyances([...conveyances, { conveyanceType: orNull(cv.type), signedDate: orNull(cv.signed), recordedDate: orNull(cv.recorded), recordingNumber: orNull(cv.number) }])
    setCv(BLANK_CONVEYANCE)
    setLocal({ ...local, conveyance: '' })
    setSaved(false)
  }

  function addCommitment() {
    if (!cm.owner.trim()) { setLocal({ ...local, commitment: 'Enter the vested owner name.' }); return }
    if (cm.planSubmitted && cm.planApproved && cm.planApproved < cm.planSubmitted) { setLocal({ ...local, commitment: 'A curative plan cannot be approved before it is submitted.' }); return }
    setCommitments([...commitments, {
      vestedOwnerName: cm.owner.trim(), requestedDate: orNull(cm.requested), receivedDate: orNull(cm.received), effectiveDate: orNull(cm.effective), issuedDate: orNull(cm.issued),
      curativePlanSubmittedDate: orNull(cm.planSubmitted), curativePlanApprovedDate: orNull(cm.planApproved),
    }])
    setCm(BLANK_COMMITMENT)
    setLocal({ ...local, commitment: '' })
    setSaved(false)
  }

  async function submit(ev: FormEvent) {
    ev.preventDefault()
    const errs: Record<string, string> = {}
    if (f.psaAmount.trim() && (Number.isNaN(Number(f.psaAmount)) || Number(f.psaAmount) < 0)) errs.psaAmount = 'The amount must be zero or more.'
    if (f.settlementReceived && f.settlementExecuted && f.settlementExecuted < f.settlementReceived) errs.settlementExecutedDate = 'The statement cannot be executed before it is received.'
    if (f.pkgSubmitted && f.pkgApproved && f.pkgApproved < f.pkgSubmitted) errs.closingPackageApprovedDate = 'The closing package cannot be approved before it is submitted.'
    setLocal(errs)
    if (Object.keys(errs).length) return
    const body = {
      assignedTo: orNull(f.assignedTo), titleCompany: orNull(f.titleCompany), titleClosingAgent: orNull(f.agent), gfNumber: orNull(f.gf), finalTitlePolicyNo: orNull(f.policy),
      psaSignedByOwnerDate: orNull(f.psaOwner), psaSignedByAtpDate: orNull(f.psaAtp), psaAmount: numOrNull(f.psaAmount),
      settlementReceivedDate: orNull(f.settlementReceived), settlementExecutedDate: orNull(f.settlementExecuted), curativeItemsCompleted: f.curative,
      closingPackageSubmittedDate: orNull(f.pkgSubmitted), closingPackageApprovedDate: orNull(f.pkgApproved), closingInstructionDate: orNull(f.instruction),
      anticipatedClosingDate: orNull(f.anticipated), actualClosingDate: orNull(f.actual),
      conveyanceDocuments: conveyances.map(({ conveyanceType, signedDate, recordedDate, recordingNumber }) => ({ conveyanceType, signedDate, recordedDate, recordingNumber })),
      titleCommitments: commitments.map(({ vestedOwnerName, requestedDate, receivedDate, effectiveDate, issuedDate, curativePlanSubmittedDate, curativePlanApprovedDate }) =>
        ({ vestedOwnerName, requestedDate, receivedDate, effectiveDate, issuedDate, curativePlanSubmittedDate, curativePlanApprovedDate })),
    }
    if (await run(() => api.save(parcelId, body))) setSaved(true)
  }
  const errors = { ...fields, ...local }
  const disabled = !canSave

  return (
    <>
      <form onSubmit={submit} noValidate aria-label="Title and closing" className="max-w-5xl">
        <Section title="General">
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            <div className="text-sm"><span className="text-xs text-muted-foreground">ID</span><div>{record?.number ?? 'Assigned when saved'}</div></div>
            <div className="text-sm"><span className="text-xs text-muted-foreground">Vested owner name</span><div>{record?.vestedOwnerName ?? '—'}</div></div>
            <SelectField label="Assigned to" value={f.assignedTo} onChange={set('assignedTo')} options={optionsWith(teamNames, f.assignedTo)} blankLabel="Select one" disabled={disabled} error={errors.assignedTo} />
            <SelectField label="Title company" value={f.titleCompany} onChange={set('titleCompany')} options={optionsWith(companies, f.titleCompany)} blankLabel="Select one" disabled={disabled} error={errors.titleCompany} />
            <SelectField label="Title/closing agent" value={f.agent} onChange={set('agent')} options={optionsWith(agents, f.agent)} blankLabel="Select one" disabled={disabled} error={errors.titleClosingAgent} />
            <TextField label="GF number" value={f.gf} onChange={set('gf')} maxLength={100} disabled={disabled} error={errors.gfNumber} />
            <TextField label="Final title policy no." value={f.policy} onChange={set('policy')} maxLength={100} disabled={disabled} error={errors.finalTitlePolicyNo} />
          </div>
        </Section>

        <Section title="Agreement / contract dates">
          <div className="grid gap-3 sm:grid-cols-2">
            <TextField label="Purchase and sale agreement signed by owner date" type="date" value={f.psaOwner} onChange={set('psaOwner')} disabled={disabled} error={errors.psaSignedByOwnerDate} />
            <TextField label="Closing package submitted date" type="date" value={f.pkgSubmitted} onChange={set('pkgSubmitted')} disabled={disabled} error={errors.closingPackageSubmittedDate} />
            <TextField label="Purchase and sale agreement signed by ATP date" type="date" value={f.psaAtp} onChange={set('psaAtp')} disabled={disabled} error={errors.psaSignedByAtpDate} />
            <TextField label="Closing package approved date" type="date" value={f.pkgApproved} onChange={set('pkgApproved')} disabled={disabled} error={errors.closingPackageApprovedDate} />
            <TextField label="Purchase and sale agreement amount" type="number" inputMode="decimal" value={f.psaAmount} onChange={set('psaAmount')} disabled={disabled} error={errors.psaAmount} />
            <TextField label="Closing instruction to title company date" type="date" value={f.instruction} onChange={set('instruction')} disabled={disabled} error={errors.closingInstructionDate} />
            <TextField label="Settlement statement received date" type="date" value={f.settlementReceived} onChange={set('settlementReceived')} disabled={disabled} error={errors.settlementReceivedDate} />
            <TextField label="Anticipated closing date" type="date" value={f.anticipated} onChange={set('anticipated')} disabled={disabled} error={errors.anticipatedClosingDate} />
            <TextField label="Settlement statement executed date" type="date" value={f.settlementExecuted} onChange={set('settlementExecuted')} disabled={disabled} error={errors.settlementExecutedDate} />
            <TextField label="Actual closing date" type="date" value={f.actual} onChange={set('actual')} disabled={disabled} error={errors.actualClosingDate} />
            <CheckboxField label="Curative items completed" checked={f.curative} onChange={(v) => !disabled && set('curative')(v)} />
          </div>
        </Section>

        <Section title="Conveyance documents">
          {conveyances.length > 0 && (
            <DataTable caption="Conveyance documents" headers={['Conveyance type', 'Signed date', 'Recorded date', 'Recording number', ...(canSave ? ['Actions'] : [])]}>
              {conveyances.map((c, i) => (
                <tr key={`${c.recordingNumber}-${i}`}>
                  <td className="px-3 py-2">{c.conveyanceType ?? '—'}</td>
                  <td className="px-3 py-2">{c.signedDate ? dateText(c.signedDate) : '—'}</td>
                  <td className="px-3 py-2">{c.recordedDate ? dateText(c.recordedDate) : '—'}</td>
                  <td className="px-3 py-2">{c.recordingNumber ?? '—'}</td>
                  {canSave && <td className="px-3 py-2"><button type="button" aria-label={`Remove conveyance document ${i + 1}`} className={buttonClassName('ghost', 'sm')} onClick={() => { setSaved(false); setConveyances(conveyances.filter((_, j) => j !== i)) }}><Trash2 size={14} aria-hidden /></button></td>}
                </tr>
              ))}
            </DataTable>
          )}
          {canSave && (
            <>
              <div className="mt-3 grid gap-3 sm:grid-cols-4">
                <SelectField label="Conveyance type" value={cv.type} onChange={(v) => setCv({ ...cv, type: v })} options={optionsWith(conveyanceTypes, cv.type)} blankLabel="Select one" error={errors.conveyance} />
                <TextField label="Conveyance document signed date" type="date" value={cv.signed} onChange={(v) => setCv({ ...cv, signed: v })} />
                <TextField label="Conveyance document recorded date" type="date" value={cv.recorded} onChange={(v) => setCv({ ...cv, recorded: v })} />
                <TextField label="Conveyance document recording number" value={cv.number} onChange={(v) => setCv({ ...cv, number: v })} maxLength={100} />
              </div>
              <button type="button" className={`${buttonClassName('outline', 'sm')} mt-2`} onClick={addConveyance}>Add conveyance document</button>
            </>
          )}
        </Section>

        <Section title="Title commitments">
          {commitments.length > 0 && (
            <DataTable caption="Title commitments" headers={['Vested owner name', 'Requested', 'Received', 'Effective', 'Issued', 'Curative plan submitted', 'Curative plan approved', ...(canSave ? ['Actions'] : [])]}>
              {commitments.map((c, i) => (
                <tr key={`${c.vestedOwnerName}-${i}`}>
                  <td className="px-3 py-2 font-medium">{c.vestedOwnerName}</td>
                  {[c.requestedDate, c.receivedDate, c.effectiveDate, c.issuedDate, c.curativePlanSubmittedDate, c.curativePlanApprovedDate].map((d, j) => <td key={j} className="px-3 py-2">{d ? dateText(d) : '—'}</td>)}
                  {canSave && <td className="px-3 py-2"><button type="button" aria-label={`Remove title commitment ${c.vestedOwnerName}`} className={buttonClassName('ghost', 'sm')} onClick={() => { setSaved(false); setCommitments(commitments.filter((_, j) => j !== i)) }}><Trash2 size={14} aria-hidden /></button></td>}
                </tr>
              ))}
            </DataTable>
          )}
          {canSave && (
            <>
              <div className="mt-3 grid gap-3 sm:grid-cols-4">
                <TextField label="Commitment vested owner name" value={cm.owner} onChange={(v) => setCm({ ...cm, owner: v })} maxLength={200} error={errors.commitment} />
                <TextField label="Title commitment requested date" type="date" value={cm.requested} onChange={(v) => setCm({ ...cm, requested: v })} />
                <TextField label="Title commitment received date" type="date" value={cm.received} onChange={(v) => setCm({ ...cm, received: v })} />
                <TextField label="Title commitment effective date" type="date" value={cm.effective} onChange={(v) => setCm({ ...cm, effective: v })} />
                <TextField label="Title commitment issued date" type="date" value={cm.issued} onChange={(v) => setCm({ ...cm, issued: v })} />
                <TextField label="Title curative plan submitted date" type="date" value={cm.planSubmitted} onChange={(v) => setCm({ ...cm, planSubmitted: v })} />
                <TextField label="Title curative plan approved date" type="date" value={cm.planApproved} onChange={(v) => setCm({ ...cm, planApproved: v })} />
              </div>
              <button type="button" className={`${buttonClassName('outline', 'sm')} mt-2`} onClick={addCommitment}>Add title commitment</button>
            </>
          )}
        </Section>

        <FormError error={error} />
        {saved && <p role="status" className="mb-2 text-sm text-ok">Title and closing saved.</p>}
        <div className="flex gap-2">
          {canSave && <button type="submit" disabled={busy} className={buttonClassName('default')}>{record ? 'Save changes' : 'Save title and closing'}</button>}
          {canRemove && (
            <button type="button" disabled={busy} className={buttonClassName('outline')} onClick={() => void run(() => api.remove(parcelId)).then((ok) => { if (ok) setSaved(false) })}>
              Remove title and closing record
            </button>
          )}
          {!canSave && <p className="text-sm text-muted-foreground">{record ? 'Your role can view this record but not change it.' : 'Nothing has been recorded yet.'}</p>}
        </div>
      </form>

      {record && <div className="mt-6"><AttachmentsPanel entityType="TitleClosing" entityId={record.id} page="titleclosing" title={`Attachments for ${record.number}`} /></div>}
    </>
  )
}
