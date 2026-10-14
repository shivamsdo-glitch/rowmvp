/**
 * Tooltip — backed by the real RCTooltip (@aurigo/react-core-component-dev,
 * Syncfusion EJ2 TooltipComponent under the hood).
 *
 * RCTooltip's contract is structurally different from the old Radix
 * `@radix-ui/react-tooltip` primitives this file used to re-export:
 *   - RCTooltip is ONE component. It takes a single trigger element as
 *     `children` and a `content` string (it renders that string to HTML
 *     itself via ReactDOMServer.renderToStaticMarkup + DOMPurify — see
 *     Aurigo-ReactCoreComponent/src/components/atoms/RCTooltip/Tooltip.tsx).
 *   - There is no separate Trigger/Content split and no context — RCTooltip
 *     needs its `content` string ready at the moment IT renders.
 *   - It wires up hover (or click, via `openOnClick`) by
 *     `React.Children.map` + `React.cloneElement` on `children`, injecting
 *     onMouseEnter/onMouseLeave directly — no Radix Slot involved.
 *
 * To avoid rewriting all call sites, `Tooltip` keeps the old compound API
 * (`Tooltip` / `TooltipTrigger` / `TooltipContent` / `TooltipProvider`) as a
 * thin *reader*: it inspects its own `children` at render time (before
 * RCTooltip ever mounts), pulls the trigger element out of `TooltipTrigger`
 * and flattens `TooltipContent`'s children to an HTML string with
 * `ReactDOMServer.renderToStaticMarkup` — the same mechanism RCTooltip uses
 * internally, so this is not a lossier transform than what RCTooltip would
 * have done with a JSX `content` prop anyway. `TooltipTrigger`/`TooltipContent`
 * never actually render as components; `Tooltip` reads their `.props`
 * straight off the element and renders `RCTooltip` in their place.
 *
 * IMPORTANT — this ONLY works because every current call site's
 * `TooltipContent` renders plain text (see src/features/lcp/CeiLabel.tsx).
 * Flattening to a static HTML string drops any event handlers / interactive
 * React state inside `TooltipContent`. Do NOT put a button, a form, or
 * anything with an onClick inside `TooltipContent` — it will silently stop
 * working. If a future call site needs that, it must not use this facade.
 */
import * as React from 'react'
import ReactDOMServer from 'react-dom/server'
import { RCTooltip as RCTooltipRaw } from '@aurigo/react-core-component-dev/esm/components/atoms/RCTooltip'
import { asReactComponent } from './_rc-adapter'
import { cn } from '@/lib/cn'

type Side = 'top' | 'right' | 'bottom' | 'left'

// RCTooltip's `position` is a Syncfusion `Position` string union
// ('TopCenter' | 'BottomCenter' | ...) — map the old Radix `side` values
// onto their nearest "-Center" equivalent.
const SIDE_TO_POSITION: Record<Side, string> = {
  top: 'TopCenter',
  bottom: 'BottomCenter',
  left: 'LeftCenter',
  right: 'RightCenter',
}

// RCTooltip's real prop contract (types.ts / ITooltipProps) — only the
// subset this facade actually drives. `children` must be a single React
// element (RCTooltip clones it to attach hover handlers).
interface RCTooltipElementProps {
  children: React.ReactElement
  content?: string
  position?: string
  openDelay?: number
  customCssClassName?: string
  id?: string
}

const RCTooltip = asReactComponent<RCTooltipElementProps>(RCTooltipRaw)

const DelayContext = React.createContext(0)

function TooltipProvider({
  delayDuration = 0,
  children,
}: {
  delayDuration?: number
  children: React.ReactNode
}) {
  return <DelayContext.Provider value={delayDuration}>{children}</DelayContext.Provider>
}

// Marker component. `Tooltip` reads `props.children` straight off the
// `<TooltipTrigger>` element and never renders this function body — it's
// only invoked as a graceful fallback if `TooltipTrigger` is ever rendered
// outside of a `Tooltip` (e.g. mid-refactor), in which case it just renders
// the trigger with no tooltip behavior instead of crashing.
function TooltipTrigger({
  children,
}: {
  asChild?: boolean
  children: React.ReactElement
}) {
  return children
}

// Marker component, same story as TooltipTrigger — `Tooltip` reads
// `props.children`/`className`/`side` directly. Standalone render (no
// parent `Tooltip`) renders nothing, matching the old Radix behavior of
// TooltipContent never appearing without an open trigger.
function TooltipContent({
  children: _children,
  className: _className,
  side: _side,
}: {
  children: React.ReactNode
  className?: string
  side?: Side
  sideOffset?: number
}) {
  return null
}

function Tooltip({ children }: { children: React.ReactNode }) {
  const delayDuration = React.useContext(DelayContext)

  let triggerElement: React.ReactElement | null = null
  let contentNode: React.ReactNode = null
  let hasContent = false
  let side: Side = 'top'
  let contentClassName: string | undefined

  React.Children.forEach(children, (child) => {
    if (!React.isValidElement(child)) return
    if (child.type === TooltipTrigger) {
      const props = child.props as { children: React.ReactElement }
      triggerElement = props.children
    } else if (child.type === TooltipContent) {
      const props = child.props as { children: React.ReactNode; className?: string; side?: Side }
      contentNode = props.children
      hasContent = true
      side = props.side ?? 'top'
      contentClassName = props.className
    }
  })

  if (!triggerElement) return null
  if (!hasContent) return triggerElement

  const contentHtml = ReactDOMServer.renderToStaticMarkup(<>{contentNode}</>)
  if (!contentHtml) return triggerElement

  return (
    <RCTooltip
      content={contentHtml}
      position={SIDE_TO_POSITION[side]}
      openDelay={delayDuration}
      customCssClassName={cn('rc-tooltip-content', contentClassName)}
    >
      {triggerElement}
    </RCTooltip>
  )
}

export { Tooltip, TooltipTrigger, TooltipContent, TooltipProvider }
