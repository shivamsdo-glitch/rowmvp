import { useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { PAGE_KEYS } from '@/api/auth'
import { settingsApi, type Role, type RolePermission, type SettingsUser } from '@/api/settings'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { PageHeader } from '@/components/ui/page-header'
import { ErrorState, LoadingRows } from '@/components/ui/states'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { CheckboxField, DataTable, fieldErrorsOf, FormError, messageOf, Pill, SelectField, TextField } from '../projects/form-kit'

type SettingsTab = 'users' | 'permissions'

/** What each page key is called in the permission grid (the grid's "modules"). */
export const PAGE_LABELS: Record<string, string> = {
  dashboard: 'Dashboard', projects: 'Projects', parcels: 'Parcels', parties: 'Parties', title: 'Title', survey: 'Survey',
  appraisals: 'Appraisals', approvals: 'Approvals', negotiations: 'Negotiations', agreements: 'Agreements', closing: 'Closing',
  payments: 'Payments', administration: 'Administration',
}
const pageLabel = (key: string) => PAGE_LABELS[key] ?? key

/** Settings: the agency's users and roles, and what each role may do on each module. Administrators only. */
export function SettingsPage() {
  const [tab, setTab] = useState<SettingsTab>('users')
  return (
    <>
      <PageHeader title="Settings" subtitle="Who can sign in, and what each role may create, read, update and delete." />
      <Tabs>
        <TabsList role="tablist" aria-label="Settings sections">
          <TabsTrigger role="tab" aria-selected={tab === 'users'} active={tab === 'users'} onClick={() => setTab('users')}>Users &amp; Roles</TabsTrigger>
          <TabsTrigger role="tab" aria-selected={tab === 'permissions'} active={tab === 'permissions'} onClick={() => setTab('permissions')}>Permissions</TabsTrigger>
        </TabsList>
        <div role="tabpanel" className="mt-4">
          {tab === 'users' ? <UsersAndRoles /> : <PermissionsGrid />}
        </div>
      </Tabs>
    </>
  )
}

function useSettingsData() {
  const { manager } = useAuth()
  const api = settingsApi(manager.api)
  const queryClient = useQueryClient()
  const roles = useQuery({ queryKey: ['settings', 'roles'], queryFn: () => api.roles() })
  const users = useQuery({ queryKey: ['settings', 'users'], queryFn: () => api.users() })
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['settings'] })
  return { manager, api, roles, users, refresh }
}

// ── Users & Roles ───────────────────────────────────────────────────────

function UsersAndRoles() {
  const { manager, api, roles, users, refresh } = useSettingsData()
  const canCreate = manager.can('administration', 'create')
  const canUpdate = manager.can('administration', 'update')
  const [addingUser, setAddingUser] = useState(false)
  const [editing, setEditing] = useState<SettingsUser | null>(null)
  const [addingRole, setAddingRole] = useState(false)
  const [editingRole, setEditingRole] = useState<Role | null>(null)

  if (roles.isPending || users.isPending) return <LoadingRows />
  if (roles.isError || users.isError) {
    const err = roles.error ?? users.error
    return <ErrorState title="Users and roles could not be loaded" description={err?.message ?? ''} retry={() => void refresh()} />
  }

  return (
    <div className="space-y-8">
      <section aria-labelledby="users-h" className="space-y-3">
        <div className="flex items-center justify-between">
          <h2 id="users-h" className="text-base font-semibold">Users</h2>
          {canCreate && !addingUser && <button type="button" className={buttonClassName('default', 'sm')} onClick={() => { setAddingUser(true); setEditing(null) }}>New user</button>}
        </div>
        {addingUser && <UserForm roles={roles.data} onCancel={() => setAddingUser(false)} onSaved={async () => { setAddingUser(false); await refresh() }} />}
        {editing && <UserForm roles={roles.data} user={editing} onCancel={() => setEditing(null)} onSaved={async () => { setEditing(null); await refresh() }} />}
        <DataTable caption="Users" headers={['Username', 'Name', 'Email', 'Role', 'Status', '']}>
          {users.data.map((u) => (
            <tr key={u.id}>
              <td className="px-3 py-2 font-medium">{u.username}</td>
              <td className="px-3 py-2">{u.firstName} {u.lastName}</td>
              <td className="px-3 py-2">{u.email}</td>
              <td className="px-3 py-2">{u.roleName ?? '—'}</td>
              <td className="px-3 py-2"><Pill tone={u.isActive ? 'ok' : 'neutral'}>{u.isActive ? 'Active' : 'Inactive'}</Pill></td>
              <td className="px-3 py-2 text-right">
                {canUpdate && <button type="button" className={buttonClassName('outline', 'sm')} aria-label={`Edit ${u.username}`} onClick={() => { setEditing(u); setAddingUser(false) }}>Edit</button>}
              </td>
            </tr>
          ))}
        </DataTable>
      </section>

      <section aria-labelledby="roles-h" className="space-y-3">
        <div className="flex items-center justify-between">
          <h2 id="roles-h" className="text-base font-semibold">Roles</h2>
          {canCreate && !addingRole && <button type="button" className={buttonClassName('default', 'sm')} onClick={() => { setAddingRole(true); setEditingRole(null) }}>New role</button>}
        </div>
        {addingRole && <RoleForm onCancel={() => setAddingRole(false)} onSaved={async () => { setAddingRole(false); await refresh() }} />}
        {editingRole && <RoleForm role={editingRole} onCancel={() => setEditingRole(null)} onSaved={async () => { setEditingRole(null); await refresh() }} />}
        <DataTable caption="Roles" headers={['Role', 'Description', 'Users', 'Type', '']}>
          {roles.data.map((r) => (
            <tr key={r.id}>
              <td className="px-3 py-2 font-medium">{r.name}</td>
              <td className="px-3 py-2">{r.description ?? '—'}</td>
              <td className="px-3 py-2">{r.userCount}</td>
              <td className="px-3 py-2"><Pill tone="neutral">{r.isLocked ? 'Locked' : r.isSystem ? 'Built-in' : 'Custom'}</Pill></td>
              <td className="px-3 py-2 text-right">
                <span className="inline-flex gap-2">
                  {canUpdate && !r.isLocked && <button type="button" className={buttonClassName('outline', 'sm')} aria-label={`Edit role ${r.name}`} onClick={() => { setEditingRole(r); setAddingRole(false) }}>Edit</button>}
                  {manager.can('administration', 'delete') && !r.isSystem && (
                    <DeleteRoleButton role={r} onDone={refresh} />
                  )}
                </span>
              </td>
            </tr>
          ))}
        </DataTable>
      </section>
    </div>
  )
}

function DeleteRoleButton({ role, onDone }: { role: Role; onDone: () => Promise<unknown> }) {
  const { manager } = useAuth()
  const [error, setError] = useState<string | null>(null)
  return (
    <>
      <button
        type="button"
        className={buttonClassName('outline', 'sm')}
        aria-label={`Delete role ${role.name}`}
        onClick={async () => {
          setError(null)
          try { await settingsApi(manager.api).deleteRole(role.id); await onDone() } catch (e) { setError(messageOf(e)) }
        }}
      >
        Delete
      </button>
      {error && <span role="alert" className="text-xs text-destructive">{error}</span>}
    </>
  )
}

function RoleForm({ role, onCancel, onSaved }: { role?: Role; onCancel: () => void; onSaved: () => Promise<unknown> }) {
  const { manager } = useAuth()
  const api = settingsApi(manager.api)
  const [name, setName] = useState(role?.name ?? '')
  const [description, setDescription] = useState(role?.description ?? '')
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setFields({})
    if (!name.trim()) { setFields({ name: 'Role name is required.' }); return }
    setBusy(true)
    try {
      if (role) await api.updateRole(role.id, name.trim(), description.trim() || null)
      else await api.createRole(name.trim(), description.trim() || null)
      await onSaved()
    } catch (err) {
      const fe = fieldErrorsOf(err)
      setFields(fe)
      setError(Object.keys(fe).length ? 'Please correct the highlighted fields.' : messageOf(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} noValidate aria-label={role ? 'Edit role' : 'Create a role'} className="max-w-md space-y-3 rounded-lg border bg-card p-3">
      <h3 className="text-sm font-semibold">{role ? `Edit ${role.name}` : 'New role'}</h3>
      <TextField label="Role name" value={name} onChange={setName} maxLength={60} disabled={role?.isSystem} error={fields.name ?? fields.Name} />
      <TextField label="Description" value={description} onChange={setDescription} maxLength={500} error={fields.description ?? fields.Description} />
      <FormError error={error} />
      <div className="flex gap-2">
        <button type="submit" disabled={busy} className={buttonClassName('default', 'sm')}>{role ? 'Save role' : 'Create role'}</button>
        <button type="button" className={buttonClassName('outline', 'sm')} onClick={onCancel}>Cancel</button>
      </div>
    </form>
  )
}

function UserForm({ roles, user, onCancel, onSaved }: { roles: Role[]; user?: SettingsUser; onCancel: () => void; onSaved: () => Promise<unknown> }) {
  const { manager } = useAuth()
  const api = settingsApi(manager.api)
  const [username, setUsername] = useState(user?.username ?? '')
  const [firstName, setFirstName] = useState(user?.firstName ?? '')
  const [lastName, setLastName] = useState(user?.lastName ?? '')
  const [email, setEmail] = useState(user?.email ?? '')
  const [password, setPassword] = useState('')
  const [roleId, setRoleId] = useState<string>(user?.roleId ?? '')
  const [isActive, setIsActive] = useState(user?.isActive ?? true)
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    const missing: Record<string, string> = {}
    if (!user && !username.trim()) missing.username = 'Username is required.'
    if (!firstName.trim()) missing.firstName = 'First name is required.'
    if (!lastName.trim()) missing.lastName = 'Last name is required.'
    if (!email.trim()) missing.email = 'Email is required.'
    if (!roleId) missing.roleId = 'Role is required.'
    if (!user && !password) missing.password = 'Password is required.'
    setFields(missing)
    if (Object.keys(missing).length) return
    setBusy(true)
    try {
      if (user) {
        await api.updateUser(user.id, { firstName: firstName.trim(), lastName: lastName.trim(), email: email.trim(), roleId, isActive })
        if (password) await api.resetPassword(user.id, password)
      } else {
        await api.createUser({ username: username.trim(), firstName: firstName.trim(), lastName: lastName.trim(), email: email.trim(), password, roleId })
      }
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
    <form onSubmit={submit} noValidate aria-label={user ? 'Edit user' : 'Create a user'} className="grid max-w-2xl gap-3 rounded-lg border bg-card p-3 sm:grid-cols-2">
      <h3 className="text-sm font-semibold sm:col-span-2">{user ? `Edit ${user.username}` : 'New user'}</h3>
      <TextField label="Username" value={username} onChange={setUsername} maxLength={64} disabled={!!user} error={fields.username} />
      <SelectField label="Role" value={roleId} onChange={setRoleId} blankLabel="Select a role" options={roles.map((r) => ({ value: r.id, label: r.name }))} error={fields.roleId} />
      <TextField label="First name" value={firstName} onChange={setFirstName} maxLength={120} error={fields.firstName} />
      <TextField label="Last name" value={lastName} onChange={setLastName} maxLength={120} error={fields.lastName} />
      <TextField label="Email" value={email} onChange={setEmail} maxLength={254} error={fields.email} />
      <TextField label={user ? 'New password (leave blank to keep)' : 'Password'} type="password" value={password} onChange={setPassword} maxLength={128} error={fields.password} />
      {user && <CheckboxField label="Active" checked={isActive} onChange={setIsActive} />}
      <div className="sm:col-span-2"><FormError error={error} /></div>
      <div className="flex gap-2 sm:col-span-2">
        <button type="submit" disabled={busy} className={buttonClassName('default', 'sm')}>{user ? 'Save user' : 'Create user'}</button>
        <button type="button" className={buttonClassName('outline', 'sm')} onClick={onCancel}>Cancel</button>
      </div>
    </form>
  )
}

// ── Permissions grid ────────────────────────────────────────────────────

const ACTIONS = [
  { key: 'canCreate', label: 'Create' },
  { key: 'canRead', label: 'Read' },
  { key: 'canUpdate', label: 'Update' },
  { key: 'canDelete', label: 'Delete' },
] as const

type Grid = Record<string, RolePermission[]>

/**
 * One row per role, one column group per module with Create, Read, Update and Delete checkboxes. Edits are held until Save;
 * the Admin role is locked at full access.
 */
function PermissionsGrid() {
  const { manager, api, roles, refresh } = useSettingsData()
  const canUpdate = manager.can('administration', 'update')
  const [draft, setDraft] = useState<Grid>({})
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  if (roles.isPending) return <LoadingRows />
  if (roles.isError) return <ErrorState title="Roles could not be loaded" description={roles.error.message} retry={() => void roles.refetch()} />

  const permissionsOf = (r: Role) => draft[r.id] ?? r.permissions
  const cell = (r: Role, page: string): RolePermission =>
    permissionsOf(r).find((p) => p.pageKey === page) ?? { pageKey: page, canCreate: false, canRead: false, canUpdate: false, canDelete: false }
  const dirtyRoles = roles.data.filter((r) => draft[r.id])

  function toggle(r: Role, page: string, key: (typeof ACTIONS)[number]['key']) {
    const current = cell(r, page)
    const next = { ...current, [key]: !current[key] }
    // Create, update and delete need read; taking read away takes the others with it.
    if (key === 'canRead' && !next.canRead) { next.canCreate = false; next.canUpdate = false; next.canDelete = false }
    if (key !== 'canRead' && next[key]) next.canRead = true
    const rows = PAGE_KEYS.map((p) => (p === page ? next : cell(r, p)))
    setDraft((d) => ({ ...d, [r.id]: rows }))
    setSaved(null)
  }

  async function save() {
    setBusy(true)
    setError(null)
    setSaved(null)
    try {
      for (const r of dirtyRoles) await api.setPermissions(r.id, draft[r.id])
      setDraft({})
      await refresh()
      setSaved(`Saved ${dirtyRoles.length} role${dirtyRoles.length === 1 ? '' : 's'}. People pick up the change the next time they sign in.`)
    } catch (e) {
      setError(messageOf(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-3">
      <p className="text-sm text-muted-foreground">Tick what each role may do in each module. Create, update and delete also need read. The Admin role always has full access.</p>
      <div className="overflow-x-auto rounded-md border">
        <table className="min-w-full text-sm">
          <caption className="sr-only">Permissions by role and module</caption>
          <thead className="bg-muted text-xs text-muted-foreground">
            <tr>
              <th scope="col" rowSpan={2} className="sticky left-0 z-10 bg-muted px-3 py-2 text-left font-medium">Role</th>
              {PAGE_KEYS.map((p) => <th key={p} scope="colgroup" colSpan={4} className="border-l px-2 py-2 text-center font-medium">{pageLabel(p)}</th>)}
            </tr>
            <tr>
              {PAGE_KEYS.flatMap((p) => ACTIONS.map((a) => (
                <th key={`${p}-${a.key}`} scope="col" className={`px-2 py-1 text-center font-normal ${a.key === 'canCreate' ? 'border-l' : ''}`}>{a.label[0]}<span className="sr-only">{a.label.slice(1)}</span></th>
              )))}
            </tr>
          </thead>
          <tbody className="divide-y">
            {roles.data.map((r) => (
              <tr key={r.id}>
                <th scope="row" className="sticky left-0 z-10 bg-card px-3 py-2 text-left font-medium">
                  {r.name}{r.isLocked && <span className="ml-2 text-xs font-normal text-muted-foreground">(locked)</span>}
                </th>
                {PAGE_KEYS.flatMap((p) => ACTIONS.map((a) => (
                  <td key={`${p}-${a.key}`} className={`px-2 py-1 text-center ${a.key === 'canCreate' ? 'border-l' : ''}`}>
                    <input
                      type="checkbox"
                      aria-label={`${r.name} ${a.label} ${pageLabel(p)}`}
                      checked={r.isLocked ? true : cell(r, p)[a.key]}
                      disabled={r.isLocked || !canUpdate}
                      onChange={() => toggle(r, p, a.key)}
                    />
                  </td>
                )))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <FormError error={error} />
      {saved && <p role="status" className="text-sm text-muted-foreground">{saved}</p>}
      {canUpdate && (
        <div className="flex gap-2">
          <button type="button" className={buttonClassName('default')} disabled={busy || dirtyRoles.length === 0} onClick={() => void save()}>Save permissions</button>
          <button type="button" className={buttonClassName('outline')} disabled={busy || dirtyRoles.length === 0} onClick={() => { setDraft({}); setError(null) }}>Discard changes</button>
        </div>
      )}
    </div>
  )
}
