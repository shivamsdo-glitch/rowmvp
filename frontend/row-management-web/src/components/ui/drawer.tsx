import { useEffect, useRef, type ReactNode } from 'react'

interface DrawerProps {
  title: string
  onClose: () => void
  children: ReactNode
  footer?: ReactNode
  /** Right-anchored drawer width. Defaults to 480px per Sprint 7 spec. */
  widthClass?: string
}

/**
 * Right-anchored slide-over drawer used for configuring an integration
 * (Sprint 7). Behavior contract:
 *
 *   • ESC key or backdrop click closes
 *   • Focus trap — Tab wraps within the drawer, initial focus goes to the
 *     first focusable element, closing restores focus to the previously
 *     focused element outside the drawer
 *   • Prevents body scroll while open (see the effect below)
 *   • Sticky footer for primary actions (Cancel / Save)
 *
 * Adapted from the existing centered <Modal> primitive but slides in from the
 * right and doesn't cap max-height (integrations forms are tall).
 */
export function Drawer({ title, onClose, children, footer, widthClass = 'w-[480px]' }: DrawerProps) {
  const rootRef = useRef<HTMLDivElement>(null)
  const previousActiveRef = useRef<Element | null>(null)

  useEffect(() => {
    previousActiveRef.current = document.activeElement
    const body = document.body
    const prevOverflow = body.style.overflow
    body.style.overflow = 'hidden'

    const firstFocusable = rootRef.current?.querySelector<HTMLElement>(
      'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
    )
    firstFocusable?.focus()

    function onKey(ev: KeyboardEvent) {
      if (ev.key === 'Escape') { ev.preventDefault(); onClose(); return }
      if (ev.key !== 'Tab' || !rootRef.current) return
      const focusables = Array.from(
        rootRef.current.querySelectorAll<HTMLElement>(
          'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
        ),
      ).filter((el) => el.offsetWidth || el.offsetHeight || el === document.activeElement)
      if (focusables.length === 0) return
      const first = focusables[0]
      const last = focusables[focusables.length - 1]
      if (ev.shiftKey && document.activeElement === first) { ev.preventDefault(); last.focus() }
      else if (!ev.shiftKey && document.activeElement === last) { ev.preventDefault(); first.focus() }
    }
    document.addEventListener('keydown', onKey)

    return () => {
      body.style.overflow = prevOverflow
      document.removeEventListener('keydown', onKey)
      const prev = previousActiveRef.current
      if (prev && 'focus' in prev && typeof (prev as HTMLElement).focus === 'function') {
        (prev as HTMLElement).focus()
      }
    }
  }, [onClose])

  return (
    <div className="fixed inset-0 z-50 flex justify-end">
      <div
        className="absolute inset-0 bg-black/50 animate-in fade-in duration-150"
        onClick={onClose}
        aria-hidden
      />
      <div
        ref={rootRef}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className={`relative z-10 h-full ${widthClass} max-w-[95vw] bg-white shadow-2xl flex flex-col border-l border-border animate-in slide-in-from-right duration-200`}
      >
        <div className="flex items-center justify-between px-5 py-4 border-b bg-slate-50 shrink-0">
          <h2 className="text-base font-semibold text-slate-900">{title}</h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="text-slate-400 hover:text-slate-700 transition-colors text-lg leading-none min-w-[44px] min-h-[44px] flex items-center justify-center -mr-3"
          >
            ×
          </button>
        </div>
        <div className="flex-1 overflow-y-auto p-5">{children}</div>
        {footer && (
          <div className="px-5 py-4 border-t bg-white flex justify-end gap-3 shrink-0">{footer}</div>
        )}
      </div>
    </div>
  )
}
