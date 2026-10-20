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
