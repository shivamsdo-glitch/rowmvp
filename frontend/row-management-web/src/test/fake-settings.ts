import { PAGE_KEYS, ROLES } from '@/api/auth'
import type { Role, RolePermission, SettingsUser } from '@/api/settings'
import { fakeAuthFetch } from './fake-api'
import { contract } from './contract'

function json(body: unknown, status = 200) {
  return new Response(status === 204 ? null : JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}
const problem = (status: number, title: string, detail: string, errors?: Record<string, string[]>) => json({ title, status, detail, errors }, status)

export interface FakeSettings {
  fetch: typeof fetch
  roles: Role[]
  users: SettingsUser[]
  calls: { key: string; body?: any }[]
}

/** An in-memory stand-in for the Settings API with the server's rules (locked Admin, built-in roles, last administrator). */
export function fakeSettingsApi(opts: { role: string; rows: Parameters<typeof fakeAuthFetch>[0]['rows'] }): FakeSettings {
  const auth = fakeAuthFetch({ rows: opts.rows, role: opts.role }) as typeof fetch
  let seq = 0
  const id = (p: string) => `${p}${++seq}`
  const permsFor = (name: string): RolePermission[] => PAGE_KEYS.map((p) => {
    const level = name === 'Admin' ? 'P' : contract.matrix[name]?.[p] ?? null
    return { pageKey: p, canCreate: level === 'S' || level === 'P', canRead: level !== null, canUpdate: level === 'S' || level === 'P', canDelete: level === 'P' }
  })
  const roles: Role[] = ROLES.map((name) => ({
    id: `role-${name}`, name, description: `${name} role`, isSystem: true, isLocked: name === 'Admin', userCount: 1, permissions: permsFor(name),
  }))
  const users: SettingsUser[] = ROLES.map((name) => ({
    id: `user-${name}`, username: name.toLowerCase(), firstName: name, lastName: 'User', email: `${name.toLowerCase()}@row.local`, isActive: true,
    roleId: `role-${name}`, roleName: name,
  }))
  const calls: FakeSettings['calls'] = []
  const can = (a: 'Create' | 'Read' | 'Update' | 'Delete') => !!opts.rows?.find((r) => r.pageKey === 'administration')?.[`can${a}` as 'canRead']

  const fetchImpl = async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://x')
    const m = /^\/api\/v1\/(roles|users)(?:\/([^/]+)(?:\/(permissions|reset-password))?)?$/.exec(url.pathname)
    if (!m) return auth(input, init)
    const headers = (init?.headers ?? {}) as Record<string, string>
    if (headers.Authorization !== 'Bearer good-token') return json({ title: 'unauthorized' }, 401)
    const method = init?.method ?? 'GET'
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    calls.push({ key: `${method} ${url.pathname}`, body })
    const [, kind, rid, sub] = m
    const forbidden = () => problem(403, 'forbidden', 'Your role does not allow this.')

    if (kind === 'roles') {
      if (!rid) {
        if (method === 'GET') return can('Read') ? json(roles) : forbidden()
        if (!can('Create')) return forbidden()
        const name = String(body.name ?? '').trim()
        if (!name) return problem(400, 'validation_failed', 'Validation failed.', { Name: ['Role name is required.'] })
        if (roles.some((r) => r.name.toLowerCase() === name.toLowerCase())) return problem(409, 'duplicate_role', `A role named '${name}' already exists.`)
        const role: Role = { id: id('role'), name, description: body.description ?? null, isSystem: false, isLocked: false, userCount: 0, permissions: PAGE_KEYS.map((p) => ({ pageKey: p, canCreate: false, canRead: false, canUpdate: false, canDelete: false })) }
        roles.push(role)
        return json(role, 201)
      }
      const role = roles.find((r) => r.id === rid)
      if (!role) return problem(404, 'not_found', 'Role was not found.')
      if (sub === 'permissions') {
        if (!can('Update')) return forbidden()
        if (role.isLocked) return problem(409, 'admin_locked', 'The Admin role always has full access and cannot be changed.')
        for (const p of body.permissions as RolePermission[]) role.permissions = role.permissions.map((x) => (x.pageKey === p.pageKey ? p : x))
        return json(role)
      }
      if (method === 'PUT') {
        if (!can('Update')) return forbidden()
        if (role.isLocked) return problem(409, 'admin_locked', 'The Admin role cannot be changed.')
        if (role.isSystem && body.name !== role.name) return problem(409, 'system_role', `'${role.name}' is a built-in role; only its description can be changed.`)
        role.name = body.name
        role.description = body.description
        return json(role)
      }
      if (method === 'DELETE') {
        if (!can('Delete')) return forbidden()
        if (role.isSystem) return problem(409, 'system_role', `'${role.name}' is a built-in role and cannot be deleted.`)
        if (users.some((u) => u.roleId === role.id)) return problem(409, 'role_in_use', `'${role.name}' still has users. Move them to another role first.`)
        roles.splice(roles.indexOf(role), 1)
        return json(null, 204)
      }
    }

    if (!rid) {
      if (method === 'GET') return can('Read') ? json(users) : forbidden()
      if (!can('Create')) return forbidden()
      const name = String(body.username ?? '').trim().toLowerCase()
      if (users.some((u) => u.username === name)) return problem(409, 'duplicate_username', `The username '${name}' is taken.`)
      if (String(body.password ?? '').length < 8) return problem(400, 'validation_failed', 'Validation failed.', { Password: ['Password must be at least 8 characters.'] })
      const role = roles.find((r) => r.id === body.roleId)
      const user: SettingsUser = { id: id('user'), username: name, firstName: body.firstName, lastName: body.lastName, email: body.email, isActive: true, roleId: body.roleId, roleName: role?.name ?? null }
      users.push(user)
      return json(user, 201)
    }
    const user = users.find((u) => u.id === rid)
    if (!user) return problem(404, 'not_found', 'User was not found.')
    if (!can('Update')) return forbidden()
    if (sub === 'reset-password') return String(body.password).length < 8 ? problem(400, 'validation_failed', 'Validation failed.', { Password: ['Password must be at least 8 characters.'] }) : json(null, 204)
    const role = roles.find((r) => r.id === body.roleId)
    Object.assign(user, { firstName: body.firstName, lastName: body.lastName, email: body.email, isActive: body.isActive, roleId: body.roleId, roleName: role?.name ?? null })
    return json(user)
  }

  return { fetch: fetchImpl as typeof fetch, roles, users, calls }
}
