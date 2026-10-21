import type { ApiClient } from './client'

// A parcel's team, negotiation diary and notes (ROW-019, 020, 021). The team is picked from the Library (ADR 0023).

export const DIARY_METHODS = ['Call', 'Meeting', 'Mailing', 'Email', 'ContactAttempt', 'Other'] as const
export type DiaryMethod = (typeof DIARY_METHODS)[number]
export const DIARY_METHOD_LABEL: Record<DiaryMethod, string> = {
  Call: 'Call', Meeting: 'Meeting', Mailing: 'Mailing', Email: 'Email', ContactAttempt: 'Contact attempt', Other: 'Other',
}
export type DiaryDirection = 'Outgoing' | 'Incoming'

/** A person on the parcel's team: picked from the Library's Agents list, so the role is the agent's type and the contact details are the Library's. */
export interface TeamMember {
  id: string
  parcelId: string
  /** The agent type (a value of the Library's Agent type list). */
  role: string
  agentId: string | null
  name: string
  organization: string | null
  email: string | null
  phone: string | null
  notes: string | null
  updatedAt: string
}

export interface TeamMemberInput {
  agentId: string
  notes: string | null
}

export interface DiaryEntry {
  id: string
  parcelId: string
  occurredOn: string
  method: DiaryMethod
  direction: DiaryDirection
  contactName: string
  partyId: string | null
  summary: string
  outcome: string | null
  createdAt: string
  createdBy: string
  authorName: string | null
}

export interface DiaryEntryInput {
  occurredOn: string
  method: DiaryMethod
  direction: DiaryDirection
  contactName: string | null
  partyId: string | null
  summary: string
  outcome: string | null
}

export interface ParcelNote {
  id: string
  parcelId: string
  text: string
  createdAt: string
  createdBy: string
  authorName: string | null
}

export const parcelRecordsApi = (api: ApiClient) => ({
  team: (parcelId: string) => api.get<TeamMember[]>(`/api/v1/parcels/${parcelId}/team`),
  addTeam: (parcelId: string, body: TeamMemberInput) => api.post<TeamMember>(`/api/v1/parcels/${parcelId}/team`, body),
  updateTeam: (parcelId: string, id: string, body: TeamMemberInput) => api.put<TeamMember>(`/api/v1/parcels/${parcelId}/team/${id}`, body),
  removeTeam: (parcelId: string, id: string) => api.delete(`/api/v1/parcels/${parcelId}/team/${id}`),

  diary: (parcelId: string) => api.get<DiaryEntry[]>(`/api/v1/parcels/${parcelId}/diary`),
  addDiary: (parcelId: string, body: DiaryEntryInput) => api.post<DiaryEntry>(`/api/v1/parcels/${parcelId}/diary`, body),
  removeDiary: (parcelId: string, id: string) => api.delete(`/api/v1/parcels/${parcelId}/diary/${id}`),

  notes: (parcelId: string) => api.get<ParcelNote[]>(`/api/v1/parcels/${parcelId}/notes`),
  addNote: (parcelId: string, text: string) => api.post<ParcelNote>(`/api/v1/parcels/${parcelId}/notes`, { text }),
  removeNote: (parcelId: string, id: string) => api.delete(`/api/v1/parcels/${parcelId}/notes/${id}`),
})
