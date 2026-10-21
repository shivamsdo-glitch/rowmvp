import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Tabs, TabsList, TabsTrigger } from './tabs'

describe('tabs', () => {
  it('marks the active tab with the primary colour, bold text and a full-width primary bottom border, and leaves the others plain', () => {
    render(
      <Tabs>
        <TabsList role="tablist" aria-label="Sections">
          <TabsTrigger role="tab" aria-selected active>Details</TabsTrigger>
          <TabsTrigger role="tab" aria-selected={false}>Scoring</TabsTrigger>
        </TabsList>
      </Tabs>,
    )
    const active = screen.getByRole('tab', { name: 'Details' })
    const other = screen.getByRole('tab', { name: 'Scoring' })

    expect(active.className).toContain('border-primary')
    expect(active.className).toContain('text-primary')
    expect(active.className).toContain('font-semibold')
    expect(other.className).toContain('border-transparent')
    expect(other.className).not.toContain('text-primary ')
    expect(other.className).not.toContain('font-semibold')
    // Both reserve the same border height, so selecting a tab never moves the others.
    expect(active.className).toContain('border-b-2')
    expect(other.className).toContain('border-b-2')
  })
})
