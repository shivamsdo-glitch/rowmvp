import type { ReactNode } from 'react'
import { cn } from '@/lib/cn'
import { RCViewModeComponent as RCViewModeComponentRaw } from '@aurigo/react-core-component-dev/esm/components/atoms/RCViewModeComponent'
import { asReactComponent } from './_rc-adapter'

/**
 * Real RCViewModeComponent's `header`/`data` props are typed `string`-only
 * (Aurigo-ReactCoreComponent src/components/atoms/RCViewModeComponent/types.ts)
 * — there is no ReactNode overload. The separate `children` slot does accept
 * arbitrary ReactNode and is how this facade renders JSX values (e.g. a
 * <Badge>). See routing logic in ViewField below.
 */
interface RCViewModeComponentProps {
  header?: string
  data?: string
  subLabel?: string
  children?: ReactNode
}
const RCViewModeComponent = asReactComponent<RCViewModeComponentProps>(RCViewModeComponentRaw)

interface ViewFieldProps {
  label: string
  value?: ReactNode
  className?: string
  /** Rendered at the trailing edge of the value row (e.g. an ErpTag badge). */
  trailing?: ReactNode
}

/**
 * Facade around the real RCViewModeComponent (label + read-only value
 * chrome). RCViewModeComponent has no `trailing`-slot equivalent, so this
 * facade keeps ownership of the outer flex row: RCViewModeComponent renders
 * the label+value block, and `trailing` (e.g. <ErpTag />, 5 call sites in
 * assets.$id.index.tsx) is slotted in beside that block by this wrapper —
 * vertically centered against the whole label+value block rather than
 * pixel-aligned to just the value line, since RCViewModeComponent owns that
 * internal layout and doesn't expose a hook to align against the value row
 * specifically.
 *
 * Value routing: plain string/number values go through RCViewModeComponent's
 * `data` prop (the prop RC actually expects for that shape). JSX values (e.g.
 * <Badge tone={...}>) go through `children`, since `data` would only ever
 * render `[object Object]` for those. Empty/null/undefined values are also
 * routed through `children` so the existing muted-italic em dash style is
 * preserved exactly (RCViewModeComponent's own built-in empty fallback is a
 * plain, unstyled "—").
 */
export function ViewField({ label, value, className, trailing }: ViewFieldProps) {
  const empty = value === undefined || value === null || value === ''
  const isPrimitive = typeof value === 'string' || typeof value === 'number'

  const field = empty ? (
    <RCViewModeComponent header={label}>
      <span className="text-muted-foreground italic">—</span>
    </RCViewModeComponent>
  ) : isPrimitive ? (
    <RCViewModeComponent header={label} data={String(value)} />
  ) : (
    <RCViewModeComponent header={label}>{value}</RCViewModeComponent>
  )

  if (!trailing) {
    return <div className={className}>{field}</div>
  }

  return (
    <div className={cn('flex items-center justify-between gap-2', className)}>
      <div className="flex-1 min-w-0">{field}</div>
      <div className="shrink-0">{trailing}</div>
    </div>
  )
}
