import { useEffect, useState } from 'react'
import { Link, useNavigate } from '@tanstack/react-router'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { FolderKanban } from 'lucide-react'
import { OVERSIGHT_TYPES, projectsApi, type OversightType, type ProjectInput } from '@/api/projects'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { PageHeader } from '@/components/ui/page-header'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { DataTable, money, Pill, SelectField, TextField } from './form-kit'
import { ImportPanel } from './ImportPanel'
import { ProjectForm } from './ProjectForm'

function useDebounced<T>(value: T, ms = 300): T {
  const [v, setV] = useState(value)
  useEffect(() => {
    const t = setTimeout(() => setV(value), ms)
    return () => clearTimeout(t)
  }, [value, ms])
  return v
}

const PAGE_SIZE = 25
const OVERSIGHT_OPTIONS = OVERSIGHT_TYPES.map((o) => ({ value: o, label: o }))

/** Projects: find, open, create and import (ROW-001, 003, 004). */
export function ProjectListPage() {
  const { manager } = useAuth()
  const api = projectsApi(manager.api)
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const canCreate = manager.can('projects', 'create')

  const [search, setSearch] = useState('')
  const [region, setRegion] = useState('')
  const [district, setDistrict] = useState('')
  const [oversight, setOversight] = useState<OversightType | ''>('')
  const [page, setPage] = useState(1)
  const [panel, setPanel] = useState<'none' | 'new' | 'import'>('none')

  const filter = {
    search: useDebounced(search), region: useDebounced(region), district: useDebounced(district), oversightType: oversight, page, pageSize: PAGE_SIZE,
  }
  const list = useQuery({ queryKey: ['projects', filter], queryFn: () => api.list(filter), placeholderData: keepPreviousData })

  const create = useMutation({
    mutationFn: (input: ProjectInput) => api.create(input),
    onSuccess: async (project) => {
      await queryClient.invalidateQueries({ queryKey: ['projects'] })
      await navigate({ to: '/projects/$projectId', params: { projectId: project.id } })
    },
  })

  const filtered = !!(filter.search || filter.region || filter.district || oversight)
  const totalPages = list.data ? Math.max(1, Math.ceil(list.data.total / PAGE_SIZE)) : 1
  const reset = <T,>(set: (v: T) => void) => (v: T) => { set(v); setPage(1) }

  return (
    <>
      <PageHeader
        title="Projects"
        subtitle="Right-of-way projects, their funding and whether acquisition can start."
        actions={canCreate && (
          <>
            <button type="button" className={buttonClassName('outline')} onClick={() => setPanel(panel === 'import' ? 'none' : 'import')}>Import CSV</button>
            <button type="button" className={buttonClassName('default')} onClick={() => setPanel(panel === 'new' ? 'none' : 'new')}>New project</button>
          </>
        )}
      />

      {panel === 'new' && (
        <section aria-label="Create a project" className="mb-6 rounded-lg border bg-card p-4">
          <h2 className="mb-3 text-sm font-semibold">New project</h2>
          <ProjectForm
            submitLabel="Create project"
            onCancel={() => setPanel('none')}
            onSubmit={async (input) => { await create.mutateAsync(input) }}
          />
        </section>
      )}
      {panel === 'import' && (
        <ImportPanel
          onImport={async (rows) => {
            const summary = await api.import(rows)
            await queryClient.invalidateQueries({ queryKey: ['projects'] })
            return summary
          }}
          onClose={() => setPanel('none')}
        />
      )}

      <form role="search" aria-label="Filter projects" className="mb-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-4" onSubmit={(e) => e.preventDefault()}>
        <TextField label="Search projects" value={search} onChange={reset(setSearch)} placeholder="Number or name" />
        <TextField label="Region" value={region} onChange={reset(setRegion)} />
        <TextField label="District" value={district} onChange={reset(setDistrict)} />
        <SelectField label="Funding oversight" value={oversight} onChange={reset(setOversight)} options={OVERSIGHT_OPTIONS} blankLabel="All" />
      </form>

      {list.isPending ? (
        <LoadingRows />
      ) : list.isError ? (
        <ErrorState title="Projects could not be loaded" description={list.error.message} retry={() => void list.refetch()} />
      ) : list.data.items.length === 0 ? (
        <EmptyState
          icon={FolderKanban}
          title={filtered ? 'No projects match your filters' : 'No projects yet'}
          description={filtered ? 'Try a different search or clear a filter.' : canCreate ? 'Create the first project to get started.' : 'Projects will appear here once they are created.'}
        />
      ) : (
        <>
          <DataTable caption="Projects" headers={['Number', 'Name', 'Region / district', 'Oversight', 'Current estimate', 'Acquisition']}>
            {list.data.items.map((p) => (
              <tr key={p.id} className="hover:bg-muted/50">
                <td className="px-3 py-2 font-medium">
                  <Link to="/projects/$projectId" params={{ projectId: p.id }} className="text-primary hover:underline">{p.number}</Link>
                </td>
                <td className="px-3 py-2">{p.name}</td>
                <td className="px-3 py-2">{[p.region, p.district].filter(Boolean).join(' / ') || '—'}</td>
                <td className="px-3 py-2">{p.oversightType}</td>
                <td className="px-3 py-2">{money(p.currentEstimate)}</td>
                <td className="px-3 py-2">
                  {p.acquisitionGate.unlocked
                    ? <Pill tone="ok">Open for offers</Pill>
                    : <Pill tone="warn">Held: {p.acquisitionGate.missing.join(', ')}</Pill>}
                </td>
              </tr>
            ))}
          </DataTable>
          <nav aria-label="Pages" className="mt-3 flex items-center justify-between text-sm">
            <span>{list.data.total} project{list.data.total === 1 ? '' : 's'}</span>
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
