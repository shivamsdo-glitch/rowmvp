import type { ApiClient } from './client'

export type Tone = 'danger' | 'warn' | 'info'

/** One kind of work waiting for the signed-in person. `link` is an in-app path. */
export interface PendingItem { key: string; label: string; count: number; link: string; tone: Tone }
export interface ActivityDay { date: string; count: number }
export interface ActivityKind { kind: string; created: number; updated: number; deleted: number }
export interface ActivityAction { kind: string; action: 'Create' | 'Update' | 'Delete'; at: string }

export interface Done {
  windowDays: number
  created: number
  updated: number
  deleted: number
  total: number
  lastActiveAt: string | null
  days: ActivityDay[]
  byKind: ActivityKind[]
  recent: ActivityAction[]
}

export interface MyDashboard {
  userId: string
  fullName: string
  roles: string[]
  isManager: boolean
  generatedAt: string
  pending: PendingItem[]
  done: Done
}

export interface TeamTile { key: string; label: string; value: number; link: string; tone: Tone }
export interface TeamStatus { status: string; color: string; parcels: number }
export interface TeamMember {
  userId: string
  fullName: string
  roles: string[]
  created: number
  updated: number
  deleted: number
  total: number
  lastActiveAt: string | null
  openRequests: number
  draftReports: number
}
export interface TeamDashboard {
  generatedAt: string
  windowDays: number
  tiles: TeamTile[]
  parcelsByStatus: TeamStatus[]
  days: ActivityDay[]
  byKind: ActivityKind[]
  members: TeamMember[]
}

export const dashboardApi = (api: ApiClient) => ({
  me: () => api.get<MyDashboard>('/api/v1/dashboard/me'),
  /** Project managers and administrators only; anyone else gets a 403. */
  team: () => api.get<TeamDashboard>('/api/v1/dashboard/team'),
})
