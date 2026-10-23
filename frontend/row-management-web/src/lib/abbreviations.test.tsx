import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Explained } from '@/components/ui/abbr'
import { ABBREVIATIONS, splitAbbreviations } from './abbreviations'

describe('AT-X-66 abbreviations explain themselves', () => {
  it('finds whole upper-case abbreviations and leaves requirement ids, codes and ordinary words alone', () => {
    expect(splitAbbreviations('FTA concurrence needed').filter((p) => p.term).map((p) => p.term)).toEqual(['FTA'])
    expect(splitAbbreviations('Submitted to ATP and sent to PDB').filter((p) => p.term).map((p) => p.term)).toEqual(['ATP', 'PDB'])
    expect(splitAbbreviations('W-9 on file').filter((p) => p.term).map((p) => p.term)).toEqual(['W-9'])
    expect(splitAbbreviations('ROW-014 and PN-1001 and ROWS and prow').filter((p) => p.term)).toEqual([])
    expect(splitAbbreviations('ROW project details').map((p) => p.text).join('')).toBe('ROW project details')
  })

  it('says plainly when the source requirements do not spell a term out', () => {
    expect(ABBREVIATIONS.JAT).toMatch(/not given in the source requirements/)
    expect(ABBREVIATIONS.PDB).toMatch(/not given in the source requirements/)
  })

  it('shows what an abbreviation stands for on hover, keeps the label readable and leaves plain text unchanged', async () => {
    const user = userEvent.setup()
    render(<><label htmlFor="a"><Explained text="PUA date" /></label><input id="a" /><p>No short forms here</p></>)
    expect(screen.getByLabelText('PUA date')).toBeInTheDocument()
    const abbr = screen.getByText('PUA')
    await user.hover(abbr)
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Possession and Use Agreement')
    await user.unhover(abbr)
    expect(screen.queryByRole('tooltip')).toBeNull()
    expect(screen.getByText('No short forms here').querySelector('abbr')).toBeNull()
  })
})
