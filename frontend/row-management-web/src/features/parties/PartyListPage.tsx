import { useEffect, useState, type FormEvent } from 'react'
import { Link, useNavigate } from '@tanstack/react-router'
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query'
import { Contact } from 'lucide-react'
import { PARTY_KINDS, partiesApi, type PartyKind } from '@/api/parties'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { PageHeader } from '@/components/ui/page-header'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { DataTable, FormError, Pill, SelectField, TextField, fieldErrorsOf, messageOf } from '../projects/form-kit'
import { KIND_LABEL, PartyFields, inputOf, usePartyDraft } from './party-kit'

function useDebounced<T>(value: T, ms = 300): T {
  const [v, setV] = useState(value)
  useEffect(() => {
    const t = setTimeout(() => setV(value), ms)
    return () => clearTimeout(t)
  }, [value, ms])
  return v
}

const PAGE_SIZE = 25
const KIND_OPTIONS = PARTY_KINDS.map((k) => ({ value: k, label: KIND_LABEL[k] }))

/** The people and organizations the agency deals with: owners, lienholders, tenants, attorneys, abutters (ROW-014..018). */
export function PartyListPage() {
  const { manager } = useAuth()
  const api = partiesApi(manager.api)
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const canCreate = manager.can('parties', 'create')
  const [search, setSearch] = useState('')
  const [kind, setKind] = useState<PartyKind | ''>('')
  const [page, setPage] = useState(1)
  const [creating, setCreating] = useState(false)

  const filter = { search: useDebounced(search), kind, page, pageSize: PAGE_SIZE }
  const list = useQuery({ queryKey: ['parties', filter], queryFn: () => api.list(filter), placeholderData: keepPreviousData })
  const filtered = !!(filter.search || kind)
  const totalPages = list.data ? Math.max(1, Math.ceil(list.data.total / PAGE_SIZE)) : 1

  return (
    <>
      <PageHeader
        title="Parties"
        subtitle="Owners, interest holders, contacts, tenants and abutters, entered once and linked to any tract or parcel."
        actions={canCreate && (
          <button type="button" className={buttonClassName('default')} onClick={() => setCreating(!creating)}>New party</button>
        )}
      />

      {creating && canCreate && (
        <NewPartyPanel
          onCancel={() => setCreating(false)}
          onCreated={async (id) => {
            await queryClient.invalidateQueries({ queryKey: ['parties'] })
            await navigate({ to: '/parties/$partyId', params: { partyId: id } })
          }}
        />
      )}

      <form role="search" aria-label="Filter parties" className="mb-4 grid gap-3 sm:grid-cols-2" onSubmit={(e) => e.preventDefault()}>
        <TextField label="Search parties" value={search} onChange={(v) => { setSearch(v); setPage(1) }} placeholder="Name, city, phone or email" />
        <SelectField label="Type" value={kind} onChange={(v) => { setKind(v); setPage(1) }} options={KIND_OPTIONS} blankLabel="All" />
      </form>

      {list.isPending ? (
        <LoadingRows />
      ) : list.isError ? (
        <ErrorState title="Parties could not be loaded" description={list.error.message} retry={() => void list.refetch()} />
      ) : list.data.items.length === 0 ? (
        <EmptyState
          icon={Contact}
          title={filtered ? 'No parties match your search' : 'No parties yet'}
          description={filtered ? 'Try a different search or clear a filter.' : canCreate ? 'Add the first owner, contact or tenant.' : 'People and organizations will appear here once they are added.'}
        />
      ) : (
        <>
          <DataTable caption="Parties" headers={['Name', 'Type', 'City', 'Phone', 'Email', 'Used on', 'Tax ID']}>
            {list.data.items.map((p) => (
              <tr key={p.id} className="hover:bg-muted/50">
                <td className="px-3 py-2 font-medium">
                  <Link to="/parties/$partyId" params={{ partyId: p.id }} className="text-primary hover:underline">{p.name}</Link>
                </td>
                <td className="px-3 py-2">{KIND_LABEL[p.kind]}</td>
                <td className="px-3 py-2">{[p.city, p.state].filter(Boolean).join(', ') || '—'}</td>
                <td className="px-3 py-2">{p.phone ?? '—'}</td>
                <td className="px-3 py-2">{p.email ?? '—'}</td>
                <td className="px-3 py-2">{p.linkCount} {p.linkCount === 1 ? 'record' : 'records'}</td>
                <td className="px-3 py-2">{p.hasTaxId ? <Pill tone="ok">On file</Pill> : <Pill tone="neutral">None</Pill>}</td>
              </tr>
            ))}
          </DataTable>
          <nav aria-label="Pages" className="mt-3 flex items-center justify-between text-sm">
            <span>{list.data.total} part{list.data.total === 1 ? 'y' : 'ies'}</span>
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

function NewPartyPanel({ onCancel, onCreated }: { onCancel: () => void; onCreated: (id: string) => Promise<void> }) {
  const { manager } = useAuth()
  const api = partiesApi(manager.api)
  const { draft, set } = usePartyDraft()
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setFields({})
    if (!draft.name.trim()) { setFields({ name: 'Name is required.' }); return }
    setBusy(true)
    try {
      const created = await api.create(inputOf(draft))
      await onCreated(created.id)
    } catch (err) {
      const fe = fieldErrorsOf(err)
      setFields(fe)
      setError(Object.keys(fe).length ? 'Please correct the highlighted fields.' : messageOf(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} noValidate aria-label="Create a party" className="mb-6 space-y-3 rounded-lg border bg-card p-4">
      <h2 className="text-sm font-semibold">New party</h2>
      <PartyFields draft={draft} set={set} errors={fields} />
      <FormError error={error} />
      <div className="flex gap-2">
        <button type="submit" disabled={busy} className={buttonClassName('default')}>Create party</button>
        <button type="button" className={buttonClassName('outline')} onClick={onCancel}>Cancel</button>
      </div>
    </form>
  )
}
