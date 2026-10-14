/**
 * Verifies PageLoader/SectionLoader still render correctly after swapping
 * their internal loading-spinner implementation to the real RCThreeDotLoader
 * (@aurigo/react-core-component-dev) — guards against the component library
 * migration silently breaking the loading state every route depends on.
 */
import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { PageLoader, SectionLoader } from '../states'

describe('PageLoader / SectionLoader (RCThreeDotLoader-backed)', () => {
  it('renders the label text via RCThreeDotLoader displayText', () => {
    render(<PageLoader label="Loading assets" />)
    expect(screen.getByText('Loading assets')).toBeInTheDocument()
  })

  it('defaults to "Loading..." when no label is passed', () => {
    render(<SectionLoader />)
    expect(screen.getByText('Loading...')).toBeInTheDocument()
  })

  it('renders the real RCThreeDotLoader DOM shape (three dots)', () => {
    const { container } = render(<PageLoader />)
    expect(container.querySelector('.dotloader')).toBeTruthy()
    expect(container.querySelector('.dots')).toBeTruthy()
    expect(container.querySelector('.dot1')).toBeTruthy()
    expect(container.querySelector('.dot2')).toBeTruthy()
    expect(container.querySelector('.dot3')).toBeTruthy()
  })

  it('never renders the literal string "undefined" from RCThreeDotLoader\'s type prop', () => {
    const { container } = render(<SectionLoader />)
    expect(container.querySelector('.dotloader')!.className).not.toContain('undefined')
  })

  it('applies the dotloader-sm modifier for SectionLoader but not PageLoader', () => {
    const section = render(<SectionLoader />)
    expect(section.container.querySelector('.dotloader-sm')).toBeTruthy()
    section.unmount()

    const page = render(<PageLoader />)
    expect(page.container.querySelector('.dotloader-sm')).toBeFalsy()
  })
})
