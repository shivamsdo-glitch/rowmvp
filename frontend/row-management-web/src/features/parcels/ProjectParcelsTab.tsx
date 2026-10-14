import { useState, type FormEvent } from 'react'
import { Link } from '@tanstack/react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Trash2 } from 'lucide-react'
import { parcelsApi, type ParcelGroup, type ParentTract, type RollupBucket } from '@/api/parcels'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { ErrorState, LoadingRows } from '@/components/ui/states'
import { DataTable, FormError, messageOf, orNull, TextField } from '../projects/form-kit'
import { acres } from './parcel-kit'

function Buckets({ title, rows }: { title: string; rows: RollupBucket[] }) {
  return (
    <DataTable caption={title} headers={[title, 'Parcels', 'Area (acres)']}>
      {rows.length === 0 ? (
        <tr><td colSpan={3} className="px-3 py-2 text-muted-foreground">None yet.</td></tr>
      ) : rows.map((r) => (
        <tr key={r.id ?? r.name}>
          <td className="px-3 py-2">
            {r.color && <span aria-hidden className="mr-1.5 inline-block h-2.5 w-2.5 rounded-full border align-middle" style={{ backgroundColor: r.color }} />}
            {r.name}
          </td>
          <td className="px-3 py-2">{r.count}</td>
          <td className="px-3 py-2">{r.acres.toLocaleString('en-US', { maximumFractionDigits: 3 })}</td>
        </tr>
      ))}
    </DataTable>
  )
}

/** A project's parcels: roll-up by status and group, the groups (second WBS level) and the parent tracts (ROW-002, 006, 233, 236). */
export function ProjectParcelsTab({ projectId }: { projectId: string }) {
  const { manager } = useAuth()
  const api = parcelsApi(manager.api)
  const queryClient = useQueryClient()
  const canCreate = manager.can('parcels', 'create')
  const canUpdate = manager.can('parcels', 'update')
  const canDelete = manager.can('parcels', 'delete')

  const rollup = useQuery({ queryKey: ['rollup', projectId], queryFn: () => api.rollup(projectId) })
  const groups = useQuery({ queryKey: ['groups', projectId], queryFn: () => api.groups(projectId) })
  const tracts = useQuery({ queryKey: ['tracts', projectId], queryFn: () => api.tracts(projectId) })

  const [groupName, setGroupName] = useState('')
  const [groupDesc, setGroupDesc] = useState('')
  const [groupError, setGroupError] = useState<string | null>(null)
  const [tractNumber, setTractNumber] = useState('')
  const [tractTax, setTractTax] = useState('')
  const [tractError, setTractError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const refresh = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['rollup', projectId] }),
    queryClient.invalidateQueries({ queryKey: ['groups', projectId] }),
    queryClient.invalidateQueries({ queryKey: ['tracts', projectId] }),
    queryClient.invalidateQueries({ queryKey: ['parcels'] }),
  ])

  async function addGroup(e: FormEvent) {
    e.preventDefault()
    setGroupError(null)
    if (!groupName.trim()) { setGroupError('Group name is required.'); return }
    setBusy(true)
    try {
      await api.saveGroup(projectId, null, groupName.trim(), orNull(groupDesc))
      setGroupName(''); setGroupDesc('')
      await refresh()
    } catch (err) { setGroupError(messageOf(err)) } finally { setBusy(false) }
  }

  async function removeGroup(g: ParcelGroup) {
    setGroupError(null)
    setBusy(true)
    try { await api.deleteGroup(projectId, g.id); await refresh() } catch (err) { setGroupError(messageOf(err)) } finally { setBusy(false) }
  }

  async function addTract(e: FormEvent) {
    e.preventDefault()
    setTractError(null)
    if (!tractNumber.trim()) { setTractError('Tract number is required.'); return }
    const tax = tractTax.split(/[,\n]/).map((s) => s.trim()).filter(Boolean)
    setBusy(true)
    try {
      await api.saveTract(projectId, null, tractNumber.trim(), null, tax)
      setTractNumber(''); setTractTax('')
      await refresh()
    } catch (err) { setTractError(messageOf(err)) } finally { setBusy(false) }
  }

  if (rollup.isPending || groups.isPending || tracts.isPending) return <LoadingRows rows={4} />
  if (rollup.isError || groups.isError || tracts.isError) {
    return <ErrorState title="Parcels could not be loaded" retry={() => { void rollup.refetch(); void groups.refetch(); void tracts.refetch() }} />
  }
  const r = rollup.data

  return (
    <div className="space-y-8">
      <section aria-label="Parcel roll-up">
        <h2 className="mb-2 text-sm font-semibold">Roll-up</h2>
        <p className="mb-3 text-sm">
          <strong>{r.totalCount}</strong> parcel{r.totalCount === 1 ? '' : 's'} · <strong>{r.totalAcres.toLocaleString('en-US', { maximumFractionDigits: 3 })}</strong> acres
          {' '}({acres(r.totalAreaSqFt)} ac)
          {manager.can('parcels') && <> · <Link to="/parcels" search={{ projectId }} className="text-primary hover:underline">Open this project&apos;s parcels</Link></>}
        </p>
        <div className="grid gap-4 lg:grid-cols-2">
          <Buckets title="By status" rows={r.byStatus} />
          <Buckets title="By group" rows={r.byGroup} />
        </div>
      </section>

      <section aria-label="Parcel groups">
        <h2 className="mb-2 text-sm font-semibold">Groups</h2>
        <p className="mb-2 text-xs text-muted-foreground">Groups are the corridors or segments between the project and its parcels.</p>
        <GroupTable groups={groups.data} canDelete={canDelete} busy={busy} onDelete={(g) => void removeGroup(g)} />
        {canCreate && (
          <form onSubmit={addGroup} noValidate aria-label="Add a group" className="mt-3 flex max-w-2xl flex-wrap items-end gap-3">
            <TextField label="Group name" value={groupName} onChange={setGroupName} maxLength={100} />
            <TextField label="Group description" value={groupDesc} onChange={setGroupDesc} maxLength={500} />
            <button type="submit" disabled={busy} className={buttonClassName('default')}>Add group</button>
          </form>
        )}
        <FormError error={groupError} />
      </section>

      <section aria-label="Parent tracts">
        <h2 className="mb-2 text-sm font-semibold">Parent tracts</h2>
        <p className="mb-2 text-xs text-muted-foreground">An owner&apos;s whole tract with its assessor numbers; parcels are the takes from it.</p>
        <TractTable tracts={tracts.data} />
        {canCreate && (
          <form onSubmit={addTract} noValidate aria-label="Add a parent tract" className="mt-3 flex max-w-2xl flex-wrap items-end gap-3">
            <TextField label="Tract number" value={tractNumber} onChange={setTractNumber} maxLength={50} />
            <TextField label="Tax parcel numbers" value={tractTax} onChange={setTractTax} hint="Separate several with commas." />
            <button type="submit" disabled={busy} className={buttonClassName('default')}>Add tract</button>
          </form>
        )}
        <FormError error={tractError} />
        {!canCreate && !canUpdate && <p className="mt-2 text-xs text-muted-foreground">Your role can view groups and tracts but not change them.</p>}
      </section>
    </div>
  )
}

function GroupTable({ groups, canDelete, busy, onDelete }: { groups: ParcelGroup[]; canDelete: boolean; busy: boolean; onDelete: (g: ParcelGroup) => void }) {
  if (groups.length === 0) return <p className="text-sm text-muted-foreground">No groups yet.</p>
  return (
    <DataTable caption="Groups" headers={canDelete ? ['Group', 'Description', 'Parcels', 'Actions'] : ['Group', 'Description', 'Parcels']}>
      {groups.map((g) => (
        <tr key={g.id}>
          <td className="px-3 py-2 font-medium">{g.name}</td>
          <td className="px-3 py-2">{g.description ?? '—'}</td>
          <td className="px-3 py-2">{g.parcelCount}</td>
          {canDelete && (
            <td className="px-3 py-2">
              <button type="button" aria-label={`Delete group ${g.name}`} disabled={busy} className={buttonClassName('ghost', 'sm')} onClick={() => onDelete(g)}>
                <Trash2 size={14} aria-hidden />
              </button>
            </td>
          )}
        </tr>
      ))}
    </DataTable>
  )
}

function TractTable({ tracts }: { tracts: ParentTract[] }) {
  if (tracts.length === 0) return <p className="text-sm text-muted-foreground">No parent tracts yet.</p>
  return (
    <DataTable caption="Parent tracts" headers={['Tract', 'Tax parcel numbers', 'Parcels']}>
      {tracts.map((t) => (
        <tr key={t.id}>
          <td className="px-3 py-2 font-medium">{t.number}</td>
          <td className="px-3 py-2">{t.taxParcelNumbers.length ? t.taxParcelNumbers.join(', ') : '—'}</td>
          <td className="px-3 py-2">{t.parcelCount}</td>
        </tr>
      ))}
    </DataTable>
  )
}
