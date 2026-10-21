import type { DiaryEntry, ParcelNote, TeamMember } from '@/api/parcel-records'
import { fakePeopleApi, type FakePeople, type FakePeopleOptions } from './fake-people'
import { FAKE_LIBRARY_AGENTS } from './fake-parcels'

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}
const problem = (status: number, title: string, detail: string, errors?: Record<string, string[]>) => json({ title, status, detail, errors }, status)

export interface FakeRecordsOptions extends FakePeopleOptions {
  team?: (Partial<TeamMember> & { parcelId: string; name: string })[]
  diary?: (Partial<DiaryEntry> & { parcelId: string })[]
  notes?: (Partial<ParcelNote> & { parcelId: string; text: string })[]
  /** "Today" for the diary's no-future-dates rule, yyyy-MM-dd. */
  today?: string
  /** Make GET /parcels/{id}/team fail with a 500. */
  teamFails?: boolean
}

export interface FakeRecords extends FakePeople {
  team: TeamMember[]
  diary: DiaryEntry[]
  notes: ParcelNote[]
}

/** An in-memory stand-in for a parcel's team, diary and notes, with the server's rules and permission pages. */
export function fakeRecordsApi(opts: FakeRecordsOptions): FakeRecords {
  const base = fakePeopleApi(opts)
  const today = opts.today ?? '2026-10-14'
  let seq = 0
  const id = (p: string) => `${p}${++seq}`
  const can = (page: string, action: 'create' | 'read' | 'update' | 'delete') =>
    !!opts.rows?.find((r) => r.pageKey === page)?.[`can${action[0].toUpperCase()}${action.slice(1)}` as 'canCreate']
  const forbidden = () => problem(403, 'forbidden', 'Your role does not allow this.')
  const key = (s: string) => s.trim().split(/\s+/).join(' ').toUpperCase()

  const team: TeamMember[] = (opts.team ?? []).map((m) => ({
    id: id('tm'), role: 'Other', agentId: null, organization: null, email: null, phone: null, notes: null, updatedAt: '2026-01-01T00:00:00Z', ...m,
  }))
  const diary: DiaryEntry[] = (opts.diary ?? []).map((d) => ({
    id: id('de'), occurredOn: today, method: 'Call', direction: 'Outgoing', contactName: 'Someone', partyId: null, summary: 'Spoke.', outcome: null,
    createdAt: '2026-01-01T00:00:00Z', createdBy: 'u1', authorName: 'Dev User', ...d,
  }))
  const notes: ParcelNote[] = (opts.notes ?? []).map((n) => ({ id: id('nt'), createdAt: '2026-01-01T00:00:00Z', createdBy: 'u1', authorName: 'Dev User', ...n }))

  const fetchImpl = async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://x')
    const m = /^\/api\/v1\/parcels\/([^/]+)\/(team|diary|notes)(?:\/([^/]+))?$/.exec(url.pathname)
    if (!m) return base.fetch(input, init)
    const headers = (init?.headers ?? {}) as Record<string, string>
    if (headers.Authorization !== 'Bearer good-token') return json({ title: 'unauthorized' }, 401)
    const method = init?.method ?? 'GET'
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    base.calls.push({ key: `${method} ${url.pathname}`, body })
    const [, parcelId, part, rid] = m
    const parcel = base.parcels.find((p) => p.id === parcelId)
    const page = part === 'diary' ? 'negotiations' : 'parcels'
    const act = method === 'GET' ? 'read' : method === 'POST' ? 'create' : method === 'PUT' ? 'update' : 'delete'
    if (!can(page, act)) return forbidden()
    if (!parcel) return problem(404, 'not_found', 'Parcel was not found.')
    if (method !== 'GET' && parcel.state !== 'Active') return problem(409, 'parcel_not_active', `Parcel ${parcel.number} can no longer be changed.`)

    if (part === 'team') {
      if (method === 'GET') {
        if (opts.teamFails) return problem(500, 'server_error', 'Boom.')
        return json(team.filter((t) => t.parcelId === parcelId).sort((a, b) => a.role.localeCompare(b.role) || a.name.localeCompare(b.name)))
      }
      if (method === 'DELETE') {
        const i = team.findIndex((t) => t.id === rid && t.parcelId === parcelId)
        if (i < 0) return problem(404, 'not_found', 'Team member was not found.')
        team.splice(i, 1)
        return new Response(null, { status: 204 })
      }
      const agent = FAKE_LIBRARY_AGENTS.find((x) => x.id === body?.agentId)
      if (!body?.agentId) return problem(400, 'validation_failed', 'Validation failed.', { AgentId: ['Choose a person from the Library.'] })
      if (!agent) return problem(404, 'not_found', 'Library agent was not found.')
      if (!agent.isActive) return problem(400, 'agent_inactive', `${agent.name} is switched off in the Library. Choose someone else or switch them back on.`)
      const exists = team.some((t) => t.parcelId === parcelId && t.role === agent.agentType && key(t.name) === key(agent.name) && t.id !== rid)
      if (exists) return problem(409, 'team_member_exists', `${agent.name} is already on this parcel as ${agent.agentType.toLowerCase()}.`)
      const fields = { role: agent.agentType, agentId: agent.id, name: agent.name, organization: agent.company, email: agent.email, phone: agent.phone, notes: body.notes ?? null }
      if (method === 'PUT') {
        const t = team.find((x) => x.id === rid && x.parcelId === parcelId)
        if (!t) return problem(404, 'not_found', 'Team member was not found.')
        Object.assign(t, fields)
        return json(t)
      }
      const t: TeamMember = { id: id('tm'), parcelId, updatedAt: '2026-10-14T00:00:00Z', ...fields }
      team.push(t)
      return json(t, 201)
    }

    if (part === 'diary') {
      if (method === 'GET') return json(diary.filter((d) => d.parcelId === parcelId).sort((a, b) => b.occurredOn.localeCompare(a.occurredOn)))
      if (method === 'DELETE') {
        const i = diary.findIndex((d) => d.id === rid && d.parcelId === parcelId)
        if (i < 0) return problem(404, 'not_found', 'Diary entry was not found.')
        diary.splice(i, 1)
        return new Response(null, { status: 204 })
      }
      const errors: Record<string, string[]> = {}
      if (!String(body?.summary ?? '').trim()) errors.Summary = ['Summary is required.']
      if (!String(body?.contactName ?? '').trim() && !body?.partyId) errors.ContactName = ['Say who was contacted, or choose a party.']
      if (body?.occurredOn > today) errors.OccurredOn = ['A diary entry cannot be dated in the future.']
      if (Object.keys(errors).length) return problem(400, 'validation_failed', 'Validation failed.', errors)
      const d: DiaryEntry = {
        id: id('de'), parcelId, occurredOn: body.occurredOn, method: body.method, direction: body.direction, contactName: body.contactName.trim(), partyId: body.partyId ?? null,
        summary: body.summary.trim(), outcome: body.outcome ?? null, createdAt: '2026-10-14T00:00:00Z', createdBy: 'u1', authorName: 'Dev User',
      }
      diary.push(d)
      return json(d, 201)
    }

    if (method === 'GET') return json(notes.filter((n) => n.parcelId === parcelId).reverse())
    if (method === 'DELETE') {
      const i = notes.findIndex((n) => n.id === rid && n.parcelId === parcelId)
      if (i < 0) return problem(404, 'not_found', 'Note was not found.')
      notes.splice(i, 1)
      return new Response(null, { status: 204 })
    }
    if (!String(body?.text ?? '').trim()) return problem(400, 'validation_failed', 'Validation failed.', { Text: ['Write the note first.'] })
    const n: ParcelNote = { id: id('nt'), parcelId, text: body.text.trim(), createdAt: '2026-10-14T00:00:00Z', createdBy: 'u1', authorName: 'Dev User' }
    notes.push(n)
    return json(n, 201)
  }

  return { ...base, fetch: fetchImpl as typeof fetch, team, diary, notes }
}
