import type { ParcelParties, Party, PartyLink, PartyLinkInput, PartyRole, PartySummary, PartyUsage } from '@/api/parties'
import { CARRIES_SHARE, CATEGORY_OF, HAS_LEASE, ROLE_LABEL } from '@/api/parties'
import { fakeParcelsApi, type FakeParcels, type FakeParcelsOptions } from './fake-parcels'

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}
const problem = (status: number, title: string, detail: string, errors?: Record<string, string[]>) => json({ title, status, detail, errors }, status)

interface StoredLink {
  id: string
  partyId: string
  tractId: string | null
  parcelId: string | null
  role: PartyRole
  sharePercent: number | null
  notes: string | null
  leaseOnFile: boolean
  leaseDetails: string | null
  insuranceDetails: string | null
}

export interface FakePeopleOptions extends FakeParcelsOptions {
  parties?: (Partial<Party> & { name: string })[]
  /** Seed links: a party (by id) on a parcel or tract. */
  links?: (Partial<StoredLink> & { partyId: string; role: PartyRole })[]
  /** Make GET /parties fail with a 500. */
  listFails?: boolean
}

export interface FakePeople extends FakeParcels {
  parties: Party[]
  links: StoredLink[]
  /** The tax numbers the server was sent, by party id. The real server never returns them; tests assert nothing here leaks into a response. */
  receivedTaxIds: { partyId: string; taxId: string }[]
}

const SSN_BAD_AREA = (d: string) => d.startsWith('000') || d.startsWith('666') || d.startsWith('9')

/** An in-memory stand-in for the Parties API on top of the Parcels fake, with the server's rules for shares, duplicates, owners-on-tract and masking. */
export function fakePeopleApi(opts: FakePeopleOptions): FakePeople {
  const base = fakeParcelsApi(opts)
  let seq = 0
  const id = (p: string) => `${p}${++seq}`
  const can = (action: 'create' | 'read' | 'update' | 'delete') =>
    !!opts.rows?.find((r) => r.pageKey === 'parties')?.[`can${action[0].toUpperCase()}${action.slice(1)}` as 'canCreate']
  const forbidden = () => problem(403, 'forbidden', 'Your role does not allow this.')
  const notFound = (what: string) => problem(404, 'not_found', `${what} was not found.`)

  const parties: Party[] = (opts.parties ?? []).map((p) => ({
    id: id('party'), kind: 'Person', legalName: null, email: null, phone: null, addressLine1: null, addressLine2: null, city: null, state: null,
    postalCode: null, notes: null, taxIdType: null, taxIdMasked: null, hasTaxId: false, w9ReceivedOn: null, reportable1099: false, linkCount: 0,
    updatedAt: '2026-01-01T00:00:00Z', ...p,
  }))
  const links: StoredLink[] = (opts.links ?? []).map((l) => ({
    id: id('link'), tractId: null, parcelId: null, sharePercent: null, notes: null, leaseOnFile: false, leaseDetails: null, insuranceDetails: null, ...l,
  }))
  const receivedTaxIds: FakePeople['receivedTaxIds'] = []

  const recount = () => parties.forEach((p) => { p.linkCount = links.filter((l) => l.partyId === p.id).length })
  recount()
  const summary = (p: Party): PartySummary => ({ id: p.id, kind: p.kind, name: p.name, city: p.city, state: p.state, phone: p.phone, email: p.email, hasTaxId: p.hasTaxId, linkCount: p.linkCount })
  const tractNumber = (tractId: string | null) => base.tracts.find((t) => t.id === tractId)?.number ?? null

  const dto = (l: StoredLink): PartyLink => ({
    id: l.id, category: CATEGORY_OF[l.role], role: l.role, sharePercent: l.sharePercent, notes: l.notes, leaseOnFile: l.leaseOnFile, leaseDetails: l.leaseDetails,
    insuranceDetails: l.insuranceDetails, source: l.tractId ? 'Tract' : 'Parcel', parentTractId: l.tractId, tractNumber: tractNumber(l.tractId), parcelId: l.parcelId,
    party: summary(parties.find((p) => p.id === l.partyId)!),
  })
  const grouped = (rows: StoredLink[], tractId: string | null): ParcelParties => {
    const all = rows.map(dto).sort((a, b) => a.role.localeCompare(b.role) || a.party.name.localeCompare(b.party.name))
    return {
      ownership: all.filter((l) => l.category === 'Ownership'), contacts: all.filter((l) => l.category === 'Contact'),
      occupants: all.filter((l) => l.category === 'Occupant'), abutters: all.filter((l) => l.category === 'Abutter'),
      parentTractId: tractId, tractNumber: tractNumber(tractId),
    }
  }

  function validateParty(b: any): Record<string, string[]> {
    const e: Record<string, string[]> = {}
    if (!String(b?.name ?? '').trim()) e.Name = ['Name is required.']
    if (b?.email && !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(b.email)) e.Email = ['Enter a valid email address.']
    if (b?.state && !/^[A-Za-z]{2}$/.test(b.state)) e.State = ['Use the two-letter state code.']
    return e
  }
  const applyParty = (p: Party, b: any) => {
    Object.assign(p, {
      kind: b.kind, name: String(b.name).trim(), legalName: b.legalName ?? null, email: b.email ?? null, phone: b.phone ?? null,
      addressLine1: b.addressLine1 ?? null, addressLine2: b.addressLine2 ?? null, city: b.city ?? null, state: b.state ? String(b.state).toUpperCase() : null,
      postalCode: b.postalCode ?? null, notes: b.notes ?? null, updatedAt: '2026-10-14T00:00:00Z',
    })
  }

  /** Adds a link the way the server does; returns the response. */
  function addLink(input: PartyLinkInput, where: { tractId?: string; parcelId?: string }): Response {
    const errors: Record<string, string[]> = {}
    if (!!input.partyId === !!input.newParty) errors.Party = ['Choose an existing party or enter a new one.']
    if (input.newParty) for (const [k, v] of Object.entries(validateParty(input.newParty))) errors[`NewParty.${k}`] = v
    if (input.sharePercent !== null && !CARRIES_SHARE.includes(input.role)) errors.SharePercent = ['Only fee owners, easement holders and life-estate holders have a share.']
    else if (input.sharePercent !== null && (input.sharePercent <= 0 || input.sharePercent > 100)) errors.SharePercent = ['A share must be more than 0 and at most 100 percent.']
    if (!HAS_LEASE.includes(input.role) && (input.leaseOnFile || input.leaseDetails || input.insuranceDetails)) errors.LeaseDetails = ['Lease and insurance details apply to tenants only.']
    if (Object.keys(errors).length) return problem(400, 'validation_failed', 'Validation failed.', errors)

    if (where.parcelId) {
      const parcel = base.parcels.find((p) => p.id === where.parcelId)
      if (!parcel) return notFound('Parcel')
      if (CATEGORY_OF[input.role] === 'Ownership' && parcel.parentTractId)
        return problem(400, 'owners_on_tract', `This parcel is part of tract ${parcel.parentTractNumber}. Record its owners and interests on the tract.`)
    }
    let party = parties.find((p) => p.id === input.partyId)
    if (input.partyId && !party) return notFound('Party')
    const sameTarget = (l: StoredLink) => (where.tractId ? l.tractId === where.tractId : l.parcelId === where.parcelId)
    if (party && links.some((l) => sameTarget(l) && l.partyId === party!.id && l.role === input.role))
      return problem(409, 'party_link_exists', `${party.name} is already recorded here as ${ROLE_LABEL[input.role].toLowerCase()}.`)
    if (input.sharePercent !== null) {
      const others = links.filter((l) => sameTarget(l) && l.role === input.role && l.sharePercent !== null).reduce((s, l) => s + l.sharePercent!, 0)
      if (others + input.sharePercent > 100.0000001)
        return problem(409, 'share_exceeds_whole', `The shares would add up to more than 100 percent. ${Math.max(0, 100 - others)} percent is unclaimed.`)
    }
    if (!party) {
      party = {
        id: id('party'), kind: input.newParty!.kind, name: '', legalName: null, email: null, phone: null, addressLine1: null, addressLine2: null, city: null,
        state: null, postalCode: null, notes: null, taxIdType: null, taxIdMasked: null, hasTaxId: false, w9ReceivedOn: null, reportable1099: false, linkCount: 0, updatedAt: '',
      }
      applyParty(party, input.newParty)
      parties.push(party)
    }
    const link: StoredLink = {
      id: id('link'), partyId: party.id, tractId: where.tractId ?? null, parcelId: where.parcelId ?? null, role: input.role, sharePercent: input.sharePercent,
      notes: input.notes, leaseOnFile: input.leaseOnFile, leaseDetails: input.leaseDetails, insuranceDetails: input.insuranceDetails,
    }
    links.push(link)
    recount()
    return json(dto(link), 201)
  }

  const fetchImpl = async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://x')
    const path = url.pathname
    const method = init?.method ?? 'GET'
    const m =
      /^\/api\/v1\/parties(?:\/([^/]+)(?:\/(usage|tax-info(?:\/number)?))?)?$/.exec(path) ??
      /^\/api\/v1\/(parcels)\/([^/]+)\/(parties)$/.exec(path) ??
      /^\/api\/v1\/projects\/([^/]+)\/tracts\/([^/]+)\/(parties)$/.exec(path) ??
      /^\/api\/v1\/(party-links)\/([^/]+)$/.exec(path)
    if (!m) return base.fetch(input, init)

    const headers = (init?.headers ?? {}) as Record<string, string>
    if (headers.Authorization !== 'Bearer good-token') return json({ title: 'unauthorized' }, 401)
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    base.calls.push({ key: `${method} ${path}${url.search}`, body })

    // /parcels/{id}/parties
    if (m[1] === 'parcels' && m[3] === 'parties') {
      const parcelId = m[2]
      if (method === 'GET') {
        if (!can('read')) return forbidden()
        const parcel = base.parcels.find((p) => p.id === parcelId)
        if (!parcel) return notFound('Parcel')
        return json(grouped(links.filter((l) => l.parcelId === parcelId || (parcel.parentTractId && l.tractId === parcel.parentTractId)), parcel.parentTractId))
      }
      if (!can('create')) return forbidden()
      const parcel = base.parcels.find((p) => p.id === parcelId)
      if (parcel && parcel.state !== 'Active') return problem(409, 'parcel_not_active', 'That parcel can no longer be changed.')
      return addLink(body, { parcelId })
    }
    // /projects/{pid}/tracts/{tid}/parties
    if (m[3] === 'parties' && path.includes('/tracts/')) {
      const tractId = m[2]
      const tract = base.tracts.find((t) => t.id === tractId && t.projectId === m[1])
      if (method === 'GET') {
        if (!can('read')) return forbidden()
        if (!tract) return notFound('Tract')
        return json(grouped(links.filter((l) => l.tractId === tractId), tractId))
      }
      if (!can('create')) return forbidden()
      if (!tract) return notFound('Tract')
      return addLink(body, { tractId })
    }
    // /party-links/{id}
    if (m[1] === 'party-links') {
      const link = links.find((l) => l.id === m[2])
      if (method === 'PUT') {
        if (!can('update')) return forbidden()
        if (!link) return notFound('Party link')
        if (body.sharePercent !== null && body.sharePercent !== undefined) {
          const others = links.filter((l) => l.id !== link.id && l.tractId === link.tractId && l.parcelId === link.parcelId && l.role === link.role && l.sharePercent !== null).reduce((s, l) => s + l.sharePercent!, 0)
          if (others + body.sharePercent > 100.0000001) return problem(409, 'share_exceeds_whole', `The shares would add up to more than 100 percent. ${Math.max(0, 100 - others)} percent is unclaimed.`)
        }
        Object.assign(link, { sharePercent: body.sharePercent ?? null, notes: body.notes ?? null, leaseOnFile: !!body.leaseOnFile, leaseDetails: body.leaseDetails ?? null, insuranceDetails: body.insuranceDetails ?? null })
        return json(dto(link))
      }
      if (method === 'DELETE') {
        if (!can('delete')) return forbidden()
        if (!link) return notFound('Party link')
        links.splice(links.indexOf(link), 1)
        recount()
        return new Response(null, { status: 204 })
      }
    }

    // /parties...
    const partyId = m[1]
    const sub = m[2]
    if (!partyId) {
      if (method === 'GET') {
        if (!can('read')) return forbidden()
        if (opts.listFails) return problem(500, 'server_error', 'Boom.')
        const search = (url.searchParams.get('search') ?? '').toLowerCase()
        const kind = url.searchParams.get('kind')
        const page = Number(url.searchParams.get('page') ?? 1)
        const size = Number(url.searchParams.get('pageSize') ?? 25)
        const rows = parties
          .filter((p) => (!kind || p.kind === kind) && (!search || [p.name, p.legalName, p.city, p.email, p.phone].some((v) => v?.toLowerCase().includes(search))))
          .sort((a, b) => a.name.localeCompare(b.name))
        return json({ items: rows.slice((page - 1) * size, page * size).map(summary), total: rows.length, page, pageSize: size })
      }
      if (method === 'POST') {
        if (!can('create')) return forbidden()
        const errors = validateParty(body)
        if (Object.keys(errors).length) return problem(400, 'validation_failed', 'Validation failed.', errors)
        const party = { id: id('party'), kind: 'Person', legalName: null, taxIdType: null, taxIdMasked: null, hasTaxId: false, w9ReceivedOn: null, reportable1099: false, linkCount: 0 } as unknown as Party
        applyParty(party, body)
        parties.push(party)
        return json(party, 201)
      }
    }
    const party = parties.find((p) => p.id === partyId)
    if (method === 'GET' && !sub) return can('read') ? (party ? json(party) : notFound('Party')) : forbidden()
    if (method === 'GET' && sub === 'usage') {
      if (!can('read')) return forbidden()
      if (!party) return notFound('Party')
      const rows: PartyUsage[] = links.filter((l) => l.partyId === party.id).map((l) => {
        const parcel = base.parcels.find((p) => p.id === l.parcelId)
        const tract = base.tracts.find((t) => t.id === l.tractId)
        return {
          linkId: l.id, role: l.role, category: CATEGORY_OF[l.role], targetKind: l.parcelId ? 'Parcel' : 'Tract', targetId: (l.parcelId ?? l.tractId)!,
          targetLabel: parcel ? `Parcel ${parcel.number}` : `Tract ${tract?.number}`, projectId: parcel?.projectId ?? tract?.projectId ?? '', projectNumber: parcel?.projectNumber ?? '2026-0001',
        }
      })
      return json(rows)
    }
    if (method === 'PUT' && !sub) {
      if (!can('update')) return forbidden()
      if (!party) return notFound('Party')
      const errors = validateParty(body)
      if (Object.keys(errors).length) return problem(400, 'validation_failed', 'Validation failed.', errors)
      applyParty(party, body)
      return json(party)
    }
    if (method === 'DELETE' && !sub) {
      if (!can('delete')) return forbidden()
      if (!party) return notFound('Party')
      if (links.some((l) => l.partyId === party.id)) return problem(409, 'party_in_use', `${party.name} is still linked to records. Remove those links first.`)
      parties.splice(parties.indexOf(party), 1)
      return new Response(null, { status: 204 })
    }
    if (method === 'PUT' && sub === 'tax-info') {
      if (!can('update')) return forbidden()
      if (!party) return notFound('Party')
      const errors: Record<string, string[]> = {}
      const raw: string | null = body.taxId ? String(body.taxId) : null
      if (raw) {
        const digits = raw.replace(/\D/g, '')
        if (!body.type) errors.Type = ['Choose the type of tax number.']
        else if (/[^\d\s-]/.test(raw)) errors.TaxId = ['A tax number can contain only digits, spaces and hyphens.']
        else if (digits.length !== 9) errors.TaxId = ['A tax number has 9 digits.']
        else if (body.type === 'Ssn' && SSN_BAD_AREA(digits)) errors.TaxId = ['That is not a valid Social Security number.']
        else if (body.type === 'Ein' && digits.startsWith('00')) errors.TaxId = ['That is not a valid Employer Identification Number.']
      }
      if (body.w9ReceivedOn && body.w9ReceivedOn > '2026-10-14') errors.W9ReceivedOn = ['A W-9 cannot be received in the future.']
      if (Object.keys(errors).length) return problem(400, 'validation_failed', 'Validation failed.', errors)
      if (raw) {
        const digits = raw.replace(/\D/g, '')
        receivedTaxIds.push({ partyId: party.id, taxId: raw })
        party.taxIdType = body.type
        party.hasTaxId = true
        party.taxIdMasked = body.type === 'Ein' ? `**-***${digits.slice(-4)}` : `***-**-${digits.slice(-4)}`
      } else if (body.type && party.taxIdType && body.type !== party.taxIdType) {
        return problem(400, 'tax_id_type_change', 'Enter the number again when changing its type.')
      }
      party.w9ReceivedOn = body.w9ReceivedOn ?? null
      party.reportable1099 = !!body.reportable1099
      return json(party)
    }
    if (method === 'DELETE' && sub === 'tax-info/number') {
      if (!can('update')) return forbidden()
      if (!party) return notFound('Party')
      Object.assign(party, { taxIdType: null, taxIdMasked: null, hasTaxId: false })
      return json(party)
    }
    return problem(404, 'not_found', 'Unknown route.')
  }

  return { ...base, fetch: fetchImpl as typeof fetch, parties, links, receivedTaxIds }
}
