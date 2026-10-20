import type { PagePermission } from '@/api/auth'

const ALL_PAGES = [
  'dashboard', 'projects', 'parcels', 'parties', 'title', 'survey', 'appraisals',
  'approvals', 'negotiations', 'agreements', 'closing', 'payments', 'administration', 'library', 'contactlog', 'easements', 'ownership', 'parcellocation',
]

/** Permission rows for a user who can read `readable` pages and nothing else. */
export function permissionsFor(readable: string[]): PagePermission[] {
  return ALL_PAGES.map((pageKey) => {
    const r = readable.includes(pageKey)
    return { pageKey, canCreate: false, canRead: r, canUpdate: false, canDelete: false }
  })
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/**
 * A fetch stand-in for the auth endpoints. Give `readable` (pages the user can read) or full `rows`.
 * A request without the expected token is a 401, like the real API.
 */
export function fakeAuthFetch(opts: { readable?: string[]; rows?: PagePermission[]; role?: string; token?: string; meFails?: boolean }) {
  const token = opts.token ?? 'good-token'
  const role = opts.role ?? 'Admin'
  return async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    const headers = (init?.headers ?? {}) as Record<string, string>

    if (url.startsWith('/api/v1/auth/dev-token')) {
      return json({ token, tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z', roles: [role], tenantId: 't1', userId: 'u1' })
    }
    if (url === '/api/v1/auth/login' && init?.method === 'POST') {
      const body = JSON.parse(String(init.body ?? '{}')) as { username?: string; password?: string }
      if (body.username?.toLowerCase() === 'admin' && body.password === 'qwertyuiop') {
        return json({
          token, tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z',
          user: { id: 'u1', fullName: 'Admin User', initials: 'AU', email: 'admin@row.local', roles: [role], tenantId: 't1', tenantName: 'Demo DOT' },
        })
      }
      return json({ title: 'invalid_credentials', detail: 'Incorrect username or password.' }, 401)
    }
    if (headers.Authorization !== `Bearer ${token}`) return json({ title: 'unauthorized' }, 401)
    if (url === '/api/v1/auth/me') {
      if (opts.meFails) return json({ title: 'not_found' }, 404)
      return json({ id: 'u1', fullName: 'Dev User', initials: 'DU', email: 'dev@row.local', roles: [role], tenantId: 't1', tenantName: 'Demo DOT' })
    }
    if (url === '/api/v1/auth/permissions') return json(opts.rows ?? permissionsFor(opts.readable ?? []))
    // The dashboard's next-actions card asks for reminders; an agency with no parcels has none.
    if (url.startsWith('/api/v1/reminders')) return json([])
    return json({ title: 'not_found' }, 404)
  }
}
