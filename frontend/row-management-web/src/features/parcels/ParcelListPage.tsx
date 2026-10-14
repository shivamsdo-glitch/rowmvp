import { useEffect, useState, type FormEvent } from 'react'
import { Link, useNavigate } from '@tanstack/react-router'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { LandPlot } from 'lucide-react'
import { ACQUISITION_LABEL, parcelsApi, type ParcelInput, type ParcelSummary } from '@/api/parcels'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { PageHeader } from '@/components/ui/page-header'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { DataTable, FormError, messageOf, SelectField, TextField } from '../projects/form-kit'
import { ParcelForm } from './ParcelForm'
import { acres, StateNote, StatusChip, useProjectOptions } from './parcel-kit'

function useDebounced<T>(value: T, ms = 300): T {
  const [v, setV] = useState(value)
  useEffect(() => {
    const t = setTimeout(() => setV(value), ms)
    return () => clearTimeout(t)
  }, [value, ms])
  return v
}

const PAGE_SIZE = 25
const BOARD_SIZE = 200

/** Parcels: find, open, create, update in bulk, combine, and see them as a status-coloured board (ROW-005, 233, 235, 236). */
export function ParcelListPage({ initialProjectId = '' }: { initialProjectId?: string } = {}) {
  const { manager } = useAuth()
  const api = parcelsApi(manager.api)
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const canCreate = manager.can('parcels', 'create')
  const canUpdate = manager.can('parcels', 'update')

  const [search, setSearch] = useState('')
  const [projectId, setProjectId] = useState(initialProjectId)
  const [statusId, setStatusId] = useState('')
  const [groupId, setGroupId] = useState('')
  const [view, setView] = useState<'table' | 'board'>('table')
  const [page, setPage] = useState(1)
  const [creating, setCreating] = useState(false)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [notice, setNotice] = useState<string | null>(null)
  const selectable = canUpdate || canCreate

  const filter = { search: useDebounced(search), projectId, statusId, groupId, page, pageSize: view === 'board' ? BOARD_SIZE : PAGE_SIZE }
  const list = useQuery({ queryKey: ['parcels', filter], queryFn: () => api.list(filter), placeholderData: keepPreviousData })
  const projectOptions = useProjectOptions(projectId)
  const statuses = useQuery({ queryKey: ['parcel-statuses'], queryFn: () => api.statuses() })
  const groups = useQuery({ queryKey: ['groups', projectId], queryFn: () => api.groups(projectId), enabled: !!projectId })

  const create = useMutation({
    mutationFn: (input: ParcelInput) => api.create(input),
    onSuccess: async (parcel) => {
      await queryClient.invalidateQueries({ queryKey: ['parcels'] })
      await navigate({ to: '/parcels/$parcelId', params: { parcelId: parcel.id } })
    },
  })

  const reset = <T,>(set: (v: T) => void) => (v: T) => { set(v); setPage(1); setSelected(new Set()); setNotice(null) }
  const filtered = !!(filter.search || projectId || statusId || groupId)
  const items = list.data?.items ?? []
  const totalPages = list.data ? Math.max(1, Math.ceil(list.data.total / PAGE_SIZE)) : 1
  const chosen = items.filter((p) => selected.has(p.id))

  const toggle = (id: string) => setSelected((s) => { const n = new Set(s); if (n.has(id)) n.delete(id); else n.add(id); return n })
  const toggleAll = () => setSelected((s) => (items.every((p) => s.has(p.id)) ? new Set() : new Set(items.map((p) => p.id))))

  return (
    <>
      <PageHeader
        title="Parcels"
        subtitle="Every take of land across your projects: where it stands and what is due."
        actions={canCreate && (
          <button type="button" className={buttonClassName('default')} onClick={() => setCreating((c) => !c)}>New parcel</button>
        )}
      />

      {creating && (
        <section aria-label="Create a parcel" className="mb-6 rounded-lg border bg-card p-4">
          <h2 className="mb-3 text-sm font-semibold">New parcel</h2>
          <ParcelForm
            defaultProjectId={projectId || undefined}
            submitLabel="Create parcel"
            onCancel={() => setCreating(false)}
            onSubmit={async (input) => { await create.mutateAsync(input) }}
          />
        </section>
      )}

      <form role="search" aria-label="Filter parcels" className="mb-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-4" onSubmit={(e) => e.preventDefault()}>
        <TextField label="Search parcels" value={search} onChange={reset(setSearch)} placeholder="Number or address" />
        <SelectField label="Project" value={projectId} onChange={(v) => { reset(setProjectId)(v); setGroupId('') }}
          options={projectOptions} blankLabel="All projects" />
        <SelectField label="Status" value={statusId} onChange={reset(setStatusId)}
          options={(statuses.data ?? []).map((s) => ({ value: s.id, label: s.name }))} blankLabel="All statuses" />
        <SelectField label="Group" value={groupId} onChange={reset(setGroupId)} disabled={!projectId}
          options={(groups.data ?? []).map((g) => ({ value: g.id, label: g.name }))} blankLabel={projectId ? 'All groups' : 'Choose a project first'} />
      </form>

      <div role="group" aria-label="View" className="mb-3 flex gap-2">
        <button type="button" aria-pressed={view === 'table'} className={buttonClassName(view === 'table' ? 'secondary' : 'outline', 'sm')} onClick={() => { setView('table'); setPage(1) }}>Table</button>
        <button type="button" aria-pressed={view === 'board'} className={buttonClassName(view === 'board' ? 'secondary' : 'outline', 'sm')} onClick={() => { setView('board'); setPage(1) }}>Board</button>
      </div>

      {notice && <p role="status" className="mb-3 text-sm">{notice}</p>}
      {selectable && chosen.length > 0 && (
        <SelectionBar
          chosen={chosen}
          canUpdate={canUpdate}
          canCombine={canCreate}
          onDone={async (message) => { setSelected(new Set()); setNotice(message); await queryClient.invalidateQueries({ queryKey: ['parcels'] }) }}
          onCombined={async (id) => { await queryClient.invalidateQueries({ queryKey: ['parcels'] }); await navigate({ to: '/parcels/$parcelId', params: { parcelId: id } }) }}
        />
      )}

      {list.isPending ? (
        <LoadingRows />
      ) : list.isError ? (
        <ErrorState title="Parcels could not be loaded" description={list.error.message} retry={() => void list.refetch()} />
      ) : items.length === 0 ? (
        <EmptyState
          icon={LandPlot}
          title={filtered ? 'No parcels match your filters' : 'No parcels yet'}
          description={filtered ? 'Try a different search or clear a filter.' : canCreate ? 'Create the first parcel to get started.' : 'Parcels will appear here once they are created.'}
        />
      ) : view === 'board' ? (
        <Board items={items} statusOrder={(statuses.data ?? []).map((s) => ({ id: s.id, name: s.name, color: s.color }))} />
      ) : (
        <>
          <DataTable caption="Parcels" headers={[...(selectable ? ['Select'] : []), 'Parcel', 'Project', 'Address', 'Area', 'Status', 'Group', 'Acquisition']}>
            {items.map((p) => (
              <tr key={p.id} className="hover:bg-muted/50">
                {selectable && (
                  <td className="px-3 py-2">
                    <input type="checkbox" aria-label={`Select parcel ${p.number}`} checked={selected.has(p.id)} onChange={() => toggle(p.id)} className="h-4 w-4" />
                  </td>
                )}
                <td className="px-3 py-2 font-medium">
                  <Link to="/parcels/$parcelId" params={{ parcelId: p.id }} className="text-primary hover:underline">{p.number}</Link>
                  {p.isRemnant && <span className="ml-1 text-xs text-muted-foreground">(remnant)</span>}{' '}
                  <StateNote state={p.state} />
                </td>
                <td className="px-3 py-2">{p.projectNumber}</td>
                <td className="px-3 py-2">{p.address ?? '—'}</td>
                <td className="px-3 py-2">{acres(p.areaSqFt)} ac</td>
                <td className="px-3 py-2"><StatusChip name={p.statusName} color={p.statusColor} /></td>
                <td className="px-3 py-2">{p.groupName ?? '—'}</td>
                <td className="px-3 py-2">{ACQUISITION_LABEL[p.acquisitionType]}</td>
              </tr>
            ))}
          </DataTable>
          {selectable && (
            <div className="mt-2 text-xs">
              <button type="button" className="text-primary hover:underline" onClick={toggleAll}>
                {items.every((p) => selected.has(p.id)) ? 'Clear selection' : 'Select all on this page'}
              </button>
            </div>
          )}
          <nav aria-label="Pages" className="mt-3 flex items-center justify-between text-sm">
            <span>{list.data.total} parcel{list.data.total === 1 ? '' : 's'}</span>
            <span className="flex items-center gap-2">
              <button type="button" className={buttonClassName('outline', 'sm')} disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>Previous</button>
              <span aria-live="polite">Page {page} of {totalPages}</span>
              <button type="button" className={buttonClassName('outline', 'sm')} disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>Next</button>
            </span>
          </nav>
        </>
      )}
    </>
  )
}

/** Parcels as columns by status, each coloured from the agency's status list (ROW-235). */
function Board({ items, statusOrder }: { items: ParcelSummary[]; statusOrder: { id: string; name: string; color: string }[] }) {
  const known = new Set(statusOrder.map((s) => s.id))
  const columns = [
    ...statusOrder,
    // A parcel can sit in a status that has since been removed from the list; it still gets a column.
    ...[...new Map(items.filter((p) => !known.has(p.statusId)).map((p) => [p.statusId, { id: p.statusId, name: p.statusName, color: p.statusColor }])).values()],
  ].filter((c) => items.some((p) => p.statusId === c.id))

  return (
    <div role="region" aria-label="Parcel board" className="flex gap-3 overflow-x-auto pb-2">
      {columns.map((c) => {
        const cards = items.filter((p) => p.statusId === c.id)
        return (
          <section key={c.id} aria-label={`${c.name} column`} className="w-56 shrink-0 rounded-lg border bg-muted/30">
            <h2 className="flex items-center justify-between rounded-t-lg border-b px-3 py-2 text-sm font-semibold" style={{ borderTop: `4px solid ${c.color}` }}>
              <span>{c.name}</span><span className="text-xs text-muted-foreground">{cards.length}</span>
            </h2>
            <ul className="space-y-2 p-2">
              {cards.map((p) => (
                <li key={p.id} className="rounded-md border bg-card p-2 text-sm">
                  <Link to="/parcels/$parcelId" params={{ parcelId: p.id }} className="font-medium text-primary hover:underline">{p.projectNumber} · {p.number}</Link>
                  <p className="text-xs text-muted-foreground">{p.address ?? 'No address'} · {acres(p.areaSqFt)} ac</p>
                </li>
              ))}
            </ul>
          </section>
        )
      })}
    </div>
  )
}

/** Acts on the ticked parcels: set status/group for all at once (ROW-236), or combine them into one (ROW-007). */
function SelectionBar({
  chosen, canUpdate, canCombine, onDone, onCombined,
}: {
  chosen: ParcelSummary[]
  canUpdate: boolean
  canCombine: boolean
  onDone: (message: string) => Promise<void>
  onCombined: (parcelId: string) => Promise<void>
}) {
  const { manager } = useAuth()
  const api = parcelsApi(manager.api)
  const [statusId, setStatusId] = useState('')
  const [groupId, setGroupId] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const statuses = useQuery({ queryKey: ['parcel-statuses'], queryFn: () => api.statuses() })
  const oneProject = new Set(chosen.map((p) => p.projectId)).size === 1
  const projectId = oneProject ? chosen[0].projectId : ''
  const groups = useQuery({ queryKey: ['groups', projectId], queryFn: () => api.groups(projectId), enabled: !!projectId })

  async function apply(e: FormEvent) {
    e.preventDefault()
    setError(null)
    if (!statusId && !groupId) { setError('Choose a status or a group to apply.'); return }
    setBusy(true)
    try {
      const r = await api.batchUpdate(chosen.map((p) => p.id), statusId || null, groupId && groupId !== '__none' ? groupId : null, groupId === '__none')
      await onDone(`Updated ${r.updated} parcel${r.updated === 1 ? '' : 's'}.`)
    } catch (err) {
      setError(messageOf(err))
    } finally {
      setBusy(false)
    }
  }

  async function combine() {
    setError(null)
    setBusy(true)
    try {
      const merged = await api.combine(chosen.map((p) => p.id), null)
      await onCombined(merged.id)
    } catch (err) {
      setError(messageOf(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section aria-label="Selected parcels" className="mb-4 rounded-lg border bg-card p-3">
      <p className="mb-2 text-sm font-medium" aria-live="polite">{chosen.length} parcel{chosen.length === 1 ? '' : 's'} selected</p>
      <div className="flex flex-wrap items-end gap-3">
        {canUpdate && (
          <form onSubmit={apply} aria-label="Update selected parcels" className="flex flex-wrap items-end gap-3">
            <SelectField label="Set status" value={statusId} onChange={setStatusId} options={(statuses.data ?? []).filter((s) => s.isActive).map((s) => ({ value: s.id, label: s.name }))} blankLabel="No change" />
            <SelectField label="Set group" value={groupId} onChange={setGroupId} disabled={!oneProject}
              options={[...(groups.data ?? []).map((g) => ({ value: g.id, label: g.name })), { value: '__none', label: 'Remove from group' }]}
              blankLabel={oneProject ? 'No change' : 'Select one project to set groups'} />
            <button type="submit" disabled={busy} className={buttonClassName('default')}>Apply to selected</button>
          </form>
        )}
        {canCombine && (
          <button type="button" disabled={busy || chosen.length < 2} onClick={() => void combine()} className={buttonClassName('outline')}>
            Combine selected
          </button>
        )}
      </div>
      <FormError error={error} />
    </section>
  )
}
