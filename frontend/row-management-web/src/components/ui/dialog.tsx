/**
 * Dialog — facade over the real Aurigo RCModal (Syncfusion DialogComponent),
 * migrated off @radix-ui/react-dialog as part of the component-library
 * migration (see states.tsx / RCThreeDotLoader for the established pattern:
 * deep-import + `asReactComponent` adapter, never the package barrel).
 *
 * WCAG 2.1 baseline this file must preserve — previously provided by Radix,
 * re-verified here against the real, installed
 * @syncfusion/ej2-popups/src/dialog/dialog.js source (not taken on faith):
 *
 *   - role="dialog" + aria-modal="true"            (SC 4.1.2)
 *       dialog.js:476, 1257, 1325 — `attributes(this.element, { 'aria-modal':
 *       (this.isModal ? 'true' : 'false') })`. RCModal defaults isModal=true
 *       and we pass it explicitly below.
 *   - Escape key closes                             (SC 2.1.2)
 *       dialog.js:395-401 — `if (event.keyCode === 27 && this.closeOnEscape)
 *       { ...; this.hide(event) }`. `closeOnEscape` defaults to `true`
 *       (dialog-model.d.ts:266-268) and we don't override it.
 *   - Focus trap while open                         (SC 2.1.2)
 *       dialog.js:363-388 — Tab/Shift+Tab handling wraps focus between the
 *       first focusable element and the last footer button when `isModal`.
 *   - Auto-focus on open + focus-restore on close   (SC 2.4.3)
 *       dialog.js:1471 stores `document.activeElement` on open;
 *       dialog.js:528-534 blurs the current element and calls
 *       `storeActiveElement.focus()` on close.
 *   - aria-labelledby wired to the title            (SC 4.1.2)
 *       dialog.js:882 wires this automatically ONLY when a native `header`
 *       prop is set (`{id}_dialog-header`). We can't use that slot — see
 *       the DialogContent comment below — so DialogTitle registers a
 *       stable id via context and DialogContent sets aria-labelledby on the
 *       real dialog root itself, mirroring exactly what Radix's own
 *       Title/Content context wiring did under the hood. Verified
 *       empirically that Syncfusion never sets aria-labelledby at all when
 *       `header` is omitted (dialog.js's `setHeader` only writes it when
 *       `this.headerEle` exists) — so there's no race to fight here, unlike
 *       aria-modal above.
 *
 * GAP Syncfusion does NOT cover — confirmed by reading dialog.js:175-183:
 * the overlay click handler (`dlgOverlayClickEventHandler`) only calls
 * `this.focusContent()` and fires the `overlayClick` event; it never calls
 * `.hide()`. Outside-click-to-close is wired back manually below via the
 * `overlayClick` prop, driving the same `onOpenChange(false)` that Escape
 * and the close icon already use.
 *
 * NOTE — Syncfusion's `header`/`footerTemplate` props are typed to accept
 * JSX (`string | Function | any` on DialogTypecast) but do NOT actually
 * work with a JSX element under this app's React 19 + this Syncfusion
 * version: rendering one throws "Element type is invalid ... got
 * undefined" (same underlying class-component/JSX-directive breakage as
 * RCGrid's ColumnsDirective — verified empirically, not assumed). So
 * <DialogHeader>/<DialogBody>/<DialogFooter> all render as ordinary
 * `children` inside Syncfusion's single `.e-dlg-content`, exactly
 * reproducing the pre-migration Radix layout (header+body+footer scroll
 * together as one block once a caller's `max-h-[90vh] overflow-y-auto`
 * className kicks in).
 *
 * Every existing caller uses the controlled `{open, onOpenChange, children}`
 * pattern with `<DialogContent><DialogHeader><DialogTitle>...` — this file
 * preserves that exact external shape. `DialogTrigger`/`DialogPortal`/
 * `DialogClose`/`DialogOverlay` are kept exported for API stability; no
 * current caller uses them (verified via repo-wide grep).
 */
import * as React from 'react'
import { RCModal as RCModalRaw } from '@aurigo/react-core-component-dev/esm/components/atoms/RCModal'
import { asReactComponent } from './_rc-adapter'
import { cn } from '@/lib/cn'

interface RCModalProps {
  show: boolean
  showCloseIcon?: boolean
  isModal?: boolean
  className?: string
  id?: string
  targetId?: string
  onModalClose?: () => void
  overlayClick?: (args?: unknown) => void
  children?: React.ReactNode
}
const RCModal = asReactComponent<RCModalProps>(RCModalRaw)

// ── Controlled open/close context (replaces Radix's DialogPrimitive.Root) ──

interface DialogContextValue {
  open: boolean
  onOpenChange: (open: boolean) => void
}
const DialogContext = React.createContext<DialogContextValue | null>(null)

function useDialogContext(component: string): DialogContextValue {
  const ctx = React.useContext(DialogContext)
  if (!ctx) throw new Error(`<${component}> must be rendered inside <Dialog>`)
  return ctx
}

// ── Title-id registration (replaces Radix's automatic aria-labelledby) ──

const DialogSlotContext = React.createContext<{ titleId: string } | null>(null)

export function Dialog({
  open,
  onOpenChange,
  children,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  children: React.ReactNode
}) {
  const value = React.useMemo(() => ({ open, onOpenChange }), [open, onOpenChange])
  return <DialogContext.Provider value={value}>{children}</DialogContext.Provider>
}

/**
 * No current caller uses an uncontrolled trigger (every Dialog in this
 * codebase is externally controlled), but the export shape stays stable.
 */
export const DialogTrigger = React.forwardRef<
  HTMLButtonElement,
  React.ButtonHTMLAttributes<HTMLButtonElement>
>(function DialogTrigger({ onClick, ...props }, ref) {
  const { onOpenChange } = useDialogContext('DialogTrigger')
  return (
    <button
      ref={ref}
      type="button"
      onClick={(e) => {
        onClick?.(e)
        onOpenChange(true)
      }}
      {...props}
    />
  )
})

/** No-op pass-through — RCModal/Syncfusion owns its own render target. */
export const DialogPortal = ({ children }: { children?: React.ReactNode }) => <>{children}</>

export const DialogClose = React.forwardRef<
  HTMLButtonElement,
  React.ButtonHTMLAttributes<HTMLButtonElement>
>(function DialogClose({ onClick, ...props }, ref) {
  const { onOpenChange } = useDialogContext('DialogClose')
  return (
    <button
      ref={ref}
      type="button"
      onClick={(e) => {
        onClick?.(e)
        onOpenChange(false)
      }}
      {...props}
    />
  )
})

/** No-op — Syncfusion renders its own `.e-dlg-overlay`; nothing to portal. */
export const DialogOverlay = () => null

export interface DialogContentProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Optional close-button label. Currently inert: Syncfusion's own close
   *  icon takes its accessible name from the Dialog's l10n text ('Close'),
   *  not a per-instance prop. No caller overrides this today (verified via
   *  grep), so it's a no-op, not a regression. Kept for prop-contract
   *  stability. */
  closeLabel?: string
  /** When true, hides the built-in X close button (caller renders their own). */
  hideClose?: boolean
}

export const DialogContent = React.forwardRef<HTMLDivElement, DialogContentProps>(
  function DialogContent({ className, children, hideClose = false, ...props }, _ref) {
    const { open, onOpenChange } = useDialogContext('DialogContent')
    const uid = React.useId().replace(/[^a-zA-Z0-9]/g, '')
    const dialogId = `dlg-${uid}`
    const titleId = `${dialogId}-title`
    const slotValue = React.useMemo(() => ({ titleId }), [titleId])

    // Syncfusion never sets aria-labelledby when no native `header` is
    // passed (dialog.js's setHeader only writes it if `this.headerEle`
    // exists — verified empirically), so this is a plain one-time
    // assignment, not a race against Syncfusion's own DOM writes.
    React.useLayoutEffect(() => {
      document.getElementById(dialogId)?.setAttribute('aria-labelledby', titleId)
    }, [dialogId, titleId])

    return (
      <RCModal
        show={open}
        isModal
        showCloseIcon={!hideClose}
        className={cn('dialog-shadcn', className)}
        id={dialogId}
        targetId={`${dialogId}-target`}
        onModalClose={() => onOpenChange(false)}
        // Syncfusion's overlay click never calls .hide() itself (verified in
        // @syncfusion/ej2-popups/src/dialog/dialog.js:175-183) — wire it back
        // to match Radix's outside-click-to-close behavior.
        overlayClick={() => onOpenChange(false)}
        {...props}
      >
        <DialogSlotContext.Provider value={slotValue}>{children}</DialogSlotContext.Provider>
      </RCModal>
    )
  },
)
DialogContent.displayName = 'DialogContent'

export const DialogHeader = ({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) => (
  <div className={cn('flex flex-col gap-1 px-5 py-4 border-b border-border', className)} {...props} />
)
DialogHeader.displayName = 'DialogHeader'

export const DialogFooter = ({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) => (
  <div className={cn('flex flex-row justify-end gap-2 px-5 py-4 border-t border-border', className)} {...props} />
)
DialogFooter.displayName = 'DialogFooter'

export const DialogTitle = ({ className, id, ...props }: React.HTMLAttributes<HTMLHeadingElement>) => {
  const slot = React.useContext(DialogSlotContext)
  return (
    <h2
      id={id ?? slot?.titleId}
      className={cn('text-base font-semibold text-foreground pr-8', className)}
      {...props}
    />
  )
}
DialogTitle.displayName = 'DialogTitle'

export const DialogDescription = ({ className, ...props }: React.HTMLAttributes<HTMLParagraphElement>) => (
  <p className={cn('text-xs text-muted-foreground mt-1', className)} {...props} />
)
DialogDescription.displayName = 'DialogDescription'

export const DialogBody = ({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) => (
  <div className={cn('px-5 py-4 space-y-3', className)} {...props} />
)
DialogBody.displayName = 'DialogBody'
