import type { ApiClient } from './client'

export interface RolePermission {
  pageKey: string
  canCreate: boolean
  canRead: boolean
  canUpdate: boolean
  canDelete: boolean
}

export interface Role {
  id: string
  name: string
  description: string | null
  /** A default role the product refers to by name: it cannot be renamed or deleted. */
  isSystem: boolean
  /** The Admin role: always full access, cannot be changed. */
  isLocked: boolean
  userCount: number
  permissions: RolePermission[]
}

export interface SettingsUser {
  id: string
  username: string
  firstName: string
  lastName: string
  email: string
  isActive: boolean
  roleId: string | null
  roleName: string | null
}

export interface NewUser {
  username: string
  firstName: string
  lastName: string
  email: string
  password: string
  roleId: string
}

export interface UserEdit {
  firstName: string
  lastName: string
  email: string
  roleId: string
  isActive: boolean
}

export const settingsApi = (api: ApiClient) => ({
  roles: () => api.get<Role[]>('/api/v1/roles'),
  createRole: (name: string, description: string | null) => api.post<Role>('/api/v1/roles', { name, description }),
  updateRole: (id: string, name: string, description: string | null) => api.put<Role>(`/api/v1/roles/${id}`, { name, description }),
  deleteRole: (id: string) => api.delete(`/api/v1/roles/${id}`),
  setPermissions: (id: string, permissions: RolePermission[]) => api.put<Role>(`/api/v1/roles/${id}/permissions`, { permissions }),
  users: () => api.get<SettingsUser[]>('/api/v1/users'),
  createUser: (u: NewUser) => api.post<SettingsUser>('/api/v1/users', u),
  updateUser: (id: string, u: UserEdit) => api.put<SettingsUser>(`/api/v1/users/${id}`, u),
  resetPassword: (id: string, password: string) => api.post<void>(`/api/v1/users/${id}/reset-password`, { password }),
})
