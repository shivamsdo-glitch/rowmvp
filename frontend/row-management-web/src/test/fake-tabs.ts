import type { ContactLogEntry, DocumentInfo, Easement, Encumbrance, Ownership, OwnershipAddress, OwnershipContact, TitleClosing, Appraisal, Negotiation } from '@/api/parcel-tabs'
import { fakeRecordsApi, type FakeRecords, type FakeRecordsOptions } from './fake-records'

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}
const problem = (status: number, title: string, detail: string, errors?: Record<string, string[]>) => json({ title, status, detail, errors }, status)

type NegotiationAgentLike = { agentType: string | null; company: string | null; phone: string | null; email: string | null }

export interface FakeTabsOptions extends FakeRecordsOptions {
  contactLog?: (Partial<ContactLogEntry> & { parcelId: string })[]
  easements?: (Partial<Easement> & { parcelId: string; number: string })[]
  documents?: Partial<DocumentInfo>[]
  ownerships?: (Partial<Ownership> & { parcelId: string; ownerName: string })[]
  encumbrances?: (Partial<Encumbrance> & { parcelId: string })[]
  titleClosings?: (Partial<TitleClosing> & { parcelId: string })[]
  appraisals?: (Partial<Appraisal> & { parcelId: string })[]
  negotiations?: (Partial<Negotiation> & { parcelId: string })[]
}

export interface FakeTabs extends FakeRecords {
  contactLog: ContactLogEntry[]
  easements: Easement[]
  documents: DocumentInfo[]
  ownerships: Ownership[]
  encumbrances: Encumbrance[]
  titleClosings: TitleClosing[]
  appraisals: Appraisal[]
  negotiations: Negotiation[]
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
  const encumbrances: Encumbrance[] = (opts.encumbrances ?? []).map((e, i) => ({
    id: id('en'), number: `ENC-001-${String(i + 1).padStart(3, '0')}`, encumbranceType: 'Mortgage Lien', documentTitle: 'Deed of Trust', documentDate: null, dateRecorded: null,
    documentNumber: null, isUnrecorded: false, grantor: 'Grantor', grantee: 'Grantee', exceptionSubmittedDate: null, exceptionApprovedDate: null, impactAnalysis: null,
    recommendedCure: null, cureStatus: null, reviewNotes: null, links: [], updatedAt: '2026-01-01T00:00:00Z', ...e,
  }))
  const titleClosings: TitleClosing[] = (opts.titleClosings ?? []).map((t) => ({
    id: id('tc'), number: 'TC-001', parcelNumber: '001', vestedOwnerName: null, assignedTo: null, titleCompany: null, titleClosingAgent: null, gfNumber: null,
    finalTitlePolicyNo: null, psaSignedByOwnerDate: null, psaSignedByAtpDate: null, psaAmount: null, settlementReceivedDate: null, settlementExecutedDate: null,
    curativeItemsCompleted: false, closingPackageSubmittedDate: null, closingPackageApprovedDate: null, closingInstructionDate: null, anticipatedClosingDate: null,
    actualClosingDate: null, conveyanceDocuments: [], titleCommitments: [], updatedAt: '2026-01-01T00:00:00Z', ...t,
  }))
  const appraisals: Appraisal[] = (opts.appraisals ?? []).map((a, i) => ({
    id: id('ap'), number: `APR-001-${String(i + 1).padStart(3, '0')}`, parcelNumber: '001', appraisalType: null, appraiser: null, appraiserCompany: null, landPlannerRequired: false,
    assignedTo: null, requestedDate: null, receivedDate: null, reportDate: null, dueDate: null, submittedToAtpDate: null, effectiveDate: null, amounts: [], appraisalTotal: 0,
    reviewAppraiser: null, reviewType: null, appraisalReceivedDate: null, approvedJustCompensation: null, reviewCompletedDate: null, reviewSubmittedDate: null,
    reviewRecommendation: null, reviewRecommendedValue: null, submittedToJatDate: null, approvedByJatDate: null, ftaConcurrenceNeeded: false, updatedAt: '2026-01-01T00:00:00Z', ...a,
  }))
  const negotiations: Negotiation[] = (opts.negotiations ?? []).map((n) => ({
    id: id('ng'), number: 'NEG-001', parcelNumber: '001', assignedTo: null, totalAgreementAmount: null, notes: null, counterofferSubmittedToJatDate: null,
    counterofferResponseFromJatDate: null, jatResponse: null, ftaApprovalRequiredForCounteroffer: false, puaRequestedDate: null, puaApprovedDate: null, puaApprovedAmount: null,
    puaExecutedDate: null, puaClosedDate: null, statusNotes: [], agents: [], rightsOfEntry: [], offers: [], settlements: [], updatedAt: '2026-01-01T00:00:00Z', ...n,
  }))
  const pageOf = (entityType: string) =>
    entityType === 'ParcelNegotiation' ? 'parcelnegotiation' : entityType === 'ParcelAppraisal' ? 'parcelappraisal' : entityType === 'TitleClosing' ? 'titleclosing' : entityType === 'Encumbrance' ? 'titleencumbrances' : entityType === 'ContactLog' ? 'contactlog' : entityType === 'Easement' ? 'easements' : entityType === 'Ownership' ? 'ownership' : 'parcels'

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

    const m = /^\/api\/v1\/parcels\/([^/]+)\/(contact-log|easements|ownerships|encumbrances|title-closing|appraisals|negotiation)(?:\/([^/]+))?$/.exec(url.pathname)
    if (!m) return base.fetch(input, init)
    if (headers.Authorization !== 'Bearer good-token') return json({ title: 'unauthorized' }, 401)
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    base.calls.push({ key: `${method} ${url.pathname}`, body })
    const [, parcelId, part, rid] = m
    const page = part === 'contact-log' ? 'contactlog' : part === 'ownerships' ? 'ownership' : part === 'encumbrances' ? 'titleencumbrances' : part === 'title-closing' ? 'titleclosing' : part === 'appraisals' ? 'parcelappraisal' : part === 'negotiation' ? 'parcelnegotiation' : 'easements'
    const existingTc = titleClosings.find((x) => x.parcelId === parcelId)
    const existingNg = negotiations.find((x) => x.parcelId === parcelId)
    const act = method === 'GET' ? 'read' : method === 'POST' ? 'create' : method === 'PUT' ? ((part === 'title-closing' && !existingTc) || (part === 'negotiation' && !existingNg) ? 'create' : 'update') : 'delete'
    if (!can(page, act)) return forbidden()
    const parcel = base.parcels.find((p) => p.id === parcelId)
    if (!parcel) return problem(404, 'not_found', 'Parcel was not found.')
    if (method !== 'GET' && parcel.state !== 'Active') return problem(409, 'parcel_not_active', `Parcel ${parcel.number} can no longer be changed.`)

    if (part === 'title-closing') {
      if (method === 'GET') return json({ record: existingTc ?? null })
      if (method === 'DELETE') {
        if (!existingTc) return problem(404, 'not_found', 'Title and closing record was not found.')
        titleClosings.splice(titleClosings.indexOf(existingTc), 1)
        return new Response(null, { status: 204 })
      }
      const errors: Record<string, string[]> = {}
      if (body?.psaAmount !== null && body?.psaAmount !== undefined && body.psaAmount < 0) errors.PsaAmount = ['The amount must be zero or more.']
      if (body?.settlementReceivedDate && body?.settlementExecutedDate && body.settlementExecutedDate < body.settlementReceivedDate) errors.SettlementExecutedDate = ['The statement cannot be executed before it is received.']
      if (Object.keys(errors).length) return problem(400, 'validation_failed', 'Validation failed.', errors)
      const withIds = <T extends object>(rows: T[] | undefined): (T & { id: string })[] => (rows ?? []).map((r) => ({ id: id('x'), ...r }))
      const { conveyanceDocuments, titleCommitments, ...rest } = body
      const fields = { ...rest, conveyanceDocuments: withIds(conveyanceDocuments), titleCommitments: withIds(titleCommitments), updatedAt: `2026-10-14T00:00:${String(seq % 60).padStart(2, '0')}Z` }
      const vestedNames = ownerships.filter((o) => o.parcelId === parcelId && o.ownerType === 'Vested Owner').sort((a, b) => a.ownershipNumber - b.ownershipNumber).map((o) => o.ownerName)
      const vested = vestedNames.length ? vestedNames.join('; ') : null
      if (existingTc) { Object.assign(existingTc, fields); return json(existingTc) }
      const tc: TitleClosing = { id: id('tc'), parcelId, number: `TC-${parcel.number}`, parcelNumber: parcel.number, vestedOwnerName: vested, ...fields }
      titleClosings.push(tc)
      return json(tc)
    }

    if (part === 'negotiation') {
      if (method === 'GET') return json({ record: existingNg ?? null })
      if (method === 'DELETE') {
        if (!existingNg) return problem(404, 'not_found', 'Negotiation record was not found.')
        negotiations.splice(negotiations.indexOf(existingNg), 1)
        return new Response(null, { status: 204 })
      }
      const errors: Record<string, string[]> = {}
      const neg = (v: unknown) => v !== null && v !== undefined && (v as number) < 0
      if (neg(body?.totalAgreementAmount)) errors.TotalAgreementAmount = ['The amount must be zero or more.']
      if (body?.puaApprovedDate && body?.puaRequestedDate && body.puaApprovedDate < body.puaRequestedDate) errors.PuaApprovedDate = ['The PUA cannot be approved before it is requested.']
      if ((body?.offers ?? []).some((o: { amount: number | null }) => neg(o.amount))) errors.Offers = ['The amount must be zero or more.']
      if (Object.keys(errors).length) return problem(400, 'validation_failed', 'Validation failed.', errors)
      const withIds = <T extends object>(rows: T[] | undefined): (T & { id: string })[] => (rows ?? []).map((r) => ({ id: id('x'), ...r }))
      const libraryAgents: Record<string, Partial<NegotiationAgentLike>> = { 'Pat Agent': { agentType: 'Broker', company: 'Acme Realty', phone: '555-0100', email: 'pat@example.com' } }
      const agents = (body?.agents ?? []).map((a: { agentName: string }) => ({ id: id('x'), agentType: null, company: null, phone: null, email: null, ...libraryAgents[a.agentName], agentName: a.agentName }))
      const fields = {
        ...body, statusNotes: withIds(body?.statusNotes), agents, rightsOfEntry: withIds(body?.rightsOfEntry), offers: withIds(body?.offers), settlements: withIds(body?.settlements),
        updatedAt: `2026-10-14T00:00:${String(seq % 60).padStart(2, '0')}Z`,
      }
      if (existingNg) { Object.assign(existingNg, fields); return json(existingNg) }
      const ng: Negotiation = { id: id('ng'), parcelId, number: `NEG-${parcel.number}`, parcelNumber: parcel.number, ...fields }
      negotiations.push(ng)
      return json(ng)
    }

    if (part === 'appraisals') {
      const REVIEW = ['reviewAppraiser', 'reviewType', 'appraisalReceivedDate', 'approvedJustCompensation', 'reviewCompletedDate', 'reviewSubmittedDate', 'reviewRecommendation', 'reviewRecommendedValue'] as const
      const mayReview = opts.role === 'AppraisalApprover' || opts.role === 'Admin'
      const clean = (v: unknown) => (typeof v === 'string' ? (v.trim() || null) : (v ?? null))
      if (method === 'GET') return json(appraisals.filter((a) => a.parcelId === parcelId))
      if (method === 'DELETE') {
        const i = appraisals.findIndex((a) => a.id === rid && a.parcelId === parcelId)
        if (i < 0) return problem(404, 'not_found', 'Appraisal was not found.')
        appraisals.splice(i, 1)
        return new Response(null, { status: 204 })
      }
      const errors: Record<string, string[]> = {}
      if (body?.requestedDate && body?.receivedDate && body.receivedDate < body.requestedDate) errors.ReceivedDate = ['The appraisal cannot be received before it is requested.']
      if (body?.submittedToJatDate && body?.approvedByJatDate && body.approvedByJatDate < body.submittedToJatDate) errors.ApprovedByJatDate = ['The appraisal cannot be approved by the JAT before it is submitted.']
      if ((body?.amounts ?? []).some((a: { amountType: string; amount: number }) => !a.amountType || a.amount < 0)) errors.Amounts = ['Each amount needs a type and an amount of zero or more.']
      if (Object.keys(errors).length) return problem(400, 'validation_failed', 'Validation failed.', errors)
      const existing = method === 'PUT' ? appraisals.find((x) => x.id === rid && x.parcelId === parcelId) : undefined
      if (method === 'PUT' && !existing) return problem(404, 'not_found', 'Appraisal was not found.')
      const before = existing ?? ({} as Partial<Appraisal>)
      if (!mayReview && REVIEW.some((k) => clean(body?.[k]) !== clean((before as Record<string, unknown>)[k]))) return problem(403, 'appraisal_review_restricted', 'Only the Appraisal Approver can change the appraisal review.')
      const amounts = (body.amounts ?? []).map((a: object) => ({ id: id('am'), ...a }))
      const fields = { ...body, amounts, appraisalTotal: amounts.reduce((s: number, a: { amount: number }) => s + a.amount, 0), updatedAt: `2026-10-14T00:00:${String(seq % 60).padStart(2, '0')}Z` }
      if (existing) { Object.assign(existing, fields); return json(existing) }
      const next = Math.max(0, ...appraisals.filter((a) => a.parcelId === parcelId).map((a) => Number(a.number.split('-').pop()))) + 1
      const a: Appraisal = { id: id('ap'), parcelId, number: `APR-${parcel.number}-${String(next).padStart(3, '0')}`, parcelNumber: parcel.number, appraiserCompany: null, ...fields }
      appraisals.push(a)
      return json(a, 201)
    }

    if (part === 'encumbrances') {
      if (method === 'GET') return json(encumbrances.filter((e) => e.parcelId === parcelId))
      if (method === 'DELETE') {
        const i = encumbrances.findIndex((e) => e.id === rid && e.parcelId === parcelId)
        if (i < 0) return problem(404, 'not_found', 'Encumbrance was not found.')
        encumbrances.splice(i, 1)
        return new Response(null, { status: 204 })
      }
      const errors: Record<string, string[]> = {}
      if (!String(body?.encumbranceType ?? '').trim()) errors.EncumbranceType = ['Encumbrance type is required.']
      if (!String(body?.documentTitle ?? '').trim()) errors.DocumentTitle = ['Document title is required.']
      if (!String(body?.grantor ?? '').trim()) errors.Grantor = ['Grantor / lessor is required.']
      if (!String(body?.grantee ?? '').trim()) errors.Grantee = ['Grantee / lessee is required.']
      if (body?.isUnrecorded && body?.dateRecorded) errors.DateRecorded = ['An unrecorded document has no recording date.']
      if (Object.keys(errors).length) return problem(400, 'validation_failed', 'Validation failed.', errors)
      const links = (body.links ?? []).map((l: { recordIdentifier: string; moduleName: string | null; notes: string | null }) => ({ id: id('lk'), ...l }))
      const fields = {
        encumbranceType: body.encumbranceType, documentTitle: body.documentTitle.trim(), documentDate: body.documentDate ?? null, dateRecorded: body.dateRecorded ?? null,
        documentNumber: body.documentNumber ?? null, isUnrecorded: !!body.isUnrecorded, grantor: body.grantor.trim(), grantee: body.grantee.trim(),
        exceptionSubmittedDate: body.exceptionSubmittedDate ?? null, exceptionApprovedDate: body.exceptionApprovedDate ?? null, impactAnalysis: body.impactAnalysis ?? null,
        recommendedCure: body.recommendedCure ?? null, cureStatus: body.cureStatus ?? null, reviewNotes: body.reviewNotes ?? null, links,
      }
      if (method === 'PUT') {
        const e = encumbrances.find((x) => x.id === rid && x.parcelId === parcelId)
        if (!e) return problem(404, 'not_found', 'Encumbrance was not found.')
        Object.assign(e, fields)
        return json(e)
      }
      const next = Math.max(0, ...encumbrances.filter((e) => e.parcelId === parcelId).map((e) => Number(e.number.split('-').pop()))) + 1
      const e: Encumbrance = { id: id('en'), parcelId, number: `ENC-${parcel.number}-${String(next).padStart(3, '0')}`, updatedAt: '2026-10-14T00:00:00Z', ...fields }
      encumbrances.push(e)
      return json(e, 201)
    }

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

  return { ...base, fetch: fetchImpl as typeof fetch, contactLog, easements, documents, ownerships, encumbrances, titleClosings, appraisals, negotiations }
}
