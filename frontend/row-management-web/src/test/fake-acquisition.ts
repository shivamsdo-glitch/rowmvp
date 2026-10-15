import type {
  AcquisitionPlan, AcquisitionRequest, PlanReview, ReportKind, RightOfEntry, RoeReportRow, StaffReport,
} from '@/api/acquisition'
import { fakeRecordsApi, type FakeRecords, type FakeRecordsOptions } from './fake-records'

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}
const problem = (status: number, title: string, detail: string, errors?: Record<string, string[]>) => json({ title, status, detail, errors }, status)

export interface FakeAcquisitionOptions extends FakeRecordsOptions {
  roes?: (Partial<RightOfEntry> & { parcelId: string })[]
  reviews?: (Partial<PlanReview> & { parcelId: string })[]
  reports?: (Partial<StaffReport> & { parcelId: string; kind: ReportKind })[]
  requests?: Partial<AcquisitionRequest>[]
  gateEnabled?: boolean
  /** Plans by project id; a project without one gets an empty plan. */
  plans?: Record<string, AcquisitionPlan['plan']>
  /** Make GET /rights-of-entry (the report) fail with a 500. */
  reportFails?: boolean
}

export interface FakeAcquisition extends FakeRecords {
  roes: (RightOfEntry & { parcelId: string })[]
  reviews: (PlanReview & { parcelId: string })[]
  reports: StaffReport[]
  requests: AcquisitionRequest[]
  gate: { enabled: boolean }
}

const addDays = (iso: string, n: number) => {
  const d = new Date(`${iso}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + n)
  return d.toISOString().slice(0, 10)
}
const daysBetween = (a: string, b: string) => Math.round((Date.parse(`${b}T00:00:00Z`) - Date.parse(`${a}T00:00:00Z`)) / 86_400_000)

/** An in-memory stand-in for rights of entry, plan reviews, staff reports, requests and the plan, with the server's rules and permission pages. */
export function fakeAcquisitionApi(opts: FakeAcquisitionOptions): FakeAcquisition {
  const base = fakeRecordsApi(opts)
  const today = opts.today ?? '2026-10-14'
  let seq = 0
  const id = (p: string) => `${p}${++seq}`
  const can = (page: string, action: 'create' | 'read' | 'update' | 'delete') =>
    !!opts.rows?.find((r) => r.pageKey === page)?.[`can${action[0].toUpperCase()}${action.slice(1)}` as 'canCreate']
  const forbidden = () => problem(403, 'forbidden', 'Your role does not allow this.')
  const effective = (status: RightOfEntry['status'], expiresOn: string | null): RightOfEntry['effectiveStatus'] =>
    status === 'Granted' && expiresOn && expiresOn < today ? 'Expired' : status

  const gate = { enabled: opts.gateEnabled ?? false }
  const roes = (opts.roes ?? []).map((r) => ({
    id: id('roe'), status: 'Requested' as const, requestedOn: null, grantedOn: null, expiresOn: null, grantedBy: null, purpose: null, notes: null, updatedAt: '2026-01-01T00:00:00Z', ...r,
    effectiveStatus: effective(r.status ?? 'Requested', r.expiresOn ?? null),
  })) as FakeAcquisition['roes']
  const reviews = (opts.reviews ?? []).map((r) => ({
    id: id('pr'), discipline: 'Engineer' as const, outcome: 'Approved' as const, reviewedOn: today, reviewerName: 'Morgan Ellis', notes: null, createdAt: '2026-01-01T00:00:00Z', ...r,
  })) as FakeAcquisition['reviews']
  const reports = (opts.reports ?? []).map((r) => ({
    id: id('sr'), status: 'Draft' as const, title: 'Report', body: 'Body', submittedAt: null, submittedByMe: false, decidedAt: null, decisionNote: null, updatedAt: '2026-01-01T00:00:00Z', ...r,
    canEdit: (r.status ?? 'Draft') === 'Draft' || r.status === 'Rejected',
  })) as StaffReport[]
  const requests: AcquisitionRequest[] = (opts.requests ?? []).map((r) => ({
    id: id('rq'), projectId: null, projectNumber: null, requesterType: 'Internal', requesterName: 'Design team', requesterEmail: null, property: 'A lot',
    desiredParameters: null, estimatedAmount: null, status: 'Submitted', submittedByMe: false, createdAt: '2026-10-01T00:00:00Z', decidedAt: null, decisionNote: null, ...r,
  }))

  const reviewStatus = (parcelId: string) => {
    const mine = reviews.filter((r) => r.parcelId === parcelId)
    const latest = (d: string) => mine.filter((r) => r.discipline === d).sort((a, b) => b.reviewedOn.localeCompare(a.reviewedOn) || mine.indexOf(b) - mine.indexOf(a))[0]
    const missing = gate.enabled ? (['Engineer', 'Surveyor'] as const).filter((d) => latest(d)?.outcome !== 'Approved') : []
    return {
      gateEnabled: gate.enabled, stepKey: 'title_survey', missing, satisfied: missing.length === 0,
      reviews: [...mine].sort((a, b) => b.reviewedOn.localeCompare(a.reviewedOn) || mine.indexOf(b) - mine.indexOf(a)),
    }
  }

  const fetchImpl = async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://x')
    const path = url.pathname
    const parcelMatch = /^\/api\/v1\/parcels\/([^/]+)\/(rights-of-entry|plan-reviews|staff-reports)(?:\/([^/]+))?(?:\/(submit|approve|reject))?$/.exec(path)
    const isOther = parcelMatch || path === '/api/v1/rights-of-entry' || path.startsWith('/api/v1/acquisition-requests') || path === '/api/v1/agency/plan-review-gate' || /\/acquisition-plan$/.test(path)
    if (!isOther) return base.fetch(input, init)

    const headers = (init?.headers ?? {}) as Record<string, string>
    if (headers.Authorization !== 'Bearer good-token') return json({ title: 'unauthorized' }, 401)
    const method = init?.method ?? 'GET'
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    base.calls.push({ key: `${method} ${path}${url.search}`, body })
    const act = method === 'GET' ? 'read' : method === 'POST' ? 'create' : method === 'PUT' ? 'update' : 'delete'

    if (path === '/api/v1/agency/plan-review-gate') {
      if (method === 'GET') return can('parcels', 'read') ? json({ enabled: gate.enabled, stepKey: 'title_survey' }) : forbidden()
      if (!can('administration', 'update')) return forbidden()
      gate.enabled = !!body.enabled
      return json({ enabled: gate.enabled, stepKey: 'title_survey' })
    }

    if (/\/acquisition-plan$/.test(path)) {
      if (!can('parcels', 'read')) return forbidden()
      const projectId = path.split('/')[4]
      const project = base.projects.find((p) => p.id === projectId)
      if (!project) return problem(404, 'not_found', 'Project was not found.')
      const plan = opts.plans?.[projectId] ?? { parcelCount: 0, areaSqFt: 0, closedCount: 0, overdueCount: 0, firstDueOn: null, lastPlannedCloseOn: null, budget: { totalBudget: 0, currentEstimate: null, authorizedTotal: 0, variance: null, overBudget: false }, parcels: [] }
      return json({ projectId, projectNumber: project.number, projectName: project.name, today, plan })
    }

    if (path === '/api/v1/rights-of-entry') {
      if (!can('parcels', 'read')) return forbidden()
      if (opts.reportFails) return problem(500, 'server_error', 'Boom.')
      const status = url.searchParams.get('status')
      const soon = url.searchParams.get('expiringWithinDays')
      let rows: RoeReportRow[] = roes.map((r) => {
        const parcel = base.parcels.find((p) => p.id === r.parcelId)
        const project = base.projects.find((p) => p.id === parcel?.projectId)
        return {
          id: r.id, parcelId: r.parcelId, parcelNumber: parcel?.number ?? '?', parcelAddress: parcel?.address ?? null, projectId: parcel?.projectId ?? '', projectNumber: project?.number ?? '?',
          status: r.status, effectiveStatus: effective(r.status, r.expiresOn), requestedOn: r.requestedOn, grantedOn: r.grantedOn, expiresOn: r.expiresOn,
          daysUntilExpiry: r.status === 'Granted' && r.expiresOn ? daysBetween(today, r.expiresOn) : null, grantedBy: r.grantedBy, purpose: r.purpose,
        }
      })
      if (status) rows = rows.filter((r) => r.effectiveStatus === status)
      if (soon) rows = rows.filter((r) => r.daysUntilExpiry !== null && r.daysUntilExpiry >= 0 && r.daysUntilExpiry <= Number(soon))
      return json({ items: rows, total: rows.length, page: 1, pageSize: 25 })
    }

    if (path.startsWith('/api/v1/acquisition-requests')) {
      const m = /^\/api\/v1\/acquisition-requests(?:\/([^/]+))?(?:\/(approve|reject|withdraw))?$/.exec(path)
      const [, rid, verb] = m ?? []
      if (!rid && method === 'GET') {
        if (!can('approvals', 'read')) return forbidden()
        const status = url.searchParams.get('status')
        const items = requests.filter((r) => !status || r.status === status)
        return json({ items, total: items.length, page: 1, pageSize: 25 })
      }
      if (!rid && method === 'POST') {
        if (!can('projects', 'create')) return forbidden()
        const errors: Record<string, string[]> = {}
        if (!String(body?.requesterName ?? '').trim()) errors.RequesterName = ['Enter who is asking.']
        if (!String(body?.property ?? '').trim()) errors.Property = ['Say which property is wanted.']
        if (body?.requesterEmail && !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(body.requesterEmail)) errors.RequesterEmail = ['Enter a valid email address.']
        if (Object.keys(errors).length) return problem(400, 'validation_failed', 'Validation failed.', errors)
        const project = body.projectId ? base.projects.find((p) => p.id === body.projectId) : null
        if (body.projectId && !project) return problem(404, 'not_found', 'Project was not found.')
        const r: AcquisitionRequest = {
          id: id('rq'), projectId: body.projectId ?? null, projectNumber: project?.number ?? null, requesterType: body.requesterType, requesterName: body.requesterName.trim(),
          requesterEmail: body.requesterEmail ?? null, property: body.property.trim(), desiredParameters: body.desiredParameters ?? null, estimatedAmount: body.estimatedAmount ?? null,
          status: 'Submitted', submittedByMe: true, createdAt: `${today}T00:00:00Z`, decidedAt: null, decisionNote: null,
        }
        requests.unshift(r)
        return json(r, 201)
      }
      const r = requests.find((x) => x.id === rid)
      if (!r) return problem(404, 'not_found', 'Acquisition request was not found.')
      if (verb === 'withdraw') {
        if (!can('projects', 'update')) return forbidden()
        if (!r.submittedByMe) return problem(403, 'forbidden', 'Only the person who entered a request can withdraw it.')
        if (r.status !== 'Submitted') return problem(409, 'request_decided', `This request is already ${r.status.toLowerCase()}.`)
        r.status = 'Withdrawn'
        return json(r)
      }
      if (!can('approvals', 'update')) return forbidden()
      if (r.status !== 'Submitted') return problem(409, 'request_decided', `This request is already ${r.status.toLowerCase()}.`)
      if (r.submittedByMe) return problem(403, 'forbidden', 'You entered this request, so someone else has to approve or reject it.')
      if (verb === 'reject' && !String(body?.note ?? '').trim()) return problem(400, 'validation_failed', 'Validation failed.', { Note: ['Say why the request is rejected.'] })
      r.status = verb === 'approve' ? 'Approved' : 'Rejected'
      r.decidedAt = `${today}T00:00:00Z`
      r.decisionNote = body?.note?.trim() || null
      return json(r)
    }

    // Everything below is on a parcel.
    const [, parcelId, part, rid, verb] = parcelMatch!
    // The server's page and action for each call: reviews and submitting are an update; deciding is an Approvals job.
    const allowed = verb === 'approve' || verb === 'reject' ? can('approvals', 'update')
      : verb === 'submit' || (part === 'plan-reviews' && method === 'POST') ? can('parcels', 'update')
      : can('parcels', act)
    if (!allowed) return forbidden()
    const parcel = base.parcels.find((p) => p.id === parcelId)
    if (!parcel) return problem(404, 'not_found', 'Parcel was not found.')
    if (method !== 'GET' && parcel.state !== 'Active') return problem(409, 'parcel_not_active', `Parcel ${parcel.number} can no longer be changed.`)

    if (part === 'rights-of-entry') {
      if (method === 'GET') return json(roes.filter((r) => r.parcelId === parcelId))
      if (method === 'DELETE') {
        const i = roes.findIndex((r) => r.id === rid && r.parcelId === parcelId)
        if (i < 0) return problem(404, 'not_found', 'Right of entry was not found.')
        roes.splice(i, 1)
        return new Response(null, { status: 204 })
      }
      const errors: Record<string, string[]> = {}
      if (body.status === 'Granted' && !body.grantedOn) errors.GrantedOn = ['Enter the date permission was given.']
      if ((body.status === 'Requested' || body.status === 'Denied') && body.grantedOn) errors.GrantedOn = ['Only a granted right of entry has a date permission was given.']
      if (body.grantedOn && body.grantedOn > today) errors.GrantedOn = ['The date permission was given cannot be in the future.']
      if (body.requestedOn && body.requestedOn > today) errors.RequestedOn = ['The request date cannot be in the future.']
      if (body.grantedOn && body.expiresOn && body.expiresOn < body.grantedOn) errors.GrantedOn = ['Permission cannot end before it was given.']
      if (Object.keys(errors).length) return problem(400, 'validation_failed', 'Validation failed.', errors)
      const fields = { status: body.status, requestedOn: body.requestedOn ?? null, grantedOn: body.grantedOn ?? null, expiresOn: body.expiresOn ?? null, grantedBy: body.grantedBy ?? null, purpose: body.purpose ?? null, notes: body.notes ?? null }
      if (method === 'PUT') {
        const r = roes.find((x) => x.id === rid && x.parcelId === parcelId)
        if (!r) return problem(404, 'not_found', 'Right of entry was not found.')
        Object.assign(r, fields, { effectiveStatus: effective(fields.status, fields.expiresOn) })
        return json(r)
      }
      const r = { id: id('roe'), parcelId, ...fields, effectiveStatus: effective(fields.status, fields.expiresOn), updatedAt: `${today}T00:00:00Z` } as FakeAcquisition['roes'][number]
      roes.push(r)
      return json(r, 201)
    }

    if (part === 'plan-reviews') {
      if (method === 'GET') return json(reviewStatus(parcelId))
      if (method === 'DELETE') {
        const i = reviews.findIndex((r) => r.id === rid && r.parcelId === parcelId)
        if (i < 0) return problem(404, 'not_found', 'Plan review was not found.')
        reviews.splice(i, 1)
        return json(reviewStatus(parcelId))
      }
      const errors: Record<string, string[]> = {}
      if (!String(body?.reviewerName ?? '').trim()) errors.ReviewerName = ['Enter who reviewed the plans.']
      if (body?.outcome === 'ChangesRequired' && !String(body?.notes ?? '').trim()) errors.Notes = ['Say what has to change.']
      if (body?.reviewedOn > today) errors.ReviewedOn = ['The review date cannot be in the future.']
      if (Object.keys(errors).length) return problem(400, 'validation_failed', 'Validation failed.', errors)
      reviews.push({ id: id('pr'), parcelId, discipline: body.discipline, outcome: body.outcome, reviewedOn: body.reviewedOn, reviewerName: body.reviewerName.trim(), notes: body.notes ?? null, createdAt: `${today}T00:00:00Z` })
      return json(reviewStatus(parcelId), 201)
    }

    // staff reports
    if (method === 'GET') return json(reports.filter((r) => r.parcelId === parcelId))
    const report = reports.find((r) => r.id === rid && r.parcelId === parcelId)
    if (method === 'POST' && !rid) {
      const errors: Record<string, string[]> = {}
      if (!String(body?.title ?? '').trim()) errors.Title = ['Enter a title.']
      if (!String(body?.body ?? '').trim()) errors.Body = ['Write the report.']
      if (Object.keys(errors).length) return problem(400, 'validation_failed', 'Validation failed.', errors)
      if (reports.some((r) => r.parcelId === parcelId && r.kind === body.kind && r.status !== 'Rejected')) return problem(409, 'report_exists', 'This parcel already has that report.')
      const r: StaffReport = { id: id('sr'), parcelId, kind: body.kind, status: 'Draft', title: body.title.trim(), body: body.body.trim(), submittedAt: null, submittedByMe: false, decidedAt: null, decisionNote: null, canEdit: true, updatedAt: `${today}T00:00:00Z` }
      reports.push(r)
      return json(r, 201)
    }
    if (!report) return problem(404, 'not_found', 'Report was not found.')
    if (method === 'DELETE') {
      if (report.status !== 'Draft' && report.status !== 'Rejected') return problem(409, 'report_locked', `A ${report.status.toLowerCase()} report cannot be deleted.`)
      reports.splice(reports.indexOf(report), 1)
      return new Response(null, { status: 204 })
    }
    if (method === 'PUT') {
      if (!report.canEdit) return problem(409, 'report_locked', `A ${report.status.toLowerCase()} report cannot be changed.`)
      if (!String(body?.title ?? '').trim()) return problem(400, 'validation_failed', 'Validation failed.', { Title: ['Enter a title.'] })
      report.title = body.title.trim(); report.body = body.body.trim()
      return json(report)
    }
    if (verb === 'submit') {
      if (!report.canEdit) return problem(409, 'report_not_submittable', `A ${report.status.toLowerCase()} report cannot be submitted.`)
      Object.assign(report, { status: 'Submitted', submittedAt: `${today}T00:00:00Z`, submittedByMe: true, canEdit: false, decisionNote: null })
      return json(report)
    }
    if (report.status !== 'Submitted') return problem(409, 'report_not_submitted', 'Only a submitted report can be approved or rejected.')
    if (report.submittedByMe) return problem(403, 'forbidden', 'You submitted this report, so someone else has to approve or reject it.')
    if (verb === 'reject' && !String(body?.note ?? '').trim()) return problem(400, 'validation_failed', 'Validation failed.', { Note: ['Say why the report is rejected.'] })
    Object.assign(report, { status: verb === 'approve' ? 'Approved' : 'Rejected', decidedAt: `${today}T00:00:00Z`, decisionNote: body?.note?.trim() || null, canEdit: verb === 'reject' })
    return json(report)
  }

  return { ...base, fetch: fetchImpl as typeof fetch, roes, reviews, reports, requests, gate }
}
