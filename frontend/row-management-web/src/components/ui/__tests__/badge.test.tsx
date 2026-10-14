/**
 * Verifies Badge still renders correctly after swapping its internal
 * implementation to the real RCBadge (@aurigo/react-core-component-dev) —
 * guards against the component library migration silently breaking the
 * status-pill look used across condition grades, RUL/risk bands, inspection
 * status, and the tone-mapping exports other features depend on.
 */
import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import {
  Badge,
  GRADE_TONE,
  RUL_BAND_TONE,
  RISK_BAND_TONE,
  INSPECTION_STATUS_TONE,
} from '../badge'

describe('Badge (RCBadge-backed)', () => {
  it('renders the visible label', () => {
    render(<Badge tone="ok">Excellent</Badge>)
    expect(screen.getByText('Excellent')).toBeInTheDocument()
  })

  it('renders the real RCBadge DOM shape (rc-badge/e-badge/e-text-badge-custom)', () => {
    const { container } = render(<Badge tone="danger">Critical</Badge>)
    const el = container.querySelector('.rc-badge')!
    expect(el).toBeTruthy()
    expect(el.classList.contains('e-badge')).toBe(true)
    expect(el.classList.contains('e-text-badge-custom')).toBe(true)
    // closest real bg for tone="danger"
    expect(el.classList.contains('e-badge-danger')).toBe(true)
  })

  it('defaults to tone="neutral" and variant="soft"', () => {
    const { container } = render(<Badge>Draft</Badge>)
    const el = container.querySelector('.rc-badge')!
    expect(el.classList.contains('badge-tone-neutral')).toBe(true)
    expect(el.classList.contains('badge-soft')).toBe(true)
  })

  it('applies the requested variant class', () => {
    const { container } = render(<Badge tone="primary" variant="outline">Role</Badge>)
    const el = container.querySelector('.rc-badge')!
    expect(el.classList.contains('badge-outline')).toBe(true)
    expect(el.classList.contains('badge-tone-primary')).toBe(true)
  })

  it('keeps "alert" and "warn" visually distinct even though both map to bg="warning"', () => {
    const warn = render(<Badge tone="warn">Fair</Badge>)
    const alert = render(<Badge tone="alert">Poor</Badge>)
    const warnEl = warn.container.querySelector('.rc-badge')!
    const alertEl = alert.container.querySelector('.rc-badge')!
    expect(warnEl.classList.contains('e-badge-warning')).toBe(true)
    expect(alertEl.classList.contains('e-badge-warning')).toBe(true)
    // Same RCBadge bg, but different tone modifier classes drive different colors
    expect(warnEl.classList.contains('badge-tone-warn')).toBe(true)
    expect(alertEl.classList.contains('badge-tone-alert')).toBe(true)
    expect(warnEl.className).not.toBe(alertEl.className)
  })

  it('a custom title wins over RCBadge\'s built-in title=children behavior', () => {
    render(
      <Badge tone="danger" title="Push failed: 500 from Aurigo Plan">
        Push failed
      </Badge>,
    )
    const el = screen.getByText('Push failed')
    expect(el.getAttribute('title')).toBe('Push failed: 500 from Aurigo Plan')
  })

  it('falls back to no title attribute when none is passed (not the raw children text)', () => {
    render(<Badge tone="ok">Applied</Badge>)
    const el = screen.getByText('Applied')
    expect(el.getAttribute('title')).toBeNull()
  })

  it('forwards a ref to the underlying DOM node', () => {
    const ref = { current: null as HTMLSpanElement | null }
    render(<Badge ref={ref} tone="info">Draft</Badge>)
    expect(ref.current).toBeInstanceOf(HTMLElement)
    expect(ref.current?.textContent).toBe('Draft')
  })

  it('keeps the tone-mapping exports other features rely on', () => {
    expect(GRADE_TONE.Excellent).toBe('ok')
    expect(GRADE_TONE.Critical).toBe('danger')
    expect(RUL_BAND_TONE).toEqual(['ok', 'warn', 'alert', 'danger'])
    expect(RISK_BAND_TONE.High).toBe('alert')
    expect(INSPECTION_STATUS_TONE.Approved).toBe('ok')
  })
})
