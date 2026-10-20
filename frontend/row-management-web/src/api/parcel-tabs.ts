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

export interface OwnershipAddress {
  id?: string
  addressType: string | null
  address: string | null
  contactPerson: string | null
  city: string | null
  state: string | null
  zip: string | null
}

export interface OwnershipContact {
  id?: string
  contactType: string | null
  name: string
  phone: string | null
  email: string | null
  address: string | null
  isActive: boolean
  isPrimary: boolean
}

export interface Ownership {
  id: string
  parcelId: string
  ownershipNumber: number
  parcelNumber: string
  ownerName: string
  ownerType: string | null
  addresses: OwnershipAddress[]
  contacts: OwnershipContact[]
  updatedAt: string
}

export interface OwnershipInput {
  ownerName: string
  ownerType: string | null
  addresses: OwnershipAddress[]
  contacts: OwnershipContact[]
}

export const ownershipApi = (api: ApiClient) => ({
  list: (parcelId: string) => api.get<Ownership[]>(`/api/v1/parcels/${parcelId}/ownerships`),
  add: (parcelId: string, body: OwnershipInput) => api.post<Ownership>(`/api/v1/parcels/${parcelId}/ownerships`, body),
  update: (parcelId: string, id: string, body: OwnershipInput) => api.put<Ownership>(`/api/v1/parcels/${parcelId}/ownerships/${id}`, body),
  remove: (parcelId: string, id: string) => api.delete(`/api/v1/parcels/${parcelId}/ownerships/${id}`),
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
  /** Read-only: the parcel's ownerships of type "Vested Owner". */
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
/** Type, company, phone and email are read-only: they come from the Library agent of that name. */
export interface NegotiationAgent { id?: string; agentName: string; agentType: string | null; company: string | null; phone: string | null; email: string | null }
export interface NegotiationRightOfEntry {
  id?: string
  letterMailedDate: string | null
  letterDeliveredDate: string | null
  signedReceivedDate: string | null
  sentToAtpDate: string | null
  expirationDate: string | null
  specialInstructions: string | null
}
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
  agents: NegotiationAgent[]
  rightsOfEntry: NegotiationRightOfEntry[]
  offers: NegotiationOffer[]
  settlements: NegotiationSettlement[]
  updatedAt: string
}

export type NegotiationInput = Omit<Negotiation, 'id' | 'parcelId' | 'number' | 'parcelNumber' | 'updatedAt' | 'agents'> & { agents: { agentName: string }[] }

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
