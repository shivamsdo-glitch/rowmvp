import { useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Trash2 } from 'lucide-react'
import { questionnairesApi, type Questionnaire, type QuestionnaireInterest, type QuestionnaireQuestion } from '@/api/parcel-tabs'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { DataTable, dateText, fieldErrorsOf, FormError, messageOf, orNull, SelectField, TextField } from '../projects/form-kit'
import { AttachmentsPanel } from '../documents/AttachmentsPanel'
import { EntryBar, EntryMissing, useEntryMode, type EntryNav } from './entry-kit'
import { optionsWith, useLibraryValues } from '../library/library-kit'
import { Grid } from './NegotiationTab'

const BLANK = { type: '', due: '', sent: '' }
const BLANK_INTEREST = { name: '', phone: '', contact: '', email: '', address: '' }
const BLANK_QUESTION = { question: '', answer: '', notes: '' }

/** Questionnaire (Phase 1.5, ADR 0019): many relocation questionnaires per parcel, each with the ownership interests it covers and its questions and answers. */
export function QuestionnaireTab({ parcelId, editable, nav }: { parcelId: string; editable: boolean; nav?: EntryNav }) {
  const { manager } = useAuth()
  const api = questionnairesApi(manager.api)
  const queryClient = useQueryClient()
  const key = ['parcel-tabs', parcelId, 'questionnaires']
  const list = useQuery({ queryKey: key, queryFn: () => api.list(parcelId), enabled: manager.can('parcelquestionnaire') })
  const types = useLibraryValues('relocation_type')
  const [editing, setEditing] = useState<Questionnaire | null>(null)
  const [f, setF] = useState(BLANK)
  const [interests, setInterests] = useState<QuestionnaireInterest[]>([])
  const [questions, setQuestions] = useState<QuestionnaireQuestion[]>([])
  const [it, setIt] = useState(BLANK_INTEREST)
  const [qn, setQn] = useState(BLANK_QUESTION)
  const [local, setLocal] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const canAdd = editable && manager.can('parcelquestionnaire', 'create')
  const canEdit = editable && manager.can('parcelquestionnaire', 'update')
  const canRemove = editable && manager.can('parcelquestionnaire', 'delete')
  const { entryMode, missing } = useEntryMode(nav, list.data, editing, begin, reset)
  const set = <K extends keyof typeof BLANK>(k: K) => (v: (typeof BLANK)[K]) => setF((x) => ({ ...x, [k]: v }))
  const err = (k: string, m: string) => setLocal((l) => ({ ...l, [k]: m }))

  if (!manager.can('parcelquestionnaire')) return <p className="text-sm text-muted-foreground">Your role cannot see questionnaires.</p>
  if (list.isPending) return <LoadingRows rows={3} />
  if (list.isError) return <ErrorState title="The questionnaires could not be loaded" description={list.error.message} retry={() => void list.refetch()} />

  function reset() {
    setEditing(null); setF(BLANK); setInterests([]); setQuestions([]); setIt(BLANK_INTEREST); setQn(BLANK_QUESTION); setLocal({}); setError(null); setFields({})
  }

  function begin(q: Questionnaire) {
    setEditing(q)
    setF({ type: q.relocationType, due: q.dueByDate ?? '', sent: q.sentToOwnerDate ?? '' })
    setInterests(q.interests)
    setQuestions(q.questions)
    setIt(BLANK_INTEREST); setQn(BLANK_QUESTION); setLocal({}); setError(null); setFields({})
  }

  async function run(action: () => Promise<unknown>): Promise<boolean> {
    setError(null)
    setFields({})
    setBusy(true)
    try {
      await action()
      await queryClient.invalidateQueries({ queryKey: key })
      return true
    } catch (e) {
      const fe = fieldErrorsOf(e)
      setFields(fe)
      setError(Object.keys(fe).length ? 'Please correct the highlighted fields.' : messageOf(e))
      return false
    } finally {
      setBusy(false)
    }
  }

  function addInterest() {
    if (!it.name.trim()) return err('interest', 'Enter the name of the owner or interest holder.')
    setInterests([...interests, { name: it.name.trim(), phone: orNull(it.phone), contact: orNull(it.contact), email: orNull(it.email), address: orNull(it.address) }])
    setIt(BLANK_INTEREST); err('interest', '')
  }
  function addQuestion() {
    if (!qn.question.trim()) return err('question', 'Enter the question.')
    setQuestions([...questions, { question: qn.question.trim(), answer: orNull(qn.answer), notes: orNull(qn.notes) }])
    setQn(BLANK_QUESTION); err('question', '')
  }

  async function submit(ev: FormEvent) {
    ev.preventDefault()
    const errs: Record<string, string> = {}
    if (!f.type) errs.relocationType = 'Relocation type is required.'
    setLocal(errs)
    if (Object.keys(errs).length) return
    const body = {
      relocationType: f.type, dueByDate: orNull(f.due), sentToOwnerDate: orNull(f.sent),
      interests: interests.map(({ name, phone, contact, email, address }) => ({ name, phone, contact, email, address })),
      questions: questions.map(({ question, answer, notes }) => ({ question, answer, notes })),
    }
    if (await run(() => (editing ? api.update(parcelId, editing.id, body) : api.add(parcelId, body)))) { reset(); if (entryMode) nav?.back() }
  }
  const errors = { ...fields, ...local }

  return (
    <>
      {entryMode && nav && <EntryBar nav={nav} label="Back to questionnaires" what={editing ? `Questionnaire ${editing.number}` : 'Questionnaire'} />}
      {missing && nav && <EntryMissing nav={nav} label="Back to questionnaires" />}
      {!entryMode && <>
      <fieldset aria-label="Questionnaires" className="mb-6 rounded-lg border bg-card p-4">
        <legend className="px-1 text-sm font-semibold">Questionnaires</legend>
        {list.data.length === 0 ? (
          <EmptyState title="No questionnaires yet" description="Record each relocation questionnaire for this parcel." />
        ) : (
          <DataTable caption="Questionnaires" headers={['Questionnaire ID', 'Relocation type', 'Due by', 'Sent to owner', 'Questions', 'Actions']}>
            {list.data.map((q) => (
              <tr key={q.id}>
                <td className="px-3 py-2 font-medium">{q.number}</td>
                <td className="px-3 py-2">{q.relocationType}</td>
                <td className="px-3 py-2">{q.dueByDate ? dateText(q.dueByDate) : '—'}</td>
                <td className="px-3 py-2">{q.sentToOwnerDate ? dateText(q.sentToOwnerDate) : '—'}</td>
                <td className="px-3 py-2">{q.questions.length}</td>
                {(
                  <td className="px-3 py-2">
                    <span className="inline-flex gap-1">
                      <button type="button" aria-label={`Open questionnaire ${q.number}`} className={buttonClassName('outline', 'sm')} onClick={() => nav?.open(q.id)}>Open</button>
                      {canRemove && (
                        <button type="button" aria-label={`Remove questionnaire ${q.number}`} disabled={busy} className={buttonClassName('ghost', 'sm')}
                          onClick={() => void run(() => api.remove(parcelId, q.id)).then((ok) => { if (ok && editing?.id === q.id) reset() })}>
                          <Trash2 size={14} aria-hidden />
                        </button>
                      )}
                    </span>
                  </td>
                )}
              </tr>
            ))}
          </DataTable>
        )}
      </fieldset>
      </>}

      {((canAdd && !editing && !entryMode) || (editing && (canEdit || entryMode))) && (
        <form onSubmit={submit} noValidate aria-label={editing ? 'Change questionnaire' : 'Add a questionnaire'} className="max-w-5xl">
        <fieldset disabled={entryMode && !canEdit} className="m-0 min-w-0 space-y-3 border-0 p-0">
          {!entryMode && <h3 className="mb-3 text-sm font-semibold">{editing ? `Change ${editing.number}` : 'Add a questionnaire'}</h3>}
          <fieldset aria-label="Questionnaire" className="mb-4 rounded-lg border bg-card p-4">
            <legend className="px-1 text-sm font-semibold">Questionnaire</legend>
            <div className="grid gap-3 sm:grid-cols-3">
              <SelectField label="Relocation type" required value={f.type} onChange={set('type')} options={optionsWith(types, f.type)} blankLabel="Select one" error={errors.relocationType} />
              <TextField label="Due by" type="date" value={f.due} onChange={set('due')} error={errors.dueByDate} />
              <TextField label="Sent to owner" type="date" value={f.sent} onChange={set('sent')} error={errors.sentToOwnerDate} />
            </div>
          </fieldset>

          <Grid title="Ownership interest" caption="Ownership interest" headers={['Name', 'Phone', 'Contact', 'Email', 'Address']} rows={interests} canSave
            cells={(r) => [r.name, r.phone ?? '—', r.contact ?? '—', r.email ?? '—', r.address ?? '—']} rowLabel={(r) => `Remove ownership interest ${r.name}`}
            onRemove={(i) => setInterests(interests.filter((_, j) => j !== i))} addLabel="Add ownership interest" onAdd={addInterest} error={errors.interest}
            adding={<>
              <TextField label="Interest holder name" value={it.name} onChange={(v) => setIt({ ...it, name: v })} maxLength={200} />
              <TextField label="Interest holder phone" value={it.phone} onChange={(v) => setIt({ ...it, phone: v })} maxLength={100} />
              <TextField label="Interest holder contact" value={it.contact} onChange={(v) => setIt({ ...it, contact: v })} maxLength={200} />
              <TextField label="Interest holder email" value={it.email} onChange={(v) => setIt({ ...it, email: v })} maxLength={200} />
              <TextField label="Interest holder address" value={it.address} onChange={(v) => setIt({ ...it, address: v })} maxLength={500} />
            </>} />

          <Grid title="Questionnaire section" caption="Questionnaire section" headers={['Question', 'Answer', 'Notes']} rows={questions} canSave
            cells={(r) => [r.question, r.answer ?? '—', r.notes ?? '—']} rowLabel={(_, i) => `Remove question ${i + 1}`}
            onRemove={(i) => setQuestions(questions.filter((_, j) => j !== i))} addLabel="Add question" onAdd={addQuestion} error={errors.question}
            adding={<>
              <TextField label="Question" value={qn.question} onChange={(v) => setQn({ ...qn, question: v })} maxLength={1000} />
              <TextField label="Answer" value={qn.answer} onChange={(v) => setQn({ ...qn, answer: v })} maxLength={4000} />
              <TextField label="Question notes" value={qn.notes} onChange={(v) => setQn({ ...qn, notes: v })} maxLength={4000} />
            </>} />

          <FormError error={error} />
          <div className="flex gap-2">
            <button type="submit" disabled={busy} className={buttonClassName('default')}>{editing ? 'Save changes' : 'Add questionnaire'}</button>
            {entryMode && nav ? <button type="button" className={buttonClassName('outline')} onClick={() => { reset(); nav.back() }}>Cancel</button> : editing && <button type="button" className={buttonClassName('outline')} onClick={reset}>Cancel</button>}
          </div>
        </fieldset>
        </form>
      )}

      {editing && <div className="mt-6"><AttachmentsPanel entityType="ParcelQuestionnaire" entityId={editing.id} page="parcelquestionnaire" title={`Attachments for ${editing.number}`} /></div>}
    </>
  )
}
