import { useQuery } from '@tanstack/react-query'
import { parcelRecordsApi } from '@/api/parcel-records'
import { useAuth } from '@/auth/auth-context'

/**
 * The names of the people on a parcel's team, for the "Assigned to" selects on the other tabs (ADR 0023). The team is the one place people
 * are assigned to a parcel; it shares its query with the Team tab, so adding someone there updates these lists.
 */
export function useTeamNames(parcelId: string): string[] {
  const { manager } = useAuth()
  const team = useQuery({ queryKey: ['parcel-records', parcelId, 'team'], queryFn: () => parcelRecordsApi(manager.api).team(parcelId), retry: false })
  return [...new Set((team.data ?? []).map((m) => m.name))]
}
