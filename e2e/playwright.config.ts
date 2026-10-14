import { defineConfig } from '@playwright/test'

/**
 * End-to-end acceptance tests: a real browser (installed Microsoft Edge, no download needed), the real
 * Vite app, the real API and a throwaway real Postgres database (`row_e2e`, recreated every run).
 * Ports differ from the dev servers (5100/5173) so both can run at once.
 */
const API = 5101
const WEB = 5174
export const PG = process.env.E2E_PG ?? 'Host=127.0.0.1;Port=5434;Username=row;Password=row_dev_only'

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  workers: 4,
  retries: 0, // a retry would hide a flaky test; flakiness is a defect to fix
  forbidOnly: !!process.env.CI,
  timeout: 45_000,
  expect: { timeout: 10_000 },
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: `http://localhost:${WEB}`,
    channel: 'msedge',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
  },
  webServer: [
    {
      command: 'node start-api.mjs', // resets the row_e2e database, then runs the API on port 5101
      url: `http://localhost:${API}/health`,
      timeout: 300_000,
      reuseExistingServer: false,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ConnectionStrings__Postgres: `${PG};Database=row_e2e;Maximum Pool Size=20`,
        RateLimiting__AuthPermitLimit: '100000',
        RateLimiting__WritePermitLimit: '100000',
      },
    },
    {
      command: `npm run dev -- --port ${WEB} --strictPort`,
      cwd: '../frontend/row-management-web',
      url: `http://localhost:${WEB}`,
      timeout: 120_000,
      reuseExistingServer: false,
      env: { VITE_PROXY_TARGET: `http://localhost:${API}` },
    },
  ],
})
