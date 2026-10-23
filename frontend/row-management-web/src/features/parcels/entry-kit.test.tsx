import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { OptionalSection } from './entry-kit'

describe('AT-X-67 forms open short: rarely used sections start folded and never hide what was entered', () => {
  it('starts folded when blank, opens by itself when it already holds a value, and stays folded once the user closes it', async () => {
    const user = userEvent.setup()
    const { rerender } = render(<OptionalSection title="Notices"><input aria-label="GIN mailed date" defaultValue="" /></OptionalSection>)
    const blank = screen.getByRole('group', { name: 'Notices' }) as HTMLDetailsElement
    expect(blank.open).toBe(false)
    await user.click(screen.getByText('Notices'))
    expect(blank.open).toBe(true)
    await user.click(screen.getByText('Notices'))
    expect(blank.open).toBe(false)
    rerender(<OptionalSection title="Notices"><input aria-label="GIN mailed date" defaultValue="" /></OptionalSection>)
    expect(blank.open).toBe(false)

    render(<OptionalSection title="Move"><input aria-label="Target vacate date" defaultValue="2026-09-01" /></OptionalSection>)
    expect((screen.getByRole('group', { name: 'Move' }) as HTMLDetailsElement).open).toBe(true)
  })
})
