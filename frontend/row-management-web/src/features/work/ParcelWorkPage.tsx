import { useEffect, useState, type ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { X } from 'lucide-react'
import type { OverviewBase } from '@/api/overview'
import { parcelsApi, type Parcel } from '@/api/parcels'
import type { PageKey } from '@/api/auth'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { PageHeader } from '@/components/ui/page-header'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { DataTable, TextField } from '../projects/form-kit'

/** One kind of record a work screen can search and edit (an appraisal, a negotiation...): how to search it, how to list it and which parcel tab edits it. */
export interface WorkKind<R extends OverviewBase = OverviewBase> {
  key: string
  label: string
  /** The page whose access governs this kind (the parcel tab's page). */
  page: PageKey
  fetch: (search: string) => Promise<R[]>
  headers: string[]
  cells: (r: R) => ReactNode[]
  /** True when the rows are records of a list tab (appraisals, easements...): Open shows that one record on its own page; otherwise Open shows the tab. */
  opensRecord?: boolean
}

const useDebounced = (value: string, ms = 250) => {
  const [v, setV] = useState(value)
  useEffect(() => {
    const t = setTimeout(() => setV(value), ms)
    return () => clearTimeout(t)
  }, [value, ms])
  return v
}

/**
 * A work screen across parcels (Phase 1.5, ADR 0021). Search by parcel number, address, owner or record ID, open the record's parcel, and add or
 * update the record right there; the same parcel tab and the same permissions as inside the parcel.
 */
export function ParcelWorkPage({ title, subtitle, kinds }: { title: string; subtitle: string; kinds: WorkKind[] }) {
  const { manager } = useAuth()
  const visible = kinds.filter((k) => manager.can(k.page))
  const [kindKey, setKindKey] = useState(kinds[0].key)
  const [text, setText] = useState('')
  const term = useDebounced(text.trim())
  const kind = visible.find((k) => k.key === kindKey) ?? visible[0]

  if (!kind) {
    return (
      <>
        <PageHeader title={title} subtitle={subtitle} />
        <p className="text-sm text-muted-foreground">Your role cannot see these records.</p>
      </>
    )
  }

  return (
    <>
      <PageHeader title={title} subtitle={subtitle} />
      {visible.length > 1 && (
        <Tabs>
          <TabsList role="tablist" aria-label="Record types">
            {visible.map((k) => (
              <TabsTrigger key={k.key} role="tab" aria-selected={k.key === kind.key} active={k.key === kind.key} onClick={() => setKindKey(k.key)}>{k.label}</TabsTrigger>
            ))}
          </TabsList>
        </Tabs>
      )}
      <div className="my-4 max-w-md">
        <TextField label="Search" value={text} onChange={setText} maxLength={100} hint="Parcel number, owner name, address or record ID" />
      </div>
      <Records kind={kind} term={term} />
      <Parcels kind={kind} term={term} />
    </>
  )
}

function Records({ kind, term }: { kind: WorkKind; term: string }) {
  const q = useQuery({ queryKey: ['work', kind.key, term], queryFn: () => kind.fetch(term) })
  return (
    <section aria-label="Matching records" className="mb-6">
      <h2 className="mb-2 text-sm font-semibold">{term ? `${kind.label} matching "${term}"` : `Recent ${kind.label.toLowerCase()}`}</h2>
      {q.isPending ? <LoadingRows rows={3} /> : q.isError ? <ErrorState title="The search failed" description={q.error.message} retry={() => void q.refetch()} /> : q.data.length === 0 ? (
        <EmptyState title="No records found" description={term ? 'Try the parcel number, the owner name or the record ID. To start a new record, open its parcel below.' : 'Nothing has been recorded yet. Search for a parcel to start one.'} />
      ) : (
        <DataTable caption={`${kind.label} found`} headers={['Record', 'Parcel', 'Owner', ...kind.headers, 'Actions']}>
          {q.data.map((r) => (
            <tr key={r.id}>
              <td className="px-3 py-2 font-medium">{r.number}</td>
              <td className="px-3 py-2">{r.parcelNumber}{r.address ? <span className="block text-xs text-muted-foreground">{r.address}</span> : null}</td>
              <td className="px-3 py-2">{r.ownerName ?? '—'}</td>
              {kind.cells(r).map((c, i) => <td key={i} className="px-3 py-2">{c}</td>)}
              <td className="px-3 py-2">
                {/* The record opens on its own page, inside its tab of the parcel (ADR 0028), never as a panel under this table. */}
                <Link to="/parcels/$parcelId" params={{ parcelId: r.parcelId }} search={{ tab: kind.page, ...(kind.opensRecord ? { record: r.id } : {}) }}
                  aria-label={`Open ${r.number}`} className={buttonClassName('outline', 'sm')}>Open</Link>
              </td>
            </tr>
          ))}
        </DataTable>
      )}
    </section>
  )
}

function Parcels({ kind, term }: { kind: WorkKind; term: string }) {
  const { manager } = useAuth()
  const api = parcelsApi(manager.api)
  const q = useQuery({ queryKey: ['work-parcels', term], queryFn: () => api.list({ search: term, pageSize: 10 }), enabled: !!term && manager.can('parcels') })
  if (!term || !manager.can('parcels')) return null
  return (
    <section aria-label="Matching parcels" className="mb-6">
      <h2 className="mb-2 text-sm font-semibold">Parcels matching "{term}"</h2>
      {q.isPending ? <LoadingRows rows={2} /> : q.isError ? <ErrorState title="The parcel search failed" description={q.error.message} retry={() => void q.refetch()} /> : q.data.items.length === 0 ? (
        <p className="text-sm text-muted-foreground">No parcels match.</p>
      ) : (
        <DataTable caption="Parcels found" headers={['Parcel', 'Address', 'Status', 'Actions']}>
          {q.data.items.map((p) => (
            <tr key={p.id}>
              <td className="px-3 py-2 font-medium">{p.number}</td>
              <td className="px-3 py-2">{p.address ?? '—'}</td>
              <td className="px-3 py-2">{p.statusName}</td>
              <td className="px-3 py-2"><Link to="/parcels/$parcelId" params={{ parcelId: p.id }} search={{ tab: kind.page }} aria-label={`Open parcel ${p.number}`} className={buttonClassName('outline', 'sm')}>Open</Link></td>
            </tr>
          ))}
        </DataTable>
      )}
    </section>
  )
}
