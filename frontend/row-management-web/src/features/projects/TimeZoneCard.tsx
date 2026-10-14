import { useEffect, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { projectsApi } from '@/api/projects'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { ErrorState, LoadingRows } from '@/components/ui/states'
import { dateText, fieldErrorsOf, FormError, messageOf, SelectField } from './form-kit'

/**
 * Administrators choose the time zone the agency counts days in (ROW-073). Deadlines, "due today" and "completed on" are
 * calendar days, and the same moment is a different day in different places, so this decides when a day ends. A project that
 * is somewhere else can have its own zone on its record.
 */
export function TimeZoneCard() {
  const { manager } = useAuth()
  const api = projectsApi(manager.api)
  const queryClient = useQueryClient()
  const canEdit = manager.can('administration', 'update')
  const zone = useQuery({ queryKey: ['agency-time-zone'], queryFn: () => api.agencyTimeZone() })
  const [value, setValue] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    if (zone.data) setValue(zone.data.timeZoneId)
  }, [zone.data])

  const save = useMutation({
    mutationFn: () => api.setAgencyTimeZone(value),
    onSuccess: async () => {
      setSaved(true)
      await queryClient.invalidateQueries({ queryKey: ['agency-time-zone'] })
      // Every deadline and reminder is counted on this calendar.
      await queryClient.invalidateQueries({ queryKey: ['parcel'] })
      await queryClient.invalidateQueries({ queryKey: ['reminders'] })
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
    <section aria-label="Time zone" className="mt-6 max-w-2xl rounded-lg border bg-card p-4">
      <h2 className="text-sm font-semibold">Time zone</h2>
      <p className="mb-3 text-xs text-muted-foreground">
        Deadlines, &ldquo;due today&rdquo; and completion dates are counted in this time zone. A project in another part of the state can
        have its own time zone on its Overview tab; every other project follows this one.
      </p>
      {zone.isPending ? <LoadingRows rows={2} /> : zone.isError ? (
        <ErrorState title="The time zone could not be loaded" retry={() => void zone.refetch()} />
      ) : (
        <form onSubmit={submit} noValidate aria-label="Agency time zone" className="space-y-3">
          <SelectField
            label="Agency time zone" value={value} onChange={(v) => setValue(v)} disabled={!canEdit} error={errors.timeZoneId}
            options={zone.data.options.map((o) => ({ value: o.id, label: o.label }))}
          />
          <p className="text-sm" aria-live="polite">Today&apos;s date there: <strong>{dateText(zone.data.today)}</strong></p>
          <FormError error={formError} />
          {saved && <p role="status" className="text-sm">Saved.</p>}
          {canEdit && <button type="submit" className={buttonClassName('default')} disabled={save.isPending || !value}>{save.isPending ? 'Saving…' : 'Save time zone'}</button>}
        </form>
      )}
    </section>
  )
}
