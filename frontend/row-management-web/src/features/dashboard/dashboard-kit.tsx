import type { ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import type { Tone } from '@/api/dashboard'
import { cn } from '@/lib/cn'

export const TONE_TEXT: Record<Tone, string> = { danger: 'text-danger', warn: 'text-foreground', info: 'text-primary' }
export const TONE_PILL: Record<Tone, string> = { danger: 'bg-danger/15 text-danger', warn: 'bg-warn/25 text-foreground', info: 'bg-primary-soft text-primary' }

/** A big number with a plain label, the way a tile reads at a glance. A tile with a link opens the screen behind it. */
export function KpiTile({ label, value, hint, tone = 'info', to }: { label: string; value: ReactNode; hint?: string; tone?: Tone; to?: string }) {
  const body = (
    <>
      <div className="eyebrow">{label}</div>
      <div className={cn('mt-1 text-3xl font-semibold leading-none tabular-nums', TONE_TEXT[tone])} data-testid={`tile-${label}`}>{value}</div>
      {hint && <div className="mt-2 text-xs text-muted-foreground">{hint}</div>}
    </>
  )
  const cls = 'block rounded-lg border bg-card p-4 shadow-sm'
  return to
    ? <Link to={to as never} className={cn(cls, 'transition-shadow hover:shadow-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring')}>{body}</Link>
    : <div className={cls}>{body}</div>
}

export function Card({ title, subtitle, children, className }: { title: string; subtitle?: string; children: ReactNode; className?: string }) {
  return (
    <section aria-label={title} className={cn('rounded-lg border bg-card p-4 shadow-sm', className)}>
      <h2 className="text-sm font-semibold">{title}</h2>
      {subtitle && <p className="mb-3 text-xs text-muted-foreground">{subtitle}</p>}
      {!subtitle && <div className="mb-3" />}
      {children}
    </section>
  )
}

