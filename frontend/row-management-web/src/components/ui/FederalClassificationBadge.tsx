import { Lock } from 'lucide-react'

/**
 * FederalClassificationBadge
 *
 * Renders an FHWA PM2 / PM3 federal condition classification chip.
 * Only bridge and pavement assets receive a non-null classification from
 * the backend (23 CFR 490 thresholds: Good / Fair / Poor).
 *
 * Visual design is intentionally different from the tenant-configurable
 * condition Badge (<Badge tone="...">):
 *   - rounded-full  vs  rounded-md  (shape)
 *   - border ring   vs  no border   (outline)
 *   - Lock icon     vs  no icon     (read-only federal signal)
 *
 * Hard-coded Tailwind colours are intentional — these map directly to
 * the FHWA statutory categories and must not follow the tenant's custom
 * condition palette.
 */

export type FedClassification = 'Good' | 'Fair' | 'Poor'

const COLOR_MAP: Record<FedClassification, string> = {
  Good: 'bg-green-100 text-green-800 border-green-300',
  Fair: 'bg-amber-100 text-amber-800 border-amber-300',
  Poor: 'bg-red-100 text-red-800 border-red-300',
}

export interface FederalClassificationBadgeProps {
  /** Backend-supplied federal classification. Renders nothing when null. */
  classification: FedClassification | null | undefined
  /**
   * PM2 = pavement (NHS pavement condition, HPMS metrics)
   * PM3 = bridge / tunnel condition (NBI element-level sufficiency)
   * Defaults to PM3.
   */
  program?: 'PM2' | 'PM3'
}

export function FederalClassificationBadge({
  classification,
  program = 'PM3',
}: FederalClassificationBadgeProps) {
  if (!classification) return null

  return (
    <span
      title="Federal threshold — 23 CFR 490"
      aria-label={`Federal PM classification: ${program} ${classification}`}
      className={[
        'inline-flex items-center gap-1 rounded-full border px-2 py-0.5',
        'text-[11px] font-semibold tracking-wide whitespace-nowrap',
        COLOR_MAP[classification],
      ].join(' ')}
    >
      <Lock size={10} aria-hidden className="shrink-0" />
      {program}: {classification}
    </span>
  )
}

/**
 * Derive the federal performance-measure program from an asset class name.
 * Returns null for asset classes that are not federally regulated under PM2/PM3.
 *
 * PM3 → bridge / tunnel / culvert  (23 CFR 490 Subpart D)
 * PM2 → pavement / road / highway  (23 CFR 490 Subpart C)
 */
export function getFederalProgram(className: string): 'PM2' | 'PM3' | null {
  const n = className.toLowerCase()
  if (n.includes('bridge') || n.includes('tunnel') || n.includes('culvert')) return 'PM3'
  if (
    n.includes('pavement') ||
    n.includes('road') ||
    n.includes('highway') ||
    n.includes('street') ||
    n.includes('pathway')
  )
    return 'PM2'
  return null
}
