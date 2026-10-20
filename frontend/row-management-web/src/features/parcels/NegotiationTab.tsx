import { useEffect, useState, type FormEvent, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Trash2 } from 'lucide-react'
import { libraryApi } from '@/api/library'
import type { Parcel } from '@/api/parcels'
import {
  negotiationApi, type Negotiation, type NegotiationOffer, type NegotiationRightOfEntry, type NegotiationSettlement, type NegotiationAgent, type NegotiationNote,
} from '@/api/parcel-tabs'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { ErrorState, LoadingRows } from '@/components/ui/states'
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

const money = (n: number | null) => (n === null ? '—' : n.toLocaleString('en-US', { style: 'currency', currency: 'USD' }))
const day = (d: string | null) => (d ? dateText(d) : '—')
const today = () => new Date().toISOString().slice(0, 10)

const BLANK = {
  assignedTo: '', total: '', notes: '', jatSubmitted: '', jatResponseDate: '', jatResponse: '', fta: false,
  puaRequested: '', puaApproved: '', puaAmount: '', puaExecuted: '', puaClosed: '',
}
const BLANK_NOTE = { date: today(), text: '' }
const BLANK_ROE = { mailed: '', delivered: '', signed: '', atp: '', expires: '', instructions: '' }
const BLANK_OFFER = { type: '', mailed: '', delivered: '', amount: '', notes: '' }
const BLANK_SETTLEMENT = { type: '', amount: '', received: '', submitted: '', response: '', fromAtp: '', toOwner: '', notes: '' }

function fromRecord(n: Negotiation) {
  return {
    assignedTo: n.assignedTo ?? '', total: n.totalAgreementAmount === null ? '' : String(n.totalAgreementAmount), notes: n.notes ?? '',
    jatSubmitted: n.counterofferSubmittedToJatDate ?? '', jatResponseDate: n.counterofferResponseFromJatDate ?? '', jatResponse: n.jatResponse ?? '',
    fta: n.ftaApprovalRequiredForCounteroffer, puaRequested: n.puaRequestedDate ?? '', puaApproved: n.puaApprovedDate ?? '',
    puaAmount: n.puaApprovedAmount === null ? '' : String(n.puaApprovedAmount), puaExecuted: n.puaExecutedDate ?? '', puaClosed: n.puaClosedDate ?? '',
  }
}

/** A grid that is edited in the form and saved with the record: its table, a remove button per row, and (for roles that may save) the add inputs. */
function Grid<T>({ title, caption, headers, rows, cells, rowLabel, onRemove, adding, onAdd, addLabel, error, canSave }: {
  title: string; caption: string; headers: string[]; rows: T[]; cells: (r: T) => ReactNode[]; rowLabel: (r: T, i: number) => string
  onRemove: (i: number) => void; adding: ReactNode; onAdd: () => void; addLabel: string; error?: string; canSave: boolean
}) {
  return (
    <Section title={title}>
      {rows.length > 0 && (
        <DataTable caption={caption} headers={[...headers, ...(canSave ? ['Actions'] : [])]}>
          {rows.map((r, i) => (
            <tr key={i}>
              {cells(r).map((c, j) => <td key={j} className="px-3 py-2">{c}</td>)}
              {canSave && <td className="px-3 py-2"><button type="button" aria-label={rowLabel(r, i)} className={buttonClassName('ghost', 'sm')} onClick={() => onRemove(i)}><Trash2 size={14} aria-hidden /></button></td>}
            </tr>
          ))}
        </DataTable>
      )}
      {canSave && (
        <>
          <div className="mt-3 grid gap-3 sm:grid-cols-3 lg:grid-cols-4">{adding}</div>
          {error && <p role="alert" className="mt-1 text-xs text-destructive">{error}</p>}
          <button type="button" className={`${buttonClassName('outline', 'sm')} mt-2`} onClick={onAdd}>{addLabel}</button>
        </>
      )}
    </Section>
  )
}

/**
 * Negotiation (Phase 1.5, ADR 0019): the one record of a parcel that tracks who negotiates, the right of entry, offers, administrative
 * settlement counteroffers, the JAT approval of a counteroffer and the possession and use agreement. The first save creates it.
 */
export function NegotiationTab({ parcel, editable }: { parcel: Parcel; editable: boolean }) {
  const parcelId = parcel.id
  const { manager } = useAuth()
  const api = negotiationApi(manager.api)
  const queryClient = useQueryClient()
  const key = ['parcel-tabs', parcelId, 'negotiation']
  const q = useQuery({ queryKey: key, queryFn: () => api.get(parcelId), enabled: manager.can('parcelnegotiation') })
  const agentNames = useAgentNames()
  const agentList = useQuery({ queryKey: ['library', 'agents'], queryFn: () => libraryApi(manager.api).agents(), retry: false })
  const offerTypes = useLibraryValues('offer_type')
  const settlementTypes = useLibraryValues('settlement_type')
  const responses = useLibraryValues('counteroffer_response')
  const jatResponses = useLibraryValues('jat_response')
  const record = q.data?.record ?? null
  const [f, setF] = useState(BLANK)
  const [notes, setNotes] = useState<NegotiationNote[]>([])
  const [agents, setAgents] = useState<NegotiationAgent[]>([])
  const [roes, setRoes] = useState<NegotiationRightOfEntry[]>([])
  const [offers, setOffers] = useState<NegotiationOffer[]>([])
  const [settlements, setSettlements] = useState<NegotiationSettlement[]>([])
  const [nt, setNt] = useState(BLANK_NOTE)
  const [ag, setAg] = useState('')
  const [re, setRe] = useState(BLANK_ROE)
  const [of, setOf] = useState(BLANK_OFFER)
  const [st, setSt] = useState(BLANK_SETTLEMENT)
  const [local, setLocal] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)
  const canSave = editable && manager.can('parcelnegotiation', record ? 'update' : 'create')
  const canRemove = editable && !!record && manager.can('parcelnegotiation', 'delete')
  const set = <K extends keyof typeof BLANK>(k: K) => (v: (typeof BLANK)[K]) => { setSaved(false); setF((x) => ({ ...x, [k]: v })) }
  const err = (k: string, m: string) => setLocal((l) => ({ ...l, [k]: m }))

  useEffect(() => {
    if (q.isPending) return
    setF(record ? fromRecord(record) : BLANK)
    setNotes(record?.statusNotes ?? [])
    setAgents(record?.agents ?? [])
    setRoes(record?.rightsOfEntry ?? [])
    setOffers(record?.offers ?? [])
    setSettlements(record?.settlements ?? [])
  }, [q.isPending, record?.id, record?.updatedAt]) // eslint-disable-line react-hooks/exhaustive-deps

  if (!manager.can('parcelnegotiation')) return <p className="text-sm text-muted-foreground">Your role cannot see negotiations.</p>
  if (q.isPending) return <LoadingRows rows={4} />
  if (q.isError) return <ErrorState title="The negotiation could not be loaded" description={q.error.message} retry={() => void q.refetch()} />

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
  const drop = <T,>(rows: T[], set: (r: T[]) => void) => (i: number) => { setSaved(false); set(rows.filter((_, j) => j !== i)) }
  const isMoney = (v: string) => !v.trim() || (!Number.isNaN(Number(v)) && Number(v) >= 0)

  function addNote() {
    if (!nt.text.trim()) return err('note', 'Enter the status note.')
    setNotes([...notes, { noteDate: nt.date || today(), text: nt.text.trim() }]); setNt(BLANK_NOTE); err('note', ''); setSaved(false)
  }
  function addAgent() {
    if (!ag) return err('agent', 'Choose the agent.')
    const lib = agentList.data?.find((a) => a.name === ag)
    setAgents([...agents, { agentName: ag, agentType: lib?.agentType ?? null, company: lib?.company ?? null, phone: lib?.phone ?? null, email: lib?.email ?? null }])
    setAg(''); err('agent', ''); setSaved(false)
  }
  function addRoe() {
    if (!Object.values(re).some((v) => v.trim())) return err('roe', 'Enter at least one detail of the right of entry.')
    if (re.mailed && re.delivered && re.delivered < re.mailed) return err('roe', 'A letter cannot be delivered before it is mailed.')
    setRoes([...roes, { letterMailedDate: orNull(re.mailed), letterDeliveredDate: orNull(re.delivered), signedReceivedDate: orNull(re.signed), sentToAtpDate: orNull(re.atp), expirationDate: orNull(re.expires), specialInstructions: orNull(re.instructions) }])
    setRe(BLANK_ROE); err('roe', ''); setSaved(false)
  }
  function addOffer() {
    if (!Object.values(of).some((v) => v.trim())) return err('offer', 'Enter at least one detail of the offer.')
    if (!isMoney(of.amount)) return err('offer', 'The amount must be zero or more.')
    if (of.mailed && of.delivered && of.delivered < of.mailed) return err('offer', 'An offer cannot be delivered before it is mailed.')
    setOffers([...offers, { offerType: orNull(of.type), mailedDate: orNull(of.mailed), deliveredDate: orNull(of.delivered), amount: numOrNull(of.amount), notes: orNull(of.notes) }])
    setOf(BLANK_OFFER); err('offer', ''); setSaved(false)
  }
  function addSettlement() {
    if (!Object.values(st).some((v) => v.trim())) return err('settlement', 'Enter at least one detail of the settlement.')
    if (!isMoney(st.amount)) return err('settlement', 'The amount must be zero or more.')
    if (st.received && st.submitted && st.submitted < st.received) return err('settlement', 'A counteroffer cannot be submitted before it is received.')
    setSettlements([...settlements, {
      settlementType: orNull(st.type), counterofferAmount: numOrNull(st.amount), dateReceived: orNull(st.received), dateSubmitted: orNull(st.submitted),
      counterofferResponse: orNull(st.response), responseDateFromAtp: orNull(st.fromAtp), responseDateToOwner: orNull(st.toOwner), settlementNotes: orNull(st.notes),
    }])
    setSt(BLANK_SETTLEMENT); err('settlement', ''); setSaved(false)
  }

  async function submit(ev: FormEvent) {
    ev.preventDefault()
    const errs: Record<string, string> = {}
    if (!isMoney(f.total)) errs.totalAgreementAmount = 'The amount must be zero or more.'
    if (!isMoney(f.puaAmount)) errs.puaApprovedAmount = 'The amount must be zero or more.'
    if (f.jatSubmitted && f.jatResponseDate && f.jatResponseDate < f.jatSubmitted) errs.counterofferResponseFromJatDate = 'The JAT cannot respond before the counteroffer is submitted.'
    if (f.puaRequested && f.puaApproved && f.puaApproved < f.puaRequested) errs.puaApprovedDate = 'The PUA cannot be approved before it is requested.'
    if (f.puaExecuted && f.puaClosed && f.puaClosed < f.puaExecuted) errs.puaClosedDate = 'The PUA cannot be closed before it is executed.'
    setLocal(errs)
    if (Object.keys(errs).length) return
    const body = {
      assignedTo: orNull(f.assignedTo), totalAgreementAmount: numOrNull(f.total), notes: orNull(f.notes),
      counterofferSubmittedToJatDate: orNull(f.jatSubmitted), counterofferResponseFromJatDate: orNull(f.jatResponseDate), jatResponse: orNull(f.jatResponse),
      ftaApprovalRequiredForCounteroffer: f.fta, puaRequestedDate: orNull(f.puaRequested), puaApprovedDate: orNull(f.puaApproved), puaApprovedAmount: numOrNull(f.puaAmount),
      puaExecutedDate: orNull(f.puaExecuted), puaClosedDate: orNull(f.puaClosed),
      statusNotes: notes.map(({ noteDate, text }) => ({ noteDate, text })),
      agents: agents.map(({ agentName }) => ({ agentName })),
      rightsOfEntry: roes.map(({ letterMailedDate, letterDeliveredDate, signedReceivedDate, sentToAtpDate, expirationDate, specialInstructions }) =>
        ({ letterMailedDate, letterDeliveredDate, signedReceivedDate, sentToAtpDate, expirationDate, specialInstructions })),
      offers: offers.map(({ offerType, mailedDate, deliveredDate, amount, notes: n }) => ({ offerType, mailedDate, deliveredDate, amount, notes: n })),
      settlements: settlements.map(({ settlementType, counterofferAmount, dateReceived, dateSubmitted, counterofferResponse, responseDateFromAtp, responseDateToOwner, settlementNotes }) =>
        ({ settlementType, counterofferAmount, dateReceived, dateSubmitted, counterofferResponse, responseDateFromAtp, responseDateToOwner, settlementNotes })),
    }
    if (await run(() => api.save(parcelId, body))) setSaved(true)
  }
  const errors = { ...fields, ...local }
  const disabled = !canSave
  const fact = (label: string, value: string | null | undefined) => <div className="text-sm"><span className="text-xs text-muted-foreground">{label}</span><div>{value || '—'}</div></div>

  return (
    <>
      <form onSubmit={submit} noValidate aria-label="Negotiation" className="max-w-5xl">
        <Section title="Parcel details">
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {fact('ID', record?.number ?? 'Assigned when saved')}
            {fact('Parcel number', parcel.number)}
            {fact('Owner name', parcel.ownerName)}
            {fact('Possession type', parcel.possessionType)}
            {fact('Possession date', parcel.possessionDate ? dateText(parcel.possessionDate) : null)}
            {fact('Proposed possession date', parcel.proposedPossessionDate ? dateText(parcel.proposedPossessionDate) : null)}
            {fact('Parcel status', parcel.statusName)}
            {fact('Parcel location', parcel.parcelLocation)}
            {fact('Parcel address', parcel.address)}
            {fact('CAD number', parcel.cadNumber)}
            <SelectField label="Assigned to" value={f.assignedTo} onChange={set('assignedTo')} options={optionsWith(agentNames, f.assignedTo)} blankLabel="Select one" disabled={disabled} error={errors.assignedTo} />
          </div>
        </Section>

        <Grid title="Negotiation status notes" caption="Negotiation status notes" headers={['Date', 'Note']} rows={notes} canSave={canSave}
          cells={(r) => [day(r.noteDate), <span key="t" className="whitespace-pre-wrap">{r.text}</span>]} rowLabel={(_, i) => `Remove status note ${i + 1}`} onRemove={drop(notes, setNotes)}
          addLabel="Add status note" onAdd={addNote} error={errors.note}
          adding={<><TextField label="Status note date" type="date" value={nt.date} onChange={(v) => setNt({ ...nt, date: v })} /><TextField label="Status note" value={nt.text} onChange={(v) => setNt({ ...nt, text: v })} maxLength={4000} /></>} />

        <Grid title="Agents" caption="Agents" headers={['Agent name', 'Agent type', 'Company', 'Phone number', 'Email address']} rows={agents} canSave={canSave}
          cells={(r) => [r.agentName, r.agentType ?? '—', r.company ?? '—', r.phone ?? '—', r.email ?? '—']} rowLabel={(r) => `Remove agent ${r.agentName}`} onRemove={drop(agents, setAgents)}
          addLabel="Add agent" onAdd={addAgent} error={errors.agent}
          adding={<SelectField label="Agent" value={ag} onChange={setAg} options={optionsWith(agentNames, ag)} blankLabel="Select one" />} />

        <Grid title="Right of entry" caption="Right of entry" headers={['Letter mailed', 'Letter delivered', 'Signed received', 'Sent to ATP', 'Expiration date', 'Special instructions']} rows={roes} canSave={canSave}
          cells={(r) => [day(r.letterMailedDate), day(r.letterDeliveredDate), day(r.signedReceivedDate), day(r.sentToAtpDate), day(r.expirationDate), r.specialInstructions ?? '—']}
          rowLabel={(_, i) => `Remove right of entry ${i + 1}`} onRemove={drop(roes, setRoes)} addLabel="Add right of entry" onAdd={addRoe} error={errors.roe}
          adding={<>
            <TextField label="Right of entry letter mailed" type="date" value={re.mailed} onChange={(v) => setRe({ ...re, mailed: v })} />
            <TextField label="Right of entry letter delivered" type="date" value={re.delivered} onChange={(v) => setRe({ ...re, delivered: v })} />
            <TextField label="Signed right of entry received" type="date" value={re.signed} onChange={(v) => setRe({ ...re, signed: v })} />
            <TextField label="Right of entry sent to ATP" type="date" value={re.atp} onChange={(v) => setRe({ ...re, atp: v })} />
            <TextField label="Right of entry expiration date" type="date" value={re.expires} onChange={(v) => setRe({ ...re, expires: v })} />
            <TextField label="Special instructions included in right of entry" value={re.instructions} onChange={(v) => setRe({ ...re, instructions: v })} maxLength={2000} />
          </>} />

        <Grid title="Offers" caption="Offers" headers={['Offer type', 'Mailed date', 'Delivered date', 'Amount ($)', 'Offer notes']} rows={offers} canSave={canSave}
          cells={(r) => [r.offerType ?? '—', day(r.mailedDate), day(r.deliveredDate), money(r.amount), r.notes ?? '—']} rowLabel={(_, i) => `Remove offer ${i + 1}`} onRemove={drop(offers, setOffers)}
          addLabel="Add offer" onAdd={addOffer} error={errors.offer}
          adding={<>
            <SelectField label="Offer type" value={of.type} onChange={(v) => setOf({ ...of, type: v })} options={optionsWith(offerTypes, of.type)} blankLabel="Select one" />
            <TextField label="Offer mailed date" type="date" value={of.mailed} onChange={(v) => setOf({ ...of, mailed: v })} />
            <TextField label="Offer delivered date" type="date" value={of.delivered} onChange={(v) => setOf({ ...of, delivered: v })} />
            <TextField label="Offer amount" type="number" inputMode="decimal" value={of.amount} onChange={(v) => setOf({ ...of, amount: v })} />
            <TextField label="Offer notes" value={of.notes} onChange={(v) => setOf({ ...of, notes: v })} maxLength={2000} />
          </>} />

        <Grid title="Administrative settlement" caption="Administrative settlement" headers={['Type', 'Counteroffer amount ($)', 'Date received', 'Date submitted', 'Counteroffer response', 'Response date from ATP', 'Response date to owner', 'Settlement notes']}
          rows={settlements} canSave={canSave}
          cells={(r) => [r.settlementType ?? '—', money(r.counterofferAmount), day(r.dateReceived), day(r.dateSubmitted), r.counterofferResponse ?? '—', day(r.responseDateFromAtp), day(r.responseDateToOwner), r.settlementNotes ?? '—']}
          rowLabel={(_, i) => `Remove settlement ${i + 1}`} onRemove={drop(settlements, setSettlements)} addLabel="Add settlement" onAdd={addSettlement} error={errors.settlement}
          adding={<>
            <SelectField label="Settlement type" value={st.type} onChange={(v) => setSt({ ...st, type: v })} options={optionsWith(settlementTypes, st.type)} blankLabel="Select one" />
            <TextField label="Counteroffer amount" type="number" inputMode="decimal" value={st.amount} onChange={(v) => setSt({ ...st, amount: v })} />
            <TextField label="Counteroffer date received" type="date" value={st.received} onChange={(v) => setSt({ ...st, received: v })} />
            <TextField label="Counteroffer date submitted" type="date" value={st.submitted} onChange={(v) => setSt({ ...st, submitted: v })} />
            <SelectField label="Counteroffer response" value={st.response} onChange={(v) => setSt({ ...st, response: v })} options={optionsWith(responses, st.response)} blankLabel="Select one" />
            <TextField label="Response date from ATP" type="date" value={st.fromAtp} onChange={(v) => setSt({ ...st, fromAtp: v })} />
            <TextField label="Response date to owner" type="date" value={st.toOwner} onChange={(v) => setSt({ ...st, toOwner: v })} />
            <TextField label="Settlement notes" value={st.notes} onChange={(v) => setSt({ ...st, notes: v })} maxLength={2000} />
          </>} />

        <Section title="Agreement and JAT">
          <div className="grid gap-3 sm:grid-cols-2">
            <TextField label="Total agreement amount" type="number" inputMode="decimal" value={f.total} onChange={set('total')} disabled={disabled} error={errors.totalAgreementAmount} />
            <TextField label="Notes" value={f.notes} onChange={set('notes')} maxLength={4000} disabled={disabled} error={errors.notes} />
            <TextField label="Counteroffer submitted to JAT" type="date" value={f.jatSubmitted} onChange={set('jatSubmitted')} disabled={disabled} error={errors.counterofferSubmittedToJatDate} />
            <CheckboxField label="FTA approval required for counteroffer" checked={f.fta} onChange={(v) => !disabled && set('fta')(v)} />
            <TextField label="Counteroffer response from JAT" type="date" value={f.jatResponseDate} onChange={set('jatResponseDate')} disabled={disabled} error={errors.counterofferResponseFromJatDate} />
            <SelectField label="JAT response" value={f.jatResponse} onChange={set('jatResponse')} options={optionsWith(jatResponses, f.jatResponse)} blankLabel="Select one" disabled={disabled} error={errors.jatResponse} />
          </div>
        </Section>

        <Section title="Possession and use agreement (PUA)">
          <div className="grid gap-3 sm:grid-cols-2">
            <TextField label="PUA requested" type="date" value={f.puaRequested} onChange={set('puaRequested')} disabled={disabled} error={errors.puaRequestedDate} />
            <TextField label="PUA executed" type="date" value={f.puaExecuted} onChange={set('puaExecuted')} disabled={disabled} error={errors.puaExecutedDate} />
            <TextField label="PUA approved" type="date" value={f.puaApproved} onChange={set('puaApproved')} disabled={disabled} error={errors.puaApprovedDate} />
            <TextField label="PUA closed" type="date" value={f.puaClosed} onChange={set('puaClosed')} disabled={disabled} error={errors.puaClosedDate} />
            <TextField label="PUA approved amount" type="number" inputMode="decimal" value={f.puaAmount} onChange={set('puaAmount')} disabled={disabled} error={errors.puaApprovedAmount} />
          </div>
        </Section>

        <FormError error={error} />
        {saved && <p role="status" className="mb-2 text-sm text-ok">Negotiation saved.</p>}
        <div className="flex gap-2">
          {canSave && <button type="submit" disabled={busy} className={buttonClassName('default')}>{record ? 'Save changes' : 'Save negotiation'}</button>}
          {canRemove && (
            <button type="button" disabled={busy} className={buttonClassName('outline')} onClick={() => void run(() => api.remove(parcelId)).then((ok) => { if (ok) setSaved(false) })}>
              Remove negotiation record
            </button>
          )}
          {!canSave && <p className="text-sm text-muted-foreground">{record ? 'Your role can view this record but not change it.' : 'Nothing has been recorded yet.'}</p>}
        </div>
      </form>

      {record && <div className="mt-6"><AttachmentsPanel entityType="ParcelNegotiation" entityId={record.id} page="parcelnegotiation" title={`Attachments for ${record.number}`} /></div>}
    </>
  )
}
