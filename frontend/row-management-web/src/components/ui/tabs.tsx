/**
 * Tabs — the one tab strip used on every screen with sections (parcel, project, settings, work screens).
 *
 * Active tab = the label in the primary blue, semibold, with a full-width 2px primary border along its bottom edge that sits on the rail.
 * Inactive tabs = muted label with a transparent border of the same height (so nothing shifts), turning primary on hover.
 * The border-bottom on the list is the rail the active border rides. A long strip wraps onto more rows instead of scrolling sideways, so every tab (and the
 * selected one) stays in view on any screen width.
 */
import * as React from 'react'
import { cn } from '@/lib/cn'

export const Tabs = ({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) =>
  <div className={cn('w-full', className)} {...props} />

export const TabsList = ({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) =>
  <div className={cn('relative flex flex-wrap gap-x-1 border-b border-border', className)} {...props} />

export interface TabsTriggerProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  active?: boolean
}
export const TabsTrigger = ({ className, active, ...props }: TabsTriggerProps) => (
  <button
    type="button"
    className={cn(
      '-mb-px inline-flex shrink-0 items-center whitespace-nowrap border-b-2 px-4 py-2.5 text-sm transition-colors',
      'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1',
      active
        ? 'border-primary font-semibold text-primary'
        : 'border-transparent font-medium text-muted-foreground hover:text-primary',
      className,
    )}
    {...props}
  />
)

export const TabsContent = ({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) =>
  <div className={cn('mt-4 animate-fade-in', className)} {...props} />
