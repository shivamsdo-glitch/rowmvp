import type { ApiClient } from './client'
import type { Paged } from './projects'

export const ACQUISITION_TYPES = ['Standard', 'Donation', 'DeveloperConveyance', 'Early'] as const
export type AcquisitionType = (typeof ACQUISITION_TYPES)[number]
export const ACQUISITION_LABEL: Record<AcquisitionType, string> = {
  Standard: 'Standard',
  Donation: 'Donation',
  DeveloperConveyance: 'Developer conveyance',
  Early: 'Early acquisition',
}

export const INTEREST_TYPES = ['FeeTaking', 'Remainder', 'PermanentEasement', 'UtilityEasement', 'TemporaryEasement'] as const
export type InterestType = (typeof INTEREST_TYPES)[number]
export const INTEREST_LABEL: Record<InterestType, string> = {
  FeeTaking: 'Fee taking',
  Remainder: 'Remainder',
  PermanentEasement: 'Permanent easement',
  UtilityEasement: 'Utility easement',
  TemporaryEasement: 'Temporary easement',
}

export const NOTICE_METHODS = ['CertifiedMail', 'FirstClassMail', 'HandDelivery', 'Email', 'Posting'] as const
export type NoticeMethod = (typeof NOTICE_METHODS)[number]
export const NOTICE_LABEL: Record<NoticeMethod, string> = {
  CertifiedMail: 'Certified mail',
  FirstClassMail: 'First-class mail',
  HandDelivery: 'Hand delivery',
  Email: 'Email',
  Posting: 'Posting',
}

export const CUSTOM_FIELD_TYPES = ['Text', 'Number', 'Date', 'Boolean', 'List'] as const
export type CustomFieldType = (typeof CUSTOM_FIELD_TYPES)[number]

export type ParcelState = 'Active' | 'Split' | 'Combined'

export interface ParcelStatus {
  id: string
  name: string
  color: string
  sortOrder: number
  isActive: boolean
  isInitial: boolean
}

export interface ParcelGroup {
  id: string
  projectId: string
  name: string
  description: string | null
  parcelCount: number
}

export interface ParentTract {
  id: string
  projectId: string
  number: string
  description: string | null
  taxParcelNumbers: string[]
  parcelCount: number
}

export interface ParcelSummary {
  id: string
  projectId: string
  projectNumber: string
  number: string
  address: string | null
  areaSqFt: number
  propertyType: string | null
  statusId: string
  statusName: string
  statusColor: string
  groupId: string | null
  groupName: string | null
  parentTractId: string | null
  parentTractNumber: string | null
  acquisitionType: AcquisitionType
  currentStepKey: string
  isRemnant: boolean
  state: ParcelState
}

export interface ParcelInterest { id: string; type: InterestType; areaSqFt: number; notes: string | null }
export interface ParcelMilestone {
  id: string
  name: string
  dueOn: string | null
  completedOn: string | null
  notes: string | null
  isStandard: boolean
  isOverdue: boolean
}
export interface ParcelSiteVisit { id: string; visitedOn: string; notes: string | null; ownerContactConfirmed: boolean }
export interface ParcelStepEvent {
  id: string
  fromStepKey: string | null
  toStepKey: string
  isOverride: boolean
  reason: string | null
  at: string
  by: string
}

/** NotStarted | Pending | DueSoon | Overdue | Met | MetLate */
export type DeadlineStatus = 'NotStarted' | 'Pending' | 'DueSoon' | 'Overdue' | 'Met' | 'MetLate'
export interface Deadline {
  ruleId: string
  name: string
  triggerMilestone: string
  targetMilestone: string
  days: number
  triggerDate: string | null
  dueOn: string | null
  status: DeadlineStatus
  daysRemaining: number | null
}

export interface WorkflowStep { key: string; label: string; order: number; requiresAcquisitionGate: boolean }
export interface Workflow { acquisitionType: AcquisitionType; steps: WorkflowStep[] }
export interface WorkflowState {
  steps: WorkflowStep[]
  currentKey: string
  nextKey: string | null
  acquisitionGateOpen: boolean
  canOverride: boolean
}

export interface Parcel {
  id: string
  projectId: string
  projectNumber: string
  projectName: string
  number: string
  legalDescription: string | null
  address: string | null
  areaSqFt: number
  propertyType: string | null
  statusId: string
  statusName: string
  statusColor: string
  groupId: string | null
  groupName: string | null
  parentTractId: string | null
  parentTractNumber: string | null
  taxParcelNumbers: string[]
  acquisitionType: AcquisitionType
  state: ParcelState
  supersededOn: string | null
  sourceParcelIds: string[]
  isRemnant: boolean
  customValues: Record<string, string>
  noticeIssuedOn: string | null
  noticeMethod: NoticeMethod | null
  interests: ParcelInterest[]
  milestones: ParcelMilestone[]
  siteVisits: ParcelSiteVisit[]
  deadlines: Deadline[]
  workflow: WorkflowState
  stepHistory: ParcelStepEvent[]
  updatedAt: string
  /** The IANA time zone this parcel's dates are counted in: its project's, else the agency's. */
  timeZoneId: string
  cadNumber: string | null
  acquisitionInterestType: string | null
  possessionDate: string | null
  proposedPossessionDate: string | null
  segment: string | null
  priority: string | null
  possessionType: string | null
  parcelLocation: string | null
  isActiveParcel: boolean
  /** The names of the parcel's ownerships (Ownership Details), read-only. */
  ownerName: string | null
}

export interface ParcelInput {
  projectId?: string
  number: string | null
  parentTractId: string | null
  groupId: string | null
  legalDescription: string | null
  address: string | null
  areaSqFt: number
  propertyType: string | null
  statusId: string | null
  acquisitionType: AcquisitionType
  customValues: Record<string, string | null> | null
  cadNumber?: string | null
  acquisitionInterestType?: string | null
  possessionDate?: string | null
  proposedPossessionDate?: string | null
  segment?: string | null
  priority?: string | null
  possessionType?: string | null
  parcelLocation?: string | null
  isActiveParcel?: boolean
}

export interface RollupBucket { id: string | null; name: string; color: string | null; count: number; areaSqFt: number; acres: number }
export interface ParcelRollup { totalCount: number; totalAreaSqFt: number; totalAcres: number; byStatus: RollupBucket[]; byGroup: RollupBucket[] }

export interface SplitResult { source: Parcel; parts: ParcelSummary[]; remnant: ParcelSummary | null }

export interface Reminder {
  parcelId: string
  parcelNumber: string
  projectId: string
  projectNumber: string
  ruleName: string
  targetMilestone: string
  dueOn: string
  status: 'Overdue' | 'DueSoon'
  daysRemaining: number
}

export interface CustomField {
  id: string
  entityType: string
  key: string
  label: string
  fieldType: CustomFieldType
  options: string[]
  isRequired: boolean
  isActive: boolean
  sortOrder: number
}

export interface DeadlineRule {
  id: string
  name: string
  triggerMilestone: string
  targetMilestone: string
  days: number
  isActive: boolean
  isPlaceholder: boolean
}

export interface StepGateSettings { alwaysRoles: string[]; extraOverrideRoles: string[]; assignableRoles: string[] }

export interface ParcelListFilter {
  projectId?: string
  groupId?: string
  statusId?: string
  tractId?: string
  search?: string
  page?: number
  pageSize?: number
}

export const parcelsApi = (api: ApiClient) => ({
  list: (f: ParcelListFilter) =>
    api.get<Paged<ParcelSummary>>('/api/v1/parcels', {
      projectId: f.projectId || undefined, groupId: f.groupId || undefined, statusId: f.statusId || undefined, tractId: f.tractId || undefined,
      search: f.search || undefined, page: f.page ?? 1, pageSize: f.pageSize ?? 50,
    }),
  get: (id: string) => api.get<Parcel>(`/api/v1/parcels/${id}`),
  create: (body: ParcelInput) => api.post<Parcel>('/api/v1/parcels', body),
  update: (id: string, body: ParcelInput) => api.put<Parcel>(`/api/v1/parcels/${id}`, body),
  batchUpdate: (parcelIds: string[], statusId: string | null, groupId: string | null, clearGroup: boolean) =>
    api.post<{ updated: number }>('/api/v1/parcels/batch-update', { parcelIds, statusId, groupId, clearGroup }),
  split: (id: string, parts: { areaSqFt: number }[]) => api.post<SplitResult>(`/api/v1/parcels/${id}/split`, { parts }),
  combine: (parcelIds: string[], number: string | null) => api.post<Parcel>('/api/v1/parcels/combine', { parcelIds, number }),

  addInterest: (id: string, type: InterestType, areaSqFt: number, notes: string | null) =>
    api.post<Parcel>(`/api/v1/parcels/${id}/interests`, { type, areaSqFt, notes }),
  deleteInterest: (id: string, interestId: string) => api.delete(`/api/v1/parcels/${id}/interests/${interestId}`),

  addMilestone: (id: string, name: string, dueOn: string | null, notes: string | null) =>
    api.post<Parcel>(`/api/v1/parcels/${id}/milestones`, { name, dueOn, notes }),
  updateMilestone: (id: string, milestoneId: string, dueOn: string | null, completedOn: string | null, notes: string | null) =>
    api.put<Parcel>(`/api/v1/parcels/${id}/milestones/${milestoneId}`, { dueOn, completedOn, notes }),
  deleteMilestone: (id: string, milestoneId: string) => api.delete(`/api/v1/parcels/${id}/milestones/${milestoneId}`),

  addSiteVisit: (id: string, visitedOn: string, notes: string | null, ownerContactConfirmed: boolean) =>
    api.post<Parcel>(`/api/v1/parcels/${id}/site-visits`, { visitedOn, notes, ownerContactConfirmed }),
  deleteSiteVisit: (id: string, visitId: string) => api.delete(`/api/v1/parcels/${id}/site-visits/${visitId}`),

  recordNotice: (id: string, issuedOn: string | null, method: NoticeMethod | null) =>
    api.put<Parcel>(`/api/v1/parcels/${id}/notice`, { issuedOn, method }),
  advance: (id: string, targetStepKey: string, overrideReason: string | null) =>
    api.post<Parcel>(`/api/v1/parcels/${id}/advance`, { targetStepKey, overrideReason }),

  groups: (projectId: string) => api.get<ParcelGroup[]>(`/api/v1/projects/${projectId}/groups`),
  saveGroup: (projectId: string, id: string | null, name: string, description: string | null) =>
    id ? api.put<ParcelGroup>(`/api/v1/projects/${projectId}/groups/${id}`, { name, description })
       : api.post<ParcelGroup>(`/api/v1/projects/${projectId}/groups`, { name, description }),
  deleteGroup: (projectId: string, id: string) => api.delete(`/api/v1/projects/${projectId}/groups/${id}`),
  tracts: (projectId: string) => api.get<ParentTract[]>(`/api/v1/projects/${projectId}/tracts`),
  saveTract: (projectId: string, id: string | null, number: string, description: string | null, taxParcelNumbers: string[]) =>
    id ? api.put<ParentTract>(`/api/v1/projects/${projectId}/tracts/${id}`, { number, description, taxParcelNumbers })
       : api.post<ParentTract>(`/api/v1/projects/${projectId}/tracts`, { number, description, taxParcelNumbers }),
  rollup: (projectId: string, groupId?: string) =>
    api.get<ParcelRollup>(`/api/v1/projects/${projectId}/parcel-rollup`, { groupId: groupId || undefined }),

  reminders: (withinDays = 7) => api.get<Reminder[]>('/api/v1/reminders', { withinDays }),

  statuses: () => api.get<ParcelStatus[]>('/api/v1/parcel-rules/statuses'),
  saveStatus: (id: string | null, body: Omit<ParcelStatus, 'id'>) =>
    id ? api.put<ParcelStatus>(`/api/v1/parcel-rules/statuses/${id}`, body) : api.post<ParcelStatus>('/api/v1/parcel-rules/statuses', body),
  customFields: () => api.get<CustomField[]>('/api/v1/parcel-rules/custom-fields'),
  saveCustomField: (id: string | null, body: Omit<CustomField, 'id' | 'entityType'>) =>
    id ? api.put<CustomField>(`/api/v1/parcel-rules/custom-fields/${id}`, body) : api.post<CustomField>('/api/v1/parcel-rules/custom-fields', body),
  deadlineRules: () => api.get<DeadlineRule[]>('/api/v1/parcel-rules/deadline-rules'),
  saveDeadlineRule: (id: string | null, body: Omit<DeadlineRule, 'id' | 'isPlaceholder'>) =>
    id ? api.put<DeadlineRule>(`/api/v1/parcel-rules/deadline-rules/${id}`, body) : api.post<DeadlineRule>('/api/v1/parcel-rules/deadline-rules', body),
  workflows: () => api.get<Workflow[]>('/api/v1/parcel-rules/workflows'),
  stepGate: () => api.get<StepGateSettings>('/api/v1/parcel-rules/step-gate'),
  setStepGate: (extraOverrideRoles: string[]) => api.put<StepGateSettings>('/api/v1/parcel-rules/step-gate', { extraOverrideRoles }),
})
