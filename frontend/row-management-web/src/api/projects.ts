import type { ApiClient } from './client'

/** Mirrors the backend enums (Domain/Enums/ProjectEnums), sent as strings. */
export const OVERSIGHT_TYPES = ['Federal', 'State', 'Local'] as const
export type OversightType = (typeof OVERSIGHT_TYPES)[number]

export const NEPA_TYPES = ['CategoricalExclusion', 'EnvironmentalAssessment', 'EnvironmentalImpactStatement'] as const
export type NepaDecisionType = (typeof NEPA_TYPES)[number]
export const NEPA_LABEL: Record<NepaDecisionType, string> = {
  CategoricalExclusion: 'Categorical exclusion',
  EnvironmentalAssessment: 'Environmental assessment (EA)',
  EnvironmentalImpactStatement: 'Environmental impact statement (EIS)',
}

export const AUTHORIZATION_TYPES = ['Preliminary', 'Acquisition', 'Supplemental'] as const
export type AuthorizationType = (typeof AUTHORIZATION_TYPES)[number]

export const EXCEPTION_TYPES = ['Early', 'Hardship', 'Protective'] as const
export type AcquisitionExceptionType = (typeof EXCEPTION_TYPES)[number]

export interface AcquisitionGate {
  unlocked: boolean
  /** UngatedFunding | ClearanceComplete | ExceptionApproved | Blocked */
  reason: string
  missing: string[]
}

export interface ProjectSummary {
  id: string
  number: string
  name: string
  region: string | null
  district: string | null
  oversightType: OversightType
  currentEstimate: number | null
  acquisitionGate: AcquisitionGate
}

export interface Project {
  id: string
  number: string
  name: string
  description: string | null
  region: string | null
  district: string | null
  oversightType: OversightType
  fundingSource: string | null
  fundingAmount: number | null
  designEstimate: number | null
  currentEstimate: number | null
  lettingDate: string | null
  planTurnInDate: string | null
  constructionStartDate: string | null
  constructionEndDate: string | null
  nepaDecisionType: NepaDecisionType | null
  nepaDecisionDate: string | null
  /** The project's own IANA time zone; null means it follows the agency's. */
  timeZoneId: string | null
  exceptionType: AcquisitionExceptionType | null
  exceptionApprovedOn: string | null
  exceptionReason: string | null
  acquisitionGate: AcquisitionGate
  updatedAt: string
  phase: string | null
  location: string | null
  projectManagerUserId: string | null
  projectManagerName: string | null
  documentFolderStructure: string | null
  documentProperties: string | null
  constructionProjectManager: string | null
  rightOfWayClearDate: string | null
  /** The Planned or Build project this ROW project is linked to. */
  linkedSource: CatalogSource | null
  linkedProjectNumber: string | null
  linkedProjectName: string | null
  /** The annual budgets added together. */
  boardApprovedFunds: number
  /** 0 until payments exist. */
  actualCost: number
}

export const CATALOG_SOURCES = ['Planned', 'Build'] as const
export type CatalogSource = (typeof CATALOG_SOURCES)[number]

export interface CatalogProject {
  source: CatalogSource
  number: string
  name: string
}

export interface AssignableManager {
  id: string
  name: string
}

/** What the create and edit forms send. Blank optional fields are null. */
export interface ProjectInput {
  number: string | null
  name: string
  description: string | null
  region: string | null
  district: string | null
  oversightType: OversightType
  fundingSource: string | null
  fundingAmount: number | null
  designEstimate: number | null
  lettingDate: string | null
  planTurnInDate: string | null
  constructionStartDate: string | null
  constructionEndDate: string | null
  phase?: string | null
  location?: string | null
  projectManagerUserId?: string | null
  documentFolderStructure?: string | null
  documentProperties?: string | null
  constructionProjectManager?: string | null
  rightOfWayClearDate?: string | null
  linkedSource?: CatalogSource | null
  linkedProjectNumber?: string | null
}

export interface Paged<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export interface CostEstimate {
  id: string
  version: number
  landCost: number
  improvementsCost: number
  relocationCost: number
  indirectCost: number
  total: number
  notes: string | null
  createdAt: string
}

export interface CostEstimateInput {
  landCost: number
  improvementsCost: number
  relocationCost: number
  indirectCost: number
  notes: string | null
}

export interface Budget {
  id: string
  fiscalYear: number
  amount: number
  notes: string | null
}

export interface RowAuthorization {
  id: string
  type: AuthorizationType
  authorizedAmount: number
  federalSharePercent: number | null
  federalShareAmount: number | null
  authorizedOn: string
  referenceNumber: string | null
  programmingConfirmed: boolean
  notes: string | null
}

export interface RowAuthorizationInput {
  type: AuthorizationType
  authorizedAmount: number
  federalSharePercent: number | null
  authorizedOn: string
  referenceNumber: string | null
  programmingConfirmed: boolean
  notes: string | null
}

export interface NumberSettings {
  pattern: string
  fiscalYearStartMonth: number
  /** The number the next project would receive. */
  example: string
}

export interface TimeZoneOption { id: string; label: string }
/** The agency's default time zone, the zones it can choose from, and today's date there. */
export interface AgencyTimeZone { timeZoneId: string; options: TimeZoneOption[]; today: string }

export interface ImportResult {
  row: number
  outcome: 'Created' | 'Failed'
  projectId: string | null
  number: string | null
  error: string | null
}
export interface ImportSummary {
  created: number
  failed: number
  results: ImportResult[]
}

export interface ProjectListFilter {
  search?: string
  region?: string
  district?: string
  oversightType?: OversightType | ''
  page?: number
  pageSize?: number
}

export const projectsApi = (api: ApiClient) => ({
  list: (f: ProjectListFilter) =>
    api.get<Paged<ProjectSummary>>('/api/v1/projects', {
      search: f.search || undefined,
      region: f.region || undefined,
      district: f.district || undefined,
      oversightType: f.oversightType || undefined,
      page: f.page ?? 1,
      pageSize: f.pageSize ?? 25,
    }),
  get: (id: string) => api.get<Project>(`/api/v1/projects/${id}`),
  managers: () => api.get<AssignableManager[]>('/api/v1/projects/managers'),
  catalog: (source: CatalogSource, search?: string) => api.get<CatalogProject[]>('/api/v1/projects/catalog', { source, search: search || undefined }),
  create: (body: ProjectInput) => api.post<Project>('/api/v1/projects', body),
  update: (id: string, body: ProjectInput) => api.put<Project>(`/api/v1/projects/${id}`, body),
  import: (rows: Partial<ProjectInput>[]) => api.post<ImportSummary>('/api/v1/projects/import', { rows }),

  recordNepa: (id: string, decisionType: NepaDecisionType | null, decisionDate: string | null) =>
    api.put<Project>(`/api/v1/projects/${id}/nepa`, { decisionType, decisionDate }),
  setException: (id: string, exceptionType: AcquisitionExceptionType | null, approvedOn: string | null, reason: string | null) =>
    api.put<Project>(`/api/v1/projects/${id}/acquisition-exception`, { exceptionType, approvedOn, reason }),

  estimates: (id: string) => api.get<CostEstimate[]>(`/api/v1/projects/${id}/cost-estimates`),
  addEstimate: (id: string, body: CostEstimateInput) => api.post<CostEstimate>(`/api/v1/projects/${id}/cost-estimates`, body),

  budgets: (id: string) => api.get<Budget[]>(`/api/v1/projects/${id}/budgets`),
  setBudget: (id: string, fiscalYear: number, amount: number, notes: string | null) =>
    api.put<Budget>(`/api/v1/projects/${id}/budgets/${fiscalYear}`, { amount, notes }),
  deleteBudget: (id: string, fiscalYear: number) => api.delete(`/api/v1/projects/${id}/budgets/${fiscalYear}`),

  authorizations: (id: string) => api.get<RowAuthorization[]>(`/api/v1/projects/${id}/authorizations`),
  addAuthorization: (id: string, body: RowAuthorizationInput) =>
    api.post<RowAuthorization>(`/api/v1/projects/${id}/authorizations`, body),
  deleteAuthorization: (id: string, authorizationId: string) =>
    api.delete(`/api/v1/projects/${id}/authorizations/${authorizationId}`),

  agencyTimeZone: () => api.get<AgencyTimeZone>('/api/v1/agency/time-zone'),
  setAgencyTimeZone: (timeZoneId: string) => api.put<AgencyTimeZone>('/api/v1/agency/time-zone', { timeZoneId }),
  setProjectTimeZone: (id: string, timeZoneId: string | null) => api.put<Project>(`/api/v1/projects/${id}/time-zone`, { timeZoneId }),
  numberSettings: () => api.get<NumberSettings>('/api/v1/projects/number-settings'),
  setNumberSettings: (pattern: string, fiscalYearStartMonth: number) =>
    api.put<NumberSettings>('/api/v1/projects/number-settings', { pattern, fiscalYearStartMonth }),
})
