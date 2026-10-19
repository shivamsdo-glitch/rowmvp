import { useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { libraryApi, type LibraryAgent } from '@/api/library'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { DataTable, fieldErrorsOf, FormError, messageOf, SelectField, TextField } from '../projects/form-kit'

/**
 * The Library's Agents list: the people the agency assigns to parcels (designer, surveyor, title researcher, appraiser, negotiator,
 * broker...), entered once so forms pick them. Everyone reads it; people who may change the Library add, edit and switch off.
 */
export function AgentsPanel({ agentTypes }: { agentTypes: string[] }) {
  const { manager } = useAuth()
  const api = libraryApi(manager.api)
  const queryClient = useQueryClient()
  const canCreate = manager.can('library', 'create')
  const canUpdate = manager.can('library', 'update')
  const agents = useQuery({ queryKey: ['library', 'agents'], queryFn: () => api.agents() })
  const [filter, setFilter] = useState('')
  const [typeFilter, setTypeFilter] = useState('')
  const [form, setForm] = useState<'new' | LibraryAgent | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['library', 'agents'] })

  if (agents.isPending) return <LoadingRows rows={4} />
  if (agents.isError) return <ErrorState title="The agents could not be loaded" description={agents.error.message} retry={() => void agents.refetch()} />

  const text = filter.trim().toLowerCase()
  const shown = agents.data.filter((a) =>
    (!typeFilter || a.agentType === typeFilter) &&
    (!text || [a.name, a.company ?? '', a.email ?? ''].some((v) => v.toLowerCase().includes(text))))
  const typeOptions = [...new Set([...agentTypes, ...agents.data.map((a) => a.agentType)])].sort().map((t) => ({ value: t, label: t }))

  async function setActive(a: LibraryAgent, isActive: boolean) {
    setError(null)
    setBusy(true)
    try {
      await api.saveAgent(a.id, { name: a.name, agentType: a.agentType, company: a.company, phone: a.phone, email: a.email, isActive })
      await refresh()
    } catch (e) {
      setError(messageOf(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section aria-label="Agents" className="min-w-0 space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="text-base font-semibold">Agents</h2>
          <p className="text-xs text-muted-foreground">People you assign to parcels, entered once. Switch a person off instead of removing them.</p>
        </div>
        {canCreate && form === null && <button type="button" className={buttonClassName('default', 'sm')} onClick={() => setForm('new')}>New agent</button>}
      </div>

      {form !== null && (
        <AgentForm
          agent={form === 'new' ? undefined : form}
          typeOptions={agentTypes.map((t) => ({ value: t, label: t }))}
          onCancel={() => setForm(null)}
          onSaved={async () => { setForm(null); await refresh() }}
        />
      )}
      <FormError error={error} />

      <div className="grid max-w-2xl gap-3 sm:grid-cols-2">
        <TextField label="Filter agents" value={filter} onChange={setFilter} placeholder="Name, company or email" />
        <SelectField label="Agent type filter" value={typeFilter} onChange={setTypeFilter} blankLabel="All types" options={typeOptions} />
      </div>

      {agents.data.length === 0 ? (
        <EmptyState title="No agents yet" description={canCreate ? 'Add the first agent.' : 'Nobody has been added yet.'} />
      ) : shown.length === 0 ? (
        <p className="text-sm text-muted-foreground">No agent matches your filter.</p>
      ) : (
        <DataTable caption="Agents" headers={canUpdate ? ['Agent name', 'Agent type', 'Company', 'Phone number', 'Email', 'Is active', 'Actions'] : ['Agent name', 'Agent type', 'Company', 'Phone number', 'Email', 'Is active']}>
          {shown.map((a) => (
            <tr key={a.id}>
              <td className="px-3 py-2 font-medium">{a.name}</td>
              <td className="px-3 py-2">{a.agentType}</td>
              <td className="px-3 py-2">{a.company ?? ''}</td>
              <td className="px-3 py-2">{a.phone ?? ''}</td>
              <td className="px-3 py-2">{a.email ?? ''}</td>
              <td className="px-3 py-2">
                <input type="checkbox" className="h-4 w-4" checked={a.isActive} disabled={!canUpdate || busy} aria-label={`${a.name}, ${a.agentType} is active`} onChange={(e) => void setActive(a, e.target.checked)} />
              </td>
              {canUpdate && (
                <td className="px-3 py-2">
                  <button type="button" className={buttonClassName('ghost', 'sm')} aria-label={`Edit ${a.name}, ${a.agentType}`} onClick={() => { setForm(a); setError(null) }}>Edit</button>
                </td>
              )}
            </tr>
          ))}
        </DataTable>
      )}
    </section>
  )
}

function AgentForm({ agent, typeOptions, onCancel, onSaved }: {
  agent?: LibraryAgent
  typeOptions: { value: string; label: string }[]
  onCancel: () => void
  onSaved: () => Promise<unknown>
}) {
  const { manager } = useAuth()
  const api = libraryApi(manager.api)
  const [name, setName] = useState(agent?.name ?? '')
  const [agentType, setAgentType] = useState(agent?.agentType ?? '')
  const [company, setCompany] = useState(agent?.company ?? '')
  const [phone, setPhone] = useState(agent?.phone ?? '')
  const [email, setEmail] = useState(agent?.email ?? '')
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  // A job that has been switched off since stays selectable for the agent who already has it.
  const options = agent && !typeOptions.some((o) => o.value === agent.agentType) ? [...typeOptions, { value: agent.agentType, label: agent.agentType }] : typeOptions

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    const missing: Record<string, string> = {}
    if (!name.trim()) missing.name = 'Agent name is required.'
    if (!agentType) missing.agentType = 'Agent type is required.'
    setFields(missing)
    if (Object.keys(missing).length) return
    setBusy(true)
    try {
      await api.saveAgent(agent?.id ?? null, {
        name: name.trim(), agentType, company: company.trim() || null, phone: phone.trim() || null, email: email.trim() || null, isActive: agent?.isActive ?? true,
      })
      await onSaved()
    } catch (err) {
      const fe = fieldErrorsOf(err)
      setFields(Object.fromEntries(Object.entries(fe).map(([k, v]) => [k.charAt(0).toLowerCase() + k.slice(1), v])))
      setError(Object.keys(fe).length ? 'Please correct the highlighted fields.' : messageOf(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} noValidate aria-label={agent ? 'Edit agent' : 'Add an agent'} className="grid max-w-2xl gap-3 rounded-lg border bg-card p-3 sm:grid-cols-2">
      <h3 className="text-sm font-semibold sm:col-span-2">{agent ? `Edit ${agent.name}` : 'New agent'}</h3>
      <TextField label="Agent name" value={name} onChange={setName} maxLength={150} error={fields.name} />
      <SelectField label="Agent type" value={agentType} onChange={setAgentType} blankLabel="Select one" options={options} error={fields.agentType} />
      <TextField label="Company" value={company} onChange={setCompany} maxLength={150} error={fields.company} />
      <TextField label="Phone number" value={phone} onChange={setPhone} maxLength={40} error={fields.phone} />
      <TextField label="Email" value={email} onChange={setEmail} maxLength={254} error={fields.email} />
      <div className="sm:col-span-2"><FormError error={error} /></div>
      <div className="flex gap-2 sm:col-span-2">
        <button type="submit" disabled={busy} className={buttonClassName('default', 'sm')}>{agent ? 'Save agent' : 'Add agent'}</button>
        <button type="button" className={buttonClassName('outline', 'sm')} onClick={onCancel}>Cancel</button>
      </div>
    </form>
  )
}
