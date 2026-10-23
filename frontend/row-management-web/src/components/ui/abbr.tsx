import { Fragment, useId, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ABBREVIATIONS, splitAbbreviations } from '@/lib/abbreviations'

/** An abbreviation with a tooltip that says what it stands for (on hover; screen readers get it as the description). */
export function Abbr({ term, children }: { term: string; children?: ReactNode }) {
  const meaning = ABBREVIATIONS[term]
  const id = useId()
  const ref = useRef<HTMLElement>(null)
  const [at, setAt] = useState<{ left: number; top: number } | null>(null)
  if (!meaning) return <>{children ?? term}</>
  const show = () => {
    const r = ref.current?.getBoundingClientRect()
    if (r) setAt({ left: Math.max(8, Math.min(r.left, window.innerWidth - 288)), top: r.bottom + 6 })
  }
  return (
    <>
      <abbr ref={ref} {...{ 'aria-description': meaning }} aria-describedby={at ? id : undefined} onMouseEnter={show} onMouseLeave={() => setAt(null)}
        className="cursor-help underline decoration-dotted underline-offset-2">{children ?? term}</abbr>
      {at && createPortal(
        <div role="tooltip" id={id} style={{ position: 'fixed', left: at.left, top: at.top }}
          className="pointer-events-none z-[100] w-72 rounded-md bg-foreground px-3 py-2 text-left text-xs font-normal normal-case leading-snug tracking-normal text-background shadow-lg">
          <strong>{term}</strong>: {meaning}
        </div>, document.body)}
    </>
  )
}

/** Plain text in which every known abbreviation carries its tooltip. Text with none comes back unchanged. */
export function Explained({ text }: { text: string }) {
  const parts = splitAbbreviations(text)
  if (!parts.some((p) => p.term)) return <>{text}</>
  return <>{parts.map((p, i) => (p.term ? <Abbr key={i} term={p.term} /> : <Fragment key={i}>{p.text}</Fragment>))}</>
}
