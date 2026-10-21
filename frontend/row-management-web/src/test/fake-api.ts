import type { PagePermission } from '@/api/auth'
import type { MyDashboard, ProgramSettings, ProgramSettingsInput, ProgramDashboard, ProjectDashboard, TeamDashboard } from '@/api/dashboard'
import { emptyDashboard, sampleProgram, sampleSettings, sampleProjectDashboard, sampleTeam } from './fake-dashboard'

const ALL_PAGES = [
  'dashboard', 'projects', 'parcels', 'parties', 'title', 'survey', 'appraisals',
  'approvals', 'negotiations', 'agreements', 'closing', 'payments', 'administration', 'library', 'contactlog', 'easements', 'parcellocation', 'titleencumbrances', 'titleclosing', 'parcelappraisal', 'parcelnegotiation', 'parcelquestionnaire', 'parcelcondemnation', 'parcelproperty', 'parcelrelocation',
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
export function fakeAuthFetch(opts: { readable?: string[]; rows?: PagePermission[]; role?: string; token?: string; meFails?: boolean; dashboard?: MyDashboard; team?: TeamDashboard | 'forbidden'; dashboardFails?: boolean; program?: ProgramDashboard | 'forbidden' | 'fails'; project?: ProjectDashboard | 'fails'; settings?: ProgramSettings | 'fails'; onSettingsSaved?: (body: ProgramSettingsInput) => void; settingsRejects?: string }) {
  const token = opts.token ?? 'good-token'
  const role = opts.role ?? 'Admin'
  let settings = opts.settings && opts.settings !== 'fails' ? opts.settings : sampleSettings()
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
    // The dashboard: by default a person with nothing done and nothing waiting; only managers may ask for the team view.
    if (url === '/api/v1/dashboard/me') {
      if (opts.dashboardFails) return json({ title: 'server_error' }, 500)
      return json(opts.dashboard ?? emptyDashboard({ roles: [role], isManager: role === 'Admin' || role === 'ProjectManager' }))
    }
    if (url === '/api/v1/dashboard/team') {
      if (opts.team === 'forbidden' || !(role === 'Admin' || role === 'ProjectManager' || opts.team)) return json({ title: 'forbidden' }, 403)
      return json(opts.team ?? sampleTeam())
    }
    if (url === '/api/v1/dashboard/settings') {
      if (role !== 'Admin') return json({ title: 'forbidden' }, 403)
      if (init?.method === 'PUT') {
        const body = JSON.parse(String(init.body ?? '{}')) as ProgramSettingsInput
        if (opts.settingsRejects) return json({ title: 'invalid_request', detail: opts.settingsRejects }, 400)
        opts.onSettingsSaved?.(body)
        const { steps, ...rules } = body
        settings = { ...settings, rules, steps: settings.steps.map((s) => ({ ...s, team: steps.find((x) => x.stepKey === s.stepKey)?.team ?? s.defaultTeam })) }
        return json(settings)
      }
      if (init?.method === 'DELETE') { settings = sampleSettings(); return new Response(null, { status: 204 }) }
      if (opts.settings === 'fails') return json({ title: 'server_error' }, 500)
      return json(settings)
    }
    if (url === '/api/v1/dashboard/program') {
      if (opts.program === 'fails') return json({ title: 'server_error' }, 500)
      if (opts.program === 'forbidden' || !(role === 'Admin' || role === 'ProjectManager')) return json({ title: 'forbidden' }, 403)
      return json(opts.program ?? sampleProgram())
    }
    if (url.startsWith('/api/v1/dashboard/projects/')) {
      if (opts.project === 'fails') return json({ title: 'server_error' }, 500)
      return json(opts.project ?? sampleProjectDashboard())
    }
    // The dashboard's next-actions card asks for reminders; an agency with no parcels has none.
    if (url.startsWith('/api/v1/reminders')) return json([])
    return json({ title: 'not_found' }, 404)
  }
}
