import { expect, type APIRequestContext, type Page } from '@playwright/test'
import contract from '../../vault/acceptance/permission-matrix.json' with { type: 'json' }

export const ROLES = contract.roles as string[]
export const PAGES = contract.pages as string[]
export type Level = 'B' | 'U' | 'S' | 'P' | null
export const level = (role: string, page: string) => (contract.matrix as Record<string, Record<string, Level>>)[role][page]
export const canRead = (role: string, page: string) => level(role, page) !== null
export const canUpdate = (role: string, page: string) => ['U', 'S', 'P'].includes(level(role, page) ?? '')
export const canDelete = (role: string, page: string) => level(role, page) === 'P'
/** Which page's permissions govern attachments on each kind of record. */
export const attachmentPage = contract.attachmentPages as Record<string, string>

/** The sidebar as the product defines it. `built` = a real screen exists today (else shown as "coming soon"). */
export const NAV = [
  { label: 'Home', page: 'dashboard', built: true },
  { label: 'Projects', page: 'projects', built: true },
  { label: 'Parcels', page: 'parcels', built: true },
  { label: 'Parties', page: 'parties', built: true },
  { label: 'Requests', page: 'approvals', built: true },
  { label: 'Rights of entry', page: 'parcels', built: true },
  { label: 'Title & Survey', page: 'title', built: false },
  { label: 'Appraisal', page: 'appraisals', built: false },
  { label: 'Negotiation & Closing', page: 'negotiations', built: false },
  { label: 'Configuration', page: 'administration', built: true },
  { label: 'Settings', page: 'administration', built: true },
  { label: 'Library', page: 'library', built: true },
]

/** Signs in through the real login screen, exactly as a person would. */
export async function signInViaUi(page: Page, role: string) {
  await page.goto('/login')
  await page.getByRole('button', { name: role, exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
}

export async function openAccountMenu(page: Page) {
  await page.getByRole('button', { name: 'Account' }).click()
}

export async function signOutViaUi(page: Page) {
  await openAccountMenu(page)
  await page.getByRole('button', { name: 'Log Out' }).click()
  await expect(page).toHaveURL(/\/login/)
}

/** The token the signed-in browser session holds: lets a test act as the same person against the API. */
export async function sessionToken(page: Page) {
  const token = await page.evaluate(() => sessionStorage.getItem('row.auth.token'))
  expect(token, 'the browser session should hold a token').toBeTruthy()
  return token!
}

/** A fresh dev token for a role (optionally a specific user or agency), fetched the way the login screen does. */
export async function devToken(request: APIRequestContext, role: string, opts: { user?: string; tenant?: string } = {}) {
  const q = new URLSearchParams({ role })
  if (opts.user) q.set('user', opts.user)
  if (opts.tenant) q.set('tenant', opts.tenant)
  const res = await request.get(`/api/v1/auth/dev-token?${q}`)
  expect(res.ok()).toBeTruthy()
  return (await res.json()).token as string
}

export const newId = () => crypto.randomUUID()

/** The documents API as a person with `token` would use it. */
export function documents(request: APIRequestContext, token: string) {
  const headers = { Authorization: `Bearer ${token}` }
  return {
    upload: (entityType: string, entityId: string, name: string, content: string | Buffer, docType?: string) =>
      request.post('/api/v1/documents', {
        headers,
        multipart: {
          entityType, entityId, ...(docType ? { docType } : {}),
          file: { name, mimeType: 'application/octet-stream', buffer: Buffer.from(content) },
        },
      }),
    list: (entityType: string, entityId: string) => request.get(`/api/v1/documents?entityType=${entityType}&entityId=${entityId}`, { headers }),
    download: (id: string) => request.get(`/api/v1/documents/${id}/content`, { headers }),
    remove: (id: string) => request.delete(`/api/v1/documents/${id}`, { headers }),
  }
}

export function savedFilters(request: APIRequestContext, token: string) {
  const headers = { Authorization: `Bearer ${token}` }
  return {
    create: (pageKey: string, name: string, filter: object = {}) =>
      request.post('/api/v1/saved-filters', { headers, data: { pageKey, name, filterJson: JSON.stringify(filter) } }),
    list: (pageKey: string) => request.get(`/api/v1/saved-filters?pageKey=${pageKey}`, { headers }),
    remove: (id: string) => request.delete(`/api/v1/saved-filters/${id}`, { headers }),
  }
}

export async function jsonOf(res: { json(): Promise<any> }) {
  return res.json()
}
