import { useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { relocationsApi, type PaymentStatus, type Relocation, type RelocationPayment } from '@/api/parcel-tabs'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { CheckboxField, DataTable, dateText, FormError, messageOf, numOrNull, orNull, Pill, SelectField, TextField } from '../projects/form-kit'
import { optionsWith, useLibraryValues } from '../library/library-kit'

const dollars = (n: number) => n.toLocaleString('en-US', { style: 'currency', currency: 'USD' })
const tone = (s: PaymentStatus) => (s === 'Paid' || s === 'Approved' ? 'ok' : s === 'Rejected' ? 'neutral' : 'warn')

/** The parcel's relocation progress: how many displaced ownership interests have finished, and the Acquired rule (ADR 0026). */
export function RelocationProgressBanner({ parcelId, compact = false }: { parcelId: string; compact?: boolean }) {
  const { manager } = useAuth()
  const allowed = manager.can('parcelrelocation')
  const q = useQuery({ queryKey: ['parcel-tabs', parcelId, 'relocation-progress'], queryFn: () => relocationsApi(manager.api).progress(parcelId), enabled: allowed, retry: false })
  if (!allowed || !q.data || q.data.displacedInterests === 0) return null
  const p = q.data
  return (
    <div role="status" aria-label="Relocation progress" className="mb-4 rounded-md border bg-muted/40 p-3 text-sm">
      <p className="font-medium">{p.completedInterests} of {p.displacedInterests} displaced interests relocated</p>
      {!compact && (
        <p className="text-xs text-muted-foreground">
          {p.isAcquired ? 'All relocations are complete and the parcel is Acquired.' : 'The parcel moves to Acquired automatically when every displaced interest has a completed relocation.'}
        </p>
      )}
      {!p.acquiredStatusAvailable && !p.isAcquired && (
        <p className="mt-1 text-xs font-medium text-danger">There is no active &ldquo;Acquired&rdquo; parcel status, so the parcel will not move by itself. An administrator can add it in the Library.</p>
      )}
    </div>
  )
}

/** One relocation's payments: request one, sign it along the approval chain, and (Finance) mark it paid (ADR 0026). Amounts are typed. */
export function RelocationPaymentsPanel({ parcelId, relocation, editable }: { parcelId: string; relocation: Relocation; editable: boolean }) {
  const { manager } = useAuth()
  const api = relocationsApi(manager.api)
  const queryClient = useQueryClient()
  const types = useLibraryValues('relocation_payment_type')
  const [type, setType] = useState('')
  const [amount, setAmount] = useState('')
  const [advance, setAdvance] = useState(false)
  const [taxable, setTaxable] = useState(false)
  const [notes, setNotes] = useState('')
  const [decisionNote, setDecisionNote] = useState('')
  const [reference, setReference] = useState('')
  const [fields, setFields] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const payments = relocation.payments ?? []
  const canRequest = editable && manager.can('parcelrelocation', 'update') && !relocation.details.ineligibleForRelocation
  const canPay = manager.can('payments', 'update')

  async function run(action: () => Promise<unknown>): Promise<boolean> {
    setError(null); setBusy(true)
    try {
      await action()
      await queryClient.invalidateQueries({ queryKey: ['parcel-tabs', parcelId, 'relocations'] })
      return true
    } catch (e) {
      setError(messageOf(e))
      return false
    } finally {
      setBusy(false)
    }
  }

  async function request(ev: FormEvent) {
    ev.preventDefault()
    const errs: Record<string, string> = {}
    const value = numOrNull(amount)
    if (!type) errs.type = 'Choose the type of payment.'
    if (value === null || Number.isNaN(value) || value <= 0) errs.amount = 'Enter an amount above zero.'
    setFields(errs)
    if (Object.keys(errs).length) return
    if (await run(() => api.requestPayment(parcelId, relocation.id, { paymentType: type, amount: value!, isAdvance: advance, taxable, notes: orNull(notes) }))) {
      setType(''); setAmount(''); setAdvance(false); setTaxable(false); setNotes('')
    }
  }

  function signable(p: RelocationPayment) {
    if (p.status !== 'Requested' || !p.nextApprover) return { ok: false, why: '' }
    if (p.requestedByMe) return { ok: false, why: 'You requested this payment, so someone else has to sign it.' }
    if (!manager.hasRole(p.nextApprover) && !manager.hasRole('Admin')) return { ok: false, why: `Waiting for the ${p.nextApprover} to sign.` }
    return { ok: true, why: '' }
  }

  function decide(p: RelocationPayment, approve: boolean) {
    if (!approve && !decisionNote.trim()) { setError('Say why the payment is rejected.'); return }
    void run(() => api.decidePayment(parcelId, relocation.id, p.id, { approve, note: orNull(decisionNote) })).then((ok) => { if (ok) setDecisionNote('') })
  }

  return (
    <section aria-label={`Payments for relocation ${String(relocation.details.relocationNumber)}`} className="mt-6 rounded-lg border bg-card p-4">
      <h3 className="mb-1 text-sm font-semibold">Payments for relocation {String(relocation.details.relocationNumber)}</h3>
      <p className="mb-3 text-xs text-muted-foreground">The amount is entered by the agent. Each payment goes through the agency&rsquo;s approval chain, then Finance marks it paid.</p>
      {payments.length === 0 ? <p className="mb-3 text-sm text-muted-foreground">No payments requested yet.</p> : (
        <DataTable caption="Relocation payments" headers={['Type', 'Amount', 'Status', 'Signed so far', 'Next sign-off', 'Actions']}>
          {payments.map((p) => {
            const s = signable(p)
            return (
              <tr key={p.id}>
                <td className="px-3 py-2">{p.paymentType}{p.isAdvance ? ' (advance)' : ''}{p.taxable ? ' · taxable' : ''}</td>
                <td className="px-3 py-2">{dollars(p.amount)}</td>
                <td className="px-3 py-2"><Pill tone={tone(p.status)}>{p.status}</Pill>{p.decisionNote ? <span className="block text-xs text-muted-foreground">{p.decisionNote}</span> : null}{p.paymentReference ? <span className="block text-xs text-muted-foreground">Ref {p.paymentReference}</span> : null}</td>
                <td className="px-3 py-2">{p.approvals.length ? p.approvals.map((a) => `${a.role} ${dateText(a.approvedAt.slice(0, 10))}`).join(', ') : '—'}<span className="block text-xs text-muted-foreground">Chain: {p.requiredRoles.join(' → ')}</span></td>
                <td className="px-3 py-2">{p.status === 'Requested' ? p.nextApprover ?? '—' : '—'}</td>
                <td className="px-3 py-2">
                  <span className="inline-flex flex-wrap gap-1">
                    {p.status === 'Requested' && (
                      <>
                        <button type="button" aria-label={`Approve payment ${p.paymentType} ${dollars(p.amount)}`} disabled={busy || !s.ok} title={s.why || undefined} className={buttonClassName('outline', 'sm')} onClick={() => decide(p, true)}>Approve</button>
                        <button type="button" aria-label={`Reject payment ${p.paymentType} ${dollars(p.amount)}`} disabled={busy || !s.ok} title={s.why || undefined} className={buttonClassName('outline', 'sm')} onClick={() => decide(p, false)}>Reject</button>
                        {!s.ok && s.why && <span className="block w-full text-xs text-muted-foreground">{s.why}</span>}
                      </>
                    )}
                    {p.status === 'Approved' && (canPay ? (
                      <button type="button" aria-label={`Mark paid ${p.paymentType} ${dollars(p.amount)}`} disabled={busy} className={buttonClassName('default', 'sm')} onClick={() => void run(() => api.markPaid(parcelId, relocation.id, p.id, { reference: orNull(reference) }))}>Mark paid</button>
                    ) : <span className="text-xs text-muted-foreground">Waiting for Finance to pay.</span>)}
                  </span>
                </td>
              </tr>
            )
          })}
        </DataTable>
      )}
      {payments.some((p) => p.status === 'Requested') && (
        <div className="mt-3 max-w-md"><TextField label="Decision note" value={decisionNote} onChange={setDecisionNote} maxLength={1000} hint="Needed to reject a payment." /></div>
      )}
      {canPay && payments.some((p) => p.status === 'Approved') && (
        <div className="mt-3 max-w-md"><TextField label="Payment reference" value={reference} onChange={setReference} maxLength={200} hint="Warrant or EFT number." /></div>
      )}
      {canRequest && (
        <form onSubmit={request} noValidate aria-label="Request a payment" className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          <SelectField label="Payment type" required value={type} onChange={setType} options={optionsWith(types, type)} blankLabel="Select one" error={fields.type} />
          <TextField label="Payment amount" required type="number" inputMode="decimal" value={amount} onChange={setAmount} error={fields.amount} />
          <TextField label="Payment notes" value={notes} onChange={setNotes} maxLength={2000} />
          <CheckboxField label="Advance payment (hardship)" checked={advance} onChange={setAdvance} />
          <CheckboxField label="Taxable (1099)" checked={taxable} onChange={setTaxable} />
          <div className="flex items-end"><button type="submit" disabled={busy} className={buttonClassName('default')}>Request payment</button></div>
        </form>
      )}
      <FormError error={error} />
    </section>
  )
}
