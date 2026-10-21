import type { ParcelRow, ProgramRules, ProgramSettings, ProgramDashboard, ProjectDashboard, ProjectRow, MyDashboard, TeamDashboard } from '@/api/dashboard'

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

export const DEFAULT_RULES: ProgramRules = { stuckDays: 30, clearDateWarningDays: 90, clearDateWarningPercentClosed: 75, redOverdueMinimumParcels: 3, redOverduePercent: 25 }

const STEPS = [['setup', 'Parcel set up'], ['title_survey', 'Title & survey'], ['appraisal', 'Appraisal & just compensation'], ['offer', 'Offer'], ['negotiation', 'Negotiation'], ['agreement', 'Agreement'], ['closing', 'Closing']] as const
const byStep = (counts: Record<string, number>) => STEPS.map(([stepKey, label]) => ({ stepKey, label, count: counts[stepKey] ?? 0 }))

export const sampleProject = (over: Partial<ProjectRow> = {}): ProjectRow => ({
  id: 'p1', number: 'P-2026-001', name: 'US-90 Widening', phase: 'Phase 1', projectManager: 'Pat Manager', oversightType: 'Federal',
  parcels: 4, closed: 1, percentClosed: 25, byStep: byStep({ setup: 1, title_survey: 1, appraisal: 1, closing: 1 }), overdueParcels: 1, stuckParcels: 1,
  estimateTotal: 500_000, knownCost: 620_000, rowClearDate: '2026-09-01', daysToClear: -50, health: 'Red',
  reasons: ['The ROW clear date (2026-09-01) has passed with 3 parcels not closed.', 'Known cost $620,000 is over the estimate $500,000.'], ...over,
})

export const sampleProgram = (over: Partial<ProgramDashboard> = {}): ProgramDashboard => ({
  generatedAt: new Date().toISOString(), stuckDays: 30, rules: DEFAULT_RULES,
  tiles: [
    { key: 'projects', label: 'Projects', value: 2, format: 'count', tone: 'info' },
    { key: 'parcels', label: 'Active parcels', value: 7, format: 'count', tone: 'info' },
    { key: 'closed', label: 'Parcels closed', value: 28, format: 'percent', tone: 'info' },
    { key: 'attention', label: 'Projects needing attention', value: 1, format: 'count', tone: 'warn' },
    { key: 'stuck', label: 'Parcels stuck on a step', value: 1, format: 'count', tone: 'danger' },
    { key: 'estimate', label: 'Estimated ROW cost', value: 1_250_000, format: 'money', tone: 'info' },
    { key: 'known', label: 'Known cost to date', value: 840_000, format: 'money', tone: 'info' },
  ],
  projects: [sampleProject(), sampleProject({ id: 'p2', number: 'P-2026-002', name: 'Main St Bridge', parcels: 3, closed: 1, percentClosed: 33, byStep: byStep({ negotiation: 2, closing: 1 }), overdueParcels: 0, stuckParcels: 0, estimateTotal: 750_000, knownCost: 220_000, rowClearDate: null, daysToClear: null, health: 'Green', reasons: [] })],
  stages: STEPS.map(([stepKey, label], i) => ({ stepKey, label, team: ['Project team', 'Title & Survey', 'Appraisal', 'Negotiation', 'Negotiation', 'Negotiation', 'Closing'][i], parcelsNow: i === 1 ? 2 : 1, completedStays: i, avgDaysCompleted: i ? 4.5 * i : null, avgDaysOpen: 12.5, longestOpenDays: 41, stuck: i === 2 ? 1 : 0 })),
  teams: [
    { team: 'Appraisal', parcelsNow: 2, stays: 5, avgDaysPerStay: 42.5, totalDays: 212, longestOpenDays: 61, stuck: 1 },
    { team: 'Negotiation', parcelsNow: 3, stays: 9, avgDaysPerStay: 18, totalDays: 162, longestOpenDays: 20, stuck: 0 },
    { team: 'Title & Survey', parcelsNow: 1, stays: 4, avgDaysPerStay: 6.2, totalDays: 25, longestOpenDays: 9, stuck: 0 },
  ],
  stuck: [{ parcelId: 'pc1', parcelNumber: '1004', projectId: 'p1', projectNumber: 'P-2026-001', stepLabel: 'Appraisal & just compensation', team: 'Appraisal', days: 61 }],
  ...over,
})

export const sampleParcels = (): ParcelRow[] => [
  { id: 'pc1', number: '1001', address: '12 Harbor Rd', ownerName: 'Ada Owner', status: 'Appraisal', statusColor: '#2563eb', stepKey: 'appraisal', stepLabel: 'Appraisal & just compensation', team: 'Appraisal', daysInStep: 61,
    appraisalTotal: 102_500.25, approvedJustCompensation: 90_000, agreedAmount: null, anticipatedClosing: null, actualClosing: null, closed: false, flags: ['Overdue deadline', '61 days on this step'] },
  { id: 'pc2', number: '1002', address: '14 Harbor Rd', ownerName: 'Bo Owner', status: 'In Possession - CLOSED', statusColor: '#16a34a', stepKey: 'closing', stepLabel: 'Closing', team: 'Closing', daysInStep: 3,
    appraisalTotal: 80_000, approvedJustCompensation: 80_000, agreedAmount: 95_000, anticipatedClosing: '2026-05-01', actualClosing: '2026-05-02', closed: true, flags: [] },
  { id: 'pc3', number: '1003', address: null, ownerName: null, status: 'Pre-Acquisition', statusColor: '#64748b', stepKey: 'setup', stepLabel: 'Parcel set up', team: 'Project team', daysInStep: 2,
    appraisalTotal: null, approvedJustCompensation: null, agreedAmount: null, anticipatedClosing: '2026-12-01', actualClosing: null, closed: false, flags: [] },
]

export const sampleProjectDashboard = (over: Partial<ProjectDashboard> = {}): ProjectDashboard => {
  const program = sampleProgram()
  return { generatedAt: new Date().toISOString(), stuckDays: 30, rules: DEFAULT_RULES, project: program.projects[0], stages: program.stages, teams: program.teams, parcels: sampleParcels(), ...over }
}

const DEFAULT_TEAMS: Record<string, string> = { setup: 'Project team', title_survey: 'Title & Survey', appraisal: 'Appraisal', offer: 'Negotiation', negotiation: 'Negotiation', agreement: 'Negotiation', closing: 'Closing' }

/** The Program settings an agency has before anybody changes anything. */
export const sampleSettings = (over: Partial<ProgramSettings> = {}): ProgramSettings => ({
  rules: DEFAULT_RULES, defaults: DEFAULT_RULES,
  steps: STEPS.map(([stepKey, label]) => ({ stepKey, label, team: DEFAULT_TEAMS[stepKey], defaultTeam: DEFAULT_TEAMS[stepKey] })),
  teams: ['Appraisal', 'Closing', 'Negotiation', 'Project team', 'Title & Survey'], ...over,
})
