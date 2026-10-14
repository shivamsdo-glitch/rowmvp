/**
 * Verifies Modal still renders correctly, and still closes on every trigger
 * it supported before, after swapping its hand-rolled fixed/overlay div for
 * the real RCModal (Syncfusion DialogComponent) — guards against the
 * component library migration silently dropping outside-click-to-close
 * (Syncfusion's own overlay click handler does NOT call `.hide()`, see
 * modal.tsx for the sourced explanation).
 */
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { Modal } from '../modal'

describe('Modal (RCModal-backed)', () => {
  it('renders the title and children', () => {
    render(
      <Modal title="Create Need" onClose={vi.fn()}>
        <div>Form body</div>
      </Modal>,
    )
    expect(screen.getByText('Create Need')).toBeInTheDocument()
    expect(screen.getByText('Form body')).toBeInTheDocument()
  })

  it('renders the footer only when provided', () => {
    const { rerender } = render(
      <Modal title="No footer" onClose={vi.fn()}>
        <div>Body</div>
      </Modal>,
    )
    expect(screen.queryByText('Save')).not.toBeInTheDocument()

    rerender(
      <Modal title="With footer" onClose={vi.fn()} footer={<button>Save</button>}>
        <div>Body</div>
      </Modal>,
    )
    expect(screen.getByText('Save')).toBeInTheDocument()
  })

  it('renders the real Syncfusion e-dialog DOM shape', () => {
    const { container } = render(
      <Modal title="Shape check" onClose={vi.fn()}>
        <div>Body</div>
      </Modal>,
    )
    expect(container.querySelector('.e-dialog.modal-plain')).toBeTruthy()
  })

  it('calls onClose when the native close icon is activated', async () => {
    const onClose = vi.fn()
    const { container } = render(
      <Modal title="Closeable" onClose={onClose}>
        <div>Body</div>
      </Modal>,
    )
    const closeBtn = container.querySelector('.e-dlg-closeicon-btn') as HTMLElement | null
    expect(closeBtn).toBeTruthy()
    fireEvent.click(closeBtn!)
    // Syncfusion's close-icon handler routes through `.hide()`, which runs
    // the dialog's Fade-out close animation (animationSettings default:
    // { effect: 'Fade', duration: 400, delay: 0 } — dialog.js:1929) before
    // firing the `close` event that `onModalClose`/`onClose` is wired to.
    // Verified empirically: onClose is NOT called synchronously, only after
    // that ~400ms window elapses.
    await waitFor(() => expect(onClose).toHaveBeenCalled(), { timeout: 2000 })
  })

  it('calls onClose on outside (overlay) click — the gap Syncfusion does not close for itself', () => {
    const onClose = vi.fn()
    const { container } = render(
      <Modal title="Outside click" onClose={onClose}>
        <div>Body</div>
      </Modal>,
    )
    const overlay = container.querySelector('.e-dlg-overlay') as HTMLElement | null
    expect(overlay).toBeTruthy()
    fireEvent.click(overlay!)
    expect(onClose).toHaveBeenCalled()
  })
})
