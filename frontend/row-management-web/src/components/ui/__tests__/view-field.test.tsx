/**
 * Verifies ViewField still renders correctly after swapping its internal
 * label+value chrome to the real RCViewModeComponent
 * (@aurigo/react-core-component-dev) — guards against the component library
 * migration silently breaking the read-only field pattern used across
 * assets.$id.index.tsx, JobOrderDetail.tsx, and lcp/details.tsx.
 */
import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ViewField } from '../view-field'

describe('ViewField (RCViewModeComponent-backed)', () => {
  it('renders the label and a plain string value via the RC data prop', () => {
    const { container } = render(<ViewField label="Owner" value="City of Springfield" />)
    expect(screen.getByText('Owner')).toBeInTheDocument()
    expect(screen.getByText('City of Springfield')).toBeInTheDocument()
    // Real RCViewModeComponent DOM shape.
    expect(container.querySelector('.component-viewmodeContainer')).toBeTruthy()
    expect(container.querySelector('.label-viewmode-component')).toBeTruthy()
    expect(container.querySelector('.viewmodeContent-component')).toBeTruthy()
  })

  it('renders a numeric value (routed through data as a string)', () => {
    render(<ViewField label="Year installed" value={1998} />)
    expect(screen.getByText('1998')).toBeInTheDocument()
  })

  it('renders a JSX value via the RC children slot', () => {
    render(<ViewField label="Status" value={<span data-testid="badge">Active</span>} />)
    expect(screen.getByTestId('badge')).toBeInTheDocument()
    expect(screen.getByText('Active')).toBeInTheDocument()
  })

  it('renders the muted em dash for empty/null/undefined values', () => {
    const { container } = render(<ViewField label="Region" value={undefined} />)
    const dash = screen.getByText('—')
    expect(dash).toBeInTheDocument()
    expect(dash.className).toContain('italic')
    expect(container.querySelector('.viewmodeContent-component')).toBeTruthy()
  })

  it('renders the trailing slot beside the RC value block as a sibling, not nested inside it', () => {
    const { container } = render(
      <ViewField label="Original cost" value="$1,000,000" trailing={<span data-testid="erp-tag">ERP-fed</span>} />,
    )
    const erpTag = screen.getByTestId('erp-tag')
    const rcContainer = container.querySelector('.component-viewmodeContainer')
    expect(erpTag).toBeInTheDocument()
    expect(rcContainer).toBeTruthy()
    // trailing must NOT be inside RCViewModeComponent's own DOM.
    expect(rcContainer!.contains(erpTag)).toBe(false)
    // trailing and the RC block share the same flex-row parent (siblings).
    const row = rcContainer!.closest('.flex')
    expect(row).toBeTruthy()
    expect(row!.contains(erpTag)).toBe(true)
  })

  it('does not add a flex row wrapper when there is no trailing content', () => {
    const { container } = render(<ViewField label="Description" value="A short description" />)
    expect(container.querySelector('.flex')).toBeFalsy()
  })
})
