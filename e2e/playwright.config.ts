import { defineConfig } from '@playwright/test'

/**
 * End-to-end acceptance tests: a real browser (installed Microsoft Edge, no download needed), the real
 * Vite app, the real API and a throwaway real Postgres database (`row_e2e`, recreated every run).
 * Ports differ from the dev servers (5100/5173) so both can run at once. Another project's server, or a second chat's gate, may hold these
 * ports or the database: set E2E_API_PORT, E2E_WEB_PORT and E2E_DB to run beside it (defaults 5101, 5174, row_e2e).
 */
const API = Number(process.env.E2E_API_PORT ?? 5101)
const WEB = Number(process.env.E2E_WEB_PORT ?? 5174)
const DB = process.env.E2E_DB ?? 'row_e2e'
export const PG = process.env.E2E_PG ?? 'Host=127.0.0.1;Port=5434;Username=row;Password=row_dev_only'

export default defineConfig({
  testDir: './tests',
  globalSetup: './warm-up.ts',
  fullyParallel: true,
  workers: 4,
  retries: 0, // a retry would hide a flaky test; flakiness is a defect to fix
  forbidOnly: !!process.env.CI,
  timeout: 90_000, // the role-matrix specs now run for eleven roles, so the shared API is busier
  expect: { timeout: 10_000 },
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: `http://localhost:${WEB}`,
    channel: 'msedge',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
  },
  // The plan-review gate and the Program view settings are settings for the whole agency, so their scenarios run alone, after every other spec.
  projects: [
    { name: 'main', testIgnore: /(gate|program-settings)\.spec\.ts/ },
    { name: 'gate', testMatch: /(gate|program-settings)\.spec\.ts/, dependencies: ['main'] },
  ],
  webServer: [
    {
      command: 'node start-api.mjs', // resets the row_e2e database, then runs the API on port 5101
      url: `http://localhost:${API}/health`,
      timeout: 300_000,
      reuseExistingServer: false,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ConnectionStrings__Postgres: `${PG};Database=${DB};Maximum Pool Size=20`,
        E2E_API_PORT: String(API),
        E2E_DB: DB,
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
