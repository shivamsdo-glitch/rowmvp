import * as React from 'react'
import { cn } from '@/lib/cn'
import { RCBadge as RCBadgeRaw } from '@aurigo/react-core-component-dev/esm/components/atoms/RCBadge'
import { asReactComponent } from './_rc-adapter'

/**
 * Badge — quiet status pill, backed by the real RCBadge
 * (@aurigo/react-core-component-dev). Used for condition grades, RUL bands,
 * risk bands, inspection status, role tags, etc. Single source of truth so
 * every page speaks the same color language.
 *
 *   • soft    — pale background + saturated text   (default for most pills)
 *   • solid   — saturated background + white text  (high-emphasis indicators)
 *   • outline — bordered, transparent bg            (read-only / inactive)
 *
 * RCBadge only exposes 7 `bg` values (primary/dark/light/success/danger/
 * warning/info) but this app has 8 tones × 3 variants. `bg` below is only
 * the closest semantic match — passed through so RCBadge renders a real
 * `e-badge-{bg}` class for structural fidelity. The actual pixel color
 * always comes from the `badge-tone-*`/`badge-{variant}` CSS classes added
 * via `className` (see index.css), which are 4-class-specificity compound
 * selectors that always beat RCBadge's own 3-class `.e-badge-{bg}` rule —
 * that's what lets `alert` and `warn` (both mapped to bg="warning", since
 * RCBadge has no dedicated "alert" slot) still render as two visually
 * distinct colors instead of colliding.
 */

type Tone =
  | 'neutral'
  | 'primary'
  | 'ok'
  | 'warn'
  | 'alert'
  | 'danger'
  | 'info'
  | 'accent'

type Variant = 'soft' | 'solid' | 'outline'

export interface BadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  tone?: Tone
  variant?: Variant
}

type RCBadgeBg = 'primary' | 'dark' | 'light' | 'success' | 'danger' | 'warning' | 'info'

// Closest real RCBadge `bg` per app tone — see file header for why exact
// color fidelity doesn't actually depend on this mapping being 1:1.
const TONE_TO_BG: Record<Tone, RCBadgeBg> = {
  neutral: 'light',
  primary: 'primary',
  ok:      'success',
  warn:    'warning',
  alert:   'warning', // no distinct "alert" bg in RCBadge; closest severity slot
  danger:  'danger',
  info:    'info',
  accent:  'dark',     // no gold/amber bg in RCBadge; arbitrary — color comes from badge-tone-accent
}

// RCBadge's real prop contract (types.d.ts) destructures only these named
// props — it does not spread a `...rest` onto the rendered element, so any
// other HTMLAttributes we forward (aria-*, data-*, onClick, style, ...) are
// silently ignored by RCBadge itself, not by us. `ref` is added here (not in
// the real IBadgeProps) purely so this app's React 19 "ref as prop" typing
// lets us pass a ref through to the underlying DOM node — RCBadge is a real
// forwardRef component at runtime regardless of how we type the cast.
interface RCBadgeElementProps {
  as?: React.ElementType
  type?: 'text' | 'dot' | 'number'
  bg?: RCBadgeBg
  children?: React.ReactNode
  id?: string
  isNotification?: boolean
  className?: string
  ref?: React.Ref<HTMLSpanElement>
}

const RCBadge = asReactComponent<RCBadgeElementProps>(RCBadgeRaw)

export const Badge = React.forwardRef<HTMLSpanElement, BadgeProps>(
  ({ className, tone = 'neutral', variant = 'soft', title, children, id, ...rest }, forwardedRef) => {
    // RCBadge's own implementation always sets `title={children}` on the DOM
    // node and has no prop for a custom tooltip (see IBadgeProps) — several
    // real call sites (ProvenanceChip, PushStatusChip, hazard-risk badges on
    // the asset detail page) pass a `title` that's intentionally *different*
    // from the visible label (e.g. a detailed push-failure reason vs. the
    // short "Push failed" text). Naively dropping `title` would silently
    // break those tooltips, and RCBadge would still render its own
    // title=children in its place. Patch the DOM node's title attribute via
    // the ref, after RCBadge's own render has applied title=children for
    // this commit, so our explicit title always wins.
    //
    // This callback is intentionally NOT memoized: it's re-created (and thus
    // re-invoked by React with null-then-node) on every render so the patch
    // is reliably re-applied even when only `children` changes but `title`
    // does not. Badge is a cheap leaf node with no ref-teardown side effects,
    // so the extra ref churn is a non-issue.
    const setRef = (node: HTMLSpanElement | null) => {
      if (node) {
        if (title != null) node.setAttribute('title', String(title))
        else node.removeAttribute('title')
        // RCBadge drops every prop it doesn't explicitly destructure — not just
        // `title`. `data-*`/`aria-*` attributes (e.g. IntegrationCard's
        // data-testid="integration-status-pill", aria-label) are real call-site
        // usages that silently never reach the DOM otherwise. Patch them on
        // directly, same technique as the title fix above.
        for (const [key, value] of Object.entries(rest)) {
          if (key.startsWith('data-') || key.startsWith('aria-')) {
            if (value == null) node.removeAttribute(key)
            else node.setAttribute(key, String(value))
          }
        }
      }
      if (typeof forwardedRef === 'function') forwardedRef(node)
      else if (forwardedRef) (forwardedRef as React.RefObject<HTMLSpanElement | null>).current = node
    }

    return (
      <RCBadge
        ref={setRef}
        bg={TONE_TO_BG[tone]}
        id={id}
        className={cn(`badge-${variant}`, `badge-tone-${tone}`, className)}
        {...rest}
      >
        {children}
      </RCBadge>
    )
  }
)
Badge.displayName = 'Badge'

/* Map our app-wide grade / RUL band / risk band labels to Badge tones, so callers
   stop hand-rolling colors and one change here re-skins every page. */

export const GRADE_TONE: Record<string, Tone> = {
  Excellent: 'ok',
  Good:      'ok',
  Fair:      'warn',
  Poor:      'alert',
  Critical:  'danger',
}

export const RUL_BAND_TONE = ['ok', 'warn', 'alert', 'danger'] as const

export const RISK_BAND_TONE: Record<string, Tone> = {
  Low:      'ok',
  Med:      'warn',
  Medium:   'warn',
  High:     'alert',
  Critical: 'danger',
}

export const INSPECTION_STATUS_TONE: Record<string, Tone> = {
  Draft:      'neutral',
  InProgress: 'info',
  Submitted:  'info',
  Approved:   'ok',
  Rejected:   'danger',
}
