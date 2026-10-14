import type {
  AcquisitionGate, Budget, CostEstimate, NumberSettings, Project, ProjectSummary, RowAuthorization,
} from '@/api/projects'
import { fakeAuthFetch } from './fake-api'

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}
const problem = (status: number, title: string, detail: string, errors?: Record<string, string[]>) =>
  json({ title, status, detail, errors }, status)

/** Mirrors the backend's AcquisitionGate rules closely enough for the UI to be exercised against it. */
export function gateOf(p: Pick<Project, 'oversightType' | 'nepaDecisionType' | 'exceptionType' | 'exceptionApprovedOn'>, auths: RowAuthorization[]): AcquisitionGate {
  if (p.oversightType === 'Local') return { unlocked: true, reason: 'UngatedFunding', missing: [] }
  const nepa = p.nepaDecisionType !== null
  const auth = auths.some((a) => a.type !== 'Preliminary')
  if (nepa && auth) return { unlocked: true, reason: 'ClearanceComplete', missing: [] }
  if (p.exceptionType && p.exceptionApprovedOn) return { unlocked: true, reason: 'ExceptionApproved', missing: [] }
  const missing: string[] = []
  if (!nepa) missing.push('NEPA decision')
  if (!auth) missing.push('Acquisition authorization')
  return { unlocked: false, reason: 'Blocked', missing }
}

export const TIME_ZONE_OPTIONS = [
  { id: 'UTC', label: 'UTC (Coordinated Universal Time)' },
  { id: 'America/New_York', label: 'Eastern Time (New York)' },
  { id: 'America/Chicago', label: 'Central Time (Chicago)' },
  { id: 'Pacific/Guam', label: 'Chamorro Time (Guam, Northern Mariana Islands)' },
]

export interface FakeProjectsOptions {
  role: string
  /** The agency's default time zone (default UTC). */
  agencyZone?: string
  rows: Parameters<typeof fakeAuthFetch>[0]['rows']
  /** Seed projects (full records); the fake assigns ids and numbers where missing. */
  projects?: Partial<Project>[]
  /** Seed budgets / authorizations by project id. */
  budgets?: Record<string, Budget[]>
  authorizations?: Record<string, RowAuthorization[]>
  /** Make GET /projects fail with a 500. */
  listFails?: boolean
  /** Make GET /projects/{id} answer 404. */
  detailMissing?: boolean
}

export interface FakeProjects {
  fetch: typeof fetch
  /** Every request seen: "METHOD path?query" plus its parsed JSON body. */
  calls: { key: string; body?: any }[]
  projects: Project[]
  settings: NumberSettings
  /** The agency's default time zone. */
  agencyZone: string
}

/** An in-memory stand-in for the Projects API (and the auth endpoints), good enough to drive the real screens. */
export function fakeProjectsApi(opts: FakeProjectsOptions): FakeProjects {
  const auth = fakeAuthFetch({ rows: opts.rows, role: opts.role }) as typeof fetch
  let seq = 0
  const mk = (given: Partial<Project>): Project => {
    seq += 1
    // An explicit `undefined` must not overwrite a default (a spread would).
    const p = Object.fromEntries(Object.entries(given).filter(([, v]) => v !== undefined)) as Partial<Project>
    return {
      id: p.id ?? `p${seq}`, number: p.number ?? `2026-${String(seq).padStart(4, '0')}`, name: p.name ?? `Project ${seq}`,
      description: null, region: null, district: null, oversightType: 'Federal', fundingSource: null, fundingAmount: null,
      designEstimate: null, currentEstimate: null, lettingDate: null, planTurnInDate: null, constructionStartDate: null,
      constructionEndDate: null, nepaDecisionType: null, nepaDecisionDate: null, exceptionType: null, exceptionApprovedOn: null,
      exceptionReason: null, timeZoneId: null, acquisitionGate: { unlocked: false, reason: 'Blocked', missing: [] }, updatedAt: '2026-01-01T00:00:00Z', ...p,
    }
  }
  const state = {
    projects: (opts.projects ?? []).map(mk),
    estimates: {} as Record<string, CostEstimate[]>,
    budgets: { ...(opts.budgets ?? {}) } as Record<string, Budget[]>,
    auths: { ...(opts.authorizations ?? {}) } as Record<string, RowAuthorization[]>,
    settings: { pattern: '{FY}-{SEQ:4}', fiscalYearStartMonth: 1, example: '2026-0001' } as NumberSettings,
    agencyZone: opts.agencyZone ?? 'UTC',
  }
  const calls: FakeProjects['calls'] = []
  const refresh = (p: Project) => {
    p.acquisitionGate = gateOf(p, state.auths[p.id] ?? [])
    p.currentEstimate = state.estimates[p.id]?.[0]?.total ?? null
  }
  state.projects.forEach(refresh)
  const find = (id: string) => state.projects.find((p) => p.id === id)
  const can = (action: 'create' | 'read' | 'update' | 'delete', page = 'projects') =>
    !!opts.rows?.find((r) => r.pageKey === page)?.[`can${action[0].toUpperCase()}${action.slice(1)}` as 'canCreate']
  const forbidden = () => problem(403, 'forbidden', 'Your role does not allow this.')

  const fetchImpl = async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://x')
    const path = url.pathname
    if (path === '/api/v1/agency/time-zone') {
      const h = (init?.headers ?? {}) as Record<string, string>
      if (h.Authorization !== 'Bearer good-token') return json({ title: 'unauthorized' }, 401)
      const m = init?.method ?? 'GET'
      const b = init?.body ? JSON.parse(String(init.body)) : undefined
      calls.push({ key: `${m} ${path}`, body: b })
      const shape = () => ({ timeZoneId: state.agencyZone, options: TIME_ZONE_OPTIONS, today: '2026-10-14' })
      if (m === 'GET') return can('read') ? json(shape()) : forbidden()
      if (!can('update', 'administration')) return forbidden()
      if (!TIME_ZONE_OPTIONS.some((o) => o.id === b?.timeZoneId)) return problem(400, 'validation_failed', 'Validation failed.', { TimeZoneId: ['Choose one of the listed time zones.'] })
      state.agencyZone = b.timeZoneId
      return json(shape())
    }
    if (!path.startsWith('/api/v1/projects')) return auth(input, init)
    const headers = (init?.headers ?? {}) as Record<string, string>
    if (headers.Authorization !== 'Bearer good-token') return json({ title: 'unauthorized' }, 401)

    const method = init?.method ?? 'GET'
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    calls.push({ key: `${method} ${path}${url.search}`, body })

    const rest = path.slice('/api/v1/projects'.length).split('/').filter(Boolean)

    if (method === 'GET' && rest.length === 0) {
      if (!can('read')) return forbidden()
      if (opts.listFails) return problem(500, 'internal_error', 'An unexpected error occurred.')
      const q = (url.searchParams.get('search') ?? '').toLowerCase()
      const region = url.searchParams.get('region')
      const district = url.searchParams.get('district')
      const oversight = url.searchParams.get('oversightType')
      const page = Number(url.searchParams.get('page') ?? 1)
      const pageSize = Number(url.searchParams.get('pageSize') ?? 25)
      const all = state.projects
        .filter((p) => !q || p.number.toLowerCase().includes(q) || p.name.toLowerCase().includes(q))
        .filter((p) => !region || p.region === region)
        .filter((p) => !district || p.district === district)
        .filter((p) => !oversight || p.oversightType === oversight)
        .sort((a, b) => a.number.localeCompare(b.number))
      const items: ProjectSummary[] = all.slice((page - 1) * pageSize, page * pageSize).map((p) => ({
        id: p.id, number: p.number, name: p.name, region: p.region, district: p.district, oversightType: p.oversightType,
        currentEstimate: p.currentEstimate, acquisitionGate: p.acquisitionGate,
      }))
      return json({ items, total: all.length, page, pageSize })
    }

    if (method === 'POST' && rest.length === 0) {
      if (!can('create')) return forbidden()
      if (!String(body.name ?? '').trim()) return problem(400, 'validation_failed', 'Validation failed.', { Name: ['Name is required.'] })
      if (body.constructionStartDate && body.constructionEndDate && body.constructionEndDate < body.constructionStartDate)
        return problem(400, 'validation_failed', 'Validation failed.', { ConstructionEndDate: ['Construction end cannot be before construction start.'] })
      if (body.number && state.projects.some((p) => p.number.toLowerCase() === String(body.number).toLowerCase()))
        return problem(409, 'project_number_exists', `A project numbered '${body.number}' already exists.`)
      const p = mk({ ...body, number: body.number ?? undefined })
      state.projects.push(p)
      refresh(p)
      return json(p, 201)
    }

    if (rest[0] === 'import' && method === 'POST') {
      if (!can('create')) return forbidden()
      const results = (body.rows as any[]).map((r, i) => {
        if (!String(r.name ?? '').trim()) return { row: i + 1, outcome: 'Failed', projectId: null, number: r.number ?? null, error: 'Name is required.' }
        if (r.number && state.projects.some((p) => p.number === r.number))
          return { row: i + 1, outcome: 'Failed', projectId: null, number: r.number, error: `A project numbered '${r.number}' already exists.` }
        const p = mk({ ...r, number: r.number ?? undefined })
        state.projects.push(p); refresh(p)
        return { row: i + 1, outcome: 'Created', projectId: p.id, number: p.number, error: null }
      })
      return json({ created: results.filter((r) => r.outcome === 'Created').length, failed: results.filter((r) => r.outcome === 'Failed').length, results })
    }

    if (rest[0] === 'number-settings') {
      if (method === 'GET') return can('read') ? json(state.settings) : forbidden()
      if (!can('update', 'administration')) return forbidden()
      if (!/\{SEQ:\d\}/.test(body.pattern)) return problem(400, 'validation_failed', 'Validation failed.', { Pattern: ['The pattern must contain exactly one {SEQ:n} token.'] })
      state.settings = { ...state.settings, pattern: body.pattern, fiscalYearStartMonth: body.fiscalYearStartMonth, example: body.pattern.replace('{FY}', '2026').replace(/\{SEQ:(\d)\}/, (_: string, n: string) => '1'.padStart(Number(n), '0')) }
      return json(state.settings)
    }

    const project = find(rest[0])
    if (!project || opts.detailMissing) return problem(404, 'not_found', `Project with key '${rest[0]}' was not found.`)
    const sub = rest[1]

    if (!sub) {
      if (method === 'GET') return can('read') ? json(project) : forbidden()
      if (method === 'PUT') {
        if (!can('update')) return forbidden()
        if (!String(body.name ?? '').trim()) return problem(400, 'validation_failed', 'Validation failed.', { Name: ['Name is required.'] })
        Object.assign(project, body, { number: body.number || project.number })
        refresh(project)
        return json(project)
      }
    }
    if (sub === 'time-zone' && method === 'PUT') {
      if (!can('update')) return forbidden()
      if (body.timeZoneId !== null && !TIME_ZONE_OPTIONS.some((o) => o.id === body.timeZoneId)) return problem(400, 'validation_failed', 'Validation failed.', { TimeZoneId: ['Choose one of the listed time zones.'] })
      project.timeZoneId = body.timeZoneId
      return json(project)
    }
    if (sub === 'nepa' && method === 'PUT') {
      if (!can('update')) return forbidden()
      project.nepaDecisionType = body.decisionType; project.nepaDecisionDate = body.decisionDate
      refresh(project)
      return json(project)
    }
    if (sub === 'acquisition-exception' && method === 'PUT') {
      if (!can('update')) return forbidden()
      project.exceptionType = body.exceptionType; project.exceptionApprovedOn = body.approvedOn; project.exceptionReason = body.reason
      refresh(project)
      return json(project)
    }
    if (sub === 'cost-estimates') {
      if (method === 'GET') return json(state.estimates[project.id] ?? [])
      if (!can('update')) return forbidden()
      const list = (state.estimates[project.id] ??= [])
      const total = body.landCost + body.improvementsCost + body.relocationCost + body.indirectCost
      const created: CostEstimate = { id: `e${list.length + 1}-${project.id}`, version: list.length + 1, ...body, total, createdAt: '2026-03-01T00:00:00Z' }
      list.unshift(created)
      refresh(project)
      return json(created, 201)
    }
    if (sub === 'budgets') {
      const list = (state.budgets[project.id] ??= [])
      if (method === 'GET') return json([...list].sort((a, b) => a.fiscalYear - b.fiscalYear))
      const fy = Number(rest[2])
      if (method === 'PUT') {
        if (!can('update')) return forbidden()
        let b = list.find((x) => x.fiscalYear === fy)
        if (!b) { b = { id: `b${fy}`, fiscalYear: fy, amount: 0, notes: null }; list.push(b) }
        b.amount = body.amount; b.notes = body.notes
        return json(b)
      }
      if (method === 'DELETE') {
        if (!can('delete')) return forbidden()
        state.budgets[project.id] = list.filter((x) => x.fiscalYear !== fy)
        return new Response(null, { status: 204 })
      }
    }
    if (sub === 'authorizations') {
      const list = (state.auths[project.id] ??= [])
      if (method === 'GET') return json(list)
      if (method === 'POST') {
        if (!can('update')) return forbidden()
        const created: RowAuthorization = {
          id: `a${list.length + 1}-${project.id}`, ...body,
          federalShareAmount: body.federalSharePercent === null ? null : (body.authorizedAmount * body.federalSharePercent) / 100,
        }
        list.push(created)
        refresh(project)
        return json(created, 201)
      }
      if (method === 'DELETE') {
        if (!can('delete')) return forbidden()
        state.auths[project.id] = list.filter((a) => a.id !== rest[2])
        refresh(project)
        return new Response(null, { status: 204 })
      }
    }
    return problem(404, 'not_found', 'Unknown route.')
  }

  return { fetch: fetchImpl as typeof fetch, calls, projects: state.projects, get settings() { return state.settings }, get agencyZone() { return state.agencyZone } }
}
