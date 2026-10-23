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
  { label: 'Title & Survey', page: 'title', built: true },
  { label: 'Appraisal', page: 'appraisals', built: true },
  { label: 'Negotiation & Closing', page: 'negotiations', built: true },
  { label: 'Relocations', page: 'relocations', built: true },
  { label: 'Configuration', page: 'administration', built: true },
  { label: 'Settings', page: 'administration', built: true },
  { label: 'Library', page: 'library', built: true },
]

/** Rarely used form sections start folded (ADR 0028); the tests fill whole forms, so unfold them as they appear. */
async function unfoldFormSections(page: Page) {
  await page.addInitScript(() => {
    const unfold = () => document.querySelectorAll('details:not([open])').forEach((d) => { (d as HTMLDetailsElement).open = true })
    new MutationObserver(unfold).observe(document, { childList: true, subtree: true })
  })
}

/**
 * Signs in the way the login screen does, minus the screen (fast path): asks the dev-token endpoint for the role's token (what the
 * screen's button calls) and hands it to the tab's session before the app loads. Most tests are about something after
 * sign-in, and the screen itself costs seconds per test; login.spec.ts and signInViaLoginScreen keep the screen covered.
 * OPT-IN (E2E_FAST_LOGIN=1): measured no faster on the full run, and it broke tracing of page.request on some runs
 * (ENOENT in test-results/.playwright-artifacts), so the default stays the real login screen.
 */
export async function signInViaUi(page: Page, role: string) {
  if (!process.env.E2E_FAST_LOGIN) return signInViaLoginScreen(page, role)
  await unfoldFormSections(page)
  const token = await devToken(page.request, role)
  // Seed once per tab (sessionStorage survives reloads), so a later sign-out or reload is not undone by this script.
  await page.addInitScript((t) => {
    if (!sessionStorage.getItem('e2e.seeded')) {
      sessionStorage.setItem('row.auth.token', t)
      sessionStorage.setItem('e2e.seeded', '1')
    }
  }, token)
  for (let attempt = 1; attempt <= 3; attempt++) {
    await page.goto('/')
    // The Vite dev server can answer a page load with a blank page while it re-optimises dependencies under load; load again.
    if (await page.getByRole('heading', { name: 'Dashboard' }).waitFor({ timeout: attempt < 3 ? 20_000 : 60_000 }).then(() => true, () => false)) break
  }
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
}

/** Signs in through the real login screen, exactly as a person would. */
export async function signInViaLoginScreen(page: Page, role: string) {
  // The Vite dev server can answer a page load with a blank page while it re-optimises dependencies under load; load again
  // (up to three times) instead of failing a test that is about something else. A page that never renders still fails below.
  await unfoldFormSections(page)
  const pick = page.getByRole('button', { name: role, exact: true })
  for (let attempt = 1; attempt <= 3; attempt++) {
    await page.goto('/login')
    if (await pick.waitFor({ timeout: attempt < 3 ? 20_000 : 60_000 }).then(() => true, () => false)) break
  }
  await pick.click()
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
