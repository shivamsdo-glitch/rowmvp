import * as React from 'react'
import { cn } from '@/lib/cn'
import { AlertTriangle, Inbox } from 'lucide-react'
import { RCThreeDotLoader as RCThreeDotLoaderRaw } from '@aurigo/react-core-component-dev/esm/components/atoms/RCThreeDotLoader'
import { asReactComponent } from './_rc-adapter'

const RCThreeDotLoader = asReactComponent<{ displayText?: string; type?: string }>(RCThreeDotLoaderRaw)

/**
 * Empty, Loading, ErrorState — the three states every list/page needs.
 * They share one visual language so the app never feels half-finished.
 */

export function EmptyState({
  icon: Icon = Inbox,
  title,
  description,
  action,
  className,
}: {
  icon?: React.ComponentType<{ size?: number; className?: string }>
  title: React.ReactNode
  description?: React.ReactNode
  action?: React.ReactNode
  className?: string
}) {
  return (
    <div className={cn('flex flex-col items-center justify-center py-12 px-6 text-center', className)}>
      <div className="mb-3 inline-flex h-12 w-12 items-center justify-center rounded-full bg-muted text-muted-foreground">
        <Icon size={22} />
      </div>
      <h2 className="font-sans text-sm font-semibold text-foreground">{title}</h2>
      {description && <p className="mt-1 max-w-sm text-xs text-muted-foreground">{description}</p>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  )
}

/** Shimmer skeleton — pass width/height as classNames (e.g. 'h-4 w-32'). */
export function Skeleton({ className }: { className?: string }) {
  return (
    <div
      className={cn(
        'animate-sheen rounded-md bg-[linear-gradient(90deg,hsl(var(--muted)),hsl(var(--subtle)),hsl(var(--muted)))] bg-[length:200%_100%]',
        className,
      )}
    />
  )
}

/** Generic loading row used in tables / lists. */
export function LoadingRows({ rows = 5 }: { rows?: number }) {
  return (
    <div className="space-y-2 p-4">
      {Array.from({ length: rows }).map((_, i) => (
        <Skeleton key={i} className="h-8 w-full" />
      ))}
    </div>
  )
}

/**
 * Loading indicator — the real Aurigo RCThreeDotLoader (structure + wave
 * animation), colored via --primary so it stays correct per brand.
 * RCThreeDotLoader has no size prop; `dotloader-sm` is a local CSS modifier
 * (see index.css) so PageLoader (full-page) and SectionLoader (inline) keep
 * their own size distinction. RCThreeDotLoader only destructures
 * displayText/type — it doesn't spread a className, so the size modifier has
 * to come from a wrapping element instead.
 */
function DotLoader({ size = 'md', label }: { size?: 'sm' | 'md'; label?: string }) {
  return (
    <div className={cn(size === 'sm' && 'dotloader-sm')}>
      {/* RCThreeDotLoader renders `dotloader ${type}` verbatim — pass '' so it
          doesn't literally render the string "undefined" into the class list. */}
      <RCThreeDotLoader displayText={label ?? 'Loading...'} type="" />
    </div>
  )
}

/** Full-page loading state — centred RCThreeDotLoader with optional label. */
export function PageLoader({ label }: { label?: string } = {}) {
  return (
    <div className="flex flex-col items-center justify-center min-h-[70vh]">
      <DotLoader size="md" label={label} />
    </div>
  )
}

/** Section-level loading state — smaller RCThreeDotLoader, used inside cards/tabs. */
export function SectionLoader({ label }: { label?: string } = {}) {
  return (
    <div className="flex flex-col items-center justify-center min-h-[50vh]">
      <DotLoader size="sm" label={label} />
    </div>
  )
}

export function ErrorState({
  title = 'Something went wrong',
  description,
  retry,
  className,
}: {
  title?: React.ReactNode
  description?: React.ReactNode
  retry?: () => void
  className?: string
}) {
  return (
    <div className={cn('flex flex-col items-center justify-center py-10 px-6 text-center', className)}>
      <div className="mb-3 inline-flex h-12 w-12 items-center justify-center rounded-full bg-danger/10 text-danger">
        <AlertTriangle size={22} />
      </div>
      <h2 className="font-sans text-sm font-semibold text-foreground">{title}</h2>
      {description && <p className="mt-1 max-w-sm text-xs text-muted-foreground">{description}</p>}
      {retry && (
        <button
          type="button"
          onClick={retry}
          className="mt-4 text-xs font-medium text-primary hover:underline"
        >
          Try again
        </button>
      )}
    </div>
  )
}
