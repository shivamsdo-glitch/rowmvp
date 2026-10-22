import { useEffect, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowDown, ArrowUp, Plus, Trash2 } from 'lucide-react'
import { ROLES } from '@/api/auth'
import { relocationBandsApi, type RelocationBand } from '@/api/parcel-tabs'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { ErrorState, LoadingRows } from '@/components/ui/states'
import { FormError, messageOf, SelectField } from '../projects/form-kit'

interface Row { min: string; max: string; roles: string[] }

const toRows = (bands: RelocationBand[]): Row[] => bands.map((b) => ({ min: String(b.minAmount), max: b.maxAmount === null ? '' : String(b.maxAmount), roles: b.roles }))

/**
 * The approval chain relocation payments go through (ADR 0026): dollar bands, and for each band the roles that sign in order. Until an
 * administrator saves bands, placeholder defaults apply.
 */
export function RelocationBandsCard() {
  const { manager } = useAuth()
  const api = relocationBandsApi(manager.api)
  const queryClient = useQueryClient()
  const canEdit = manager.can('administration', 'update')
  const query = useQuery({ queryKey: ['relocation-bands'], queryFn: () => api.get() })
  const [rows, setRows] = useState<Row[]>([])
  const [pick, setPick] = useState<Record<number, string>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [saved, setSaved] = useState<string | null>(null)

  useEffect(() => { if (query.data) setRows(toRows(query.data.bands)) }, [query.data])

  const save = useMutation({
    mutationFn: (bands: RelocationBand[]) => api.save(bands),
    onSuccess: async () => { setSaved('Saved. New payment requests use these bands.'); setFormError(null); await queryClient.invalidateQueries({ queryKey: ['relocation-bands'] }) },
    onError: (e) => { setSaved(null); setFormError(messageOf(e)) },
  })

  const patch = (i: number, r: Partial<Row>) => setRows(rows.map((x, j) => (j === i ? { ...x, ...r } : x)))
  const move = (i: number, j: number, d: number) => {
    const roles = [...rows[i].roles]
    ;[roles[j], roles[j + d]] = [roles[j + d], roles[j]]
    patch(i, { roles })
  }

  function submit(e: FormEvent) {
    e.preventDefault()
    setSaved(null); setFormError(null)
    const bands: RelocationBand[] = []
    for (const [i, r] of rows.entries()) {
      const min = Number(r.min)
      const max = r.max.trim() === '' ? null : Number(r.max)
      if (r.min.trim() === '' || Number.isNaN(min) || min < 0) return setFormError(`Band ${i + 1}: enter a minimum of zero or more.`)
      if (max !== null && (Number.isNaN(max) || max <= min)) return setFormError(`Band ${i + 1}: the maximum must be above the minimum.`)
      if (r.roles.length === 0) return setFormError(`Band ${i + 1}: add at least one approver role.`)
      bands.push({ minAmount: min, maxAmount: max, roles: r.roles })
    }
    save.mutate(bands)
  }

  return (
    <section aria-label="Relocation payment approvals" className="mt-6 max-w-3xl rounded-lg border bg-card p-4">
      <h2 className="text-sm font-semibold">Relocation payment approvals</h2>
      <p className="mb-3 text-xs text-muted-foreground">
        Who signs a relocation payment, by amount. The roles sign in the order listed, then Finance marks the payment paid.
        {query.data?.isDefault && ' These are placeholder defaults until saved.'}
      </p>
      {query.isError ? <ErrorState title="The approval bands could not be loaded" retry={() => void query.refetch()} /> : query.isPending ? <LoadingRows rows={2} /> : (
        <form onSubmit={submit} noValidate aria-label="Relocation payment approval bands" className="space-y-3">
          {rows.map((r, i) => (
            <fieldset key={i} aria-label={`Band ${i + 1}`} className="rounded-md border p-3">
              <legend className="px-1 text-xs font-medium text-muted-foreground">Band {i + 1}</legend>
              <div className="grid gap-3 sm:grid-cols-2">
                <label className="text-sm">From ($)
                  <input aria-label={`Band ${i + 1} minimum`} type="number" min={0} step="any" disabled={!canEdit} value={r.min} onChange={(e) => patch(i, { min: e.target.value })} className="mt-1 w-full rounded-md border bg-background px-2 py-1" />
                </label>
                <label className="text-sm">Up to, not including ($)
                  <input aria-label={`Band ${i + 1} maximum`} type="number" min={0} step="any" disabled={!canEdit} value={r.max} placeholder="No limit" onChange={(e) => patch(i, { max: e.target.value })} className="mt-1 w-full rounded-md border bg-background px-2 py-1" />
                </label>
              </div>
              <ol className="mt-2 space-y-1" aria-label={`Band ${i + 1} approvers`}>
                {r.roles.map((role, j) => (
                  <li key={role} className="flex items-center gap-2 text-sm">
                    <span className="w-5 text-xs text-muted-foreground">{j + 1}.</span><span className="flex-1">{role}</span>
                    {canEdit && (
                      <>
                        <button type="button" aria-label={`Move ${role} up in band ${i + 1}`} disabled={j === 0} className={buttonClassName('ghost', 'sm')} onClick={() => move(i, j, -1)}><ArrowUp size={14} aria-hidden /></button>
                        <button type="button" aria-label={`Move ${role} down in band ${i + 1}`} disabled={j === r.roles.length - 1} className={buttonClassName('ghost', 'sm')} onClick={() => move(i, j, 1)}><ArrowDown size={14} aria-hidden /></button>
                        <button type="button" aria-label={`Remove ${role} from band ${i + 1}`} className={buttonClassName('ghost', 'sm')} onClick={() => patch(i, { roles: r.roles.filter((x) => x !== role) })}><Trash2 size={14} aria-hidden /></button>
                      </>
                    )}
                  </li>
                ))}
              </ol>
              {canEdit && (
                <div className="mt-2 flex items-end gap-2">
                  <SelectField label={`Add approver to band ${i + 1}`} value={pick[i] ?? ''} onChange={(v) => setPick({ ...pick, [i]: v })} blankLabel="Select a role"
                    options={ROLES.filter((x) => !r.roles.includes(x)).map((x) => ({ value: x, label: x }))} />
                  <button type="button" className={buttonClassName('outline', 'sm')} onClick={() => { if (pick[i]) { patch(i, { roles: [...r.roles, pick[i]] }); setPick({ ...pick, [i]: '' }) } }}><Plus size={14} aria-hidden /> Add</button>
                  <button type="button" aria-label={`Remove band ${i + 1}`} className={buttonClassName('ghost', 'sm')} onClick={() => setRows(rows.filter((_, j) => j !== i))}><Trash2 size={14} aria-hidden /></button>
                </div>
              )}
            </fieldset>
          ))}
          <FormError error={formError} />
          {saved && <p role="status" className="text-sm">{saved}</p>}
          {canEdit ? (
            <div className="flex flex-wrap gap-2">
              <button type="button" className={buttonClassName('outline')} onClick={() => setRows([...rows, { min: rows.length ? (rows[rows.length - 1].max || '0') : '0', max: '', roles: [] }])}>Add band</button>
              <button type="submit" className={buttonClassName('default')} disabled={save.isPending}>{save.isPending ? 'Saving…' : 'Save approval bands'}</button>
            </div>
          ) : <p className="text-xs text-muted-foreground">Only an administrator can change these.</p>}
        </form>
      )}
    </section>
  )
}
