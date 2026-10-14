/**
 * Verifies Button still renders correctly after swapping its internal
 * implementation to the real RCButton (@aurigo/react-core-component-dev,
 * Syncfusion EJ2 ButtonComponent) — guards against the component library
 * migration silently dropping click handlers, mis-shaping icon+text
 * children, or corrupting the ~280 existing call sites this facade
 * reconstructs `text`/`leftIcon`/`rightIcon` for. See button.tsx's header
 * comment for the full list of structural mismatches this test suite
 * exercises.
 */
import * as React from 'react'
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { RefreshCw, Plus, Loader2, MoreHorizontal, ChevronDown } from 'lucide-react'
import { Button, buttonClassName } from '../button'

describe('Button (RCButton-backed)', () => {
  it('renders a text-only button and fires onClick', () => {
    const onClick = vi.fn()
    render(<Button onClick={onClick}>Save</Button>)
    const btn = screen.getByText('Save').closest('button')!
    fireEvent.click(btn)
    expect(onClick).toHaveBeenCalledTimes(1)
  })

  it('mounts the real RCButton DOM shape (button-btn / button-text)', () => {
    const { container } = render(<Button>Save</Button>)
    expect(container.querySelector('.button-btn')).toBeTruthy()
    expect(container.querySelector('.button-text')?.textContent).toBe('Save')
  })

  it('extracts a leading icon with a real RC icon-font match and flattens the remaining text', () => {
    const { container } = render(
      <Button variant="outline">
        <RefreshCw size={14} /> Sync from GIS
      </Button>,
    )
    expect(container.querySelector('.button-text')?.textContent).toBe('Sync from GIS')
    // RefreshCw maps to the real "refresh" icon-font glyph — the original
    // Lucide <svg> should NOT be present (it went through leftIcon, not an
    // overlay).
    expect(container.querySelector('svg.lucide-refresh-cw')).toBeFalsy()
  })

  it('extracts a leading icon for asChild-free <Link>-less composition (Plus -> big-plus)', () => {
    const { container } = render(
      <Button>
        <Plus size={14} /> Add Asset
      </Button>,
    )
    expect(container.querySelector('.button-text')?.textContent).toBe('Add Asset')
    expect(container.querySelector('svg.lucide-plus')).toBeFalsy()
  })

  it('renders an icon with no RC icon-font match (Loader2 spinner) as an absolutely-positioned overlay, preserving animate-spin', () => {
    const { container } = render(
      <Button disabled>
        <Loader2 className="animate-spin" size={13} /> Saving…
      </Button>,
    )
    expect(container.querySelector('.button-text')?.textContent).toBe('Saving…')
    const overlay = container.querySelector('.rc-button-icon-overlay-left')
    expect(overlay).toBeTruthy()
    expect(overlay!.querySelector('svg.animate-spin')).toBeTruthy()
  })

  it('collapses a mutually-exclusive loading-state ternary to a single icon at render time', () => {
    let isPending = false
    const { container, rerender } = render(
      <Button>
        {isPending ? <Loader2 className="animate-spin" size={13} /> : <RefreshCw size={13} />} Refresh
      </Button>,
    )
    // Only the truthy branch (RefreshCw, a real RC match) should be present.
    expect(container.querySelector('.rc-button-icon-overlay')).toBeFalsy()
    expect(container.querySelector('.button-text')?.textContent).toBe('Refresh')

    isPending = true
    rerender(
      <Button>
        {isPending ? <Loader2 className="animate-spin" size={13} /> : <RefreshCw size={13} />} Refresh
      </Button>,
    )
    // Now the Loader2 branch is truthy — falls to the overlay path.
    expect(container.querySelector('.rc-button-icon-overlay')).toBeTruthy()
  })

  it('renders an icon-only button (no text) with the icon-only collapse class', () => {
    const { container } = render(
      <Button aria-label="More actions">
        <MoreHorizontal size={15} />
      </Button>,
    )
    expect(container.querySelector('.button-icon-only')).toBeTruthy()
    expect(container.querySelector('.button-text')?.textContent?.trim()).toBe('')
  })

  it('treats a single trailing icon as rightIcon (e.g. label + chevron)', () => {
    const { container } = render(
      <Button>
        Choose target
        <ChevronDown size={13} />
      </Button>,
    )
    expect(container.querySelector('.button-text')?.textContent).toBe('Choose target')
    expect(container.querySelector('svg.lucide-chevron-down')).toBeFalsy()
  })

  it('forces buttonState to disabled and sets the real DOM disabled attribute', () => {
    const onClick = vi.fn()
    render(<Button disabled onClick={onClick}>Save</Button>)
    const btn = screen.getByText('Save').closest('button')!
    expect(btn).toBeDisabled()
    fireEvent.click(btn)
    expect(onClick).not.toHaveBeenCalled()
  })

  it('applies a button-tone-* class per variant so color comes from this app\'s tokens, not RCButton\'s palette', () => {
    const { container: destructive } = render(<Button variant="destructive">Delete</Button>)
    expect(destructive.querySelector('.button-tone-destructive')).toBeTruthy()

    const { container: ghost } = render(<Button variant="ghost">Cancel</Button>)
    expect(ghost.querySelector('.button-tone-ghost')).toBeTruthy()
  })

  it('asChild composes Button styling onto a single child element instead of rendering RCButton', () => {
    const { container } = render(
      <Button asChild variant="outline">
        <a href="/assets/new">Add Asset</a>
      </Button>,
    )
    expect(container.querySelector('.button-btn')).toBeFalsy()
    const link = screen.getByText('Add Asset')
    expect(link.tagName).toBe('A')
    expect(link.className).toContain('border')
  })

  it('unwraps a styling-only <span> text wrapper into the text stream (e.g. IntegrationCard\'s "<RefreshCw/> <span className=\'ml-1\'>Sync now</span>")', () => {
    const { container } = render(
      <Button variant="outline">
        <RefreshCw size={13} /> <span className="ml-1">Sync now</span>
      </Button>,
    )
    expect(container.querySelector('.button-btn')).toBeTruthy()
    expect(container.querySelector('.button-text')?.textContent).toBe('Sync now')
  })

  it('falls back to a plain <button> when children do not fit the icon(s)+text shape', () => {
    const { container } = render(
      <Button>
        <span data-testid="custom-badge">3 new</span>
      </Button>,
    )
    expect(container.querySelector('.button-btn')).toBeFalsy()
    expect(screen.getByTestId('custom-badge')).toBeInTheDocument()
  })

  it('buttonClassName() reproduces the legacy Tailwind classes for Radix asChild trigger sites that cannot use <Button>', () => {
    const cls = buttonClassName('outline', 'default', 'px-2')
    expect(cls).toContain('border')
    expect(cls).toContain('px-2')
  })
})
