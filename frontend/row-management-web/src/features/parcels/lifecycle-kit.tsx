import { useState, type ReactNode } from 'react'
import type { DetailValues, StatusNote } from '@/api/parcel-tabs'
import { CheckboxField, SelectField, TextField, numOrNull, orNull } from '../projects/form-kit'
import { optionsWith } from '../library/library-kit'
import { Grid } from './NegotiationTab'

/** One field of a record's form: how it is labelled, what kind of input it is and where its choices come from. */
export interface FieldSpec {
  key: string
  label: string
  kind: 'date' | 'text' | 'money' | 'bool' | 'agent' | 'list'
  /** For kind "list": the Library values to pick from. */
  options?: string[]
  max?: number
  required?: boolean
}

export type FormState = Record<string, string | boolean>

export const date = (key: string, label: string): FieldSpec => ({ key, label, kind: 'date' })
export const text = (key: string, label: string, max = 200, required = false): FieldSpec => ({ key, label, kind: 'text', max, required })
export const money = (key: string, label: string): FieldSpec => ({ key, label, kind: 'money' })
export const flag = (key: string, label: string): FieldSpec => ({ key, label, kind: 'bool' })
export const agent = (key: string, label: string): FieldSpec => ({ key, label, kind: 'agent' })
export const list = (key: string, label: string, options: string[], required = false): FieldSpec => ({ key, label, kind: 'list', options, required })

export const blankState = (specs: FieldSpec[]): FormState => Object.fromEntries(specs.map((s) => [s.key, s.kind === 'bool' ? false : '']))

export function stateFrom(specs: FieldSpec[], values: DetailValues | null | undefined): FormState {
  return Object.fromEntries(specs.map((s) => {
    const v = values?.[s.key]
    return [s.key, s.kind === 'bool' ? v === true : v === null || v === undefined ? '' : String(v)]
  }))
}

export function bodyFrom(specs: FieldSpec[], state: FormState): DetailValues {
  return Object.fromEntries(specs.map((s) => {
    const v = state[s.key]
    if (s.kind === 'bool') return [s.key, v === true]
    if (s.kind === 'money') return [s.key, numOrNull(String(v))]
    return [s.key, orNull(String(v))]
  }))
}

/** Checks the rules that need no server: required fields, amounts of zero or more, and dates that must not go backwards ([earlier, later, message]). */
export function checkState(specs: FieldSpec[], state: FormState, order: [string, string, string][] = []): Record<string, string> {
  const errs: Record<string, string> = {}
  for (const s of specs) {
    const v = state[s.key]
    if (s.required && !String(v).trim()) errs[s.key] = `${s.label} is required.`
    if (s.kind === 'money' && String(v).trim() && (Number.isNaN(Number(v)) || Number(v) < 0)) errs[s.key] = 'The amount must be zero or more.'
  }
  for (const [a, b, message] of order) if (state[a] && state[b] && String(state[b]) < String(state[a])) errs[b] = message
  return errs
}

export function SpecFields({ specs, state, onChange, disabled, errors, agents, columns = 'sm:grid-cols-2' }: {
  specs: FieldSpec[]; state: FormState; onChange: (key: string, value: string | boolean) => void; disabled?: boolean; errors: Record<string, string>; agents: string[]; columns?: string
}) {
  return (
    <div className={`grid gap-3 ${columns}`}>
      {specs.map((s) => {
        const v = state[s.key]
        const err = errors[s.key]
        if (s.kind === 'bool') return <CheckboxField key={s.key} label={s.label} checked={v === true} onChange={(c) => !disabled && onChange(s.key, c)} disabled={disabled} />
        if (s.kind === 'agent') return <SelectField key={s.key} label={s.label} value={String(v)} onChange={(x) => onChange(s.key, x)} options={optionsWith(agents, String(v))} blankLabel="Select one" disabled={disabled} error={err} />
        if (s.kind === 'list') return <SelectField key={s.key} label={s.label} required={s.required} value={String(v)} onChange={(x) => onChange(s.key, x)} options={optionsWith(s.options ?? [], String(v))} blankLabel="Select one" disabled={disabled} error={err} />
        return (
          <TextField key={s.key} label={s.label} required={s.required} type={s.kind === 'date' ? 'date' : s.kind === 'money' ? 'number' : 'text'} inputMode={s.kind === 'money' ? 'decimal' : undefined}
            value={String(v)} onChange={(x) => onChange(s.key, x)} maxLength={s.kind === 'text' ? s.max : undefined} disabled={disabled} error={err} />
        )
      })}
    </div>
  )
}

/** The dated status notes grid every one of these records has, edited in the form and saved with it. */
export function StatusNotesGrid({ notes, onChange, canSave, title }: { notes: StatusNote[]; onChange: (n: StatusNote[]) => void; canSave: boolean; title: string }) {
  const [d, setD] = useState(() => new Date().toISOString().slice(0, 10))
  const [t, setT] = useState('')
  const [error, setError] = useState('')
  function add() {
    if (!t.trim()) return setError('Enter the status note.')
    onChange([...notes, { noteDate: d || new Date().toISOString().slice(0, 10), text: t.trim() }])
    setT(''); setError('')
  }
  return (
    <Grid title={title} caption={title} headers={['Date', 'Note']} rows={notes} canSave={canSave} cells={(r) => [r.noteDate, <span key="t" className="whitespace-pre-wrap">{r.text}</span>]}
      rowLabel={(_, i) => `Remove status note ${i + 1}`} onRemove={(i) => onChange(notes.filter((_, j) => j !== i))} addLabel="Add status note" onAdd={add} error={error}
      adding={<><TextField label="Status note date" type="date" value={d} onChange={setD} /><TextField label="Status note" value={t} onChange={setT} maxLength={4000} /></>} />
  )
}

export function Fact({ label, value }: { label: string; value: ReactNode }) {
  return <div className="text-sm"><span className="text-xs text-muted-foreground">{label}</span><div>{value || '—'}</div></div>
}
