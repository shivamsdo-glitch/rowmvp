/**
 * Verifies the shadcn-style compound Card still renders correctly after
 * swapping its internal implementation to the real RCCard
 * (@aurigo/react-core-component-dev) — guards against the component
 * library migration silently breaking the header/content flattening (see
 * card.tsx doc comment) that every call site in this app depends on.
 */
import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Card, CardHeader, CardTitle, CardDescription, CardContent, CardFooter } from '../card'

describe('Card (RCCard-backed)', () => {
  it('renders CardHeader (title + description) and CardContent', () => {
    render(
      <Card>
        <CardHeader>
          <CardTitle>Identity</CardTitle>
          <CardDescription>Parent-child position and GIS source.</CardDescription>
        </CardHeader>
        <CardContent>
          <p>Body content</p>
        </CardContent>
      </Card>,
    )
    expect(screen.getByText('Identity')).toBeInTheDocument()
    expect(screen.getByText('Parent-child position and GIS source.')).toBeInTheDocument()
    expect(screen.getByText('Body content')).toBeInTheDocument()
  })

  it('renders the real RCCard DOM shape (card-control/e-card)', () => {
    const { container } = render(
      <Card>
        <CardContent>content only</CardContent>
      </Card>,
    )
    const el = container.querySelector('.card-control')!
    expect(el).toBeTruthy()
    expect(el.querySelector('.e-card')).toBeTruthy()
    expect(el.querySelector('.e-card-content')).toBeTruthy()
  })

  it('renders CardContent alone with no CardHeader (no header caption in the DOM)', () => {
    const { container } = render(
      <Card>
        <CardContent>KPI value</CardContent>
      </Card>,
    )
    expect(screen.getByText('KPI value')).toBeInTheDocument()
    expect(container.querySelector('.e-card-header-title')).toBeFalsy()
  })

  it('renders bare (non-CardHeader/CardContent) children — e.g. a loading placeholder', () => {
    render(
      <Card>
        <span>Loading…</span>
      </Card>,
    )
    expect(screen.getByText('Loading…')).toBeInTheDocument()
  })

  it('folds a bare footer div (no <CardFooter> wrapper) after CardContent, matching current call sites', () => {
    render(
      <Card>
        <CardHeader><CardTitle>Vendor</CardTitle></CardHeader>
        <CardContent>Details</CardContent>
        <div data-testid="footer-row">
          <button>Configure</button>
        </div>
      </Card>,
    )
    expect(screen.getByText('Vendor')).toBeInTheDocument()
    expect(screen.getByText('Details')).toBeInTheDocument()
    expect(screen.getByTestId('footer-row')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Configure' })).toBeInTheDocument()
  })

  it('renders a header containing a title+description and a sibling action button unchanged (e.g. Emergency Repairs pattern)', () => {
    render(
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <div>
              <CardTitle>Emergency Repairs</CardTitle>
              <CardDescription>FHWA ER claims.</CardDescription>
            </div>
            <button>Add Claim</button>
          </div>
        </CardHeader>
        <CardContent>Claims table</CardContent>
      </Card>,
    )
    expect(screen.getByText('Emergency Repairs')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add Claim' })).toBeInTheDocument()
  })

  it('forwards className onto the real painted box (card-control)', () => {
    const { container } = render(
      <Card className="border-red-500">
        <CardContent>x</CardContent>
      </Card>,
    )
    const el = container.querySelector('.card-control')!
    expect(el.className).toContain('border-red-500')
  })

  it('calls onClick when the card is clicked', () => {
    const onClick = vi.fn()
    const { container } = render(
      <Card onClick={onClick}>
        <CardContent>clickable</CardContent>
      </Card>,
    )
    const el = container.querySelector('.card-control') as HTMLElement
    el.click()
    expect(onClick).toHaveBeenCalled()
  })

  it('assigns a unique id to every card instance (no id="card" collisions)', () => {
    const { container } = render(
      <>
        <Card><CardContent>a</CardContent></Card>
        <Card><CardContent>b</CardContent></Card>
      </>,
    )
    const cards = container.querySelectorAll('.card-control')
    expect(cards.length).toBe(2)
    const ids = Array.from(cards).map((c) => c.id)
    expect(ids[0]).toBeTruthy()
    expect(ids[1]).toBeTruthy()
    expect(ids[0]).not.toBe('card')
    expect(ids[1]).not.toBe('card')
    expect(ids[0]).not.toBe(ids[1])
  })

  it('CardFooter still renders (kept for API stability, no current call site)', () => {
    render(
      <Card>
        <CardContent>body</CardContent>
        <CardFooter>
          <button>Save</button>
        </CardFooter>
      </Card>,
    )
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument()
  })
})
