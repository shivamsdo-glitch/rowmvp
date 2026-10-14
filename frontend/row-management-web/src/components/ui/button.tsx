import * as React from 'react'
import { cn } from '@/lib/cn'
import { RCButton as RCButtonRaw } from '@aurigo/react-core-component-dev/esm/components/atoms/RCButton'
import { asReactComponent } from './_rc-adapter'
import * as LucideIcons from 'lucide-react'

type Variant = 'default' | 'destructive' | 'outline' | 'secondary' | 'ghost' | 'link'
type Size = 'default' | 'sm' | 'lg' | 'icon'

export interface ButtonProps
  extends Omit<React.ButtonHTMLAttributes<HTMLButtonElement>, 'type' | 'ref'> {
  variant?: Variant
  size?: Size
  asChild?: boolean
  type?: 'button' | 'submit' | 'reset'
}

/**
 * Button — facade over the real Aurigo RCButton (@aurigo/react-core-component-dev,
 * Syncfusion EJ2 ButtonComponent under the hood). Migrated off the hand-rolled
 * Tailwind `<button>` as part of the component-library migration (see badge.tsx /
 * card.tsx / modal.tsx / tooltip.tsx for the established deep-import +
 * `asReactComponent` pattern).
 *
 * STRUCTURAL MISMATCHES — read before touching this file. This is the highest-risk
 * primitive migrated so far because RCButton's contract is fundamentally
 * incompatible with how this app's ~280 call sites (65 files) use `<Button>`:
 *
 * 1. NO `children` prop. RCButton takes `text: string` (rendered verbatim inside
 *    `<span className="button-text">`) plus `leftIcon`/`rightIcon: {name, iconSet?,
 *    variant?}` — icon-font lookups against @aurigo's own IcoMoon icon set
 *    (`aurigo-material-icons.json`), NOT arbitrary JSX/Lucide elements. Every call
 *    site in this app instead composes `<Button><RefreshCw/> Sync from GIS</Button>`
 *    style children. `parseButtonChildren` below reconstructs `text`/`leftIcon`/
 *    `rightIcon` from `children` at render time so the ~272 "clean" call sites
 *    (icon + text, text-only, icon-only, or a mutually-exclusive loading-spinner
 *    ternary that only ever resolves to one element at a time) never had to change.
 *
 * 2. `ref` forwards to Syncfusion's `ButtonComponent` widget instance (wrapped in an
 *    extra `<div className="button">`), not a plain `HTMLButtonElement`. Verified:
 *    zero call sites in this app pass a `ref` into `<Button>` (grepped), so this is
 *    a real but currently-dormant type lie — the exported type still says
 *    `HTMLButtonElement` for API stability, but do not rely on `ref.current` being
 *    a DOM node if a future caller adds one.
 *
 * 3. `asChild` is used TWO different ways in this app and RCButton supports NEITHER:
 *    a) `<Button asChild><Link to="...">Add Asset</Link></Button>` (Button's own
 *       composition, used to skin a TanStack Router `<Link>` as a button — 3 sites:
 *       AssetFederalComplianceTab.tsx x2, assets.index.tsx x1). RCButton always
 *       renders its own fixed `<div><ButtonComponent><span class="button-text">`
 *       markup — there is no "render as this other element" mode. This path
 *       intentionally NEVER touches RCButton; it keeps the exact pre-migration
 *       `cloneElement` behavior via `LEGACY_VARIANTS`/`LEGACY_SIZES` below.
 *    b) `<DropdownMenuTrigger asChild><Button>...</Button></DropdownMenuTrigger>`
 *       (Radix's *own* Slot, using `<Button>` as the clone target — 2 sites, both
 *       in JobOrderDetail.tsx: the handoff split-button chevron and the overflow
 *       kebab menu). Radix's Popper positioning needs a real DOM-node ref to call
 *       `getBoundingClientRect()` on; RCButton's ref is the EJ2 widget instance, not
 *       a DOM node, so this breaks positioning/detection silently. Fixed by NOT
 *       wrapping `<Button>` there anymore — see JobOrderDetail.tsx, both sites now
 *       render their own plain `<button>` with the equivalent Tailwind classes
 *       inlined (matching the pattern the file's third trigger, ScopeStatusChip,
 *       already used). This is a deliberate, narrow exception, not a precedent for
 *       every asChild+Trigger combination — re-check case by case.
 *
 * 4. RCButton always sets `className={generateClassName}` (its own
 *    `button-btn button-{size}-default background-color-{state}` string) and then
 *    spreads `...rest` AFTER it in `Aurigo-ReactCoreComponent/.../RCButton/Button.tsx`
 *    — since `className` is not destructured, any `className` we pass through
 *    `rest` REPLACES `generateClassName` outright (JSX prop spread is a full
 *    overwrite, not a merge). So this facade never passes a bare className through
 *    — it recomputes the exact same base classes itself (`rcBaseClass()`) and
 *    always includes them alongside our own tone/overlay/icon-only modifier
 *    classes and the caller's `className`.
 *
 * 5. `disabled` is similarly not destructured by RCButton — it computes the real
 *    DOM `disabled` attribute from `buttonState === 'disabled'` and ignores a raw
 *    `disabled` prop unless that prop happens to win the same spread-order
 *    footgun as (4). To avoid relying on spread order, this facade never passes a
 *    separate `disabled` — the `disabled` prop instead forces `buttonState`
 *    to `'disabled'` directly.
 *
 * 6. RCButton has no "destructive" / "ghost" / "link" tone — only default/
 *    secondary/pressed/hover/disabled *states*. Same category of gap as Badge's
 *    7-bg-vs-8-tone mismatch: `VARIANT_TO_RC_STATE` below maps to the closest
 *    structural state, and the actual pixel color for destructive/ghost/link comes
 *    from `button-tone-*` classes in index.css with higher cascade order than
 *    RCButton's own copied `background-color-*` rules (same "our compound class
 *    always wins" trick Badge already uses) — not from RCButton's own palette.
 *
 * 7. Icon-name coverage: RCButton's icon font (`aurigo-material-icons.json`) does
 *    not cover most Lucide icons used in this app (no spinner/loading glyph, no
 *    generic "more"/kebab beyond `more-horiz`, no chevron-left/up, no shield/pin/
 *    sparkle/pause glyphs, etc.). `LUCIDE_TO_RC_ICON` maps only the icons with a
 *    genuine 1:1 semantic match (~30). Everything else — most importantly every
 *    `<Loader2 className="animate-spin" />` loading spinner, which RCButton's Icon
 *    component cannot animate at all — renders as the *original* Lucide element,
 *    absolutely positioned over RCButton's own chrome (`.rc-button-icon-overlay`),
 *    rather than being silently dropped or forced into a wrong-shaped icon.
 *
 * 8. Content that doesn't fit the "at most one leading icon + at most one trailing
 *    icon + flattened text" shape (e.g. a nested `<Badge>`, more than two icon-like
 *    elements simultaneously) is a genuine gap, not something to force through
 *    RCButton. `parseButtonChildren` flags this (`ok: false`) and this facade falls
 *    back to the exact pre-migration plain `<button>` render for that one instance
 *    only — verified via repo-wide scan that every real multi-element `<Button>`
 *    body in this app is a mutually-exclusive loading-state ternary (only one
 *    element ever survives at render time), so this fallback path is not known to
 *    be exercised by any current call site, but it exists as a safety net rather
 *    than a silent corruption if a future one doesn't fit.
 */

// ── Icon extraction ─────────────────────────────────────────────────────────

/** Recursively flattens `children`, unwrapping `<>...</>` Fragments (the shape
 *  every `{cond ? <><Icon/> text</> : 'text'}` loading-state pattern produces)
 *  and dropping `null`/`undefined`/boolean children (what `cond && <Icon/>`
 *  evaluates to when `cond` is false — React.Children still hands these to us
 *  even though React itself renders nothing for them). */
function flattenChildren(children: React.ReactNode, out: React.ReactNode[] = []): React.ReactNode[] {
  React.Children.forEach(children, (child) => {
    if (child == null || typeof child === 'boolean') return
    if (React.isValidElement(child) && child.type === React.Fragment) {
      flattenChildren((child.props as { children?: React.ReactNode }).children, out)
    } else {
      out.push(child)
    }
  })
  return out
}

interface ParsedButtonContent {
  text: string
  leftIcon?: React.ReactElement
  rightIcon?: React.ReactElement
  /** false when `children` doesn't fit the icon(s)+text shape RCButton supports. */
  ok: boolean
}

function parseButtonChildren(children: React.ReactNode): ParsedButtonContent {
  const flat = flattenChildren(children)
  const iconEls: { el: React.ReactElement; index: number }[] = []
  const textParts: string[] = []
  let ok = true

  flat.forEach((node, index) => {
    if (typeof node === 'string' || typeof node === 'number') {
      textParts.push(String(node))
    } else if (React.isValidElement(node)) {
      // A plain host element (span/strong/em/...) whose own children are all
      // plain strings is just a styling wrapper around a text run — e.g.
      // IntegrationCard's `<span className="ml-1">Sync now</span>` next to a
      // leading icon (margin-only, no semantic difference from a bare
      // string). Unwrap it into the text stream instead of treating it as an
      // (unrecognized) icon slot, so this facade doesn't unnecessarily give
      // up RCButton for one of the most common real patterns.
      const wrapperText = simpleHostElementText(node)
      if (wrapperText != null) {
        textParts.push(wrapperText)
      } else if (isLucideIcon(node)) {
        // Only trust an element as an icon slot if it's a real Lucide icon
        // component (mapped or not — unmapped ones still get the overlay
        // treatment). Anything else (a <Badge>, a custom component) is NOT
        // icon-shaped — the overlay slot is sized/positioned for a ~14px
        // glyph, not arbitrary content — so it's marked unparseable and
        // falls back to the legacy plain <button> render (mismatch #8)
        // instead of silently mis-rendering.
        iconEls.push({ el: node, index })
      } else {
        ok = false
      }
    } else {
      ok = false
    }
  })

  if (iconEls.length > 2) ok = false

  let leftIcon: React.ReactElement | undefined
  let rightIcon: React.ReactElement | undefined
  if (iconEls.length === 1) {
    const { el, index } = iconEls[0]
    // Trailing only if it's genuinely the last item with text before it
    // (e.g. `{label}<ChevronDown/>`) — otherwise treat as leading, which
    // covers both `<Icon/> text` and lone icon-only buttons.
    const isTrailing = flat.length > 1 && index === flat.length - 1 && textParts.length > 0
    if (isTrailing) rightIcon = el
    else leftIcon = el
  } else if (iconEls.length === 2) {
    // Source order: first = left, second = right. True for every real
    // multi-icon site found in this app (leading status icon + trailing
    // chevron, or two mutually-exclusive conditional icons that never both
    // survive at once).
    leftIcon = iconEls[0].el
    rightIcon = iconEls[1].el
  }

  return { text: textParts.join('').trim(), leftIcon, rightIcon, ok }
}

/** Closest 1:1 semantic match in @aurigo's IcoMoon icon set
 *  (Aurigo-ReactCoreComponent/src/assets/Icons/aurigo-material-icons.json).
 *  Deliberately conservative — an icon is only listed here when it depicts the
 *  same real-world referent as the Lucide glyph it replaces. Everything else
 *  (spinners, shields, pins, sparkles, pause/ban/unlock, etc.) is intentionally
 *  left out so it falls through to the Lucide-overlay path instead of showing
 *  a wrong or misleading glyph. */
const LUCIDE_TO_RC_ICON = new Map<unknown, string>([
  [LucideIcons.RefreshCw, 'refresh'],
  [LucideIcons.RotateCcw, 'restore'],
  [LucideIcons.Plus, 'big-plus'],
  [LucideIcons.PlusCircle, 'circle-add'],
  [LucideIcons.Check, 'check'],
  [LucideIcons.CheckCircle, 'check-circle'],
  [LucideIcons.CheckCircle2, 'check-circle'],
  [LucideIcons.Trash2, 'delete'],
  [LucideIcons.Download, 'download'],
  [LucideIcons.Edit2, 'edit'],
  [LucideIcons.Pencil, 'edit'],
  [LucideIcons.PencilLine, 'edit'],
  [LucideIcons.Filter, 'filter'],
  [LucideIcons.SlidersHorizontal, 'filter-list'],
  [LucideIcons.Search, 'search'],
  [LucideIcons.Settings, 'settings'],
  [LucideIcons.Cog, 'settings'],
  [LucideIcons.X, 'cancel'],
  [LucideIcons.XCircle, 'cancel-circle'],
  [LucideIcons.Info, 'info'],
  [LucideIcons.AlertTriangle, 'warning'],
  [LucideIcons.TriangleAlert, 'warning'],
  [LucideIcons.HelpCircle, 'help'],
  [LucideIcons.Home, 'home'],
  [LucideIcons.Copy, 'content-copy'],
  [LucideIcons.MoreHorizontal, 'more-horiz'],
  [LucideIcons.MoreVertical, 'more-horiz'],
  [LucideIcons.ChevronDown, 'keyboard-arrow-down'],
  [LucideIcons.ChevronRight, 'keyboard-arrow-right'],
  [LucideIcons.ExternalLink, 'open-in-new'],
  [LucideIcons.Send, 'send'],
  [LucideIcons.Star, 'star'],
  [LucideIcons.Eye, 'visibility'],
  [LucideIcons.Calendar, 'calendar-month'],
  [LucideIcons.CalendarClock, 'calendar-month'],
  [LucideIcons.ArrowLeft, 'arrow-back'],
  [LucideIcons.ArrowRight, 'arrow-forward'],
  [LucideIcons.ArrowUp, 'arrow-upward'],
  [LucideIcons.ArrowDown, 'arrow-down'],
  [LucideIcons.Upload, 'import'],
  [LucideIcons.Paperclip, 'attach-file'],
  [LucideIcons.Printer, 'print'],
  [LucideIcons.Lock, 'lock'],
])

function rcIconName(el: React.ReactElement): string | undefined {
  return LUCIDE_TO_RC_ICON.get(el.type)
}

/** Every export of `lucide-react` that could appear as a JSX element type —
 *  used to distinguish "a real icon we should extract into leftIcon/rightIcon
 *  or overlay" from "arbitrary element content that doesn't fit RCButton's
 *  shape" (see the `ok: false` fallback in parseButtonChildren). */
const ALL_LUCIDE_ICONS = new Set<unknown>(Object.values(LucideIcons))

function isLucideIcon(el: React.ReactElement): boolean {
  return ALL_LUCIDE_ICONS.has(el.type)
}

/** Returns the flattened text of a plain *styling-only* host element
 *  (`<span className="ml-1">Sync now</span>` — the actual shape used by
 *  IntegrationCard.tsx) — a pure text wrapper, not a component with its own
 *  behavior. Deliberately narrow: only unwraps when the element carries
 *  nothing but `className`/`style`/`key` and string/number children, so a
 *  `data-testid`, `aria-*`, `onClick`, or any other meaningful attribute on
 *  the wrapper is never silently dropped — that element instead falls
 *  through to `isLucideIcon` (false) and marks the whole button unparseable
 *  (mismatch #8), which is the safer outcome for anything with real behavior
 *  attached. */
function simpleHostElementText(el: React.ReactElement): string | null {
  if (typeof el.type !== 'string') return null
  const props = el.props as Record<string, unknown>
  const extraKeys = Object.keys(props).filter((k) => k !== 'children' && k !== 'className' && k !== 'style' && k !== 'key')
  if (extraKeys.length > 0) return null
  const kids = React.Children.toArray(props.children as React.ReactNode)
  if (kids.length === 0) return null
  const parts: string[] = []
  for (const kid of kids) {
    if (typeof kid === 'string' || typeof kid === 'number') parts.push(String(kid))
    else return null
  }
  return parts.join('')
}

// ── Legacy Tailwind classes ──────────────────────────────────────────────────
// Kept verbatim from the pre-migration implementation. Used ONLY by the two
// codepaths that never touch RCButton: Button's own `asChild` composition
// (mismatch #3a above) and the "content doesn't parse" fallback (#8 above).

const LEGACY_BASE =
  'inline-flex items-center justify-center gap-2 whitespace-nowrap font-medium ' +
  'rounded-md select-none ' +
  'transition-[background-color,color,box-shadow,transform] duration-150 ease-out ' +
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background ' +
  'disabled:opacity-50 disabled:pointer-events-none active:translate-y-px'

const LEGACY_VARIANTS: Record<Variant, string> = {
  default:
    'bg-primary text-primary-foreground shadow-sm hover:bg-primary/90 active:bg-primary',
  destructive:
    'bg-destructive text-destructive-foreground shadow-sm hover:bg-destructive/90 active:bg-destructive',
  outline:
    'border border-border-strong/70 bg-card text-foreground hover:bg-muted hover:border-border-strong',
  secondary:
    'bg-muted text-foreground hover:bg-subtle',
  ghost:
    'text-foreground hover:bg-muted',
  link:
    'text-primary underline-offset-4 hover:underline px-0 h-auto',
}

const LEGACY_SIZES: Record<Size, string> = {
  default: 'h-9 px-4 text-sm',
  sm:      'h-8 px-3 text-xs',
  lg:      'h-10 px-5 text-sm',
  icon:    'h-9 w-9 p-0',
}

/**
 * Exposes the pre-migration Tailwind button classes for the rare call site
 * that needs "looks like a Button" without being one — specifically, a
 * Radix `<XxxTrigger asChild>` target (mismatch #3b above), where wrapping
 * the real `<Button>` breaks Popper positioning because RCButton's ref is
 * the EJ2 widget instance, not a DOM node. Those sites render their own
 * plain `<button>`/native element styled with this instead. See
 * JobOrderDetail.tsx (HandoffSplitButton's chevron, OverflowMenu's kebab)
 * for the only two current call sites.
 */
export function buttonClassName(variant: Variant = 'default', size: Size = 'default', className?: string) {
  return cn(LEGACY_BASE, LEGACY_VARIANTS[variant], LEGACY_SIZES[size], className)
}

// ── RCButton mapping ─────────────────────────────────────────────────────────

type RCButtonSize = 'small' | 'medium' | 'large'
type RCButtonState = 'default' | 'secondary-default' | 'disabled'

const SIZE_TO_RC: Record<Size, RCButtonSize> = {
  default: 'medium',
  sm: 'small',
  lg: 'large',
  icon: 'small',
}

// Closest real RCButton `buttonState` per app variant — see file header
// mismatch #6 for why exact color fidelity doesn't depend on this being 1:1;
// the actual paint comes from `button-tone-*` in index.css.
const VARIANT_TO_RC_STATE: Record<Variant, RCButtonState> = {
  default: 'default',
  destructive: 'default',
  outline: 'secondary-default',
  secondary: 'secondary-default',
  ghost: 'secondary-default',
  link: 'secondary-default',
}

/** Replicates RCButton's own `generateClassName` (see mismatch #4) so passing
 *  our own `className` never drops the base structural classes it depends on. */
function rcBaseClass(buttonSize: RCButtonSize, buttonState: RCButtonState) {
  return `button-btn button-${buttonSize}-default background-color-${buttonState}`
}

interface RCButtonElementProps {
  text?: string
  buttonSize?: RCButtonSize
  buttonState?: RCButtonState
  isLeftIcon?: boolean
  isRightIcon?: boolean
  leftIcon?: { name: string; color?: string }
  rightIcon?: { name: string; color?: string }
  id?: string
  className?: string
  type?: string
  title?: string
  form?: string
  name?: string
  onClick?: React.MouseEventHandler
  ref?: React.Ref<unknown>
}

const RCButton = asReactComponent<RCButtonElementProps>(RCButtonRaw)

export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  (
    { className, variant = 'default', size = 'default', asChild, children, type = 'button', disabled, onClick, title, id, form, name, ...rest },
    ref,
  ) => {
    // Mismatch #3a — Button's own asChild, styling an arbitrary child (almost
    // always a <Link>). RCButton has no equivalent mode; never touch it here.
    if (asChild) {
      const cls = cn(LEGACY_BASE, LEGACY_VARIANTS[variant], LEGACY_SIZES[size], className)
      const child = React.Children.only(children) as React.ReactElement<any>
      return React.cloneElement(child, {
        ...(child.props as object),
        className: cn(cls, (child.props as any).className),
      })
    }

    const parsed = parseButtonChildren(children)

    if (!parsed.ok) {
      // Mismatch #8 — content doesn't fit RCButton's shape. Fall back to the
      // pre-migration plain <button> rather than mangling it.
      const cls = cn(LEGACY_BASE, LEGACY_VARIANTS[variant], LEGACY_SIZES[size], className)
      return (
        <button
          ref={ref}
          type={type}
          className={cls}
          disabled={disabled}
          onClick={onClick}
          title={title}
          id={id}
          form={form}
          name={name}
          {...rest}
        >
          {children}
        </button>
      )
    }

    const buttonSize = SIZE_TO_RC[size]
    const buttonState: RCButtonState = disabled ? 'disabled' : VARIANT_TO_RC_STATE[variant]
    const isIconOnly = !parsed.text && (!!parsed.leftIcon || !!parsed.rightIcon)

    const leftName = parsed.leftIcon ? rcIconName(parsed.leftIcon) : undefined
    const rightName = parsed.rightIcon ? rcIconName(parsed.rightIcon) : undefined
    // Mismatch #7 — icons with no real RC icon-font match (most importantly
    // any spinner) render as an absolutely positioned overlay instead of
    // being dropped or misrepresented.
    const leftOverlay = parsed.leftIcon && !leftName ? parsed.leftIcon : null
    const rightOverlay = parsed.rightIcon && !rightName ? parsed.rightIcon : null
    const hasOverlay = !!(leftOverlay || rightOverlay)

    return (
      <span className={cn('rc-button-wrap', hasOverlay && 'relative inline-flex')}>
        <RCButton
          ref={ref as unknown as React.Ref<unknown>}
          id={id}
          text={parsed.text}
          buttonSize={buttonSize}
          buttonState={buttonState}
          isLeftIcon={!!leftName}
          isRightIcon={!!rightName}
          leftIcon={leftName ? { name: leftName } : undefined}
          rightIcon={rightName ? { name: rightName } : undefined}
          type={type}
          title={title}
          form={form}
          name={name}
          onClick={onClick}
          className={cn(
            rcBaseClass(buttonSize, buttonState),
            `button-tone-${variant}`,
            isIconOnly && 'button-icon-only',
            leftOverlay && 'has-left-overlay',
            rightOverlay && 'has-right-overlay',
            className,
          )}
          {...rest}
        />
        {leftOverlay && (
          <span className="rc-button-icon-overlay rc-button-icon-overlay-left" aria-hidden>
            {leftOverlay}
          </span>
        )}
        {rightOverlay && (
          <span className="rc-button-icon-overlay rc-button-icon-overlay-right" aria-hidden>
            {rightOverlay}
          </span>
        )}
      </span>
    )
  },
)
Button.displayName = 'Button'
