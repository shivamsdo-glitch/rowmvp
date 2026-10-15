import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { acquisitionApi } from '@/api/acquisition'
import { useAuth } from '@/auth/auth-context'
import { ErrorState, LoadingRows } from '@/components/ui/states'
import { FormError, messageOf } from '../projects/form-kit'

/** Administrators decide whether a parcel must have an approved engineer and surveyor plan review to leave Title & survey (ROW-239). */
export function PlanReviewGateCard() {
  const { manager } = useAuth()
  const api = acquisitionApi(manager.api)
  const queryClient = useQueryClient()
  const canEdit = manager.can('administration', 'update')
  const gate = useQuery({ queryKey: ['plan-review-gate'], queryFn: () => api.gate() })
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function set(enabled: boolean) {
    setError(null); setBusy(true)
    try {
      await api.setGate(enabled)
      await queryClient.invalidateQueries({ queryKey: ['plan-review-gate'] })
      await queryClient.invalidateQueries({ queryKey: ['parcel-acquisition'] })
    } catch (e) {
      setError(messageOf(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section aria-label="Plan review gate" className="mt-6 max-w-2xl rounded-lg border bg-card p-4">
      <h2 className="text-sm font-semibold">Plan review gate</h2>
      <p className="mb-3 text-xs text-muted-foreground">
        When on, a parcel cannot leave the Title &amp; survey step until an engineer and a surveyor have each approved the plans. An
        override cannot get past it. When off, plan reviews are only kept for the file.
      </p>
      {gate.isPending ? <LoadingRows rows={1} /> : gate.isError ? (
        <ErrorState title="The setting could not be loaded" retry={() => void gate.refetch()} />
      ) : (
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox" className="h-4 w-4" checked={gate.data.enabled} disabled={!canEdit || busy}
            onChange={(e) => void set(e.target.checked)}
          />
          Require engineer and surveyor plan reviews
        </label>
      )}
      <FormError error={error} />
    </section>
  )
}
