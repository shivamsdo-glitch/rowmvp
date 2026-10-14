import type {
  CustomField, DeadlineRule, Deadline, DeadlineStatus, Parcel, ParcelGroup, ParcelStatus, ParcelSummary, ParentTract, Reminder, Workflow, WorkflowStep,
} from '@/api/parcels'
import { fakeProjectsApi, type FakeProjects, type FakeProjectsOptions } from './fake-projects'

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}
const problem = (status: number, title: string, detail: string, errors?: Record<string, string[]>) => json({ title, status, detail, errors }, status)

export const STEPS: WorkflowStep[] = [
  { key: 'setup', label: 'Parcel set up', order: 1, requiresAcquisitionGate: false },
  { key: 'title_survey', label: 'Title & survey', order: 2, requiresAcquisitionGate: false },
  { key: 'appraisal', label: 'Appraisal', order: 3, requiresAcquisitionGate: false },
  { key: 'offer', label: 'Offer', order: 4, requiresAcquisitionGate: true },
  { key: 'negotiation', label: 'Negotiation', order: 5, requiresAcquisitionGate: true },
  { key: 'closing', label: 'Closing', order: 6, requiresAcquisitionGate: true },
]

export const DEFAULT_STATUSES: ParcelStatus[] = [
  { id: 's-ident', name: 'Identified', color: '#64748b', sortOrder: 1, isActive: true, isInitial: true },
  { id: 's-neg', name: 'Negotiation', color: '#d97706', sortOrder: 2, isActive: true, isInitial: false },
  { id: 's-closed', name: 'Closed', color: '#16a34a', sortOrder: 3, isActive: true, isInitial: false },
]

const STANDARD_MILESTONES = ['Parcel set up', 'Notice to owner', 'Offer delivered', 'Agreement signed', 'Closed']
const ALWAYS_OVERRIDE = ['ProjectManager', 'Admin']
const ASSIGNABLE = ['TitleExaminer', 'Appraiser', 'Negotiator', 'Finance']

export interface FakeParcelsOptions extends FakeProjectsOptions {
  parcels?: Partial<Parcel>[]
  groups?: (Partial<ParcelGroup> & { projectId: string; name: string })[]
  tracts?: (Partial<ParentTract> & { projectId: string; number: string })[]
  customFields?: Partial<CustomField>[]
  deadlineRules?: Partial<DeadlineRule>[]
  /** "Today" for deadline maths, yyyy-MM-dd. */
  today?: string
  /** Make GET /parcels fail with a 500. */
  parcelListFails?: boolean
}

export interface FakeParcels extends FakeProjects {
  parcels: Parcel[]
  groups: ParcelGroup[]
  tracts: ParentTract[]
  statuses: ParcelStatus[]
  customFields: CustomField[]
  deadlineRules: DeadlineRule[]
  stepGate: { extraOverrideRoles: string[] }
}

const addDays = (iso: string, n: number) => {
  const d = new Date(`${iso}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + n)
  return d.toISOString().slice(0, 10)
}
const daysBetween = (a: string, b: string) => Math.round((Date.parse(`${b}T00:00:00Z`) - Date.parse(`${a}T00:00:00Z`)) / 86_400_000)

/** An in-memory stand-in for the Parcels API on top of the Projects fake: enough of the server's rules for the real screens to be exercised. */
export function fakeParcelsApi(opts: FakeParcelsOptions): FakeParcels {
  const base = fakeProjectsApi(opts)
  const today = opts.today ?? '2026-10-14'
  let seq = 0
  const id = (p: string) => `${p}${++seq}`

  const statuses = DEFAULT_STATUSES.map((s) => ({ ...s }))
  const groups: ParcelGroup[] = (opts.groups ?? []).map((g) => ({ id: g.id ?? id('g'), description: null, parcelCount: 0, ...g }))
  const tracts: ParentTract[] = (opts.tracts ?? []).map((t) => ({ id: t.id ?? id('t'), description: null, taxParcelNumbers: [], parcelCount: 0, ...t }))
  const customFields: CustomField[] = (opts.customFields ?? []).map((f, i) => ({
    id: id('cf'), entityType: 'Parcel', key: `f${i}`, label: `Field ${i}`, fieldType: 'Text', options: [], isRequired: false, isActive: true, sortOrder: i, ...f,
  }))
  const deadlineRules: DeadlineRule[] = (opts.deadlineRules ?? [
    { id: 'dr-offer', name: 'Offer within 90 days of the Notice to Owner', triggerMilestone: 'Notice to owner', targetMilestone: 'Offer delivered', days: 90, isActive: true, isPlaceholder: true },
  ]).map((r) => ({ id: id('dr'), name: 'Rule', triggerMilestone: 'A', targetMilestone: 'B', days: 1, isActive: true, isPlaceholder: false, ...r }))
  const stepGate = { extraOverrideRoles: [] as string[] }

  const can = (action: 'create' | 'read' | 'update' | 'delete', page = 'parcels') =>
    !!opts.rows?.find((r) => r.pageKey === page)?.[`can${action[0].toUpperCase()}${action.slice(1)}` as 'canCreate']
  const forbidden = () => problem(403, 'forbidden', 'Your role does not allow this.')
  const canOverride = () => ALWAYS_OVERRIDE.includes(opts.role) || stepGate.extraOverrideRoles.includes(opts.role)

  const statusOf = (sid: string) => statuses.find((s) => s.id === sid) ?? statuses[0]
  const mkMilestones = () => STANDARD_MILESTONES.map((name) => ({ id: id('m'), name, dueOn: null, completedOn: null, notes: null, isStandard: true, isOverdue: false }))

  const mk = (given: Partial<Parcel> & { projectId: string }): Parcel => {
    const g = Object.fromEntries(Object.entries(given).filter(([, v]) => v !== undefined)) as Partial<Parcel> & { projectId: string }
    const project = base.projects.find((p) => p.id === g.projectId)
    const status = statusOf(g.statusId ?? statuses.find((s) => s.isInitial)!.id)
    return {
      id: id('pc'), number: '001', projectNumber: project?.number ?? '', projectName: project?.name ?? '', legalDescription: null, address: null, areaSqFt: 43560,
      propertyType: null, groupId: null, groupName: null, parentTractId: null, parentTractNumber: null, taxParcelNumbers: [], acquisitionType: 'Standard',
      state: 'Active', supersededOn: null, sourceParcelIds: [], isRemnant: false, customValues: {}, noticeIssuedOn: null, noticeMethod: null, interests: [],
      milestones: mkMilestones(), siteVisits: [], deadlines: [], stepHistory: [], updatedAt: '2026-01-01T00:00:00Z',
      workflow: { steps: STEPS, currentKey: 'setup', nextKey: 'title_survey', acquisitionGateOpen: false, canOverride: false },
      ...g, statusId: status.id, statusName: status.name, statusColor: status.color,
    }
  }

  const parcels: Parcel[] = (opts.parcels ?? []).map((p) => mk(p as Partial<Parcel> & { projectId: string }))

  /** Re-derives everything a server response would compute from the parcel's stored facts. */
  function refresh(p: Parcel) {
    const project = base.projects.find((x) => x.id === p.projectId)
    const status = statusOf(p.statusId)
    p.statusName = status.name; p.statusColor = status.color
    p.groupName = groups.find((g) => g.id === p.groupId)?.name ?? null
    const tract = tracts.find((t) => t.id === p.parentTractId)
    p.parentTractNumber = tract?.number ?? null
    p.taxParcelNumbers = tract?.taxParcelNumbers ?? []
    const idx = Math.max(0, STEPS.findIndex((s) => s.key === p.workflow.currentKey))
    p.workflow = {
      steps: STEPS, currentKey: STEPS[idx].key, nextKey: STEPS[idx + 1]?.key ?? null,
      acquisitionGateOpen: !!project?.acquisitionGate.unlocked, canOverride: canOverride(),
    }
    const done = (name: string) => p.milestones.find((m) => m.name === name)?.completedOn ?? null
    p.milestones.forEach((m) => { m.isOverdue = !m.completedOn && !!m.dueOn && m.dueOn < today })
    p.deadlines = deadlineRules.filter((r) => r.isActive).map((r): Deadline => {
      const trigger = done(r.triggerMilestone)
      const dueOn = trigger ? addDays(trigger, r.days) : null
      const met = done(r.targetMilestone)
      let status: DeadlineStatus = 'NotStarted'
      let remaining: number | null = null
      if (dueOn) {
        remaining = daysBetween(today, dueOn)
        status = met ? (met <= dueOn ? 'Met' : 'MetLate') : remaining < 0 ? 'Overdue' : remaining <= 7 ? 'DueSoon' : 'Pending'
      }
      return { ruleId: r.id, name: r.name, triggerMilestone: r.triggerMilestone, targetMilestone: r.targetMilestone, days: r.days, triggerDate: trigger, dueOn, status, daysRemaining: remaining }
    })
  }
  parcels.forEach(refresh)

  const summary = (p: Parcel): ParcelSummary => ({
    id: p.id, projectId: p.projectId, projectNumber: p.projectNumber, number: p.number, address: p.address, areaSqFt: p.areaSqFt, propertyType: p.propertyType,
    statusId: p.statusId, statusName: p.statusName, statusColor: p.statusColor, groupId: p.groupId, groupName: p.groupName, parentTractId: p.parentTractId,
    parentTractNumber: p.parentTractNumber, acquisitionType: p.acquisitionType, currentStepKey: p.workflow.currentKey, isRemnant: p.isRemnant, state: p.state,
  })
  const live = (projectId?: string) => parcels.filter((p) => p.state === 'Active' && (!projectId || p.projectId === projectId))
  const countFor = () => {
    groups.forEach((g) => { g.parcelCount = parcels.filter((p) => p.groupId === g.id).length })
    tracts.forEach((t) => { t.parcelCount = parcels.filter((p) => p.parentTractId === t.id).length })
  }
  const nextNumber = (projectId: string) => {
    const nums = parcels.filter((p) => p.projectId === projectId && /^\d+$/.test(p.number)).map((p) => Number(p.number))
    return String((nums.length ? Math.max(...nums) : 0) + 1).padStart(3, '0')
  }
  const bucket = (idv: string | null, name: string, color: string | null, items: Parcel[]) => {
    const areaSqFt = items.reduce((s, p) => s + p.areaSqFt, 0)
    return { id: idv, name, color, count: items.length, areaSqFt, acres: areaSqFt / 43560 }
  }

  const fetchImpl = async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://x')
    const path = url.pathname
    const structure = /^\/api\/v1\/projects\/([^/]+)\/(groups|tracts|parcel-rollup)(?:\/([^/]+))?$/.exec(path)
    const mine = path.startsWith('/api/v1/parcels') || path.startsWith('/api/v1/parcel-rules') || path.startsWith('/api/v1/reminders') || !!structure
    if (!mine) return base.fetch(input, init)

    const headers = (init?.headers ?? {}) as Record<string, string>
    if (headers.Authorization !== 'Bearer good-token') return json({ title: 'unauthorized' }, 401)
    const method = init?.method ?? 'GET'
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    base.calls.push({ key: `${method} ${path}${url.search}`, body })
    const touch = (p: Parcel) => { refresh(p); p.updatedAt = '2026-10-14T00:00:00Z'; countFor() }

    // ── project structure ──
    if (structure) {
      const [, projectId, kind, itemId] = structure
      if (!can('read')) return forbidden()
      if (kind === 'parcel-rollup') {
        const gid = url.searchParams.get('groupId')
        const items = live(projectId).filter((p) => !gid || p.groupId === gid)
        const totalArea = items.reduce((s, p) => s + p.areaSqFt, 0)
        return json({
          totalCount: items.length, totalAreaSqFt: totalArea, totalAcres: totalArea / 43560,
          byStatus: statuses.filter((s) => items.some((p) => p.statusId === s.id)).map((s) => bucket(s.id, s.name, s.color, items.filter((p) => p.statusId === s.id))),
          byGroup: [
            ...groups.filter((g) => g.projectId === projectId).map((g) => bucket(g.id, g.name, null, items.filter((p) => p.groupId === g.id))),
            ...(items.some((p) => !p.groupId) ? [bucket(null, 'No group', null, items.filter((p) => !p.groupId))] : []),
          ],
        })
      }
      countFor()
      if (kind === 'groups') {
        if (method === 'GET') return json(groups.filter((g) => g.projectId === projectId))
        if (method === 'POST') {
          if (!can('create')) return forbidden()
          if (!String(body.name ?? '').trim()) return problem(400, 'validation_failed', 'Validation failed.', { Name: ['Group name is required.'] })
          if (groups.some((g) => g.projectId === projectId && g.name.toLowerCase() === body.name.toLowerCase())) return problem(409, 'group_exists', `This project already has a group named '${body.name}'.`)
          const g: ParcelGroup = { id: id('g'), projectId, name: body.name, description: body.description ?? null, parcelCount: 0 }
          groups.push(g)
          return json(g, 201)
        }
        if (method === 'DELETE') {
          if (!can('delete')) return forbidden()
          const g = groups.find((x) => x.id === itemId)
          if (!g) return problem(404, 'not_found', 'Group was not found.')
          if (parcels.some((p) => p.groupId === g.id)) return problem(409, 'group_not_empty', `The group '${g.name}' still has parcels. Move them to another group first.`)
          groups.splice(groups.indexOf(g), 1)
          return new Response(null, { status: 204 })
        }
      }
      if (kind === 'tracts') {
        if (method === 'GET') return json(tracts.filter((t) => t.projectId === projectId))
        if (method === 'POST') {
          if (!can('create')) return forbidden()
          if (!String(body.number ?? '').trim()) return problem(400, 'validation_failed', 'Validation failed.', { Number: ['Tract number is required.'] })
          const t: ParentTract = { id: id('t'), projectId, number: body.number, description: body.description ?? null, taxParcelNumbers: body.taxParcelNumbers ?? [], parcelCount: 0 }
          tracts.push(t)
          return json(t, 201)
        }
      }
      return problem(404, 'not_found', 'Unknown route.')
    }

    // ── reminders ──
    if (path === '/api/v1/reminders') {
      if (!can('read')) return forbidden()
      const within = Number(url.searchParams.get('withinDays') ?? 7)
      const out: Reminder[] = []
      for (const p of live()) {
        for (const d of p.deadlines) {
          if (d.dueOn && (d.status === 'Overdue' || (d.status === 'DueSoon' && (d.daysRemaining ?? 99) <= within))) {
            out.push({ parcelId: p.id, parcelNumber: p.number, projectId: p.projectId, projectNumber: p.projectNumber, ruleName: d.name, targetMilestone: d.targetMilestone, dueOn: d.dueOn, status: d.status as 'Overdue' | 'DueSoon', daysRemaining: d.daysRemaining ?? 0 })
          }
        }
      }
      return json(out.sort((a, b) => a.dueOn.localeCompare(b.dueOn)))
    }

    // ── rules ──
    if (path.startsWith('/api/v1/parcel-rules')) {
      const [, , , , kind, rid] = path.split('/')
      if (method === 'GET' && !can('read')) return forbidden()
      if (method !== 'GET' && !can('update', 'administration')) return forbidden()
      if (kind === 'statuses') {
        if (method === 'GET') return json([...statuses].sort((a, b) => a.sortOrder - b.sortOrder))
        if (!String(body.name ?? '').trim()) return problem(400, 'validation_failed', 'Validation failed.', { Name: ['Status name is required.'] })
        if (statuses.some((s) => s.id !== rid && s.name.toLowerCase() === body.name.toLowerCase())) return problem(409, 'status_exists', `There is already a status named '${body.name}'.`)
        if (body.isInitial) statuses.forEach((s) => { s.isInitial = false })
        if (method === 'POST') { const s = { id: id('s'), ...body }; statuses.push(s); return json(s, 201) }
        const s = statuses.find((x) => x.id === rid)
        if (!s) return problem(404, 'not_found', 'Status was not found.')
        Object.assign(s, body)
        parcels.forEach(refresh)
        return json(s)
      }
      if (kind === 'custom-fields') {
        if (method === 'GET') return json([...customFields].sort((a, b) => a.sortOrder - b.sortOrder))
        if (!/^[a-z][a-z0-9_]{0,39}$/.test(body.key ?? '')) return problem(400, 'validation_failed', 'Validation failed.', { Key: ['The key starts with a lower-case letter and uses lower-case letters, digits and _ (40 characters at most).'] })
        if (method === 'POST') {
          if (customFields.some((f) => f.key === body.key)) return problem(409, 'custom_field_exists', `A field with the key '${body.key}' already exists.`)
          const f: CustomField = { id: id('cf'), entityType: 'Parcel', ...body }
          customFields.push(f)
          return json(f, 201)
        }
        const f = customFields.find((x) => x.id === rid)
        if (!f) return problem(404, 'not_found', 'Field was not found.')
        Object.assign(f, { label: body.label, options: body.options, isRequired: body.isRequired, isActive: body.isActive, sortOrder: body.sortOrder })
        return json(f)
      }
      if (kind === 'deadline-rules') {
        if (method === 'GET') return json(deadlineRules)
        if (body.days < 0) return problem(400, 'validation_failed', 'Validation failed.', { Days: ['Days must be zero or more.'] })
        if (method === 'POST') { const r: DeadlineRule = { id: id('dr'), isPlaceholder: false, ...body }; deadlineRules.push(r); parcels.forEach(refresh); return json(r, 201) }
        const r = deadlineRules.find((x) => x.id === rid)
        if (!r) return problem(404, 'not_found', 'Rule was not found.')
        Object.assign(r, body, { isPlaceholder: false })
        parcels.forEach(refresh)
        return json(r)
      }
      if (kind === 'workflows') {
        const all: Workflow[] = (['Standard', 'Donation', 'DeveloperConveyance', 'Early'] as const).map((t) => ({ acquisitionType: t, steps: STEPS }))
        return json(all)
      }
      if (kind === 'step-gate') {
        if (method === 'PUT') stepGate.extraOverrideRoles = body.extraOverrideRoles ?? []
        return json({ alwaysRoles: ALWAYS_OVERRIDE, extraOverrideRoles: stepGate.extraOverrideRoles, assignableRoles: ASSIGNABLE })
      }
      return problem(404, 'not_found', 'Unknown route.')
    }

    // ── parcels ──
    const rest = path.slice('/api/v1/parcels'.length).split('/').filter(Boolean)
    countFor()

    if (rest.length === 0 && method === 'GET') {
      if (!can('read')) return forbidden()
      if (opts.parcelListFails) return problem(500, 'internal_error', 'An unexpected error occurred.')
      const q = (url.searchParams.get('search') ?? '').toLowerCase()
      const f = (k: string) => url.searchParams.get(k)
      const all = parcels
        .filter((p) => p.state === 'Active')
        .filter((p) => !f('projectId') || p.projectId === f('projectId'))
        .filter((p) => !f('statusId') || p.statusId === f('statusId'))
        .filter((p) => !f('groupId') || p.groupId === f('groupId'))
        .filter((p) => !q || p.number.toLowerCase().includes(q) || (p.address ?? '').toLowerCase().includes(q))
        .sort((a, b) => a.projectNumber.localeCompare(b.projectNumber) || a.number.localeCompare(b.number))
      const page = Number(f('page') ?? 1)
      const pageSize = Number(f('pageSize') ?? 50)
      return json({ items: all.slice((page - 1) * pageSize, page * pageSize).map(summary), total: all.length, page, pageSize })
    }

    const validateFields = (b: any, projectId: string, selfId?: string) => {
      if (Number.isNaN(Number(b.areaSqFt)) || b.areaSqFt < 0) return problem(400, 'validation_failed', 'Validation failed.', { AreaSqFt: ['Area must be zero or more square feet.'] })
      for (const def of customFields.filter((c) => c.isActive)) {
        const v = b.customValues?.[def.key]
        if (def.isRequired && !v) return problem(400, 'validation_failed', 'Validation failed.', { CustomValues: [`${def.label} is required.`] })
      }
      if (b.number && parcels.some((p) => p.projectId === projectId && p.id !== selfId && p.number.toLowerCase() === String(b.number).toLowerCase()))
        return problem(409, 'parcel_number_exists', `This project already has a parcel numbered '${b.number}'.`)
      return null
    }
    const assign = (p: Parcel, b: any) => {
      p.number = b.number ?? p.number; p.parentTractId = b.parentTractId; p.groupId = b.groupId; p.legalDescription = b.legalDescription; p.address = b.address
      p.areaSqFt = b.areaSqFt; p.propertyType = b.propertyType; p.acquisitionType = b.acquisitionType
      if (b.statusId) p.statusId = b.statusId
      p.customValues = Object.fromEntries(Object.entries(b.customValues ?? {}).filter(([, v]) => v !== null && v !== '')) as Record<string, string>
    }

    if (rest.length === 0 && method === 'POST') {
      if (!can('create')) return forbidden()
      if (!base.projects.some((p) => p.id === body.projectId)) return problem(404, 'not_found', 'Project was not found.')
      const bad = validateFields(body, body.projectId)
      if (bad) return bad
      const p = mk({ projectId: body.projectId, number: body.number ?? nextNumber(body.projectId) })
      assign(p, { ...body, number: body.number ?? p.number })
      p.milestones.find((m) => m.name === 'Parcel set up')!.completedOn = today
      parcels.push(p); touch(p)
      return json(p, 201)
    }

    if (rest[0] === 'batch-update' && method === 'POST') {
      if (!can('update')) return forbidden()
      const chosen = (body.parcelIds as string[]).map((x) => parcels.find((p) => p.id === x))
      if (chosen.some((p) => !p)) return problem(404, 'not_found', 'A parcel was not found.')
      for (const p of chosen as Parcel[]) {
        if (body.statusId) p.statusId = body.statusId
        if (body.clearGroup) p.groupId = null
        else if (body.groupId) p.groupId = body.groupId
        touch(p)
      }
      return json({ updated: chosen.length })
    }

    if (rest[0] === 'combine' && method === 'POST') {
      if (!can('create')) return forbidden()
      const sources = (body.parcelIds as string[]).map((x) => parcels.find((p) => p.id === x)).filter(Boolean) as Parcel[]
      if (sources.length < 2) return problem(400, 'validation_failed', 'Validation failed.', { ParcelIds: ['Choose at least two parcels to combine.'] })
      if (new Set(sources.map((s) => s.projectId)).size > 1) return problem(400, 'invalid_combination', 'Parcels from different projects cannot be combined.')
      const first = sources[0]
      const p = mk({ projectId: first.projectId, number: body.number ?? nextNumber(first.projectId), areaSqFt: sources.reduce((s, x) => s + x.areaSqFt, 0), sourceParcelIds: sources.map((s) => s.id) })
      sources.forEach((s) => { s.state = 'Combined' })
      parcels.push(p); touch(p)
      return json(p, 201)
    }

    const parcel = parcels.find((p) => p.id === rest[0])
    if (!parcel) return problem(404, 'not_found', `Parcel with key '${rest[0]}' was not found.`)
    const sub = rest[1]
    /** The server's rules for milestone dates (see UpdateParcelMilestoneCommand): plausible years, no future completion, notice and set-up are special. */
    const milestoneProblem = (b: any, existing?: Parcel['milestones'][number]) => {
      const bad = (field: string, msg: string) => problem(400, 'validation_failed', 'Validation failed.', { [field]: [msg] })
      const year = (d?: string | null) => (d ? Number(d.slice(0, 4)) : null)
      if (year(b.dueOn) !== null && (year(b.dueOn)! < 1990 || year(b.dueOn)! > 2100)) return bad('DueOn', 'Enter a valid due date.')
      if (year(b.completedOn) !== null && (year(b.completedOn)! < 1990 || year(b.completedOn)! > 2100)) return bad('CompletedOn', 'Enter a valid completion date.')
      if (b.completedOn && b.completedOn > today) return bad('CompletedOn', 'A milestone cannot be completed in the future.')
      if (existing?.name === 'Notice to owner' && (b.completedOn ?? null) !== existing.completedOn)
        return problem(400, 'milestone_notice_managed', 'The Notice to owner date is set by recording the notice (date and delivery method), not by editing this milestone.')
      if (existing?.name === 'Parcel set up' && !b.completedOn) return bad('CompletedOn', 'The set-up date cannot be cleared.')
      return null
    }
    const frozen = () => (parcel.state !== 'Active' ? problem(409, 'parcel_not_active', `Parcel ${parcel.number} was ${parcel.state.toLowerCase()} and can no longer be changed.`) : null)

    if (!sub) {
      if (method === 'GET') { refresh(parcel); return can('read') ? json(parcel) : forbidden() }
      if (method === 'PUT') {
        if (!can('update')) return forbidden()
        const bad = frozen() ?? validateFields(body, parcel.projectId, parcel.id)
        if (bad) return bad
        assign(parcel, body); touch(parcel)
        return json(parcel)
      }
    }

    if (sub === 'split' && method === 'POST') {
      if (!can('create')) return forbidden()
      const bad = frozen()
      if (bad) return bad
      const areas = (body.parts as { areaSqFt: number }[]).map((x) => x.areaSqFt)
      const used = areas.reduce((a, b) => a + b, 0)
      if (used > parcel.areaSqFt) return problem(400, 'validation_failed', 'Validation failed.', { Parts: ['The parts add up to more than the parcel.'] })
      const parts = areas.map((area, i) => mk({ projectId: parcel.projectId, number: `${parcel.number}-${String.fromCharCode(65 + i)}`, areaSqFt: area, sourceParcelIds: [parcel.id], groupId: parcel.groupId, parentTractId: parcel.parentTractId }))
      const remnant = parcel.areaSqFt - used > 0
        ? mk({ projectId: parcel.projectId, number: `${parcel.number}-R`, areaSqFt: parcel.areaSqFt - used, sourceParcelIds: [parcel.id], isRemnant: true, groupId: parcel.groupId, parentTractId: parcel.parentTractId })
        : null
      parcel.state = 'Split'
      const created = remnant ? [...parts, remnant] : parts
      parcels.push(...created); created.forEach(touch); touch(parcel)
      return json({ source: parcel, parts: parts.map(summary), remnant: remnant ? summary(remnant) : null }, 201)
    }

    const guard = frozen()
    if (guard) return guard

    if (sub === 'interests') {
      if (method === 'POST') {
        if (!can('create')) return forbidden()
        if (Number.isNaN(Number(body.areaSqFt)) || body.areaSqFt < 0) return problem(400, 'validation_failed', 'Validation failed.', { AreaSqFt: ['Area must be zero or more square feet.'] })
        parcel.interests.push({ id: id('i'), type: body.type, areaSqFt: body.areaSqFt, notes: body.notes ?? null })
        touch(parcel)
        return json(parcel, 201)
      }
      if (method === 'DELETE') {
        if (!can('delete')) return forbidden()
        parcel.interests = parcel.interests.filter((i) => i.id !== rest[2]); touch(parcel)
        return json(parcel)
      }
    }
    if (sub === 'milestones') {
      if (method === 'POST') {
        if (!can('create')) return forbidden()
        if (!String(body.name ?? '').trim()) return problem(400, 'validation_failed', 'Validation failed.', { Name: ['Milestone name is required.'] })
        const badDate = milestoneProblem(body)
        if (badDate) return badDate
        if (parcel.milestones.some((m) => m.name.toLowerCase() === body.name.toLowerCase())) return problem(409, 'milestone_exists', `This parcel already has a milestone named '${body.name}'.`)
        parcel.milestones.push({ id: id('m'), name: body.name, dueOn: body.dueOn ?? null, completedOn: body.completedOn ?? null, notes: body.notes ?? null, isStandard: false, isOverdue: false })
        touch(parcel)
        return json(parcel, 201)
      }
      const m = parcel.milestones.find((x) => x.id === rest[2])
      if (!m) return problem(404, 'not_found', 'Milestone was not found.')
      if (method === 'PUT') {
        if (!can('update')) return forbidden()
        const badEdit = milestoneProblem(body, m)
        if (badEdit) return badEdit
        Object.assign(m, { dueOn: body.dueOn ?? null, completedOn: body.completedOn ?? null, notes: body.notes ?? null }); touch(parcel)
        return json(parcel)
      }
      if (method === 'DELETE') {
        if (!can('delete')) return forbidden()
        if (m.isStandard) return problem(409, 'milestone_standard', `'${m.name}' is a standard milestone and cannot be removed.`)
        parcel.milestones = parcel.milestones.filter((x) => x.id !== m.id); touch(parcel)
        return json(parcel)
      }
    }
    if (sub === 'site-visits') {
      if (method === 'POST') {
        if (!can('create')) return forbidden()
        if (!body.visitedOn) return problem(400, 'validation_failed', 'Validation failed.', { VisitedOn: ['Visit date is required.'] })
        parcel.siteVisits.push({ id: id('v'), visitedOn: body.visitedOn, notes: body.notes ?? null, ownerContactConfirmed: !!body.ownerContactConfirmed })
        touch(parcel)
        return json(parcel, 201)
      }
      if (method === 'DELETE') {
        if (!can('delete')) return forbidden()
        parcel.siteVisits = parcel.siteVisits.filter((v) => v.id !== rest[2]); touch(parcel)
        return json(parcel)
      }
    }
    if (sub === 'notice' && method === 'PUT') {
      if (!can('update')) return forbidden()
      parcel.noticeIssuedOn = body.issuedOn ?? null; parcel.noticeMethod = body.method ?? null
      const m = parcel.milestones.find((x) => x.name === 'Notice to owner')!
      m.completedOn = body.issuedOn ?? null
      touch(parcel)
      return json(parcel)
    }
    if (sub === 'advance' && method === 'POST') {
      if (!can('update')) return forbidden()
      const from = STEPS.findIndex((s) => s.key === parcel.workflow.currentKey)
      const to = STEPS.findIndex((s) => s.key === body.targetStepKey)
      if (to < 0) return problem(400, 'validation_failed', 'Validation failed.', { TargetStepKey: ['That step does not exist.'] })
      if (to === from) return problem(409, 'already_on_step', 'The parcel is already on that step.')
      const gateOpen = !!base.projects.find((p) => p.id === parcel.projectId)?.acquisitionGate.unlocked
      if (to > from && STEPS[to].requiresAcquisitionGate && !gateOpen) return problem(409, 'acquisition_gate_closed', 'Offers and negotiations are on hold until the project gate opens.')
      const override = to !== from + 1
      if (override && !canOverride()) return problem(403, 'forbidden', 'Your role may not move a parcel out of order.')
      if (override && !String(body.overrideReason ?? '').trim()) return problem(400, 'validation_failed', 'Validation failed.', { OverrideReason: ['A reason is required to move out of order.'] })
      parcel.stepHistory.unshift({ id: id('ev'), fromStepKey: STEPS[from].key, toStepKey: STEPS[to].key, isOverride: override, reason: override ? body.overrideReason : null, at: '2026-10-14T00:00:00Z', by: 'u1' })
      parcel.workflow.currentKey = STEPS[to].key
      touch(parcel)
      return json(parcel)
    }
    return problem(404, 'not_found', 'Unknown route.')
  }

  return {
    ...base,
    fetch: fetchImpl as typeof fetch,
    parcels, groups, tracts, statuses, customFields, deadlineRules, stepGate,
  }
}
