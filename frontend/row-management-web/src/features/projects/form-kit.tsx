import { useId, type ReactNode } from 'react'
import { ApiError } from '@/api/client'
import { cn } from '@/lib/cn'

const INPUT =
  'h-9 w-full rounded-md border border-input bg-background px-3 text-sm text-foreground placeholder:text-muted-foreground ' +
  'focus:outline-none focus:ring-2 focus:ring-ring disabled:opacity-60'

/** A labelled control with its hint and error wired up for assistive technology. */
export function Field({
  label, required, hint, error, children,
}: {
  label: string
  required?: boolean
  hint?: string
  error?: string
  children: (props: { id: string; 'aria-invalid': boolean; 'aria-describedby': string | undefined }) => ReactNode
}) {
  const id = useId()
  const describedBy = error ? `${id}-error` : hint ? `${id}-hint` : undefined
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} className="text-xs font-medium text-foreground">
        {label}
        {required && <span aria-hidden className="text-danger"> *</span>}
      </label>
      {children({ id, 'aria-invalid': !!error, 'aria-describedby': describedBy })}
      {error ? (
        <p id={`${id}-error`} role="alert" className="text-xs text-danger">{error}</p>
      ) : hint ? (
        <p id={`${id}-hint`} className="text-[11px] text-muted-foreground">{hint}</p>
      ) : null}
    </div>
  )
}

export function TextField({
  label, value, onChange, required, hint, error, type = 'text', placeholder, maxLength, disabled, inputMode,
}: {
  label: string
  value: string
  onChange: (v: string) => void
  required?: boolean
  hint?: string
  error?: string
  type?: 'text' | 'date' | 'datetime-local' | 'number' | 'password'
  placeholder?: string
  maxLength?: number
  disabled?: boolean
  inputMode?: 'decimal' | 'numeric' | 'text'
}) {
  return (
    <Field label={label} required={required} hint={hint} error={error}>
      {(a) => (
        <input
          {...a}
          type={type}
          className={cn(INPUT, error && 'border-danger')}
          value={value}
          placeholder={placeholder}
          maxLength={maxLength}
          disabled={disabled}
          inputMode={inputMode}
          step={type === 'number' ? 'any' : undefined}
          min={type === 'number' ? 0 : undefined}
          onChange={(e) => onChange(e.target.value)}
        />
      )}
    </Field>
  )
}

export function SelectField<T extends string>({
  label, value, onChange, options, required, error, blankLabel, disabled,
}: {
  label: string
  value: T | ''
  onChange: (v: T | '') => void
  options: readonly { value: T; label: string }[]
  required?: boolean
  error?: string
  /** Adds a first empty option with this label (for optional selects). */
  blankLabel?: string
  disabled?: boolean
}) {
  return (
    <Field label={label} required={required} error={error}>
      {(a) => (
        <select
          {...a}
          className={cn(INPUT, error && 'border-danger')}
          value={value}
          disabled={disabled}
          onChange={(e) => onChange(e.target.value as T | '')}
        >
          {blankLabel !== undefined && <option value="">{blankLabel}</option>}
          {options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
        </select>
      )}
    </Field>
  )
}

export function CheckboxField({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  const id = useId()
  return (
    <div className="flex items-center gap-2 pt-5">
      <input id={id} type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} className="h-4 w-4" />
      <label htmlFor={id} className="text-sm">{label}</label>
    </div>
  )
}

/** Field errors by form-field name (camelCase), from the API's ProblemDetails `errors` (PascalCase property names). */
export function fieldErrorsOf(e: unknown): Record<string, string> {
  const out: Record<string, string> = {}
  if (e instanceof ApiError) {
    for (const [k, msgs] of Object.entries(e.fieldErrors)) out[k.charAt(0).toLowerCase() + k.slice(1)] = msgs.join(' ')
  }
  return out
}

/** The message for a failed request that is not tied to one field. */
export function messageOf(e: unknown): string {
  if (e instanceof ApiError) {
    if (e.status === 403) return 'Your role does not allow this.'
    if (e.status === 409) return e.message
    return e.message
  }
  return 'Something went wrong. Please try again.'
}

export function FormError({ error }: { error: string | null }) {
  return error ? <p role="alert" className="text-sm text-danger">{error}</p> : null
}

export const money = (v: number | null | undefined) =>
  v === null || v === undefined ? '—' : new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 }).format(v)

export const dateText = (v: string | null | undefined) => {
  if (!v) return '—'
  const [y, m, d] = v.slice(0, 10).split('-').map(Number)
  return new Date(y, m - 1, d).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' })
}

/** '' → null; otherwise the trimmed text. */
export const orNull = (s: string) => (s.trim() === '' ? null : s.trim())
/** '' → null; otherwise a number (NaN is kept so the server's validation reports it). */
export const numOrNull = (s: string) => (s.trim() === '' ? null : Number(s))

export function Pill({ tone, children }: { tone: 'ok' | 'warn' | 'neutral'; children: ReactNode }) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        tone === 'ok' && 'bg-ok/20 text-foreground',
        tone === 'warn' && 'bg-warn/20 text-foreground',
        tone === 'neutral' && 'bg-muted text-muted-foreground',
      )}
    >
      {children}
    </span>
  )
}

export function DataTable({ caption, headers, children }: { caption: string; headers: string[]; children: ReactNode }) {
  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-sm">
        <caption className="sr-only">{caption}</caption>
        <thead className="bg-muted text-left text-xs uppercase text-muted-foreground">
          <tr>{headers.map((h) => <th key={h} scope="col" className="px-3 py-2 font-medium">{h}</th>)}</tr>
        </thead>
        <tbody className="divide-y">{children}</tbody>
      </table>
    </div>
  )
}
