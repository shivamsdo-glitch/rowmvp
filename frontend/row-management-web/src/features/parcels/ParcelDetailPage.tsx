import { useState, type FormEvent, type ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Printer, Trash2 } from 'lucide-react'
import {
  ACQUISITION_LABEL, INTEREST_LABEL, INTEREST_TYPES, NOTICE_LABEL, NOTICE_METHODS, parcelsApi,
  type InterestType, type NoticeMethod, type Parcel, type ParcelInput, type SplitResult,
} from '@/api/parcels'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { PageHeader } from '@/components/ui/page-header'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import {
  CheckboxField, DataTable, dateText, fieldErrorsOf, FormError, messageOf, orNull, Pill, SelectField, TextField,
} from '../projects/form-kit'
import { PartyLinksPanel } from '../parties/PartyLinksPanel'
import { RightOfEntryTab, ReviewsReportsTab } from './AcquisitionTabs'
import { DiaryTab, NotesTab, TeamTab } from './ParcelRecordTabs'
import { ParcelForm } from './ParcelForm'
import { ContactLogTab, EasementsTab } from './ParcelTabs'
import { OwnershipTab, ParcelLocationTab } from './OwnershipTabs'
import { EncumbrancesTab } from './TitleTabs'
import { TitleClosingTab } from './TitleClosingTab'
import { AppraisalTab } from './AppraisalTab'
import { NegotiationTab } from './NegotiationTab'
import { QuestionnaireTab } from './QuestionnaireTab'
import { CondemnationTab, PropertyTab, RelocationTab } from './LifecycleTabs'
import { acres, DeadlinePill, sqFtText, StateNote, StatusChip, zoneName } from './parcel-kit'

const NOTICE_MILESTONE = 'Notice to owner'

const TABS = [
  ['summary', 'Summary'], ['team', 'Team'], ['parties', 'Parties'], ['diary', 'Diary'], ['notes', 'Notes'], ['roe', 'Right of entry'], ['reviews', 'Reviews & reports'], ['interests', 'Interests'], ['milestones', 'Milestones & deadlines'], ['workflow', 'Workflow'], ['visits', 'Site visits & notice'],
  ['ownership', 'Ownership details'], ['parcellocation', 'Parcel location'], ['titleencumbrances', 'Title encumbrances'], ['titleclosing', 'Title and closing'], ['parcelappraisal', 'Appraisal'], ['parcelnegotiation', 'Negotiation'], ['parcelquestionnaire', 'Questionnaire'], ['parcelcondemnation', 'Condemnation'], ['parcelproperty', 'Property management & disposition'], ['parcelrelocation', 'Relocation'], ['contactlog', 'Contact log'], ['easements', 'Easements'],
] as const
type TabKey = (typeof TABS)[number][0]
/** Tabs with their own page permission: shown only to roles that can read that page. */
const PAGE_TABS: readonly string[] = ['ownership', 'parcellocation', 'contactlog', 'easements', 'titleencumbrances', 'titleclosing', 'parcelappraisal', 'parcelnegotiation', 'parcelquestionnaire', 'parcelcondemnation', 'parcelproperty', 'parcelrelocation']

/** Runs a save or delete, keeping the screen's error and busy state and refreshing what depends on the parcel. */
function useParcelActions(parcelId: string) {
  const { manager } = useAuth()
  const queryClient = useQueryClient()
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  return {
    api: parcelsApi(manager.api),
    manager,
    error,
    /** The server's complaints about single fields, by form-field name (for example `completedOn`). */
    fields,
    busy,
    clearError: () => setError(null),
    /** Resolves true when the action succeeded. */
    run: async (action: () => Promise<unknown>): Promise<boolean> => {
      setError(null)
      setFields({})
      setBusy(true)
      try {
        await action()
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: ['parcel', parcelId] }),
          queryClient.invalidateQueries({ queryKey: ['parcels'] }),
          queryClient.invalidateQueries({ queryKey: ['reminders'] }),
        ])
        return true
      } catch (e) {
        const fe = fieldErrorsOf(e)
        setFields(fe)
        setError(Object.keys(fe).length ? 'Please correct the highlighted fields.' : messageOf(e))
        return false
      } finally {
        setBusy(false)
      }
    },
  }
}

/** One parcel: its synopsis, interests, milestones and deadlines, workflow step and visits (ROW-008, 010, 071-074, 234, 237). */
export function ParcelDetailPage({ parcelId }: { parcelId: string }) {
  const { manager } = useAuth()
  const api = parcelsApi(manager.api)
  const queryClient = useQueryClient()
  const [tab, setTab] = useState<TabKey>('summary')
  const [panel, setPanel] = useState<'none' | 'edit' | 'split'>('none')
  const parcel = useQuery({ queryKey: ['parcel', parcelId], queryFn: () => api.get(parcelId) })

  if (parcel.isPending) return <LoadingRows />
  if (parcel.isError) {
    return <ErrorState title="This parcel could not be opened" description={parcel.error.message} retry={() => void parcel.refetch()} />
  }
  const p = parcel.data
  const live = p.state === 'Active'
  const canUpdate = manager.can('parcels', 'update') && live
  const canSplit = manager.can('parcels', 'create') && live

  return (
    <>
      <PageHeader
        title={`${p.projectNumber} · Parcel ${p.number}`}
        subtitle={p.address ?? undefined}
        crumbs={[{ label: 'Parcels', to: '/parcels' }, { label: `${p.projectNumber} · ${p.number}` }]}
        actions={(canUpdate || canSplit) && (
          <>
            {canSplit && <button type="button" className={buttonClassName('outline')} onClick={() => setPanel(panel === 'split' ? 'none' : 'split')}>Split</button>}
            {canUpdate && <button type="button" className={buttonClassName('default')} onClick={() => setPanel(panel === 'edit' ? 'none' : 'edit')}>Edit parcel</button>}
          </>
        )}
      />

      {!live && (
        <div role="status" className="mb-4 rounded-lg border border-warn/60 bg-warn/10 px-4 py-3 text-sm">
          <strong>This parcel was {p.state === 'Split' ? 'split' : 'combined'} and is kept on record only.</strong> It can no longer be changed.
        </div>
      )}

      {panel === 'edit' && canUpdate && (
        <section aria-label="Edit this parcel" className="mb-6 rounded-lg border bg-card p-4">
          <h2 className="mb-3 text-sm font-semibold">Edit parcel</h2>
          <ParcelForm
            parcel={p}
            submitLabel="Save parcel"
            onCancel={() => setPanel('none')}
            onSubmit={async (input: ParcelInput) => {
              await api.update(p.id, input)
              await Promise.all([queryClient.invalidateQueries({ queryKey: ['parcel', p.id] }), queryClient.invalidateQueries({ queryKey: ['parcels'] })])
              setPanel('none')
            }}
          />
        </section>
      )}
      {panel === 'split' && manager.can('parcels', 'create') && <SplitPanel parcel={p} onClose={() => setPanel('none')} />}

      <Tabs>
        <TabsList role="tablist" aria-label="Parcel sections">
          {TABS.filter(([key]) => !PAGE_TABS.includes(key) || manager.can(key)).map(([key, label]) => (
            <TabsTrigger key={key} role="tab" id={`tab-${key}`} aria-selected={tab === key} aria-controls={`panel-${key}`} active={tab === key} onClick={() => setTab(key)}>
              {label}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`}>
          {tab === 'summary' && <SummaryTab parcel={p} />}
          {tab === 'team' && <TeamTab parcelId={p.id} editable={live} />}
          {tab === 'diary' && <DiaryTab parcelId={p.id} editable={live} />}
          {tab === 'notes' && <NotesTab parcelId={p.id} editable={live} />}
          {tab === 'roe' && <RightOfEntryTab parcelId={p.id} editable={live} />}
          {tab === 'reviews' && <ReviewsReportsTab parcelId={p.id} editable={live} />}
          {tab === 'parties' && (manager.can('parties') ? <PartyLinksPanel target={{ kind: 'parcel', parcelId: p.id }} editable={live} /> : <p className="text-sm text-muted-foreground">Your role cannot see parties.</p>)}
          {tab === 'interests' && <InterestsTab parcel={p} editable={live} />}
          {tab === 'milestones' && <MilestonesTab parcel={p} editable={live} />}
          {tab === 'workflow' && <WorkflowTab parcel={p} editable={live} />}
          {tab === 'visits' && <VisitsTab parcel={p} editable={live} />}
          {tab === 'contactlog' && <ContactLogTab parcelId={p.id} editable={live} />}
          {tab === 'easements' && <EasementsTab parcelId={p.id} editable={live} />}
          {tab === 'ownership' && <OwnershipTab parcelId={p.id} editable={live} />}
          {tab === 'titleencumbrances' && <EncumbrancesTab parcelId={p.id} editable={live} />}
          {tab === 'titleclosing' && <TitleClosingTab parcelId={p.id} editable={live} />}
          {tab === 'parcelcondemnation' && <CondemnationTab parcel={p} editable={live} />}
          {tab === 'parcelproperty' && <PropertyTab parcel={p} editable={live} />}
          {tab === 'parcelrelocation' && <RelocationTab parcel={p} editable={live} />}
          {tab === 'parcelquestionnaire' && <QuestionnaireTab parcelId={p.id} editable={live} />}
          {tab === 'parcelnegotiation' && <NegotiationTab parcel={p} editable={live} />}
          {tab === 'parcelappraisal' && <AppraisalTab parcelId={p.id} editable={live} />}
          {tab === 'parcellocation' && <ParcelLocationTab parcel={p} />}
        </TabsContent>
      </Tabs>
    </>
  )
}

function Section({ title, children, className }: { title: string; children: ReactNode; className?: string }) {
  return (
    <section aria-label={title} className={`mb-6 ${className ?? ''}`}>
      <h2 className="mb-2 text-sm font-semibold">{title}</h2>
      {children}
    </section>
  )
}

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-sm">{children}</dd>
    </div>
  )
}

// ── Summary / synopsis (ROW-008) ────────────────────────────────────────

function SummaryTab({ parcel: p }: { parcel: Parcel }) {
  const { manager } = useAuth()
  const fields = useQuery({ queryKey: ['custom-fields'], queryFn: () => parcelsApi(manager.api).customFields() })
  const labelOf = (key: string) => fields.data?.find((f) => f.key === key)?.label ?? key
  const custom = Object.entries(p.customValues)

  return (
    <div className="parcel-synopsis">
      <div className="mb-3 flex justify-end print:hidden">
        <button type="button" className={buttonClassName('outline', 'sm')} onClick={() => window.print()}>
          <Printer size={14} aria-hidden /> Print synopsis
        </button>
      </div>
      <Section title="Parcel synopsis">
        <dl className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <Fact label="Project">{p.projectNumber} · {p.projectName}</Fact>
          <Fact label="Parcel number">{p.number} <StateNote state={p.state} />{p.isRemnant && <span className="text-xs text-muted-foreground"> (remnant)</span>}</Fact>
          <Fact label="Status"><StatusChip name={p.statusName} color={p.statusColor} /></Fact>
          <Fact label="Acquisition type">{ACQUISITION_LABEL[p.acquisitionType]}</Fact>
          <Fact label="Area">{sqFtText(p.areaSqFt)} ({acres(p.areaSqFt)} acres)</Fact>
          <Fact label="Property type">{p.propertyType ?? '—'}</Fact>
          <Fact label="Group">{p.groupName ?? '—'}</Fact>
          <Fact label="Parent tract">{p.parentTractNumber ?? '—'}</Fact>
          <Fact label="Tax parcel numbers">{p.taxParcelNumbers.length ? p.taxParcelNumbers.join(', ') : '—'}</Fact>
          <Fact label="Workflow step">{p.workflow.steps.find((s) => s.key === p.workflow.currentKey)?.label ?? p.workflow.currentKey}</Fact>
          <Fact label="Notice to owner">{p.noticeIssuedOn ? `${dateText(p.noticeIssuedOn)}${p.noticeMethod ? ` by ${NOTICE_LABEL[p.noticeMethod].toLowerCase()}` : ''}` : 'Not issued'}</Fact>
          <Fact label="Owner name">{p.ownerName ?? '—'}</Fact>
          <Fact label="CAD number">{p.cadNumber ?? '—'}</Fact>
          <Fact label="Acquisition interest type">{p.acquisitionInterestType ?? '—'}</Fact>
          <Fact label="Possession type">{p.possessionType ?? '—'}</Fact>
          <Fact label="Possession date">{p.possessionDate ? dateText(p.possessionDate) : '—'}</Fact>
          <Fact label="Proposed possession date">{p.proposedPossessionDate ? dateText(p.proposedPossessionDate) : '—'}</Fact>
          <Fact label="Segment">{p.segment ?? '—'}</Fact>
          <Fact label="Priority">{p.priority ?? '—'}</Fact>
          <Fact label="Parcel location">{p.parcelLocation ?? '—'}</Fact>
          <Fact label="Active parcel">{p.isActiveParcel ? 'Yes' : 'No'}</Fact>
          <Fact label="Dates counted in">{zoneName(p.timeZoneId)}</Fact>
          <Fact label="Last updated">{dateText(p.updatedAt)}</Fact>
        </dl>
        {p.legalDescription && <p className="mt-3 whitespace-pre-wrap text-sm"><span className="text-xs text-muted-foreground">Legal description: </span>{p.legalDescription}</p>}
        {p.sourceParcelIds.length > 0 && (
          <p className="mt-3 text-sm">
            Created from {p.sourceParcelIds.length === 1 ? 'a split of' : 'combining'}{' '}
            {p.sourceParcelIds.map((id, i) => (
              <span key={id}>{i > 0 && ', '}<Link to="/parcels/$parcelId" params={{ parcelId: id }} className="text-primary hover:underline">parcel record {i + 1}</Link></span>
            ))}.
          </p>
        )}
      </Section>
      {custom.length > 0 && (
        <Section title="Agency fields">
          <dl className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {custom.map(([k, v]) => <Fact key={k} label={labelOf(k)}>{v}</Fact>)}
          </dl>
        </Section>
      )}
      <Section title="Deadlines">
        <DeadlineTable parcel={p} />
      </Section>
      <Section title="Interests being acquired">
        {p.interests.length === 0 ? <p className="text-sm text-muted-foreground">None recorded.</p> : (
          <ul className="text-sm">{p.interests.map((i) => <li key={i.id}>{INTEREST_LABEL[i.type]} — {sqFtText(i.areaSqFt)}</li>)}</ul>
        )}
      </Section>
    </div>
  )
}

function DeadlineTable({ parcel: p }: { parcel: Parcel }) {
  if (p.deadlines.length === 0) return <p className="text-sm text-muted-foreground">No deadline rules apply.</p>
  return (
    <DataTable caption="Deadlines" headers={['Deadline', 'Starts when', 'Due', 'Status']}>
      {p.deadlines.map((d) => (
        <tr key={d.ruleId}>
          <td className="px-3 py-2">{d.name}</td>
          <td className="px-3 py-2">{d.triggerMilestone}{d.triggerDate ? ` (${dateText(d.triggerDate)})` : ''}</td>
          <td className="px-3 py-2">{dateText(d.dueOn)}</td>
          <td className="px-3 py-2">
            <DeadlinePill status={d.status} />
            {d.daysRemaining !== null && (d.status === 'Overdue' || d.status === 'DueSoon' || d.status === 'Pending') && (
              <span className="ml-2 text-xs text-muted-foreground">
                {d.daysRemaining < 0 ? `${-d.daysRemaining} day${d.daysRemaining === -1 ? '' : 's'} late` : d.daysRemaining === 0 ? 'due today' : `${d.daysRemaining} day${d.daysRemaining === 1 ? '' : 's'} left`}
              </span>
            )}
          </td>
        </tr>
      ))}
    </DataTable>
  )
}

// ── Interests (ROW-234) ─────────────────────────────────────────────────

const INTEREST_OPTIONS = INTEREST_TYPES.map((t) => ({ value: t, label: INTEREST_LABEL[t] }))

function InterestsTab({ parcel: p, editable }: { parcel: Parcel; editable: boolean }) {
  const a = useParcelActions(p.id)
  const [type, setType] = useState<InterestType | ''>('FeeTaking')
  const [area, setArea] = useState('')
  const [notes, setNotes] = useState('')
  const [fieldError, setFieldError] = useState<string | null>(null)
  const canAdd = editable && a.manager.can('parcels', 'create')
  const canDelete = editable && a.manager.can('parcels', 'delete')

  async function add(e: FormEvent) {
    e.preventDefault()
    setFieldError(null)
    if (!type) { setFieldError('Choose the interest type.'); return }
    if (area.trim() === '' || Number.isNaN(Number(area)) || Number(area) < 0) { setFieldError('Area must be zero or more square feet.'); return }
    if (await a.run(() => a.api.addInterest(p.id, type, Number(area), orNull(notes)))) { setArea(''); setNotes('') }
  }

  return (
    <>
      <Section title="Interests being acquired">
        {p.interests.length === 0 ? (
          <EmptyState title="No interests recorded" description="Add the fee, easements or other rights this parcel takes." />
        ) : (
          <DataTable caption="Interests" headers={canDelete ? ['Interest', 'Area', 'Acres', 'Notes', 'Actions'] : ['Interest', 'Area', 'Acres', 'Notes']}>
            {p.interests.map((i) => (
              <tr key={i.id}>
                <td className="px-3 py-2">{INTEREST_LABEL[i.type]}</td>
                <td className="px-3 py-2">{sqFtText(i.areaSqFt)}</td>
                <td className="px-3 py-2">{acres(i.areaSqFt)}</td>
                <td className="px-3 py-2">{i.notes ?? '—'}</td>
                {canDelete && (
                  <td className="px-3 py-2">
                    <button type="button" aria-label={`Remove ${INTEREST_LABEL[i.type]} interest`} disabled={a.busy} className={buttonClassName('ghost', 'sm')}
                      onClick={() => void a.run(() => a.api.deleteInterest(p.id, i.id))}>
                      <Trash2 size={14} aria-hidden />
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </DataTable>
        )}
      </Section>
      {canAdd && (
        <form onSubmit={add} noValidate aria-label="Add an interest" className="max-w-2xl space-y-3 rounded-lg border bg-card p-4">
          <h3 className="text-sm font-semibold">Add an interest</h3>
          <div className="grid gap-3 sm:grid-cols-2">
            <SelectField label="Interest type" required value={type} onChange={setType} options={INTEREST_OPTIONS} />
            <TextField label="Interest area (sq ft)" required type="number" inputMode="decimal" value={area} onChange={setArea} />
          </div>
          <TextField label="Interest notes" value={notes} onChange={setNotes} maxLength={500} />
          <FormError error={fieldError ?? a.error} />
          <button type="submit" disabled={a.busy} className={buttonClassName('default')}>Add interest</button>
        </form>
      )}
      {!canAdd && a.error && <FormError error={a.error} />}
    </>
  )
}

// ── Milestones and deadlines (ROW-010, 073) ─────────────────────────────

function MilestonesTab({ parcel: p, editable }: { parcel: Parcel; editable: boolean }) {
  const a = useParcelActions(p.id)
  const [editing, setEditing] = useState<string | null>(null)
  const [due, setDue] = useState('')
  const [done, setDone] = useState('')
  const [mNotes, setMNotes] = useState('')
  const [name, setName] = useState('')
  const [newDue, setNewDue] = useState('')
  const [fieldError, setFieldError] = useState<string | null>(null)
  const canUpdate = editable && a.manager.can('parcels', 'update')
  const canAdd = editable && a.manager.can('parcels', 'create')
  const canDelete = editable && a.manager.can('parcels', 'delete')

  async function addMilestone(e: FormEvent) {
    e.preventDefault()
    setFieldError(null)
    if (!name.trim()) { setFieldError('Milestone name is required.'); return }
    if (await a.run(() => a.api.addMilestone(p.id, name.trim(), orNull(newDue), null))) { setName(''); setNewDue('') }
  }

  async function saveEdit(e: FormEvent, id: string) {
    e.preventDefault()
    if (await a.run(() => a.api.updateMilestone(p.id, id, orNull(due), orNull(done), orNull(mNotes)))) setEditing(null)
  }

  return (
    <>
      <Section title="Milestones">
        <DataTable caption="Milestones" headers={['Milestone', 'Due', 'Completed', 'Notes', 'Actions']}>
          {p.milestones.map((m) => (
            <tr key={m.id}>
              {editing === m.id ? (
                <td colSpan={5} className="px-3 py-2">
                  <form onSubmit={(e) => void saveEdit(e, m.id)} noValidate aria-label={`Edit milestone ${m.name}`} className="flex flex-wrap items-end gap-3">
                    <span className="min-w-32 text-sm font-medium">{m.name}</span>
                    <TextField label="Due date" type="date" value={due} onChange={setDue} error={a.fields.dueOn} />
                    <TextField
                      label="Completed on" type="date" value={done} onChange={setDone} error={a.fields.completedOn}
                      disabled={m.name === NOTICE_MILESTONE}
                      hint={m.name === NOTICE_MILESTONE ? 'Set by recording the notice on the Site visits & notice tab.' : undefined}
                    />
                    <TextField label="Milestone notes" value={mNotes} onChange={setMNotes} maxLength={500} />
                    <button type="submit" disabled={a.busy} className={buttonClassName('default', 'sm')}>Save milestone</button>
                    <button type="button" className={buttonClassName('outline', 'sm')} onClick={() => setEditing(null)}>Cancel</button>
                    <div className="basis-full"><FormError error={a.error} /></div>
                  </form>
                </td>
              ) : (
                <>
                  <td className="px-3 py-2 font-medium">{m.name}{m.isOverdue && <> <Pill tone="warn">Overdue</Pill></>}</td>
                  <td className="px-3 py-2">{dateText(m.dueOn)}</td>
                  <td className="px-3 py-2">{m.completedOn ? dateText(m.completedOn) : 'Open'}</td>
                  <td className="px-3 py-2">{m.notes ?? '—'}</td>
                  <td className="px-3 py-2">
                    <span className="flex gap-1">
                      {canUpdate && (
                        <button type="button" aria-label={`Edit milestone ${m.name}`} className={buttonClassName('outline', 'sm')}
                          onClick={() => { setEditing(m.id); setDue(m.dueOn ?? ''); setDone(m.completedOn ?? ''); setMNotes(m.notes ?? '') }}>
                          Edit
                        </button>
                      )}
                      {canDelete && !m.isStandard && (
                        <button type="button" aria-label={`Remove milestone ${m.name}`} disabled={a.busy} className={buttonClassName('ghost', 'sm')}
                          onClick={() => void a.run(() => a.api.deleteMilestone(p.id, m.id))}>
                          <Trash2 size={14} aria-hidden />
                        </button>
                      )}
                    </span>
                  </td>
                </>
              )}
            </tr>
          ))}
        </DataTable>
      </Section>
      {!canAdd && <FormError error={a.error} />}
      {canAdd && (
        <form onSubmit={addMilestone} noValidate aria-label="Add a milestone" className="mb-6 max-w-2xl space-y-3 rounded-lg border bg-card p-4">
          <h3 className="text-sm font-semibold">Add a milestone</h3>
          <div className="grid gap-3 sm:grid-cols-2">
            <TextField label="Milestone name" required value={name} onChange={setName} maxLength={100} error={a.fields.name} />
            <TextField label="Due on" type="date" value={newDue} onChange={setNewDue} error={a.fields.dueOn} />
          </div>
          <FormError error={fieldError ?? (editing === null ? a.error : null)} />
          <button type="submit" disabled={a.busy} className={buttonClassName('default')}>Add milestone</button>
        </form>
      )}
      <Section title="Statutory and agency deadlines">
        <p className="mb-2 text-xs text-muted-foreground">
          Periods come from the agency&apos;s rules. Placeholder periods are marked in Configuration until the agency confirms them.
          Days are counted in {zoneName(p.timeZoneId)}.
        </p>
        <DeadlineTable parcel={p} />
      </Section>
    </>
  )
}

// ── Workflow and step gating (ROW-071, 072) ─────────────────────────────

function WorkflowTab({ parcel: p, editable }: { parcel: Parcel; editable: boolean }) {
  const a = useParcelActions(p.id)
  const w = p.workflow
  const canMove = editable && a.manager.can('parcels', 'update')
  const [target, setTarget] = useState('')
  const [reason, setReason] = useState('')
  const [fieldError, setFieldError] = useState<string | null>(null)
  const currentIdx = w.steps.findIndex((s) => s.key === w.currentKey)
  const next = w.steps.find((s) => s.key === w.nextKey)
  const nextHeld = !!next?.requiresAcquisitionGate && !w.acquisitionGateOpen

  const targetStep = w.steps.find((s) => s.key === target)
  const isOverride = !!targetStep && w.steps.indexOf(targetStep) !== currentIdx + 1
  const targetHeld = !!targetStep?.requiresAcquisitionGate && !w.acquisitionGateOpen && w.steps.indexOf(targetStep) > currentIdx

  async function move(e: FormEvent) {
    e.preventDefault()
    setFieldError(null)
    if (!targetStep) { setFieldError('Choose the step to move to.'); return }
    if (isOverride && !reason.trim()) { setFieldError('A reason is required when moving out of order.'); return }
    if (await a.run(() => a.api.advance(p.id, targetStep.key, isOverride ? reason.trim() : null))) { setTarget(''); setReason('') }
  }

  return (
    <>
      <Section title="Where this parcel is">
        <ol aria-label="Workflow steps" className="flex flex-wrap gap-2">
          {w.steps.map((s, i) => (
            <li key={s.key} aria-current={s.key === w.currentKey ? 'step' : undefined}
              className={`rounded-md border px-3 py-1.5 text-sm ${s.key === w.currentKey ? 'border-primary bg-primary/10 font-semibold' : i < currentIdx ? 'text-muted-foreground' : ''}`}>
              {s.label}{s.key === w.currentKey && <span className="sr-only"> (current step)</span>}
              {s.requiresAcquisitionGate && <span className="ml-1 text-[10px] uppercase text-muted-foreground">offer gate</span>}
            </li>
          ))}
        </ol>
        <p className="mt-2 text-xs text-muted-foreground">These steps are agency placeholders until each acquisition path is built out.</p>
      </Section>

      {next && nextHeld && (
        <div role="status" className="mb-4 rounded-lg border border-warn/60 bg-warn/10 px-4 py-3 text-sm">
          <strong>{next.label} is on hold.</strong> The project&apos;s NEPA decision and acquisition authorization (or an approved exception) must be on file first. This cannot be overridden.
        </div>
      )}

      {canMove && (
        <div className="mb-6 space-y-4">
          {next && (
            <div>
              <button type="button" disabled={a.busy || nextHeld} className={buttonClassName('default')}
                onClick={() => void a.run(() => a.api.advance(p.id, next.key, null))}>
                Advance to {next.label}
              </button>
            </div>
          )}
          {w.canOverride ? (
            <form onSubmit={move} noValidate aria-label="Move to another step" className="max-w-2xl space-y-3 rounded-lg border bg-card p-4">
              <h3 className="text-sm font-semibold">Move to another step</h3>
              <p className="text-xs text-muted-foreground">Skipping ahead, going back or repeating a step is an override. It needs a written reason and is recorded.</p>
              <SelectField label="Move to step" value={target} onChange={setTarget}
                options={w.steps.filter((s) => s.key !== w.currentKey).map((s) => ({ value: s.key, label: s.label }))} blankLabel="Choose a step" />
              {isOverride && <TextField label="Reason for the override" required value={reason} onChange={setReason} maxLength={1000} />}
              {targetHeld && <p role="alert" className="text-xs text-danger">That step is on hold until the project&apos;s acquisition gate opens.</p>}
              <FormError error={fieldError ?? a.error} />
              <button type="submit" disabled={a.busy || targetHeld} className={buttonClassName('outline')}>Move parcel</button>
            </form>
          ) : (
            <p className="text-xs text-muted-foreground">Only a project manager, an administrator or another role your agency has authorised can skip or repeat steps.</p>
          )}
          {!w.canOverride && <FormError error={a.error} />}
        </div>
      )}

      <Section title="Step history">
        {p.stepHistory.length === 0 ? <p className="text-sm text-muted-foreground">No moves yet.</p> : (
          <DataTable caption="Step history" headers={['When', 'From', 'To', 'Override', 'Reason']}>
            {p.stepHistory.map((h) => {
              const label = (k: string | null) => (k ? w.steps.find((s) => s.key === k)?.label ?? k : '—')
              return (
                <tr key={h.id}>
                  <td className="px-3 py-2">{dateText(h.at)}</td>
                  <td className="px-3 py-2">{label(h.fromStepKey)}</td>
                  <td className="px-3 py-2">{label(h.toStepKey)}</td>
                  <td className="px-3 py-2">{h.isOverride ? <Pill tone="warn">Override</Pill> : 'No'}</td>
                  <td className="px-3 py-2">{h.reason ?? '—'}</td>
                </tr>
              )
            })}
          </DataTable>
        )}
      </Section>
    </>
  )
}

// ── Site visits and notice to owner (ROW-237) ───────────────────────────

const NOTICE_OPTIONS = NOTICE_METHODS.map((m) => ({ value: m, label: NOTICE_LABEL[m] }))

function VisitsTab({ parcel: p, editable }: { parcel: Parcel; editable: boolean }) {
  const a = useParcelActions(p.id)
  const [issuedOn, setIssuedOn] = useState(p.noticeIssuedOn ?? '')
  const [method, setMethod] = useState<NoticeMethod | ''>(p.noticeMethod ?? '')
  const [noticeError, setNoticeError] = useState<string | null>(null)
  const [noticeSaved, setNoticeSaved] = useState(false)
  const [visitedOn, setVisitedOn] = useState('')
  const [notes, setNotes] = useState('')
  const [contact, setContact] = useState(false)
  const [visitError, setVisitError] = useState<string | null>(null)
  const canNotice = editable && a.manager.can('parcels', 'update')
  const canAdd = editable && a.manager.can('parcels', 'create')
  const canDelete = editable && a.manager.can('parcels', 'delete')

  async function saveNotice(e: FormEvent) {
    e.preventDefault()
    setNoticeError(null); setNoticeSaved(false)
    if (issuedOn && !method) { setNoticeError('Choose how the notice was delivered.'); return }
    if (await a.run(() => a.api.recordNotice(p.id, orNull(issuedOn), method || null))) setNoticeSaved(true)
  }

  async function addVisit(e: FormEvent) {
    e.preventDefault()
    setVisitError(null)
    if (!visitedOn) { setVisitError('Visit date is required.'); return }
    if (await a.run(() => a.api.addSiteVisit(p.id, visitedOn, orNull(notes), contact))) { setVisitedOn(''); setNotes(''); setContact(false) }
  }

  return (
    <>
      <Section title="Notice to owner">
        {canNotice ? (
          <form onSubmit={saveNotice} noValidate aria-label="Notice to owner" className="max-w-2xl space-y-3 rounded-lg border bg-card p-4">
            <div className="grid gap-3 sm:grid-cols-2">
              <TextField label="Notice issued on" type="date" value={issuedOn} onChange={setIssuedOn} />
              <SelectField label="Delivery method" value={method} onChange={setMethod} options={NOTICE_OPTIONS} blankLabel="Not recorded" />
            </div>
            <p className="text-xs text-muted-foreground">Recording the notice completes the &ldquo;Notice to owner&rdquo; milestone and starts the offer deadline.</p>
            <FormError error={noticeError} />
            {noticeSaved && <p role="status" className="text-sm">Notice saved.</p>}
            <button type="submit" disabled={a.busy} className={buttonClassName('default')}>Save notice</button>
          </form>
        ) : (
          <p className="text-sm">{p.noticeIssuedOn ? `Issued ${dateText(p.noticeIssuedOn)}${p.noticeMethod ? ` by ${NOTICE_LABEL[p.noticeMethod].toLowerCase()}` : ''}.` : 'Not issued yet.'}</p>
        )}
      </Section>

      <Section title="Site visits">
        {p.siteVisits.length === 0 ? (
          <EmptyState title="No site visits yet" description="Record each visit to the property and whether the owner was reached." />
        ) : (
          <DataTable caption="Site visits" headers={canDelete ? ['Date', 'Owner reached', 'Notes', 'Actions'] : ['Date', 'Owner reached', 'Notes']}>
            {p.siteVisits.map((v) => (
              <tr key={v.id}>
                <td className="px-3 py-2">{dateText(v.visitedOn)}</td>
                <td className="px-3 py-2">{v.ownerContactConfirmed ? 'Yes' : 'No'}</td>
                <td className="px-3 py-2">{v.notes ?? '—'}</td>
                {canDelete && (
                  <td className="px-3 py-2">
                    <button type="button" aria-label={`Remove visit of ${dateText(v.visitedOn)}`} disabled={a.busy} className={buttonClassName('ghost', 'sm')}
                      onClick={() => void a.run(() => a.api.deleteSiteVisit(p.id, v.id))}>
                      <Trash2 size={14} aria-hidden />
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </DataTable>
        )}
      </Section>
      {canAdd && (
        <form onSubmit={addVisit} noValidate aria-label="Record a site visit" className="max-w-2xl space-y-3 rounded-lg border bg-card p-4">
          <h3 className="text-sm font-semibold">Record a site visit</h3>
          <div className="grid gap-3 sm:grid-cols-2">
            <TextField label="Visit date" required type="date" value={visitedOn} onChange={setVisitedOn} />
            <CheckboxField label="Owner contact confirmed" checked={contact} onChange={setContact} />
          </div>
          <TextField label="Visit notes" value={notes} onChange={setNotes} maxLength={2000} />
          <FormError error={visitError ?? a.error} />
          <button type="submit" disabled={a.busy} className={buttonClassName('default')}>Record visit</button>
        </form>
      )}
    </>
  )
}

// ── Split (ROW-007) ─────────────────────────────────────────────────────

function SplitPanel({ parcel: p, onClose }: { parcel: Parcel; onClose: () => void }) {
  const { manager } = useAuth()
  const api = parcelsApi(manager.api)
  const queryClient = useQueryClient()
  const [parts, setParts] = useState<string[]>(['', ''])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<SplitResult | null>(null)

  const used = parts.reduce((sum, v) => sum + (Number(v) || 0), 0)
  const remnant = p.areaSqFt - used

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    const areas = parts.map((v) => Number(v))
    if (parts.some((v) => v.trim() === '' || Number.isNaN(Number(v)) || Number(v) <= 0)) { setError('Each part needs an area greater than zero.'); return }
    if (areas.reduce((a, b) => a + b, 0) > p.areaSqFt) { setError(`The parts add up to more than the parcel (${sqFtText(p.areaSqFt)}).`); return }
    setBusy(true)
    try {
      setResult(await api.split(p.id, areas.map((areaSqFt) => ({ areaSqFt }))))
      await queryClient.invalidateQueries({ queryKey: ['parcel', p.id] })
      await queryClient.invalidateQueries({ queryKey: ['parcels'] })
    } catch (err) {
      setError(messageOf(err))
    } finally {
      setBusy(false)
    }
  }

  if (result) {
    const created = [...result.parts, ...(result.remnant ? [result.remnant] : [])]
    return (
      <section aria-label="Split result" className="mb-6 rounded-lg border bg-card p-4">
        <h2 className="mb-2 text-sm font-semibold">Parcel {p.number} was split</h2>
        <p role="status" className="mb-2 text-sm">{created.length} new parcel{created.length === 1 ? '' : 's'} created. The original stays on record.</p>
        <ul className="mb-3 text-sm">
          {created.map((c) => (
            <li key={c.id}><Link to="/parcels/$parcelId" params={{ parcelId: c.id }} className="text-primary hover:underline">{c.number}</Link> — {sqFtText(c.areaSqFt)}{c.isRemnant ? ' (remnant)' : ''}</li>
          ))}
        </ul>
        <button type="button" className={buttonClassName('outline')} onClick={onClose}>Close</button>
      </section>
    )
  }

  return (
    <section aria-label="Split this parcel" className="mb-6 rounded-lg border bg-card p-4">
      <h2 className="mb-1 text-sm font-semibold">Split parcel {p.number}</h2>
      <p className="mb-3 text-xs text-muted-foreground">
        Each part becomes a new parcel (for example {p.number}-A). Any area not used stays with a remnant parcel. The original is kept on record and can no longer be changed.
      </p>
      <form onSubmit={submit} noValidate aria-label="Split parts" className="space-y-3">
        <div className="grid gap-3 sm:grid-cols-3">
          {parts.map((v, i) => (
            <TextField key={i} label={`Part ${i + 1} area (sq ft)`} type="number" inputMode="decimal" value={v} onChange={(x) => setParts((ps) => ps.map((q, j) => (j === i ? x : q)))} />
          ))}
        </div>
        <p className="text-sm" aria-live="polite">
          {remnant >= 0 ? <>Remnant: <strong>{sqFtText(remnant)}</strong></> : <span className="text-danger">The parts exceed the parcel by {sqFtText(-remnant)}.</span>}
        </p>
        <FormError error={error} />
        <div className="flex flex-wrap gap-2">
          <button type="button" className={buttonClassName('outline')} disabled={parts.length >= 25} onClick={() => setParts((ps) => [...ps, ''])}>Add a part</button>
          <button type="submit" disabled={busy} className={buttonClassName('default')}>{busy ? 'Splitting…' : 'Split parcel'}</button>
          <button type="button" disabled={busy} className={buttonClassName('outline')} onClick={onClose}>Cancel</button>
        </div>
      </form>
    </section>
  )
}
