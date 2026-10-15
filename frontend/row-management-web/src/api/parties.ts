import type { ApiClient } from './client'
import type { Paged } from './projects'

// Owners, interests, contacts, tenants, abutters and W-9 tax information (ROW-014..018).

export const PARTY_KINDS = ['Person', 'Organization'] as const
export type PartyKind = (typeof PARTY_KINDS)[number]

export const TAX_ID_TYPES = ['Ssn', 'Ein', 'Itin'] as const
export type TaxIdType = (typeof TAX_ID_TYPES)[number]
export const TAX_ID_LABEL: Record<TaxIdType, string> = {
  Ssn: 'Social Security number (SSN)',
  Ein: 'Employer ID number (EIN)',
  Itin: 'Individual taxpayer ID (ITIN)',
}

export type PartyCategory = 'Ownership' | 'Contact' | 'Occupant' | 'Abutter'

export const PARTY_ROLES = [
  'FeeOwner', 'EasementHolder', 'Lienholder', 'Trustee', 'LifeEstateHolder', 'OtherInterest',
  'Attorney', 'Bank', 'OwnerDesignee', 'OwnerRepresentative', 'OtherContact',
  'Tenant', 'Occupant',
  'Abutter', 'InterestedParty',
] as const
export type PartyRole = (typeof PARTY_ROLES)[number]

export const ROLE_LABEL: Record<PartyRole, string> = {
  FeeOwner: 'Fee owner',
  EasementHolder: 'Easement holder',
  Lienholder: 'Lienholder',
  Trustee: 'Trustee',
  LifeEstateHolder: 'Life-estate holder',
  OtherInterest: 'Other interest holder',
  Attorney: 'Attorney',
  Bank: 'Bank or lender',
  OwnerDesignee: 'Owner designee',
  OwnerRepresentative: 'Owner representative',
  OtherContact: 'Other contact',
  Tenant: 'Tenant',
  Occupant: 'Occupant',
  Abutter: 'Abutter',
  InterestedParty: 'Interested party',
}

export const CATEGORY_OF: Record<PartyRole, PartyCategory> = {
  FeeOwner: 'Ownership', EasementHolder: 'Ownership', Lienholder: 'Ownership', Trustee: 'Ownership', LifeEstateHolder: 'Ownership', OtherInterest: 'Ownership',
  Attorney: 'Contact', Bank: 'Contact', OwnerDesignee: 'Contact', OwnerRepresentative: 'Contact', OtherContact: 'Contact',
  Tenant: 'Occupant', Occupant: 'Occupant',
  Abutter: 'Abutter', InterestedParty: 'Abutter',
}

/** Roles that hold a share of the interest. */
export const CARRIES_SHARE: readonly PartyRole[] = ['FeeOwner', 'EasementHolder', 'LifeEstateHolder']
export const HAS_LEASE: readonly PartyRole[] = ['Tenant']

export const CATEGORY_LABEL: Record<PartyCategory, string> = {
  Ownership: 'Owners and interests',
  Contact: 'Contacts',
  Occupant: 'Tenants and occupants',
  Abutter: 'Abutters and interested parties',
}

export interface PartySummary {
  id: string
  kind: PartyKind
  name: string
  city: string | null
  state: string | null
  phone: string | null
  email: string | null
  hasTaxId: boolean
  linkCount: number
}

export interface Party {
  id: string
  kind: PartyKind
  name: string
  legalName: string | null
  email: string | null
  phone: string | null
  addressLine1: string | null
  addressLine2: string | null
  city: string | null
  state: string | null
  postalCode: string | null
  notes: string | null
  taxIdType: TaxIdType | null
  /** The last four digits in the shape of the type, for example `***-**-1234`. The number itself is never returned. */
  taxIdMasked: string | null
  hasTaxId: boolean
  w9ReceivedOn: string | null
  reportable1099: boolean
  linkCount: number
  updatedAt: string
}

export interface PartyInput {
  kind: PartyKind
  name: string
  legalName: string | null
  email: string | null
  phone: string | null
  addressLine1: string | null
  addressLine2: string | null
  city: string | null
  state: string | null
  postalCode: string | null
  notes: string | null
}

export interface PartyLink {
  id: string
  category: PartyCategory
  role: PartyRole
  sharePercent: number | null
  notes: string | null
  leaseOnFile: boolean
  leaseDetails: string | null
  insuranceDetails: string | null
  /** "Parcel" for what is recorded on the parcel itself, "Tract" for what it inherits from its parent tract. */
  source: 'Parcel' | 'Tract'
  parentTractId: string | null
  tractNumber: string | null
  parcelId: string | null
  party: PartySummary
}

export interface ParcelParties {
  ownership: PartyLink[]
  contacts: PartyLink[]
  occupants: PartyLink[]
  abutters: PartyLink[]
  parentTractId: string | null
  tractNumber: string | null
}

export interface PartyUsage {
  linkId: string
  role: PartyRole
  category: PartyCategory
  targetKind: 'Parcel' | 'Tract'
  targetId: string
  targetLabel: string
  projectId: string
  projectNumber: string
}

export interface PartyLinkInput {
  /** An existing party, or `newParty` to make one while linking. Exactly one. */
  partyId: string | null
  newParty: PartyInput | null
  role: PartyRole
  sharePercent: number | null
  notes: string | null
  leaseOnFile: boolean
  leaseDetails: string | null
  insuranceDetails: string | null
}

export interface PartyLinkChange {
  sharePercent: number | null
  notes: string | null
  leaseOnFile: boolean
  leaseDetails: string | null
  insuranceDetails: string | null
}

export interface PartyTaxInput {
  type: TaxIdType | null
  /** Sent to the server and never kept: leave empty to keep the stored number. */
  taxId: string | null
  w9ReceivedOn: string | null
  reportable1099: boolean
}

export interface PartyListParams {
  search?: string
  kind?: PartyKind | ''
  page?: number
  pageSize?: number
}

export const partiesApi = (api: ApiClient) => ({
  list: (p: PartyListParams = {}) =>
    api.get<Paged<PartySummary>>('/api/v1/parties', { search: p.search || undefined, kind: p.kind || undefined, page: p.page, pageSize: p.pageSize }),
  get: (id: string) => api.get<Party>(`/api/v1/parties/${id}`),
  usage: (id: string) => api.get<PartyUsage[]>(`/api/v1/parties/${id}/usage`),
  create: (body: PartyInput) => api.post<Party>('/api/v1/parties', body),
  update: (id: string, body: PartyInput) => api.put<Party>(`/api/v1/parties/${id}`, body),
  remove: (id: string) => api.delete(`/api/v1/parties/${id}`),
  setTax: (id: string, body: PartyTaxInput) => api.put<Party>(`/api/v1/parties/${id}/tax-info`, body),
  clearTaxId: (id: string) => api.delete(`/api/v1/parties/${id}/tax-info/number`),

  forParcel: (parcelId: string) => api.get<ParcelParties>(`/api/v1/parcels/${parcelId}/parties`),
  forTract: (projectId: string, tractId: string) => api.get<ParcelParties>(`/api/v1/projects/${projectId}/tracts/${tractId}/parties`),
  addToParcel: (parcelId: string, body: PartyLinkInput) => api.post<PartyLink>(`/api/v1/parcels/${parcelId}/parties`, body),
  addToTract: (projectId: string, tractId: string, body: PartyLinkInput) =>
    api.post<PartyLink>(`/api/v1/projects/${projectId}/tracts/${tractId}/parties`, body),
  updateLink: (linkId: string, body: PartyLinkChange) => api.put<PartyLink>(`/api/v1/party-links/${linkId}`, body),
  removeLink: (linkId: string) => api.delete(`/api/v1/party-links/${linkId}`),
})
