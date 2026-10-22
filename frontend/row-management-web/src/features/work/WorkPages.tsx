import { useAuth } from '@/auth/auth-context'
import {
  overviewApi, type AppraisalOverview, type EasementOverview, type EncumbranceOverview, type NegotiationOverview, type RelocationOverview, type TitleClosingOverview,
} from '@/api/overview'
import { dateText } from '../projects/form-kit'
import { AppraisalTab } from '../parcels/AppraisalTab'
import { NegotiationTab } from '../parcels/NegotiationTab'
import { EasementsTab } from '../parcels/ParcelTabs'
import { EncumbrancesTab } from '../parcels/TitleTabs'
import { TitleClosingTab } from '../parcels/TitleClosingTab'
import { RelocationTab } from '../parcels/LifecycleTabs'
import { ParcelWorkPage, type WorkKind } from './ParcelWorkPage'

const day = (d: string | null) => (d ? dateText(d) : '—')
const dollars = (n: number | null) => (n === null ? '—' : n.toLocaleString('en-US', { style: 'currency', currency: 'USD' }))
const text = (s: string | null) => s ?? '—'

/** Appraisal: find an appraisal by parcel, owner or ID and add to or update it, or start one for a parcel. */
export function AppraisalWorkPage() {
  const { manager } = useAuth()
  const api = overviewApi(manager.api)
  const kinds: WorkKind<AppraisalOverview>[] = [{
    key: 'appraisals', label: 'Appraisals', page: 'parcelappraisal', fetch: api.appraisals,
    headers: ['Type', 'Appraiser', 'Received', 'Appraisal total', 'Approved just compensation'],
    cells: (r) => [text(r.appraisalType), text(r.appraiser), day(r.receivedDate), dollars(r.appraisalTotal), dollars(r.approvedJustCompensation)],
    tab: (p, editable) => <AppraisalTab parcelId={p.id} editable={editable} />,
  }]
  return <ParcelWorkPage title="Appraisal" subtitle="Find a parcel's appraisal by parcel, owner, address or ID, then add to it or update it here." kinds={kinds as unknown as WorkKind[]} />
}

/** Negotiation & Closing: the negotiation record and the title and closing record of a parcel. */
export function NegotiationClosingPage() {
  const { manager } = useAuth()
  const api = overviewApi(manager.api)
  const negotiation: WorkKind<NegotiationOverview> = {
    key: 'negotiations', label: 'Negotiations', page: 'parcelnegotiation', fetch: api.negotiations,
    headers: ['Assigned to', 'Total agreement amount', 'JAT response', 'PUA executed'],
    cells: (r) => [text(r.assignedTo), dollars(r.totalAgreementAmount), text(r.jatResponse), day(r.puaExecutedDate)],
    tab: (p, editable) => <NegotiationTab parcel={p} editable={editable} />,
  }
  const closing: WorkKind<TitleClosingOverview> = {
    key: 'closings', label: 'Title and closing', page: 'titleclosing', fetch: api.titleClosings,
    headers: ['Title company', 'Closing agent', 'Anticipated closing', 'Actual closing'],
    cells: (r) => [text(r.titleCompany), text(r.titleClosingAgent), day(r.anticipatedClosingDate), day(r.actualClosingDate)],
    tab: (p, editable) => <TitleClosingTab parcelId={p.id} editable={editable} />,
  }
  return <ParcelWorkPage title="Negotiation & Closing" subtitle="Find a parcel's negotiation or closing record by parcel, owner, address or ID, then add to it or update it here." kinds={[negotiation, closing] as unknown as WorkKind[]} />
}

/** Title & Survey: the title encumbrances and easements of a parcel. */
export function TitleSurveyPage() {
  const { manager } = useAuth()
  const api = overviewApi(manager.api)
  const encumbrances: WorkKind<EncumbranceOverview> = {
    key: 'encumbrances', label: 'Title encumbrances', page: 'titleencumbrances', fetch: api.encumbrances,
    headers: ['Type', 'Document title', 'Cure status'],
    cells: (r) => [r.encumbranceType, r.documentTitle, text(r.cureStatus)],
    tab: (p, editable) => <EncumbrancesTab parcelId={p.id} editable={editable} />,
  }
  const easements: WorkKind<EasementOverview> = {
    key: 'easements', label: 'Easements', page: 'easements', fetch: api.easements,
    headers: ['Type', 'Start', 'End'],
    cells: (r) => [text(r.easementType), day(r.startDate), day(r.endDate)],
    tab: (p, editable) => <EasementsTab parcelId={p.id} editable={editable} />,
  }
  return <ParcelWorkPage title="Title & Survey" subtitle="Find a parcel's title encumbrances or easements by parcel, owner, address or ID, then add to them or update them here." kinds={[encumbrances, easements] as unknown as WorkKind[]} />
}

/** Relocations (ADR 0026): find a displacee's relocation case by parcel, owner, relocation number or displacee, then work it through right here. */
export function RelocationWorkPage() {
  const { manager } = useAuth()
  const api = overviewApi(manager.api)
  const relocations: WorkKind<RelocationOverview> = {
    key: 'relocations', label: 'Relocations', page: 'parcelrelocation', fetch: api.relocations,
    headers: ['Displacee', 'Stage', 'Assigned to', 'Target vacate', 'Claim deadline', 'Open payments'],
    cells: (r) => [r.displaceeName, r.stage, text(r.assignedTo), day(r.targetVacateDate), day(r.claimDeadline), String(r.openPayments)],
    tab: (p, editable) => <RelocationTab parcel={p} editable={editable} />,
  }
  return <ParcelWorkPage title="Relocations" subtitle="Find a displacee's relocation by parcel, owner, address, relocation number or displacee, then work the case here." kinds={[relocations] as unknown as WorkKind[]} />
}
