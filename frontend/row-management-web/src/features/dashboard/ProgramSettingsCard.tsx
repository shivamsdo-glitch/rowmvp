import { useEffect, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { dashboardApi, type ProgramRules, type ProgramSettings } from '@/api/dashboard'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { ErrorState, LoadingRows } from '@/components/ui/states'
import { FormError, messageOf, TextField } from '../projects/form-kit'

type Draft = Record<keyof ProgramRules, string>

const FIELDS: { key: keyof ProgramRules; label: string; hint: string; max: number }[] = [
  { key: 'stuckDays', label: 'Days on one step before a parcel is stuck', hint: 'Stuck parcels are counted on the Program view and make a project "Watch".', max: 365 },
  { key: 'clearDateWarningDays', label: 'Warn this many days before the ROW clear date', hint: 'Inside this window a project is "Watch" if too little is closed.', max: 365 },
  { key: 'clearDateWarningPercentClosed', label: 'Percent closed expected inside that window', hint: 'Below this share closed, a project near its clear date is "Watch".', max: 100 },
  { key: 'redOverdueMinimumParcels', label: 'Overdue parcels before a project is "Off track" (at least)', hint: 'Together with the share below. Fewer overdue parcels only make it "Watch".', max: 1000 },
  { key: 'redOverduePercent', label: 'Share of the project overdue before it is "Off track" (%)', hint: 'Both this and the number above must be reached.', max: 100 },
]

const toDraft = (r: ProgramRules): Draft => ({
  stuckDays: String(r.stuckDays), clearDateWarningDays: String(r.clearDateWarningDays), clearDateWarningPercentClosed: String(r.clearDateWarningPercentClosed),
  redOverdueMinimumParcels: String(r.redOverdueMinimumParcels), redOverduePercent: String(r.redOverduePercent),
})

/**
 * Administrators set what the ROW Director's Program view judges by (ADR 0025): the days before a parcel counts as stuck, the health
 * thresholds, and which team owns each workflow step. The defaults are the ones ADR 0024 set, and Reset puts them back.
 */
export function ProgramSettingsCard() {
  const { manager } = useAuth()
  const api = dashboardApi(manager.api)
  const queryClient = useQueryClient()
  const canEdit = manager.can('administration', 'update')
  const query = useQuery({ queryKey: ['program-settings'], queryFn: () => api.settings() })
  const [draft, setDraft] = useState<Draft | null>(null)
  const [teams, setTeams] = useState<Record<string, string>>({})
  const [errors, setErrors] = useState<Partial<Record<keyof ProgramRules, string>>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [saved, setSaved] = useState<string | null>(null)

  function load(s: ProgramSettings) {
    setDraft(toDraft(s.rules))
    setTeams(Object.fromEntries(s.steps.map((x) => [x.stepKey, x.team])))
  }
  useEffect(() => {
    if (query.data) load(query.data)
  }, [query.data])

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: ['program-settings'] })
    await queryClient.invalidateQueries({ queryKey: ['dashboard'] }) // the Program view reads these
  }

  const save = useMutation({
    mutationFn: (s: ProgramSettings) => {
      const n = (k: keyof ProgramRules) => Number(draft![k])
      return api.saveSettings({
        stuckDays: n('stuckDays'), clearDateWarningDays: n('clearDateWarningDays'), clearDateWarningPercentClosed: n('clearDateWarningPercentClosed'),
        redOverdueMinimumParcels: n('redOverdueMinimumParcels'), redOverduePercent: n('redOverduePercent'),
        steps: s.steps.map((x) => ({ stepKey: x.stepKey, team: (teams[x.stepKey] ?? x.team).trim() })),
      })
    },
    onSuccess: async () => { setSaved('Saved. The Program view now uses these settings.'); await refresh() },
    onError: (e) => { setSaved(null); setFormError(messageOf(e)) },
  })
  const reset = useMutation({
    mutationFn: () => api.resetSettings(),
    onSuccess: async () => { setSaved('Back to the default settings.'); await refresh() },
    onError: (e) => { setSaved(null); setFormError(messageOf(e)) },
  })

  function submit(e: FormEvent) {
    e.preventDefault()
    if (!query.data || !draft) return
    setSaved(null); setFormError(null)
    const found: typeof errors = {}
    for (const f of FIELDS) {
      const text = draft[f.key].trim()
      const v = Number(text)
      if (text === '' || !Number.isInteger(v) || v < 1 || v > f.max) found[f.key] = `Enter a whole number from 1 to ${f.max}.`
    }
    const blank = query.data.steps.find((s) => (teams[s.stepKey] ?? '').trim() === '')
    setErrors(found)
    if (Object.keys(found).length > 0) return
    if (blank) { setFormError(`Give "${blank.label}" a team.`); return }
    save.mutate(query.data)
  }

  return (
    <section aria-label="Program view settings" className="mt-6 max-w-3xl rounded-lg border bg-card p-4">
      <h2 className="text-sm font-semibold">Program view settings</h2>
      <p className="mb-3 text-xs text-muted-foreground">
        What the ROW Director&rsquo;s Program view judges by: when a parcel counts as stuck, when a project is on watch or off track, and which team owns each step.
      </p>
      {query.isError ? (
        <ErrorState title="The program settings could not be loaded" retry={() => void query.refetch()} />
      ) : query.isPending || !draft || !query.data ? <LoadingRows rows={3} /> : (
        <form onSubmit={submit} noValidate aria-label="Program view settings" className="space-y-4">
          <div className="grid gap-3 sm:grid-cols-2">
            {FIELDS.map((f) => (
              <TextField
                key={f.key} label={f.label} type="number" inputMode="numeric" value={draft[f.key]} disabled={!canEdit} error={errors[f.key]}
                hint={`${f.hint} Default: ${query.data.defaults[f.key]}.`} onChange={(v) => setDraft({ ...draft, [f.key]: v })}
              />
            ))}
          </div>

          <div>
            <h3 className="mb-1 text-xs font-semibold">Which team owns each step</h3>
            <p className="mb-2 text-[11px] text-muted-foreground">Time in a step is added up under its team. Steps given the same team are pooled. Pick a team from the list or type a new name.</p>
            <datalist id="program-teams">{query.data.teams.map((t) => <option key={t} value={t} />)}</datalist>
            <div className="overflow-x-auto rounded-md border">
              <table className="w-full text-sm">
                <caption className="sr-only">Team for each workflow step</caption>
                <thead className="bg-muted text-left text-xs uppercase text-muted-foreground">
                  <tr><th scope="col" className="px-3 py-2 font-medium">Step</th><th scope="col" className="px-3 py-2 font-medium">Team</th><th scope="col" className="px-3 py-2 font-medium">Default</th></tr>
                </thead>
                <tbody className="divide-y">
                  {query.data.steps.map((s) => (
                    <tr key={s.stepKey}>
                      <td className="px-3 py-1.5">{s.label}</td>
                      <td className="px-3 py-1.5">
                        <input
                          list="program-teams" aria-label={`Team for ${s.label}`} value={teams[s.stepKey] ?? ''} maxLength={50} disabled={!canEdit}
                          onChange={(e) => setTeams({ ...teams, [s.stepKey]: e.target.value })}
                          className="w-full rounded-md border bg-background px-2 py-1 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                        />
                      </td>
                      <td className="px-3 py-1.5 text-xs text-muted-foreground">{s.defaultTeam}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          <FormError error={formError} />
          {saved && <p role="status" className="text-sm">{saved}</p>}
          {canEdit ? (
            <div className="flex flex-wrap gap-2">
              <button type="submit" className={buttonClassName('default')} disabled={save.isPending || reset.isPending}>{save.isPending ? 'Saving…' : 'Save program settings'}</button>
              <button type="button" className={buttonClassName('outline')} disabled={save.isPending || reset.isPending} onClick={() => { setSaved(null); setFormError(null); setErrors({}); reset.mutate() }}>
                {reset.isPending ? 'Resetting…' : 'Reset to defaults'}
              </button>
            </div>
          ) : <p className="text-xs text-muted-foreground">Only an administrator can change these.</p>}
        </form>
      )}
    </section>
  )
}
