import type { ContactLogEntry, DocumentInfo, Easement, Encumbrance, TitleClosing, Appraisal, Negotiation, Questionnaire, Condemnation, PropertyRecord, Relocation, RelocationBand, RelocationPayment, RelocationProgress } from '@/api/parcel-tabs'
import { fakeRecordsApi, type FakeRecords, type FakeRecordsOptions } from './fake-records'

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}
const problem = (status: number, title: string, detail: string, errors?: Record<string, string[]>) => json({ title, status, detail, errors }, status)

export interface FakeTabsOptions extends FakeRecordsOptions {
  contactLog?: (Partial<ContactLogEntry> & { parcelId: string })[]
  easements?: (Partial<Easement> & { parcelId: string; number: string })[]
  documents?: Partial<DocumentInfo>[]
  encumbrances?: (Partial<Encumbrance> & { parcelId: string })[]
  titleClosings?: (Partial<TitleClosing> & { parcelId: string })[]
  appraisals?: (Partial<Appraisal> & { parcelId: string })[]
  negotiations?: (Partial<Negotiation> & { parcelId: string })[]
  questionnaires?: (Partial<Questionnaire> & { parcelId: string })[]
  condemnations?: (Partial<Condemnation> & { parcelId: string })[]
  properties?: (Partial<PropertyRecord> & { parcelId: string })[]
  relocations?: (Partial<Relocation> & { parcelId: string })[]
  /** The relocation progress the server reports for every parcel. */
  progress?: Partial<RelocationProgress>
  bands?: RelocationBand[]
  /** The signed-in person's user id (the requester of fake payments), so "requested by me" can be tested. */
  userId?: string
}

export interface FakeTabs extends FakeRecords {
  contactLog: ContactLogEntry[]
  easements: Easement[]
  documents: DocumentInfo[]
  encumbrances: Encumbrance[]
  titleClosings: TitleClosing[]
  appraisals: Appraisal[]
  negotiations: Negotiation[]
  questionnaires: Questionnaire[]
  condemnations: Condemnation[]
  properties: PropertyRecord[]
  relocations: Relocation[]
  progress: RelocationProgress
  bands: { bands: RelocationBand[]; isDefault: boolean }
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
  const questionnaires: Questionnaire[] = (opts.questionnaires ?? []).map((q, i) => ({
    id: id('qs'), number: `QST-001-${String(i + 1).padStart(3, '0')}`, parcelNumber: '001', relocationType: 'Residential', dueByDate: null, sentToOwnerDate: null,
    interests: [], questions: [], updatedAt: '2026-01-01T00:00:00Z', ...q,
  }))
  const condemnations: Condemnation[] = (opts.condemnations ?? []).map((c) => ({
    id: id('cn'), number: 'CON-001', parcelNumber: '001', vestedOwnerName: null, details: {}, statusNotes: [], attorneys: [], commissioners: [], notices: [], updatedAt: '2026-01-01T00:00:00Z', ...c,
  }))
  const properties: PropertyRecord[] = (opts.properties ?? []).map((p) => ({
    id: id('pm'), number: 'PMD-001', parcelNumber: '001', details: {}, statusNotes: [], funding: [], updatedAt: '2026-01-01T00:00:00Z', ...p,
  }))
  const relocations: Relocation[] = (opts.relocations ?? []).map((r) => ({
    id: id('rl'), parcelNumber: '001', vestedOwnerName: null, details: { relocationNumber: 'R-1', displaceeName: 'Kamali', relocationType: 'Residential' }, statusNotes: [], agents: [], contacts: [],
    eligibilities: [], contactLog: [], updatedAt: '2026-01-01T00:00:00Z', payments: [], interestOwnerName: null, claimDeadline: null, stage: 'Not started', ...r,
  }))
  const progress: RelocationProgress = { displacedInterests: 0, completedInterests: 0, readyToAcquire: false, acquiredStatusAvailable: true, statusName: 'Inactive', isAcquired: false, ...opts.progress }
  const bands = { bands: opts.bands ?? [{ minAmount: 0, maxAmount: 25000, roles: ['ProjectManager'] }, { minAmount: 25000, maxAmount: null, roles: ['ProjectManager', 'Finance'] }], isDefault: !opts.bands }
  const rolesOf = () => [opts.role ?? '']
  const pageOf = (entityType: string) =>
    entityType === 'ParcelCondemnation' ? 'parcelcondemnation' : entityType === 'ParcelProperty' ? 'parcelproperty' : entityType === 'ParcelRelocation' ? 'parcelrelocation'
    : entityType === 'ParcelQuestionnaire' ? 'parcelquestionnaire' : entityType === 'ParcelNegotiation' ? 'parcelnegotiation' : entityType === 'ParcelAppraisal' ? 'parcelappraisal' : entityType === 'TitleClosing' ? 'titleclosing' : entityType === 'Encumbrance' ? 'titleencumbrances' : entityType === 'ContactLog' ? 'contactlog' : entityType === 'Easement' ? 'easements' : 'parcels'

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

    if (url.pathname === '/api/v1/relocation-payment-bands') {
      if (headers.Authorization !== 'Bearer good-token') return json({ title: 'unauthorized' }, 401)
      if (method === 'GET') return json(bands)
      if (!can('administration', 'update')) return forbidden()
      const sent = (JSON.parse(String(init?.body)).bands ?? []) as RelocationBand[]
      if (sent.length === 0 || sent[0].minAmount !== 0 || sent[sent.length - 1].maxAmount !== null) return problem(400, 'validation_failed', 'Validation failed.', { Bands: ['The first band must start at 0, and the last band must have no upper limit.'] })
      bands.bands = sent
      bands.isDefault = false
      return json(bands)
    }

    const pr = /^\/api\/v1\/parcels\/([^/]+)\/relocation-progress$/.exec(url.pathname)
    if (pr) {
      if (headers.Authorization !== 'Bearer good-token') return json({ title: 'unauthorized' }, 401)
      if (!can('parcelrelocation', 'read')) return forbidden()
      return json(progress)
    }

    const pm = /^\/api\/v1\/parcels\/([^/]+)\/relocations\/([^/]+)\/payments(?:\/([^/]+)\/(decision|paid))?$/.exec(url.pathname)
    if (pm) {
      if (headers.Authorization !== 'Bearer good-token') return json({ title: 'unauthorized' }, 401)
      const [, pid, rlid, payId, verb] = pm
      const body = JSON.parse(String(init?.body ?? '{}'))
      base.calls.push({ key: `${method} ${url.pathname}`, body })
      const r = relocations.find((x) => x.id === rlid && x.parcelId === pid)
      if (!r) return problem(404, 'not_found', 'Relocation was not found.')
      r.payments = r.payments ?? []
      if (!verb) {
        if (!can('parcelrelocation', 'update')) return forbidden()
        if (!(body.amount > 0)) return problem(400, 'validation_failed', 'Validation failed.', { Amount: ['Enter an amount above zero.'] })
        const chain = body.amount >= 25000 ? ['ProjectManager', 'Finance'] : ['ProjectManager']
        const p: RelocationPayment = {
          id: id('pay'), paymentType: body.paymentType, amount: body.amount, isAdvance: !!body.isAdvance, taxable: !!body.taxable, notes: body.notes ?? null, status: 'Requested',
          requiredRoles: chain, approvals: [], nextApprover: chain[0], decisionNote: null, paidAt: null, paymentReference: null, requestedAt: '2026-10-22T00:00:00Z', requestedByMe: true,
        }
        r.payments.push(p)
        return json(r)
      }
      const p = r.payments.find((x) => x.id === payId)
      if (!p) return problem(404, 'not_found', 'Payment was not found.')
      if (verb === 'paid') {
        if (!can('payments', 'update')) return forbidden()
        if (p.status !== 'Approved') return problem(409, 'payment_not_approved', 'Only an approved payment can be marked paid.')
        p.status = 'Paid'; p.paymentReference = body.reference ?? null; p.paidAt = '2026-10-22T00:00:00Z'
        return json(r)
      }
      if (p.requestedByMe) return problem(403, 'forbidden', 'You requested this payment, so someone else has to approve or reject it.')
      if (!rolesOf().includes(p.nextApprover ?? '') && opts.role !== 'Admin') return problem(403, 'forbidden', `The next sign-off on this payment is ${p.nextApprover}.`)
      if (!body.approve) {
        if (!String(body.note ?? '').trim()) return problem(400, 'validation_failed', 'Validation failed.', { Note: ['Say why the payment is rejected.'] })
        p.status = 'Rejected'; p.decisionNote = body.note; p.nextApprover = null
        return json(r)
      }
      p.approvals.push({ role: p.nextApprover!, approvedAt: '2026-10-22T00:00:00Z' })
      const next = p.requiredRoles.find((x) => !p.approvals.some((a) => a.role === x)) ?? null
      p.nextApprover = next
      if (!next) p.status = 'Approved'
      return json(r)
    }

    const om = /^\/api\/v1\/overview\/(appraisals|negotiations|title-closings|encumbrances|easements|relocations)$/.exec(url.pathname)
    if (om) {
      if (headers.Authorization !== 'Bearer good-token') return json({ title: 'unauthorized' }, 401)
      const kind = om[1] as 'appraisals' | 'negotiations' | 'title-closings' | 'encumbrances' | 'easements' | 'relocations'
      const page = { appraisals: 'parcelappraisal', negotiations: 'parcelnegotiation', 'title-closings': 'titleclosing', encumbrances: 'titleencumbrances', easements: 'easements', relocations: 'relocations' }[kind]
      if (!can(page, 'read')) return forbidden()
      const term = (url.searchParams.get('search') ?? '').trim().toLowerCase()
      const store = (kind === 'relocations'
        ? relocations.map((r) => ({ ...r, number: String(r.details.relocationNumber), displaceeName: r.details.displaceeName, relocationType: r.details.relocationType, assignedTo: r.details.assignedTo ?? null, targetVacateDate: r.details.targetVacateDate ?? null, openPayments: (r.payments ?? []).filter((p) => p.status === 'Requested' || p.status === 'Approved').length }))
        : ({ appraisals, negotiations, 'title-closings': titleClosings, encumbrances, easements }[kind] as unknown as { id: string; parcelId: string; number: string }[]))
      const rows = store.map((r) => {
        const parcel = base.parcels.find((p) => p.id === r.parcelId)
        return { ...r, parcelNumber: parcel?.number ?? '', address: parcel?.address ?? null, ownerName: parcel?.ownerName ?? null }
      }).filter((r) => !term || [r.parcelNumber, r.address, r.ownerName, r.number, (r as { displaceeName?: unknown }).displaceeName as string | undefined].some((v) => (v ?? '').toLowerCase().includes(term)))
      return json(rows)
    }

    const m = /^\/api\/v1\/parcels\/([^/]+)\/(contact-log|easements|encumbrances|title-closing|appraisals|negotiation|questionnaires|condemnation|property-management|relocations)(?:\/([^/]+))?$/.exec(url.pathname)
    if (!m) return base.fetch(input, init)
    if (headers.Authorization !== 'Bearer good-token') return json({ title: 'unauthorized' }, 401)
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    base.calls.push({ key: `${method} ${url.pathname}`, body })
    const [, parcelId, part, rid] = m
    const page = part === 'contact-log' ? 'contactlog' : part === 'encumbrances' ? 'titleencumbrances' : part === 'title-closing' ? 'titleclosing' : part === 'appraisals' ? 'parcelappraisal' : part === 'negotiation' ? 'parcelnegotiation' : part === 'questionnaires' ? 'parcelquestionnaire' : part === 'condemnation' ? 'parcelcondemnation' : part === 'property-management' ? 'parcelproperty' : part === 'relocations' ? 'parcelrelocation' : 'easements'
    const existingTc = titleClosings.find((x) => x.parcelId === parcelId)
    const existingNg = negotiations.find((x) => x.parcelId === parcelId)
    const existingCn = condemnations.find((x) => x.parcelId === parcelId)
    const existingPm = properties.find((x) => x.parcelId === parcelId)
    const act = method === 'GET' ? 'read' : method === 'POST' ? 'create' : method === 'PUT' ? ((part === 'title-closing' && !existingTc) || (part === 'negotiation' && !existingNg) || (part === 'condemnation' && !existingCn) || (part === 'property-management' && !existingPm) ? 'create' : 'update') : 'delete'
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
      const vestedNames = base.links.filter((l) => l.parcelId === parcelId && l.ownerType === 'Vested Owner').map((l) => base.parties.find((p) => p.id === l.partyId)?.name).filter((n): n is string => !!n)
      const vested = vestedNames.length ? vestedNames.join('; ') : null
      if (existingTc) { Object.assign(existingTc, fields); return json(existingTc) }
      const tc: TitleClosing = { id: id('tc'), parcelId, number: `TC-${parcel.number}`, parcelNumber: parcel.number, vestedOwnerName: vested, ...fields }
      titleClosings.push(tc)
      return json(tc)
    }

    if (part === 'condemnation' || part === 'property-management') {
      const existing = part === 'condemnation' ? existingCn : existingPm
      const store = (part === 'condemnation' ? condemnations : properties) as { parcelId: string }[]
      if (method === 'GET') return json({ record: existing ?? null })
      if (method === 'DELETE') {
        if (!existing) return problem(404, 'not_found', 'Record was not found.')
        store.splice(store.indexOf(existing), 1)
        return new Response(null, { status: 204 })
      }
      const errors: Record<string, string[]> = {}
      const d = body?.details ?? {}
      if (part === 'property-management' && d.salePrice !== null && d.salePrice !== undefined && d.salePrice < 0) errors.SalePrice = ['The amount must be zero or more.']
      if (part === 'condemnation' && d.prelimPackageSubmittedToAtp && d.prelimPackageApprovedByAtp && d.prelimPackageApprovedByAtp < d.prelimPackageSubmittedToAtp) errors.PrelimPackageApprovedByAtp = ['The package cannot be approved before it is submitted.']
      if (Object.keys(errors).length) return problem(400, 'validation_failed', 'Validation failed.', errors)
      const withIds = <T extends object>(rows: T[] | undefined): (T & { id: string })[] => (rows ?? []).map((r) => ({ id: id('x'), ...r }))
      const common = { ...body, statusNotes: withIds(body?.statusNotes), updatedAt: `2026-10-14T00:00:${String(seq % 60).padStart(2, '0')}Z` }
      const fields = part === 'condemnation'
        ? { ...common, attorneys: withIds(body?.attorneys), commissioners: withIds(body?.commissioners), notices: withIds(body?.notices) }
        : { ...common, funding: withIds(body?.funding) }
      if (existing) { Object.assign(existing, fields); return json(existing) }
      if (part === 'condemnation') {
        const c: Condemnation = { id: id('cn'), parcelId, number: `CON-${parcel.number}`, parcelNumber: parcel.number, vestedOwnerName: null, ...(fields as object) } as Condemnation
        condemnations.push(c)
        return json(c)
      }
      const p: PropertyRecord = { id: id('pm'), parcelId, number: `PMD-${parcel.number}`, parcelNumber: parcel.number, ...(fields as object) } as PropertyRecord
      properties.push(p)
      return json(p)
    }

    if (part === 'relocations') {
      if (method === 'GET') return json(relocations.filter((r) => r.parcelId === parcelId))
      if (method === 'DELETE') {
        const i = relocations.findIndex((r) => r.id === rid && r.parcelId === parcelId)
        if (i < 0) return problem(404, 'not_found', 'Relocation was not found.')
        relocations.splice(i, 1)
        return new Response(null, { status: 204 })
      }
      const d = body?.details ?? {}
      const errors: Record<string, string[]> = {}
      if (!String(d.relocationNumber ?? '').trim()) errors.RelocationNumber = ['Relocation number is required.']
      if (!String(d.displaceeName ?? '').trim()) errors.DisplaceeName = ['Displacee name is required.']
      if (!String(d.relocationType ?? '').trim()) errors.RelocationType = ['Relocation type is required.']
      if (Object.keys(errors).length) return problem(400, 'validation_failed', 'Validation failed.', errors)
      if (relocations.some((r) => r.parcelId === parcelId && r.details.relocationNumber === d.relocationNumber && r.id !== rid))
        return problem(409, 'relocation_number_exists', `This parcel already has a relocation numbered '${d.relocationNumber}'.`)
      const withIds = <T extends object>(rows: T[] | undefined): (T & { id: string })[] => (rows ?? []).map((r) => ({ id: id('x'), ...r }))
      const fields = {
        ...body, statusNotes: withIds(body?.statusNotes), agents: withIds(body?.agents), contacts: withIds(body?.contacts), eligibilities: withIds(body?.eligibilities),
        contactLog: withIds(body?.contactLog), updatedAt: `2026-10-14T00:00:${String(seq % 60).padStart(2, '0')}Z`,
      }
      if (method === 'PUT') {
        const r = relocations.find((x) => x.id === rid && x.parcelId === parcelId)
        if (!r) return problem(404, 'not_found', 'Relocation was not found.')
        Object.assign(r, fields)
        return json(r)
      }
      const r: Relocation = { id: id('rl'), parcelId, parcelNumber: parcel.number, vestedOwnerName: null, payments: [], interestOwnerName: null, claimDeadline: null, stage: 'Not started', ...fields }
      relocations.push(r)
      return json(r, 201)
    }

    if (part === 'questionnaires') {
      if (method === 'GET') return json(questionnaires.filter((q) => q.parcelId === parcelId))
      if (method === 'DELETE') {
        const i = questionnaires.findIndex((q) => q.id === rid && q.parcelId === parcelId)
        if (i < 0) return problem(404, 'not_found', 'Questionnaire was not found.')
        questionnaires.splice(i, 1)
        return new Response(null, { status: 204 })
      }
      const errors: Record<string, string[]> = {}
      if (!String(body?.relocationType ?? '').trim()) errors.RelocationType = ['Relocation type is required.']
      if (Object.keys(errors).length) return problem(400, 'validation_failed', 'Validation failed.', errors)
      const withIds = <T extends object>(rows: T[] | undefined): (T & { id: string })[] => (rows ?? []).map((r) => ({ id: id('x'), ...r }))
      const fields = { ...body, interests: withIds(body.interests), questions: withIds(body.questions), updatedAt: `2026-10-14T00:00:${String(seq % 60).padStart(2, '0')}Z` }
      if (method === 'PUT') {
        const q = questionnaires.find((x) => x.id === rid && x.parcelId === parcelId)
        if (!q) return problem(404, 'not_found', 'Questionnaire was not found.')
        Object.assign(q, fields)
        return json(q)
      }
      const next = Math.max(0, ...questionnaires.filter((q) => q.parcelId === parcelId).map((q) => Number(q.number.split('-').pop()))) + 1
      const q: Questionnaire = { id: id('qs'), parcelId, number: `QST-${parcel.number}-${String(next).padStart(3, '0')}`, parcelNumber: parcel.number, ...fields }
      questionnaires.push(q)
      return json(q, 201)
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
      const fields = {
        ...body, statusNotes: withIds(body?.statusNotes), offers: withIds(body?.offers), settlements: withIds(body?.settlements),
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

  return { ...base, fetch: fetchImpl as typeof fetch, contactLog, easements, documents, encumbrances, titleClosings, appraisals, negotiations, questionnaires, condemnations, properties, relocations, progress, bands }
}
