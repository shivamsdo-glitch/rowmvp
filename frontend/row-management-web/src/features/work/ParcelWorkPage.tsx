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
  /** The parcel's own tab, which does the adding, changing and removing under that page's permissions. */
  tab: (parcel: Parcel, editable: boolean) => ReactNode
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
  const [parcelId, setParcelId] = useState<string | null>(null)
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
      <Records kind={kind} term={term} onOpen={setParcelId} />
      <Parcels term={term} onOpen={setParcelId} />
      {parcelId && <SelectedParcel key={parcelId} parcelId={parcelId} kind={kind} onClose={() => setParcelId(null)} />}
    </>
  )
}

function Records({ kind, term, onOpen }: { kind: WorkKind; term: string; onOpen: (parcelId: string) => void }) {
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
              <td className="px-3 py-2"><button type="button" aria-label={`Open ${r.number}`} className={buttonClassName('outline', 'sm')} onClick={() => onOpen(r.parcelId)}>Open</button></td>
            </tr>
          ))}
        </DataTable>
      )}
    </section>
  )
}

function Parcels({ term, onOpen }: { term: string; onOpen: (parcelId: string) => void }) {
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
              <td className="px-3 py-2"><button type="button" aria-label={`Open parcel ${p.number}`} className={buttonClassName('outline', 'sm')} onClick={() => onOpen(p.id)}>Open</button></td>
            </tr>
          ))}
        </DataTable>
      )}
    </section>
  )
}

function SelectedParcel({ parcelId, kind, onClose }: { parcelId: string; kind: WorkKind; onClose: () => void }) {
  const { manager } = useAuth()
  const api = parcelsApi(manager.api)
  const q = useQuery({ queryKey: ['parcel', parcelId], queryFn: () => api.get(parcelId) })
  return (
    <section aria-label="Selected parcel" className="mb-6 rounded-lg border bg-card p-4">
      {q.isPending ? <LoadingRows rows={3} /> : q.isError ? <ErrorState title="The parcel could not be loaded" description={q.error.message} retry={() => void q.refetch()} /> : (
        <>
          <div className="mb-4 flex items-start justify-between gap-3">
            <div>
              <h2 className="text-base font-semibold">Parcel {q.data.number}</h2>
              <p className="text-sm text-muted-foreground">
                {[q.data.ownerName, q.data.address].filter(Boolean).join(' · ') || 'No owner or address recorded'}
                {' · '}
                <Link to="/parcels/$parcelId" params={{ parcelId }} className="underline">Open the whole parcel</Link>
              </p>
            </div>
            <button type="button" aria-label="Close parcel" className={buttonClassName('ghost', 'sm')} onClick={onClose}><X size={16} aria-hidden /></button>
          </div>
          {kind.tab(q.data, q.data.state === 'Active')}
        </>
      )}
    </section>
  )
}
