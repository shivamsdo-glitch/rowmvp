import type { LibraryAgent, LibraryField, LibraryFieldType } from '@/api/library'
import { fakeAuthFetch } from './fake-api'

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}
const problem = (status: number, title: string, detail: string, errors?: Record<string, string[]>) => json({ title, status, detail, errors }, status)

interface Stored { id: string; key: string; label: string; fieldType: LibraryFieldType; isSystem: boolean; values: { id: string; value: string; isActive: boolean; isDefault: boolean }[] }

export interface FakeLibraryOptions {
  role: string
  rows: Parameters<typeof fakeAuthFetch>[0]['rows']
  fields?: Partial<Stored>[]
  /** Make GET /library/fields fail with a 500. */
  listFails?: boolean
  agents?: LibraryAgent[]
}

export interface FakeLibrary {
  fetch: typeof fetch
  calls: { key: string; body?: any }[]
  fields: Stored[]
  agents: LibraryAgent[]
}

export const DEFAULT_FIELDS: Partial<Stored>[] = [
  { id: 'f-status', key: 'parcel_status', label: 'Parcel status', fieldType: 'List', isSystem: true, values: [
    { id: 's1', value: 'Inactive', isActive: true, isDefault: true }, { id: 's2', value: 'In Design', isActive: true, isDefault: false },
  ] },
  { id: 'f-poss', key: 'possession_type', label: 'Possession type', fieldType: 'List', isSystem: true, values: [
    { id: 'p1', value: 'PUA', isActive: true, isDefault: false }, { id: 'p2', value: 'Closing', isActive: true, isDefault: false },
    { id: 'p3', value: 'Notice of Deposit', isActive: true, isDefault: false }, { id: 'p4', value: 'Judgement', isActive: false, isDefault: false },
  ] },
  { id: 'f-agent', key: 'agent_type', label: 'Agent type', fieldType: 'List', isSystem: true, values: [
    { id: 'a1', value: 'Surveyor', isActive: true, isDefault: false }, { id: 'a2', value: 'Negotiator', isActive: true, isDefault: false },
    { id: 'a3', value: 'Retired Job', isActive: false, isDefault: false },
  ] },
  { id: 'f-cad', key: 'cad_number', label: 'CAD number', fieldType: 'Text', isSystem: true, values: [] },
]

/** An in-memory stand-in for the Library API with the server's rules for duplicates, types and system fields. */
export function fakeLibraryApi(opts: FakeLibraryOptions): FakeLibrary {
  const auth = fakeAuthFetch({ rows: opts.rows, role: opts.role }) as typeof fetch
  let seq = 0
  const id = (p: string) => `${p}${++seq}`
  const fields: Stored[] = (opts.fields ?? DEFAULT_FIELDS).map((f) => ({ id: id('f'), key: 'k', label: 'Field', fieldType: 'List', isSystem: false, values: [], ...f } as Stored))
  const calls: FakeLibrary['calls'] = []
  const can = (a: 'create' | 'read' | 'update' | 'delete') =>
    !!opts.rows?.find((r) => r.pageKey === 'library')?.[`can${a[0].toUpperCase()}${a.slice(1)}` as 'canCreate']
  const forbidden = () => problem(403, 'forbidden', 'Your role does not allow this.')
  const dto = (f: Stored): LibraryField => ({ ...f, isLibraryDriven: f.fieldType === 'List', values: f.values.map((v, i) => ({ ...v, sortOrder: i })) })
  const agents: LibraryAgent[] = opts.agents ?? [
    { id: 'ag1', name: 'Casey Surveyor', agentType: 'Surveyor', company: 'Acme Survey', phone: '555-0100', email: 'casey@example.test', isActive: true },
    { id: 'ag2', name: 'Dana Negotiator', agentType: 'Negotiator', company: 'Stateside ROW', phone: null, email: null, isActive: true },
    { id: 'ag3', name: 'Old Hand', agentType: 'Retired Job', company: null, phone: null, email: null, isActive: false },
  ]
  const norm = (s: string) => s.trim().toUpperCase()

  const fetchImpl = async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://x')
    if (url.pathname.startsWith('/api/v1/library/agents')) {
      const hdrs = (init?.headers ?? {}) as Record<string, string>
      if (hdrs.Authorization !== 'Bearer good-token') return json({ title: 'unauthorized' }, 401)
      const meth = init?.method ?? 'GET'
      const payload = init?.body ? JSON.parse(String(init.body)) : undefined
      calls.push({ key: `${meth} ${url.pathname}`, body: payload })
      if (meth === 'GET') return can('read') ? json(agents) : problem(403, 'forbidden', 'Your role does not allow this.')
      if (!can(meth === 'POST' ? 'create' : 'update')) return problem(403, 'forbidden', 'Your role does not allow this.')
      const agentTypeField = fields.find((x) => x.key === 'agent_type')
      const type = agentTypeField?.values.find((v) => norm(v.value) === norm(String(payload.agentType ?? '')))
      if (!String(payload.name ?? '').trim()) return problem(400, 'validation_failed', 'Validation failed.', { Name: ['Agent name is required.'] })
      if (!type) return problem(400, 'unknown_agent_type', `'${payload.agentType}' is not in the Agent type list. Add it to the Library first.`)
      const key = (a: { name: string; agentType: string; company: string | null }) => `${norm(a.name)}|${norm(a.agentType)}|${norm(a.company ?? '')}`
      const existingId = meth === 'PUT' ? url.pathname.split('/').pop() : undefined
      if (agents.some((a) => a.id !== existingId && key(a) === key({ name: payload.name, agentType: type.value, company: payload.company ?? null }))) {
        return problem(409, 'library_agent_exists', `'${payload.name}' is already listed as ${type.value}.`)
      }
      if (meth === 'POST') {
        const a: LibraryAgent = { id: id('ag'), name: payload.name.trim(), agentType: type.value, company: payload.company ?? null, phone: payload.phone ?? null, email: payload.email ?? null, isActive: true }
        agents.push(a)
        return json(a, 201)
      }
      const a = agents.find((x) => x.id === existingId)
      if (!a) return problem(404, 'not_found', 'Agent was not found.')
      Object.assign(a, { name: payload.name.trim(), agentType: type.value, company: payload.company ?? null, phone: payload.phone ?? null, email: payload.email ?? null, isActive: !!payload.isActive })
      return json(a)
    }
    const m = /^\/api\/v1\/library\/fields(?:\/([^/]+)(?:\/(values|import)(?:\/([^/]+))?)?)?$/.exec(url.pathname)
    if (!m) return auth(input, init)
    const headers = (init?.headers ?? {}) as Record<string, string>
    if (headers.Authorization !== 'Bearer good-token') return json({ title: 'unauthorized' }, 401)
    const method = init?.method ?? 'GET'
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    calls.push({ key: `${method} ${url.pathname}`, body })
    const [, fid, sub, vid] = m

    if (!fid) {
      if (method === 'GET') {
        if (!can('read')) return forbidden()
        if (opts.listFails) return problem(500, 'server_error', 'Boom.')
        return json(fields.map((f) => ({ id: f.id, key: f.key, label: f.label, fieldType: f.fieldType, isSystem: f.isSystem, valueCount: f.values.length, activeCount: f.values.filter((v) => v.isActive).length, isLibraryDriven: f.fieldType === 'List' })))
      }
      if (!can('create')) return forbidden()
      if (!String(body?.label ?? '').trim()) return problem(400, 'validation_failed', 'Validation failed.', { Label: ['Name is required.'] })
      if (fields.some((f) => f.label.toLowerCase() === body.label.trim().toLowerCase())) return problem(409, 'library_field_exists', `There is already a field named '${body.label.trim()}'.`)
      const f: Stored = { id: id('f'), key: body.label.trim().toLowerCase().replace(/[^a-z0-9]+/g, '_'), label: body.label.trim(), fieldType: body.fieldType, isSystem: false, values: [] }
      fields.push(f)
      return json(dto(f), 201)
    }

    const f = fields.find((x) => x.id === fid)
    if (method === 'GET' && !sub) return can('read') ? (f ? json(dto(f)) : problem(404, 'not_found', 'Library field was not found.')) : forbidden()
    if (!f) return problem(404, 'not_found', 'Library field was not found.')

    if (method === 'PUT' && !sub) {
      if (!can('update')) return forbidden()
      if (!String(body?.label ?? '').trim()) return problem(400, 'validation_failed', 'Validation failed.', { Label: ['Name is required.'] })
      if (fields.some((x) => x.id !== f.id && x.label.toLowerCase() === body.label.trim().toLowerCase())) return problem(409, 'library_field_exists', `There is already a field named '${body.label.trim()}'.`)
      if (body.fieldType !== f.fieldType) {
        if (f.isSystem) return problem(400, 'business_rule_violation', `The type of '${f.label}' is fixed because the product uses it.`)
        if (f.values.length) return problem(409, 'library_field_has_values', `'${f.label}' already has values. Switch them off instead of changing the type.`)
        f.fieldType = body.fieldType
      }
      f.label = body.label.trim()
      return json(dto(f))
    }
    if (f.fieldType !== 'List') return problem(400, 'not_a_list', `'${f.label}' is not a list, so it has no values to choose from.`)

    if (sub === 'values' && method === 'POST') {
      if (!can('create')) return forbidden()
      const v = String(body?.value ?? '').trim()
      if (!v) return problem(400, 'validation_failed', 'Validation failed.', { Value: ['Value is required.'] })
      if (f.values.some((x) => norm(x.value) === norm(v))) return problem(409, 'library_value_exists', `'${v}' is already in ${f.label}.`)
      f.values.push({ id: id('v'), value: v, isActive: true, isDefault: false })
      return json(dto(f), 201)
    }
    if (sub === 'values' && method === 'PUT') {
      if (!can('update')) return forbidden()
      const row = f.values.find((x) => x.id === vid)
      if (!row) return problem(404, 'not_found', 'Library value was not found.')
      const v = String(body?.value ?? '').trim()
      if (!v) return problem(400, 'validation_failed', 'Validation failed.', { Value: ['Value is required.'] })
      if (f.values.some((x) => x.id !== row.id && norm(x.value) === norm(v))) return problem(409, 'library_value_exists', `'${v}' is already in ${f.label}.`)
      if (row.isDefault && !body.isActive) return problem(400, 'business_rule_violation', 'The initial status must be active.')
      row.value = v
      row.isActive = !!body.isActive
      return json(dto(f))
    }
    if (sub === 'import' && method === 'POST') {
      if (!can('update')) return forbidden()
      let added = 0
      let there = 0
      for (const raw of body.values as string[]) {
        if (f.values.some((x) => norm(x.value) === norm(raw))) { there++; continue }
        f.values.push({ id: id('v'), value: raw.trim(), isActive: true, isDefault: false })
        added++
      }
      return json({ added, alreadyThere: there, field: dto(f) })
    }
    return problem(404, 'not_found', 'Unknown route.')
  }

  return { fetch: fetchImpl as typeof fetch, calls, fields, agents }
}
