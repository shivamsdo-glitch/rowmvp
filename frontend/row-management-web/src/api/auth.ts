import type { ApiClient } from './client'

/** Mirrors the backend role names (Domain/Common/RoleNames). */
export const ROLES = [
  'ProjectManager', 'Designer', 'DesignApprover', 'Surveyor', 'TitleResearcher', 'Appraiser',
  'AppraisalApprover', 'Negotiator', 'RelocationAgent', 'Finance', 'Admin',
] as const
export type Role = (typeof ROLES)[number]

/** Mirrors the backend page keys (Domain/Common/PageKeys). */
export const PAGE_KEYS = [
  'dashboard', 'projects', 'parcels', 'parties', 'title', 'survey', 'appraisals',
  'approvals', 'negotiations', 'agreements', 'closing', 'payments', 'administration', 'library', 'contactlog', 'easements', 'ownership', 'parcellocation', 'titleencumbrances', 'titleclosing', 'parcelappraisal', 'parcelnegotiation', 'parcelquestionnaire', 'parcelcondemnation', 'parcelproperty', 'parcelrelocation',
] as const
export type PageKey = (typeof PAGE_KEYS)[number]

export type PermissionAction = 'create' | 'read' | 'update' | 'delete'

export interface CurrentUser {
  id: string
  fullName: string
  initials: string
  email: string
  roles: string[]
  tenantId: string
  tenantName: string | null
}

export interface AuthResponse {
  token: string
  tokenType: string
  expiresAtUtc: string
  user: CurrentUser
}

export interface PagePermission {
  pageKey: string
  canCreate: boolean
  canRead: boolean
  canUpdate: boolean
  canDelete: boolean
}

export interface DevTokenResponse {
  token: string
  tokenType: string
  expiresAtUtc: string
  roles: string[]
  tenantId: string
  userId: string
}

export const authApi = (api: ApiClient) => ({
  login: (username: string, password: string) => api.post<AuthResponse>('/api/v1/auth/login', { username, password }),
  devToken: (role: Role) => api.get<DevTokenResponse>('/api/v1/auth/dev-token', { role }),
  me: () => api.get<CurrentUser>('/api/v1/auth/me'),
  permissions: () => api.get<PagePermission[]>('/api/v1/auth/permissions'),
})
