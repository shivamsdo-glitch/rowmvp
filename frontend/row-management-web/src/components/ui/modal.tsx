import type { ReactNode } from 'react'
import { useId } from 'react'
import { RCModal as RCModalRaw } from '@aurigo/react-core-component-dev/esm/components/atoms/RCModal'
import { asReactComponent } from './_rc-adapter'

interface ModalProps {
  title: string
  onClose: () => void
  children: ReactNode
  footer?: ReactNode
  maxWidthClass?: string
}

/**
 * Narrow view of RCModal's real prop surface (`ModalComponentPropsType` in
 * @aurigo/react-core-component-dev/esm/components/atoms/RCModal/types.d.ts)
 * that this facade actually drives.
 *
 * `header`/`footerTemplate` are typed `string | Function | any` on the
 * underlying Syncfusion DialogComponent (dialog.component.d.ts's
 * `DialogTypecast`), which looks like it should accept JSX — it does NOT
 * work here in practice. Verified empirically (render a JSX element as
 * `header` or `footerTemplate` under this app's React 19 + this Syncfusion
 * version): both throw "Element type is invalid ... got undefined" at
 * render time. That's the same class-component/JSX-directive template
 * mechanism already broken for RCGrid's ColumnsDirective in this app (see
 * tsc output / .ai/architecture-review.md) — not something we can paper
 * over here. So `header` is a plain string (the title always was one), and
 * the footer renders as ordinary children instead of `footerTemplate`.
 */
interface RCModalProps {
  show: boolean
  header?: string
  showCloseIcon?: boolean
  isModal?: boolean
  className?: string
  id?: string
  targetId?: string
  onModalClose?: () => void
  overlayClick?: (args?: unknown) => void
  children?: ReactNode
}

const RCModal = asReactComponent<RCModalProps>(RCModalRaw)

/**
 * Lightweight centered modal, now backed by the real Aurigo RCModal
 * (Syncfusion DialogComponent) instead of a hand-rolled fixed/overlay div.
 *
 * This component never had a focus trap, Escape-close, or any ARIA wiring
 * of its own (fully hand-rolled) — none of that is a regression to guard,
 * it's a free upgrade from Syncfusion's native Dialog. The one thing it DID
 * support that needs explicit wiring here: closing on outside (backdrop)
 * click. Syncfusion's overlay click handler does NOT call `.hide()` itself —
 * verified in @syncfusion/ej2-popups/src/dialog/dialog.js:175-183, the
 * handler only calls `focusContent()` and fires the `overlayClick` event —
 * so we wire that event straight to `onClose` below.
 */
export function Modal({ title, onClose, children, footer, maxWidthClass = 'max-w-lg' }: ModalProps) {
  // useId() contains colons (":r0:") which Syncfusion's `target={'#'+id}`
  // feeds straight into querySelector — strip to keep it a valid selector.
  const uid = useId().replace(/[^a-zA-Z0-9]/g, '')

  return (
    <RCModal
      show
      isModal
      header={title}
      showCloseIcon
      className={`modal-plain ${maxWidthClass}`}
      id={`modal-dlg-${uid}`}
      targetId={`modal-target-${uid}`}
      onModalClose={onClose}
      overlayClick={onClose}
    >
      <div className="modal-plain-body">{children}</div>
      {footer && <div className="modal-plain-footer">{footer}</div>}
    </RCModal>
  )
}
