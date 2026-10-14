import type { ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import type { DeadlineStatus, ParcelState } from '@/api/parcels'
import { projectsApi } from '@/api/projects'
import { useAuth } from '@/auth/auth-context'
import { Pill } from '../projects/form-kit'

/** A parcel status as its colour swatch and name (ROW-005, 235). The text carries the meaning; the colour is a cue. */
export function StatusChip({ name, color }: { name: string; color: string }) {
  return (
    <span className="inline-flex items-center gap-1.5 text-sm">
      <span aria-hidden className="inline-block h-2.5 w-2.5 rounded-full border" style={{ backgroundColor: color }} />
      {name}
    </span>
  )
}

export const DEADLINE_LABEL: Record<DeadlineStatus, string> = {
  NotStarted: 'Not started', Pending: 'Pending', DueSoon: 'Due soon', Overdue: 'Overdue', Met: 'Met', MetLate: 'Met late',
}

export function DeadlinePill({ status }: { status: DeadlineStatus }) {
  const tone = status === 'Overdue' || status === 'MetLate' || status === 'DueSoon' ? 'warn' : status === 'Met' ? 'ok' : 'neutral'
  return <Pill tone={tone}>{DEADLINE_LABEL[status]}</Pill>
}

export function StateNote({ state }: { state: ParcelState }): ReactNode {
  if (state === 'Active') return null
  return <Pill tone="neutral">{state === 'Split' ? 'Split — superseded' : 'Combined — superseded'}</Pill>
}

/** A zone's everyday name ("Eastern Daylight Time"), falling back to its id. */
export function zoneName(timeZoneId: string): string {
  try {
    return new Intl.DateTimeFormat('en-US', { timeZone: timeZoneId, timeZoneName: 'long' }).formatToParts(new Date()).find((p) => p.type === 'timeZoneName')?.value ?? timeZoneId
  } catch {
    return timeZoneId
  }
}

export const acres = (sqFt: number) => (sqFt / 43560).toLocaleString('en-US', { maximumFractionDigits: 3 })
export const sqFtText = (sqFt: number) => `${sqFt.toLocaleString('en-US', { maximumFractionDigits: 2 })} sq ft`

/**
 * The projects a parcel screen can pick from. The list is the first page of projects; the selected project is always
 * included, even when it is not on that page (for example after following a link from a project).
 */
export function useProjectOptions(selectedId: string, enabled = true) {
  const { manager } = useAuth()
  const api = projectsApi(manager.api)
  const page = useQuery({ queryKey: ['projects', 'options'], queryFn: () => api.list({ pageSize: 100 }), enabled })
  const missing = !!selectedId && !!page.data && !page.data.items.some((p) => p.id === selectedId)
  const selected = useQuery({ queryKey: ['project', selectedId], queryFn: () => api.get(selectedId), enabled: enabled && missing })
  const items = [...(page.data?.items ?? []).map((p) => ({ id: p.id, number: p.number, name: p.name })), ...(selected.data ? [{ id: selected.data.id, number: selected.data.number, name: selected.data.name }] : [])]
  return items.map((p) => ({ value: p.id, label: `${p.number} · ${p.name}` }))
}
