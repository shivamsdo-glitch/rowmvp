#!/usr/bin/env node
// The release gate. Runs every layer of the test pyramid and stops at the first failure.
//   node scripts/verify.mjs                full gate (before merge / end of a phase)
//   node scripts/verify.mjs --quick        inner loop: BUILT.md check, backend build + unit tests, frontend type-check + tests
//                                          related to the changed files. No Postgres, no browser. Typically 1-3 minutes.
//   node scripts/verify.mjs --changed      --quick plus the E2E specs that match the changed files (needs Docker + Edge)
//   node scripts/verify.mjs --e2e=<filter> full gate, but the browser run is limited to spec files/titles matching <filter>
//                                          (anything `playwright test` accepts, e.g. --e2e=parcel-title or --e2e=AT-TE-10)
//   node scripts/verify.mjs --skip-e2e     everything except the browser run
//   node scripts/verify.mjs --serial       run the backend and frontend tracks one after the other (low-memory machines)
//   node scripts/verify.mjs --skip-built-check   skip the "vault/BUILT.md was updated" check (pure bug fixes and refactors only)
//
// Needs (full gate): Docker Desktop running (Postgres), .NET 8 SDK, Node, and Microsoft Edge for the E2E layer.
// The backend track (build, unit, integration) and the frontend track (type-check, tests, build) are independent, so they run
// side by side; the browser run needs both, so it goes last.
import { spawn, spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { existsSync, readdirSync } from 'node:fs'
import { dirname, resolve } from 'node:path'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const backend = resolve(root, 'backend/Aurigo.RowManagement')
const web = resolve(root, 'frontend/row-management-web')
const e2e = resolve(root, 'e2e')
const args = process.argv.slice(2)
const flag = (f) => args.includes(f)
const e2eFilter = args.find((a) => a.startsWith('--e2e='))?.slice('--e2e='.length)
const changedMode = flag('--changed')
const quick = flag('--quick') || changedMode
const skipE2e = flag('--skip-e2e') || (quick && !changedMode)
const serial = flag('--serial')
const skipBuiltCheck = flag('--skip-built-check')
const isWin = process.platform === 'win32'
const npm = isWin ? 'npm.cmd' : 'npm'
const npx = isWin ? 'npx.cmd' : 'npx'

// The corporate proxy re-signs HTTPS; Node needs to trust the OS certificate store.
const env = { ...process.env, NODE_OPTIONS: process.env.NODE_OPTIONS ?? '--use-system-ca', ROW_REQUIRE_PG: quick ? '' : '1' }

/** Files changed in the working tree (relative to the repo root, forward slashes). */
function changedFiles() {
  const status = spawnSync('git', ['status', '--porcelain', '--untracked-files=all'], { cwd: root, encoding: 'utf8' })
  if (status.status !== 0) return []
  return status.stdout.split(/\r?\n/).filter((l) => l.length > 3).map((l) => l.slice(3).replace(/^"|"$/g, '').split(' -> ').pop())
}
const changed = quick ? changedFiles() : []

/** E2E spec files that match the changed files: a changed spec itself, or a spec named after a changed frontend feature folder. */
function specsFor(files) {
  const specs = readdirSync(resolve(e2e, 'tests')).filter((f) => f.endsWith('.spec.ts'))
  const hit = new Set()
  for (const f of files) {
    const direct = f.match(/^e2e\/tests\/(.+\.spec\.ts)$/)
    if (direct) hit.add(direct[1])
    const feature = f.match(/^frontend\/[^/]+\/src\/features\/([^/]+)\//)?.[1]
    if (feature) {
      const stem = feature.replace(/s$/, '')
      // work/ holds the cross-parcel work screens; settings/ and library/ map to the specs of the same name
      const aliases = feature === 'work' ? ['work-pages', 'relocation', 'jobs'] : [stem]
      for (const s of specs) if (aliases.some((a) => s.startsWith(a))) hit.add(s)
    }
  }
  return [...hit]
}

const frontendQuickTests = () => {
  const src = changed.filter((f) => /^frontend\/[^/]+\/src\//.test(f)).map((f) => f.replace(/^frontend\/[^/]+\//, ''))
  return src.length ? ['vitest', 'related', '--run', ...src] : null
}

// Each step: [name, cmd, args, cwd]. `track` groups steps that must stay in order; tracks run side by side.
const backendTrack = [
  ['Backend builds with zero warnings', 'dotnet', ['build', '--nologo', '-warnaserror'], backend],
  ['Backend unit tests', 'dotnet', ['test', 'tests/Aurigo.RowManagement.UnitTests', '--no-build', '--nologo'], backend],
  ...(quick ? [] : [['Backend integration tests (real Postgres required)', 'dotnet', ['test', 'tests/Aurigo.RowManagement.IntegrationTests', '--no-build', '--nologo'], backend]]),
]
const quickFrontend = frontendQuickTests()
const frontendTrack = [
  ['Frontend type-check', npx, ['tsc', '--noEmit'], web],
  ...(quick
    ? quickFrontend ? [['Frontend tests (related to the change)', npx, quickFrontend, web]] : []
    : [['Frontend tests', npx, ['vitest', 'run'], web]]),
  // `npm run build` repeats the type-check above, so the gate calls the bundler directly.
  ...(quick ? [] : [['Frontend production build', npx, ['vite', 'build'], web]]),
]
const e2eSteps = []
if (!skipE2e) {
  const specs = changedMode ? specsFor(changed) : []
  if (changedMode && specs.length === 0 && !e2eFilter) {
    console.log('No E2E spec matches the changed files; skipping the browser run (use --e2e=<filter> to pick specs).')
  } else {
    if (!existsSync(resolve(e2e, 'node_modules'))) e2eSteps.push(['Install end-to-end dependencies (first run only)', npm, ['ci'], e2e])
    const filters = e2eFilter ? [e2eFilter] : specs
    e2eSteps.push(['End-to-end acceptance (real browser, API, database)', npx, ['playwright', 'test', ...filters], e2e])
  }
}
const preSteps = [
  ...(skipBuiltCheck ? [] : [['vault/BUILT.md is updated with the change', 'node', ['scripts/check-built.mjs'], root]]),
  ...(quick ? [] : [['Database is up (docker compose)', 'docker', ['compose', 'up', '-d', '--wait'], root]]),
]
if (changedMode && e2eSteps.length) preSteps.push(['Database is up (docker compose)', 'docker', ['compose', 'up', '-d', '--wait'], root])

// vitest cannot always start a worker on a busy machine (a fixed 60 s start timeout). That is the test runner failing to
// start, not a test failing, so that one error (and only that) earns a single retry.
const INFRA_RETRY = /Failed to start threads worker|Timeout waiting for worker to respond|Timeout starting threads runner/
const retryable = (name) => name.startsWith('Frontend tests')

/** Runs one step, capturing its output. Resolves with { ok, seconds, output }. */
function run([name, cmd, cmdArgs, cwd]) {
  return new Promise((done) => {
    const started = Date.now()
    const once = () =>
      new Promise((res) => {
        let output = ''
        const child = spawn(cmd, cmdArgs, { cwd, env, shell: isWin })
        child.stdout.on('data', (d) => (output += d))
        child.stderr.on('data', (d) => (output += d))
        child.on('error', (e) => res({ status: 1, output: output + String(e) }))
        child.on('close', (status) => res({ status, output }))
      })
    ;(async () => {
      let r = await once()
      if (r.status !== 0 && retryable(name) && INFRA_RETRY.test(r.output)) {
        r.output += '\n⚠ The test runner could not start a worker (a machine problem, not a failed test). Running it once more.\n'
        r = await once()
      }
      done({ name, ok: r.status === 0, seconds: Math.round((Date.now() - started) / 1000), output: r.output })
    })()
  })
}

/** Runs a track's steps in order, printing each step's output as it finishes. Stops the track at its first failure. */
async function runTrack(steps) {
  const results = []
  for (const step of steps) {
    const [name, cmd, cmdArgs, cwd] = step
    // The compiler and MSBuild servers stay resident after the backend steps and hold memory the test runner's workers need.
    if (serial && name === 'Frontend type-check') spawnSync('dotnet', ['build-server', 'shutdown'], { cwd, env, stdio: 'ignore', shell: isWin })
    console.log(`\n━━ ${name}\n   $ ${cmd} ${cmdArgs.join(' ')}   (${cwd.replace(root, '.')})`)
    const r = await run(step)
    process.stdout.write(r.output)
    console.log(`   ${r.ok ? '✅' : '❌'} ${name} (${r.seconds}s)`)
    results.push(r)
    if (!r.ok) break
  }
  return results
}

if (skipBuiltCheck) console.log('⚠ Skipping the vault/BUILT.md check (--skip-built-check). Only for a change that adds nothing to the map.')
if (quick) console.log(`Quick run: ${changed.length} changed file(s). This is the inner loop; the full gate (no flags) is still the release gate.`)

const planned = [...preSteps, ...backendTrack, ...frontendTrack, ...e2eSteps]
const results = []
const pre = await runTrack(preSteps)
results.push(...pre)
if (pre.every((r) => r.ok)) {
  if (serial) {
    const be = await runTrack(backendTrack)
    results.push(...be)
    if (be.every((r) => r.ok)) results.push(...(await runTrack(frontendTrack)))
  } else {
    const [be, fe] = await Promise.all([runTrack(backendTrack), runTrack(frontendTrack)])
    results.push(...be, ...fe)
  }
  if (results.every((r) => r.ok) && e2eSteps.length) results.push(...(await runTrack(e2eSteps)))
}

console.log('\n══════════ Release gate ══════════')
for (const r of results) console.log(`${r.ok ? '✅' : '❌'} ${r.name}  (${r.seconds}s)`)
const failed = results.find((r) => !r.ok)
if (failed || results.length < planned.length) {
  console.log(`\nNOT READY: "${failed?.name ?? 'a step before it'}" failed.`)
  process.exit(1)
}
const scope = quick ? ' (QUICK run: not the release gate)' : e2eFilter ? ' (browser run limited to the filter)' : skipE2e ? ' (browser run skipped)' : ''
console.log(`\nREADY: all ${results.length} checks passed.${scope}`)
