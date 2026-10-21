import type { ApiClient } from './client'

// Rights of entry, plan reviews, staff reports and certificates, acquisition requests and the project plan
// (ROW-077, 080, 239, 240, 241).

export const ROE_STATUSES = ['Requested', 'Granted', 'Denied', 'Revoked'] as const
export type RoeStatus = (typeof ROE_STATUSES)[number]
/** What the screen shows: a granted right whose end date has passed reads "Expired". */
export type RoeEffective = RoeStatus | 'Expired'

export interface RightOfEntry {
  id: string
  parcelId: string
  status: RoeStatus
  effectiveStatus: RoeEffective
  requestedOn: string | null
  grantedOn: string | null
  expiresOn: string | null
  grantedBy: string | null
  purpose: string | null
  /** Special instructions for the entry. */
  notes: string | null
  letterMailedDate: string | null
  letterDeliveredDate: string | null
  sentToAtpDate: string | null
  updatedAt: string
}

export interface RightOfEntryInput {
  status: RoeStatus
  requestedOn: string | null
  grantedOn: string | null
  expiresOn: string | null
  grantedBy: string | null
  purpose: string | null
  notes: string | null
  letterMailedDate: string | null
  letterDeliveredDate: string | null
  sentToAtpDate: string | null
}

export interface RoeReportRow {
  id: string
  parcelId: string
  parcelNumber: string
  parcelAddress: string | null
  projectId: string
  projectNumber: string
  status: RoeStatus
  effectiveStatus: RoeEffective
  requestedOn: string | null
  grantedOn: string | null
  expiresOn: string | null
  daysUntilExpiry: number | null
  grantedBy: string | null
  purpose: string | null
}

export interface RoeReportFilter {
  projectId?: string
  status?: string
  expiringWithinDays?: number | null
  page?: number
  pageSize?: number
}

export interface Paged<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export const REVIEW_DISCIPLINES = ['Engineer', 'Surveyor'] as const
export type ReviewDiscipline = (typeof REVIEW_DISCIPLINES)[number]
export const REVIEW_OUTCOMES = ['Approved', 'ChangesRequired'] as const
export type ReviewOutcome = (typeof REVIEW_OUTCOMES)[number]
export const OUTCOME_LABEL: Record<ReviewOutcome, string> = { Approved: 'Approved', ChangesRequired: 'Changes required' }

export interface PlanReview {
  id: string
  discipline: ReviewDiscipline
  outcome: ReviewOutcome
  reviewedOn: string
  reviewerName: string
  notes: string | null
  createdAt: string
}

export interface PlanReviewStatus {
  gateEnabled: boolean
  stepKey: string
  missing: ReviewDiscipline[]
  satisfied: boolean
  reviews: PlanReview[]
}

export interface PlanReviewInput {
  discipline: ReviewDiscipline
  outcome: ReviewOutcome
  reviewedOn: string
  reviewerName: string
  notes: string | null
}

export interface PlanReviewGateSetting {
  enabled: boolean
  stepKey: string
}

export type ReportKind = 'StaffReport' | 'CertificateOfNecessity'
export const REPORT_KIND_LABEL: Record<ReportKind, string> = { StaffReport: 'Staff report', CertificateOfNecessity: 'Certificate of necessity' }
export type ReportStatus = 'Draft' | 'Submitted' | 'Approved' | 'Rejected'

export interface StaffReport {
  id: string
  parcelId: string
  kind: ReportKind
  status: ReportStatus
  title: string
  body: string
  submittedAt: string | null
  submittedByMe: boolean
  decidedAt: string | null
  decisionNote: string | null
  canEdit: boolean
  updatedAt: string
}

export type RequestStatus = 'Submitted' | 'Approved' | 'Rejected' | 'Withdrawn'
export const REQUEST_STATUSES: readonly RequestStatus[] = ['Submitted', 'Approved', 'Rejected', 'Withdrawn']
export type RequesterType = 'Internal' | 'External'

export interface AcquisitionRequest {
  id: string
  projectId: string | null
  projectNumber: string | null
  requesterType: RequesterType
  requesterName: string
  requesterEmail: string | null
  property: string
  desiredParameters: string | null
  estimatedAmount: number | null
  status: RequestStatus
  submittedByMe: boolean
  createdAt: string
  decidedAt: string | null
  decisionNote: string | null
}

export interface AcquisitionRequestInput {
  projectId: string | null
  requesterType: RequesterType
  requesterName: string
  requesterEmail: string | null
  property: string
  desiredParameters: string | null
  estimatedAmount: number | null
}

export interface PlanParcelRow {
  id: string
  number: string
  address: string | null
  areaSqFt: number
  statusName: string
  stepLabel: string
  nextMilestone: string | null
  nextDueOn: string | null
  isOverdue: boolean
  plannedCloseOn: string | null
  isClosed: boolean
  milestonesDone: number
  milestonesTotal: number
}

export interface PlanBudget {
  totalBudget: number
  currentEstimate: number | null
  authorizedTotal: number
  variance: number | null
  overBudget: boolean
}

export interface AcquisitionPlan {
  projectId: string
  projectNumber: string
  projectName: string
  today: string
  plan: {
    parcelCount: number
    areaSqFt: number
    closedCount: number
    overdueCount: number
    firstDueOn: string | null
    lastPlannedCloseOn: string | null
    budget: PlanBudget
    parcels: PlanParcelRow[]
  }
}

const qs = (params: Record<string, string | number | null | undefined>) => {
  const q = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== '') q.set(k, String(v))
  const s = q.toString()
  return s ? `?${s}` : ''
}

export const acquisitionApi = (api: ApiClient) => ({
  roes: (parcelId: string) => api.get<RightOfEntry[]>(`/api/v1/parcels/${parcelId}/rights-of-entry`),
  addRoe: (parcelId: string, body: RightOfEntryInput) => api.post<RightOfEntry>(`/api/v1/parcels/${parcelId}/rights-of-entry`, body),
  updateRoe: (parcelId: string, id: string, body: RightOfEntryInput) => api.put<RightOfEntry>(`/api/v1/parcels/${parcelId}/rights-of-entry/${id}`, body),
  removeRoe: (parcelId: string, id: string) => api.delete(`/api/v1/parcels/${parcelId}/rights-of-entry/${id}`),
  roeReport: (f: RoeReportFilter) =>
    api.get<Paged<RoeReportRow>>(`/api/v1/rights-of-entry${qs({ projectId: f.projectId, status: f.status, expiringWithinDays: f.expiringWithinDays, page: f.page, pageSize: f.pageSize })}`),

  planReviews: (parcelId: string) => api.get<PlanReviewStatus>(`/api/v1/parcels/${parcelId}/plan-reviews`),
  addPlanReview: (parcelId: string, body: PlanReviewInput) => api.post<PlanReviewStatus>(`/api/v1/parcels/${parcelId}/plan-reviews`, body),
  removePlanReview: (parcelId: string, id: string) => api.delete(`/api/v1/parcels/${parcelId}/plan-reviews/${id}`),
  gate: () => api.get<PlanReviewGateSetting>('/api/v1/agency/plan-review-gate'),
  setGate: (enabled: boolean) => api.put<PlanReviewGateSetting>('/api/v1/agency/plan-review-gate', { enabled }),

  reports: (parcelId: string) => api.get<StaffReport[]>(`/api/v1/parcels/${parcelId}/staff-reports`),
  createReport: (parcelId: string, body: { kind: ReportKind; title: string; body: string }) => api.post<StaffReport>(`/api/v1/parcels/${parcelId}/staff-reports`, body),
  updateReport: (parcelId: string, id: string, body: { title: string; body: string }) => api.put<StaffReport>(`/api/v1/parcels/${parcelId}/staff-reports/${id}`, body),
  submitReport: (parcelId: string, id: string) => api.post<StaffReport>(`/api/v1/parcels/${parcelId}/staff-reports/${id}/submit`, {}),
  approveReport: (parcelId: string, id: string, note: string | null) => api.post<StaffReport>(`/api/v1/parcels/${parcelId}/staff-reports/${id}/approve`, { note }),
  rejectReport: (parcelId: string, id: string, note: string) => api.post<StaffReport>(`/api/v1/parcels/${parcelId}/staff-reports/${id}/reject`, { note }),
  deleteReport: (parcelId: string, id: string) => api.delete(`/api/v1/parcels/${parcelId}/staff-reports/${id}`),

  requests: (status: string, page: number, pageSize: number) =>
    api.get<Paged<AcquisitionRequest>>(`/api/v1/acquisition-requests${qs({ status, page, pageSize })}`),
  submitRequest: (body: AcquisitionRequestInput) => api.post<AcquisitionRequest>('/api/v1/acquisition-requests', body),
  approveRequest: (id: string, note: string | null) => api.post<AcquisitionRequest>(`/api/v1/acquisition-requests/${id}/approve`, { note }),
  rejectRequest: (id: string, note: string) => api.post<AcquisitionRequest>(`/api/v1/acquisition-requests/${id}/reject`, { note }),
  withdrawRequest: (id: string) => api.post<AcquisitionRequest>(`/api/v1/acquisition-requests/${id}/withdraw`, {}),

  plan: (projectId: string) => api.get<AcquisitionPlan>(`/api/v1/projects/${projectId}/acquisition-plan`),
})
