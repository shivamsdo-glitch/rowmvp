import { useEffect, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { projectsApi } from '@/api/projects'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { ErrorState, LoadingRows } from '@/components/ui/states'
import { fieldErrorsOf, FormError, messageOf, SelectField, TextField } from './form-kit'

const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December']
  .map((label, i) => ({ value: String(i + 1), label }))

/** Administrators set how project numbers are generated (ROW-001): a pattern and the fiscal-year start month. */
export function NumberSettingsCard() {
  const { manager } = useAuth()
  const api = projectsApi(manager.api)
  const queryClient = useQueryClient()
  const canEdit = manager.can('administration', 'update')
  const settings = useQuery({ queryKey: ['project-number-settings'], queryFn: () => api.numberSettings() })
  const [pattern, setPattern] = useState('')
  const [month, setMonth] = useState('1')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    if (settings.data) {
      setPattern(settings.data.pattern)
      setMonth(String(settings.data.fiscalYearStartMonth))
    }
  }, [settings.data])

  const save = useMutation({
    mutationFn: () => api.setNumberSettings(pattern.trim(), Number(month)),
    onSuccess: async () => {
      setSaved(true)
      await queryClient.invalidateQueries({ queryKey: ['project-number-settings'] })
    },
    onError: (e) => {
      setErrors(fieldErrorsOf(e))
      setFormError(messageOf(e))
    },
  })

  function submit(e: FormEvent) {
    e.preventDefault()
    setSaved(false); setErrors({}); setFormError(null)
    save.mutate()
  }

  return (
    <section aria-label="Project numbers" className="max-w-2xl rounded-lg border bg-card p-4">
      <h2 className="text-sm font-semibold">Project numbers</h2>
      <p className="mb-3 text-xs text-muted-foreground">
        New projects are numbered from this pattern. Use {'{FY}'} (4-digit fiscal year), {'{YY}'} (2-digit) and exactly one {'{SEQ:n}'} (sequence padded to n digits).
        Existing project numbers never change. Users can still type or import their own numbers.
      </p>
      {settings.isPending ? <LoadingRows rows={2} /> : settings.isError ? (
        <ErrorState title="Settings could not be loaded" retry={() => void settings.refetch()} />
      ) : (
        <form onSubmit={submit} noValidate aria-label="Project number format" className="space-y-3">
          <div className="grid gap-3 sm:grid-cols-2">
            <TextField label="Number pattern" value={pattern} onChange={setPattern} error={errors.pattern} disabled={!canEdit} maxLength={100} />
            <SelectField label="Fiscal year starts in" value={month} onChange={(m) => setMonth(m || '1')} options={MONTHS} disabled={!canEdit} error={errors.fiscalYearStartMonth} />
          </div>
          <p className="text-sm" aria-live="polite">Next project number: <strong>{settings.data.example}</strong></p>
          <FormError error={formError} />
          {saved && <p role="status" className="text-sm">Saved.</p>}
          {canEdit && <button type="submit" className={buttonClassName('default')} disabled={save.isPending}>{save.isPending ? 'Saving…' : 'Save format'}</button>}
        </form>
      )}
    </section>
  )
}
