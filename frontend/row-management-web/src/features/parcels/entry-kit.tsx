import { useEffect, useRef, type ReactNode } from 'react'
import { ArrowLeft } from 'lucide-react'
import { buttonClassName } from '@/components/ui/button'
import { Explained } from '@/components/ui/abbr'
import { EmptyState } from '@/components/ui/states'

/**
 * How a tab shows one record on a page of its own (ADR 0028). `id` is the record being viewed, or null for the tab's list; `open` goes
 * to a record, `back` returns to the list. The parcel page owns it, so the tab strip stays and the record opens inside its own tab.
 */
export interface EntryNav {
  id: string | null
  open: (id: string) => void
  back: () => void
}

/** Starts editing the record named by `nav.id` as soon as the list has loaded (call before any early return). */
export function useEntryMode<T extends { id: string }>(nav: EntryNav | undefined, rows: T[] | undefined, editing: T | null, begin: (row: T) => void, reset: () => void) {
  const id = nav?.id ?? null
  const begunFor = useRef<string | null>(null)
  // Going back to the list (by any button) leaves the record: its form must not linger under the list.
  useEffect(() => {
    if (id) return
    begunFor.current = null
    if (editing) reset()
  })
  // Starts editing the record as soon as it is in the list; once per visit, whatever else changes meanwhile.
  useEffect(() => {
    if (!id || begunFor.current === id) return
    const row = rows?.find((r) => r.id === id)
    if (!row) return
    begunFor.current = id
    begin(row)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, rows])
  return { entryMode: id !== null, missing: id !== null && !editing && !!rows && !rows.some((r) => r.id === id) }
}

/** The bar at the top of a record's page: a way back to the tab's list and what is being viewed. */
export function EntryBar({ nav, label, what }: { nav: EntryNav; label: string; what: string }) {
  return (
    <div className="mb-4 flex items-center gap-3">
      <button type="button" className={buttonClassName('outline', 'sm')} onClick={nav.back}><ArrowLeft size={14} aria-hidden /> {label}</button>
      <h2 className="text-base font-semibold">{what}</h2>
    </div>
  )
}

export function EntryMissing({ nav, label }: { nav: EntryNav; label: string }) {
  return (
    <>
      <EmptyState title="This record could not be found" description="It may have been removed." />
      <button type="button" className={`${buttonClassName('outline', 'sm')} mt-3`} onClick={nav.back}>{label}</button>
    </>
  )
}

/**
 * A section of a form that is usually left blank. It starts folded so the form opens short, and unfolds by itself when the record already
 * has something in it, so nothing entered is ever hidden (ADR 0028).
 */
export function OptionalSection({ title, children }: { title: string; children: ReactNode }) {
  const ref = useRef<HTMLDetailsElement>(null)
  const closedByUser = useRef(false)
  const opening = useRef(false)
  useEffect(() => {
    const el = ref.current
    if (!el || el.open || closedByUser.current) return
    const filled = [...el.querySelectorAll<HTMLInputElement>('input, select, textarea')].some((i) => (i.type === 'checkbox' || i.type === 'radio' ? i.checked : i.value !== ''))
      || el.querySelector('table') !== null
    if (filled) { opening.current = true; el.open = true }
  })
  return (
    <details ref={ref} aria-label={title} className="mb-4 rounded-lg border bg-card" onToggle={(e) => {
      if (opening.current) { opening.current = false; return }
      closedByUser.current = !e.currentTarget.open
    }}>
      <summary className="cursor-pointer select-none px-4 py-3 text-sm font-semibold"><Explained text={title} /> <span className="text-xs font-normal text-muted-foreground">(optional)</span></summary>
      <div className="px-4 pb-4">{children}</div>
    </details>
  )
}
