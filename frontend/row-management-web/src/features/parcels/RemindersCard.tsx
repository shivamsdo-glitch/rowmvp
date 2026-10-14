import { Link } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { parcelsApi } from '@/api/parcels'
import { useAuth } from '@/auth/auth-context'
import { ErrorState, LoadingRows } from '@/components/ui/states'
import { dateText, Pill } from '../projects/form-kit'

/** The dashboard's "next actions": parcel deadlines that are overdue or due within a week (ROW-074). Nothing is emailed. */
export function RemindersCard() {
  const { manager } = useAuth()
  const list = useQuery({ queryKey: ['reminders'], queryFn: () => parcelsApi(manager.api).reminders(7), enabled: manager.can('parcels') })
  if (!manager.can('parcels')) return null

  return (
    <section aria-label="Next actions" className="mt-6 max-w-3xl rounded-lg border bg-card p-4">
      <h2 className="text-sm font-semibold">Next actions</h2>
      <p className="mb-3 text-xs text-muted-foreground">Parcel deadlines that are overdue or due within 7 days.</p>
      {list.isPending ? <LoadingRows rows={2} /> : list.isError ? (
        <ErrorState title="Reminders could not be loaded" retry={() => void list.refetch()} />
      ) : list.data.length === 0 ? (
        <p className="text-sm text-muted-foreground">Nothing is due. You are all caught up.</p>
      ) : (
        <ul className="divide-y text-sm">
          {list.data.map((r) => (
            <li key={`${r.parcelId}-${r.ruleName}`} className="flex flex-wrap items-center justify-between gap-2 py-2">
              <span>
                <Link to="/parcels/$parcelId" params={{ parcelId: r.parcelId }} className="font-medium text-primary hover:underline">
                  {r.projectNumber} · Parcel {r.parcelNumber}
                </Link>
                {' — '}{r.targetMilestone} due {dateText(r.dueOn)}
              </span>
              <span>
                <Pill tone="warn">{r.status === 'Overdue' ? `Overdue by ${-r.daysRemaining} day${r.daysRemaining === -1 ? '' : 's'}` : r.daysRemaining === 0 ? 'Due today' : `Due in ${r.daysRemaining} day${r.daysRemaining === 1 ? '' : 's'}`}</Pill>
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
