import type { ApiClient } from './client'

/** One record found by an across-parcel search, with the parcel it belongs to. */
export interface OverviewBase {
  id: string
  parcelId: string
  parcelNumber: string
  address: string | null
  ownerName: string | null
  /** The record's own ID (APR-..., NEG-..., TC-..., ENC-..., or the easement number). */
  number: string
}
export interface AppraisalOverview extends OverviewBase {
  appraisalType: string | null
  appraiser: string | null
  receivedDate: string | null
  appraisalTotal: number
  approvedJustCompensation: number | null
}
export interface NegotiationOverview extends OverviewBase { assignedTo: string | null; totalAgreementAmount: number | null; jatResponse: string | null; puaExecutedDate: string | null }
export interface TitleClosingOverview extends OverviewBase { titleCompany: string | null; titleClosingAgent: string | null; anticipatedClosingDate: string | null; actualClosingDate: string | null }
export interface EncumbranceOverview extends OverviewBase { encumbranceType: string; documentTitle: string; cureStatus: string | null }
export interface EasementOverview extends OverviewBase { easementType: string | null; startDate: string | null; endDate: string | null }

export interface RelocationOverview extends OverviewBase {
  displaceeName: string
  relocationType: string
  stage: string
  assignedTo: string | null
  targetVacateDate: string | null
  claimDeadline: string | null
  openPayments: number
}

/** Searches by parcel number, address, owner name or the record's own ID; with no text, the most recent records. */
export const overviewApi = (api: ApiClient) => {
  const find = <T,>(path: string) => (search: string) => api.get<T[]>(`/api/v1/overview/${path}`, { search: search || undefined })
  return {
    appraisals: find<AppraisalOverview>('appraisals'),
    negotiations: find<NegotiationOverview>('negotiations'),
    titleClosings: find<TitleClosingOverview>('title-closings'),
    encumbrances: find<EncumbranceOverview>('encumbrances'),
    easements: find<EasementOverview>('easements'),
    relocations: find<RelocationOverview>('relocations'),
  }
}
