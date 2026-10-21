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

export type Health = 'None' | 'Green' | 'Amber' | 'Red'
export interface ProgramTile { key: string; label: string; value: number; format: 'count' | 'percent' | 'money'; tone: Tone }
export interface StepCount { stepKey: string; label: string; count: number }
export interface Stage {
  stepKey: string
  label: string
  team: string
  parcelsNow: number
  completedStays: number
  avgDaysCompleted: number | null
  avgDaysOpen: number | null
  longestOpenDays: number | null
  stuck: number
}
export interface TeamTime { team: string; parcelsNow: number; stays: number; avgDaysPerStay: number | null; totalDays: number; longestOpenDays: number | null; stuck: number }
export interface StuckParcel { parcelId: string; parcelNumber: string; projectId: string; projectNumber: string; stepLabel: string; team: string; days: number }
export interface ProjectRow {
  id: string
  number: string
  name: string
  phase: string | null
  projectManager: string | null
  oversightType: string
  parcels: number
  closed: number
  percentClosed: number
  byStep: StepCount[]
  overdueParcels: number
  stuckParcels: number
  estimateTotal: number | null
  knownCost: number
  rowClearDate: string | null
  daysToClear: number | null
  health: Health
  reasons: string[]
}
export interface ParcelRow {
  id: string
  number: string
  address: string | null
  ownerName: string | null
  status: string
  statusColor: string
  stepKey: string
  stepLabel: string
  team: string
  daysInStep: number
  appraisalTotal: number | null
  approvedJustCompensation: number | null
  agreedAmount: number | null
  anticipatedClosing: string | null
  actualClosing: string | null
  closed: boolean
  flags: string[]
}
export interface ProgramDashboard { generatedAt: string; stuckDays: number; tiles: ProgramTile[]; projects: ProjectRow[]; stages: Stage[]; teams: TeamTime[]; stuck: StuckParcel[] }
export interface ProjectDashboard { generatedAt: string; stuckDays: number; project: ProjectRow; stages: Stage[]; teams: TeamTime[]; parcels: ParcelRow[] }

export const dashboardApi = (api: ApiClient) => ({
  /** The ROW Director's view (project managers and administrators only). */
  program: () => api.get<ProgramDashboard>('/api/v1/dashboard/program'),
  project: (id: string) => api.get<ProjectDashboard>(`/api/v1/dashboard/projects/${id}`),
  me: () => api.get<MyDashboard>('/api/v1/dashboard/me'),
  /** Project managers and administrators only; anyone else gets a 403. */
  team: () => api.get<TeamDashboard>('/api/v1/dashboard/team'),
})
