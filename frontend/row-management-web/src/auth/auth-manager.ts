import { authApi, type CurrentUser, type PageKey, type PagePermission, type PermissionAction, type Role } from '@/api/auth'
import { ApiError, createApiClient, type ApiClient } from '@/api/client'

export type AuthStatus = 'anonymous' | 'authenticated'

export interface AuthState {
  status: AuthStatus
  token: string | null
  user: CurrentUser | null
  permissions: PagePermission[]
  /** True when the session ended because the server stopped accepting the token (not a deliberate sign-out). */
  expired?: boolean
}

const STORAGE_KEY = 'row.auth.token'
const ANONYMOUS: AuthState = { status: 'anonymous', token: null, user: null, permissions: [] }

// Session storage so a closed tab signs out; every access is guarded because storage can be blocked.
function readStoredToken(): string | null {
  try {
    return sessionStorage.getItem(STORAGE_KEY)
  } catch {
    return null
  }
}
function writeStoredToken(token: string | null) {
  try {
    if (token) sessionStorage.setItem(STORAGE_KEY, token)
    else sessionStorage.removeItem(STORAGE_KEY)
  } catch {
    // storage unavailable: the session just won't survive a reload
  }
}

/**
 * Owns the signed-in session outside React so router guards can use it. React reads it through
 * `useAuth` (useSyncExternalStore). The server stays the authority: this only decides what to show.
 */
export class AuthManager {
  private state: AuthState = ANONYMOUS
  private listeners = new Set<() => void>()
  private restoring: Promise<void> | null = null
  readonly api: ApiClient

  constructor(options: { baseUrl?: string; fetchImpl?: typeof fetch } = {}) {
    this.api = createApiClient({
      baseUrl: options.baseUrl,
      fetchImpl: options.fetchImpl,
      getToken: () => this.state.token ?? readStoredToken(),
      onUnauthorized: () => this.signOut(true),
    })
  }

  subscribe = (listener: () => void) => {
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }

  getState = () => this.state

  private set(next: AuthState) {
    this.state = next
    this.listeners.forEach((l) => l())
  }

  /** Re-establishes a stored session once per page load. Safe to await from every route guard. */
  restore(): Promise<void> {
    this.restoring ??= (async () => {
      const token = readStoredToken()
      if (!token || this.state.status === 'authenticated') return
      try {
        await this.load(token)
      } catch (e) {
        if (e instanceof ApiError && (e.status === 401 || e.status === 403)) this.signOut()
        else this.restoring = null // server unreachable right now: keep the token and try again on the next navigation
      }
    })()
    return this.restoring
  }

  /** Development login: asks the API's dev-token endpoint (404s outside Development) for a role. */
  async signInAsDevRole(role: Role) {
    const issued = await authApi(this.api).devToken(role)
    writeStoredToken(issued.token)
    await this.load(issued.token, {
      id: issued.userId,
      fullName: `${role} (dev)`,
      initials: role.slice(0, 2).toUpperCase(),
      email: '',
      roles: issued.roles,
      tenantId: issued.tenantId,
      tenantName: null,
    })
  }

  private async load(token: string, fallbackUser?: CurrentUser) {
    // Set the token first so the next calls are authenticated.
    this.state = { ...this.state, token }
    const api = authApi(this.api)
    const [user, permissions] = await Promise.all([
      api.me().catch((e) => {
        // A dev token's user may not exist as a row; the token's own claims are enough to show the shell.
        if (fallbackUser) return fallbackUser
        throw e
      }),
      api.permissions(),
    ])
    this.set({ status: 'authenticated', token, user, permissions })
  }

  signOut(expired = false) {
    writeStoredToken(null)
    this.restoring = null
    this.set(expired ? { ...ANONYMOUS, expired: true } : ANONYMOUS)
  }

  hasRole(...roles: string[]) {
    return !!this.state.user?.roles.some((r) => roles.includes(r))
  }

  /** Whether the user may do `action` on `page` (read by default). */
  can(page: PageKey | string, action: PermissionAction = 'read') {
    const p = this.state.permissions.find((x) => x.pageKey === page)
    if (!p) return false
    return { create: p.canCreate, read: p.canRead, update: p.canUpdate, delete: p.canDelete }[action]
  }
}
