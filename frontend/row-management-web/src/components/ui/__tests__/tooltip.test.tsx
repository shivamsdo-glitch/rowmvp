/**
 * Verifies Tooltip still renders correctly after swapping its internal
 * implementation to the real RCTooltip (@aurigo/react-core-component-dev,
 * Syncfusion EJ2 TooltipComponent) — guards against the component library
 * migration silently breaking the info-icon tooltip pattern used by
 * CeiLabel and the LCP condition-projection panels.
 *
 * The EJ2 TooltipComponent doesn't render its popup content into the DOM
 * until it's actually opened (hover/focus) — real hover simulation isn't
 * reliable under jsdom (no layout engine for EJ2's positioning calc), so
 * these tests assert against the ej2_instances[0] widget state Syncfusion
 * attaches to the wrapping DOM node, which is populated synchronously at
 * mount regardless of open state.
 */
import * as React from 'react'
import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Tooltip, TooltipTrigger, TooltipContent, TooltipProvider } from '../tooltip'

type TooltipEj2Node = HTMLElement & {
  ej2_instances?: Array<{ content?: string; cssClass?: string; position?: string }>
}

function Wrapped({ content }: { content: React.ReactNode }) {
  return (
    <TooltipProvider delayDuration={200}>
      <Tooltip>
        <TooltipTrigger asChild>
          <span data-testid="trigger" aria-label="explanation">
            info
          </span>
        </TooltipTrigger>
        <TooltipContent side="top" className="max-w-sm leading-snug">
          {content}
        </TooltipContent>
      </Tooltip>
    </TooltipProvider>
  )
}

function getEj2(container: HTMLElement) {
  const node = container.querySelector('#tooltip') as TooltipEj2Node | null
  return node?.ej2_instances?.[0]
}

describe('Tooltip (RCTooltip-backed)', () => {
  it('always renders the trigger, regardless of hover state', () => {
    render(<Wrapped content="Priority index explanation" />)
    expect(screen.getByTestId('trigger')).toBeInTheDocument()
  })

  it('mounts the real EJ2 TooltipComponent wrapping the trigger', () => {
    const { container } = render(<Wrapped content="Priority index explanation" />)
    const wrap = container.querySelector('.e-tooltip.e-control')
    expect(wrap).toBeTruthy()
    expect(wrap!.contains(screen.getByTestId('trigger'))).toBe(true)
  })

  it('flattens plain-text TooltipContent children into the RCTooltip content HTML', () => {
    const { container } = render(<Wrapped content="Priority index explanation" />)
    const ej2 = getEj2(container)
    expect(ej2?.content).toContain('Priority index explanation')
    expect(ej2?.content).toContain('tip-content')
  })

  it('maps side="top" to the Syncfusion TopCenter position and forwards the className', () => {
    const { container } = render(<Wrapped content="x" />)
    const ej2 = getEj2(container)
    expect(ej2?.position).toBe('TopCenter')
    expect(ej2?.cssClass).toContain('rc-tooltip-content')
    expect(ej2?.cssClass).toContain('max-w-sm leading-snug')
  })

  it('renders just the trigger (no crash, no EJ2 wrapper) when TooltipContent is omitted', () => {
    const { container } = render(
      <TooltipProvider>
        <Tooltip>
          <TooltipTrigger asChild>
            <span data-testid="bare-trigger">bare</span>
          </TooltipTrigger>
        </Tooltip>
      </TooltipProvider>,
    )
    expect(screen.getByTestId('bare-trigger')).toBeInTheDocument()
    expect(container.querySelector('.e-tooltip')).toBeFalsy()
  })

  it('TooltipContent never renders standalone (no parent Tooltip to read it)', () => {
    render(<TooltipContent>orphaned content</TooltipContent>)
    expect(screen.queryByText('orphaned content')).not.toBeInTheDocument()
  })
})
