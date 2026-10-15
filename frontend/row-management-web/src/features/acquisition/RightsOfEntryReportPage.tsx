import { useState } from 'react'
import { Link } from '@tanstack/react-router'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Printer } from 'lucide-react'
import { ROE_STATUSES, acquisitionApi } from '@/api/acquisition'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { PageHeader } from '@/components/ui/page-header'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { roeTone } from '../parcels/AcquisitionTabs'
import { DataTable, dateText, Pill, SelectField } from '../projects/form-kit'

const PAGE_SIZE = 25
const STATUS_OPTIONS = [...ROE_STATUSES, 'Expired'].map((s) => ({ value: s, label: s }))
const SOON_OPTIONS = [{ value: '7', label: 'within 7 days' }, { value: '30', label: 'within 30 days' }, { value: '90', label: 'within 90 days' }]

/** Every right of entry on a live parcel, with who gave it and when it ends (ROW-077). */
export function RightsOfEntryReportPage() {
  const { manager } = useAuth()
  const api = acquisitionApi(manager.api)
  const [status, setStatus] = useState('')
  const [soon, setSoon] = useState('')
  const [page, setPage] = useState(1)
  const filter = { status, expiringWithinDays: soon ? Number(soon) : null, page, pageSize: PAGE_SIZE }
  const list = useQuery({ queryKey: ['roe-report', filter], queryFn: () => api.roeReport(filter), placeholderData: keepPreviousData })
  const totalPages = list.data ? Math.max(1, Math.ceil(list.data.total / PAGE_SIZE)) : 1
  const filtered = !!(status || soon)

  return (
    <>
      <PageHeader
        title="Rights of entry"
        subtitle="Permission to enter parcels before they are acquired, across every project."
        actions={<button type="button" className={buttonClassName('outline')} onClick={() => window.print()}><Printer size={14} aria-hidden /> Print</button>}
      />
      <div className="mb-4 flex flex-wrap items-end gap-3">
        <SelectField label="Status" value={status} onChange={(v) => { setStatus(v); setPage(1) }} options={STATUS_OPTIONS} blankLabel="Any status" />
        <SelectField label="Ending" value={soon} onChange={(v) => { setSoon(v); setPage(1) }} options={SOON_OPTIONS} blankLabel="Any time" />
      </div>

      {list.isPending ? <LoadingRows rows={5} /> : list.isError ? (
        <ErrorState title="Rights of entry could not be loaded" description={list.error.message} retry={() => void list.refetch()} />
      ) : list.data.items.length === 0 ? (
        <EmptyState title={filtered ? 'No rights of entry match' : 'No rights of entry yet'} description={filtered ? 'Try a different status or time frame.' : 'Record one on a parcel’s Right of entry tab.'} />
      ) : (
        <>
          <DataTable caption="Rights of entry" headers={['Project', 'Parcel', 'Status', 'Requested', 'Granted', 'Ends', 'Given by', 'Purpose']}>
            {list.data.items.map((r) => (
              <tr key={r.id}>
                <td className="px-3 py-2">{r.projectNumber}</td>
                <td className="px-3 py-2">
                  <Link to="/parcels/$parcelId" params={{ parcelId: r.parcelId }} className="font-medium text-primary hover:underline">{r.parcelNumber}</Link>
                  {r.parcelAddress ? <span className="block text-xs text-muted-foreground">{r.parcelAddress}</span> : null}
                </td>
                <td className="px-3 py-2"><Pill tone={roeTone(r.effectiveStatus)}>{r.effectiveStatus}</Pill></td>
                <td className="px-3 py-2">{dateText(r.requestedOn)}</td>
                <td className="px-3 py-2">{dateText(r.grantedOn)}</td>
                <td className="px-3 py-2">
                  {dateText(r.expiresOn)}
                  {r.daysUntilExpiry !== null && r.daysUntilExpiry >= 0 && r.daysUntilExpiry <= 30 && (
                    <span className="ml-1 text-xs text-muted-foreground">({r.daysUntilExpiry === 0 ? 'today' : `in ${r.daysUntilExpiry} day${r.daysUntilExpiry === 1 ? '' : 's'}`})</span>
                  )}
                </td>
                <td className="px-3 py-2">{r.grantedBy ?? '—'}</td>
                <td className="px-3 py-2">{r.purpose ?? '—'}</td>
              </tr>
            ))}
          </DataTable>
          <nav aria-label="Pages" className="mt-3 flex items-center justify-between text-sm">
            <span className="text-muted-foreground">{list.data.total} right{list.data.total === 1 ? '' : 's'} of entry</span>
            <span className="flex items-center gap-2">
              <button type="button" className={buttonClassName('outline', 'sm')} disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
              <span aria-live="polite">Page {page} of {totalPages}</span>
              <button type="button" className={buttonClassName('outline', 'sm')} disabled={page >= totalPages} onClick={() => setPage(page + 1)}>Next</button>
            </span>
          </nav>
        </>
      )}
    </>
  )
}
