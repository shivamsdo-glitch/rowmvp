import type { ApiClient } from './client'

// The Contact Log and Easements tabs of a parcel (Phase 1.5, ADR 0017), and the attachments any record can carry.

export interface ContactLogLinkInput {
  recordIdentifier: string
  moduleName: string | null
  notes: string | null
}

export interface ContactLogLink extends ContactLogLinkInput {
  id: string
}

export interface ContactLogEntry {
  id: string
  parcelId: string
  number: string
  contactedAt: string
  mode: string | null
  contactedBy: string | null
  contactPerson: string | null
  notes: string | null
  links: ContactLogLink[]
  createdAt: string
  authorName: string | null
}

export interface ContactLogInput {
  contactedAt: string
  mode: string | null
  contactedBy: string | null
  contactPerson: string | null
  notes: string | null
  links: ContactLogLinkInput[]
}

export interface Easement {
  id: string
  parcelId: string
  number: string
  easementType: string | null
  description: string | null
  startDate: string | null
  endDate: string | null
  startStation: string | null
  endStation: string | null
  updatedAt: string
}

export type EasementInput = Omit<Easement, 'id' | 'parcelId' | 'updatedAt'>

export const parcelTabsApi = (api: ApiClient) => ({
  contactLog: (parcelId: string) => api.get<ContactLogEntry[]>(`/api/v1/parcels/${parcelId}/contact-log`),
  addContactLog: (parcelId: string, body: ContactLogInput) => api.post<ContactLogEntry>(`/api/v1/parcels/${parcelId}/contact-log`, body),
  updateContactLog: (parcelId: string, id: string, body: ContactLogInput) => api.put<ContactLogEntry>(`/api/v1/parcels/${parcelId}/contact-log/${id}`, body),
  removeContactLog: (parcelId: string, id: string) => api.delete(`/api/v1/parcels/${parcelId}/contact-log/${id}`),

  easements: (parcelId: string) => api.get<Easement[]>(`/api/v1/parcels/${parcelId}/easements`),
  addEasement: (parcelId: string, body: EasementInput) => api.post<Easement>(`/api/v1/parcels/${parcelId}/easements`, body),
  updateEasement: (parcelId: string, id: string, body: EasementInput) => api.put<Easement>(`/api/v1/parcels/${parcelId}/easements/${id}`, body),
  removeEasement: (parcelId: string, id: string) => api.delete(`/api/v1/parcels/${parcelId}/easements/${id}`),
})

export interface DocumentInfo {
  id: string
  entityType: string
  entityId: string
  fileName: string
  contentType: string
  sizeBytes: number
  docType: string | null
  description: string | null
  uploadedAt: string
  uploadedBy: string
}

export const documentsApi = (api: ApiClient) => ({
  list: (entityType: string, entityId: string) => api.get<DocumentInfo[]>('/api/v1/documents', { entityType, entityId }),
  upload: (entityType: string, entityId: string, file: File) => {
    const form = new FormData()
    form.append('entityType', entityType)
    form.append('entityId', entityId)
    form.append('file', file)
    return api.upload<DocumentInfo>('/api/v1/documents', form)
  },
  remove: (id: string) => api.delete(`/api/v1/documents/${id}`),
})

export interface Encumbrance {
  id: string
  parcelId: string
  number: string
  encumbranceType: string
  documentTitle: string
  documentDate: string | null
  dateRecorded: string | null
  documentNumber: string | null
  isUnrecorded: boolean
  grantor: string
  grantee: string
  exceptionSubmittedDate: string | null
  exceptionApprovedDate: string | null
  impactAnalysis: string | null
  recommendedCure: string | null
  cureStatus: string | null
  reviewNotes: string | null
  links: ContactLogLink[]
  updatedAt: string
}

export interface EncumbranceInput {
  encumbranceType: string
  documentTitle: string
  documentDate: string | null
  dateRecorded: string | null
  documentNumber: string | null
  isUnrecorded: boolean
  grantor: string
  grantee: string
  exceptionSubmittedDate: string | null
  exceptionApprovedDate: string | null
  impactAnalysis: string | null
  recommendedCure: string | null
  cureStatus: string | null
  reviewNotes: string | null
  links: ContactLogLinkInput[]
}

export const encumbrancesApi = (api: ApiClient) => ({
  list: (parcelId: string) => api.get<Encumbrance[]>(`/api/v1/parcels/${parcelId}/encumbrances`),
  add: (parcelId: string, body: EncumbranceInput) => api.post<Encumbrance>(`/api/v1/parcels/${parcelId}/encumbrances`, body),
  update: (parcelId: string, id: string, body: EncumbranceInput) => api.put<Encumbrance>(`/api/v1/parcels/${parcelId}/encumbrances/${id}`, body),
  remove: (parcelId: string, id: string) => api.delete(`/api/v1/parcels/${parcelId}/encumbrances/${id}`),
})

export interface ConveyanceDocument {
  id?: string
  conveyanceType: string | null
  signedDate: string | null
  recordedDate: string | null
  recordingNumber: string | null
}

export interface TitleCommitment {
  id?: string
  vestedOwnerName: string
  requestedDate: string | null
  receivedDate: string | null
  effectiveDate: string | null
  issuedDate: string | null
  curativePlanSubmittedDate: string | null
  curativePlanApprovedDate: string | null
}

export interface TitleClosing {
  id: string
  parcelId: string
  number: string
  parcelNumber: string
  /** Read-only: the parcel's owners of owner type "Vested Owner". */
  vestedOwnerName: string | null
  assignedTo: string | null
  titleCompany: string | null
  titleClosingAgent: string | null
  gfNumber: string | null
  finalTitlePolicyNo: string | null
  psaSignedByOwnerDate: string | null
  psaSignedByAtpDate: string | null
  psaAmount: number | null
  settlementReceivedDate: string | null
  settlementExecutedDate: string | null
  curativeItemsCompleted: boolean
  closingPackageSubmittedDate: string | null
  closingPackageApprovedDate: string | null
  closingInstructionDate: string | null
  anticipatedClosingDate: string | null
  actualClosingDate: string | null
  conveyanceDocuments: ConveyanceDocument[]
  titleCommitments: TitleCommitment[]
  updatedAt: string
}

export type TitleClosingInput = Omit<TitleClosing, 'id' | 'parcelId' | 'number' | 'parcelNumber' | 'vestedOwnerName' | 'updatedAt'>

export const titleClosingApi = (api: ApiClient) => ({
  get: (parcelId: string) => api.get<{ record: TitleClosing | null }>(`/api/v1/parcels/${parcelId}/title-closing`),
  save: (parcelId: string, body: TitleClosingInput) => api.put<TitleClosing>(`/api/v1/parcels/${parcelId}/title-closing`, body),
  remove: (parcelId: string) => api.delete(`/api/v1/parcels/${parcelId}/title-closing`),
})

export interface AppraisalAmount {
  id?: string
  amountType: string
  description: string | null
  amount: number
}

export interface Appraisal {
  id: string
  parcelId: string
  number: string
  parcelNumber: string
  appraisalType: string | null
  appraiser: string | null
  /** Read-only: the appraiser's company in the Library's Agents list. */
  appraiserCompany: string | null
  landPlannerRequired: boolean
  assignedTo: string | null
  requestedDate: string | null
  receivedDate: string | null
  reportDate: string | null
  dueDate: string | null
  submittedToAtpDate: string | null
  effectiveDate: string | null
  amounts: AppraisalAmount[]
  /** Read-only: the amounts added up. */
  appraisalTotal: number
  reviewAppraiser: string | null
  reviewType: string | null
  appraisalReceivedDate: string | null
  approvedJustCompensation: number | null
  reviewCompletedDate: string | null
  reviewSubmittedDate: string | null
  reviewRecommendation: string | null
  reviewRecommendedValue: number | null
  submittedToJatDate: string | null
  approvedByJatDate: string | null
  ftaConcurrenceNeeded: boolean
  updatedAt: string
}

export type AppraisalInput = Omit<Appraisal, 'id' | 'parcelId' | 'number' | 'parcelNumber' | 'appraiserCompany' | 'appraisalTotal' | 'updatedAt'>

export const appraisalsApi = (api: ApiClient) => ({
  list: (parcelId: string) => api.get<Appraisal[]>(`/api/v1/parcels/${parcelId}/appraisals`),
  add: (parcelId: string, body: AppraisalInput) => api.post<Appraisal>(`/api/v1/parcels/${parcelId}/appraisals`, body),
  update: (parcelId: string, id: string, body: AppraisalInput) => api.put<Appraisal>(`/api/v1/parcels/${parcelId}/appraisals/${id}`, body),
  remove: (parcelId: string, id: string) => api.delete(`/api/v1/parcels/${parcelId}/appraisals/${id}`),
})

export interface NegotiationNote { id?: string; noteDate: string; text: string }
export interface NegotiationOffer { id?: string; offerType: string | null; mailedDate: string | null; deliveredDate: string | null; amount: number | null; notes: string | null }
export interface NegotiationSettlement {
  id?: string
  settlementType: string | null
  counterofferAmount: number | null
  dateReceived: string | null
  dateSubmitted: string | null
  counterofferResponse: string | null
  responseDateFromAtp: string | null
  responseDateToOwner: string | null
  settlementNotes: string | null
}

export interface Negotiation {
  id: string
  parcelId: string
  number: string
  parcelNumber: string
  assignedTo: string | null
  totalAgreementAmount: number | null
  notes: string | null
  counterofferSubmittedToJatDate: string | null
  counterofferResponseFromJatDate: string | null
  jatResponse: string | null
  ftaApprovalRequiredForCounteroffer: boolean
  puaRequestedDate: string | null
  puaApprovedDate: string | null
  puaApprovedAmount: number | null
  puaExecutedDate: string | null
  puaClosedDate: string | null
  statusNotes: NegotiationNote[]
  offers: NegotiationOffer[]
  settlements: NegotiationSettlement[]
  updatedAt: string
}

export type NegotiationInput = Omit<Negotiation, 'id' | 'parcelId' | 'number' | 'parcelNumber' | 'updatedAt'>

export const negotiationApi = (api: ApiClient) => ({
  get: (parcelId: string) => api.get<{ record: Negotiation | null }>(`/api/v1/parcels/${parcelId}/negotiation`),
  save: (parcelId: string, body: NegotiationInput) => api.put<Negotiation>(`/api/v1/parcels/${parcelId}/negotiation`, body),
  remove: (parcelId: string) => api.delete(`/api/v1/parcels/${parcelId}/negotiation`),
})

export interface QuestionnaireInterest { id?: string; name: string; phone: string | null; contact: string | null; email: string | null; address: string | null }
export interface QuestionnaireQuestion { id?: string; question: string; answer: string | null; notes: string | null }

export interface Questionnaire {
  id: string
  parcelId: string
  number: string
  parcelNumber: string
  relocationType: string
  dueByDate: string | null
  sentToOwnerDate: string | null
  interests: QuestionnaireInterest[]
  questions: QuestionnaireQuestion[]
  updatedAt: string
}

export type QuestionnaireInput = Omit<Questionnaire, 'id' | 'parcelId' | 'number' | 'parcelNumber' | 'updatedAt'>

export const questionnairesApi = (api: ApiClient) => ({
  list: (parcelId: string) => api.get<Questionnaire[]>(`/api/v1/parcels/${parcelId}/questionnaires`),
  add: (parcelId: string, body: QuestionnaireInput) => api.post<Questionnaire>(`/api/v1/parcels/${parcelId}/questionnaires`, body),
  update: (parcelId: string, id: string, body: QuestionnaireInput) => api.put<Questionnaire>(`/api/v1/parcels/${parcelId}/questionnaires/${id}`, body),
  remove: (parcelId: string, id: string) => api.delete(`/api/v1/parcels/${parcelId}/questionnaires/${id}`),
})

export interface StatusNote { id?: string; noteDate: string; text: string }
/** Dates, people and flags of a record, by field name; null when empty. */
export type DetailValues = Record<string, string | number | boolean | null>

export interface CondemnationAttorney { id?: string; partyRepresented: string; attorneyName: string | null; firm: string | null; address: string | null; phone: string | null; email: string | null }
export interface CondemnationCommissioner {
  id?: string
  appointmentDate: string | null
  appointmentType: string | null
  name: string
  address: string | null
  email: string | null
  phone: string | null
  struck: boolean
}
export interface CondemnationNotice { id?: string; partyToServe: string; serviceSubmitted: string | null; serviceCompleted: string | null; nohFiled: string | null; comments: string | null }

export interface Condemnation {
  id: string
  parcelId: string
  number: string
  parcelNumber: string
  /** Read-only: the parcel's owners of owner type "Vested Owner". */
  vestedOwnerName: string | null
  details: DetailValues
  statusNotes: StatusNote[]
  attorneys: CondemnationAttorney[]
  commissioners: CondemnationCommissioner[]
  notices: CondemnationNotice[]
  updatedAt: string
}
export interface CondemnationInput {
  details: DetailValues
  statusNotes: StatusNote[]
  attorneys: CondemnationAttorney[]
  commissioners: CondemnationCommissioner[]
  notices: CondemnationNotice[]
}

export const condemnationApi = (api: ApiClient) => ({
  get: (parcelId: string) => api.get<{ record: Condemnation | null }>(`/api/v1/parcels/${parcelId}/condemnation`),
  save: (parcelId: string, body: CondemnationInput) => api.put<Condemnation>(`/api/v1/parcels/${parcelId}/condemnation`, body),
  remove: (parcelId: string) => api.delete(`/api/v1/parcels/${parcelId}/condemnation`),
})

export interface PropertyFunding { id?: string; fundType: string; fundSource: string | null; amount: number | null; percentage: number | null }
export interface PropertyRecord {
  id: string
  parcelId: string
  number: string
  parcelNumber: string
  details: DetailValues
  statusNotes: StatusNote[]
  funding: PropertyFunding[]
  updatedAt: string
}
export interface PropertyInput { details: DetailValues; statusNotes: StatusNote[]; funding: PropertyFunding[] }

export const propertyApi = (api: ApiClient) => ({
  get: (parcelId: string) => api.get<{ record: PropertyRecord | null }>(`/api/v1/parcels/${parcelId}/property-management`),
  save: (parcelId: string, body: PropertyInput) => api.put<PropertyRecord>(`/api/v1/parcels/${parcelId}/property-management`, body),
  remove: (parcelId: string) => api.delete(`/api/v1/parcels/${parcelId}/property-management`),
})

export interface RelocationAgent { id?: string; agentName: string; agentType?: string | null; company?: string | null; phone?: string | null; email?: string | null }
export interface RelocationContact { id?: string; name: string; contactType: string | null; contact: string | null; email: string | null; address: string | null; isPrimary: boolean }
export interface RelocationEligibility { id?: string; eligibilityType: string; claimLimit: number | null; submittedForApproval: string | null; approved: string | null }
export interface RelocationContactLog { id?: string; contactedAt: string; mode: string | null; contactedBy: string | null; contactPersons: string | null; notes: string | null }

export interface Relocation {
  id: string
  parcelId: string
  parcelNumber: string
  /** Read-only: the parcel's owners of owner type "Vested Owner". */
  vestedOwnerName: string | null
  details: DetailValues
  statusNotes: StatusNote[]
  agents: RelocationAgent[]
  contacts: RelocationContact[]
  eligibilities: RelocationEligibility[]
  contactLog: RelocationContactLog[]
  updatedAt: string
}
export interface RelocationInput {
  details: DetailValues
  statusNotes: StatusNote[]
  agents: { agentName: string }[]
  contacts: RelocationContact[]
  eligibilities: RelocationEligibility[]
  contactLog: RelocationContactLog[]
}

export const relocationsApi = (api: ApiClient) => ({
  list: (parcelId: string) => api.get<Relocation[]>(`/api/v1/parcels/${parcelId}/relocations`),
  add: (parcelId: string, body: RelocationInput) => api.post<Relocation>(`/api/v1/parcels/${parcelId}/relocations`, body),
  update: (parcelId: string, id: string, body: RelocationInput) => api.put<Relocation>(`/api/v1/parcels/${parcelId}/relocations/${id}`, body),
  remove: (parcelId: string, id: string) => api.delete(`/api/v1/parcels/${parcelId}/relocations/${id}`),
})
