import { Explained } from './abbr'
import * as React from 'react'
import { cn } from '@/lib/cn'
import { Link } from '@tanstack/react-router'
import { ChevronRight } from 'lucide-react'

/**
 * PageHeader — the title block that sits at the top of every page.
 *
 *   <PageHeader
 *     eyebrow="Inventory"
 *     title="Assets"
 *     subtitle="Browse, filter, and import the registry."
 *     crumbs={[{ label: 'Home', to: '/' }, { label: 'Assets' }]}
 *     actions={<Button>New asset</Button>}
 *   />
 *
 * One source of truth for page-level rhythm so every page reads the same.
 */

export interface Crumb { label: string; to?: string }

export interface PageHeaderProps {
  eyebrow?: React.ReactNode
  title: React.ReactNode
  subtitle?: React.ReactNode
  crumbs?: Crumb[]
  actions?: React.ReactNode
  className?: string
}

export function PageHeader({ eyebrow, title, subtitle, crumbs, actions, className }: PageHeaderProps) {
  return (
    <div className={cn('mb-6', className)}>
      {crumbs && crumbs.length > 0 && (
        <nav aria-label="Breadcrumb" className="mb-2 flex items-center gap-1 text-xs text-muted-foreground">
          {crumbs.map((c, i) => (
            <React.Fragment key={i}>
              {i > 0 && <ChevronRight size={12} className="text-muted-foreground/60" />}
              {c.to ? (
                <Link to={c.to as any} className="hover:text-foreground transition-colors">
                  {c.label}
                </Link>
              ) : (
                <span className="text-foreground/80">{c.label}</span>
              )}
            </React.Fragment>
          ))}
        </nav>
      )}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div className="min-w-0">
          {eyebrow && <div className="eyebrow mb-2">{eyebrow}</div>}
          <h1 className="page-title m-0">{typeof title === 'string' ? <Explained text={title} /> : title}</h1>
          {subtitle && <p className="page-subtitle">{typeof subtitle === 'string' ? <Explained text={subtitle} /> : subtitle}</p>}
        </div>
        {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
      </div>
      <div className="hairline mt-5" />
    </div>
  )
}
