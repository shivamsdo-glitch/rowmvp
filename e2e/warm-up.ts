import { chromium } from '@playwright/test'

/**
 * Loads the sign-in page once before any test runs. The Vite dev server optimises its dependencies on the first
 * request and reloads the page while it does; without this, whichever tests start first race that reload.
 */
export default async function globalSetup() {
  const browser = await chromium.launch({ channel: 'msedge' })
  try {
    const page = await browser.newPage()
    for (let attempt = 0; attempt < 3; attempt++) {
      await page.goto(`http://localhost:${process.env.E2E_WEB_PORT ?? 5174}/login`, { waitUntil: 'networkidle' })
      try {
        await page.getByRole('button', { name: 'ProjectManager', exact: true }).waitFor({ timeout: 30_000 })
        // A second load proves the optimised bundle is stable (no further reload is pending).
        await page.reload({ waitUntil: 'networkidle' })
        await page.getByRole('button', { name: 'ProjectManager', exact: true }).waitFor({ timeout: 30_000 })
        return
      } catch {
        /* the dev server reloaded under us: try again */
      }
    }
    throw new Error('The sign-in page never rendered during warm-up.')
  } finally {
    await browser.close()
  }
}
