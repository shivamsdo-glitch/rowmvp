#!/usr/bin/env node
// Keeps vault/BUILT.md honest. Run as the first step of the release gate (scripts/verify.mjs).
//
// RULE: a change to the product code (backend/*/src, frontend/*/src) must come with an update to vault/BUILT.md in the same commit,
// so a new chat can always read what exists. Tests, e2e specs, generated files and docs alone do not need it.
//
// This looks at the working tree (what is about to be committed). It passes when nothing in the product code changed, or when
// vault/BUILT.md changed too. For a change that genuinely adds nothing to the map (a pure bug fix or refactor), run the gate with
// --skip-built-check and say so in the commit message.
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

const BUILT = 'vault/BUILT.md'
const PRODUCT = /^(backend\/[^/]+\/src\/|frontend\/[^/]+\/src\/)/
const NOT_FEATURE = /(\.test\.|\.spec\.|\/test\/|\/bin\/|\/obj\/|routeTree\.gen\.ts$)/

/** The paths in `git status --porcelain` output (a rename is reported by its new path). */
export function pathsOf(porcelain) {
  return porcelain.split(/\r?\n/).filter((l) => l.length > 3).map((l) => l.slice(3).replace(/^"|"$/g, '').split(' -> ').pop())
}

/** The product files in `paths` that changed without BUILT.md changing; empty when the rule is satisfied. */
export function unrecorded(paths) {
  if (paths.includes(BUILT)) return []
  return paths.filter((p) => PRODUCT.test(p) && !NOT_FEATURE.test(p))
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
  const status = spawnSync('git', ['status', '--porcelain', '--untracked-files=all'], { cwd: root, encoding: 'utf8' })
  if (status.status !== 0) {
    console.log('Could not read git status; skipping the BUILT.md check.')
    process.exit(0)
  }
  const missing = unrecorded(pathsOf(status.stdout))
  if (missing.length === 0) {
    console.log(`OK: ${BUILT} is up to date with the change (or no product code changed).`)
    process.exit(0)
  }
  console.log(`\n${BUILT} was not updated, but product code changed:`)
  for (const p of missing.slice(0, 15)) console.log(`  - ${p}`)
  if (missing.length > 15) console.log(`  ... and ${missing.length - 15} more`)
  console.log(`\nUpdate ${BUILT} in this change: add or amend the feature, screen, route, page key, ADR link or "not built" note, and bump "Last updated".`)
  console.log('A pure bug fix or refactor that adds nothing to the map may run the gate with --skip-built-check (say so in the commit message).')
  process.exit(1)
}
