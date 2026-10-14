/**
 * Verifies the shadcn-style compound Dialog still renders and closes
 * correctly, and that the WCAG 2.1 baseline it must preserve (see dialog.tsx
 * doc comment) still holds, after swapping @radix-ui/react-dialog for the
 * real RCModal (Syncfusion DialogComponent).
 */
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogBody,
  DialogFooter,
} from '../dialog'

function renderDialog(onOpenChange = vi.fn(), open = true) {
  return render(
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Add Mitigation Strategy</DialogTitle>
          <DialogDescription>Describe the plan.</DialogDescription>
        </DialogHeader>
        <DialogBody>
          <p>Body content</p>
        </DialogBody>
        <DialogFooter>
          <button>Save</button>
        </DialogFooter>
      </DialogContent>
    </Dialog>,
  )
}

describe('Dialog (RCModal-backed)', () => {
  it('renders header, body, and footer content', () => {
    renderDialog()
    expect(screen.getByText('Add Mitigation Strategy')).toBeInTheDocument()
    expect(screen.getByText('Describe the plan.')).toBeInTheDocument()
    expect(screen.getByText('Body content')).toBeInTheDocument()
    expect(screen.getByText('Save')).toBeInTheDocument()
  })

  it('marks the dialog closed (e-popup-close) instead of unmounting it', () => {
    // Real, verified architecture difference from Radix: Radix unmounts
    // DialogContent entirely when `open=false`. Syncfusion keeps the DOM
    // mounted and toggles the `e-popup-open`/`e-popup-close` CSS classes
    // (`display:none` lives in the real, loaded @syncfusion/ej2-popups
    // stylesheet, which jsdom doesn't apply in this test env — hence
    // asserting the class, the actual signal Syncfusion flips, rather than
    // computed visibility). Not a WCAG regression (`display:none` still
    // removes it from the accessibility tree in a real browser), but a
    // real behavior difference worth flagging: content persists in the DOM
    // (and in React state) across open/close toggles instead of remounting
    // fresh each time.
    const { container } = renderDialog(vi.fn(), false)
    const dlg = container.querySelector('.e-dialog') as HTMLElement
    expect(dlg.className).toContain('e-popup-close')
    expect(dlg.className).not.toContain('e-popup-open')
    expect(screen.getByText('Add Mitigation Strategy')).toBeInTheDocument()
  })

  it('wires aria-modal="true" and role="dialog" on the real dialog root (SC 4.1.2)', () => {
    renderDialog()
    const dlg = screen.getByRole('dialog')
    expect(dlg).toHaveAttribute('aria-modal', 'true')
  })

  it('wires aria-labelledby to the real DialogTitle element (SC 4.1.2)', () => {
    renderDialog()
    const dlg = screen.getByRole('dialog')
    const labelledbyId = dlg.getAttribute('aria-labelledby')
    expect(labelledbyId).toBeTruthy()
    const titleEl = document.getElementById(labelledbyId!)
    expect(titleEl).toHaveTextContent('Add Mitigation Strategy')
  })

  it('calls onOpenChange(false) on outside (overlay) click — the gap Syncfusion does not close for itself', () => {
    const onOpenChange = vi.fn()
    const { container } = renderDialog(onOpenChange)
    const overlay = container.querySelector('.e-dlg-overlay') as HTMLElement | null
    expect(overlay).toBeTruthy()
    fireEvent.click(overlay!)
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it('calls onOpenChange(false) when the native close icon is activated', async () => {
    const onOpenChange = vi.fn()
    const { container } = renderDialog(onOpenChange)
    const closeBtn = container.querySelector('.e-dlg-closeicon-btn') as HTMLElement | null
    expect(closeBtn).toBeTruthy()
    fireEvent.click(closeBtn!)
    // Routes through Syncfusion's `.hide()` Fade-out close animation
    // (~400ms default) before the `close` event fires — see modal.test.tsx.
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false), { timeout: 2000 })
  })

  it('hides the close icon when hideClose is set', () => {
    render(
      <Dialog open onOpenChange={vi.fn()}>
        <DialogContent hideClose>
          <DialogHeader>
            <DialogTitle>No close icon</DialogTitle>
          </DialogHeader>
        </DialogContent>
      </Dialog>,
    )
    expect(document.querySelector('.e-dlg-closeicon-btn')).not.toBeInTheDocument()
  })
})
