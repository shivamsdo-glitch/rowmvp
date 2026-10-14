import { redirect } from '@tanstack/react-router'
import type { PageKey } from '@/api/auth'
import type { AuthManager } from './auth-manager'

/** Router context: everything a guard needs. */
export interface RouterContext {
  auth: AuthManager
}

/** Signed-in users only. Sends everyone else to /login, remembering where they were going. */
export async function requireSignedIn(auth: AuthManager, pathname: string) {
  await auth.restore()
  if (auth.getState().status !== 'authenticated') {
    throw redirect({ to: '/login', search: { redirect: pathname } })
  }
}

/** Signed in AND allowed to read the page; otherwise the forbidden screen (never a blank page). */
export async function requirePageAccess(auth: AuthManager, pathname: string, page: PageKey) {
  await requireSignedIn(auth, pathname)
  if (!auth.can(page, 'read')) throw redirect({ to: '/forbidden' })
}
