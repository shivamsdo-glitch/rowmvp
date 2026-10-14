#!/usr/bin/env node
// The release gate. Runs every layer of the test pyramid in order and stops at the first failure.
//   node scripts/verify.mjs            everything
//   node scripts/verify.mjs --skip-e2e everything except the browser run (faster, for quick checks)
//
// Needs: Docker Desktop running (Postgres), .NET 8 SDK, Node, and Microsoft Edge for the E2E layer.
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { existsSync } from 'node:fs'
import { dirname, resolve } from 'node:path'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const backend = resolve(root, 'backend/Aurigo.RowManagement')
const web = resolve(root, 'frontend/row-management-web')
const e2e = resolve(root, 'e2e')
const skipE2e = process.argv.includes('--skip-e2e')
const isWin = process.platform === 'win32'
const npm = isWin ? 'npm.cmd' : 'npm'
const npx = isWin ? 'npx.cmd' : 'npx'

// The corporate proxy re-signs HTTPS; Node needs to trust the OS certificate store.
const env = { ...process.env, NODE_OPTIONS: process.env.NODE_OPTIONS ?? '--use-system-ca', ROW_REQUIRE_PG: '1' }

const steps = [
  ['Database is up (docker compose)', 'docker', ['compose', 'up', '-d', '--wait'], root],
  ['Backend builds with zero warnings', 'dotnet', ['build', '--nologo', '-warnaserror'], backend],
  ['Backend unit tests', 'dotnet', ['test', 'tests/Aurigo.RowManagement.UnitTests', '--no-build', '--nologo'], backend],
  ['Backend integration tests (real Postgres required)', 'dotnet', ['test', 'tests/Aurigo.RowManagement.IntegrationTests', '--no-build', '--nologo'], backend],
  ['Frontend type-check', npx, ['tsc', '--noEmit'], web],
  ['Frontend tests', npx, ['vitest', 'run'], web],
  ['Frontend production build', npm, ['run', 'build'], web],
  ...(skipE2e || existsSync(resolve(e2e, 'node_modules')) ? [] : [['Install end-to-end dependencies (first run only)', npm, ['ci'], e2e]]),
  ...(skipE2e ? [] : [['End-to-end acceptance (real browser, API, database)', npx, ['playwright', 'test'], e2e]]),
]

// vitest cannot always start a worker on a busy machine (a fixed 60 s start timeout). That is the test runner failing to
// start, not a test failing, so that one error (and only that) earns a single retry.
const INFRA_RETRY = { 'Frontend tests': /Failed to start threads worker|Timeout waiting for worker to respond|Timeout starting threads runner/ }

function runStep(cmd, args, cwd, name) {
  const retryOn = INFRA_RETRY[name]
  if (!retryOn) return spawnSync(cmd, args, { cwd, env, stdio: 'inherit', shell: isWin })
  for (let attempt = 1; ; attempt++) {
    const run = spawnSync(cmd, args, { cwd, env, shell: isWin, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 })
    process.stdout.write(run.stdout ?? '')
    process.stderr.write(run.stderr ?? '')
    if (run.status === 0 || attempt >= 2 || !retryOn.test(`${run.stdout}\n${run.stderr}`)) return run
    console.log(`\n⚠ The test runner could not start a worker (a machine problem, not a failed test). Running "${name}" once more.`)
  }
}

const results = []
for (const [name, cmd, args, cwd] of steps) {
  const started = Date.now()
  console.log(`\n━━ ${name}\n   $ ${cmd} ${args.join(' ')}   (${cwd.replace(root, '.')})`)
  // The compiler and MSBuild servers stay resident after the backend steps and hold memory the test runner's workers need.
  if (name === 'Frontend type-check') spawnSync('dotnet', ['build-server', 'shutdown'], { cwd, env, stdio: 'ignore', shell: isWin })
  const run = runStep(cmd, args, cwd, name)
  const seconds = Math.round((Date.now() - started) / 1000)
  results.push({ name, ok: run.status === 0, seconds })
  if (run.status !== 0) break
}

console.log('\n══════════ Release gate ══════════')
for (const r of results) console.log(`${r.ok ? '✅' : '❌'} ${r.name}  (${r.seconds}s)`)
const failed = results.find((r) => !r.ok)
if (failed || results.length < steps.length) {
  console.log(`\nNOT READY: "${failed?.name}" failed.`)
  process.exit(1)
}
console.log(`\nREADY: all ${results.length} checks passed.${skipE2e ? ' (browser run skipped)' : ''}`)
