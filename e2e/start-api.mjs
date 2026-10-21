// Starts the API for the E2E run on a brand-new empty database.
// The reset MUST happen here, before the API boots: Playwright starts web servers before globalSetup.
import { execFileSync, spawn } from 'node:child_process'

const container = process.env.E2E_PG_CONTAINER ?? 'row-mvp-postgres'
const db = process.env.E2E_DB ?? 'row_e2e'
const port = process.env.E2E_API_PORT ?? '5101'
const psql = (sql) =>
  execFileSync('docker', ['exec', container, 'psql', '-U', 'row', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1', '-c', sql], { stdio: 'pipe' })
psql(`DROP DATABASE IF EXISTS ${db} WITH (FORCE)`)
psql(`CREATE DATABASE ${db}`)

const child = spawn(
  'dotnet',
  ['run', '--project', '../backend/Aurigo.RowManagement/src/Aurigo.RowManagement.Api', '--no-launch-profile', '--urls', `http://localhost:${port}`],
  { stdio: 'inherit' },
)
child.on('exit', (code) => process.exit(code ?? 0))
for (const sig of ['SIGINT', 'SIGTERM']) process.on(sig, () => child.kill())
