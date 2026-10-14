/**
 * Tabs — a small primitive used in a few places (asset detail uses Syncfusion
 * TabComponent for animated underline + ARIA; this is for simpler page tabs).
 *
 * Active state = teal underline + foreground text. Inactive = muted text with
 * a hover lift. Border-bottom on the list = visual rail the underline rides.
 */
import * as React from 'react'
import { cn } from '@/lib/cn'

export const Tabs = ({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) =>
  <div className={cn('w-full', className)} {...props} />

export const TabsList = ({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) =>
  <div className={cn('relative flex gap-1 border-b border-border overflow-x-auto', className)} {...props} />

export interface TabsTriggerProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  active?: boolean
}
export const TabsTrigger = ({ className, active, ...props }: TabsTriggerProps) => (
  <button
    type="button"
    className={cn(
      'relative -mb-px px-3 py-2 text-sm font-medium transition-colors',
      active
        ? 'text-foreground after:absolute after:left-2 after:right-2 after:bottom-[-1px] after:h-[2px] after:bg-primary after:rounded-full'
        : 'text-muted-foreground hover:text-foreground',
      className,
    )}
    {...props}
  />
)

export const TabsContent = ({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) =>
  <div className={cn('mt-4 animate-fade-in', className)} {...props} />
