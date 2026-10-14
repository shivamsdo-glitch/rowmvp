import { createContext, useContext, useSyncExternalStore, type ReactNode } from 'react'
import type { AuthManager, AuthState } from './auth-manager'

const AuthContext = createContext<AuthManager | null>(null)

export function AuthProvider({ auth, children }: { auth: AuthManager; children: ReactNode }) {
  return <AuthContext.Provider value={auth}>{children}</AuthContext.Provider>
}

export function useAuthManager(): AuthManager {
  const auth = useContext(AuthContext)
  if (!auth) throw new Error('useAuthManager must be used inside <AuthProvider>')
  return auth
}

/** The current session; re-renders when it changes. */
export function useAuth(): AuthState & { manager: AuthManager } {
  const manager = useAuthManager()
  const state = useSyncExternalStore(manager.subscribe, manager.getState)
  return { ...state, manager }
}
