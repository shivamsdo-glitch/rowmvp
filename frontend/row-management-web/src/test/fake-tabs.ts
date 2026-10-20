import type { ContactLogEntry, DocumentInfo, Easement, Ownership, OwnershipAddress, OwnershipContact } from '@/api/parcel-tabs'
import { fakeRecordsApi, type FakeRecords, type FakeRecordsOptions } from './fake-records'

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}
const problem = (status: number, title: string, detail: string, errors?: Record<string, string[]>) => json({ title, status, detail, errors }, status)

export interface FakeTabsOptions extends FakeRecordsOptions {
  contactLog?: (Partial<ContactLogEntry> & { parcelId: string })[]
  easements?: (Partial<Easement> & { parcelId: string; number: string })[]
  documents?: Partial<DocumentInfo>[]
  ownerships?: (Partial<Ownership> & { parcelId: string; ownerName: string })[]
}

export interface FakeTabs extends FakeRecords {
  contactLog: ContactLogEntry[]
  easements: Easement[]
  documents: DocumentInfo[]
  ownerships: Ownership[]
}

/** An in-memory stand-in for a parcel's Contact Log, Easements and the attachments on them, with the server's rules and page permissions. */
export function fakeTabsApi(opts: FakeTabsOptions): FakeTabs {
  const base = fakeRecordsApi(opts)
  let seq = 0
  const id = (p: string) => `${p}${++seq}`
  const can = (page: string, action: 'create' | 'read' | 'update' | 'delete') =>
    !!opts.rows?.find((r) => r.pageKey === page)?.[`can${action[0].toUpperCase()}${action.slice(1)}` as 'canCreate']
  const forbidden = () => problem(403, 'forbidden', 'Your role does not allow this.')

  const contactLog: ContactLogEntry[] = (opts.contactLog ?? []).map((e, i) => ({
    id: id('cl'), number: `CL-001-${String(i + 1).padStart(3, '0')}`, contactedAt: '2026-10-01T15:30:00.000Z', mode: null, contactedBy: null, contactPerson: null,
    notes: null, links: [], createdAt: '2026-10-01T15:30:00.000Z', authorName: 'Dev User', ...e,
  }))
  const easements: Easement[] = (opts.easements ?? []).map((e) => ({
    id: id('es'), easementType: null, description: null, startDate: null, endDate: null, startStation: null, endStation: null, updatedAt: '2026-01-01T00:00:00Z', ...e,
  }))
  const documents: DocumentInfo[] = (opts.documents ?? []).map((d) => ({
    id: id('doc'), entityType: 'ContactLog', entityId: 'x', fileName: 'file.pdf', contentType: 'application/pdf', sizeBytes: 2048, docType: null, description: null,
    uploadedAt: '2026-10-01T00:00:00Z', uploadedBy: 'u1', ...d,
  }))
  const ownerships: Ownership[] = (opts.ownerships ?? []).map((o, i) => ({
    id: id('ow'), ownershipNumber: i + 1, parcelNumber: '001', ownerType: null, addresses: [], contacts: [], updatedAt: '2026-01-01T00:00:00Z', ...o,
  }))
  const syncOwnerName = (parcelId: string) => {
    const parcel = base.parcels.find((p) => p.id === parcelId)
    const names = ownerships.filter((o) => o.parcelId === parcelId).sort((a, b) => a.ownershipNumber - b.ownershipNumber).map((o) => o.ownerName)
    if (parcel) parcel.ownerName = names.length ? names.join('; ') : null
  }
  for (const p of base.parcels) syncOwnerName(p.id)
  const pageOf = (entityType: string) =>
    entityType === 'ContactLog' ? 'contactlog' : entityType === 'Easement' ? 'easements' : entityType === 'Ownership' ? 'ownership' : 'parcels'

  const fetchImpl = async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://x')
    const headers = (init?.headers ?? {}) as Record<string, string>
    const method = init?.method ?? 'GET'

    if (url.pathname.startsWith('/api/v1/documents')) {
      if (headers.Authorization !== 'Bearer good-token') return json({ title: 'unauthorized' }, 401)
      const rid = url.pathname.split('/')[4]
      if (method === 'GET') {
        const type = url.searchParams.get('entityType') ?? ''
        if (!can(pageOf(type), 'read')) return forbidden()
        return json(documents.filter((d) => d.entityType === type && d.entityId === url.searchParams.get('entityId')))
      }
      base.calls.push({ key: `${method} ${url.pathname}` })
      if (method === 'POST') {
        const form = init?.body as FormData
        const type = String(form.get('entityType'))
        if (!can(pageOf(type), 'update')) return forbidden()
        const file = form.get('file') as File
        const d: DocumentInfo = {
          id: id('doc'), entityType: type, entityId: String(form.get('entityId')), fileName: file.name, contentType: file.type || 'application/octet-stream',
          sizeBytes: file.size, docType: null, description: null, uploadedAt: '2026-10-14T00:00:00Z', uploadedBy: 'u1',
        }
        documents.push(d)
        return json(d, 201)
      }
      const i = documents.findIndex((d) => d.id === rid)
      if (i < 0) return problem(404, 'not_found', 'Document was not found.')
      if (!can(pageOf(documents[i].entityType), 'delete')) return forbidden()
      documents.splice(i, 1)
      return new Response(null, { status: 204 })
    }

    const m = /^\/api\/v1\/parcels\/([^/]+)\/(contact-log|easements|ownerships)(?:\/([^/]+))?$/.exec(url.pathname)
    if (!m) return base.fetch(input, init)
    if (headers.Authorization !== 'Bearer good-token') return json({ title: 'unauthorized' }, 401)
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    base.calls.push({ key: `${method} ${url.pathname}`, body })
    const [, parcelId, part, rid] = m
    const page = part === 'contact-log' ? 'contactlog' : part === 'ownerships' ? 'ownership' : 'easements'
    const act = method === 'GET' ? 'read' : method === 'POST' ? 'create' : method === 'PUT' ? 'update' : 'delete'
    if (!can(page, act)) return forbidden()
    const parcel = base.parcels.find((p) => p.id === parcelId)
    if (!parcel) return problem(404, 'not_found', 'Parcel was not found.')
    if (method !== 'GET' && parcel.state !== 'Active') return problem(409, 'parcel_not_active', `Parcel ${parcel.number} can no longer be changed.`)

    if (part === 'ownerships') {
      if (method === 'GET') return json(ownerships.filter((o) => o.parcelId === parcelId).sort((a, b) => a.ownershipNumber - b.ownershipNumber))
      if (method === 'DELETE') {
        const i = ownerships.findIndex((o) => o.id === rid && o.parcelId === parcelId)
        if (i < 0) return problem(404, 'not_found', 'Ownership was not found.')
        ownerships.splice(i, 1)
        syncOwnerName(parcelId)
        return new Response(null, { status: 204 })
      }
      const errors: Record<string, string[]> = {}
      if (!String(body?.ownerName ?? '').trim()) errors.OwnerName = ['Owner name is required.']
      if ((body?.contacts ?? []).filter((c: { isPrimary: boolean }) => c.isPrimary).length > 1) errors.Contacts = ['Only one contact can be the primary contact.']
      if (Object.keys(errors).length) return problem(400, 'validation_failed', 'Validation failed.', errors)
      const withIds = <T extends object>(rows: T[] | undefined): (T & { id: string })[] => (rows ?? []).map((r) => ({ id: id('x'), ...r }))
      const fields = { ownerName: body.ownerName.trim(), ownerType: body.ownerType ?? null, addresses: withIds<OwnershipAddress>(body.addresses), contacts: withIds<OwnershipContact>(body.contacts) }
      if (method === 'PUT') {
        const o = ownerships.find((x) => x.id === rid && x.parcelId === parcelId)
        if (!o) return problem(404, 'not_found', 'Ownership was not found.')
        Object.assign(o, fields)
        syncOwnerName(parcelId)
        return json(o)
      }
      const next = Math.max(0, ...ownerships.filter((o) => o.parcelId === parcelId).map((o) => o.ownershipNumber)) + 1
      const o: Ownership = { id: id('ow'), parcelId, ownershipNumber: next, parcelNumber: parcel.number, updatedAt: '2026-10-14T00:00:00Z', ...fields }
      ownerships.push(o)
      syncOwnerName(parcelId)
      return json(o, 201)
    }

    if (part === 'contact-log') {
      if (method === 'GET') return json(contactLog.filter((e) => e.parcelId === parcelId).sort((a, b) => b.contactedAt.localeCompare(a.contactedAt)))
      if (method === 'DELETE') {
        const i = contactLog.findIndex((e) => e.id === rid && e.parcelId === parcelId)
        if (i < 0) return problem(404, 'not_found', 'Contact log entry was not found.')
        contactLog.splice(i, 1)
        return new Response(null, { status: 204 })
      }
      if (!body?.contactedAt || Number.isNaN(Date.parse(body.contactedAt))) return problem(400, 'validation_failed', 'Validation failed.', { ContactedAt: ['Enter the date and time of the contact.'] })
      const links = (body.links ?? []).map((l: { recordIdentifier: string; moduleName: string | null; notes: string | null }) => ({ id: id('lk'), ...l }))
      if (method === 'PUT') {
        const e = contactLog.find((x) => x.id === rid && x.parcelId === parcelId)
        if (!e) return problem(404, 'not_found', 'Contact log entry was not found.')
        Object.assign(e, { contactedAt: body.contactedAt, mode: body.mode ?? null, contactedBy: body.contactedBy ?? null, contactPerson: body.contactPerson ?? null, notes: body.notes ?? null, links })
        return json(e)
      }
      const next = Math.max(0, ...contactLog.filter((e) => e.parcelId === parcelId).map((e) => Number(e.number.split('-').pop()))) + 1
      const e: ContactLogEntry = {
        id: id('cl'), parcelId, number: `CL-${parcel.number}-${String(next).padStart(3, '0')}`, contactedAt: body.contactedAt, mode: body.mode ?? null,
        contactedBy: body.contactedBy ?? null, contactPerson: body.contactPerson ?? null, notes: body.notes ?? null, links, createdAt: '2026-10-14T00:00:00Z', authorName: 'Dev User',
      }
      contactLog.push(e)
      return json(e, 201)
    }

    if (method === 'GET') return json(easements.filter((e) => e.parcelId === parcelId).sort((a, b) => a.number.localeCompare(b.number)))
    if (method === 'DELETE') {
      const i = easements.findIndex((e) => e.id === rid && e.parcelId === parcelId)
      if (i < 0) return problem(404, 'not_found', 'Easement was not found.')
      easements.splice(i, 1)
      return new Response(null, { status: 204 })
    }
    const errors: Record<string, string[]> = {}
    if (!String(body?.number ?? '').trim()) errors.Number = ['Easement number is required.']
    if (body?.startDate && body?.endDate && body.endDate < body.startDate) errors.EndDate = ['The end date cannot be before the start date.']
    if (Object.keys(errors).length) return problem(400, 'validation_failed', 'Validation failed.', errors)
    const number = String(body.number).trim()
    if (easements.some((e) => e.parcelId === parcelId && e.number === number && e.id !== rid)) return problem(409, 'easement_number_exists', `This parcel already has an easement numbered '${number}'.`)
    const fields = {
      number, easementType: body.easementType ?? null, description: body.description ?? null, startDate: body.startDate ?? null, endDate: body.endDate ?? null,
      startStation: body.startStation ?? null, endStation: body.endStation ?? null,
    }
    if (method === 'PUT') {
      const e = easements.find((x) => x.id === rid && x.parcelId === parcelId)
      if (!e) return problem(404, 'not_found', 'Easement was not found.')
      Object.assign(e, fields)
      return json(e)
    }
    const e: Easement = { id: id('es'), parcelId, updatedAt: '2026-10-14T00:00:00Z', ...fields }
    easements.push(e)
    return json(e, 201)
  }

  return { ...base, fetch: fetchImpl as typeof fetch, contactLog, easements, documents, ownerships }
}
