import type { MyDashboard, TeamDashboard } from '@/api/dashboard'

const today = new Date()
const iso = (daysAgo: number) => {
  const d = new Date(today.getTime() - daysAgo * 86_400_000)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
export const SAMPLE_DAYS = [6, 5, 4, 3, 2, 1, 0].map((ago, i) => ({ date: iso(ago), count: [0, 2, 0, 5, 1, 0, 3][i] }))

/** A dashboard with something waiting and something done. */
export const sampleDashboard = (over: Partial<MyDashboard> = {}): MyDashboard => ({
  userId: 'u1', fullName: 'Dev User', roles: ['Negotiator'], isManager: false, generatedAt: new Date().toISOString(),
  pending: [
    { key: 'requests-to-decide', label: 'Acquisition requests waiting for your decision', count: 3, link: '/requests', tone: 'warn' },
    { key: 'deadlines-overdue', label: 'Parcel deadlines that are overdue', count: 2, link: '/parcels', tone: 'danger' },
    { key: 'my-draft-reports', label: 'Your staff reports still in draft', count: 0, link: '/parcels', tone: 'info' },
  ],
  done: {
    windowDays: 30, created: 7, updated: 4, deleted: 0, total: 11, lastActiveAt: new Date(Date.now() - 5 * 60_000).toISOString(),
    days: SAMPLE_DAYS,
    byKind: [{ kind: 'Parcel contact log entry', created: 5, updated: 1, deleted: 0 }, { kind: 'Parcel negotiation', created: 2, updated: 3, deleted: 0 }],
    recent: [{ kind: 'Parcel contact log entry', action: 'Create', at: new Date(Date.now() - 5 * 60_000).toISOString() }],
  },
  ...over,
})

/** A dashboard for a person who has not done anything and has nothing waiting. */
export const emptyDashboard = (over: Partial<MyDashboard> = {}): MyDashboard => sampleDashboard({
  pending: [{ key: 'requests-to-decide', label: 'Acquisition requests waiting for your decision', count: 0, link: '/requests', tone: 'warn' }],
  done: { windowDays: 30, created: 0, updated: 0, deleted: 0, total: 0, lastActiveAt: null, days: SAMPLE_DAYS.map((d) => ({ ...d, count: 0 })), byKind: [], recent: [] },
  ...over,
})

export const sampleTeam = (over: Partial<TeamDashboard> = {}): TeamDashboard => ({
  generatedAt: new Date().toISOString(), windowDays: 30,
  tiles: [
    { key: 'projects', label: 'Projects', value: 4, link: '/projects', tone: 'info' },
    { key: 'parcels', label: 'Active parcels', value: 31, link: '/parcels', tone: 'info' },
    { key: 'awaiting-decision', label: 'Awaiting a decision', value: 3, link: '/requests', tone: 'warn' },
    { key: 'deadlines-overdue', label: 'Overdue deadlines', value: 2, link: '/parcels', tone: 'danger' },
    { key: 'roe-expiring', label: 'Rights of entry expiring', value: 0, link: '/rights-of-entry', tone: 'warn' },
    { key: 'closings-due', label: 'Closings due or past due', value: 1, link: '/negotiations', tone: 'warn' },
  ],
  parcelsByStatus: [{ status: 'Appraisal', color: '#2563eb', parcels: 12 }, { status: 'Negotiation', color: '#d97706', parcels: 19 }],
  days: SAMPLE_DAYS,
  byKind: [{ kind: 'Parcel', created: 9, updated: 4, deleted: 1 }],
  members: [
    { userId: 'a', fullName: 'Nora Negotiator', roles: ['Negotiator'], created: 7, updated: 4, deleted: 0, total: 11, lastActiveAt: new Date(Date.now() - 3_600_000).toISOString(), openRequests: 0, draftReports: 1 },
    { userId: 'b', fullName: 'Dan Designer', roles: ['Designer'], created: 0, updated: 0, deleted: 0, total: 0, lastActiveAt: null, openRequests: 2, draftReports: 0 },
  ],
  ...over,
})
