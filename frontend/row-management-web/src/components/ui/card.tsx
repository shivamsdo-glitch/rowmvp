import * as React from 'react'
import { cn } from '@/lib/cn'
import { RCCard as RCCardRaw } from '@aurigo/react-core-component-dev/esm/components/atoms/RCCard'
import { asReactComponent } from './_rc-adapter'

/**
 * Card — facade over the real Aurigo RCCard (@aurigo/react-core-component-dev).
 * Migrated off the hand-rolled shadcn-style compound `<div>` as part of the
 * component-library migration (see states.tsx / badge.tsx / dialog.tsx for
 * the established pattern: deep-import + `asReactComponent` adapter, never
 * the package barrel).
 *
 * STRUCTURAL MISMATCH — read before touching this file:
 * RCCard's real prop contract (Aurigo-ReactCoreComponent/src/components/
 * atoms/RCCard/types.ts) is a flat two-slot shape: `headerTitle` / `content`
 * (both `string | ReactElement`), no footer slot, no compound children.
 * Every one of the ~30 call sites in this app instead composes
 * `<Card><CardHeader><CardTitle/><CardDescription/></CardHeader>
 * <CardContent>...</CardContent></Card>` freely, and a few (IntegrationCard,
 * integrations.tsx PreferredPmisCard, integrations.tsx's loading state)
 * put un-wrapped children directly under `<Card>` with no `<CardHeader>` at
 * all — real repo-wide grep found **zero** `<CardFooter>` usages; the
 * handful of "footer" regions (IntegrationCard's action-button row,
 * GhostIntegrationCard's disabled button) are already just bare `<div>`
 * siblings under `<Card>` today, not a distinct compound part.
 *
 * Rather than rewrite every call site to the flat shape (high blast radius
 * — this primitive alone touches assets.$id.index.tsx, every Job Order tab,
 * every Configuration route, the dashboard, and more), this facade keeps
 * the compound API and flattens internally at render time:
 *
 *   - The (at most one) <CardHeader> child is passed to RCCard's
 *     `headerTitle` verbatim — headerTitle accepts a ReactElement, so a
 *     header containing <CardTitle>+<CardDescription>, or the two
 *     assets.$id.index.tsx cases where a <Button> sits beside the title in
 *     a flex row (Emergency Repairs, Risk Mitigations), both survive
 *     completely unchanged.
 *   - Every other child — <CardContent>, and any bare non-header children
 *     (IntegrationCard's un-wrapped footer <div>, integrations.tsx's bare
 *     <LoadingRows/>) — is collected in source order into one Fragment
 *     passed as `content`. This reproduces today's exact DOM
 *     order/adjacency, since those "footer" divs are already ordinary
 *     Card children, not a separate compound part.
 *
 * Visual chrome ownership: RCCard always renders its own
 * `.card-control > .e-card > .card > .image-header > .card-content > ...`
 * wrapper chain (see the real Card.tsx). Rather than fight that nesting
 * with padding math, every one of those wrapper divs is reset to
 * `display: contents` in index.css — they own no box/paint of their own —
 * so <CardHeader>/<CardContent> (unchanged: still real divs with their
 * original Tailwind classes) end up laid out as if they were direct flex
 * children of `.card-control`, exactly like the pre-migration plain <div>
 * Card. `.card-control` is where the visible card box (border/bg/shadow/
 * radius) now lives, via the exact same Tailwind expression the old Card
 * div used, passed through RCCard's `cssClass` prop — so every existing
 * call-site `className` (`rounded-none`, `border-red-500`,
 * `flex flex-col relative`, `md:col-span-2`, `border-dashed opacity-60`,
 * ...) keeps working exactly as before, since it lands on the one real
 * painted box in the tree, same as it always did.
 *
 * Trade-off accepted: this `display: contents` flattening assumes a
 * text/content-only card. RCCard's `leftIcon`/`rightIcon`/`uploadStatus`/
 * `isCardWithIcons` image-flanking layout is not reproducible through this
 * facade (confirmed zero call sites in this app use it today — a future
 * icon-flanked card should deep-import RCCard directly rather than going
 * through this facade).
 *
 * RCCard defaults `id` to the literal string `"card"` when none is passed
 * (`id ? id : "card"` in the real component) — every instance in this app
 * would collide on that id without one, so this facade always generates a
 * unique id via `useId()` when the caller doesn't supply one.
 */

interface RCCardElementProps {
  headerTitle?: string | React.ReactElement
  content?: string | React.ReactElement
  onClick?: React.MouseEventHandler<HTMLDivElement>
  cssClass?: string
  id?: string
  hasNoBorder?: boolean
  leftIcon?: string | React.ReactElement
  rightIcon?: string | React.ReactElement
  uploadStatus?: string | React.ReactElement
  isCardWithIcons?: boolean
  onClickLeftIcon?: React.MouseEventHandler
  onClickRightIcon?: React.MouseEventHandler
}
const RCCard = asReactComponent<RCCardElementProps>(RCCardRaw)

type CardProps = React.HTMLAttributes<HTMLDivElement> & {
  /** No call site uses this today (verified via grep) — kept for API
   *  stability. Previously dropped only the shadow, kept the hairline
   *  border; reproduced here as the exact same conditional Tailwind class
   *  in the `cssClass` expression below (RCCard has no equivalent prop). */
  flat?: boolean
}

export function Card({ className, flat, onClick, id, children }: CardProps) {
  const autoId = React.useId()

  let headerEl: React.ReactElement | undefined
  const others: React.ReactNode[] = []
  React.Children.forEach(children, (child) => {
    if (React.isValidElement(child) && child.type === CardHeader) {
      headerEl = child
    } else {
      others.push(child)
    }
  })
  const content = others.length > 0 ? <>{others}</> : undefined

  return (
    <RCCard
      headerTitle={headerEl}
      content={content}
      cssClass={cn(
        'rounded-lg bg-card text-card-foreground border border-border',
        flat ? '' : 'shadow-aurigo-card',
        className,
      )}
      onClick={onClick}
      id={id ?? autoId}
    />
  )
}

/**
 * CardHeader pairs with CardContent. Bottom padding is intentionally small (pb-2/pb-3) so the
 * combo CardHeader + CardContent (now symmetric, pt full) reads with sensible spacing between
 * the title block and the content body — without falling off a cliff when CardContent is used
 * alone (no header above).
 *
 * Unchanged from the pre-migration implementation — Card (above) passes this
 * element straight through to RCCard's `headerTitle` slot verbatim, so it
 * still needs to carry its own layout/spacing classes.
 */
export const CardHeader = React.forwardRef<HTMLDivElement, React.HTMLAttributes<HTMLDivElement>>(
  ({ className, ...props }, ref) => (
    <div ref={ref} className={cn('flex flex-col gap-1.5 px-5 pt-5 pb-2 sm:px-6 sm:pt-6 sm:pb-3', className)} {...props} />
  )
)
CardHeader.displayName = 'CardHeader'

export const CardTitle = React.forwardRef<HTMLDivElement, React.HTMLAttributes<HTMLHeadingElement>>(
  ({ className, ...props }, ref) => (
    <h3
      ref={ref as any}
      className={cn('font-serif text-[1.05rem] leading-tight tracking-tight m-0', className)}
      {...props}
    />
  )
)
CardTitle.displayName = 'CardTitle'

export const CardDescription = React.forwardRef<HTMLDivElement, React.HTMLAttributes<HTMLParagraphElement>>(
  ({ className, ...props }, ref) => (
    <p ref={ref as any} className={cn('text-[13px] text-muted-foreground m-0 leading-relaxed', className)} {...props} />
  )
)
CardDescription.displayName = 'CardDescription'

/**
 * CardContent is symmetric by default (full padding on every side). The old shadcn shape was
 * `pt-0` so it could butt up against a CardHeader, but in practice many of our cards use
 * CardContent alone (KPIs, section bodies) and that collapsed the top padding, jamming the
 * first row of text against the card border. When a CardHeader sits above, the extra top
 * padding here is fine — the visual rhythm is intentional. Pass `tight` for the legacy
 * "no top padding" behavior when you really do need content butted to a header.
 *
 * Unchanged from the pre-migration implementation — see CardHeader comment above.
 */
type CardContentProps = React.HTMLAttributes<HTMLDivElement> & { tight?: boolean }
export const CardContent = React.forwardRef<HTMLDivElement, CardContentProps>(
  ({ className, tight, ...props }, ref) => (
    <div ref={ref} className={cn(tight ? 'p-5 pt-0 sm:p-6 sm:pt-0' : 'p-5 sm:p-6', className)} {...props} />
  )
)
CardContent.displayName = 'CardContent'

/**
 * No current call site uses <CardFooter> (verified via repo-wide grep) —
 * RCCard has no footer slot to map it onto. Kept exported, unchanged, for
 * API stability: since Card (above) folds any non-<CardHeader> child into
 * `content` in source order, a future <CardFooter> user would render
 * correctly (right after <CardContent>, matching today's un-wrapped
 * "footer div" call sites like IntegrationCard) without any change here.
 */
export const CardFooter = React.forwardRef<HTMLDivElement, React.HTMLAttributes<HTMLDivElement>>(
  ({ className, ...props }, ref) => (
    <div ref={ref} className={cn('flex items-center p-5 pt-0 sm:p-6 sm:pt-0', className)} {...props} />
  )
)
CardFooter.displayName = 'CardFooter'
