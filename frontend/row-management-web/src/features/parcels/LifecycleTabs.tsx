import { useEffect, useState, type FormEvent, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Pencil, Trash2 } from 'lucide-react'
import { libraryApi } from '@/api/library'
import type { Parcel } from '@/api/parcels'
import {
  condemnationApi, propertyApi, relocationsApi, type Condemnation, type CondemnationAttorney, type CondemnationCommissioner, type CondemnationNotice, type PropertyFunding,
  type PropertyRecord, type Relocation, type RelocationAgent, type RelocationContact, type RelocationContactLog, type RelocationEligibility, type StatusNote,
} from '@/api/parcel-tabs'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { CheckboxField, DataTable, dateText, fieldErrorsOf, FormError, messageOf, numOrNull, orNull, SelectField, TextField } from '../projects/form-kit'
import { AttachmentsPanel } from '../documents/AttachmentsPanel'
import { optionsWith, useAgentNames, useLibraryValues } from '../library/library-kit'
import { Grid } from './NegotiationTab'
import { useTeamNames } from './team-kit'
import { agent, bodyFrom, blankState, checkState, date, Fact, flag, list, money, SpecFields, stateFrom, StatusNotesGrid, text, type FieldSpec, type FormState } from './lifecycle-kit'

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <fieldset aria-label={title} className="mb-4 rounded-lg border bg-card p-4">
      <legend className="px-1 text-sm font-semibold">{title}</legend>
      {children}
    </fieldset>
  )
}

const day = (d: string | null) => (d ? dateText(d) : '—')
const dollars = (n: number | null) => (n === null ? '—' : n.toLocaleString('en-US', { style: 'currency', currency: 'USD' }))

// ── Condemnation ─────────────────────────────────────────────────────────

const PRELIM: FieldSpec[] = [
  date('prelimPackageSubmittedToAtp', 'Preliminary condemnation package submitted to ATP'), date('prelimPackageApprovedByAtp', 'Preliminary condemnation package approved by ATP'),
  date('prelimConcurrenceMemoSentToFta', 'Preliminary condemnation concurrence memo sent to FTA'), date('jatApprovalOfPrelimPackage', 'JAT approval of preliminary condemnation package'),
  date('jatApprovedPackageToCityLegal', 'JAT approved condemnation package to city legal'), date('cityLegalReviewCompleted', 'City legal review completed'),
  date('prelimPackageSubmittedForCouncil', 'Preliminary condemnation package submitted for city council approval'), date('cityCouncilApproval', 'City council condemnation approval'),
]
const SUPPORT: FieldSpec[] = [
  agent('edCoordinator', 'ED coordinator'), agent('outsideCounsel', 'Outside counsel'), agent('appraisalWitness', 'Appraisal witness'), agent('engineeringWitness', 'Engineering witness'),
  text('expertWitness1', 'Expert witness 1'), text('expertWitness2', 'Expert witness 2'), date('petitionPackageReceived', 'Petition package received from outside counsel'),
  date('petitionPackageReviewedByAtp', 'Petition package reviewed by ATP'), text('probateCauseNo', 'Probate cause no.', 100), date('petitionFiledDate', 'Petition filed date'),
  date('lisPendensRecordedDate', 'Notice of lis pendens recorded date'), date('lisPendensLettersMailedDate', 'Notice of lis pendens notification letters mailed'),
  date('commissionerStrikePeriodExpires', 'Commissioner strike period expires'), date('commissionerNotificationLettersMailed', 'Commissioner notification letters mailed'),
  date('orderSettingHearingDate', 'Order setting special commissioners hearing'), date('oathsOfCommissionersDate', 'Oaths of special commissioners'),
  date('prehearingScheduled', 'Prehearing scheduled'), date('commissionerFeesDeposited', 'Special commissioner fees deposited with court'),
  date('objectionDeadline', 'Objection deadline'), flag('objectionsFiled', 'Objections filed?'),
]
const CONDEMNATION_SPECS = [...PRELIM, ...SUPPORT]
const CONDEMNATION_ORDER: [string, string, string][] = [
  ['prelimPackageSubmittedToAtp', 'prelimPackageApprovedByAtp', 'The package cannot be approved before it is submitted.'],
  ['petitionPackageReceived', 'petitionPackageReviewedByAtp', 'The petition package cannot be reviewed before it is received.'],
  ['petitionFiledDate', 'objectionDeadline', 'The objection deadline cannot be before the petition is filed.'],
]

/** Condemnation (Phase 1.5, ADR 0020): the one record of a parcel that tracks its preliminary condemnation, support, attorneys, commissioners and notices of hearing. */
export function CondemnationTab({ parcel, editable }: { parcel: Parcel; editable: boolean }) {
  const parcelId = parcel.id
  const { manager } = useAuth()
  const api = condemnationApi(manager.api)
  const queryClient = useQueryClient()
  const key = ['parcel-tabs', parcelId, 'condemnation']
  const q = useQuery({ queryKey: key, queryFn: () => api.get(parcelId), enabled: manager.can('parcelcondemnation') })
  const agents = useAgentNames()
  const record: Condemnation | null = q.data?.record ?? null
  const [f, setF] = useState<FormState>(() => blankState(CONDEMNATION_SPECS))
  const [notes, setNotes] = useState<StatusNote[]>([])
  const [attorneys, setAttorneys] = useState<CondemnationAttorney[]>([])
  const [commissioners, setCommissioners] = useState<CondemnationCommissioner[]>([])
  const [notices, setNotices] = useState<CondemnationNotice[]>([])
  const [at, setAt] = useState({ party: '', name: '', firm: '', address: '', phone: '', email: '' })
  const [cm, setCm] = useState({ date: '', type: '', name: '', address: '', email: '', phone: '', struck: false })
  const [nt, setNt] = useState({ party: '', submitted: '', completed: '', filed: '', comments: '' })
  const [local, setLocal] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)
  const canSave = editable && manager.can('parcelcondemnation', record ? 'update' : 'create')
  const canRemove = editable && !!record && manager.can('parcelcondemnation', 'delete')
  const err = (k: string, m: string) => setLocal((l) => ({ ...l, [k]: m }))

  useEffect(() => {
    if (q.isPending) return
    setF(stateFrom(CONDEMNATION_SPECS, record?.details))
    setNotes(record?.statusNotes ?? []); setAttorneys(record?.attorneys ?? []); setCommissioners(record?.commissioners ?? []); setNotices(record?.notices ?? [])
  }, [q.isPending, record?.id, record?.updatedAt]) // eslint-disable-line react-hooks/exhaustive-deps

  if (!manager.can('parcelcondemnation')) return <p className="text-sm text-muted-foreground">Your role cannot see condemnations.</p>
  if (q.isPending) return <LoadingRows rows={4} />
  if (q.isError) return <ErrorState title="The condemnation could not be loaded" description={q.error.message} retry={() => void q.refetch()} />

  async function run(action: () => Promise<unknown>): Promise<boolean> {
    setError(null); setFields({}); setBusy(true)
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
  const touched = <T,>(set: (v: T) => void) => (v: T) => { setSaved(false); set(v) }

  function addAttorney() {
    if (!at.party.trim()) return err('attorney', 'Enter the party represented.')
    setAttorneys([...attorneys, { partyRepresented: at.party.trim(), attorneyName: orNull(at.name), firm: orNull(at.firm), address: orNull(at.address), phone: orNull(at.phone), email: orNull(at.email) }])
    setAt({ party: '', name: '', firm: '', address: '', phone: '', email: '' }); err('attorney', ''); setSaved(false)
  }
  function addCommissioner() {
    if (!cm.name.trim()) return err('commissioner', 'Enter the name of the commissioner.')
    setCommissioners([...commissioners, { appointmentDate: orNull(cm.date), appointmentType: orNull(cm.type), name: cm.name.trim(), address: orNull(cm.address), email: orNull(cm.email), phone: orNull(cm.phone), struck: cm.struck }])
    setCm({ date: '', type: '', name: '', address: '', email: '', phone: '', struck: false }); err('commissioner', ''); setSaved(false)
  }
  function addNotice() {
    if (!nt.party.trim()) return err('notice', 'Enter the party to serve.')
    if (nt.submitted && nt.completed && nt.completed < nt.submitted) return err('notice', 'Service cannot be completed before it is submitted.')
    setNotices([...notices, { partyToServe: nt.party.trim(), serviceSubmitted: orNull(nt.submitted), serviceCompleted: orNull(nt.completed), nohFiled: orNull(nt.filed), comments: orNull(nt.comments) }])
    setNt({ party: '', submitted: '', completed: '', filed: '', comments: '' }); err('notice', ''); setSaved(false)
  }

  async function submit(ev: FormEvent) {
    ev.preventDefault()
    const errs = checkState(CONDEMNATION_SPECS, f, CONDEMNATION_ORDER)
    setLocal(errs)
    if (Object.keys(errs).length) return
    const body = { details: bodyFrom(CONDEMNATION_SPECS, f), statusNotes: notes.map(({ noteDate, text: t }) => ({ noteDate, text: t })), attorneys, commissioners, notices }
    if (await run(() => api.save(parcelId, body))) setSaved(true)
  }
  const errors = { ...fields, ...local }
  const disabled = !canSave
  const setField = (k: string, v: string | boolean) => { setSaved(false); setF((x) => ({ ...x, [k]: v })) }

  return (
    <>
      <form onSubmit={submit} noValidate aria-label="Condemnation" className="max-w-5xl">
        <Section title="Condemnation">
          <div className="grid gap-3 sm:grid-cols-3">
            <Fact label="ID" value={record?.number ?? 'Assigned when saved'} />
            <Fact label="Parcel number" value={parcel.number} />
            <Fact label="Vested owner name" value={record?.vestedOwnerName} />
          </div>
        </Section>
        <StatusNotesGrid title="Condemnation status notes" notes={notes} onChange={touched(setNotes)} canSave={canSave} />
        <Section title="Preliminary condemnation"><SpecFields specs={PRELIM} state={f} onChange={setField} disabled={disabled} errors={errors} agents={agents} /></Section>
        <Section title="Condemnation support"><SpecFields specs={SUPPORT} state={f} onChange={setField} disabled={disabled} errors={errors} agents={agents} /></Section>

        <Grid title="Attorney of record" caption="Attorney of record" headers={['Party represented', 'Attorney name', 'Attorney firm', 'Attorney address', 'Attorney phone', 'Attorney email']} rows={attorneys} canSave={canSave}
          cells={(r) => [r.partyRepresented, r.attorneyName ?? '—', r.firm ?? '—', r.address ?? '—', r.phone ?? '—', r.email ?? '—']} rowLabel={(r) => `Remove attorney ${r.partyRepresented}`}
          onRemove={(i) => touched(setAttorneys)(attorneys.filter((_, j) => j !== i))} addLabel="Add attorney" onAdd={addAttorney} error={errors.attorney}
          adding={<>
            <TextField label="Party represented" value={at.party} onChange={(v) => setAt({ ...at, party: v })} maxLength={200} />
            <TextField label="Attorney name" value={at.name} onChange={(v) => setAt({ ...at, name: v })} maxLength={200} />
            <TextField label="Attorney firm" value={at.firm} onChange={(v) => setAt({ ...at, firm: v })} maxLength={200} />
            <TextField label="Attorney address" value={at.address} onChange={(v) => setAt({ ...at, address: v })} maxLength={500} />
            <TextField label="Attorney phone" value={at.phone} onChange={(v) => setAt({ ...at, phone: v })} maxLength={100} />
            <TextField label="Attorney email" value={at.email} onChange={(v) => setAt({ ...at, email: v })} maxLength={200} />
          </>} />

        <Grid title="Special commissioners" caption="Special commissioners" headers={['Appointment date', 'Appointment type', 'Name of commissioner', 'Commissioner address', 'Commissioner email', 'Commissioner phone number', 'Struck']}
          rows={commissioners} canSave={canSave}
          cells={(r) => [day(r.appointmentDate), r.appointmentType ?? '—', r.name, r.address ?? '—', r.email ?? '—', r.phone ?? '—', r.struck ? 'Yes' : 'No']} rowLabel={(r) => `Remove commissioner ${r.name}`}
          onRemove={(i) => touched(setCommissioners)(commissioners.filter((_, j) => j !== i))} addLabel="Add commissioner" onAdd={addCommissioner} error={errors.commissioner}
          adding={<>
            <TextField label="Appointment date" type="date" value={cm.date} onChange={(v) => setCm({ ...cm, date: v })} />
            <TextField label="Appointment type" value={cm.type} onChange={(v) => setCm({ ...cm, type: v })} maxLength={100} />
            <TextField label="Name of commissioner" value={cm.name} onChange={(v) => setCm({ ...cm, name: v })} maxLength={200} />
            <TextField label="Commissioner address" value={cm.address} onChange={(v) => setCm({ ...cm, address: v })} maxLength={500} />
            <TextField label="Commissioner email" value={cm.email} onChange={(v) => setCm({ ...cm, email: v })} maxLength={200} />
            <TextField label="Commissioner phone number" value={cm.phone} onChange={(v) => setCm({ ...cm, phone: v })} maxLength={100} />
            <CheckboxField label="Commissioner struck" checked={cm.struck} onChange={(v) => setCm({ ...cm, struck: v })} />
          </>} />

        <Grid title="Notice of hearing" caption="Notice of hearing" headers={['Party to serve', 'Service submitted', 'Service completed', 'NOH filed', 'NOH comments']} rows={notices} canSave={canSave}
          cells={(r) => [r.partyToServe, day(r.serviceSubmitted), day(r.serviceCompleted), day(r.nohFiled), r.comments ?? '—']} rowLabel={(r) => `Remove notice ${r.partyToServe}`}
          onRemove={(i) => touched(setNotices)(notices.filter((_, j) => j !== i))} addLabel="Add notice of hearing" onAdd={addNotice} error={errors.notice}
          adding={<>
            <TextField label="Party to serve" value={nt.party} onChange={(v) => setNt({ ...nt, party: v })} maxLength={200} />
            <TextField label="Service submitted" type="date" value={nt.submitted} onChange={(v) => setNt({ ...nt, submitted: v })} />
            <TextField label="Service completed" type="date" value={nt.completed} onChange={(v) => setNt({ ...nt, completed: v })} />
            <TextField label="NOH filed" type="date" value={nt.filed} onChange={(v) => setNt({ ...nt, filed: v })} />
            <TextField label="NOH comments" value={nt.comments} onChange={(v) => setNt({ ...nt, comments: v })} maxLength={2000} />
          </>} />

        <FormError error={error} />
        {saved && <p role="status" className="mb-2 text-sm text-ok">Condemnation saved.</p>}
        <div className="flex gap-2">
          {canSave && <button type="submit" disabled={busy} className={buttonClassName('default')}>{record ? 'Save changes' : 'Save condemnation'}</button>}
          {canRemove && <button type="button" disabled={busy} className={buttonClassName('outline')} onClick={() => void run(() => api.remove(parcelId)).then((ok) => { if (ok) setSaved(false) })}>Remove condemnation record</button>}
          {!canSave && <p className="text-sm text-muted-foreground">{record ? 'Your role can view this record but not change it.' : 'Nothing has been recorded yet.'}</p>}
        </div>
      </form>
      {record && <div className="mt-6"><AttachmentsPanel entityType="ParcelCondemnation" entityId={record.id} page="parcelcondemnation" title={`Attachments for ${record.number}`} /></div>}
    </>
  )
}

// ── Property Management & Disposition ────────────────────────────────────

/** Property Management & Disposition (Phase 1.5, ADR 0020): the one record of a parcel that tracks its property transfer, demo, disposition and funding. */
export function PropertyTab({ parcel, editable }: { parcel: Parcel; editable: boolean }) {
  const parcelId = parcel.id
  const { manager } = useAuth()
  const api = propertyApi(manager.api)
  const queryClient = useQueryClient()
  const key = ['parcel-tabs', parcelId, 'property-management']
  const q = useQuery({ queryKey: key, queryFn: () => api.get(parcelId), enabled: manager.can('parcelproperty') })
  const agents = useAgentNames()
  const demoStatuses = useLibraryValues('demo_status')
  const dispTypes = useLibraryValues('disposition_type')
  const dispMethods = useLibraryValues('disposition_method')
  const dispStatuses = useLibraryValues('disposition_status')
  const fundTypes = useLibraryValues('fund_type')
  const record: PropertyRecord | null = q.data?.record ?? null
  const MGMT: FieldSpec[] = [
    date('transferMemoSubmittedToAtp', 'Property transfer memo submitted to ATP'), date('transferMemoApprovedByAtp', 'Property transfer memo approved by ATP'),
    date('transferMemoToPdb', 'Property transfer memo to PDB'), date('propertySecured', 'Property secured'), list('demoStatus', 'Demo status', demoStatuses), date('demoCompleted', 'Demo completed'),
  ]
  const DISP: FieldSpec[] = [
    list('dispositionType', 'Disposition type', dispTypes), list('dispositionMethod', 'Disposition method', dispMethods), list('dispositionStatus', 'Disposition status', dispStatuses),
    text('buyerGrantee', 'Buyer / grantee', 1000), text('dispositionJustification', 'Disposition justification', 4000), flag('ftaConcurrenceRequired', 'FTA concurrence required?'),
    money('salePrice', 'Sale price / consideration value ($)'), date('legalReviewCompleteDate', 'Legal review complete date'), date('boardApprovalDate', 'Board approval date'),
    date('closingTransferDate', 'Closing / transfer date'), date('recordingDate', 'Recording date'), text('documentRecordingNo', 'Document recording no.', 1000),
  ]
  const specs = [...MGMT, ...DISP]
  const [f, setF] = useState<FormState>(() => blankState(specs))
  const [notes, setNotes] = useState<StatusNote[]>([])
  const [funding, setFunding] = useState<PropertyFunding[]>([])
  const [fd, setFd] = useState({ type: '', source: '', amount: '', pct: '' })
  const [local, setLocal] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)
  const canSave = editable && manager.can('parcelproperty', record ? 'update' : 'create')
  const canRemove = editable && !!record && manager.can('parcelproperty', 'delete')

  useEffect(() => {
    if (q.isPending) return
    setF(stateFrom(specs, record?.details)); setNotes(record?.statusNotes ?? []); setFunding(record?.funding ?? [])
  }, [q.isPending, record?.id, record?.updatedAt]) // eslint-disable-line react-hooks/exhaustive-deps

  if (!manager.can('parcelproperty')) return <p className="text-sm text-muted-foreground">Your role cannot see property management.</p>
  if (q.isPending) return <LoadingRows rows={4} />
  if (q.isError) return <ErrorState title="Property management could not be loaded" description={q.error.message} retry={() => void q.refetch()} />

  async function run(action: () => Promise<unknown>): Promise<boolean> {
    setError(null); setFields({}); setBusy(true)
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
  const total = funding.reduce((s, x) => s + (x.percentage ?? 0), 0)

  function addFunding() {
    const amount = fd.amount.trim() ? Number(fd.amount) : null
    const pct = fd.pct.trim() ? Number(fd.pct) : null
    const msg = !fd.type ? 'Choose the fund type.'
      : (amount !== null && (Number.isNaN(amount) || amount < 0)) ? 'The amount must be zero or more.'
      : (pct !== null && (Number.isNaN(pct) || pct < 0 || pct > 100)) ? 'The percentage must be between 0 and 100.'
      : total + (pct ?? 0) > 100 ? 'The funding percentages cannot add up to more than 100.' : ''
    setLocal((l) => ({ ...l, funding: msg }))
    if (msg) return
    setFunding([...funding, { fundType: fd.type, fundSource: orNull(fd.source), amount, percentage: pct }])
    setFd({ type: '', source: '', amount: '', pct: '' }); setSaved(false)
  }

  async function submit(ev: FormEvent) {
    ev.preventDefault()
    const errs = checkState(specs, f, [
      ['transferMemoSubmittedToAtp', 'transferMemoApprovedByAtp', 'The memo cannot be approved before it is submitted.'],
      ['closingTransferDate', 'recordingDate', 'The deed cannot be recorded before the closing or transfer date.'],
    ])
    setLocal((l) => ({ funding: l.funding ?? '', ...errs }))
    if (Object.keys(errs).length) return
    const body = { details: bodyFrom(specs, f), statusNotes: notes.map(({ noteDate, text: t }) => ({ noteDate, text: t })), funding }
    if (await run(() => api.save(parcelId, body))) setSaved(true)
  }
  const errors = { ...fields, ...local }
  const disabled = !canSave
  const setField = (k: string, v: string | boolean) => { setSaved(false); setF((x) => ({ ...x, [k]: v })) }

  return (
    <>
      <form onSubmit={submit} noValidate aria-label="Property management and disposition" className="max-w-5xl">
        <Section title="Property management and disposition">
          <div className="grid gap-3 sm:grid-cols-3"><Fact label="ID" value={record?.number ?? 'Assigned when saved'} /><Fact label="Parcel number" value={parcel.number} /></div>
        </Section>
        <StatusNotesGrid title="Property status notes" notes={notes} onChange={(n) => { setSaved(false); setNotes(n) }} canSave={canSave} />
        <Section title="Property management"><SpecFields specs={MGMT} state={f} onChange={setField} disabled={disabled} errors={errors} agents={agents} /></Section>
        <Section title="Disposition"><SpecFields specs={DISP} state={f} onChange={setField} disabled={disabled} errors={errors} agents={agents} /></Section>

        <Grid title="Funding distribution" caption="Funding distribution" headers={['Fund type', 'Fund source no. / name', 'Funding amount ($)', 'Percentage (%)']} rows={funding} canSave={canSave}
          cells={(r) => [r.fundType, r.fundSource ?? '—', dollars(r.amount), r.percentage === null ? '—' : `${r.percentage}%`]} rowLabel={(r) => `Remove funding ${r.fundType}`}
          onRemove={(i) => { setSaved(false); setFunding(funding.filter((_, j) => j !== i)) }} addLabel="Add funding" onAdd={addFunding} error={errors.funding}
          adding={<>
            <SelectField label="Fund type" required value={fd.type} onChange={(v) => setFd({ ...fd, type: v })} options={optionsWith(fundTypes, fd.type)} blankLabel="Select one" />
            <TextField label="Fund source no. / name" value={fd.source} onChange={(v) => setFd({ ...fd, source: v })} maxLength={200} />
            <TextField label="Funding amount" type="number" inputMode="decimal" value={fd.amount} onChange={(v) => setFd({ ...fd, amount: v })} />
            <TextField label="Funding percentage" type="number" inputMode="decimal" value={fd.pct} onChange={(v) => setFd({ ...fd, pct: v })} />
          </>} />

        <FormError error={error} />
        {saved && <p role="status" className="mb-2 text-sm text-ok">Property management saved.</p>}
        <div className="flex gap-2">
          {canSave && <button type="submit" disabled={busy} className={buttonClassName('default')}>{record ? 'Save changes' : 'Save property management'}</button>}
          {canRemove && <button type="button" disabled={busy} className={buttonClassName('outline')} onClick={() => void run(() => api.remove(parcelId)).then((ok) => { if (ok) setSaved(false) })}>Remove property management record</button>}
          {!canSave && <p className="text-sm text-muted-foreground">{record ? 'Your role can view this record but not change it.' : 'Nothing has been recorded yet.'}</p>}
        </div>
      </form>
      {record && <div className="mt-6"><AttachmentsPanel entityType="ParcelProperty" entityId={record.id} page="parcelproperty" title={`Attachments for ${record.number}`} /></div>}
    </>
  )
}

// ── Relocation ───────────────────────────────────────────────────────────

const RELOCATION_DATES: FieldSpec[] = [
  date('ginMailedDate', 'GIN mailed date'), date('ginDeliveredDate', 'GIN delivered date'), date('initialInterview', 'Initial interview'), date('certificateOfEligibility', 'Certificate of eligibility'),
  date('noticeOfEligibility', 'Notice of eligibility'), date('deliveryOfNoticeOfEligibility', 'Delivery of notice of eligibility date'), date('notice90Day', '90 day notice'),
  date('deliveryOf90DayNotice', 'Delivery of 90 day notice'), date('vacateEligibleDate', 'Vacate eligible date'), date('notice30DayVacate', '30 day vacate notice'),
  date('notice30DayVacateDelivered', '30 day vacate notice delivered'), flag('demolitionRequired', 'Demolition required'), date('targetVacateDate', 'Target vacate date'),
  date('actualVacateDate', 'Actual vacate date'), flag('ineligibleForRelocation', 'Ineligible for relocation?'), date('movePlan', 'Move plan'),
  date('certificationOfInventory', 'Certification of inventory'), date('benefitsExpirationLetterDate', 'Benefits expiration letter date'), date('benefitExpiration', 'Benefit expiration'),
  date('releaseOfProperty', 'Release of property'), date('certificateOfCompletion', 'Certificate of completion'), date('fileCloseOutSubmitted', 'File close out submitted'),
  date('relocationComplete', 'Relocation complete'), date('relocationComplete1', 'Relocation complete 1'),
]
const RELOCATION_ORDER: [string, string, string][] = [
  ['ginMailedDate', 'ginDeliveredDate', 'The GIN cannot be delivered before it is mailed.'],
  ['initialInterview', 'actualVacateDate', 'The displacee cannot vacate before the initial interview.'],
  ['notice30DayVacate', 'notice30DayVacateDelivered', 'The notice cannot be delivered before it is issued.'],
]
const BLANK_CONTACT = { name: '', type: '', contact: '', email: '', address: '', primary: false }
const BLANK_ELIG = { type: '', limit: '', submitted: '', approved: '' }
const BLANK_LOG = { at: '', mode: '', by: '', persons: '', notes: '' }

/** Relocation (Phase 1.5, ADR 0020): many per parcel, each for one displacee, with their contacts, eligibility, contact log and the relocation milestones. */
export function RelocationTab({ parcel, editable }: { parcel: Parcel; editable: boolean }) {
  const parcelId = parcel.id
  const { manager } = useAuth()
  const api = relocationsApi(manager.api)
  const queryClient = useQueryClient()
  const key = ['parcel-tabs', parcelId, 'relocations']
  const listQ = useQuery({ queryKey: key, queryFn: () => api.list(parcelId), enabled: manager.can('parcelrelocation') })
  const agentList = useQuery({ queryKey: ['library', 'agents'], queryFn: () => libraryApi(manager.api).agents(), retry: false })
  const agents = useAgentNames()
  const teamNames = useTeamNames(parcelId)
  const statuses = useLibraryValues('displacee_status')
  const interests = useLibraryValues('displacee_interest')
  const types = useLibraryValues('relocation_type')
  const eligTypes = useLibraryValues('eligibility_type')
  const modes = useLibraryValues('mode_of_contact')
  const HEAD: FieldSpec[] = [
    text('relocationNumber', 'Relocation number', 100, true), list('assignedTo', 'Assigned to', teamNames), text('displaceeName', 'Displacee name', 200, true), flag('limitedEnglishProficiency', 'Limited English proficiency'),
    list('displaceeStatus', 'Displacee status', statuses), list('displaceeInterest', 'Displacee interest', interests), text('displaceeMailingAddress', 'Displacee mailing address', 1000),
    list('relocationType', 'Relocation type', types, true), text('propertySiteAddress', 'Property site address', 1000),
  ]
  const specs = [...HEAD, ...RELOCATION_DATES]
  const [editing, setEditing] = useState<Relocation | null>(null)
  const [f, setF] = useState<FormState>(() => blankState(specs))
  const [notes, setNotes] = useState<StatusNote[]>([])
  const [agentRows, setAgentRows] = useState<RelocationAgent[]>([])
  const [contacts, setContacts] = useState<RelocationContact[]>([])
  const [eligibilities, setEligibilities] = useState<RelocationEligibility[]>([])
  const [log, setLog] = useState<RelocationContactLog[]>([])
  const [ag, setAg] = useState('')
  const [ct, setCt] = useState(BLANK_CONTACT)
  const [el, setEl] = useState(BLANK_ELIG)
  const [lg, setLg] = useState(BLANK_LOG)
  const [local, setLocal] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const canAdd = editable && manager.can('parcelrelocation', 'create')
  const canEdit = editable && manager.can('parcelrelocation', 'update')
  const canRemove = editable && manager.can('parcelrelocation', 'delete')
  const err = (k: string, m: string) => setLocal((l) => ({ ...l, [k]: m }))

  if (!manager.can('parcelrelocation')) return <p className="text-sm text-muted-foreground">Your role cannot see relocations.</p>
  if (listQ.isPending) return <LoadingRows rows={3} />
  if (listQ.isError) return <ErrorState title="The relocations could not be loaded" description={listQ.error.message} retry={() => void listQ.refetch()} />

  function reset() {
    setEditing(null); setF(blankState(specs)); setNotes([]); setAgentRows([]); setContacts([]); setEligibilities([]); setLog([])
    setAg(''); setCt(BLANK_CONTACT); setEl(BLANK_ELIG); setLg(BLANK_LOG); setLocal({}); setError(null); setFields({})
  }
  function begin(r: Relocation) {
    reset()
    setEditing(r); setF(stateFrom(specs, r.details)); setNotes(r.statusNotes); setAgentRows(r.agents); setContacts(r.contacts); setEligibilities(r.eligibilities); setLog(r.contactLog)
  }

  async function run(action: () => Promise<unknown>): Promise<boolean> {
    setError(null); setFields({}); setBusy(true)
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

  function addAgent() {
    if (!ag) return err('agent', 'Choose the agent.')
    const lib = agentList.data?.find((a) => a.name === ag)
    setAgentRows([...agentRows, { agentName: ag, agentType: lib?.agentType ?? null, company: lib?.company ?? null, phone: lib?.phone ?? null, email: lib?.email ?? null }])
    setAg(''); err('agent', '')
  }
  function addContact() {
    if (!ct.name.trim()) return err('contact', 'Enter the name of the contact.')
    if (ct.primary && contacts.some((c) => c.isPrimary)) return err('contact', 'Only one contact can be the primary contact.')
    setContacts([...contacts, { name: ct.name.trim(), contactType: orNull(ct.type), contact: orNull(ct.contact), email: orNull(ct.email), address: orNull(ct.address), isPrimary: ct.primary }])
    setCt(BLANK_CONTACT); err('contact', '')
  }
  function addEligibility() {
    if (!el.type) return err('eligibility', 'Choose the eligibility type.')
    if (el.limit.trim() && (Number.isNaN(Number(el.limit)) || Number(el.limit) < 0)) return err('eligibility', 'The amount must be zero or more.')
    if (el.submitted && el.approved && el.approved < el.submitted) return err('eligibility', 'Eligibility cannot be approved before it is submitted.')
    setEligibilities([...eligibilities, { eligibilityType: el.type, claimLimit: numOrNull(el.limit), submittedForApproval: orNull(el.submitted), approved: orNull(el.approved) }])
    setEl(BLANK_ELIG); err('eligibility', '')
  }
  function addLog() {
    if (!lg.at || Number.isNaN(Date.parse(lg.at))) return err('log', 'Enter the date and time of the contact.')
    setLog([{ contactedAt: new Date(lg.at).toISOString(), mode: orNull(lg.mode), contactedBy: orNull(lg.by), contactPersons: orNull(lg.persons), notes: orNull(lg.notes) }, ...log])
    setLg(BLANK_LOG); err('log', '')
  }

  async function submit(ev: FormEvent) {
    ev.preventDefault()
    const errs = checkState(specs, f, RELOCATION_ORDER)
    setLocal(errs)
    if (Object.keys(errs).length) return
    const body = {
      details: bodyFrom(specs, f), statusNotes: notes.map(({ noteDate, text: t }) => ({ noteDate, text: t })), agents: agentRows.map(({ agentName }) => ({ agentName })),
      contacts, eligibilities, contactLog: log,
    }
    if (await run(() => (editing ? api.update(parcelId, editing.id, body) : api.add(parcelId, body)))) reset()
  }
  const errors = { ...fields, ...local }
  const setField = (k: string, v: string | boolean) => setF((x) => ({ ...x, [k]: v }))
  const label = (r: Relocation) => String(r.details.relocationNumber)

  return (
    <>
      <fieldset aria-label="Relocations" className="mb-6 rounded-lg border bg-card p-4">
        <legend className="px-1 text-sm font-semibold">Relocations</legend>
        {listQ.data.length === 0 ? (
          <EmptyState title="No relocations yet" description="Record each displacee's relocation on this parcel." />
        ) : (
          <DataTable caption="Relocations" headers={['Relocation number', 'Displacee', 'Relocation type', 'Displacee status', 'Target vacate date', ...(canEdit || canRemove ? ['Actions'] : [])]}>
            {listQ.data.map((r) => (
              <tr key={r.id}>
                <td className="px-3 py-2 font-medium">{label(r)}</td>
                <td className="px-3 py-2">{String(r.details.displaceeName)}</td>
                <td className="px-3 py-2">{String(r.details.relocationType)}</td>
                <td className="px-3 py-2">{r.details.displaceeStatus ? String(r.details.displaceeStatus) : '—'}</td>
                <td className="px-3 py-2">{day(r.details.targetVacateDate as string | null)}</td>
                {(canEdit || canRemove) && (
                  <td className="px-3 py-2">
                    <span className="inline-flex gap-1">
                      {canEdit && <button type="button" aria-label={`Edit relocation ${label(r)}`} className={buttonClassName('ghost', 'sm')} onClick={() => begin(r)}><Pencil size={14} aria-hidden /></button>}
                      {canRemove && (
                        <button type="button" aria-label={`Remove relocation ${label(r)}`} disabled={busy} className={buttonClassName('ghost', 'sm')}
                          onClick={() => void run(() => api.remove(parcelId, r.id)).then((ok) => { if (ok && editing?.id === r.id) reset() })}><Trash2 size={14} aria-hidden /></button>
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
        <form onSubmit={submit} noValidate aria-label={editing ? 'Change relocation' : 'Add a relocation'} className="max-w-5xl">
          <h3 className="mb-3 text-sm font-semibold">{editing ? `Change relocation ${label(editing)}` : 'Add a relocation'}</h3>
          <Section title="Relocation">
            <div className="mb-3 grid gap-3 sm:grid-cols-3"><Fact label="Parcel number" value={parcel.number} /><Fact label="Vested owner name" value={editing?.vestedOwnerName} /></div>
            <SpecFields specs={HEAD} state={f} onChange={setField} errors={errors} agents={agents} columns="sm:grid-cols-2 lg:grid-cols-3" />
          </Section>
          <Section title="Relocation milestones"><SpecFields specs={RELOCATION_DATES} state={f} onChange={setField} errors={errors} agents={agents} columns="sm:grid-cols-2 lg:grid-cols-3" /></Section>
          <StatusNotesGrid title="Relocation status notes" notes={notes} onChange={setNotes} canSave />

          <Grid title="Agents" caption="Agents" headers={['Agent name', 'Agent type', 'Company', 'Phone number', 'Email address']} rows={agentRows} canSave
            cells={(r) => [r.agentName, r.agentType ?? '—', r.company ?? '—', r.phone ?? '—', r.email ?? '—']} rowLabel={(r) => `Remove agent ${r.agentName}`}
            onRemove={(i) => setAgentRows(agentRows.filter((_, j) => j !== i))} addLabel="Add agent" onAdd={addAgent} error={errors.agent}
            adding={<SelectField label="Agent" value={ag} onChange={setAg} options={optionsWith(agents, ag)} blankLabel="Select one" />} />

          <Grid title="Relocation contacts" caption="Relocation contacts" headers={['Name', 'Type', 'Contact', 'Email', 'Address', 'Is primary']} rows={contacts} canSave
            cells={(r) => [r.name, r.contactType ?? '—', r.contact ?? '—', r.email ?? '—', r.address ?? '—', r.isPrimary ? 'Yes' : 'No']} rowLabel={(r) => `Remove contact ${r.name}`}
            onRemove={(i) => setContacts(contacts.filter((_, j) => j !== i))} addLabel="Add relocation contact" onAdd={addContact} error={errors.contact}
            adding={<>
              <TextField label="Contact name" value={ct.name} onChange={(v) => setCt({ ...ct, name: v })} maxLength={200} />
              <TextField label="Contact type" value={ct.type} onChange={(v) => setCt({ ...ct, type: v })} maxLength={100} />
              <TextField label="Contact details" value={ct.contact} onChange={(v) => setCt({ ...ct, contact: v })} maxLength={200} />
              <TextField label="Contact email" value={ct.email} onChange={(v) => setCt({ ...ct, email: v })} maxLength={200} />
              <TextField label="Contact address" value={ct.address} onChange={(v) => setCt({ ...ct, address: v })} maxLength={500} />
              <CheckboxField label="Primary contact" checked={ct.primary} onChange={(v) => setCt({ ...ct, primary: v })} />
            </>} />

          <Grid title="Relocation eligibility" caption="Relocation eligibility" headers={['Eligibility type', 'Claim limit ($)', 'Submitted for approval', 'Approved']} rows={eligibilities} canSave
            cells={(r) => [r.eligibilityType, dollars(r.claimLimit), day(r.submittedForApproval), day(r.approved)]} rowLabel={(r) => `Remove eligibility ${r.eligibilityType}`}
            onRemove={(i) => setEligibilities(eligibilities.filter((_, j) => j !== i))} addLabel="Add eligibility" onAdd={addEligibility} error={errors.eligibility}
            adding={<>
              <SelectField label="Eligibility type" value={el.type} onChange={(v) => setEl({ ...el, type: v })} options={optionsWith(eligTypes, el.type)} blankLabel="Select one" />
              <TextField label="Claim limit" type="number" inputMode="decimal" value={el.limit} onChange={(v) => setEl({ ...el, limit: v })} />
              <TextField label="Eligibility submitted for approval" type="date" value={el.submitted} onChange={(v) => setEl({ ...el, submitted: v })} />
              <TextField label="Eligibility approved" type="date" value={el.approved} onChange={(v) => setEl({ ...el, approved: v })} />
            </>} />

          <Grid title="Contact log" caption="Contact log" headers={['Date/time of contact', 'Mode of contact', 'Contacted by', 'Contact person(s)', 'Discussion notes']} rows={log} canSave
            cells={(r) => [new Date(r.contactedAt).toLocaleString(), r.mode ?? '—', r.contactedBy ?? '—', r.contactPersons ?? '—', r.notes ?? '—']} rowLabel={(_, i) => `Remove contact log entry ${i + 1}`}
            onRemove={(i) => setLog(log.filter((_, j) => j !== i))} addLabel="Add contact log entry" onAdd={addLog} error={errors.log}
            adding={<>
              <TextField label="Log date and time" type="datetime-local" value={lg.at} onChange={(v) => setLg({ ...lg, at: v })} />
              <SelectField label="Log mode of contact" value={lg.mode} onChange={(v) => setLg({ ...lg, mode: v })} options={optionsWith(modes, lg.mode)} blankLabel="Select one" />
              <SelectField label="Log contacted by" value={lg.by} onChange={(v) => setLg({ ...lg, by: v })} options={optionsWith(agents, lg.by)} blankLabel="Select one" />
              <TextField label="Log contact persons" value={lg.persons} onChange={(v) => setLg({ ...lg, persons: v })} maxLength={500} />
              <TextField label="Log discussion notes" value={lg.notes} onChange={(v) => setLg({ ...lg, notes: v })} maxLength={4000} />
            </>} />

          <FormError error={error} />
          <div className="flex gap-2">
            <button type="submit" disabled={busy} className={buttonClassName('default')}>{editing ? 'Save changes' : 'Add relocation'}</button>
            {editing && <button type="button" className={buttonClassName('outline')} onClick={reset}>Cancel</button>}
          </div>
        </form>
      )}
      {editing && <div className="mt-6"><AttachmentsPanel entityType="ParcelRelocation" entityId={editing.id} page="parcelrelocation" title={`Attachments for relocation ${label(editing)}`} /></div>}
    </>
  )
}
