import { createRouter, type RouterHistory } from '@tanstack/react-router'
import type { AuthManager } from '@/auth/auth-manager'
import { routeTree } from './routeTree.gen'

export function createAppRouter(auth: AuthManager, history?: RouterHistory) {
  return createRouter({ routeTree, history, context: { auth }, defaultPreload: 'intent' })
}

declare module '@tanstack/react-router' {
  interface Register {
    router: ReturnType<typeof createAppRouter>
  }
}
